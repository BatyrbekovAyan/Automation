using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Real purchases-unity 9.5.x backend — device-only. RevenueCat's own docs state running the
/// Purchases SDK is unsupported inside the Unity Editor, so <see cref="Initialize"/> no-ops there
/// (BillingService already selects FakeBillingBackend for the Editor — this guard is a second line
/// of defense so this class can never touch the native wrapper if ever constructed by mistake). A
/// missing/empty API key (Task 0's store keys don't exist yet) also no-ops rather than throwing.
///
/// <c>Purchases</c> is a MonoBehaviour, so this plain class hosts one on a lazily-created, persistent
/// GameObject and drives it via the SDK's "programmatic configuration" path (<c>useRuntimeSetup</c>).
/// Configure() is deferred one frame past AddComponent — Purchases.Start() (which assigns the native
/// wrapper Configure() needs) does not run synchronously with AddComponent, so calling Configure()
/// immediately would NRE on a null wrapper.
/// </summary>
public class RevenueCatBackend : IBillingBackend
{
    private const string HostName = "RevenueCat_Purchases";

    private Purchases _purchases;
    private Action<PlanTier> _onEntitlementChanged;
    private string _apiKey;

    // True only after Configure() has actually run (FinishConfigure, one frame after
    // AddComponent). Purchase/RestorePurchases must refuse during that window rather than call
    // into a Purchases component whose native wrapper hasn't been Setup() yet — _purchases itself
    // is already non-null by then (assigned synchronously in Initialize), so a null check alone
    // would let a call straight through.
    private bool _configured;

    public string AppUserId { get; private set; } = "";
    public PlanTier PurchasedTier { get; private set; } = PlanTier.None;

    public void Initialize(string apiKey, Action<PlanTier> onEntitlementChanged)
    {
        _onEntitlementChanged = onEntitlementChanged;

#if UNITY_EDITOR
        Debug.LogWarning("[RevenueCatBackend] Unsupported in the Unity Editor — staying uninitialized.");
#else
        if (_purchases != null) return;   // Initialize is not meant to be called twice; guard anyway

        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogWarning("[RevenueCatBackend] No RevenueCat API key for this platform yet (Task 0) — staying uninitialized.");
            return;
        }

        _apiKey = apiKey;

        var host = new GameObject(HostName);
        UnityEngine.Object.DontDestroyOnLoad(host);

        var listener = host.AddComponent<EntitlementListener>();
        listener.Owner = this;

        _purchases = host.AddComponent<Purchases>();
        _purchases.useRuntimeSetup = true;   // suppress the SDK's own Start()-time auto-configure
        _purchases.listener = listener;
#endif
    }

    public void Purchase(string sku, Action<bool, string> done)
    {
        if (!_configured) { done?.Invoke(false, "not_initialized"); return; }

        // The top-up SKU is a one-time consumable, not a subscription — everything else in
        // PlanCatalog is a sub.*.month/year SKU. Mistyping this as "subs" would make the store
        // treat a one-time purchase as a recurring subscription.
        string type = sku == PlanCatalog.SkuTopUp ? "inapp" : "subs";

        _purchases.PurchaseProduct(sku, result =>
        {
            if (result.Error != null) { done?.Invoke(false, result.Error.Message); return; }
            if (result.UserCancelled) { done?.Invoke(false, "cancelled"); return; }
            if (result.CustomerInfo != null) Apply(result.CustomerInfo);
            done?.Invoke(true, null);
        }, type: type);
    }

    public void RestorePurchases(Action<bool> done)
    {
        if (!_configured) { done?.Invoke(false); return; }

        _purchases.RestorePurchases((info, error) =>
        {
            if (error != null)
            {
                Debug.LogWarning($"[RevenueCatBackend] RestorePurchases error: {error.Message}");
                done?.Invoke(false);
                return;
            }
            if (info == null)
            {
                Debug.LogWarning("[RevenueCatBackend] RestorePurchases returned no CustomerInfo and no error.");
                done?.Invoke(false);
                return;
            }
            Apply(info);
            done?.Invoke(true);
        });
    }

    public void FetchPrices(Action<System.Collections.Generic.Dictionary<string, string>> done)
    {
        if (!_configured) { done?.Invoke(null); return; }

        // One call covers iOS fully — StoreKit ignores the subs/inapp type filter, so the
        // consumable top-up rides along with the six subscriptions. Android's Play Billing
        // DOES split by type and would miss the top-up here; add a second "inapp" call when
        // the Android track ships (deferred with the rest of Android, owner 2026-08-31).
        _purchases.GetProducts(PlanCatalog.AllSkus(), (products, error) =>
        {
            if (error != null)
            {
                Debug.LogWarning($"[RevenueCatBackend] GetProducts error: {error.Message}");
                done?.Invoke(null);
                return;
            }
            if (products == null || products.Count == 0) { done?.Invoke(null); return; }

            var prices = new System.Collections.Generic.Dictionary<string, string>(products.Count);
            foreach (var product in products)
                if (product != null && !string.IsNullOrEmpty(product.Identifier)
                    && !string.IsNullOrEmpty(product.PriceString))
                    prices[product.Identifier] = product.PriceString;
            done?.Invoke(prices);
        });
    }

    private void FinishConfigure()
    {
        try
        {
            var config = Purchases.PurchasesConfiguration.Builder.Init(_apiKey)
                .SetStoreKitVersion(Purchases.StoreKitVersion.Default)
                .SetEntitlementVerificationMode(Purchases.EntitlementVerificationMode.Informational)
                .SetShouldShowInAppMessagesAutomatically(true)
                .Build();
            _purchases.Configure(config);
            _configured = true;

            AppUserId = _purchases.GetAppUserId();

            _purchases.GetCustomerInfo((info, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"[RevenueCatBackend] GetCustomerInfo error: {error.Message}");
                    _onEntitlementChanged?.Invoke(PurchasedTier);   // resolve window closes even on error
                    return;
                }
                if (info == null)
                {
                    Debug.LogWarning("[RevenueCatBackend] GetCustomerInfo returned no CustomerInfo and no error.");
                    _onEntitlementChanged?.Invoke(PurchasedTier);
                    return;
                }
                Apply(info);
            });
        }
        catch (Exception e)
        {
            Debug.LogError($"[RevenueCatBackend] FinishConfigure threw — billing stays uninitialized: {e}");

            // Deliberately asymmetric vs. the GetCustomerInfo error/null branches above (which DO
            // fire _onEntitlementChanged to close BillingService's resolve window): those mean "the
            // SDK is healthy, this one network round-trip failed" — a common, recoverable case, and
            // the SDK will keep retrying on its own. A throw HERE means Configure() itself — the
            // SDK's own entry point — is broken on this device, so we know NOTHING trustworthy about
            // this user's entitlement. Leaving BillingService.EntitlementsKnown permanently false is
            // fail-open-as-grace: on a device with a broken SDK integration it can only ever
            // OVER-grant a stuck client-side Trial gate, never lock out a real subscriber — and
            // server-side dialog metering (Task 9) is the actual enforcement boundary regardless of
            // what this client-side gate believes.
        }
    }

    // MAX active-entitlement tier by enum order, mirroring FakeBillingBackend.SetActiveEntitlements
    // (kept as a small self-contained duplicate rather than a cross-reference — a real backend
    // depending on the test double would be backwards).
    private void Apply(Purchases.CustomerInfo info)
    {
        // Re-capture on every resolution, not just the first — but only when non-empty: a
        // transient empty read (e.g. mid-flight before the native side has one to give) must not
        // roll back a previously-good id to BillingService's device-id fallback.
        var freshAppUserId = _purchases?.GetAppUserId();
        if (!string.IsNullOrEmpty(freshAppUserId)) AppUserId = freshAppUserId;

        PlanTier max = PlanTier.None;
        foreach (var entitlementId in info.Entitlements.Active.Keys)
        {
            var tier = PlanCatalog.FromEntitlementId(entitlementId);
            if (tier > max) max = tier;
        }
        PurchasedTier = max;
        _onEntitlementChanged?.Invoke(PurchasedTier);
    }

    // Nested so it can reach FinishConfigure()/Apply() directly; Purchases.listener only accepts a
    // Purchases.UpdatedCustomerInfoListener component, hence the MonoBehaviour subclass.
    private class EntitlementListener : Purchases.UpdatedCustomerInfoListener
    {
        public RevenueCatBackend Owner;

        private void Start() => StartCoroutine(ConfigureNextFrame());

        private IEnumerator ConfigureNextFrame()
        {
            yield return null;
            Owner?.FinishConfigure();
        }

        public override void CustomerInfoReceived(Purchases.CustomerInfo customerInfo) => Owner?.Apply(customerInfo);
    }
}

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

    // Bare product id of the subscription behind the highest active entitlement, kept so a plan
    // change on Google Play is issued as a REPLACEMENT (see PurchaseParamsPolicy.Resolve).
    private string _activeSubscriptionProductId;

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
        if (!_configured) { done?.Invoke(false, BillingFailure.NotInitialized); return; }

        // Type (subs / inapp for the one-time top-up) and — on Google Play only — the owned
        // subscription this purchase replaces, both decided by the pure, test-pinned policy.
        PurchaseParams p = PurchaseParamsPolicy.Resolve(_activeSubscriptionProductId, sku,
            Application.platform == RuntimePlatform.Android);

        Purchases.MakePurchaseFunc callback = result =>
        {
            // Cancel BEFORE error: the Android wrapper reports a dismissed Play sheet as both,
            // and the paywall must stay silent on a deliberate cancel.
            if (result.UserCancelled) { done?.Invoke(false, BillingFailure.UserCancelled); return; }
            if (result.Error != null) { done?.Invoke(false, PurchaseParamsPolicy.FailureReason(false, result.Error.Message)); return; }
            if (result.CustomerInfo != null) Apply(result.CustomerInfo);
            done?.Invoke(true, null);
        };

        if (p.Replace)
            _purchases.PurchaseProduct(sku, callback, type: p.Type, oldSku: p.OldSku,
                prorationMode: Purchases.ProrationMode.ImmediateWithTimeProration);
        else
            _purchases.PurchaseProduct(sku, callback, type: p.Type);
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

        var prices = new System.Collections.Generic.Dictionary<string, string>(PlanCatalog.AllSkus().Length);
#if UNITY_ANDROID
        // Google Play Billing answers a product query per TYPE: a «subs» query never returns the
        // consumable top-up and an «inapp» query never returns a subscription, so the seven
        // sellable SKUs take two round-trips here. Each leg is best-effort — a failed leg logs
        // and the other leg's prices still reach the paywall; the KZT fallback covers the rest.
        GetProductsInto(PlanCatalog.SubscriptionSkus(), "subs", prices, () =>
            GetProductsInto(new[] { PlanCatalog.SkuTopUp }, "inapp", prices, () =>
                done?.Invoke(prices.Count > 0 ? prices : null)));
#else
        // One call covers iOS fully — StoreKit ignores the subs/inapp type filter, so the
        // consumable top-up rides along with the six subscriptions.
        GetProductsInto(PlanCatalog.AllSkus(), "subs", prices, () =>
            done?.Invoke(prices.Count > 0 ? prices : null));
#endif
    }

    /// <summary>
    /// One store product query; every product that came back with an identifier and a
    /// formatted price lands in <paramref name="into"/>, then <paramref name="next"/> runs —
    /// on error too, so a chained leg still executes and the caller always completes.
    /// </summary>
    private void GetProductsInto(string[] skus, string type,
                                 System.Collections.Generic.Dictionary<string, string> into, Action next)
    {
        _purchases.GetProducts(skus, (products, error) =>
        {
            if (error != null)
                Debug.LogWarning($"[RevenueCatBackend] GetProducts({type}) error: {error.Message}");
            else if (products != null)
                foreach (var product in products)
                {
                    if (product == null || string.IsNullOrEmpty(product.Identifier)
                        || string.IsNullOrEmpty(product.PriceString)) continue;
                    // Play ids carry «:basePlanId»; the paywall keys by the bare id. First hit
                    // wins so a product with several base plans cannot overwrite itself.
                    string key = StoreProductKey.Normalize(product.Identifier);
                    if (!into.ContainsKey(key)) into[key] = product.PriceString;
                }
            next();
        }, type: type);
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
        string activeProduct = null;
        foreach (var kv in info.Entitlements.Active)
        {
            var tier = PlanCatalog.FromEntitlementId(kv.Key);
            if (tier > max)
            {
                max = tier;
                activeProduct = kv.Value != null ? kv.Value.ProductIdentifier : null;
            }
        }
        _activeSubscriptionProductId = StoreProductKey.Normalize(activeProduct);
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

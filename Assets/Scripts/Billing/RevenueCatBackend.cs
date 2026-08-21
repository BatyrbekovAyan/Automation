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
        if (_purchases == null) { done?.Invoke(false, "not_initialized"); return; }

        _purchases.PurchaseProduct(sku, result =>
        {
            if (result.Error != null) { done?.Invoke(false, result.Error.Message); return; }
            if (result.UserCancelled) { done?.Invoke(false, "cancelled"); return; }
            if (result.CustomerInfo != null) Apply(result.CustomerInfo);
            done?.Invoke(true, null);
        });
    }

    public void RestorePurchases(Action<bool> done)
    {
        if (_purchases == null) { done?.Invoke(false); return; }

        _purchases.RestorePurchases((info, error) =>
        {
            if (error != null || info == null) { done?.Invoke(false); return; }
            Apply(info);
            done?.Invoke(true);
        });
    }

    private void FinishConfigure()
    {
        var config = Purchases.PurchasesConfiguration.Builder.Init(_apiKey).Build();
        _purchases.Configure(config);

        AppUserId = _purchases.GetAppUserId();

        _purchases.GetCustomerInfo((info, error) =>
        {
            if (error == null && info != null) Apply(info);
        });
    }

    // MAX active-entitlement tier by enum order, mirroring FakeBillingBackend.SetActiveEntitlements
    // (kept as a small self-contained duplicate rather than a cross-reference — a real backend
    // depending on the test double would be backwards).
    private void Apply(Purchases.CustomerInfo info)
    {
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

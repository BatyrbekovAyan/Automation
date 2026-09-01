using System;

/// <summary>
/// In-memory <see cref="IBillingBackend"/> for the Editor and EditMode tests — RevenueCat's own SDK
/// is unsupported inside the Unity Editor (see RevenueCatBackend), so <see cref="BillingService"/>
/// selects this whenever it can't select the real backend. Every member is directly settable so
/// tests can drive it without touching BillingService's own selection logic.
/// </summary>
public class FakeBillingBackend : IBillingBackend
{
    public string AppUserId { get; set; } = "";
    public PlanTier PurchasedTier { get; set; } = PlanTier.None;

    // Recorded for test assertions — never read by BillingService itself.
    public bool Initialized { get; private set; }
    public string LastPurchaseSku { get; private set; }
    public bool RestoreCalled { get; private set; }

    // Test-controlled outcomes for Purchase/RestorePurchases.
    public bool PurchaseSucceeds = true;
    public string PurchaseFailureReason = "fake_declined";
    public bool RestoreSucceeds = true;

    private Action<PlanTier> _onEntitlementChanged;

    public void Initialize(string apiKey, Action<PlanTier> onEntitlementChanged)
    {
        Initialized = true;
        _onEntitlementChanged = onEntitlementChanged;
    }

    // Test helper: simulate the entitlement set RevenueCat would report as active, mapped to the
    // MAX tier by enum order (mirrors RevenueCatBackend's real CustomerInfo.Entitlements.Active
    // parsing) — lets tests pin the mapping rule without a live SDK.
    public void SetActiveEntitlements(params string[] entitlementIds)
    {
        PlanTier max = PlanTier.None;
        foreach (var id in entitlementIds)
        {
            var tier = PlanCatalog.FromEntitlementId(id);
            if (tier > max) max = tier;
        }
        PurchasedTier = max;
        _onEntitlementChanged?.Invoke(PurchasedTier);
    }

    public void Purchase(string sku, Action<bool, string> done)
    {
        LastPurchaseSku = sku;
        done?.Invoke(PurchaseSucceeds, PurchaseSucceeds ? null : PurchaseFailureReason);
    }

    public void RestorePurchases(Action<bool> done)
    {
        RestoreCalled = true;
        done?.Invoke(RestoreSucceeds);
    }

    // Test-controlled localized price map; null (the default) = store prices unavailable,
    // which is also the honest Editor behavior — the paywall then renders the KZT fallback.
    public System.Collections.Generic.Dictionary<string, string> LocalizedPrices;
    public bool FetchPricesCalled { get; private set; }

    public void FetchPrices(Action<System.Collections.Generic.Dictionary<string, string>> done)
    {
        FetchPricesCalled = true;
        done?.Invoke(LocalizedPrices);
    }
}

using System;
using UnityEngine;

/// <summary>
/// Static facade over whichever <see cref="IBillingBackend"/> is active. Owns backend selection
/// (real RevenueCat SDK on device once a key exists in secrets.json; <see cref="FakeBillingBackend"/>
/// in the Editor or whenever Task 0's store keys haven't landed yet) and re-exposes the pieces
/// <see cref="EntitlementGate"/> and <see cref="BillingIdentity"/> need. Call <see cref="Initialize"/>
/// once, early (see Manager.Start()); every member is safe to read before that (degrades to
/// None/device-id rather than throwing).
/// </summary>
public static class BillingService
{
    // House pattern (TrialLedger/EntitlementGate/BillingIdentity): default seam encodes the real
    // policy, tests reassign it directly to inject a pre-configured FakeBillingBackend instance.
    internal static Func<IBillingBackend> BackendFactory = DefaultBackendFactory;

    private static IBillingBackend _backend;
    private static bool _initialized;

    internal static void ResetSeamsForTests()
    {
        BackendFactory = DefaultBackendFactory;
        _backend = null;
        _initialized = false;
        OnEntitlementChanged = null;   // a leaked subscriber from one test would fire in every test after it
    }

    /// <summary>Fires whenever the active backend reports a new active-entitlement set (purchase, restore, push update).</summary>
    public static event Action<PlanTier> OnEntitlementChanged;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _backend = BackendFactory();
        _backend.Initialize(ResolveApiKey(), tier => OnEntitlementChanged?.Invoke(tier));

        EntitlementGate.PurchasedTierSource = () => PurchasedTier;
    }

    /// <summary>MAX active entitlement tier from the current backend; None if uninitialized/backend has nothing active.</summary>
    public static PlanTier PurchasedTier => _backend?.PurchasedTier ?? PlanTier.None;

    /// <summary>
    /// RevenueCat's anonymous app user id once a real backend is configured; falls back to
    /// SystemInfo.deviceUniqueIdentifier whenever no backend is active or it reports nothing yet
    /// (Editor, keyless secrets.json, Initialize() not yet called, or first-launch race).
    /// </summary>
    public static string AppUserId =>
        !string.IsNullOrEmpty(_backend?.AppUserId) ? _backend.AppUserId : SystemInfo.deviceUniqueIdentifier;

    public static void Purchase(string sku, Action<bool, string> done)
    {
        if (_backend == null) { done?.Invoke(false, "not_initialized"); return; }
        _backend.Purchase(sku, done);
    }

    public static void RestorePurchases(Action<bool> done)
    {
        if (_backend == null) { done?.Invoke(false); return; }
        _backend.RestorePurchases(done);
    }

    private static IBillingBackend DefaultBackendFactory()
    {
#if UNITY_EDITOR
        return new FakeBillingBackend();
#else
        return string.IsNullOrEmpty(ResolveApiKey()) ? (IBillingBackend)new FakeBillingBackend() : new RevenueCatBackend();
#endif
    }

    private static string ResolveApiKey()
    {
        var revenueCat = Secrets.Data?.revenueCat;
        if (revenueCat == null) return "";
#if UNITY_IOS
        return revenueCat.iosKey ?? "";
#elif UNITY_ANDROID
        return revenueCat.androidKey ?? "";
#else
        return "";   // desktop/Editor builds have no store to key against
#endif
    }
}

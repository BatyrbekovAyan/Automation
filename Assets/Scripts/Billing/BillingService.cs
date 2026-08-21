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
        EntitlementsKnown = true;
        OnEntitlementChanged = null;   // a leaked subscriber from one test would fire in every test after it
        EntitlementGate.PurchasedTierSource = () => PlanTier.None;   // Initialize() overwrites this — restore EntitlementGate's own default too
    }

    /// <summary>Fires whenever the active backend reports a new active-entitlement set (purchase, restore, push update).</summary>
    public static event Action<PlanTier> OnEntitlementChanged;

    /// <summary>
    /// False only during the brief window between a keyed real-backend Initialize() and its first
    /// CustomerInfo round-trip landing (success OR error) — Fake/keyless backends are synchronous
    /// by construction and are "known" immediately. EntitlementGate.CurrentTier reads this to grant
    /// a grace window rather than paywall a paying customer before we've heard back from RevenueCat.
    /// </summary>
    public static bool EntitlementsKnown { get; private set; } = true;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _backend = BackendFactory();
        EntitlementsKnown = !(_backend is RevenueCatBackend);

        _backend.Initialize(ResolveApiKey(), tier =>
        {
            EntitlementsKnown = true;
            OnEntitlementChanged?.Invoke(tier);
        });

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
        // Non-mobile build targets only (Standalone, WebGL, ...) — no store to key against.
        // NOT "Editor": UNITY_IOS/UNITY_ANDROID are ALSO defined when the Editor is running with
        // that platform selected as the active build target, so this branch is never what keeps
        // the Editor on FakeBillingBackend — DefaultBackendFactory's own #if UNITY_EDITOR does that.
        return "";
#endif
    }
}

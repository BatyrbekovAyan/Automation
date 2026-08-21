using System;

/// <summary>
/// Seam between <see cref="BillingService"/> and whichever purchase backend is active: the real
/// RevenueCat SDK on device, or <see cref="FakeBillingBackend"/> in the Editor / EditMode tests
/// (RevenueCat's own SDK is unsupported inside the Unity Editor — see RevenueCatBackend). This is
/// the full surface either implementation must provide; BillingService owns backend selection and
/// re-exposes these members as its own static facade.
/// </summary>
public interface IBillingBackend
{
    /// <summary>
    /// Configure the backend for this platform's key (may be empty — a real backend must no-op
    /// gracefully rather than throw). <paramref name="onEntitlementChanged"/> fires whenever the
    /// backend observes a new active-entitlement set (a fresh purchase, a restore, or a push
    /// update from the store) — NOT on every read of <see cref="PurchasedTier"/>.
    /// </summary>
    void Initialize(string apiKey, Action<PlanTier> onEntitlementChanged);

    /// <summary>The app user id this backend is tracking purchases under (may be empty before/without init).</summary>
    string AppUserId { get; }

    /// <summary>The MAX tier among this user's currently active entitlements (None if none/uninitialized).</summary>
    PlanTier PurchasedTier { get; }

    /// <summary>Buy <paramref name="sku"/>. <paramref name="done"/> fires with (success, failureReason); failureReason is null on success.</summary>
    void Purchase(string sku, Action<bool, string> done);

    /// <summary>Restore previously-bought entitlements for the current store account. <paramref name="done"/> fires with success.</summary>
    void RestorePurchases(Action<bool> done);
}

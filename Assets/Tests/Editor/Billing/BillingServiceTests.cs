using NUnit.Framework;
using UnityEngine;

public class BillingServiceTests
{
    [SetUp]
    public void Seams()
    {
        BillingService.ResetSeamsForTests();
        EntitlementGate.ResetSeamsForTests();
        BillingIdentity.ResetSeamsForTests();
    }

    [TearDown]
    public void Reset()
    {
        BillingService.ResetSeamsForTests();
        EntitlementGate.ResetSeamsForTests();
        BillingIdentity.ResetSeamsForTests();
    }

    // --- Entitlement mapping (FakeBillingBackend, no BillingService involved) ------------------

    [Test] public void Entitlement_mapping_picks_max_active_tier()
    {
        var fake = new FakeBillingBackend();
        fake.SetActiveEntitlements("tier_start", "tier_network");

        Assert.AreEqual(PlanTier.Network, fake.PurchasedTier);
    }

    [Test] public void Entitlement_mapping_single_entitlement()
    {
        var fake = new FakeBillingBackend();
        fake.SetActiveEntitlements("tier_business");

        Assert.AreEqual(PlanTier.Business, fake.PurchasedTier);
    }

    [Test] public void Entitlement_mapping_no_active_entitlements_is_none()
    {
        var fake = new FakeBillingBackend();
        fake.SetActiveEntitlements();

        Assert.AreEqual(PlanTier.None, fake.PurchasedTier);
    }

    [Test] public void Entitlement_mapping_unknown_id_is_none()
    {
        var fake = new FakeBillingBackend();
        fake.SetActiveEntitlements("not_a_real_entitlement");

        Assert.AreEqual(PlanTier.None, fake.PurchasedTier);
    }

    // --- BillingService <-> backend wiring ------------------------------------------------------

    [Test] public void Initialize_wires_fake_backend_into_purchased_tier()
    {
        var fake = new FakeBillingBackend();
        fake.SetActiveEntitlements("tier_business");
        BillingService.BackendFactory = () => fake;

        BillingService.Initialize();

        Assert.AreEqual(PlanTier.Business, BillingService.PurchasedTier);
    }

    [Test] public void Initialize_is_idempotent()
    {
        var fake = new FakeBillingBackend();
        int factoryCalls = 0;
        BillingService.BackendFactory = () => { factoryCalls++; return fake; };

        BillingService.Initialize();
        BillingService.Initialize();

        Assert.AreEqual(1, factoryCalls, "a second Initialize() must not recreate the backend");
    }

    [Test] public void PurchasedTier_is_none_before_initialize()
    {
        Assert.AreEqual(PlanTier.None, BillingService.PurchasedTier);
    }

    [Test] public void EntitlementGate_reflects_fake_tier_after_initialize()
    {
        var fake = new FakeBillingBackend();
        fake.SetActiveEntitlements("tier_network");
        BillingService.BackendFactory = () => fake;

        BillingService.Initialize();

        Assert.AreEqual(PlanTier.Network, EntitlementGate.CurrentTier);
    }

    [Test] public void OnEntitlementChanged_fires_when_backend_reports_a_change()
    {
        var fake = new FakeBillingBackend();
        BillingService.BackendFactory = () => fake;
        BillingService.Initialize();

        PlanTier? seen = null;
        void Handler(PlanTier t) => seen = t;
        BillingService.OnEntitlementChanged += Handler;
        try
        {
            fake.SetActiveEntitlements("tier_start");
            Assert.AreEqual(PlanTier.Start, seen);
        }
        finally
        {
            BillingService.OnEntitlementChanged -= Handler;
        }
    }

    [Test] public void ResetSeamsForTests_clears_entitlement_subscribers()
    {
        bool called = false;
        BillingService.OnEntitlementChanged += _ => called = true;

        BillingService.ResetSeamsForTests();
        var fake = new FakeBillingBackend();
        BillingService.BackendFactory = () => fake;
        BillingService.Initialize();
        fake.SetActiveEntitlements("tier_start");

        Assert.IsFalse(called, "a subscriber surviving ResetSeamsForTests would leak into other tests");
    }

    // --- AppUserId fallback ----------------------------------------------------------------------

    [Test] public void AppUserId_falls_back_to_device_id_when_backend_absent()
    {
        Assert.AreEqual(SystemInfo.deviceUniqueIdentifier, BillingService.AppUserId);
    }

    [Test] public void AppUserId_uses_backend_value_when_present()
    {
        var fake = new FakeBillingBackend { AppUserId = "rc-anon-123" };
        BillingService.BackendFactory = () => fake;

        BillingService.Initialize();

        Assert.AreEqual("rc-anon-123", BillingService.AppUserId);
    }

    [Test] public void AppUserId_falls_back_to_device_id_when_backend_reports_empty()
    {
        var fake = new FakeBillingBackend { AppUserId = "" };
        BillingService.BackendFactory = () => fake;

        BillingService.Initialize();

        Assert.AreEqual(SystemInfo.deviceUniqueIdentifier, BillingService.AppUserId);
    }

    [Test] public void BillingIdentity_reflects_billing_service_app_user_id()
    {
        var fake = new FakeBillingBackend { AppUserId = "rc-anon-456" };
        BillingService.BackendFactory = () => fake;

        BillingService.Initialize();

        Assert.AreEqual("rc-anon-456", BillingIdentity.AppUserId);
    }

    [Test] public void BillingIdentity_falls_back_to_device_id_when_backend_reports_empty()
    {
        var fake = new FakeBillingBackend { AppUserId = "" };
        BillingService.BackendFactory = () => fake;

        BillingService.Initialize();

        Assert.AreEqual(SystemInfo.deviceUniqueIdentifier, BillingIdentity.AppUserId);
    }

    // --- Purchase / Restore forwarding -------------------------------------------------------------

    [Test] public void Purchase_forwards_to_backend_and_reports_success()
    {
        var fake = new FakeBillingBackend();
        BillingService.BackendFactory = () => fake;
        BillingService.Initialize();

        bool? ok = null; string reason = "unset";
        BillingService.Purchase("sub.start.month", (success, why) => { ok = success; reason = why; });

        Assert.AreEqual("sub.start.month", fake.LastPurchaseSku);
        Assert.IsTrue(ok);
        Assert.IsNull(reason);
    }

    [Test] public void Purchase_forwards_backend_failure_reason()
    {
        var fake = new FakeBillingBackend { PurchaseSucceeds = false, PurchaseFailureReason = "user_cancelled" };
        BillingService.BackendFactory = () => fake;
        BillingService.Initialize();

        bool? ok = null; string reason = null;
        BillingService.Purchase("sub.start.month", (success, why) => { ok = success; reason = why; });

        Assert.IsFalse(ok);
        Assert.AreEqual("user_cancelled", reason);
    }

    [Test] public void Purchase_before_initialize_reports_failure_without_throwing()
    {
        bool? ok = null;
        Assert.DoesNotThrow(() => BillingService.Purchase("sub.start.month", (success, _) => ok = success));
        Assert.IsFalse(ok);
    }

    [Test] public void RestorePurchases_forwards_to_backend()
    {
        var fake = new FakeBillingBackend();
        BillingService.BackendFactory = () => fake;
        BillingService.Initialize();

        bool? ok = null;
        BillingService.RestorePurchases(success => ok = success);

        Assert.IsTrue(fake.RestoreCalled);
        Assert.IsTrue(ok);
    }

    [Test] public void RestorePurchases_before_initialize_reports_failure_without_throwing()
    {
        bool? ok = null;
        Assert.DoesNotThrow(() => BillingService.RestorePurchases(success => ok = success));
        Assert.IsFalse(ok);
    }
}

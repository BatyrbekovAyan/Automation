using NUnit.Framework;

/// <summary>
/// Pins the two Android-track billing rules (2026-09-05, Google Play submission):
///
///  1. A DEVICE never gets <see cref="FakeBillingBackend"/>, key or no key. The old factory
///     fell back to the fake whenever the platform key was empty — and secrets.json carried
///     no androidKey — so on Android every plan «sold» for free through the fake's synchronous
///     success. The real backend without a key stays uninitialised instead (purchases refuse
///     with «not_initialized»), and <see cref="StoreBillingKeyGuard"/> stops the keyless release
///     build from ever being produced.
///
///  2. The price fetch on Android is split by Play Billing product TYPE: subscriptions under
///     «subs», the consumable top-up under «inapp». The split's input is
///     <see cref="PlanCatalog.SubscriptionSkus"/>; this pins it against <see cref="PlanCatalog.AllSkus"/>
///     so the seven sellable ids can never drift apart between the two calls.
/// </summary>
public class AndroidBillingPathTests
{
    // ---- 1. backend selection ----------------------------------------------------------------

    [Test]
    public void Editor_always_gets_the_fake_backend()
    {
        Assert.AreEqual(BillingService.BackendKind.Fake, BillingService.SelectBackendKind(isEditor: true, keyPresent: true));
        Assert.AreEqual(BillingService.BackendKind.Fake, BillingService.SelectBackendKind(isEditor: true, keyPresent: false));
    }

    [Test]
    public void Device_with_a_key_gets_the_real_backend()
        => Assert.AreEqual(BillingService.BackendKind.RevenueCat, BillingService.SelectBackendKind(isEditor: false, keyPresent: true));

    [Test]
    public void Device_without_a_key_never_gets_the_fake_backend()
    {
        // THE BUG: this used to be Fake, whose Purchase() succeeds synchronously — a free
        // entitlement for anyone on a keyless Android build. The key must not enter the decision.
        Assert.AreEqual(BillingService.BackendKind.RevenueCat, BillingService.SelectBackendKind(isEditor: false, keyPresent: false));
    }

    // ---- 2. the subs / inapp split -------------------------------------------------------------

    [Test]
    public void Subscription_skus_are_all_skus_minus_the_topup()
    {
        string[] subs = PlanCatalog.SubscriptionSkus();

        CollectionAssert.DoesNotContain(subs, PlanCatalog.SkuTopUp, "the top-up is a one-time product — it must go to the inapp query");
        CollectionAssert.AreEquivalent(new[]
        {
            "sub.start.month", "sub.start.year",
            "sub.business.month", "sub.business.year",
            "sub.network.month", "sub.network.year",
        }, subs);
    }

    [Test]
    public void Subs_plus_topup_is_exactly_the_sellable_set()
    {
        var union = new System.Collections.Generic.List<string>(PlanCatalog.SubscriptionSkus()) { PlanCatalog.SkuTopUp };
        CollectionAssert.AreEquivalent(PlanCatalog.AllSkus(), union,
            "the two Android queries together must ask the store for every sellable product, and nothing else");
    }

    // ---- 3. Play product ids, replacement, cancel reason -----------------------------------------

    [TestCase("sub.start.month:monthly", "sub.start.month")]
    [TestCase("sub.business.year:yearly:intro", "sub.business.year")]
    [TestCase("sub.start.month", "sub.start.month")]      // App Store ids carry no base plan
    [TestCase("topup.dialogs.500", "topup.dialogs.500")]
    [TestCase("", "")]
    [TestCase(null, null)]
    public void StoreProductKey_StripsThePlayBasePlanSuffix(string storeId, string expected)
        => Assert.AreEqual(expected, StoreProductKey.Normalize(storeId));

    [Test]
    public void PlanChange_OnPlay_ReplacesTheOwnedSubscription()
    {
        // THE BUG: Start → Business without an old sku is a SECOND subscription on Play, billed
        // alongside the first until the owner finds it in Play's subscription settings.
        PurchaseParams p = PurchaseParamsPolicy.Resolve("sub.start.month", "sub.business.month", googlePlay: true);

        Assert.IsTrue(p.Replace);
        Assert.AreEqual("sub.start.month", p.OldSku);
        Assert.AreEqual(PurchaseParamsPolicy.SubscriptionType, p.Type);
    }

    [Test]
    public void PeriodChange_OnPlay_IsAlsoAReplacement()
        => Assert.AreEqual("sub.start.month",
            PurchaseParamsPolicy.Resolve("sub.start.month", "sub.start.year", googlePlay: true).OldSku);

    [Test]
    public void FirstSubscription_OnPlay_IsAPlainBuy()
    {
        Assert.IsFalse(PurchaseParamsPolicy.Resolve(null, "sub.start.month", googlePlay: true).Replace);
        Assert.IsFalse(PurchaseParamsPolicy.Resolve("", "sub.start.month", googlePlay: true).Replace);
    }

    [Test]
    public void BuyingTheOwnedProductAgain_IsNotAReplacement()
        => Assert.IsFalse(PurchaseParamsPolicy.Resolve("sub.start.month", "sub.start.month", googlePlay: true).Replace);

    [Test]
    public void TheTopUp_IsOneTime_AndNeverReplacesASubscription()
    {
        PurchaseParams p = PurchaseParamsPolicy.Resolve("sub.start.month", PlanCatalog.SkuTopUp, googlePlay: true);

        Assert.AreEqual(PurchaseParamsPolicy.OneTimeType, p.Type);
        Assert.IsFalse(p.Replace, "a consumable bought over a subscription must not cancel the subscription");
    }

    [Test]
    public void AppStore_NeverPassesAnOldSku()
    {
        // StoreKit swaps within the subscription group by itself; an old sku there is meaningless.
        PurchaseParams p = PurchaseParamsPolicy.Resolve("sub.start.month", "sub.business.month", googlePlay: false);
        Assert.IsFalse(p.Replace);
        Assert.AreEqual(PurchaseParamsPolicy.SubscriptionType, p.Type);
    }

    [Test]
    public void UserCancel_OutranksTheErrorObject()
    {
        // The Android wrapper sends BOTH for a dismissed Play sheet; the paywall compares the
        // reason against BillingFailure.UserCancelled to stay silent.
        Assert.AreEqual(BillingFailure.UserCancelled, PurchaseParamsPolicy.FailureReason(userCancelled: true, "Purchase was cancelled."));
        Assert.AreEqual("Purchase was cancelled.", PurchaseParamsPolicy.FailureReason(userCancelled: false, "Purchase was cancelled."));
        Assert.AreEqual("unknown_error", PurchaseParamsPolicy.FailureReason(userCancelled: false, null));
    }

    [Test]
    public void CancelSentinel_IsTheOneThePaywallCompares()
        => Assert.AreEqual("user_cancelled", BillingFailure.UserCancelled,
            "PaywallController / ProfileSubPages compare against this constant — changing it silently re-enables the failure notice on cancel");

    // ---- 4. the build guard ----------------------------------------------------------------------

    [Test]
    public void Guard_passes_when_the_platform_key_is_present()
    {
        Assert.AreEqual(StoreBillingKeyGuard.Verdict.Ok, StoreBillingKeyGuard.Decide(keyPresent: true, developmentBuild: false));
        Assert.AreEqual(StoreBillingKeyGuard.Verdict.Ok, StoreBillingKeyGuard.Decide(keyPresent: true, developmentBuild: true));
    }

    [Test]
    public void Guard_fails_a_keyless_release_build()
        => Assert.AreEqual(StoreBillingKeyGuard.Verdict.FailRelease, StoreBillingKeyGuard.Decide(keyPresent: false, developmentBuild: false));

    [Test]
    public void Guard_only_warns_on_a_keyless_development_build()
        => Assert.AreEqual(StoreBillingKeyGuard.Verdict.WarnDevelopment, StoreBillingKeyGuard.Decide(keyPresent: false, developmentBuild: true));
}

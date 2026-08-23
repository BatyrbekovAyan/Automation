using NUnit.Framework;

/// <summary>
/// Truth table for the launch-time «чек ценности» decision (Task 15a, Part B).
/// Four inputs, one output — every combination is pinned here rather than being
/// re-derived inside Manager's boot coroutine, which EditMode cannot instantiate.
/// </summary>
public class LaunchPaywallPolicyTests
{
    [Test]
    public void Spent_trial_with_nothing_purchased_opens_the_paywall_once()
    {
        Assert.IsTrue(LaunchPaywallPolicy.ShouldShowExpiry(
            PlanTier.None, entitlementsKnown: true, trialExpired: true, alreadyShownThisLaunch: false));
    }

    [Test]
    public void The_same_launch_never_opens_it_twice()
    {
        Assert.IsFalse(LaunchPaywallPolicy.ShouldShowExpiry(
            PlanTier.None, entitlementsKnown: true, trialExpired: true, alreadyShownThisLaunch: true));
    }

    [Test]
    public void An_unresolved_entitlement_set_waits_instead_of_paywalling()
    {
        // Keyed device, first CustomerInfo still in flight: EntitlementGate.CurrentTier is
        // Trial GRACE there, so this branch is normally unreachable through the tier alone —
        // the flag is the explicit guard, so a future caller that reads a purchased tier
        // straight off the backend can't slip past it.
        Assert.IsFalse(LaunchPaywallPolicy.ShouldShowExpiry(
            PlanTier.None, entitlementsKnown: false, trialExpired: true, alreadyShownThisLaunch: false));
    }

    [Test]
    public void A_live_trial_is_not_an_expired_one()
    {
        Assert.IsFalse(LaunchPaywallPolicy.ShouldShowExpiry(
            PlanTier.Trial, entitlementsKnown: true, trialExpired: false, alreadyShownThisLaunch: false));
    }

    [Test]
    public void A_paying_customer_is_never_shown_the_expiry_paywall()
    {
        // The trial clock keeps ticking (and expiring) behind a purchase — the tier is what
        // decides, never the ledger on its own.
        foreach (PlanTier tier in new[] { PlanTier.Start, PlanTier.Business, PlanTier.Network })
            Assert.IsFalse(LaunchPaywallPolicy.ShouldShowExpiry(
                tier, entitlementsKnown: true, trialExpired: true, alreadyShownThisLaunch: false), $"tier={tier}");
    }

    [Test]
    public void Full_truth_table()
    {
        foreach (PlanTier tier in new[] { PlanTier.None, PlanTier.Trial, PlanTier.Business })
            foreach (bool known in new[] { false, true })
                foreach (bool expired in new[] { false, true })
                    foreach (bool shown in new[] { false, true })
                    {
                        bool expected = !shown && known && tier == PlanTier.None && expired;
                        Assert.AreEqual(expected,
                            LaunchPaywallPolicy.ShouldShowExpiry(tier, known, expired, shown),
                            $"tier={tier} known={known} expired={expired} shown={shown}");
                    }
    }
}

using NUnit.Framework;

public class EntitlementPolicyTests
{
    [Test] public void Purchase_beats_trial()
        => Assert.AreEqual(PlanTier.Start, EntitlementPolicy.EffectiveTier(PlanTier.Start, true, true));

    [Test] public void Active_trial_when_nothing_purchased()
        => Assert.AreEqual(PlanTier.Trial, EntitlementPolicy.EffectiveTier(PlanTier.None, true, false));

    [Test] public void Expired_trial_without_purchase_is_none()
        => Assert.AreEqual(PlanTier.None, EntitlementPolicy.EffectiveTier(PlanTier.None, true, true));

    [Test] public void Not_started_trial_is_trial_grace()   // мастер первого бота должен открываться до первой авторизации
        => Assert.AreEqual(PlanTier.Trial, EntitlementPolicy.EffectiveTier(PlanTier.None, false, false));

    [TestCase(PlanTier.Start, 0, true)]
    [TestCase(PlanTier.Start, 1, false)]
    [TestCase(PlanTier.Business, 2, true)]
    [TestCase(PlanTier.Business, 3, false)]
    [TestCase(PlanTier.None, 0, false)]
    public void Bot_gate(PlanTier t, int existing, bool ok)
        => Assert.AreEqual(ok, EntitlementPolicy.CanCreateBot(t, existing));

    [TestCase(PlanTier.Network, 4, true)]
    [TestCase(PlanTier.Network, 5, false)]
    [TestCase(PlanTier.Trial, 2, true)]
    [TestCase(PlanTier.Trial, 3, false)]
    public void Channel_gate(PlanTier t, int connected, bool ok)
        => Assert.AreEqual(ok, EntitlementPolicy.CanConnectChannel(t, connected));
}

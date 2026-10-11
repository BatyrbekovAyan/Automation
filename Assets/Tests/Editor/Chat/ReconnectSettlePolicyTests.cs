using NUnit.Framework;

/// <summary>
/// F2 (plan 2026-10-11 §5.1–5.2, task T9): the two pure decisions around a purchase.
/// <list type="bullet">
/// <item>The wait before a pairing: the settings' fresh path re-reads GetUsage on the top-up schedule only while the last
/// snapshot DEFINITELY says the server has no channel slot (expired / grace, or every slot of the plan taken — Create's
/// own <c>Compute Slot Limit</c> table); an unknown snapshot never blocks.</item>
/// <item>The purchase: only a move from no paid plan to a paid one — never the session's first (boot) resolution.</item>
/// </list>
/// </summary>
public class ReconnectSettlePolicyTests
{
    private static UsageSnapshot Usage(string plan, string status, int channels, bool success = true) =>
        new UsageSnapshot { success = success, plan = plan, status = status, channelsConnected = channels };

    [TestCase("trial", "trialing", 3)]
    [TestCase("start", "active", 1)]
    [TestCase("business", "active", 3)]
    [TestCase("network", "active", 5)]
    [TestCase("none", "active", 0)]
    [TestCase("Business", " Active ", 3)]
    public void ChannelLimit_IsCreatesOwnSlotTable(string plan, string status, int limit)
    {
        Assert.AreEqual(limit, ReconnectSettlePolicy.ChannelLimit(plan, status));
    }

    [TestCase("start", "expired")]
    [TestCase("business", "grace")]
    [TestCase("trial", "EXPIRED")]
    public void ChannelLimit_ExpiredOrGrace_GivesNoSlot(string plan, string status)
    {
        Assert.AreEqual(0, ReconnectSettlePolicy.ChannelLimit(plan, status),
            "Compute Slot Limit: grace counts as expired — a pairing there would be refused with channel_limit");
    }

    [Test]
    public void ChannelLimit_AnUnknownPlan_IsUnknown_NotZero()
    {
        Assert.IsNull(ReconnectSettlePolicy.ChannelLimit("enterprise", "active"), "a plan this build does not know never blocks");
        Assert.IsNull(ReconnectSettlePolicy.ChannelLimit(null, "active"));
    }

    [Test]
    public void ServerHasFreeChannel_ComparesTheLiveRowsWithThePlan()
    {
        Assert.IsTrue(ReconnectSettlePolicy.ServerHasFreeChannel(Usage("start", "active", 0)), "bought Start, the retired row no longer counts");
        Assert.IsFalse(ReconnectSettlePolicy.ServerHasFreeChannel(Usage("start", "active", 1)), "Start's one slot is taken");
        Assert.IsTrue(ReconnectSettlePolicy.ServerHasFreeChannel(Usage("business", "active", 2)));
        Assert.IsFalse(ReconnectSettlePolicy.ServerHasFreeChannel(Usage("trial", "expired", 0)),
            "the webhook has not landed yet: the server still reads the subscriber as expired");
    }

    [Test]
    public void ServerHasFreeChannel_UnknownSnapshot_IsNull()
    {
        Assert.IsNull(ReconnectSettlePolicy.ServerHasFreeChannel(null), "no read yet this session");
        Assert.IsNull(ReconnectSettlePolicy.ServerHasFreeChannel(Usage("start", "active", 5, success: false)), "an unusable body");
        Assert.IsNull(ReconnectSettlePolicy.ServerHasFreeChannel(Usage("enterprise", "active", 0)));
    }

    [Test]
    public void TheWait_OnlyWhenTheSnapshotDefinitelySaysNoSlot()
    {
        Assert.IsTrue(ReconnectSettlePolicy.ShouldWaitBeforePairing(Usage("trial", "expired", 0)));
        Assert.IsTrue(ReconnectSettlePolicy.ShouldWaitBeforePairing(Usage("start", "active", 1)));
        Assert.IsFalse(ReconnectSettlePolicy.ShouldWaitBeforePairing(Usage("start", "active", 0)), "the ordinary connect costs no read at all");
        Assert.IsFalse(ReconnectSettlePolicy.ShouldWaitBeforePairing(Usage("trial", "trialing", 1)));
        Assert.IsFalse(ReconnectSettlePolicy.ShouldWaitBeforePairing(null), "unknown is fail-open (ServerAccountStatus' rule)");
    }

    [Test]
    public void TheReReads_FollowTheTopUpSchedule_AndEnd()
    {
        float[] expected = TopUpSettlePolicy.ReadDelaysSeconds;
        Assert.AreEqual(new[] { 0f, 2f, 5f, 10f, 20f }, expected, "0/2/5/10/20 s (plan §1 T9)");
        for (int i = 0; i < expected.Length; i++)
            Assert.AreEqual(expected[i], ReconnectSettlePolicy.DelayBeforeRead(i));
        Assert.IsNull(ReconnectSettlePolicy.DelayBeforeRead(expected.Length), "finite — then «Оплата ещё не дошла до сервера…», no pairing");
        Assert.IsNull(ReconnectSettlePolicy.DelayBeforeRead(-1));
    }

    [Test]
    public void APurchase_IsAMoveFromNoPaidPlanToAPaidOne()
    {
        Assert.IsTrue(ReconnectSettlePolicy.IsPurchaseTransition(PlanTier.None, PlanTier.Start));
        Assert.IsTrue(ReconnectSettlePolicy.IsPurchaseTransition(PlanTier.None, PlanTier.Network));
        Assert.IsTrue(ReconnectSettlePolicy.IsPurchaseTransition(PlanTier.Trial, PlanTier.Business), "a trial is not a paid plan");
    }

    [Test]
    public void NotAPurchase_TheBootResolution_AnUpgrade_OrALapse()
    {
        Assert.IsFalse(ReconnectSettlePolicy.IsPurchaseTransition(null, PlanTier.Business),
            "the first answer of the session is the plan the owner already had — no sheet at every launch");
        Assert.IsFalse(ReconnectSettlePolicy.IsPurchaseTransition(PlanTier.Start, PlanTier.Business), "an upgrade changes no channel");
        Assert.IsFalse(ReconnectSettlePolicy.IsPurchaseTransition(PlanTier.Business, PlanTier.None), "a lapse");
        Assert.IsFalse(ReconnectSettlePolicy.IsPurchaseTransition(PlanTier.None, PlanTier.None), "a failed purchase");
        Assert.IsFalse(ReconnectSettlePolicy.IsPurchaseTransition(PlanTier.None, PlanTier.Trial));
    }
}

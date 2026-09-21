using NUnit.Framework;

/// <summary>Stage 7.2/7.3: readiness facts and outcome labels are computed from reliable inputs, never assumed.</summary>
public class BotReadinessTests
{
    [Test]
    public void Readiness_NeedsAllFourFacts()
    {
        var r = BotReadiness.Compute("wa1", false, false, 2, 0, "", true);
        Assert.IsTrue(r.ChannelConnected && r.ModeConfirmed && r.KnowledgeAvailable && r.Ready);
        Assert.IsFalse(BotReadiness.Compute("-1", false, false, 2, 0, "", true).ChannelConnected, "the unauthed sentinel is not a channel");
        Assert.IsFalse(BotReadiness.Compute("wa1", true, false, 2, 0, "", true).ModeConfirmed, "a pending sync is not a confirmed mode");
        Assert.IsFalse(BotReadiness.Compute("wa1", false, true, 2, 0, "", true).ModeConfirmed, "a failed sync is not a confirmed mode");
        Assert.IsFalse(BotReadiness.Compute("wa1", false, false, 0, 0, "  ", true).KnowledgeAvailable);
        Assert.IsTrue(BotReadiness.Compute("wa1", false, false, 0, 1, "", null).KnowledgeAvailable, "a document alone is knowledge");
        Assert.IsFalse(BotReadiness.Compute("wa1", false, false, 1, 0, "x", null).Ready, "never tested ≠ ready");
        Assert.IsFalse(BotReadiness.Compute("wa1", false, false, 1, 0, "x", false).Ready);
    }

    [Test]
    public void Copy_LinesAndSources()
    {
        Assert.AreEqual("• Канал подключён", BotReadinessCopy.Line(true, BotReadinessCopy.Channel, BotReadinessCopy.ChannelNo));
        Assert.IsFalse(BotReadinessCopy.Line(true, BotReadinessCopy.Channel, BotReadinessCopy.ChannelNo).Contains("\u2713"), "no ✓: the static SF Pro atlases would draw it as nothing");
        Assert.AreEqual("• Канал не подключён", BotReadinessCopy.Line(false, BotReadinessCopy.Channel, BotReadinessCopy.ChannelNo));
        Assert.AreEqual("• Тест ещё не запускали", BotReadinessCopy.TestLine(null));
        Assert.AreEqual("Источники: каталог, контакты, прайс-листы (2)", BotReadinessCopy.Sources(true, true, 2));
        Assert.AreEqual("Источники: только описание бизнеса", BotReadinessCopy.Sources(false, false, 0));
    }

    [Test]
    public void OutcomeLabels_DistinguishModeLimitErrorAndUnknown()
    {
        Assert.AreEqual(ReplyOutcomeCopy.Suppressed, ReplyOutcomeCopy.Label("suppressed"));
        Assert.AreEqual(ReplyOutcomeCopy.QuotaDenied, ReplyOutcomeCopy.Label("quota_denied"));
        Assert.AreEqual(ReplyOutcomeCopy.SendFailed, ReplyOutcomeCopy.Label("send_failed"));
        Assert.AreEqual(ReplyOutcomeCopy.SendUnknown, ReplyOutcomeCopy.Label("send_unknown"));
        Assert.AreEqual(ReplyOutcomeCopy.Sent, ReplyOutcomeCopy.Label("sent"));
        Assert.AreEqual(ReplyOutcomeCopy.Pending, ReplyOutcomeCopy.Label("send_pending"));
        Assert.AreEqual(ReplyOutcomeCopy.Cancelled, ReplyOutcomeCopy.Label("something_new"), "an unknown state is never shown raw");
        Assert.IsTrue(ReplyOutcomeCopy.NeedsAttention("send_unknown") && ReplyOutcomeCopy.NeedsAttention("quota_denied"));
        Assert.IsFalse(ReplyOutcomeCopy.NeedsAttention("sent") || ReplyOutcomeCopy.NeedsAttention("suppressed"));
        foreach (string s in new[] { "sent", "suppressed", "quota_denied", "send_failed", "send_unknown", "superseded", "cancelled_owner", "cancelled_mode", "duplicate", "generation_failed", "claimed", "cancelled_busy" })
            Assert.IsFalse(ReplyOutcomeCopy.Label(s).Contains("_"), s + ": no technical code in the label");
    }
}

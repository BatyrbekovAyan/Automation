using System.Collections.Generic;
using NUnit.Framework;

/// <summary>A5 (audit stage 4): wire models, the pause mirror and the row copy of the handoff surface.</summary>
public class HandoffModelsTests
{
    [Test]
    public void Parse_ReadsRequestsAndPausedChats()
    {
        var r = HandoffsResponse.Parse("{\"success\":true,\"requests\":[{\"id\":7,\"profileId\":\"p1\",\"chatId\":\"c1@c.us\",\"reason\":\"order\",\"status\":\"open\",\"summary\":\"25 роз\",\"createdAt\":1758000000000,\"notifyState\":\"sent\",\"paused\":true}],\"pausedChats\":[{\"profileId\":\"p1\",\"chatId\":\"c1@c.us\"}],\"changed\":0}");
        Assert.IsNotNull(r);
        Assert.IsTrue(r.success);
        Assert.AreEqual(1, r.requests.Count);
        Assert.AreEqual(7, r.requests[0].id);
        Assert.IsTrue(r.requests[0].IsOpen);
        Assert.AreEqual("25 роз", r.requests[0].summary);
        Assert.AreEqual(1, r.pausedChats.Count);
        Assert.AreEqual("c1@c.us", r.pausedChats[0].chatId);
    }

    [Test]
    public void Parse_ToleratesMissingListsAndGarbage()
    {
        var r = HandoffsResponse.Parse("{\"success\":true}");
        Assert.IsNotNull(r);
        Assert.IsEmpty(r.requests);
        Assert.IsEmpty(r.pausedChats);
        Assert.IsNull(HandoffsResponse.Parse(""));
        Assert.IsNull(HandoffsResponse.Parse("not json"));
        var f = HandoffsResponse.Parse("{\"success\":false,\"error\":\"unauthorized\"}");
        Assert.IsFalse(f.success);
        Assert.AreEqual("unauthorized", f.error);
    }

    [Test]
    public void DashboardOutcome_EventFields_DefaultToClassifier()
    {
        var old = DashboardResponse.Parse("{\"success\":true,\"outcomes\":[{\"profileId\":\"p\",\"chatId\":\"c\",\"outcome\":\"owner_needed\",\"summary\":\"s\",\"outcomeAt\":1,\"lastMessageAt\":1}]}");
        Assert.IsFalse(old.outcomes[0].IsEvent, "a row without `source` (older server / cached) is a classifier verdict");
        Assert.IsFalse(old.outcomes[0].HasOpenHandoff);
        var ev = DashboardResponse.Parse("{\"success\":true,\"outcomes\":[{\"profileId\":\"p\",\"chatId\":\"c\",\"outcome\":\"order_collected\",\"summary\":\"s\",\"outcomeAt\":1,\"lastMessageAt\":1,\"source\":\"event\",\"handoffId\":12,\"handoffStatus\":\"open\",\"handoffReason\":\"order\"}]}");
        Assert.IsTrue(ev.outcomes[0].IsEvent);
        Assert.IsTrue(ev.outcomes[0].HasOpenHandoff);
        Assert.AreEqual(12, ev.outcomes[0].handoffId.Value);
        var taken = DashboardResponse.Parse("{\"success\":true,\"outcomes\":[{\"profileId\":\"p\",\"chatId\":\"c\",\"outcome\":\"owner_needed\",\"source\":\"event\",\"handoffId\":12,\"handoffStatus\":\"taken\"}]}");
        Assert.IsFalse(taken.outcomes[0].HasOpenHandoff, "only an OPEN request is taken on open");
    }

    [Test]
    public void RowSummary_MarksOnlyRealRequests()
    {
        Assert.AreEqual("Заявка · 25 роз", HandoffCopy.RowSummary(new DashboardOutcome { summary = "25 роз", source = "event" }));
        Assert.AreEqual("25 роз", HandoffCopy.RowSummary(new DashboardOutcome { summary = "25 роз" }));
        Assert.AreEqual("Заявка · ", HandoffCopy.RowSummary(new DashboardOutcome { source = "event" }));
        Assert.AreEqual("", HandoffCopy.RowSummary(null));
    }

    private static IReadOnlyDictionary<string, DashboardProfileRef> Map() =>
        DashboardProfileMap.ProfileToBot(new List<BotProfiles>
        {
            new BotProfiles { botName = "Bot0", whatsappProfileId = "wa0", telegramProfileId = "tg0" },
            new BotProfiles { botName = "Bot1", whatsappProfileId = "wa1", telegramProfileId = "-1" },
        });

    [Test]
    public void PauseMirror_PausesOnlyResolvableChats_Once()
    {
        var paused = new List<HandoffPausedChat>
        {
            new HandoffPausedChat { profileId = "wa0", chatId = "a@c.us" },
            new HandoffPausedChat { profileId = "wa0", chatId = "a@c.us" },       // duplicate row
            new HandoffPausedChat { profileId = "tg0", chatId = "555" },
            new HandoffPausedChat { profileId = "unknown", chatId = "z@c.us" },   // not one of our bots
            new HandoffPausedChat { profileId = "wa1", chatId = "" },             // malformed
            null,
        };
        var plans = HandoffPauseMirror.Compute(paused, Map(), (b, c) => (false, false));
        Assert.AreEqual(2, plans.Count);
        Assert.AreEqual("Bot0", plans[0].BotId); Assert.AreEqual("a@c.us", plans[0].ChatId);
        Assert.AreEqual("Bot0", plans[1].BotId); Assert.AreEqual("555", plans[1].ChatId);
    }

    [Test]
    public void PauseMirror_SkipsChatsAlreadyExplicitlyOn_ButOverridesExplicitOff()
    {
        var paused = new List<HandoffPausedChat>
        {
            new HandoffPausedChat { profileId = "wa0", chatId = "on@c.us" },
            new HandoffPausedChat { profileId = "wa0", chatId = "off@c.us" },
            new HandoffPausedChat { profileId = "wa0", chatId = "unset@c.us" },
        };
        var plans = HandoffPauseMirror.Compute(paused, Map(), (b, c) => c == "on@c.us" ? (true, true) : c == "off@c.us" ? (true, false) : (false, false));
        CollectionAssert.AreEqual(new[] { "off@c.us", "unset@c.us" }, plans.ConvertAll(p => p.ChatId),
            "the server's pause is a fact: an explicit local «Авто» is overridden, an explicit «Вместе» is left alone");
    }

    [Test]
    public void PauseMirror_NullInputs_AreEmpty()
    {
        Assert.IsEmpty(HandoffPauseMirror.Compute(null, Map(), (b, c) => (false, false)));
        Assert.IsEmpty(HandoffPauseMirror.Compute(new List<HandoffPausedChat>(), null, (b, c) => (false, false)));
    }
}

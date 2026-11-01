using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

/// <summary>
/// «Личные и рабочие чаты»: the app's mirror of the server's decisions. What a chat IS for the assistant
/// (<see cref="ChatAudienceStore.KindOf"/>, <see cref="ChatAudienceStore.AllowsAssistant"/>) is the server's decision or the
/// owner's move still on its way — never the rule. How a chat is SHOWN (<see cref="ChatAudienceStore.ViewOf"/>) falls back
/// to the shared rule for a chat the server has not decided yet, judged by the chat's facts and the EARLIEST last message the
/// app has seen for it against the connection moment. The server's view and those times are cached on disk; the moves are
/// not (the queue redraws them on launch).
/// </summary>
public class ChatAudienceStoreTests
{
    private const string Wa = "store-wa-profile";
    private const string Tg = "store-tg-profile";
    private const long MomentMs = 1_760_000_000_000;
    private const long BeforeSec = MomentMs / 1000 - 3600;   // an hour before the connection
    private const long AfterSec = MomentMs / 1000 + 3600;    // an hour after it

    private string _dir;
    private string _cache;

    [SetUp]
    public void UseATestCache()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ChatAudienceStoreTests-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _cache = Path.Combine(_dir, ChatAudienceStore.CacheFileName);
        ChatAudienceStore.UseCacheFileForTests(_cache);
    }

    [TearDown]
    public void RestoreTheStore()
    {
        ChatAudienceStore.ResetForTests();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static ChatViewModel WhatsappChat(string chatId, long lastMessageSec, bool? contact = false)
        => new ChatViewModel(chatId, "Title", null, "hi", lastMessageSec,
            facts: new AudienceFacts(false, chatId, "", false, false, false, contact));

    private static ChatViewModel TelegramChat(string chatId, long lastMessageSec, bool bot = false)
        => new ChatViewModel(chatId, "Title", null, "hi", lastMessageSec,
            facts: new AudienceFacts(true, chatId, "user", bot, false, false, false));

    private static AudienceDecision Decision(string chatId, AudienceKind kind, AudienceReason reason, bool byOwner = false, long version = 0)
        => new AudienceDecision { ChatId = chatId, Kind = kind, Reason = reason, ByOwner = byOwner, Version = version };

    private static AudienceProfileState Profile(string profileId, long? connectedAtMs, params AudienceDecision[] decisions)
        => new AudienceProfileState { ProfileId = profileId, ConnectedAtMs = connectedAtMs, Decisions = new List<AudienceDecision>(decisions) };

    [Test]
    public void Store_ServerDecisionBeatsRule_DesireBeatsServer()
    {
        var chat = WhatsappChat("77011112233@c.us", BeforeSec, contact: true);
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
        var byRule = ChatAudienceStore.ViewOf(Wa, chat);
        Assert.AreEqual(AudienceKind.Personal, byRule.Kind, "a saved contact is personal by the rule");
        Assert.AreEqual(AudienceReason.Contact, byRule.Reason);
        Assert.IsFalse(byRule.Decided || byRule.Pending);

        ChatAudienceStore.ApplyList(new[] { Profile(Wa, MomentMs, Decision(chat.ChatId, AudienceKind.Business, AudienceReason.Owner, true, 7)) });
        var byServer = ChatAudienceStore.ViewOf(Wa, chat);
        Assert.AreEqual(AudienceKind.Business, byServer.Kind, "the owner marked the client — the server's decision beats the rule");
        Assert.AreEqual(AudienceReason.Owner, byServer.Reason);
        Assert.IsTrue(byServer.Decided);
        Assert.IsFalse(byServer.Pending);
        Assert.AreEqual(AudienceKind.Business, ChatAudienceStore.KindOf(Wa, chat.ChatId));

        ChatAudienceStore.ApplyDesire(Wa, chat.ChatId, AudienceKind.Personal);
        var moving = ChatAudienceStore.ViewOf(Wa, chat);
        Assert.AreEqual(AudienceKind.Personal, moving.Kind, "the owner's move is drawn at once");
        Assert.AreEqual(AudienceReason.Owner, moving.Reason);
        Assert.IsTrue(moving.Pending, "…and is shown as still on its way");
        Assert.IsTrue(moving.Decided, "the server still holds its own decision meanwhile");
        Assert.AreEqual(AudienceKind.Personal, ChatAudienceStore.KindOf(Wa, chat.ChatId));

        ChatAudienceStore.ApplyList(new[] { Profile(Wa, MomentMs, Decision(chat.ChatId, AudienceKind.Business, AudienceReason.Owner, true, 7)) });
        Assert.AreEqual(AudienceKind.Personal, ChatAudienceStore.KindOf(Wa, chat.ChatId), "a list read meanwhile never overrides a move on its way");

        ChatAudienceStore.ApplyDesireResult(Wa, chat.ChatId, AudienceKind.Personal, applied: true);
        var settled = ChatAudienceStore.ViewOf(Wa, chat);
        Assert.AreEqual(AudienceKind.Personal, settled.Kind);
        Assert.AreEqual(AudienceReason.Owner, settled.Reason);
        Assert.IsTrue(settled.Decided);
        Assert.IsFalse(settled.Pending, "confirmed by the server");
    }

    [Test]
    public void Store_UndecidedUsesTheRule()
    {
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
        Assert.AreEqual(MomentMs, ChatAudienceStore.ConnectedAtMs(Wa));
        var old = WhatsappChat("77010000001@c.us", BeforeSec);
        var fresh = WhatsappChat("77010000002@c.us", AfterSec);

        var oldView = ChatAudienceStore.ViewOf(Wa, old);
        Assert.AreEqual(AudienceKind.Personal, oldView.Kind, "the conversation existed before the connection");
        Assert.AreEqual(AudienceReason.History, oldView.Reason);
        Assert.IsFalse(oldView.Decided);
        var freshView = ChatAudienceStore.ViewOf(Wa, fresh);
        Assert.AreEqual(AudienceKind.Business, freshView.Kind, "a new person, not in the contacts");
        Assert.AreEqual(AudienceReason.New, freshView.Reason);
        Assert.IsFalse(freshView.Decided || freshView.Pending);
        Assert.IsTrue(freshView.CanMove);

        Assert.IsNull(ChatAudienceStore.KindOf(Wa, fresh.ChatId), "the rule only draws a chat — it decides nothing");
        Assert.IsFalse(ChatAudienceStore.AllowsAssistant(Wa, fresh.ChatId), "the assistant waits for the server's word");

        Assert.IsNull(ChatAudienceStore.ConnectedAtMs(Tg));
        var unknown = ChatAudienceStore.ViewOf(Tg, TelegramChat("555", AfterSec));
        Assert.AreEqual(AudienceKind.Personal, unknown.Kind, "the moment is not known: the history is not either — personal");
        Assert.AreEqual(AudienceReason.Unknown, unknown.Reason);

        var group = ChatAudienceStore.ViewOf(Wa, WhatsappChat("120363000000000000@g.us", AfterSec));
        Assert.AreEqual(AudienceKind.Personal, group.Kind);
        Assert.AreEqual(AudienceReason.NonHuman, group.Reason);
        Assert.AreEqual(NonHumanKind.Group, group.NonHuman);
        Assert.IsFalse(group.CanMove, "the assistant never answers a group — it cannot be moved");
        var bot = ChatAudienceStore.ViewOf(Tg, TelegramChat("777", AfterSec, bot: true));
        Assert.AreEqual(NonHumanKind.Bot, bot.NonHuman);
        Assert.IsFalse(bot.CanMove);

        var nothing = ChatAudienceStore.ViewOf(Wa, null);
        Assert.AreEqual(AudienceKind.Personal, nothing.Kind, "no chat — nothing to show as business");
    }

    [Test]
    public void Store_AnUndecidedChat_IsJudgedByTheEarliestLastMessageTheAppSaw()
    {
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
        var old = WhatsappChat("77010000011@c.us", AfterSec);
        ChatAudienceStore.NoteLastMessage(Wa, old.ChatId, BeforeSec);   // the list showed it before the connection…
        ChatAudienceStore.NoteLastMessage(Wa, old.ChatId, AfterSec);    // …then with its first message after it
        AudienceView oldView = ChatAudienceStore.ViewOf(Wa, old);
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.History), (oldView.Kind, oldView.Reason),
            "a conversation the app saw before the connection stays one when the person writes again");
        Assert.IsFalse(oldView.Decided, "still only how the chat is shown — the server decides it");

        var fresh = WhatsappChat("77010000012@c.us", AfterSec + 60);
        ChatAudienceStore.NoteLastMessage(Wa, fresh.ChatId, AfterSec);
        AudienceView freshView = ChatAudienceStore.ViewOf(Wa, fresh);
        Assert.AreEqual((AudienceKind.Business, AudienceReason.New), (freshView.Kind, freshView.Reason),
            "a chat the app first saw after the connection: a new person");
        Assert.AreEqual(AudienceReason.History, ChatAudienceStore.ViewOf(Wa, WhatsappChat(fresh.ChatId, BeforeSec)).Reason,
            "the chat's own last message counts when it is the earlier one");

        Assert.AreEqual(AudienceReason.History, ChatAudienceStore.ViewOf(Wa, WhatsappChat("77010000013@c.us", BeforeSec)).Reason,
            "a chat nothing was noted for is judged by its own last message");
        Assert.AreEqual(AudienceReason.New, ChatAudienceStore.ViewOf(Wa, WhatsappChat("77010000014@c.us", AfterSec)).Reason);

        const string other = "store-wa-profile-2";
        ChatAudienceStore.ApplyConnected(other, MomentMs);
        Assert.AreEqual(AudienceKind.Business, ChatAudienceStore.ViewOf(other, old).Kind, "what the app saw belongs to its profile");

        const string noTime = "77010000015@c.us";
        ChatAudienceStore.NoteLastMessage(Wa, noTime, 0);
        ChatAudienceStore.NoteLastMessage(Wa, noTime, -5);
        ChatAudienceStore.NoteLastMessage(Bot.UnauthedProfileSentinel, noTime, BeforeSec);
        ChatAudienceStore.NoteLastMessage(Wa, "", BeforeSec);
        ChatAudienceStore.NoteLastMessage(Wa, null, BeforeSec);
        Assert.AreEqual(AudienceKind.Business, ChatAudienceStore.ViewOf(Wa, WhatsappChat(noTime, AfterSec)).Kind,
            "a time the list did not give, or a profile that is no profile, is no evidence");
    }

    [Test]
    public void Store_TheEarliestTimeSurvivesARestart_AndACacheWrittenBeforeItLoads()
    {
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
        var chat = WhatsappChat("77010000021@c.us", AfterSec);
        ChatAudienceStore.NoteLastMessage(Wa, chat.ChatId, BeforeSec);
        ChatAudienceStore.DropMemoryForTests();   // a restart
        Assert.AreEqual(AudienceReason.History, ChatAudienceStore.ViewOf(Wa, chat).Reason, "the earliest time is written down with the mirror");

        // A cache from before the app kept these times: its mirror loads, and the chat is judged by its own last message.
        File.WriteAllText(_cache, "{\"profiles\":[{\"profileId\":\"" + Wa + "\",\"connectedAtMs\":" + MomentMs
            + ",\"decisions\":[{\"chatId\":\"a@c.us\",\"kind\":\"business\",\"reason\":\"new\",\"byOwner\":false,\"version\":0}]}]}");
        ChatAudienceStore.DropMemoryForTests();
        Assert.AreEqual(MomentMs, ChatAudienceStore.ConnectedAtMs(Wa));
        Assert.AreEqual(AudienceKind.Business, ChatAudienceStore.KindOf(Wa, "a@c.us"));
        Assert.AreEqual(AudienceKind.Business, ChatAudienceStore.ViewOf(Wa, chat).Kind, "nothing was noted in that cache");

        ChatAudienceStore.NoteLastMessage(Wa, chat.ChatId, BeforeSec);
        ChatAudienceStore.DropMemoryForTests();
        Assert.AreEqual(AudienceKind.Business, ChatAudienceStore.KindOf(Wa, "a@c.us"), "the mirror is written back with the times");
        Assert.AreEqual(AudienceReason.History, ChatAudienceStore.ViewOf(Wa, chat).Reason);

        File.WriteAllText(_cache, "{\"profiles\":null,\"seen\":null}");
        ChatAudienceStore.DropMemoryForTests();
        Assert.IsNull(ChatAudienceStore.ConnectedAtMs(Wa), "empty parts read as nothing, never a crash");
    }

    [Test]
    public void Store_ANewConnectionMoment_RejudgesByTheSameEarliestTime()
    {
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
        var chat = WhatsappChat("77010000031@c.us", AfterSec + 7200);
        ChatAudienceStore.NoteLastMessage(Wa, chat.ChatId, AfterSec);
        Assert.AreEqual((AudienceKind.Business, AudienceReason.New), Verdict(ChatAudienceStore.ViewOf(Wa, chat)),
            "first seen an hour after the connection: a new person");

        ChatAudienceStore.ApplyConnected(Wa, MomentMs + 2 * 3_600_000L);   // another number: a new connection after it
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.History), Verdict(ChatAudienceStore.ViewOf(Wa, chat)),
            "the same earliest time is from before the new connection");
    }

    private static (AudienceKind, AudienceReason) Verdict(AudienceView view) => (view.Kind, view.Reason);

    [Test]
    public void Store_AllowsAssistantOnlyForBusiness()
    {
        ChatAudienceStore.ApplyList(new[] { Profile(Wa, MomentMs,
            Decision("b@c.us", AudienceKind.Business, AudienceReason.New),
            Decision("p@c.us", AudienceKind.Personal, AudienceReason.Contact)) });
        Assert.IsTrue(ChatAudienceStore.AllowsAssistant(Wa, "b@c.us"));
        Assert.IsFalse(ChatAudienceStore.AllowsAssistant(Wa, "p@c.us"));
        Assert.IsFalse(ChatAudienceStore.AllowsAssistant(Wa, "x@c.us"), "no decision — not business");
        Assert.IsFalse(ChatAudienceStore.AllowsAssistant(Tg, "b@c.us"), "a decision belongs to its profile");
        Assert.IsFalse(ChatAudienceStore.AllowsAssistant(Wa, null));

        ChatAudienceStore.ApplyDesire(Wa, "p@c.us", AudienceKind.Business);
        Assert.IsTrue(ChatAudienceStore.AllowsAssistant(Wa, "p@c.us"), "a move to business counts at once");
        ChatAudienceStore.ApplyDesire(Wa, "b@c.us", AudienceKind.Personal);
        Assert.IsFalse(ChatAudienceStore.AllowsAssistant(Wa, "b@c.us"), "…and so does a move to personal");

        ChatAudienceStore.RetractDesire(Wa, "b@c.us");
        Assert.IsTrue(ChatAudienceStore.AllowsAssistant(Wa, "b@c.us"), "a move the server refused goes back: the chat is where it was");
        Assert.IsFalse(ChatAudienceStore.ViewOf(Wa, WhatsappChat("b@c.us", AfterSec)).Pending);
    }

    [Test]
    public void Store_ForgetProfile()
    {
        ChatAudienceStore.ApplyList(new[]
        {
            Profile(Wa, MomentMs, Decision("a@c.us", AudienceKind.Business, AudienceReason.New)),
            Profile(Tg, MomentMs + 1, Decision("555", AudienceKind.Personal, AudienceReason.Contact)),
        });
        ChatAudienceStore.ApplyDesire(Wa, "x@c.us", AudienceKind.Business);
        ChatAudienceStore.NoteLastMessage(Wa, "h@c.us", BeforeSec);
        ChatAudienceStore.NoteLastMessage(Tg, "556", BeforeSec);

        ChatAudienceStore.ForgetProfile(Wa);
        Assert.IsNull(ChatAudienceStore.KindOf(Wa, "a@c.us"), "the profile's decisions go");
        Assert.IsNull(ChatAudienceStore.KindOf(Wa, "x@c.us"), "…and its moves");
        Assert.IsNull(ChatAudienceStore.ConnectedAtMs(Wa), "…and its moment");
        Assert.AreEqual(AudienceKind.Personal, ChatAudienceStore.KindOf(Tg, "555"), "another profile keeps its own");

        ChatAudienceStore.DropMemoryForTests();   // a restart: what the cache file still holds
        Assert.IsNull(ChatAudienceStore.KindOf(Wa, "a@c.us"), "the cache forgot it too");
        Assert.AreEqual(MomentMs + 1, ChatAudienceStore.ConnectedAtMs(Tg));
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
        Assert.AreEqual(AudienceReason.New, ChatAudienceStore.ViewOf(Wa, WhatsappChat("h@c.us", AfterSec)).Reason,
            "…and the times the app saw for its chats");
        Assert.AreEqual(AudienceReason.History, ChatAudienceStore.ViewOf(Tg, TelegramChat("556", AfterSec)).Reason,
            "another profile keeps those too");
    }

    [Test]
    public void Store_TheServersViewSurvivesARestart_TheMovesDoNot()
    {
        ChatAudienceStore.ApplyList(new[] { Profile(Wa, MomentMs,
            Decision("a@c.us", AudienceKind.Business, AudienceReason.Owner, true, 42),
            Decision("h@c.us", AudienceKind.Personal, AudienceReason.History)) });
        ChatAudienceStore.ApplyDesire(Wa, "m@c.us", AudienceKind.Business);
        Assert.IsTrue(File.Exists(_cache), "the server's view is written down");
        StringAssert.DoesNotContain("m@c.us", File.ReadAllText(_cache), "a move is the queue's to remember, not the cache's");

        ChatAudienceStore.DropMemoryForTests();
        Assert.AreEqual(MomentMs, ChatAudienceStore.ConnectedAtMs(Wa));
        Assert.AreEqual(AudienceKind.Business, ChatAudienceStore.KindOf(Wa, "a@c.us"));
        var history = ChatAudienceStore.ViewOf(Wa, WhatsappChat("h@c.us", AfterSec));
        Assert.AreEqual(AudienceReason.History, history.Reason, "the reason survives too");
        Assert.IsTrue(history.Decided);
        Assert.IsNull(ChatAudienceStore.KindOf(Wa, "m@c.us"), "the queue redraws its moves on launch");

        File.WriteAllText(_cache, "{not json");
        ChatAudienceStore.DropMemoryForTests();
        Assert.IsNull(ChatAudienceStore.ConnectedAtMs(Wa), "an unreadable cache is an empty mirror, never a crash");
    }

    [Test]
    public void Store_AListReplacesOnlyTheProfilesItNames()
    {
        ChatAudienceStore.ApplyList(new[]
        {
            Profile(Wa, MomentMs, Decision("a@c.us", AudienceKind.Business, AudienceReason.New)),
            Profile(Tg, MomentMs, Decision("555", AudienceKind.Personal, AudienceReason.Contact)),
        });
        ChatAudienceStore.NoteLastMessage(Wa, "s@c.us", BeforeSec);
        ChatAudienceStore.ApplyList(new[] { Profile(Wa, MomentMs + 60_000, Decision("b@c.us", AudienceKind.Personal, AudienceReason.History)) });
        Assert.IsNull(ChatAudienceStore.KindOf(Wa, "a@c.us"), "the listed profile is replaced: a decision that no longer holds goes");
        Assert.AreEqual(AudienceKind.Personal, ChatAudienceStore.KindOf(Wa, "b@c.us"));
        Assert.AreEqual(MomentMs + 60_000, ChatAudienceStore.ConnectedAtMs(Wa), "another number: a new moment");
        Assert.AreEqual(AudienceKind.Personal, ChatAudienceStore.KindOf(Tg, "555"), "a profile the list did not name is kept");
        Assert.AreEqual(AudienceReason.History, ChatAudienceStore.ViewOf(Wa, WhatsappChat("s@c.us", AfterSec)).Reason,
            "what the app saw is its own, not the server's view: a list answer keeps it");

        ChatAudienceStore.ApplyList(new[] { Profile(Wa, null) });
        Assert.IsNull(ChatAudienceStore.ConnectedAtMs(Wa), "the server knows no moment for it now");
    }

    [Test]
    public void Store_NotApplied_AdoptsTheServersKind()
    {
        ChatAudienceStore.ApplyList(new[] { Profile(Wa, MomentMs, Decision("a@c.us", AudienceKind.Business, AudienceReason.New)) });
        ChatAudienceStore.ApplyDesire(Wa, "a@c.us", AudienceKind.Personal);
        ChatAudienceStore.ApplyDesireResult(Wa, "a@c.us", AudienceKind.Business, applied: false);
        var view = ChatAudienceStore.ViewOf(Wa, WhatsappChat("a@c.us", AfterSec));
        Assert.AreEqual(AudienceKind.Business, view.Kind, "the server holds a newer decision: its view is adopted");
        Assert.IsFalse(view.Pending);
        Assert.AreEqual(AudienceReason.New, view.Reason, "the server kept the decision the app already showed");

        ChatAudienceStore.ApplyDesire(Wa, "n@c.us", AudienceKind.Business);
        ChatAudienceStore.ApplyDesireResult(Wa, "n@c.us", AudienceKind.Personal, applied: false);
        Assert.AreEqual(AudienceKind.Personal, ChatAudienceStore.KindOf(Wa, "n@c.us"), "a decision the app did not know is taken as it is");
    }

    [Test]
    public void Store_TellsOncePerChange()
    {
        int changes = 0;
        ChatAudienceStore.OnChanged += () => changes++;

        ChatAudienceStore.ApplyList(new[] { Profile(Wa, MomentMs, Decision("a@c.us", AudienceKind.Business, AudienceReason.New)) });
        Assert.AreEqual(1, changes);
        ChatAudienceStore.ApplyList(new[] { Profile(Wa, MomentMs, Decision("a@c.us", AudienceKind.Business, AudienceReason.New)) });
        Assert.AreEqual(1, changes, "the same answer again changes nothing — no repaint");
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
        Assert.AreEqual(1, changes);
        ChatAudienceStore.ApplyDesire(Wa, "a@c.us", AudienceKind.Personal);
        ChatAudienceStore.ApplyDesire(Wa, "a@c.us", AudienceKind.Personal);
        Assert.AreEqual(2, changes, "a move is told once");

        ChatAudienceStore.Batch(() =>
        {
            ChatAudienceStore.ApplyDesire(Wa, "b@c.us", AudienceKind.Business);
            ChatAudienceStore.ApplyDesire(Wa, "c@c.us", AudienceKind.Business);
            ChatAudienceStore.RetractDesire(Wa, "a@c.us");
        });
        Assert.AreEqual(3, changes, "many chats moved at once are told once");
        ChatAudienceStore.Batch(() => ChatAudienceStore.RetractDesire(Wa, "zzz@c.us"));
        Assert.AreEqual(3, changes, "a batch that changed nothing tells nothing");
        ChatAudienceStore.NoteLastMessage(Wa, "n@c.us", BeforeSec);
        Assert.AreEqual(3, changes, "noting what the list showed tells nothing — the view already reads the chat's own last message");

        ChatAudienceStore.ClearAll();
        Assert.AreEqual(4, changes);
        Assert.IsFalse(File.Exists(_cache), "the full wipe deletes the cache file");
        Assert.IsNull(ChatAudienceStore.KindOf(Wa, "b@c.us"));
        Assert.IsNull(ChatAudienceStore.ConnectedAtMs(Wa));
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
        Assert.AreEqual(AudienceReason.New, ChatAudienceStore.ViewOf(Wa, WhatsappChat("n@c.us", AfterSec)).Reason,
            "…and what the app saw");
    }
}

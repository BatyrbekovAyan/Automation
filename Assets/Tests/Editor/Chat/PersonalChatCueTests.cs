using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using static OpenChatFixture;

/// <summary>
/// «Личные и рабочие чаты»: what an incoming message does on the REAL ChatManager.ParseChatsJson (driven by reflection on a
/// bare manager — OpenChatFixture — with a bot whose WhatsApp profile the audience store knows and has a connection moment
/// for).
///   • The cue. The app judges an undecided chat's history by the EARLIEST last message it has seen for the chat (kept by
///     ChatAudienceStore, across restarts), so a conversation from before the connection stays personal and does not ring
///     when its person writes again — the server decides the chat on that message by its history. On a merged row the
///     chat's verdict BEFORE the message counts too (the row's facts may change with it).
///   • The read. An incoming message in a person's chat the app holds no verdict for (no server decision mirrored, no owner's
///     move on its way) asks ChatAudienceSyncService for a list read soon — once per pass, never for the cache load, our own
///     message, a decided chat or a non-person (a group or a channel is never decided). The open chat asks too, for a message
///     from the other side among those its sync finds (ChatManager.AskVerdictForOpenChat), and — the server writes its
///     decision some seconds after the message — asks again 7 and 15 seconds later while the chat stays open and undecided
///     (OpenChatVerdictWatch, looked at on every tick of the live poll): at most three reads per message, none once the
///     verdict is mirrored, the chat closes or another chat or bot is open. A chat the owner began gets its verdict, the
///     «Авто» chip and the panel while it stays open. (The service's one-timer coalescing is pinned in
///     IncomingNotifyPolicyTests.)
/// The cue and the request are observed through NotificationFx.IncomingDetected and ChatAudienceSyncService.ListSoonRequested.
/// </summary>
public class PersonalChatCueTests
{
    private const string TestBot = "PersonalChatCueTestBot";
    private const string Profile = "cue-wa-profile";
    private const string Before = "2026-10-01T10:00:00Z";   // before the connection
    private const string After = "2026-10-21T10:00:00Z";    // after it
    private const string Later = "2026-10-21T10:05:00Z";
    private static readonly long MomentMs = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private readonly List<GameObject> hosts = new List<GameObject>();
    private string dir;
    private Manager previousManager;
    private int cues;
    private int readRequests;

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "PersonalChatCueTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ChatAudienceStore.UseCacheFileForTests(Path.Combine(dir, ChatAudienceStore.CacheFileName));
        ChatAudienceStore.ApplyConnected(Profile, MomentMs);

        previousManager = Manager.Instance;
        Manager.Instance = ManagerWithBot(TestBot, Profile, hosts);

        cues = 0;
        readRequests = 0;
        NotificationFx.IncomingDetected += CountCue;
        ChatAudienceSyncService.ListSoonRequested += CountRead;
    }

    [TearDown]
    public void TearDown()
    {
        NotificationFx.IncomingDetected -= CountCue;
        ChatAudienceSyncService.ListSoonRequested -= CountRead;
        Manager.Instance = previousManager;
        foreach (GameObject host in hosts)
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
        hosts.Clear();
        ChatAudienceStore.ResetForTests();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private void CountCue() => cues++;
    private void CountRead() => readRequests++;

    // ---- the cue ----

    [Test]
    public void AConversationFromBeforeTheConnection_StaysPersonal_AndQuiet_AsItsPersonWritesAgain()
    {
        const string chat = "77000000001@c.us";
        ChatManager manager = NewChatManager();
        Parse(manager, List(Row(chat, "M1", Before, unread: 0)), isInitialLoad: true);
        AudienceView before = View(manager, chat);
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.History), (before.Kind, before.Reason),
            "a non-contact the owner wrote with before the connection");

        Parse(manager, List(Row(chat, "M2", After, unread: 1)), isInitialLoad: false);
        AudienceView first = View(manager, chat);
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.History), (first.Kind, first.Reason),
            "its first message after the connection: the app judges the chat by the earliest last message it saw");

        Parse(manager, List(Row(chat, "M3", Later, unread: 2)), isInitialLoad: false);
        Assert.AreEqual(AudienceKind.Personal, View(manager, chat).Kind, "…and so on every later one");

        Assert.AreEqual(0, cues, "the server decides personal/history on these messages — the app stays quiet throughout");
        Assert.AreEqual(2, readRequests, "…and asks for the server's verdict soon, on each pass while it holds none");
    }

    [Test]
    public void TheEarliestLastMessage_OutlivesARestart()
    {
        const string chat = "77000000010@c.us";
        Parse(NewChatManager(), List(Row(chat, "R1", Before, unread: 0)), isInitialLoad: true);

        ChatAudienceStore.DropMemoryForTests();   // the app restarts: the mirror and what it saw come back from the cache file
        ChatManager restarted = NewChatManager();
        Parse(restarted, List(Row(chat, "R2", After, unread: 1)), isInitialLoad: true);   // the list cached after the person wrote
        AudienceView view = View(restarted, chat);
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.History), (view.Kind, view.Reason),
            "the time the app saw before the restart still counts");

        Parse(restarted, List(Row(chat, "R3", Later, unread: 2)), isInitialLoad: false);
        Assert.AreEqual(0, cues, "a conversation from before the connection does not ring after a restart either");
    }

    [Test]
    public void ANewPersonsNextMessage_Rings()
    {
        const string chat = "77000000002@c.us";
        ChatManager manager = NewChatManager();
        Parse(manager, List(Row(chat, "N1", After, unread: 0)), isInitialLoad: true);
        Assert.AreEqual(AudienceKind.Business, View(manager, chat).Kind, "a new person — the chat began after the connection");

        Parse(manager, List(Row(chat, "N2", Later, unread: 1)), isInitialLoad: false);

        Assert.AreEqual(1, cues, "working before the message and after it — the cue plays");
        Assert.AreEqual(1, readRequests, "still no verdict from the server in the app — read soon");
    }

    [Test]
    public void AChatTheAppHoldsAVerdictFor_RingsByIt_AndAsksNoRead()
    {
        const string client = "77000000003@c.us";   // an old conversation the owner made working
        const string friend = "77000000004@c.us";   // a new person the owner made personal
        const string moving = "77000000005@c.us";   // a new person the owner is moving away right now
        ChatAudienceStore.ApplyList(new[]
        {
            new AudienceProfileState
            {
                ProfileId = Profile, ConnectedAtMs = MomentMs,
                Decisions = new List<AudienceDecision>
                {
                    new AudienceDecision { ChatId = client, Kind = AudienceKind.Business, Reason = AudienceReason.Owner, ByOwner = true, Version = 1 },
                    new AudienceDecision { ChatId = friend, Kind = AudienceKind.Personal, Reason = AudienceReason.Owner, ByOwner = true, Version = 2 },
                },
            },
        });
        ChatAudienceStore.ApplyDesire(Profile, moving, AudienceKind.Personal);
        ChatManager manager = NewChatManager();
        Parse(manager, List(Row(client, "C1", Before, 0), Row(friend, "F1", After, 0), Row(moving, "V1", After, 0)), isInitialLoad: true);

        Parse(manager, List(Row(client, "C2", After, 1), Row(friend, "F1", After, 0), Row(moving, "V1", After, 0)), isInitialLoad: false);
        Assert.AreEqual(1, cues, "the owner's client rings, whatever its history");

        Parse(manager, List(Row(client, "C2", After, 1), Row(friend, "F2", Later, 1), Row(moving, "V2", Later, 1)), isInitialLoad: false);
        Assert.AreEqual(1, cues, "a chat decided personal — or being moved there — stays quiet");
        Assert.AreEqual(0, readRequests, "the app already holds a verdict for every one of them");
    }

    // ---- the read ----

    [Test]
    public void TheCacheLoad_AndOurOwnMessages_AskNoRead()
    {
        const string chat = "77000000006@c.us";
        ChatManager manager = NewChatManager();
        Parse(manager, List(Row(chat, "S1", After, unread: 1)), isInitialLoad: true);
        Assert.AreEqual(0, readRequests, "the cache load is no incoming message");

        Parse(manager, List(Row(chat, "S2", Later, unread: 0, mine: true)), isInitialLoad: false);
        Assert.AreEqual(0, readRequests, "the owner's own message decides nothing on the server");
        Assert.AreEqual(0, cues);
    }

    [Test]
    public void AGroupOrAChannel_AsksNoRead_TheServerNeverDecidesThem()
    {
        const string group = "120363000000000002@g.us";
        const string channel = "120363000000000003@newsletter";
        ChatManager manager = NewChatManager();
        Parse(manager, List(Row(group, "G1", After, 0, isGroup: true), Row(channel, "K1", After, 0)), isInitialLoad: true);

        Parse(manager, List(Row(group, "G2", Later, 1, isGroup: true), Row(channel, "K2", Later, 1)), isInitialLoad: false);

        Assert.AreEqual(0, readRequests, "no verdict will ever come for a non-person — asking would read the list on every pass");
        Assert.AreEqual(0, cues, "…and a non-person is personal: no cue either");
    }

    // ---- the open chat ----

    [Test]
    public void AMessageFromTheOtherSide_InTheOpenUndecidedChat_AsksForTheServersVerdict()
    {
        const string lead = "77000000020@c.us";      // the owner wrote first; the person has just replied, the chat open
        const string decided = "77000000021@c.us";   // the server's decision is mirrored
        const string moving = "77000000022@c.us";    // the owner's move on its way
        const string group = "120363000000000004@g.us";
        ChatAudienceStore.ApplyList(new[]
        {
            new AudienceProfileState
            {
                ProfileId = Profile, ConnectedAtMs = MomentMs,
                Decisions = new List<AudienceDecision>
                {
                    new AudienceDecision { ChatId = decided, Kind = AudienceKind.Business, Reason = AudienceReason.New, Version = 0 },
                },
            },
        });
        ChatAudienceStore.ApplyDesire(Profile, moving, AudienceKind.Business);
        ChatManager manager = NewChatManager();
        Parse(manager, List(Row(lead, "L1", After, 0, mine: true), Row(decided, "D1", After, 0), Row(moving, "V1", After, 0),
            Row(group, "G1", After, 0, isGroup: true)), isInitialLoad: true);

        Arrived(manager, lead, Message("L2", incoming: false));
        Assert.AreEqual(0, readRequests, "the owner's own message decides nothing on the server");
        Arrived(manager, lead, Message("L3", incoming: false), Message("L4", incoming: true));
        Assert.AreEqual(1, readRequests, "the person replied in the open chat: the server decides it on that message — read soon");

        Arrived(manager, decided, Message("D2", incoming: true));
        Arrived(manager, moving, Message("V2", incoming: true));
        Arrived(manager, group, Message("G2", incoming: true));
        Arrived(manager, "77000000029@c.us", Message("X1", incoming: true));
        Arrived(manager, lead);
        Assert.AreEqual(1, readRequests,
            "a decided chat, a move on its way, a non-person, a chat the list does not hold and nothing new ask nothing");
    }

    [Test]
    public void TheOpenChat_AsksAgainWhileUndecided_AtMostThreeTimes_AndStopsOnTheVerdict()
    {
        const string lead = "77000000030@c.us";      // the owner began, the person replies while the chat is open
        ChatManager manager = NewChatManager();
        Parse(manager, List(Row(lead, "L1", After, 0, mine: true)), isInitialLoad: true);
        OpenChat(manager, lead);

        ArrivedAt(manager, lead, 100f, Message("L2", incoming: true));
        Assert.AreEqual(1, readRequests, "the message's own ask");
        Tick(manager, true, 103f);
        Tick(manager, true, 106f);
        Assert.AreEqual(1, readRequests, "nothing more before seven seconds");
        Tick(manager, true, 107f);
        Assert.AreEqual(2, readRequests, "still undecided seven seconds after the message: asked again");
        Tick(manager, true, 115f);
        Assert.AreEqual(3, readRequests, "and fifteen seconds after it");
        Tick(manager, true, 123f);
        Tick(manager, true, 140f);
        Assert.AreEqual(3, readRequests, "never more than three reads for one message");

        ArrivedAt(manager, lead, 200f, Message("L3", incoming: true));
        Assert.AreEqual(4, readRequests, "a new message from the other side starts the schedule over");
        ChatAudienceStore.ApplyList(new[]
        {
            new AudienceProfileState
            {
                ProfileId = Profile, ConnectedAtMs = MomentMs,
                Decisions = new List<AudienceDecision>
                {
                    new AudienceDecision { ChatId = lead, Kind = AudienceKind.Business, Reason = AudienceReason.New, Version = 0 },
                },
            },
        });
        Tick(manager, true, 207f);
        Tick(manager, true, 215f);
        Assert.AreEqual(4, readRequests, "the verdict is mirrored: nothing more is asked");
    }

    [TestCase("closed", TestName = "TheOpenChatsAsks_Stop_WhenTheChatCloses")]
    [TestCase("another chat", TestName = "TheOpenChatsAsks_Stop_WhenAnotherChatOpens")]
    [TestCase("another bot", TestName = "TheOpenChatsAsks_Stop_WhenTheBotSwitches")]
    public void TheOpenChatsAsks_StopAtOnce(string what)
    {
        const string lead = "77000000031@c.us";
        ChatManager manager = NewChatManager();
        Parse(manager, List(Row(lead, "L1", After, 0, mine: true), Row("77000000032@c.us", "O1", After, 0)), isInitialLoad: true);
        OpenChat(manager, lead);
        ArrivedAt(manager, lead, 100f, Message("L2", incoming: true));
        Assert.AreEqual(1, readRequests, "premise: the message's own ask");

        bool open = true;
        if (what == "closed") open = false;
        else if (what == "another chat") OpenChat(manager, "77000000032@c.us");
        else typeof(ChatManager).GetProperty(nameof(ChatManager.CurrentBotId)).SetValue(manager, "AnotherBotWithoutTheProfile");
        Tick(manager, open, 107f);
        if (what == "another chat") OpenChat(manager, lead);                 // back to the chat: the watch stays stopped
        if (what == "another bot") typeof(ChatManager).GetProperty(nameof(ChatManager.CurrentBotId)).SetValue(manager, TestBot);
        Tick(manager, true, 115f);
        Assert.AreEqual(1, readRequests, $"{what}: nothing more is asked");
    }

    [Test]
    public void TheLivePoll_LooksEverySecond_AndEverySwitchStopsTheAsks()
    {
        string routine = SourceGuard.MethodBody(SourceGuard.Read("Scripts/Main/ChatManager.LivePoll.cs"), "private IEnumerator OpenChatLivePollRoutine()");
        int tick = routine.IndexOf("TickOpenChatVerdictWatch(chatIsOpen, Time.realtimeSinceStartup);", StringComparison.Ordinal);
        int cacheGuard = routine.IndexOf("if (_activeChatCache == null) continue;", StringComparison.Ordinal);
        int gate = routine.IndexOf("OpenChatLivePollGate.ShouldIssue(", StringComparison.Ordinal);
        Assert.IsTrue(tick >= 0 && tick < cacheGuard && tick < gate,
            "the open chat's verdict asks are looked at on every tick, whatever the poll's own gate says");
        foreach ((string file, string method) in new[]
                 {
                     ("Scripts/Main/ChatManager.BotState.cs", "public void SetActiveBot("),
                     ("Scripts/Main/ChatManager.Channel.cs", "public void SetActiveChannel("),
                     ("Scripts/Main/ChatManager.PrivacyClear.cs", "public void ClearAllLocalHistory("),
                 })
        {
            StringAssert.Contains("_verdictWatch.Stop();", SourceGuard.MethodBody(SourceGuard.Read(file), method),
                $"{method}: the asks of the chat left behind stop");
        }
    }

    [Test]
    public void TheOpenChatsSync_AsksForTheVerdict_WithEveryBatchItFinds()
    {
        string sync = SourceGuard.MethodBody(SourceGuard.Read("Scripts/Main/ChatManager.cs"), "IEnumerator SyncLatestMessages(");
        int found = sync.IndexOf("if (newMessages.Count > 0)", StringComparison.Ordinal);
        int asked = sync.IndexOf("AskVerdictForOpenChat(chatId, brandNew);", StringComparison.Ordinal);
        int settled = sync.IndexOf("bool isSettled", StringComparison.Ordinal);
        Assert.IsTrue(found >= 0 && asked > found && settled > asked,
            "the messages new to the open chat ask for the verdict as soon as they are found — shown now or queued for the slide");
    }

    [Test]
    public void ABurstOfUndecidedChats_AsksOnceAPass()
    {
        ChatManager manager = NewChatManager();
        Parse(manager, List(Row("77000000007@c.us", "A1", After, 0), Row("77000000008@c.us", "B1", After, 0),
            Row("77000000009@c.us", "C1", After, 0)), isInitialLoad: true);

        Parse(manager, List(Row("77000000007@c.us", "A2", Later, 1), Row("77000000008@c.us", "B2", Later, 1),
            Row("77000000009@c.us", "C2", Later, 1)), isInitialLoad: false);

        Assert.AreEqual(1, readRequests, "one request for the whole pass");
        Assert.AreEqual(1, cues, "one cue per pass, as before");
    }

    // ---- the harness (the Manager with the bot, the bare ChatManager and Parse are OpenChatFixture's) ----

    // A real ChatManager on the test bot's WhatsApp channel.
    private ChatManager NewChatManager() => BareChatManager(TestBot, hosts);

    private static AudienceView View(ChatManager manager, string chatId)
    {
        ChatViewModel vm = manager.GetChat(chatId);
        Assert.IsNotNull(vm, $"no row for {chatId}");
        return ChatAudienceStore.ViewOf(Profile, vm);
    }

    // Messages new to the open chat, as its sync hands them on (ChatManager.AskVerdictForOpenChat).
    private static void Arrived(ChatManager manager, string chatId, params MessageViewModel[] messages)
    {
        MethodInfo ask = typeof(ChatManager).GetMethod("AskVerdictForOpenChat", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(ask, "ChatManager.AskVerdictForOpenChat moved — the open chat's new messages no longer ask for a verdict");
        Call(() => ask.Invoke(manager, new object[] { chatId, new List<MessageViewModel>(messages) }));
    }

    // The same at the clock reading `now` (ChatManager.AskVerdictForOpenChatAt — the live path hands in the real clock).
    private static void ArrivedAt(ChatManager manager, string chatId, float now, params MessageViewModel[] messages)
    {
        MethodInfo ask = typeof(ChatManager).GetMethod("AskVerdictForOpenChatAt", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(ask, "ChatManager.AskVerdictForOpenChatAt moved — the open chat's new messages no longer start the asks");
        Call(() => ask.Invoke(manager, new object[] { chatId, new List<MessageViewModel>(messages), now }));
    }

    // One tick of the open chat's live poll for the verdict asks (ChatManager.TickOpenChatVerdictWatch).
    private static void Tick(ChatManager manager, bool chatIsOpen, float now)
    {
        MethodInfo tick = typeof(ChatManager).GetMethod("TickOpenChatVerdictWatch", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(tick, "ChatManager.TickOpenChatVerdictWatch moved — the open chat no longer asks again");
        Call(() => tick.Invoke(manager, new object[] { chatIsOpen, now }));
    }

    private static MessageViewModel Message(string id, bool incoming) =>
        new MessageViewModel { messageId = id, type = MessageType.Chat, text = "text " + id, isIncoming = incoming };

    // A WhatsApp chats/filter row of a person who is not a saved contact (or of a group).
    private static object Row(string id, string lastId, string time, int unread, bool mine = false, bool isGroup = false) => new
    {
        id,
        name = "Chat " + id,
        contact = new { Found = true, FullName = "" },
        isGroup,
        unread_count = unread,
        last_message_id = lastId,
        last_message_data = "text " + lastId,
        last_timestamp = time,
        last_message_sender = new { isMe = mine, pushname = "" },
    };

    private static string List(params object[] dialogs) => JsonConvert.SerializeObject(new { status = "done", dialogs });
}

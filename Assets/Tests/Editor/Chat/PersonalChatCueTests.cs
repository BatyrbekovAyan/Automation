using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;
using static OpenChatFixture;

/// <summary>
/// «Личные и рабочие чаты»: what an incoming message does on the REAL ChatManager.ParseChatsJson (driven by reflection on a
/// bare manager — OpenChatFixture — with a bot whose WhatsApp profile the audience store knows and has a connection moment
/// for).
///   • The cue. On a merged row the chat's verdict BEFORE the message counts too: the app judges an undecided chat's
///     history by its LAST message, and the message itself moves that past the connection moment — while the server decides
///     the chat on that very message by its history. So a conversation from before the connection does not ring.
///   • The read. An incoming message in a chat the app holds no verdict for (no server decision mirrored, no owner's move on
///     its way) asks ChatAudienceSyncService for a list read soon — once per pass, never for the cache load, our own
///     message or a decided chat. (The service's one-timer coalescing is pinned in IncomingNotifyPolicyTests.)
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
    public void AConversationFromBeforeTheConnection_DoesNotRing_OnItsFirstMessageAfterIt()
    {
        const string chat = "77000000001@c.us";
        ChatManager manager = NewChatManager();
        Parse(manager, List(Row(chat, "M1", Before, unread: 0)), isInitialLoad: true);
        AudienceView before = View(manager, chat);
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.History), (before.Kind, before.Reason),
            "a non-contact the owner wrote with before the connection");

        Parse(manager, List(Row(chat, "M2", After, unread: 1)), isInitialLoad: false);

        Assert.AreEqual(AudienceKind.Business, View(manager, chat).Kind,
            "the app's estimate now reads the new last message as after the connection — why the verdict before it counts");
        Assert.AreEqual(0, cues, "the server decides personal/history on this very message — the app stays quiet");
        Assert.AreEqual(1, readRequests, "…and asks for the server's verdict soon");
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

    // A WhatsApp chats/filter row of a person who is not a saved contact.
    private static object Row(string id, string lastId, string time, int unread, bool mine = false) => new
    {
        id,
        name = "Chat " + id,
        contact = new { Found = true, FullName = "" },
        isGroup = false,
        unread_count = unread,
        last_message_id = lastId,
        last_message_data = "text " + lastId,
        last_timestamp = time,
        last_message_sender = new { isMe = mine, pushname = "" },
    };

    private static string List(params object[] dialogs) => JsonConvert.SerializeObject(new { status = "done", dialogs });
}

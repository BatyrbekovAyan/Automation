using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.7, sketch 009 screen 7): the number on the bot settings' «Личные чаты» row — the chats the
/// bot's «Личные» folder lists in Browse (every personal chat, the ones that are not people included), on BOTH channels.
/// <see cref="PersonalChatsCount"/> reads BotChatStats' source: each channel's cached list under the list's hide rules, every
/// chat drawn by <see cref="ChatAudienceStore.ViewOf"/> under the channel's profile — except the channel the chats screen shows
/// for the active bot, whose live list is fresher and is exactly what the folder lists. A channel without a live profile counts
/// nothing (the folder has nothing to open there). The caches are written under a temporary root
/// (BotChatStats.UseCacheRootForTests) — nothing here touches persistentDataPath.
/// </summary>
public class PersonalChatsCountTests
{
    private const string BotName = "PersonalChatsCountTestBot";
    private const string Wa = "count-wa-profile";
    private const string Tg = "count-tg-profile";
    private const string After = "2026-10-21T10:00:00Z";    // a chat that began after the connection
    private const string Before = "2026-10-01T10:00:00Z";   // a conversation from before it
    private static readonly long MomentMs = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static readonly long AfterSec = new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private readonly List<GameObject> hosts = new List<GameObject>();
    private string dir;
    private Manager previousManager;
    private ChatManager previousChatManager;
    private Bot bot;

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "PersonalChatsCountTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ChatAudienceStore.UseCacheFileForTests(Path.Combine(dir, ChatAudienceStore.CacheFileName));
        BotChatStats.UseCacheRootForTests(Path.Combine(dir, "BotCache"));
        previousManager = Manager.Instance;
        previousChatManager = ChatManager.Instance;

        Manager manager = OpenChatFixture.ManagerWithBot(BotName, Wa, hosts);
        bot = manager.FindBotByName(BotName);
        bot.telegramProfileId = Tg;
        Manager.Instance = manager;
        ChatManager.Instance = null;   // no chats screen: both channels come from their caches
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
        ChatAudienceStore.ApplyConnected(Tg, MomentMs);
    }

    [TearDown]
    public void TearDown()
    {
        Manager.Instance = previousManager;
        ChatManager.Instance = previousChatManager;
        foreach (GameObject host in hosts)
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
        hosts.Clear();
        BotChatStats.ResetForTests();
        ChatAudienceStore.ResetForTests();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Test]
    public void Count_SumsBothChannels_SkipsWorking()
    {
        ChatAudienceStore.ApplyList(new[]
        {
            new AudienceProfileState
            {
                ProfileId = Wa, ConnectedAtMs = MomentMs,
                Decisions = new List<AudienceDecision>
                {
                    // a saved contact the owner marked as a client
                    new AudienceDecision { ChatId = "77000000004@c.us", Kind = AudienceKind.Business, Reason = AudienceReason.Owner, ByOwner = true, Version = 3 },
                },
            },
        });
        WriteCache(ChatChannel.WhatsApp,
            WaRow("77000000001@c.us", fullName: "Мама", time: After),            // a contact — personal
            WaRow("77000000002@c.us", fullName: "", time: After),                // a new person — working
            WaRow("120363000000000003@g.us", fullName: "", time: After),         // a group — personal
            WaRow("77000000004@c.us", fullName: "Асем клиент", time: Before),    // the owner's client — working
            WaRow("77000000005@c.us", fullName: "", time: Before),               // an old conversation — personal
            WaRow("77000000006@c.us", fullName: "Друг", time: After, deleted: true));   // deleted — no chat at all
        WriteCache(ChatChannel.Telegram,
            TgRow("5550000001", contact: false, isBot: false),   // a new person — working
            TgRow("5550000002", contact: true, isBot: false),    // a contact — personal
            TgRow("5550000003", contact: false, isBot: true),    // a bot — personal
            TgRow("777000", contact: false, isBot: false));      // the service account — personal

        Assert.AreEqual(3 + 3, PersonalChatsCount.Read(BotName),
            "WhatsApp's contact, group and old conversation + Telegram's contact, bot and service account; never a working chat");
    }

    [Test]
    public void Count_TheChannelOnTheChatsScreen_ReadsTheLiveList()
    {
        WriteCache(ChatChannel.WhatsApp,
            WaRow("77000000001@c.us", fullName: "Мама", time: After),
            WaRow("120363000000000003@g.us", fullName: "", time: After),
            WaRow("77000000005@c.us", fullName: "", time: Before));
        WriteCache(ChatChannel.Telegram,
            TgRow("5550000002", contact: true, isBot: false),
            TgRow("5550000003", contact: false, isBot: true));

        ChatManager chats = OpenChatFixture.BareChatManager(BotName, hosts);   // the active bot, on WhatsApp
        ChatManager.Instance = chats;
        chats.Chats.Add(LiveChat("77000000001@c.us", contact: true));    // personal
        chats.Chats.Add(LiveChat("77000000002@c.us", contact: false));   // a new person — working
        chats.Chats.Add(LiveChat("77000000007@c.us", contact: false));   // another new person — working

        Assert.AreEqual(1 + 2, PersonalChatsCount.Read(BotName),
            "WhatsApp from the list on screen (1, not the cache's 3), Telegram — off screen — from its cache");
    }

    [Test]
    public void Count_AChannelWithoutALiveProfile_CountsNothing()
    {
        bot.telegramProfileId = Bot.UnauthedProfileSentinel;   // Telegram never connected, or retired
        WriteCache(ChatChannel.WhatsApp, WaRow("77000000001@c.us", fullName: "Мама", time: After));
        WriteCache(ChatChannel.Telegram,
            TgRow("5550000002", contact: true, isBot: false),
            TgRow("5550000003", contact: false, isBot: true));

        Assert.AreEqual(1, PersonalChatsCount.Read(BotName), "the folder has nothing to open on a channel without a profile");
    }

    [Test]
    public void Count_AnOwnersMoveOnItsWay_CountsWhereTheOwnerPutTheChat()
    {
        WriteCache(ChatChannel.WhatsApp,
            WaRow("77000000001@c.us", fullName: "Мама", time: After),   // a contact, moved to work
            WaRow("77000000002@c.us", fullName: "", time: After));      // a new person, moved away
        ChatAudienceStore.ApplyDesire(Wa, "77000000001@c.us", AudienceKind.Business);
        ChatAudienceStore.ApplyDesire(Wa, "77000000002@c.us", AudienceKind.Personal);

        Assert.AreEqual(1, PersonalChatsCount.Read(BotName), "the owner's move is drawn at once, as the folder draws it");
    }

    [Test]
    public void Tally_KeepsTheListsHideRules()
    {
        // Telegram: the service dialog in its two id-forms is one chat (ParseChatsJson merges the twin into one row).
        ChatsResponse telegram = Parse(TgRow("777000", contact: false, isBot: false), TgRow("777000@c.us", contact: false, isBot: false));
        Assert.AreEqual(1, PersonalChatsCount.Tally(telegram, ChatChannel.Telegram, Tg, ""), "one chat, one count");
        Assert.AreEqual(1, BotChatStats.Tally(telegram, ChatChannel.Telegram, Tg, "").ChatCount, "the switcher counts it once too");

        // WhatsApp: a bare id leaked from Telegram is not a WhatsApp chat; a deleted chat is no chat.
        ChatsResponse whatsapp = Parse(TgRow("777000", contact: false, isBot: false),
            WaRow("77000000006@c.us", fullName: "Друг", time: After, deleted: true));
        Assert.AreEqual(0, PersonalChatsCount.Tally(whatsapp, ChatChannel.WhatsApp, Wa, ""));
        Assert.AreEqual(0, BotChatStats.Tally(whatsapp, ChatChannel.WhatsApp, Wa, "").ChatCount);
    }

    [Test]
    public void Count_NoSuchBot_OrNothingParsed_IsZero()
    {
        Assert.AreEqual(0, PersonalChatsCount.Read(null));
        Assert.AreEqual(0, PersonalChatsCount.Read("NoSuchBot"), "a bot that does not exist holds no profile");
        Assert.AreEqual(0, PersonalChatsCount.Read(BotName), "no cache yet");
        Assert.AreEqual(0, PersonalChatsCount.Tally(null, ChatChannel.WhatsApp, Wa, ""));
        Assert.AreEqual(0, PersonalChatsCount.Live(null, Wa));
    }

    // ---- the caches and rows as Wappi sends them ----

    private void WriteCache(ChatChannel channel, params object[] dialogs)
    {
        string folder = Path.Combine(dir, "BotCache", BotName, ChannelCachePath.SubDir(channel));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "chats.json"), Json(dialogs));
    }

    private static ChatsResponse Parse(params object[] dialogs)
    {
        ChatsResponse response = JsonUtility.FromJson<ChatsResponse>(Json(dialogs));
        Assert.IsNotNull(response?.dialogs, "the list did not parse");
        return response;
    }

    private static string Json(object[] dialogs) =>
        JsonConvert.SerializeObject(new { status = "done", dialogs }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

    private static ChatViewModel LiveChat(string chatId, bool contact) =>
        new ChatViewModel(chatId, "Chat " + chatId, null, "", AfterSec, facts: new AudienceFacts(false, chatId, "", false, false, false, contact));

    private static object WaRow(string id, string fullName, string time, bool deleted = false) => new
    {
        id,
        name = "Chat " + id,
        contact = new { Found = true, FullName = fullName },
        isGroup = id.EndsWith("@g.us", StringComparison.Ordinal),
        isDeleted = deleted,
        unread_count = 0,
        last_message_id = "M-" + id,
        last_timestamp = time,
    };

    private static object TgRow(string id, bool contact, bool isBot) => new
    {
        id,
        name = "Chat " + id,
        type = "user",
        user = new { Contact = contact, Bot = isBot, Support = false, Self = false },
        unread_count = 0,
        last_message_id = "M-" + id,
        last_time = After,
    };
}

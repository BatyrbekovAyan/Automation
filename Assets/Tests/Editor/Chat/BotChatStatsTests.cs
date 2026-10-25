using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.8): the bot switcher's «N чатов · M новых» reads a bot's cached chat lists, and «новых»
/// counts the unread messages of WORKING chats only — a personal chat is muted in the app. <see cref="BotChatStats.Tally"/>
/// is the pure core over one channel's parsed list: it draws every chat the way the list does
/// (<see cref="ChatAudienceStore.ViewOf"/> — the owner's move, else the server's decision, else the shared rule) and keeps the
/// one hide rule the list has (a deleted chat is not a chat).
/// </summary>
public class BotChatStatsTests
{
    private const string Wa = "stats-wa-profile";
    private const string Tg = "stats-tg-profile";
    private static readonly long MomentMs = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private const string After = "2026-10-21T10:00:00Z";    // a chat that began after the connection
    private const string Before = "2026-10-01T10:00:00Z";   // a conversation from before it

    private string _dir;

    [SetUp]
    public void UseATestStore()
    {
        _dir = Path.Combine(Path.GetTempPath(), "BotChatStatsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ChatAudienceStore.UseCacheFileForTests(Path.Combine(_dir, ChatAudienceStore.CacheFileName));
    }

    [TearDown]
    public void RestoreTheStore()
    {
        ChatAudienceStore.ResetForTests();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public void Tally_Whatsapp_CountsEveryChat_ButOnlyWorkingUnread()
    {
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
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

        ChatsResponse list = Parse(
            WaRow("77000000001@c.us", unread: 3, fullName: "Мама", time: After),          // a contact — personal by the rule
            WaRow("77000000002@c.us", unread: 2, fullName: "", time: After),              // a new person — working
            WaRow("120363000000000003@g.us", unread: 5, fullName: "", time: After),       // a group — personal
            WaRow("77000000004@c.us", unread: 4, fullName: "Асем клиент", time: Before),  // the owner's client — working
            WaRow("77000000005@c.us", unread: 7, fullName: "", time: Before),             // an old conversation — personal
            WaRow("77000000006@c.us", unread: 9, fullName: "", time: After, deleted: true));

        BotChatStats.Stats stats = BotChatStats.Tally(list, ChatChannel.WhatsApp, Wa, ownWhatsappNumber: "");
        Assert.AreEqual(5, stats.ChatCount, "every chat the list shows — personal ones live in the folder, not nowhere");
        Assert.AreEqual(2 + 4, stats.UnreadCount, "unread messages of the new person and of the owner's client only");
    }

    [Test]
    public void Tally_Telegram_SkipsBotsAndContacts()
    {
        ChatAudienceStore.ApplyConnected(Tg, MomentMs);
        ChatsResponse list = Parse(
            TgRow("5550000001", unread: 2, contact: false, bot: false),   // a new person — working
            TgRow("5550000002", unread: 6, contact: true, bot: false),    // a contact — personal
            TgRow("5550000003", unread: 1, contact: false, bot: true),    // a bot — personal, never a client
            TgRow("777000", unread: 1, contact: false, bot: false));      // the service account — personal

        BotChatStats.Stats stats = BotChatStats.Tally(list, ChatChannel.Telegram, Tg, ownWhatsappNumber: "");
        Assert.AreEqual(4, stats.ChatCount);
        Assert.AreEqual(2, stats.UnreadCount);
    }

    [Test]
    public void Tally_AnOwnersMoveOnItsWay_CountsWhereTheOwnerPutTheChat()
    {
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);
        ChatAudienceStore.ApplyDesire(Wa, "77000000002@c.us", AudienceKind.Personal);   // the owner just moved a new person away

        ChatsResponse list = Parse(WaRow("77000000002@c.us", unread: 2, fullName: "", time: After));
        Assert.AreEqual(0, BotChatStats.Tally(list, ChatChannel.WhatsApp, Wa, "").UnreadCount,
            "the move is drawn at once — its unread leaves the counter with it");
    }

    [Test]
    public void Tally_NoConnectionMomentKnown_ReadsUndecidedChatsAsPersonal()
    {
        ChatsResponse list = Parse(WaRow("77000000002@c.us", unread: 2, fullName: "", time: After));
        Assert.AreEqual(0, BotChatStats.Tally(list, ChatChannel.WhatsApp, Wa, "").UnreadCount,
            "without the moment the history is not known, and what is not known is personal");
        Assert.AreEqual(1, BotChatStats.Tally(list, ChatChannel.WhatsApp, Wa, "").ChatCount);
    }

    [Test]
    public void Tally_NothingParsed_IsZero()
    {
        Assert.AreEqual(0, BotChatStats.Tally(null, ChatChannel.WhatsApp, Wa, "").ChatCount);
        Assert.AreEqual(0, BotChatStats.Tally(new ChatsResponse(), ChatChannel.Telegram, Tg, "").UnreadCount);
    }

    [Test]
    public void Read_AddsUpBothChannelsCaches()
    {
        const string bot = "BotChatStatsReadTestBot";
        Manager previous = Manager.Instance;
        Manager.Instance = null;   // no bot object: every chat counts, nothing is a working chat's unread
        BotChatStats.UseCacheRootForTests(Path.Combine(_dir, "BotCache"));
        try
        {
            Assert.AreEqual(0, BotChatStats.Read(bot).ChatCount, "no cache yet");
            WriteCache(bot, ChatChannel.WhatsApp, WaRow("77000000001@c.us", unread: 3, fullName: "Мама", time: After),
                WaRow("77000000002@c.us", unread: 2, fullName: "", time: After));
            Assert.AreEqual(2, BotChatStats.Read(bot).ChatCount, "WhatsApp alone — Telegram has no cache");
            WriteCache(bot, ChatChannel.Telegram, TgRow("5550000001", unread: 2, contact: false, bot: false));
            Assert.AreEqual(3, BotChatStats.Read(bot).ChatCount, "both channels' caches");
        }
        finally
        {
            BotChatStats.ResetForTests();
            Manager.Instance = previous;
        }
    }

    private void WriteCache(string bot, ChatChannel channel, params object[] dialogs)
    {
        string folder = Path.Combine(_dir, "BotCache", bot, ChannelCachePath.SubDir(channel));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "chats.json"), JsonConvert.SerializeObject(new { status = "done", dialogs },
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
    }

    // ---- rows as Wappi sends them ----

    private static ChatsResponse Parse(params object[] dialogs)
    {
        string json = JsonConvert.SerializeObject(new { status = "done", dialogs },
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        ChatsResponse response = JsonUtility.FromJson<ChatsResponse>(json);
        Assert.IsNotNull(response?.dialogs, "the list did not parse");
        return response;
    }

    private static object WaRow(string id, int unread, string fullName, string time, bool deleted = false) => new
    {
        id,
        name = "Chat " + id,
        contact = new { Found = true, FullName = fullName },
        isGroup = id.EndsWith("@g.us", StringComparison.Ordinal),
        isDeleted = deleted,
        unread_count = unread,
        last_message_id = "M-" + id,
        last_timestamp = time,
    };

    private static object TgRow(string id, int unread, bool contact, bool bot) => new
    {
        id,
        name = "Chat " + id,
        type = "user",
        user = new { Contact = contact, Bot = bot, Support = false, Self = false },
        unread_count = unread,
        last_message_id = "M-" + id,
        last_time = After,
    };
}

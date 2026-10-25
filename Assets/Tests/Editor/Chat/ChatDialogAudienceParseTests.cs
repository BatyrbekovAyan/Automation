using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты»: the facts the personal / business rule reads are taken from the chat-list row — WhatsApp's
/// address-book name (<c>contact.FullName</c>) and Telegram's user flags (<c>user.Contact/Bot/Support/Self</c> + the
/// dialog <c>type</c>). Three layers are pinned here, each on its own:
///   • the DTO: what JsonUtility makes of the server's objects (and of their absence — a missing object reads as all
///     false / empty, never null; the extra fields of the real payload are ignored);
///   • the pure builder (<see cref="ChatAudienceFacts"/>): dialog + channel + chat key + the bot's stored number → facts;
///   • the wiring: the REAL <c>ChatManager.ParseChatsJson</c> (driven by reflection on a bare manager, the way the
///     lifecycle tests drive their views) gives every new chat view-model its facts and refreshes an existing one's.
/// The chat-list cache is the server's answer verbatim (<c>chats.json</c>), so the facts need no storage of their own:
/// the cached text parses to the same facts as the live one — shown below on the real method and pinned at the source.
/// </summary>
public class ChatDialogAudienceParseTests
{
    private const string TestBot = "ChatAudienceParseTestBot";
    private const string OwnNumberKey = TestBot + "WhatsappNumber";
    private const string OwnNumber = "+7 700 000 00 01";   // as typed in the app; a chat id carries the same number as digits

    private readonly List<GameObject> hosts = new List<GameObject>();
    private string tempDir;

    [SetUp]
    [TearDown]
    public void CleanUp()
    {
        foreach (GameObject host in hosts)
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
        hosts.Clear();
        PlayerPrefs.DeleteKey(OwnNumberKey);
        if (tempDir != null && Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        tempDir = null;
    }

    // ── the DTO: what JsonUtility makes of the server's objects ───────────────────────────────────────────

    // A faithful WhatsApp chats/filter row: the contact object as the address book fills it, the rest as today.
    private const string WhatsappRow = @"{
      'status': 'done',
      'dialogs': [
        { 'id': '77000000011@c.us',
          'contact': { 'Found': true, 'FirstName': 'Алия', 'FullName': 'Алия Сатпаева', 'PushName': 'Aliya', 'BusinessName': '' },
          'chat': { 'VerifiedName': null, 'Status': '', 'PictureID': '', 'Devices': [ '77000000011.0:0@c.us' ] },
          'group': null, 'isGroup': false, 'name': 'Алия Сатпаева',
          'last_message_id': 'A1', 'last_timestamp': '2026-10-20T10:00:00Z', 'unread_count': 2 }
      ] }";

    // A faithful Telegram (tapi) chats/filter row: the user card with its many unrelated fields, a nested photo, an array
    // of key/value pairs and a 64-bit hash.
    private const string TelegramRow = @"{
      'status': 'done',
      'dialogs': [
        { 'id': '5550000011',
          'user': { 'Self': false, 'Contact': true, 'MutualContact': true, 'Deleted': false, 'Bot': false,
                    'BotChatHistory': false, 'Verified': false, 'Restricted': false, 'Support': false, 'Scam': false,
                    'Premium': true, 'ID': 5550000011, 'AccessHash': 1234567890123456789,
                    'FirstName': 'Test', 'LastName': 'User', 'Username': 'test_user', 'Phone': '',
                    'Photo': { 'HasVideo': false, 'Personal': false, 'PhotoID': 1234567890123456789,
                               'StrippedThumb': 'AQgIoMjluFI/KiiigD8=', 'DCID': 2 },
                    'Status': [ { 'Key': '', 'Value': null } ], 'Usernames': null },
          'unread_count': 0, 'isDeleted': false, 'isArchived': false, 'name': 'Test User', 'picture': '',
          'last_message_id': '287', 'last_message_data': '', 'last_timestamp': null,
          'last_time': '2026-10-20T14:42:41Z', 'type': 'user' }
      ] }";

    private static string Json(string singleQuoted) => singleQuoted.Replace('\'', '"');

    private static List<ChatDialog> Dialogs(string json)
    {
        ChatsResponse response = JsonUtility.FromJson<ChatsResponse>(Json(json));
        Assert.IsNotNull(response?.dialogs, "the payload did not parse");
        return response.dialogs;
    }

    [Test]
    public void WhatsappContactFullName_Parses()
    {
        ChatDialog dialog = Dialogs(WhatsappRow)[0];

        Assert.IsNotNull(dialog.contact, "the contact object must parse");
        Assert.AreEqual("Алия Сатпаева", dialog.contact.FullName);
        Assert.AreEqual("77000000011@c.us", dialog.id, "the rest of the row still parses next to the new fields");
        Assert.AreEqual(2, dialog.unread_count);
    }

    [Test]
    public void TelegramUserFlags_Parse()
    {
        ChatDialog dialog = Dialogs(TelegramRow)[0];

        Assert.IsNotNull(dialog.user, "the user card must parse");
        Assert.IsTrue(dialog.user.Contact);
        Assert.IsFalse(dialog.user.Bot);
        Assert.IsFalse(dialog.user.Support);
        Assert.IsFalse(dialog.user.Self);
        Assert.AreEqual("user", dialog.type);
        Assert.AreEqual("5550000011", dialog.id, "the unrelated fields of the card (photo, hash, key/value array) do not disturb the row");
        Assert.AreEqual("2026-10-20T14:42:41Z", dialog.last_time);
    }

    [Test]
    public void TelegramUserFlags_EachOneParsesOnItsOwn()
    {
        foreach (string flag in new[] { "Contact", "Bot", "Support", "Self" })
        {
            ChatDialog dialog = Dialogs($"{{ 'dialogs': [ {{ 'id': '5550000099', 'type': 'user', 'user': {{ '{flag}': true }} }} ] }}")[0];
            Assert.AreEqual(flag == "Contact", dialog.user.Contact, $"{flag}: Contact");
            Assert.AreEqual(flag == "Bot", dialog.user.Bot, $"{flag}: Bot");
            Assert.AreEqual(flag == "Support", dialog.user.Support, $"{flag}: Support");
            Assert.AreEqual(flag == "Self", dialog.user.Self, $"{flag}: Self");
        }
    }

    [Test]
    public void MissingObjects_ReadAsAllFalse()
    {
        // WhatsApp rows carry no user card, Telegram rows no contact object, a group/channel neither.
        ChatDialog dialog = Dialogs("{ 'dialogs': [ { 'id': '77000000020@c.us', 'name': 'X' } ] }")[0];

        Assert.IsNotNull(dialog.contact, "JsonUtility must not leave a missing contact object null");
        Assert.IsTrue(string.IsNullOrEmpty(dialog.contact.FullName), "…it reads as an empty name");
        Assert.IsNotNull(dialog.user, "JsonUtility must not leave a missing user card null");
        Assert.IsFalse(dialog.user.Contact);
        Assert.IsFalse(dialog.user.Bot);
        Assert.IsFalse(dialog.user.Support);
        Assert.IsFalse(dialog.user.Self);
    }

    [Test]
    public void ExplicitNullObjects_ReadAsAllFalse_AndDoNotBreakTheList()
    {
        // 'contact': null / 'user': null must neither throw nor drop the sibling dialogs of the same answer.
        List<ChatDialog> dialogs = Dialogs(
            "{ 'dialogs': [ { 'id': '1@c.us', 'contact': null, 'user': null }, { 'id': '2@c.us', 'contact': { 'FullName': 'Ж' } } ] }");

        Assert.AreEqual(2, dialogs.Count);
        Assert.IsNotNull(dialogs[0].contact, "an explicit null reads as a default object too");
        Assert.IsTrue(string.IsNullOrEmpty(dialogs[0].contact.FullName));
        Assert.IsNotNull(dialogs[0].user);
        Assert.IsFalse(dialogs[0].user.Contact || dialogs[0].user.Bot || dialogs[0].user.Support || dialogs[0].user.Self);
        Assert.AreEqual("Ж", dialogs[1].contact.FullName, "the sibling row is intact");

        Assert.AreEqual(false, ChatAudienceFacts.From(dialogs[0], ChatChannel.WhatsApp, "1@c.us", "").Contact);
        Assert.AreEqual(false, ChatAudienceFacts.From(dialogs[0], ChatChannel.Telegram, "1", "").Bot);
    }

    // ── the pure builder: dialog → facts ──────────────────────────────────────────────────────────────────

    private static ChatDialog WaDialog(string fullName) =>
        new ChatDialog { contact = new ChatDialogContact { FullName = fullName } };

    private static ChatDialog TgDialog(string type, bool contact = false, bool bot = false, bool support = false, bool self = false) =>
        new ChatDialog { type = type, user = new ChatDialogUser { Contact = contact, Bot = bot, Support = support, Self = self } };

    [Test]
    public void WhatsappFacts_ContactIsAnAddressBookName()
    {
        string id = "77000000011@c.us";
        Assert.AreEqual(true, ChatAudienceFacts.From(WaDialog("Алия Сатпаева"), ChatChannel.WhatsApp, id, "").Contact);
        Assert.AreEqual(false, ChatAudienceFacts.From(WaDialog(""), ChatChannel.WhatsApp, id, "").Contact, "no name — not a contact");
        Assert.AreEqual(false, ChatAudienceFacts.From(WaDialog(null), ChatChannel.WhatsApp, id, "").Contact);
        Assert.AreEqual(false, ChatAudienceFacts.From(WaDialog("  \t "), ChatChannel.WhatsApp, id, "").Contact, "blanks are no name");
        Assert.AreEqual(true, ChatAudienceFacts.From(WaDialog(" А "), ChatChannel.WhatsApp, id, "").Contact);
    }

    [Test]
    public void WhatsappFacts_FoundAndPushNameDoNotMakeAContact()
    {
        // Wappi's «Found» flag means nothing and a push name is what the person calls themselves, not the address book.
        ChatDialog dialog = Dialogs(
            "{ 'dialogs': [ { 'id': '77000000015@c.us', 'contact': { 'Found': true, 'FirstName': '', 'FullName': '', 'PushName': 'Карина' } } ] }")[0];

        Assert.AreEqual(false, ChatAudienceFacts.From(dialog, ChatChannel.WhatsApp, dialog.id, "").Contact);
    }

    [Test]
    public void WhatsappFacts_AreKnownBooleans_NeverUnknown()
    {
        AudienceFacts f = ChatAudienceFacts.From(WaDialog("А"), ChatChannel.WhatsApp, "77000000011@c.us", "");

        Assert.IsFalse(f.Telegram);
        Assert.AreEqual("77000000011@c.us", f.ChatId);
        Assert.AreEqual(false, f.Bot, "bot = false");
        Assert.AreEqual(false, f.Support);
        Assert.AreEqual(false, f.Self);
        Assert.AreEqual(true, f.Contact);
    }

    [Test]
    public void WhatsappFacts_SelfIsTheChatWithTheBotsOwnNumber()
    {
        const string own = "77000000001@c.us";
        // The number is stored as typed; only its digits count.
        foreach (string stored in new[] { "77000000001", "+7 700 000 00 01", "+7(700)000-00-01", " 77000000001 " })
            Assert.AreEqual(true, ChatAudienceFacts.From(WaDialog(""), ChatChannel.WhatsApp, own, stored).Self, $"stored '{stored}'");

        Assert.AreEqual(false, ChatAudienceFacts.From(WaDialog(""), ChatChannel.WhatsApp, "77000000002@c.us", OwnNumber).Self,
            "another person");
        Assert.AreEqual(false, ChatAudienceFacts.From(WaDialog(""), ChatChannel.WhatsApp, "7700000000@c.us", OwnNumber).Self,
            "a prefix of the number is not the number");
        Assert.AreEqual(false, ChatAudienceFacts.From(WaDialog(""), ChatChannel.WhatsApp, "177000000010@c.us", OwnNumber).Self,
            "neither is a longer number that merely contains it");
    }

    [Test]
    public void WhatsappFacts_NoStoredNumber_IsNeverSelf()
    {
        // An empty number has empty digits, and so do ids like status@broadcast: «equal» must not mean «both empty».
        foreach (string stored in new[] { null, "", "   ", "no digits at all", "+-()" })
        {
            Assert.AreEqual(false, ChatAudienceFacts.From(WaDialog(""), ChatChannel.WhatsApp, "status@broadcast", stored).Self, $"'{stored}' vs status@broadcast");
            Assert.AreEqual(false, ChatAudienceFacts.From(WaDialog(""), ChatChannel.WhatsApp, "77000000001@c.us", stored).Self, $"'{stored}'");
            Assert.AreEqual(false, ChatAudienceFacts.From(WaDialog(""), ChatChannel.WhatsApp, "", stored).Self, $"'{stored}' vs no id");
        }
    }

    [Test]
    public void WhatsappFacts_SelfReadsTheNumberPartOfTheId()
    {
        Assert.IsTrue(ChatAudienceFacts.IsOwnWhatsappChat("77000000001@c.us", OwnNumber));
        Assert.IsTrue(ChatAudienceFacts.IsOwnWhatsappChat("77000000001", OwnNumber), "an id without a suffix is read as it stands");
        Assert.IsFalse(ChatAudienceFacts.IsOwnWhatsappChat(null, OwnNumber));
        Assert.IsFalse(ChatAudienceFacts.IsOwnWhatsappChat("", OwnNumber));
        Assert.IsFalse(ChatAudienceFacts.IsOwnWhatsappChat("77000000001-1600000000@g.us", OwnNumber),
            "a group id that starts with the number is a group, not the chat with oneself");
    }

    [Test]
    public void TelegramFacts_TakeTheUserFlagsAndTheChatType()
    {
        AudienceFacts person = ChatAudienceFacts.From(TgDialog("user", contact: true), ChatChannel.Telegram, "5550000011", "ignored");
        Assert.IsTrue(person.Telegram);
        Assert.AreEqual("5550000011", person.ChatId);
        Assert.AreEqual("user", person.ChatType);
        Assert.AreEqual(true, person.Contact);
        Assert.AreEqual(false, person.Bot);
        Assert.AreEqual(false, person.Support);
        Assert.AreEqual(false, person.Self);

        AudienceFacts flags = ChatAudienceFacts.From(TgDialog("user", bot: true, support: true, self: true), ChatChannel.Telegram, "5550000001", "");
        Assert.AreEqual(true, flags.Bot);
        Assert.AreEqual(true, flags.Support);
        Assert.AreEqual(true, flags.Self);
        Assert.AreEqual(false, flags.Contact);

        Assert.AreEqual("channel", ChatAudienceFacts.From(TgDialog("channel"), ChatChannel.Telegram, "5550100001", "").ChatType);
    }

    [Test]
    public void TelegramFacts_ReadNoStoredNumber_SelfComesFromTheFlag()
    {
        // The stored WhatsApp number says nothing about a Telegram chat — even when the ids happen to share digits.
        Assert.AreEqual(false, ChatAudienceFacts.From(TgDialog("user"), ChatChannel.Telegram, "77000000001", OwnNumber).Self);
        Assert.AreEqual(true, ChatAudienceFacts.From(TgDialog("user", self: true), ChatChannel.Telegram, "5550000003", "").Self);
    }

    [Test]
    public void TelegramFacts_NoChatType_IsAnEmptyOne_NotAGuess()
    {
        AudienceFacts f = ChatAudienceFacts.From(new ChatDialog { user = new ChatDialogUser() }, ChatChannel.Telegram, "5550000017", "");
        Assert.AreEqual("", f.ChatType);
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.Unknown), ChatAudienceRule.Decide(f, false, null));
    }

    [Test]
    public void Facts_OfADialogWithoutItsObjects_AreUnknown_NotAllFalse()
    {
        // Only a dialog built by hand has a null object (JsonUtility never leaves one): nothing was received, so nothing is known.
        var bare = new ChatDialog { type = "user" };

        AudienceFacts wa = ChatAudienceFacts.From(bare, ChatChannel.WhatsApp, "77000000014@c.us", "");
        Assert.IsNull(wa.Contact);
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.Unknown), ChatAudienceRule.Decide(wa, false, null));

        AudienceFacts tg = ChatAudienceFacts.From(bare, ChatChannel.Telegram, "5550000012", "");
        Assert.IsNull(tg.Bot);
        Assert.IsNull(tg.Support);
        Assert.IsNull(tg.Self);
        Assert.IsNull(tg.Contact);
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.Unknown), ChatAudienceRule.Decide(tg, false, null));

        Assert.DoesNotThrow(() => ChatAudienceFacts.From(null, ChatChannel.WhatsApp, "x@c.us", ""));
    }

    // ── the wiring: the real ChatManager.ParseChatsJson ───────────────────────────────────────────────────

    private ChatManager NewManager(ChatChannel channel)
    {
        var host = new GameObject("ChatManagerHost");
        hosts.Add(host);
        // A real manager WITHOUT its Awake/Start (EditMode runs neither): no scene wiring, no singleton, no coroutines.
        var manager = host.AddComponent<ChatManager>();
        SetProperty(manager, nameof(ChatManager.ActiveChannel), channel);
        SetProperty(manager, nameof(ChatManager.CurrentBotId), TestBot);
        return manager;
    }

    private static void SetProperty(ChatManager manager, string name, object value)
    {
        PropertyInfo property = typeof(ChatManager).GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.IsNotNull(property, $"ChatManager.{name} moved — update ChatDialogAudienceParseTests");
        property.SetValue(manager, value);
    }

    private static void ParseChatsJson(ChatManager manager, string json, bool isInitialLoad)
    {
        MethodInfo parse = typeof(ChatManager).GetMethod("ParseChatsJson", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(parse, "ChatManager.ParseChatsJson moved — update ChatDialogAudienceParseTests");
        try
        {
            parse.Invoke(manager, new object[] { json, isInitialLoad });
        }
        catch (TargetInvocationException e)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
    }

    private static object WaRow(string id, object contact = null, bool isGroup = false) => new
    {
        id,
        name = "Chat " + id,
        contact,
        isGroup,
        unread_count = 0,
        last_message_id = "M-" + id,
        last_timestamp = "2026-10-20T10:00:00Z",
    };

    private static object TgRow(string id, string type, object user = null) => new
    {
        id,
        name = "Chat " + id,
        type,
        user,
        unread_count = 0,
        last_message_id = "M-" + id,
        last_timestamp = "2026-10-20T10:00:00Z",
    };

    // Null members are left out: a group or a channel carries no user card, a WhatsApp row no user object — absent, not null.
    private static string List(params object[] dialogs) => JsonConvert.SerializeObject(
        new { status = "done", dialogs }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

    private static object Card(bool contact = false, bool bot = false, bool support = false, bool self = false) => new
    {
        Contact = contact, Bot = bot, Support = support, Self = self,
        Verified = false, Premium = true, ID = 5550000000L, FirstName = "Test",
    };

    private static void AssertVerdict(ChatManager manager, string chatId, NonHumanKind nonHuman,
                                      AudienceKind kind, AudienceReason reason, string message)
    {
        ChatViewModel vm = manager.GetChat(chatId);
        Assert.IsNotNull(vm, $"{message}: no row for {chatId}");
        Assert.AreEqual(nonHuman, ChatAudienceRule.NonHuman(vm.Facts), $"{message}: the word under the name");
        // history false = «the chat began after the connection»: the verdict then depends on the facts alone
        Assert.AreEqual((kind, reason), ChatAudienceRule.Decide(vm.Facts, false, null), $"{message}: the verdict");
    }

    [Test]
    public void ParseChatsJson_Whatsapp_GivesEveryChatItsFacts()
    {
        PlayerPrefs.SetString(OwnNumberKey, OwnNumber);
        ChatManager manager = NewManager(ChatChannel.WhatsApp);

        ParseChatsJson(manager, List(
            WaRow("77000000011@c.us", contact: new { Found = true, FullName = "Алия Сатпаева", PushName = "Aliya" }),
            WaRow("77000000014@c.us", contact: new { Found = false, FullName = "", PushName = "" }),
            WaRow("77000000015@c.us", contact: new { Found = true, FullName = "", PushName = "Карина" }),
            WaRow("120363000000000001@g.us", isGroup: true),
            WaRow("120363000000000002@newsletter"),
            WaRow("status@broadcast"),
            WaRow("77000000001@c.us", contact: new { Found = false, FullName = "" }),
            WaRow("77000000020@c.us")), isInitialLoad: true);

        Assert.AreEqual(8, manager.Chats.Count);
        AssertVerdict(manager, "77000000011@c.us", NonHumanKind.None, AudienceKind.Personal, AudienceReason.Contact, "a saved contact");
        AssertVerdict(manager, "77000000014@c.us", NonHumanKind.None, AudienceKind.Business, AudienceReason.New, "no name, nothing before");
        AssertVerdict(manager, "77000000015@c.us", NonHumanKind.None, AudienceKind.Business, AudienceReason.New, "a push name is not a contact");
        AssertVerdict(manager, "120363000000000001@g.us", NonHumanKind.Group, AudienceKind.Personal, AudienceReason.NonHuman, "a group");
        AssertVerdict(manager, "120363000000000002@newsletter", NonHumanKind.Channel, AudienceKind.Personal, AudienceReason.NonHuman, "a channel");
        AssertVerdict(manager, "status@broadcast", NonHumanKind.Channel, AudienceKind.Personal, AudienceReason.NonHuman, "the status list");
        AssertVerdict(manager, "77000000001@c.us", NonHumanKind.Self, AudienceKind.Personal, AudienceReason.NonHuman, "the chat with oneself");
        AssertVerdict(manager, "77000000020@c.us", NonHumanKind.None, AudienceKind.Business, AudienceReason.New, "no contact object at all");

        Assert.AreEqual(new AudienceFacts(false, "77000000011@c.us", "", false, false, false, true), manager.GetChat("77000000011@c.us").Facts);
    }

    [Test]
    public void ParseChatsJson_Whatsapp_WithoutAStoredNumber_FindsNoSelfChat()
    {
        ChatManager manager = NewManager(ChatChannel.WhatsApp);

        ParseChatsJson(manager, List(WaRow("77000000001@c.us"), WaRow("status@broadcast")), isInitialLoad: true);

        Assert.AreEqual(false, manager.GetChat("77000000001@c.us").Facts.Self);
        Assert.AreEqual(false, manager.GetChat("status@broadcast").Facts.Self, "an empty number must not equal an id with no digits");
    }

    [Test]
    public void ParseChatsJson_Whatsapp_TheNumberIsTheActiveBots()
    {
        // Another bot's number does not make this bot's chat «Избранное».
        PlayerPrefs.SetString("AnotherChatAudienceTestBotWhatsappNumber", OwnNumber);
        try
        {
            ChatManager manager = NewManager(ChatChannel.WhatsApp);
            ParseChatsJson(manager, List(WaRow("77000000001@c.us")), isInitialLoad: true);
            Assert.AreEqual(false, manager.GetChat("77000000001@c.us").Facts.Self);
        }
        finally
        {
            PlayerPrefs.DeleteKey("AnotherChatAudienceTestBotWhatsappNumber");
        }
    }

    [Test]
    public void ParseChatsJson_Telegram_GivesEveryChatItsFacts()
    {
        PlayerPrefs.SetString(OwnNumberKey, OwnNumber);   // a Telegram list never reads the WhatsApp number
        ChatManager manager = NewManager(ChatChannel.Telegram);

        ParseChatsJson(manager, List(
            TgRow("5550000011", "user", Card(contact: true)),
            TgRow("5550000012", "user", Card()),
            TgRow("5550000001", "user", Card(bot: true)),
            TgRow("777000", "user", Card(support: true)),
            TgRow("5550000003", "user", Card(self: true)),
            TgRow("5550100001", "channel"),
            TgRow("5550100002", "chat"),
            TgRow("5550000017", null, Card()),
            TgRow("77000000001", "user", Card())), isInitialLoad: true);

        Assert.AreEqual(9, manager.Chats.Count);
        AssertVerdict(manager, "5550000011", NonHumanKind.None, AudienceKind.Personal, AudienceReason.Contact, "a saved contact");
        AssertVerdict(manager, "5550000012", NonHumanKind.None, AudienceKind.Business, AudienceReason.New, "a stranger");
        AssertVerdict(manager, "5550000001", NonHumanKind.Bot, AudienceKind.Personal, AudienceReason.NonHuman, "a bot");
        AssertVerdict(manager, "777000", NonHumanKind.Service, AudienceKind.Personal, AudienceReason.NonHuman, "the service account");
        AssertVerdict(manager, "5550000003", NonHumanKind.Self, AudienceKind.Personal, AudienceReason.NonHuman, "the chat with oneself");
        AssertVerdict(manager, "5550100001", NonHumanKind.Channel, AudienceKind.Personal, AudienceReason.NonHuman, "a channel");
        AssertVerdict(manager, "5550100002", NonHumanKind.Group, AudienceKind.Personal, AudienceReason.NonHuman, "a group");
        AssertVerdict(manager, "5550000017", NonHumanKind.None, AudienceKind.Personal, AudienceReason.Unknown, "no chat type");
        AssertVerdict(manager, "77000000001", NonHumanKind.None, AudienceKind.Business, AudienceReason.New,
            "digits shared with the WhatsApp number mean nothing on Telegram");

        Assert.AreEqual(new AudienceFacts(true, "5550000011", "user", false, false, false, true), manager.GetChat("5550000011").Facts);
    }

    [Test]
    public void ParseChatsJson_Telegram_FactsCarryTheCanonicalChatId()
    {
        // The spurious «@c.us» twin of a Telegram dialog collapses onto the bare id BEFORE the facts are taken: the
        // service account is recognised by its id whichever form arrived.
        ChatManager manager = NewManager(ChatChannel.Telegram);

        ParseChatsJson(manager, List(TgRow("777000@c.us", "user", Card(support: true))), isInitialLoad: true);

        ChatViewModel vm = manager.GetChat("777000");
        Assert.IsNotNull(vm);
        Assert.AreEqual("777000", vm.ChatId);
        Assert.AreEqual("777000", vm.Facts.ChatId);
        Assert.AreEqual(NonHumanKind.Service, ChatAudienceRule.NonHuman(vm.Facts));
    }

    [Test]
    public void ParseChatsJson_OnAFaithfulWappiBody_TakesTheFacts_SoAnOldCacheNeedsNoMigration()
    {
        // Wappi has always sent the contact object and the user card; the app just never declared them. A chats.json written
        // by the build before this one is that same body, so its facts are readable the moment the new code parses it.
        ChatManager whatsapp = NewManager(ChatChannel.WhatsApp);
        ParseChatsJson(whatsapp, Json(WhatsappRow), isInitialLoad: true);
        Assert.AreEqual(new AudienceFacts(false, "77000000011@c.us", "", false, false, false, true),
            whatsapp.GetChat("77000000011@c.us").Facts);

        ChatManager telegram = NewManager(ChatChannel.Telegram);
        ParseChatsJson(telegram, Json(TelegramRow), isInitialLoad: true);
        Assert.AreEqual(new AudienceFacts(true, "5550000011", "user", false, false, false, true),
            telegram.GetChat("5550000011").Facts);
    }

    [Test]
    public void ParseChatsJson_AMerge_RefreshesTheFactsOfAnExistingRow()
    {
        ChatManager manager = NewManager(ChatChannel.WhatsApp);
        string first = List(
            WaRow("77000000011@c.us", contact: new { FullName = "Алия Сатпаева" }),
            WaRow("77000000014@c.us", contact: new { FullName = "" }),
            WaRow("77000000020@c.us", contact: new { FullName = "Бекзат" }));
        ParseChatsJson(manager, first, isInitialLoad: true);

        ChatViewModel gone = manager.GetChat("77000000011@c.us");
        ChatViewModel saved = manager.GetChat("77000000014@c.us");
        ChatViewModel same = manager.GetChat("77000000020@c.us");
        Assert.AreEqual(true, gone.Facts.Contact);
        Assert.AreEqual(false, saved.Facts.Contact);
        var updates = new Dictionary<ChatViewModel, int> { { gone, 0 }, { saved, 0 }, { same, 0 } };
        gone.OnUpdated += vm => updates[vm]++;
        saved.OnUpdated += vm => updates[vm]++;
        same.OnUpdated += vm => updates[vm]++;

        // A background sync: the owner deleted one contact and saved another; nothing else about the rows changed.
        string second = List(
            WaRow("77000000011@c.us", contact: new { FullName = "" }),
            WaRow("77000000014@c.us", contact: new { FullName = "Новый клиент" }),
            WaRow("77000000020@c.us", contact: new { FullName = "Бекзат" }));
        ParseChatsJson(manager, second, isInitialLoad: false);

        Assert.AreSame(gone, manager.GetChat("77000000011@c.us"), "the row is merged, not rebuilt");
        Assert.AreEqual(false, gone.Facts.Contact, "the deleted contact is no contact any more");
        Assert.AreEqual(true, saved.Facts.Contact, "the saved contact is one now");
        Assert.AreEqual(1, updates[gone], "a changed fact tells the row once — it can move between the lists");
        Assert.AreEqual(1, updates[saved]);
        Assert.AreEqual(0, updates[same], "an identical re-read tells nobody");
    }

    // ── the cache: the server's answer, byte for byte ─────────────────────────────────────────────────────

    [Test]
    public void TheCachedChatList_GivesTheSameFactsAsTheLiveAnswer()
    {
        string answer = List(
            WaRow("77000000011@c.us", contact: new { Found = true, FullName = "Алия Сатпаева" }),
            WaRow("77000000014@c.us", contact: new { Found = false, FullName = "" }),
            WaRow("120363000000000001@g.us", isGroup: true));
        PlayerPrefs.SetString(OwnNumberKey, OwnNumber);

        tempDir = Path.Combine(Path.GetTempPath(), "chat-audience-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string cachePath = Path.Combine(tempDir, "chats.json");
        File.WriteAllText(cachePath, answer);         // what a sync stores: the answer as it came
        string cached = File.ReadAllText(cachePath);  // what the next launch parses before the network answers

        ChatManager live = NewManager(ChatChannel.WhatsApp);
        ChatManager restored = NewManager(ChatChannel.WhatsApp);
        ParseChatsJson(live, answer, isInitialLoad: true);
        ParseChatsJson(restored, cached, isInitialLoad: true);

        Assert.AreEqual(3, restored.Chats.Count);
        foreach (ChatViewModel vm in live.Chats)
            Assert.AreEqual(vm.Facts, restored.GetChat(vm.ChatId).Facts, $"{vm.ChatId}: the cache lost a fact");
        Assert.AreEqual(true, restored.GetChat("77000000011@c.us").Facts.Contact);
    }

    [Test]
    public void TheChatListCache_IsTheServersAnswerVerbatim_SoNoFactNeedsStorageOfItsOwn()
    {
        // If the cache ever held a re-serialised model instead of the answer, the facts would have to be carried into it
        // explicitly — this fails then, naming the reason.
        string sync = MethodBody("Scripts/Main/ChatManager.cs", "SyncAllChats");
        Assert.IsTrue(Regex.IsMatch(sync, @"string\s+newJson\s*=\s*www\.downloadHandler\.text\s*;"),
            "SyncAllChats no longer takes the cache text from the raw answer");
        Assert.IsTrue(Regex.IsMatch(sync, @"WriteAllTextAsync\(\s*cachePath\s*,\s*newJson\s*\)"),
            "SyncAllChats no longer writes the raw answer to chats.json");

        string load = MethodBody("Scripts/Main/ChatManager.BotState.cs", "LoadChatsForActiveBot");
        Assert.IsTrue(Regex.IsMatch(load, @"cachedJson\s*=\s*File\.ReadAllText\(\s*cachePath\s*\)"),
            "LoadChatsForActiveBot no longer reads chats.json as text");
        Assert.IsTrue(Regex.IsMatch(load, @"ParseChatsJson\(\s*cachedJson\s*,\s*true\s*\)"),
            "the cached list no longer goes through ParseChatsJson — the one place the facts are taken");
    }

    // ── the view-model carries them ───────────────────────────────────────────────────────────────────────

    [Test]
    public void ChatViewModel_FactsStartEmpty_AndTheRuleReadsThemAsUnknown()
    {
        var vm = new ChatViewModel("77000000014@c.us", "T", "", "x", 100);

        Assert.AreEqual(default(AudienceFacts), vm.Facts);
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.Unknown), ChatAudienceRule.Decide(vm.Facts, false, null),
            "a row nobody has read facts for must never show as a client");
    }

    [Test]
    public void ChatViewModel_FactsComeFromTheConstructor()
    {
        AudienceFacts facts = new AudienceFacts(false, "77000000014@c.us", "", false, false, false, false);

        var vm = new ChatViewModel("77000000014@c.us", "T", "", "x", 100, facts: facts);

        Assert.AreEqual(facts, vm.Facts);
    }

    [Test]
    public void ChatViewModel_UpdateFacts_TellsTheRowOnlyAboutAChange()
    {
        var first = new AudienceFacts(false, "77000000014@c.us", "", false, false, false, false);
        var vm = new ChatViewModel("77000000014@c.us", "T", "", "x", 100, facts: first);
        int updates = 0;
        vm.OnUpdated += _ => updates++;

        vm.UpdateFacts(first);
        Assert.AreEqual(0, updates, "the same facts again change nothing");

        var saved = new AudienceFacts(false, "77000000014@c.us", "", false, false, false, true);
        vm.UpdateFacts(saved);
        Assert.AreEqual(saved, vm.Facts);
        Assert.AreEqual(1, updates);

        vm.UpdateFacts(saved);
        Assert.AreEqual(1, updates);
    }

    // ── source helpers ────────────────────────────────────────────────────────────────────────────────────

    private static string MethodBody(string relativePath, string method)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"{relativePath} moved — update ChatDialogAudienceParseTests");
        string source = File.ReadAllText(path);

        // A declaration — modifiers, a return type, the name, a parameter list — not a call site.
        var decl = Regex.Match(source,
            @"^[ \t]*(?:(?:public|private|internal|protected|static)[ \t]+)*(?!return\b)[\w<>.]+[ \t]+" +
            Regex.Escape(method) + @"[ \t]*\(",
            RegexOptions.Multiline);
        Assert.IsTrue(decl.Success, $"declaration of {method} not found in {relativePath}");

        int open = source.IndexOf('{', decl.Index);
        Assert.Greater(open, 0, $"no body after {method}");

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"unbalanced braces after {method}");
        return null;
    }
}

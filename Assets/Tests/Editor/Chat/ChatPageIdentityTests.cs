using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты»: the app shows a WhatsApp chat whose messages/get rows carry the chat's @lid code. The live
/// check (docs/audits/2026-10-25-personal-chats/live-checks.md, «@lid и номер — один id») found chats the list names by
/// their number (the code in <c>chat.LID</c>) while every row of their pages carries the code in chatId, from and to — and
/// the crossed-answer guard read every such page as another chat's: no history, no live message, an empty panel. The path
/// pinned here, on the REAL <c>ChatManager</c> (a bare manager driven by reflection, the way ChatDialogAudienceParseTests
/// drives <c>ParseChatsJson</c>):
///   • the chat-list row's <c>chat.LID</c> → <see cref="ChatViewModel.ChatCode"/> (only a code that counts — ChatIdentity);
///   • the one gate every open-chat page reader goes through — <c>AcceptChatPage</c> (SyncLatestMessages, which the live
///     poll re-issues; ValidateCachePageAgainstServer; GetMessagesRoutine, whose refusal is what retries a «crossed» page)
///     — accepts the chat's page under its code and gives those rows the chat's id;
///   • so the bubbles and the history cache keep the chat under its number: the code is never a key.
/// The three readers need the network (WappiProxy), which EditMode cannot run, so their wiring to the gate is pinned at the
/// source; the gate itself runs here for real.
/// </summary>
public class ChatPageIdentityTests
{
    private const string TestBot = "ChatPageIdentityTestBot";
    private const string Number = "77001112233@c.us";
    private const string Code = "100000000000123@lid";
    private const string OtherCode = "100000000000999@lid";
    private const string OwnCode = "100000000000777@lid";      // the owner's own code: some of his rows carry it in `from`
    private const string TelegramChat = "5550001112";

    private readonly List<GameObject> hosts = new List<GameObject>();
    private string tempDir;

    [SetUp]
    [TearDown]
    public void CleanUp()
    {
        foreach (GameObject host in hosts)
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
        hosts.Clear();
        if (tempDir != null && Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        tempDir = null;
    }

    // ── the manager, driven by reflection ─────────────────────────────────────────────────────────────────

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
        Assert.IsNotNull(property, $"ChatManager.{name} moved — update ChatPageIdentityTests");
        property.SetValue(manager, value);
    }

    private static object Invoke(ChatManager manager, string method, params object[] args)
    {
        MethodInfo info = typeof(ChatManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(info, $"ChatManager.{method} is missing — the open chat's pages have no gate that knows the chat's code");
        try
        {
            return info.Invoke(manager, args);
        }
        catch (TargetInvocationException e)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            return null;
        }
    }

    private static void ParseChatsJson(ChatManager manager, string json, bool isInitialLoad) =>
        Invoke(manager, "ParseChatsJson", json, isInitialLoad);

    private static bool AcceptChatPage(ChatManager manager, MessagesResponseRaw page, string chatId) =>
        (bool)Invoke(manager, "AcceptChatPage", page, chatId);

    private static MessageViewModel Bubble(ChatManager manager, RawMessage raw) =>
        (MessageViewModel)Invoke(manager, "CreateViewModel", Invoke(manager, "Normalize", raw));

    // ── the payloads, shaped like Wappi's ─────────────────────────────────────────────────────────────────

    // Null members are left out: a row without a chat object carries none — absent, not null.
    private static string List(params object[] dialogs) => JsonConvert.SerializeObject(
        new { status = "done", dialogs }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

    // A WhatsApp chats/filter row: the chat object as Wappi sends it (its LID next to fields the app does not read).
    private static object WaRow(string id, string lid) => new
    {
        id,
        name = "Chat " + id,
        contact = new { Found = false, FullName = "", PushName = "Клиент" },
        chat = lid == null ? null : new { VerifiedName = (string)null, Status = "", PictureID = "", Devices = new[] { "x.0:0@c.us" }, LID = lid },
        isGroup = false,
        unread_count = 0,
        last_message_id = "M-" + id,
        last_timestamp = "2026-10-28T10:00:00Z",
    };

    private static object TgRow(string id, object chat) => new
    {
        id,
        name = "Chat " + id,
        type = "user",
        user = new { Contact = false, Bot = false, Support = false, Self = false },
        chat,
        unread_count = 0,
        last_message_id = "M-" + id,
        last_timestamp = "2026-10-28T10:00:00Z",
    };

    // A messages/get page of a chat Wappi lists under its code: chatId, from and to all carry codes; whose message it is,
    // fromMe says. Parsed by the same JsonConvert as the real answer.
    private static MessagesResponseRaw Page(string rowChatId, int count)
    {
        var messages = new List<object>();
        for (int i = 0; i < count; i++)
        {
            bool mine = i % 2 == 1;
            messages.Add(new
            {
                id = $"WAMID-{i}",
                type = "chat",
                body = mine ? "Добрый день, всё в наличии" : "Здравствуйте, есть в наличии?",
                chatId = rowChatId,
                from = mine ? OwnCode : rowChatId,
                to = mine ? rowChatId : OwnCode,
                fromMe = mine,
                time = 1761645600L - i * 60,
                senderName = mine ? "" : "Клиент",
            });
        }
        return JsonConvert.DeserializeObject<MessagesResponseRaw>(JsonConvert.SerializeObject(new { status = "done", messages }));
    }

    private ChatManager WhatsappListing(string number, string lid)
    {
        ChatManager manager = NewManager(ChatChannel.WhatsApp);
        ParseChatsJson(manager, List(WaRow(number, lid)), isInitialLoad: true);
        Assert.IsNotNull(manager.GetChat(number), "the chat-list row did not reach the list");
        return manager;
    }

    // ── the chat-list row's code reaches the chat ─────────────────────────────────────────────────────────

    [Test]
    public void TheChatListRow_ParsesTheChatsLid()
    {
        ChatsResponse list = JsonUtility.FromJson<ChatsResponse>(List(WaRow(Number, Code), WaRow("77009998877@c.us", null)));

        Assert.AreEqual(Code, list.dialogs[0].chat.LID, "the LID next to the fields the app does not read");
        Assert.AreEqual("77009998877@c.us", list.dialogs[1].id, "the sibling row is intact");
        Assert.IsNotNull(list.dialogs[1].chat, "JsonUtility must not leave a missing chat object null");
        Assert.IsTrue(string.IsNullOrEmpty(list.dialogs[1].chat.LID), "…it reads as no LID");
    }

    [Test]
    public void AChatValueOfAnotherShape_NeitherBreaksTheListNorCountsAsACode()
    {
        // Until now JsonUtility skipped `chat` whatever its shape; declaring it must not let an odd value cost the whole list.
        string[] odd =
        {
            "\"x\"", "[]", "5", "true", "null", "{}", "{\"LID\":5}", "{\"LID\":true}", "{\"LID\":null}",
            "{\"LID\":[\"" + Code + "\"]}", "{\"LID\":{\"v\":\"" + Code + "\"}}",
        };
        foreach (string chat in odd)
        {
            string json = $"{{\"status\":\"done\",\"dialogs\":[{{\"id\":\"{Number}\",\"name\":\"A\",\"chat\":{chat}}}," +
                          $"{{\"id\":\"77000000002@c.us\",\"name\":\"B\",\"chat\":{{\"LID\":\"{OtherCode}\"}}}}]}}";
            ChatManager manager = NewManager(ChatChannel.WhatsApp);

            Assert.DoesNotThrow(() => ParseChatsJson(manager, json, isInitialLoad: true), $"chat {chat}");
            Assert.AreEqual(2, manager.Chats.Count, $"chat {chat}: the list keeps both rows");
            Assert.AreEqual("", manager.GetChat(Number).ChatCode, $"chat {chat}: no code");
            Assert.AreEqual(OtherCode, manager.GetChat("77000000002@c.us").ChatCode, $"chat {chat}: the next row keeps its code");
        }
    }

    [Test]
    public void ParseChatsJson_GivesAWhatsappChatTheCodeItsRowNames()
    {
        ChatManager manager = NewManager(ChatChannel.WhatsApp);

        ParseChatsJson(manager, List(
            WaRow(Number, Code),
            WaRow("77000000002@c.us", ""),
            WaRow("77000000003@c.us", "77009998877@c.us"),
            WaRow("77000000004@c.us", "@lid"),
            WaRow("77000000005@c.us", null),
            WaRow(OtherCode, OtherCode)), isInitialLoad: true);

        Assert.AreEqual(Code, manager.GetChat(Number).ChatCode, "the code the chat's rows may carry");
        Assert.AreEqual("", manager.GetChat("77000000002@c.us").ChatCode, "no LID");
        Assert.AreEqual("", manager.GetChat("77000000003@c.us").ChatCode, "a number is no code");
        Assert.AreEqual("", manager.GetChat("77000000004@c.us").ChatCode, "the suffix alone is no code");
        Assert.AreEqual("", manager.GetChat("77000000005@c.us").ChatCode, "no chat object");
        Assert.AreEqual("", manager.GetChat(OtherCode).ChatCode, "a chat known only by its code: its id already says it");
        Assert.AreEqual(Number, manager.GetChat(Number).ChatId, "the chat stays under its number");
    }

    [Test]
    public void ParseChatsJson_Telegram_NeverHasACode()
    {
        // A Telegram group's row carries a `chat` card of its own; even a LID-shaped value in it is no code.
        ChatManager manager = NewManager(ChatChannel.Telegram);

        ParseChatsJson(manager, List(
            TgRow(TelegramChat, new { Title = "Группа", ParticipantsCount = 3, LID = Code }),
            TgRow("5550001113", null)), isInitialLoad: true);

        Assert.AreEqual("", manager.GetChat(TelegramChat).ChatCode);
        Assert.AreEqual("", manager.GetChat("5550001113").ChatCode);
    }

    [Test]
    public void ParseChatsJson_AMerge_RefreshesTheCode()
    {
        ChatManager manager = NewManager(ChatChannel.WhatsApp);
        ParseChatsJson(manager, List(WaRow(Number, null)), isInitialLoad: true);
        ChatViewModel chat = manager.GetChat(Number);
        Assert.AreEqual("", chat.ChatCode);

        ParseChatsJson(manager, List(WaRow(Number, Code)), isInitialLoad: false);
        Assert.AreSame(chat, manager.GetChat(Number), "the row is merged, not rebuilt");
        Assert.AreEqual(Code, chat.ChatCode, "a code the list names later is learned on the next read");

        ParseChatsJson(manager, List(WaRow(Number, "")), isInitialLoad: false);
        Assert.AreEqual("", chat.ChatCode, "and forgotten when the list stops naming it");
    }

    // ── the gate of the open chat's pages ─────────────────────────────────────────────────────────────────

    [Test]
    public void AcceptChatPage_APageUnderTheChatsCode_IsItsPage_AndItsRowsTakeTheChatsId()
    {
        ChatManager manager = WhatsappListing(Number, Code);
        MessagesResponseRaw page = Page(Code, 6);
        page.messages.Add(new RawMessage { id = "NO-CHAT-ID", type = "chat" });

        Assert.IsTrue(AcceptChatPage(manager, page, Number),
            "the chat's own page — not a crossed answer: no retry, its history and live messages are shown");
        Assert.IsTrue(page.messages.Take(6).All(r => r.chatId == Number), "every row under the code now names the chat by its number");
        Assert.IsNull(page.messages[6].chatId, "a row without a chatId stays without one");
        Assert.AreEqual(Code, page.messages[0].from, "whose message it is, fromMe says — `from` is left as Wappi sent it");
    }

    [Test]
    public void AcceptChatPage_APageUnderAnotherCode_IsForeign_AndTouchesNothing()
    {
        ChatManager manager = WhatsappListing(Number, Code);

        foreach (string foreign in new[] { OtherCode, "77009998877@c.us" })
        {
            MessagesResponseRaw page = Page(foreign, 4);
            Assert.IsFalse(AcceptChatPage(manager, page, Number), $"a page under {foreign} is another chat's — still refused");
            Assert.IsTrue(page.messages.All(r => r.chatId == foreign), "a refused page is left as it came");
        }
    }

    [Test]
    public void AcceptChatPage_APageUnderTheNumber_IsItsPage_AsBefore()
    {
        ChatManager manager = WhatsappListing(Number, Code);
        MessagesResponseRaw page = Page(Number, 4);

        Assert.IsTrue(AcceptChatPage(manager, page, Number));
        Assert.IsTrue(page.messages.All(r => r.chatId == Number));
    }

    [Test]
    public void AcceptChatPage_WithoutACodeThatCounts_IsTheIdAlone()
    {
        // The list names no code, a number, or nothing at all (a chat the list does not hold yet): as before.
        foreach (string lid in new[] { null, "", "77009998877@c.us", "100000000000123" })
        {
            ChatManager manager = WhatsappListing(Number, lid);
            Assert.IsFalse(AcceptChatPage(manager, Page(Code, 3), Number), $"LID '{lid}': a page under an unknown code is foreign");
            Assert.IsTrue(AcceptChatPage(manager, Page(Number, 3), Number), $"LID '{lid}': the page under the number is the chat's");
        }

        ChatManager empty = NewManager(ChatChannel.WhatsApp);
        Assert.IsFalse(AcceptChatPage(empty, Page(Code, 3), Number), "a chat the list does not hold is known by its id alone");
        Assert.IsTrue(AcceptChatPage(empty, Page(Number, 3), Number));
    }

    [Test]
    public void AcceptChatPage_Telegram_IsUnchanged()
    {
        ChatManager manager = NewManager(ChatChannel.Telegram);
        ParseChatsJson(manager, List(TgRow(TelegramChat, new { LID = Code })), isInitialLoad: true);

        MessagesResponseRaw underCode = Page(Code, 3);
        Assert.IsFalse(AcceptChatPage(manager, underCode, TelegramChat), "Telegram has no code: a page under one is foreign");
        Assert.IsTrue(underCode.messages.All(r => r.chatId == Code));
        Assert.IsTrue(AcceptChatPage(manager, Page(TelegramChat, 3), TelegramChat));
    }

    [Test]
    public void AcceptChatPage_NoPage_IsNotACrossedPage()
    {
        // As before: an answer without messages is not «another chat's» — the readers treat it as an empty page.
        ChatManager manager = WhatsappListing(Number, Code);
        Assert.IsTrue(AcceptChatPage(manager, null, Number));
        Assert.IsTrue(AcceptChatPage(manager, new MessagesResponseRaw { status = "done" }, Number));
        Assert.IsTrue(AcceptChatPage(manager, new MessagesResponseRaw { messages = new List<RawMessage>() }, Number));
    }

    [Test]
    public void AcceptChatPage_AMissingChatId_RefusesNothing_AsBefore()
    {
        // The guard never judged a request without a chat id; the gate neither throws on it nor rewrites a row.
        ChatManager manager = WhatsappListing(Number, Code);
        foreach (string missing in new[] { null, "" })
        {
            MessagesResponseRaw page = Page(Code, 3);
            Assert.IsTrue(AcceptChatPage(manager, page, missing), $"chat id '{missing}'");
            Assert.IsTrue(page.messages.All(r => r.chatId == Code), "no row takes a missing id");
        }
    }

    // ── the cache keeps the chat under its number ─────────────────────────────────────────────────────────

    [Test]
    public void ThePremise_TheCacheStripsARowThatNamesAnotherId()
    {
        // Why the rows must take the chat's id: the history file of a chat drops every row naming another id (the poison of a
        // crossed answer) — a row under the code would be dropped from the chat's own history on every save and load.
        tempDir = NewTempDir();
        var rows = new List<MessageViewModel>
        {
            new MessageViewModel { messageId = "under-code", chatId = Code, type = MessageType.Chat, text = "x" },
            new MessageViewModel { messageId = "under-number", chatId = Number, type = MessageType.Chat, text = "y" },
        };

        ChatHistoryCache.SaveHistory(tempDir, Number, rows);

        CollectionAssert.AreEqual(new[] { "under-number" },
            ChatHistoryCache.LoadHistory(tempDir, Number).Select(m => m.messageId).ToArray());
    }

    [Test]
    public void RowsUnderTheCode_LieInTheCacheUnderTheChatsNumber()
    {
        ChatManager manager = WhatsappListing(Number, Code);
        MessagesResponseRaw page = Page(Code, 5);
        Assert.IsTrue(AcceptChatPage(manager, page, Number), "the chat's page under its code must be accepted first");

        List<MessageViewModel> bubbles = page.messages.Select(raw => Bubble(manager, raw)).ToList();
        Assert.IsTrue(bubbles.All(b => b.chatId == Number), "every bubble names the chat by its number");
        Assert.AreEqual(2, bubbles.Count(b => !b.isIncoming), "fromMe still says whose message it is");

        tempDir = NewTempDir();
        ChatHistoryCache.SaveHistory(tempDir, Number, bubbles);
        List<MessageViewModel> cached = ChatHistoryCache.LoadHistory(tempDir, Number);

        CollectionAssert.AreEquivalent(page.messages.Select(r => r.id).ToArray(), cached.Select(m => m.messageId).ToArray(),
            "the history file of the chat keeps every row — none is stripped as another chat's");
        Assert.IsTrue(cached.All(m => m.chatId == Number));
        Assert.IsFalse(File.Exists(Path.Combine(tempDir, "messages", $"{Code}.json")), "nothing is kept under the code");
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "chat-page-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── the three readers go through the gate (source: EditMode cannot run their network) ─────────────────

    private static readonly string[] PageReaders = { "SyncLatestMessages", "ValidateCachePageAgainstServer", "GetMessagesRoutine" };

    private static readonly Regex GateCall = new Regex(@"if\s*\(\s*!AcceptChatPage\(\s*response\s*,\s*chatId\s*\)\s*\)");

    [Test]
    public void EveryOpenChatPageReader_JudgesItsPageThroughTheGate()
    {
        foreach (string reader in PageReaders)
        {
            string body = MethodBody("Scripts/Main/ChatManager.cs", reader);
            Assert.AreEqual(1, GateCall.Matches(body).Count,
                $"{reader} must judge its page through AcceptChatPage(response, chatId) — the gate that knows the chat's code");
            Assert.IsFalse(body.Contains("CrossChatResponseGuard"),
                $"{reader} calls the guard by itself again — a page under the chat's code would read as another chat's");
        }
    }

    [Test]
    public void ThePageFetch_RetriesOnlyAPageTheGateRefused()
    {
        string fetch = MethodBody("Scripts/Main/ChatManager.cs", "GetMessagesRoutine");
        Match refused = Regex.Match(fetch, GateCall + @"\s*\{(?<branch>[^{}]*)\}");
        Assert.IsTrue(refused.Success, "GetMessagesRoutine's refusal branch moved");
        Assert.IsTrue(Regex.IsMatch(refused.Groups["branch"].Value, @"crossed\s*=\s*true\s*;"),
            "a page the gate refuses is the one that is retried");
        Assert.AreEqual(1, Regex.Matches(fetch, @"crossed\s*=\s*true").Count,
            "nothing but the gate's refusal may send a page into the «crossed» retries");
    }

    [Test]
    public void TheGate_IsTheOnlyCallerOfTheGuard()
    {
        // A messages/get reader that called the guard on its own would judge the page by the chat's id alone.
        var callers = new List<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            int calls = Regex.Matches(File.ReadAllText(file), @"CrossChatResponseGuard\.IsForDifferentChat\(").Count;
            for (int i = 0; i < calls; i++) callers.Add(Path.GetFileName(file));
        }
        CollectionAssert.AreEqual(new[] { "ChatManager.PageIdentity.cs" }, callers.ToArray(),
            "the guard must be called once, by AcceptChatPage");
        StringAssert.Contains("CrossChatResponseGuard.IsForDifferentChat(",
            MethodBody("Scripts/Main/ChatManager.PageIdentity.cs", "AcceptChatPage"));
    }

    private static string MethodBody(string relativePath, string method)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"{relativePath} is missing — update ChatPageIdentityTests");
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

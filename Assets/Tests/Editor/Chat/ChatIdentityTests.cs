using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты»: the app's twin of the server's <c>chatIdentity</c> — which messages/get rows are THIS chat's.
/// Both twins are pinned by ONE truth table — Tools/n8n/fixtures/chat-identity-cases.json, read here through the project
/// root (never a copy), the same file Tools/n8n/tests/test_audience_rule.py runs CHAT_IDENTITY_JS over. A row the server
/// passes and this fails is a bug in <see cref="ChatIdentity"/>. The server takes the code from the webhook's
/// whatsapp_chat_id, the app from the chat-list row's chat.LID: so every row's code reaches the rule here the way the app
/// reads it — through the chat-list DTO, parsed by JsonUtility, a value of another type included. The table cannot pin
/// every spelling (a twin that trimmed would pass it), so the edges beyond it — exact, untrimmed — are tests of their own.
/// </summary>
public class ChatIdentityTests
{
    private static readonly string FixturePath =
        Path.Combine(Application.dataPath, "..", "Tools", "n8n", "fixtures", "chat-identity-cases.json");

    private static readonly string ModulePath =
        Path.Combine(Application.dataPath, "..", "Tools", "n8n", "audience.py");

    private static readonly string[] FixtureFields = { "name", "channel", "chatId", "code", "expectCode", "expectNames" };

    private const string Number = "77001112233@c.us";
    private const string Code = "100000000000123@lid";
    private const string OtherCode = "100000000000999@lid";
    private const string TelegramChat = "5550001112";

    private sealed class Case
    {
        public string name;
        public string channel;
        public string chatId;
        public JToken code;                           // null = not given; any JSON type, as the server's twin receives it
        public string expectCode;
        public Dictionary<string, bool> expectNames;  // {id: does a row under this id name the chat?}

        public override string ToString() => name;
    }

    private sealed class Table
    {
        public List<Case> cases;
    }

    // DateParseHandling.None: nothing in the table may ever be re-typed by the reader.
    private static readonly JsonSerializerSettings ReadAsWritten =
        new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

    private static string FixtureText() => File.ReadAllText(FixturePath);

    private static Table LoadTable() => JsonConvert.DeserializeObject<Table>(FixtureText(), ReadAsWritten);

    private static IEnumerable<TestCaseData> FixtureCases() =>
        LoadTable().cases.Select(c => new TestCaseData(c).SetName($"EveryFixtureCase_MatchesTheServer({c.name})"));

    /// <summary>
    /// The code as the app gets it: the chat-list row's <c>chat.LID</c>, parsed by JsonUtility like every chats/filter
    /// answer. Not given = a <c>chat</c> object without a LID.
    /// </summary>
    private static string CodeAsTheAppReadsIt(JToken code)
    {
        string chat = code == null || code.Type == JTokenType.Null
            ? "{}"
            : $"{{\"LID\":{code.ToString(Formatting.None)}}}";
        ChatsResponse list = JsonUtility.FromJson<ChatsResponse>($"{{\"dialogs\":[{{\"id\":\"{Number}\",\"chat\":{chat}}}]}}");
        Assert.IsNotNull(list?.dialogs, "the chat-list row did not parse");
        Assert.IsNotNull(list.dialogs[0].chat, "JsonUtility must not leave the chat object null");
        return list.dialogs[0].chat.LID;
    }

    private static RawMessage Row(string chatId, string id = "m") => new RawMessage { id = id, chatId = chatId };

    // ── the shared truth table ────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Fixture_IsTheCanonicalTable_WithExactlyTheAgreedFields()
    {
        Assert.IsTrue(File.Exists(FixturePath), $"the shared truth table is missing: {FixturePath}");
        var doc = JsonConvert.DeserializeObject<JObject>(FixtureText(), ReadAsWritten);
        CollectionAssert.AreEquivalent(new[] { "cases" }, doc.Properties().Select(p => p.Name).ToArray(),
            "the table's top level is a contract shared with the server tests");

        var rows = (JArray)doc["cases"];
        Assert.GreaterOrEqual(rows.Count, 13, "the truth table collapsed — every row below would pass vacuously");
        foreach (JObject row in rows)
        {
            CollectionAssert.AreEquivalent(FixtureFields, row.Properties().Select(p => p.Name).ToArray(),
                $"{row["name"]}: the table's fields are a contract shared with the server tests");
            Assert.IsTrue(((JObject)row["expectNames"]).Count > 0, $"{row["name"]}: a row must ask about at least one id");
        }

        Table table = LoadTable();
        Assert.AreEqual(rows.Count, table.cases.Select(c => c.name).Distinct().Count(), "case names must be unique");
    }

    [TestCaseSource(nameof(FixtureCases))]
    public void EveryFixtureCase_MatchesTheServer(object boxed)
    {
        var c = (Case)boxed;
        ChatIdentity chat = ChatIdentity.For(c.channel, c.chatId, CodeAsTheAppReadsIt(c.code));

        Assert.AreEqual(c.expectCode, chat.Code, $"{c.name}: the code that counts");
        Assert.AreEqual(c.chatId, chat.ChatId, $"{c.name}: the chat stays under its own id");
        foreach (KeyValuePair<string, bool> id in c.expectNames)
            Assert.AreEqual(id.Value, chat.Names(id.Key), $"{c.name}: does a row under '{id.Key}' name the chat?");
    }

    [Test]
    public void TheTable_HasARowWhereTheCodeCounts_AndRowsWhereItDoesNot()
    {
        // A table where nothing ever counts would pass a twin that never reads the code.
        List<Case> cases = LoadTable().cases;
        Assert.IsTrue(cases.Any(c => c.expectCode.Length > 0 && c.expectNames.TryGetValue(c.expectCode, out bool n) && n),
            "no row of the table has a code that counts and names the chat");
        Assert.IsTrue(cases.Any(c => c.channel == "telegram" && c.expectCode.Length == 0), "no Telegram row");
    }

    // ── the module's literal ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public void TheSuffix_IsTheServersSuffix()
    {
        Assert.IsTrue(File.Exists(ModulePath), $"the server's rule module moved: {ModulePath}");
        Match line = Regex.Match(File.ReadAllText(ModulePath), @"^CHAT_CODE_SUFFIX[ \t]*=[ \t]*""([^""]*)""", RegexOptions.Multiline);
        Assert.IsTrue(line.Success, "CHAT_CODE_SUFFIX is gone from audience.py — the C# twin can no longer be compared with it");
        Assert.AreEqual(line.Groups[1].Value, ChatIdentity.CodeSuffix);
    }

    // ── the app's channel ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void TheAppsChannel_IsTheServersChannelName()
    {
        Assert.AreEqual(Code, ChatIdentity.For(ChatChannel.WhatsApp, Number, Code).Code, "WhatsApp reads the code");
        Assert.AreEqual("", ChatIdentity.For(ChatChannel.Telegram, TelegramChat, Code).Code, "Telegram never has one");
        Assert.IsFalse(ChatIdentity.For(ChatChannel.Telegram, TelegramChat, Code).Names(Code));
        Assert.IsTrue(ChatIdentity.For(ChatChannel.Telegram, TelegramChat, Code).Names(TelegramChat));
    }

    // ── exact and untrimmed: the table cannot see these ───────────────────────────────────────────────────

    [Test]
    public void TheCode_IsReadExactly_NeverTrimmed()
    {
        // The server's rule: a string ending in the suffix with something before it, not the chat id — no trimming.
        Assert.AreEqual("", ChatIdentity.For("whatsapp", Number, Code + " ").Code, "a trailing space: no longer ends in @lid");
        Assert.AreEqual(" " + Code, ChatIdentity.For("whatsapp", Number, " " + Code).Code,
            "a leading space still ends in @lid — the server counts it as it stands");
        Assert.IsFalse(ChatIdentity.For("whatsapp", Number, " " + Code).Names(Code), "…and it names only itself, exactly");
        Assert.AreEqual("", ChatIdentity.For(" whatsapp", Number, Code).Code, "the channel is matched exactly too");
        Assert.AreEqual("", ChatIdentity.For("whatsapp ", Number, Code).Code);
    }

    [Test]
    public void Names_ComparesExactly()
    {
        ChatIdentity chat = ChatIdentity.For("whatsapp", Number, Code);
        foreach (string id in new[] { Code.ToUpperInvariant(), Code + " ", " " + Code, "100000000000123", Number + " ", "77001112233" })
            Assert.IsFalse(chat.Names(id), $"'{id}' is not the chat's id or its code");
        Assert.IsFalse(chat.Names(null), "a row without a chatId is the caller's own rule, never a match here");
    }

    [Test]
    public void ADefaultIdentity_NamesNothing_AndNeverThrows()
    {
        ChatIdentity none = default;
        Assert.AreEqual("", none.Code);
        Assert.IsFalse(none.Names(Code));
        Assert.IsFalse(none.Names(""));
        Assert.DoesNotThrow(() => none.AdoptRows(new List<RawMessage> { Row(Code), Row(null), null }));
    }

    [Test]
    public void ByIdAlone_HasNoCode()
    {
        ChatIdentity chat = ChatIdentity.ByIdAlone(Number);
        Assert.AreEqual(Number, chat.ChatId);
        Assert.AreEqual("", chat.Code);
        Assert.IsTrue(chat.Names(Number));
        Assert.IsFalse(chat.Names(Code));
        Assert.AreEqual(0, chat.AdoptRows(new List<RawMessage> { Row(Code) }));
    }

    [Test]
    public void AMissingChatId_IsNotTheCode()
    {
        ChatIdentity chat = ChatIdentity.For("whatsapp", null, Code);
        Assert.AreEqual(Code, chat.Code, "the server's rule: a code that is not the chat id counts — a missing id is not it");
        Assert.IsTrue(chat.Names(Code));
        Assert.IsFalse(chat.Names(Number));
    }

    // ── the rows of an accepted page take the chat's id ───────────────────────────────────────────────────

    [Test]
    public void AdoptRows_GivesTheRowsUnderTheCode_TheChatsId_AndNothingElse()
    {
        var rows = new List<RawMessage>
        {
            Row(Code, "a"), Row(Number, "b"), Row(OtherCode, "c"), Row("77009998877@c.us", "d"), Row(null, "e"), Row("", "f"), null,
            Row(Code, "g"),
        };

        int adopted = ChatIdentity.For(ChatChannel.WhatsApp, Number, Code).AdoptRows(rows);

        Assert.AreEqual(2, adopted, "the two rows under the chat's code");
        Assert.AreEqual(Number, rows[0].chatId, "the code is never a key: the row now names the chat by its id");
        Assert.AreEqual(Number, rows[7].chatId);
        Assert.AreEqual(Number, rows[1].chatId, "a row under the id stays as it is");
        Assert.AreEqual(OtherCode, rows[2].chatId, "another code names another chat — never rewritten");
        Assert.AreEqual("77009998877@c.us", rows[3].chatId, "another number — never rewritten");
        Assert.IsNull(rows[4].chatId, "a row without a chatId stays without one");
        Assert.AreEqual("", rows[5].chatId);
    }

    [Test]
    public void AdoptRows_WithoutACodeThatCounts_TouchesNothing()
    {
        foreach (ChatIdentity chat in new[]
                 {
                     ChatIdentity.For(ChatChannel.Telegram, TelegramChat, Code),
                     ChatIdentity.For(ChatChannel.WhatsApp, Number, ""),
                     ChatIdentity.For(ChatChannel.WhatsApp, Number, null),
                     ChatIdentity.For(ChatChannel.WhatsApp, Number, "100000000000123"),
                     ChatIdentity.For(ChatChannel.WhatsApp, Number, Number),
                     ChatIdentity.For(ChatChannel.WhatsApp, Code, Code),
                 })
        {
            var rows = new List<RawMessage> { Row(Code), Row(Number), Row(TelegramChat) };
            Assert.AreEqual(0, chat.AdoptRows(rows), $"chat '{chat.ChatId}' / code '{chat.Code}'");
            CollectionAssert.AreEqual(new[] { Code, Number, TelegramChat }, rows.Select(r => r.chatId).ToArray());
        }
        Assert.AreEqual(0, ChatIdentity.For(ChatChannel.WhatsApp, Number, Code).AdoptRows(null), "no page");
    }
}

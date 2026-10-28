using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты»: the app's twin of the server's PERSONAL / BUSINESS rule. Both sides are pinned by ONE case
/// table — Tools/n8n/fixtures/audience-cases.json, read here through the project root (never a copy), the same file
/// Tools/n8n/tests/test_audience_rule.py feeds the server's RULE_JS. A row the server passes and this fails is a bug in
/// ChatAudienceRule. The table cannot pin every literal's exact spelling (a twin that folded case would pass it), so the
/// edges beyond it — exact, case-sensitive, untrimmed — are tests of their own, as are the module's literals, which are
/// compared with the text of Tools/n8n/audience.py.
/// </summary>
public class ChatAudienceRuleTests
{
    private static readonly string FixturePath =
        Path.Combine(Application.dataPath, "..", "Tools", "n8n", "fixtures", "audience-cases.json");

    private static readonly string ModulePath =
        Path.Combine(Application.dataPath, "..", "Tools", "n8n", "audience.py");

    private static readonly string[] FixtureFields =
        { "name", "channel", "chatId", "chatType", "flags", "contact", "history", "owner", "expectKind", "expectReason" };

    private static readonly Dictionary<string, AudienceReason> Reasons = new Dictionary<string, AudienceReason>
    {
        { "nonhuman", AudienceReason.NonHuman },
        { "owner", AudienceReason.Owner },
        { "contact", AudienceReason.Contact },
        { "history", AudienceReason.History },
        { "new", AudienceReason.New },
        { "unknown", AudienceReason.Unknown },
    };

    private sealed class FixtureFlags
    {
        public bool bot;
        public bool support;
        public bool self;
    }

    private sealed class Case
    {
        public string name;
        public string channel;
        public string chatId;
        public string chatType;
        public FixtureFlags flags;     // null = the Telegram chat card was not received (WhatsApp: reads as all false)
        public bool? contact;
        public bool? history;
        public string owner;
        public string expectKind;
        public string expectReason;

        // flags: null ↔ all three per-flag facts null; the rest map one to one.
        public AudienceFacts Facts => new AudienceFacts(channel == "telegram", chatId, chatType,
            flags?.bot, flags?.support, flags?.self, contact);

        public AudienceKind? OwnerKind => owner == null ? (AudienceKind?)null : KindOf(owner);
        public AudienceKind ExpectedKind => KindOf(expectKind);
        public AudienceReason ExpectedReason => Reasons[expectReason];

        public override string ToString() => name;
    }

    private sealed class Table
    {
        public Dictionary<string, int> constants;
        public List<Case> cases;
    }

    private static AudienceKind KindOf(string text)
    {
        Assert.That(text, Is.EqualTo("personal").Or.EqualTo("business"), "a kind outside the table's domain");
        return text == "personal" ? AudienceKind.Personal : AudienceKind.Business;
    }

    // DateParseHandling.None: the table holds no dates, but nothing in it may ever be re-typed by the reader.
    private static readonly JsonSerializerSettings ReadAsWritten =
        new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

    private static string FixtureText() => File.ReadAllText(FixturePath);

    private static Table LoadTable() => JsonConvert.DeserializeObject<Table>(FixtureText(), ReadAsWritten);

    private static IEnumerable<TestCaseData> FixtureCases() =>
        LoadTable().cases.Select(c => new TestCaseData(c).SetName($"EveryFixtureCase_MatchesTheServer({c.name})"));

    // A WhatsApp / Telegram person with every fact known unless the test says otherwise.
    private static AudienceFacts Wa(string chatId, bool? bot = false, bool? self = false, bool? contact = false) =>
        new AudienceFacts(false, chatId, "", bot, false, self, contact);

    private static AudienceFacts Tg(string chatId, string type = "user", bool? bot = false, bool? support = false,
                                    bool? self = false, bool? contact = false) =>
        new AudienceFacts(true, chatId, type, bot, support, self, contact);

    private static (AudienceKind, AudienceReason) Personal(AudienceReason reason) => (AudienceKind.Personal, reason);

    // ── the shared case table ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Fixture_IsTheCanonicalTable_WithExactlyTheAgreedFields()
    {
        Assert.IsTrue(File.Exists(FixturePath), $"the shared case table is missing: {FixturePath}");
        var doc = JsonConvert.DeserializeObject<JObject>(FixtureText(), ReadAsWritten);
        CollectionAssert.AreEquivalent(new[] { "constants", "cases" }, doc.Properties().Select(p => p.Name).ToArray(),
            "the table's top level is a contract shared with the server tests");

        var rows = (JArray)doc["cases"];
        Assert.GreaterOrEqual(rows.Count, 40, "the case table collapsed — every row below would pass vacuously");
        foreach (JObject row in rows)
            CollectionAssert.AreEquivalent(FixtureFields, row.Properties().Select(p => p.Name).ToArray(),
                $"{row["name"]}: the table's fields are a contract shared with the server tests");

        var table = LoadTable();
        Assert.AreEqual(rows.Count, table.cases.Select(c => c.name).Distinct().Count(), "case names must be unique");
        Assert.AreEqual(rows.Count, table.cases.Count);
    }

    [TestCaseSource(nameof(FixtureCases))]
    public void EveryFixtureCase_MatchesTheServer(object boxed)
    {
        var c = (Case)boxed;
        var (kind, reason) = ChatAudienceRule.Decide(c.Facts, c.history, c.OwnerKind);
        Assert.AreEqual(c.ExpectedKind, kind, $"{c.name}: kind — the server answers {c.expectKind}/{c.expectReason}");
        Assert.AreEqual(c.ExpectedReason, reason, $"{c.name}: reason — the server answers {c.expectKind}/{c.expectReason}");
    }

    [Test]
    public void NonHuman_AgreesWithTheDecisionOnEveryRow()
    {
        // The word under a non-person's name and the verdict come from the same step: they cannot part.
        foreach (Case c in LoadTable().cases)
        {
            bool nonHuman = ChatAudienceRule.NonHuman(c.Facts) != NonHumanKind.None;
            Assert.AreEqual(c.expectReason == "nonhuman", nonHuman, $"{c.name}: NonHuman() and Decide() disagree");
        }
    }

    // ── the constants ─────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Constants_MatchTheFixture()
    {
        Dictionary<string, int> k = LoadTable().constants;
        CollectionAssert.AreEquivalent(new[] { "settleMinutes", "freshHours", "wipeDays" }, k.Keys);
        Assert.AreEqual(k["settleMinutes"], ChatAudienceRule.SettleMinutes);
        Assert.AreEqual(k["freshHours"], ChatAudienceRule.FreshHours);
        Assert.AreEqual(k["wipeDays"], ChatAudienceRule.WipeDays);
    }

    [Test]
    public void Literals_MatchTheServerModule()
    {
        Assert.IsTrue(File.Exists(ModulePath), $"the server's rule module moved: {ModulePath}");
        string module = File.ReadAllText(ModulePath);

        Assert.AreEqual(PythonInt(module, "SETTLE_MINUTES"), ChatAudienceRule.SettleMinutes);
        Assert.AreEqual(PythonInt(module, "FRESH_HOURS"), ChatAudienceRule.FreshHours);
        Assert.AreEqual(PythonInt(module, "WIPE_DAYS"), ChatAudienceRule.WipeDays);

        CollectionAssert.AreEquivalent(PythonStrings(module, "WA_NONHUMAN_SUFFIXES"),
            ChatAudienceRule.WhatsappNonHumanSuffixes.ToArray(), "WA_NONHUMAN_SUFFIXES");
        CollectionAssert.AreEquivalent(PythonStrings(module, "TG_SERVICE_IDS"),
            ChatAudienceRule.TelegramServiceIds.ToArray(), "TG_SERVICE_IDS");
        CollectionAssert.AreEquivalent(PythonStrings(module, "TG_NONHUMAN_TYPES"),
            ChatAudienceRule.TelegramNonHumanTypes.ToArray(), "TG_NONHUMAN_TYPES");
        CollectionAssert.AreEqual(PythonStrings(module, "TG_PERSON_TYPE"),
            new[] { ChatAudienceRule.TelegramPersonType }, "TG_PERSON_TYPE");
    }

    [Test]
    public void EveryLiteral_IsExercisedByTheTable()
    {
        // A literal the table never touches could drift from the server's and nothing would notice.
        List<Case> cases = LoadTable().cases;
        bool Pins(Func<Case, bool> match, string expectReason) =>
            cases.Any(c => match(c) && c.expectReason == expectReason);

        foreach (string suffix in ChatAudienceRule.WhatsappNonHumanSuffixes)
            Assert.IsTrue(Pins(c => c.channel == "whatsapp" && c.chatId.EndsWith(suffix, StringComparison.Ordinal), "nonhuman"),
                $"no WhatsApp row of the table ends with {suffix} and expects nonhuman");
        foreach (string id in ChatAudienceRule.TelegramServiceIds)
            Assert.IsTrue(Pins(c => c.channel == "telegram" && c.chatId == id, "nonhuman"),
                $"no Telegram row of the table has the service id {id}");
        foreach (string type in ChatAudienceRule.TelegramNonHumanTypes)
            Assert.IsTrue(Pins(c => c.channel == "telegram" && c.chatType == type, "nonhuman"),
                $"no Telegram row of the table has the chat type {type}");
        Assert.IsTrue(Pins(c => c.channel == "telegram" && c.chatType == ChatAudienceRule.TelegramPersonType, "new"),
            "no Telegram row of the table is a person of the person type");
    }

    // ── what the folder says under a non-person's name ────────────────────────────────────────────────────

    [Test]
    public void NonHuman_GivesTheKindForTheSubline()
    {
        Assert.AreEqual(NonHumanKind.Group, ChatAudienceRule.NonHuman(Wa("120363000000000001@g.us")), "a WhatsApp group");
        Assert.AreEqual(NonHumanKind.Channel, ChatAudienceRule.NonHuman(Wa("120363000000000002@newsletter")), "a WhatsApp channel");
        Assert.AreEqual(NonHumanKind.Channel, ChatAudienceRule.NonHuman(Wa("status@broadcast")), "the status / broadcast list");
        Assert.AreEqual(NonHumanKind.Channel, ChatAudienceRule.NonHuman(Tg("5550100001", "channel")), "a Telegram channel");
        Assert.AreEqual(NonHumanKind.Group, ChatAudienceRule.NonHuman(Tg("5550100002", "chat")), "a Telegram group");
        Assert.AreEqual(NonHumanKind.Bot, ChatAudienceRule.NonHuman(Tg("5550000001", bot: true)), "a Telegram bot");
        Assert.AreEqual(NonHumanKind.Bot, ChatAudienceRule.NonHuman(Tg("5550100004", "bot")),
            "a Telegram bot by its chat type: a bot's reply arrives with chat_type bot");
        Assert.AreEqual(NonHumanKind.Bot,
            ChatAudienceRule.NonHuman(new AudienceFacts(true, "5550100005", "bot", null, null, null, null)),
            "a bot's chat type needs no chat card");
        Assert.AreEqual(NonHumanKind.Bot, ChatAudienceRule.NonHuman(Wa("77000000002@c.us", bot: true)), "a WhatsApp bot");
        Assert.AreEqual(NonHumanKind.Service, ChatAudienceRule.NonHuman(Tg("777000")), "the Telegram service account by its id");
        Assert.AreEqual(NonHumanKind.Service,
            ChatAudienceRule.NonHuman(new AudienceFacts(true, "777000", "user", null, null, null, null)),
            "the service account needs no chat card");
        Assert.AreEqual(NonHumanKind.Service, ChatAudienceRule.NonHuman(Tg("5550000002", support: true)), "the Support flag");
        Assert.AreEqual(NonHumanKind.Self, ChatAudienceRule.NonHuman(Tg("5550000003", self: true)), "Telegram's chat with oneself");
        Assert.AreEqual(NonHumanKind.Self, ChatAudienceRule.NonHuman(Wa("77000000001@c.us", self: true)), "WhatsApp's chat with oneself");
        Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(Wa("77000000014@c.us")), "a WhatsApp person");
        Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(Tg("5550000012")), "a Telegram person");
    }

    [Test]
    public void NonHuman_TheStructureOfTheIdOutranksTheFlags()
    {
        // When several steps apply the word follows the chat's shape first, then its flags.
        Assert.AreEqual(NonHumanKind.Group, ChatAudienceRule.NonHuman(Wa("120363000000000001@g.us", bot: true, self: true)));
        Assert.AreEqual(NonHumanKind.Service, ChatAudienceRule.NonHuman(Tg("777000", bot: true)));
        Assert.AreEqual(NonHumanKind.Channel, ChatAudienceRule.NonHuman(Tg("5550100001", "channel", bot: true)));
        Assert.AreEqual(NonHumanKind.Bot, ChatAudienceRule.NonHuman(Tg("5550100004", "bot", support: true, self: true)));
        Assert.AreEqual(NonHumanKind.Bot, ChatAudienceRule.NonHuman(Tg("5550000001", bot: true, support: true, self: true)));
    }

    [Test]
    public void WhatsappFlags_AreNotReadOnTelegramAndTheOtherWayRound()
    {
        // WhatsApp has no Support flag and Telegram's id carries no suffix: each channel reads only its own markers.
        Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(
            new AudienceFacts(false, "77000000014@c.us", "", false, true, false, false)), "WhatsApp does not read Support");
        Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(Tg("5550000012@g.us")),
            "a suffix on a Telegram id is not a WhatsApp group");
        Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(Wa("777000")),
            "777000 is the Telegram service account only");
        Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(
            new AudienceFacts(false, "77000000014@c.us", "channel", false, false, false, false)),
            "WhatsApp does not read the chat type");
    }

    // ── exact, case-sensitive, untrimmed: the table cannot see these ──────────────────────────────────────

    [Test]
    public void TelegramChatType_IsMatchedExactly()
    {
        foreach (string type in new[] { "Channel", "CHANNEL", "Chat", " chat", "chat ", "channel\n", "User", " user", "user ",
                                        "Bot", "BOT", " bot", "bot " })
        {
            AudienceFacts f = Tg("5550100003", type);
            Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(f), $"type '{type}' is not a known non-human type");
            Assert.AreEqual(Personal(AudienceReason.Unknown), ChatAudienceRule.Decide(f, false, null),
                $"type '{type}' is not the person type either: it cannot be classified");
        }
    }

    [Test]
    public void TelegramServiceId_IsMatchedExactly()
    {
        foreach (string id in new[] { "777000 ", " 777000", "7770001", "77700", "0777000" })
            Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(Tg(id)), $"'{id}' is not the service account's id");
    }

    [Test]
    public void WhatsappSuffix_IsMatchedAtTheEndAndCaseSensitively()
    {
        foreach (string id in new[] { "120363000000000001@G.US", "120363000000000001@g.us ", "120363000000000001@g.us.x",
                                      "@g.us@c.us", "120363000000000002@NewsLetter", "status@Broadcast" })
            Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(Wa(id)), $"'{id}' does not end with a non-human suffix");
        Assert.AreEqual(NonHumanKind.Group, ChatAudienceRule.NonHuman(Wa("@g.us")), "the suffix alone still ends with itself");
    }

    // ── a fact that is not known is never a known «no» ────────────────────────────────────────────────────

    [Test]
    public void MissingFacts_FailClosed()
    {
        const string wa = "77000000001@c.us";
        const string tg = "5550000001";
        var unknown = Personal(AudienceReason.Unknown);

        Assert.AreEqual(unknown, ChatAudienceRule.Decide(new AudienceFacts(false, wa, null, null, null, null, null), null, null),
            "whatsapp: only the id is given");
        Assert.AreEqual(unknown, ChatAudienceRule.Decide(Wa(wa, contact: false), null, null), "whatsapp: contact known, history not");
        Assert.AreEqual(unknown, ChatAudienceRule.Decide(Wa(wa, contact: null), false, null), "whatsapp: history known, contact not");
        Assert.AreEqual(unknown,
            ChatAudienceRule.Decide(new AudienceFacts(true, tg, "user", null, null, null, false), false, null),
            "telegram: no chat card");
        Assert.AreEqual(unknown, ChatAudienceRule.Decide(Tg(tg, type: null), false, null), "telegram: no chat type");
        Assert.AreEqual(unknown, ChatAudienceRule.Decide(Tg(tg, type: ""), false, null), "telegram: an empty chat type");
        Assert.AreEqual(unknown, ChatAudienceRule.Decide(new AudienceFacts(true, tg, "user", false, false, false, null), false, null),
            "telegram: contact not known");
    }

    [Test]
    public void NullIdAndType_DoNotThrow_AndAreNotANonHuman()
    {
        var noId = new AudienceFacts(false, null, null, false, false, false, false);
        Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(noId));
        Assert.DoesNotThrow(() => ChatAudienceRule.Decide(noId, false, null));
        var noTelegramId = new AudienceFacts(true, null, null, false, false, false, false);
        Assert.AreEqual(NonHumanKind.None, ChatAudienceRule.NonHuman(noTelegramId));
        Assert.AreEqual(Personal(AudienceReason.Unknown), ChatAudienceRule.Decide(noTelegramId, false, null),
            "a Telegram chat without a type cannot be classified");
    }

    [Test]
    public void TelegramFlags_AreACardWhenAnyIsGiven_AndMissingOnlyWhenAllThreeAreNot()
    {
        // The server's flags are one object: given (a missing key reads as false) or null. Here: any value given = a card.
        var oneFlag = new AudienceFacts(true, "5550000099", "user", false, null, null, false);
        Assert.AreEqual((AudienceKind.Business, AudienceReason.New), ChatAudienceRule.Decide(oneFlag, false, null));

        var onlySelf = new AudienceFacts(true, "5550000099", "user", null, null, true, false);
        Assert.AreEqual(Personal(AudienceReason.NonHuman), ChatAudienceRule.Decide(onlySelf, false, null));

        var none = new AudienceFacts(true, "5550000099", "user", null, null, null, false);
        Assert.AreEqual(Personal(AudienceReason.Unknown), ChatAudienceRule.Decide(none, false, null));
    }

    [Test]
    public void WhatsappFlags_Null_ReadAsAllFalse()
    {
        // WhatsApp has no card to miss: null flags are not an unknown, only a person.
        var f = new AudienceFacts(false, "77000000017@c.us", "", null, null, null, false);
        Assert.AreEqual((AudienceKind.Business, AudienceReason.New), ChatAudienceRule.Decide(f, false, null));
    }

    [Test]
    public void TheOwner_OutranksTheFacts_ButNeverANonHuman_AndNeverAnUnclassifiableTelegramChat()
    {
        Assert.AreEqual((AudienceKind.Business, AudienceReason.Owner),
            ChatAudienceRule.Decide(Wa("77000000012@c.us", contact: true), true, AudienceKind.Business));
        Assert.AreEqual((AudienceKind.Personal, AudienceReason.Owner),
            ChatAudienceRule.Decide(Wa("77000000016@c.us"), false, AudienceKind.Personal));
        Assert.AreEqual(Personal(AudienceReason.NonHuman),
            ChatAudienceRule.Decide(Wa("120363000000000001@g.us"), false, AudienceKind.Business));
        Assert.AreEqual(Personal(AudienceReason.Unknown),
            ChatAudienceRule.Decide(Tg("5550000023", "private"), false, AudienceKind.Business));
    }

    // ── the history estimate ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public void HistoryBeforeConnection_ThreeCases()
    {
        const long connectedAtMs = 1_700_000_000_000L;
        long connectedAtSec = connectedAtMs / 1000;

        Assert.AreEqual((bool?)true, ChatAudienceRule.HistoryBeforeConnection(connectedAtSec - 1, connectedAtMs),
            "the last message is older than the connection: the conversation existed before it");
        Assert.AreEqual((bool?)false, ChatAudienceRule.HistoryBeforeConnection(connectedAtSec, connectedAtMs),
            "a message at the very moment of the connection is not before it");
        Assert.AreEqual((bool?)false, ChatAudienceRule.HistoryBeforeConnection(connectedAtSec + 3600, connectedAtMs),
            "a message after the connection: no conversation before it");
        Assert.IsNull(ChatAudienceRule.HistoryBeforeConnection(connectedAtSec - 1, null),
            "the moment is not known: neither yes nor no");
    }

    [Test]
    public void HistoryBeforeConnection_ADegenerateInput_IsUnknown_NeverANo()
    {
        const long connectedAtMs = 1_700_000_000_000L;
        Assert.IsNull(ChatAudienceRule.HistoryBeforeConnection(0, connectedAtMs), "no last-message time is no evidence");
        Assert.IsNull(ChatAudienceRule.HistoryBeforeConnection(-1, connectedAtMs));
        Assert.IsNull(ChatAudienceRule.HistoryBeforeConnection(1_699_999_999L, 0), "a moment of 0 is not a moment");
        Assert.IsNull(ChatAudienceRule.HistoryBeforeConnection(1_699_999_999L, -5));
    }

    [Test]
    public void HistoryEstimate_FeedsTheRule()
    {
        const long connectedAtMs = 1_700_000_000_000L;
        AudienceFacts person = Wa("77000000014@c.us");
        bool? before = ChatAudienceRule.HistoryBeforeConnection(1_699_000_000L, connectedAtMs);
        bool? after = ChatAudienceRule.HistoryBeforeConnection(1_701_000_000L, connectedAtMs);
        bool? unknown = ChatAudienceRule.HistoryBeforeConnection(1_699_000_000L, null);

        Assert.AreEqual(Personal(AudienceReason.History), ChatAudienceRule.Decide(person, before, null));
        Assert.AreEqual((AudienceKind.Business, AudienceReason.New), ChatAudienceRule.Decide(person, after, null));
        Assert.AreEqual(Personal(AudienceReason.Unknown), ChatAudienceRule.Decide(person, unknown, null),
            "no connection moment yet: the chat shows as personal until it is known");
    }

    // ── the facts value ───────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void AudienceFacts_AreComparedByValue()
    {
        AudienceFacts a = Tg("5550000011", contact: true);
        AudienceFacts same = Tg("5550000011", contact: true);
        Assert.AreEqual(a, same);
        Assert.AreEqual(a.GetHashCode(), same.GetHashCode());
        Assert.IsTrue(a.Equals((object)same));

        Assert.AreNotEqual(a, Tg("5550000011", contact: false));
        Assert.AreNotEqual(a, Tg("5550000012", contact: true));
        Assert.AreNotEqual(a, Tg("5550000011", "chat", contact: true));
        Assert.AreNotEqual(a, Tg("5550000011", bot: true, contact: true));
        Assert.AreNotEqual(a, Tg("5550000011", support: true, contact: true));
        Assert.AreNotEqual(a, Tg("5550000011", self: true, contact: true));
        Assert.AreNotEqual(a, new AudienceFacts(false, "5550000011", "user", false, false, false, true), "the channel counts");
        Assert.AreNotEqual(Tg("5550000011", contact: null), Tg("5550000011", contact: false), "unknown is not a known no");
    }

    // ── Tools/n8n/audience.py, read as text ───────────────────────────────────────────────────────────────

    private static string[] PythonStrings(string module, string name)
    {
        string rhs = PythonRightHandSide(module, name);
        return Regex.Matches(rhs, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
    }

    private static int PythonInt(string module, string name)
    {
        Match m = Regex.Match(PythonRightHandSide(module, name), @"^\s*(\d+)\s*$");
        Assert.IsTrue(m.Success, $"{name} in audience.py is no longer an integer literal");
        return int.Parse(m.Groups[1].Value);
    }

    // The text after `NAME =` up to a trailing `# comment`.
    private static string PythonRightHandSide(string module, string name)
    {
        Match line = Regex.Match(module, @"^" + Regex.Escape(name) + @"[ \t]*=[ \t]*([^\r\n]*)", RegexOptions.Multiline);
        Assert.IsTrue(line.Success, $"{name} is gone from audience.py — the C# twin can no longer be compared with it");
        string rhs = line.Groups[1].Value;
        int comment = rhs.IndexOf('#');
        return comment >= 0 ? rhs.Substring(0, comment) : rhs;
    }
}

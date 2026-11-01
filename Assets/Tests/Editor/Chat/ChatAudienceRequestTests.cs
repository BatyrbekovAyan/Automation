using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

/// <summary>
/// «Личные и рабочие чаты»: the app's half of the ChatAudience webhook contract (list / set / connected). The bodies are
/// composed and the answers read by pure seams: the service sends only what <see cref="ChatAudienceRequest"/> composes and
/// trusts only what <see cref="ChatAudienceResponse"/> reads as the server's positive word. A refusal (400 / 401 / 403) is
/// told apart from a fault, and every fault — no answer, n8n's 404 while the webhook is not deployed, a 5xx, a 2xx that is
/// not the envelope — is a retry, never a decision.
/// </summary>
public class ChatAudienceRequestTests
{
    private const string Refusal = "{\"success\":false,\"error\":\"bad_request\"}";

    private static string[] Names(JObject jo) => jo.Properties().Select(p => p.Name).ToArray();

    // ── the bodies ───────────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void SetBody_ShapesTheContract()
    {
        var chats = new[]
        {
            new ChatAudienceRequest.SetChat("77011112233@c.us", AudienceKind.Business, 1760000000123),
            new ChatAudienceRequest.SetChat("77019998877@c.us", AudienceKind.Personal, 1760000000124),
        };
        var set = JObject.Parse(ChatAudienceRequest.SetBody("wa-profile", chats));
        CollectionAssert.AreEquivalent(new[] { "op", "profileId", "chats" }, Names(set));
        Assert.AreEqual("set", (string)set["op"]);
        Assert.AreEqual("wa-profile", (string)set["profileId"]);
        var rows = (JArray)set["chats"];
        Assert.AreEqual(2, rows.Count);
        var first = (JObject)rows[0];
        CollectionAssert.AreEquivalent(new[] { "chatId", "kind", "version" }, Names(first));
        Assert.AreEqual("77011112233@c.us", (string)first["chatId"]);
        Assert.AreEqual("business", (string)first["kind"]);
        Assert.AreEqual(JTokenType.Integer, first["version"].Type, "the server refuses a version that is not an integer");
        Assert.AreEqual(1760000000123L, (long)first["version"]);
        Assert.AreEqual("personal", (string)rows[1]["kind"]);
        Assert.AreEqual(1760000000124L, (long)rows[1]["version"]);

        var list = JObject.Parse(ChatAudienceRequest.ListBody(new[] { "wa", "-1", "", null, "tg", "wa" }));
        CollectionAssert.AreEquivalent(new[] { "op", "profileIds" }, Names(list));
        Assert.AreEqual("list", (string)list["op"]);
        CollectionAssert.AreEqual(new[] { "wa", "tg" }, list["profileIds"].Select(t => (string)t).ToArray(),
            "a never-paired channel ('-1' / '') is no profile; each profile once, in the order given");

        var connected = JObject.Parse(ChatAudienceRequest.ConnectedBody("tg"));
        CollectionAssert.AreEquivalent(new[] { "op", "profileId" }, Names(connected),
            "the number is never sent — the server reads it from get/status itself");
        Assert.AreEqual("connected", (string)connected["op"]);
        Assert.AreEqual("tg", (string)connected["profileId"]);

        Assert.AreEqual("/webhook/ChatAudience", ChatAudienceRequest.Path);
        Assert.AreEqual(20, ChatAudienceRequest.MaxListProfiles, "the server's list limit");
        Assert.AreEqual(50, ChatAudienceRequest.MaxSetChats, "the server's set limit");
    }

    [Test]
    public void ChatIdsTheServerWouldRefuse_AreNeverSendable()
    {
        Assert.IsTrue(ChatAudienceRequest.IsSendableChatId("77011112233@c.us"));
        Assert.IsTrue(ChatAudienceRequest.IsSendableChatId("123456789"), "a Telegram chat id is bare");
        Assert.IsFalse(ChatAudienceRequest.IsSendableChatId(ReplyModeSyncQueue.BotDefaultChat),
            "the reply-mode wildcard names every chat — it is not one");
        Assert.IsFalse(ChatAudienceRequest.IsSendableChatId(""));
        Assert.IsFalse(ChatAudienceRequest.IsSendableChatId(null));
        Assert.IsFalse(ChatAudienceRequest.IsSendableChatId("7701 111@c.us"), "no whitespace");
        Assert.IsFalse(ChatAudienceRequest.IsSendableChatId("77011112233@c.us\n"), "no whitespace at the end either");
        Assert.IsTrue(ChatAudienceRequest.IsSendableChatId(new string('1', ChatAudienceRequest.MaxChatIdLength)));
        Assert.IsFalse(ChatAudienceRequest.IsSendableChatId(new string('1', ChatAudienceRequest.MaxChatIdLength + 1)),
            "at most 128 characters");
        Assert.AreEqual(128, ChatAudienceRequest.MaxChatIdLength);

        Assert.IsTrue(ChatAudienceRequest.IsRealProfileId("wa"));
        Assert.IsFalse(ChatAudienceRequest.IsRealProfileId(Bot.UnauthedProfileSentinel));
        Assert.IsFalse(ChatAudienceRequest.IsRealProfileId(""));
        Assert.IsFalse(ChatAudienceRequest.IsRealProfileId(null));
    }

    [Test]
    public void TheWireWords_RoundTrip_ExactlyAsTheServerWritesThem()
    {
        Assert.AreEqual("personal", ChatAudienceRequest.KindWord(AudienceKind.Personal));
        Assert.AreEqual("business", ChatAudienceRequest.KindWord(AudienceKind.Business));
        Assert.IsTrue(ChatAudienceRequest.TryParseKind("business", out var business) && business == AudienceKind.Business);
        Assert.IsTrue(ChatAudienceRequest.TryParseKind("personal", out var personal) && personal == AudienceKind.Personal);
        foreach (var word in new[] { "Business", " business", "", null, "work" })
            Assert.IsFalse(ChatAudienceRequest.TryParseKind(word, out _), $"'{word}' is not a kind the server writes");

        foreach (AudienceReason reason in System.Enum.GetValues(typeof(AudienceReason)))
            Assert.AreEqual(reason, ChatAudienceRequest.ParseReason(ChatAudienceRequest.ReasonWord(reason)), reason.ToString());
        CollectionAssert.AreEqual(new[] { "nonhuman", "owner", "contact", "history", "new" },
            new[] { AudienceReason.NonHuman, AudienceReason.Owner, AudienceReason.Contact, AudienceReason.History, AudienceReason.New }
                .Select(ChatAudienceRequest.ReasonWord).ToArray(), "the server's reason words");
        Assert.AreEqual(AudienceReason.Unknown, ChatAudienceRequest.ParseReason("Contact"), "an unknown word is no reason");
        Assert.AreEqual(AudienceReason.Unknown, ChatAudienceRequest.ParseReason(null));
    }

    // ── the answers ──────────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void ListAnswer_ParsesDecisionsAndMoment()
    {
        const string body = @"{ 'success': true, 'profiles': [
            { 'profileId': 'wa', 'connectedAt': 1760000000000, 'decisions': [
                { 'chatId': '77011112233@c.us', 'kind': 'business', 'source': 'owner', 'reason': 'owner', 'version': 1760000000500 },
                { 'chatId': '77019998877@c.us', 'kind': 'personal', 'source': 'default', 'reason': 'contact', 'version': null },
                { 'chatId': '120363000000000000@g.us', 'kind': 'personal', 'source': 'default', 'reason': 'nonhuman' },
                { 'chatId': '77010000000@c.us', 'kind': 'business', 'source': 'default', 'reason': 'new', 'version': 0 },
                { 'chatId': '77010000001@c.us', 'kind': 'personal', 'source': 'default', 'reason': 'history' },
                { 'chatId': '', 'kind': 'business', 'source': 'owner', 'reason': 'owner' },
                { 'chatId': '77010000002@c.us', 'kind': 'Business', 'source': 'owner', 'reason': 'owner' },
                'garbage'
            ] },
            { 'profileId': 'tg', 'connectedAt': null, 'decisions': [] },
            { 'profileId': '-1', 'connectedAt': 5, 'decisions': [] },
            { 'connectedAt': 5, 'decisions': [] }
        ] }";
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Answered, ChatAudienceResponse.ClassifyList(200, body, out var profiles));
        CollectionAssert.AreEqual(new[] { "wa", "tg" }, profiles.Select(p => p.ProfileId).ToArray(),
            "a sentinel or a nameless profile is not the server's answer about a profile of ours");

        var wa = profiles[0];
        Assert.AreEqual(1760000000000L, wa.ConnectedAtMs);
        Assert.AreEqual(5, wa.Decisions.Count, "a row without a chat id or with a kind the server never writes is skipped");
        var owner = wa.Decisions[0];
        Assert.AreEqual("77011112233@c.us", owner.ChatId);
        Assert.AreEqual(AudienceKind.Business, owner.Kind);
        Assert.AreEqual(AudienceReason.Owner, owner.Reason);
        Assert.IsTrue(owner.ByOwner);
        Assert.AreEqual(1760000000500L, owner.Version);
        var contact = wa.Decisions[1];
        Assert.AreEqual(AudienceKind.Personal, contact.Kind);
        Assert.AreEqual(AudienceReason.Contact, contact.Reason);
        Assert.IsFalse(contact.ByOwner, "a computed decision");
        Assert.AreEqual(0, contact.Version, "a computed row carries no version");
        Assert.AreEqual(AudienceReason.NonHuman, wa.Decisions[2].Reason);
        Assert.AreEqual(AudienceKind.Business, wa.Decisions[3].Kind);
        Assert.AreEqual(AudienceReason.New, wa.Decisions[3].Reason);
        Assert.AreEqual(AudienceReason.History, wa.Decisions[4].Reason);

        Assert.IsNull(profiles[1].ConnectedAtMs, "no connection row yet — the moment is not known");
        Assert.IsEmpty(profiles[1].Decisions);

        const string textual = "{\"success\":true,\"profiles\":[{\"profileId\":\"wa\",\"connectedAt\":\"1760000000000\",\"decisions\":[]}]}";
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Answered, ChatAudienceResponse.ClassifyList(200, textual, out var text));
        Assert.AreEqual(1760000000000L, text[0].ConnectedAtMs, "a bigint n8n hands over as text is still the moment");
    }

    [Test]
    public void ListAnswer_OnlyThePositiveEnvelopeIsAnAnswer()
    {
        foreach (var body in new[] { "", "<html>proxy</html>", "{}", "[]", "{\"success\":\"true\",\"profiles\":[]}",
                                     "{\"success\":true}", "{\"success\":true,\"profiles\":{}}", Refusal,
                                     "Workflow executed successfully" })
        {
            Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Transient, ChatAudienceResponse.ClassifyList(200, body, out var none),
                $"'{body}' is not the server's word");
            Assert.IsNull(none);
        }
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Answered,
            ChatAudienceResponse.ClassifyList(200, "{\"success\":true,\"profiles\":[]}", out var empty), "an empty list is an answer");
        Assert.IsEmpty(empty);
    }

    [Test]
    public void SetAnswer_ParsesEveryRow()
    {
        const string body = @"{ 'success': true, 'results': [
            { 'chatId': 'a@c.us', 'applied': true, 'kind': 'business', 'version': 17, 'refused': null },
            { 'chatId': 'b@c.us', 'applied': false, 'kind': 'personal', 'version': 99, 'refused': null },
            { 'chatId': '120363000000000000@g.us', 'applied': false, 'kind': 'personal', 'version': 0, 'refused': 'nonhuman' },
            { 'chatId': 'c@c.us', 'applied': true },
            { 'applied': true, 'kind': 'business', 'version': 1 },
            7
        ] }";
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Answered, ChatAudienceResponse.ClassifySet(200, body, out var rows));
        CollectionAssert.AreEqual(new[] { "a@c.us", "b@c.us", "120363000000000000@g.us" }, rows.Select(r => r.ChatId).ToArray(),
            "a row without a chat id or a kind cannot be adopted — that chat is retried");
        Assert.IsTrue(rows[0].Applied);
        Assert.AreEqual(AudienceKind.Business, rows[0].Kind);
        Assert.AreEqual(17, rows[0].Version);
        Assert.IsFalse(rows[0].IsRefused);
        Assert.IsFalse(rows[1].Applied);
        Assert.AreEqual(AudienceKind.Personal, rows[1].Kind);
        Assert.AreEqual(99, rows[1].Version);
        Assert.IsTrue(rows[2].IsRefused);
        Assert.AreEqual("nonhuman", rows[2].Refused);

        foreach (var bad in new[] { "{\"success\":true}", "{\"success\":false,\"results\":[]}", "{}", "" })
        {
            Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Transient, ChatAudienceResponse.ClassifySet(200, bad, out var none), bad);
            Assert.IsNull(none);
        }
    }

    [Test]
    public void ConnectedAnswer_Parses()
    {
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Answered,
            ChatAudienceResponse.ClassifyConnected(200, "{\"success\":true,\"connectedAt\":1760000000000}", out long at));
        Assert.AreEqual(1760000000000L, at);
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Answered,
            ChatAudienceResponse.ClassifyConnected(200, "{\"success\":true,\"connectedAt\":\"1760000000001\"}", out long text));
        Assert.AreEqual(1760000000001L, text, "a bigint n8n hands over as text");

        foreach (var body in new[] { "{\"success\":true}", "{\"success\":true,\"connectedAt\":null}", "{\"success\":true,\"connectedAt\":0}",
                                     "{\"success\":true,\"connectedAt\":-5}", "{\"success\":true,\"connectedAt\":\"soon\"}",
                                     "{\"success\":\"true\",\"connectedAt\":1}", "{}", "" })
        {
            Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Transient, ChatAudienceResponse.ClassifyConnected(200, body, out long none),
                $"'{body}' names no moment — not the server's word");
            Assert.AreEqual(0, none);
        }

        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Transient,
            ChatAudienceResponse.ClassifyConnected(409, "{\"success\":false,\"error\":\"not_authorized\"}", out _),
            "Wappi's status did not confirm the pairing (yet) — retried, not refused");
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Transient,
            ChatAudienceResponse.ClassifyConnected(502, "{\"success\":false,\"error\":\"wappi_unavailable\"}", out _));
    }

    [Test]
    public void ConnectedAnswer_SaysWhetherTheServerRestartedTheMoment()
    {
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Answered, ChatAudienceResponse.ClassifyConnected(200,
            "{\"success\":true,\"connectedAt\":1760000000000,\"restarted\":true}", out long at, out bool restarted));
        Assert.AreEqual(1760000000000L, at);
        Assert.IsTrue(restarted, "another number: the server moved the moment");

        foreach (string body in new[]
                 {
                     "{\"success\":true,\"connectedAt\":1760000000000,\"restarted\":false}",
                     "{\"success\":true,\"connectedAt\":1760000000000}",
                     "{\"success\":true,\"connectedAt\":1760000000000,\"restarted\":\"true\"}",
                     "{\"success\":true,\"connectedAt\":1760000000000,\"restarted\":1}",
                     "{\"success\":true,\"connectedAt\":1760000000000,\"restarted\":null}",
                 })
        {
            Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Answered, ChatAudienceResponse.ClassifyConnected(200, body, out _, out bool not));
            Assert.IsFalse(not, $"'{body}': only the boolean true says the moment restarted");
        }

        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Transient,
            ChatAudienceResponse.ClassifyConnected(200, "{\"success\":true,\"restarted\":true}", out _, out bool noMoment));
        Assert.IsFalse(noMoment, "an answer that names no moment says nothing at all");
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Rejected, ChatAudienceResponse.ClassifyConnected(403, Refusal, out _, out bool refused));
        Assert.IsFalse(refused);
    }

    [Test]
    public void ConnectedAnswer_ARestoredConnection_NamesTheEarlierMoment_AndNoRestart()
    {
        // The previous number paired again the same day: the server restores the earlier connection and says so — an EARLIER
        // moment than the one the app mirrors, restarted false, restored true. The app reads the moment, «not restarted» and
        // «restored» (which withdraws the app's own «Кто из них клиенты?» for that pairing); the list read after the answer
        // brings the restored connection's decisions back.
        const long mirrored = 1760000000000L + 2 * 3_600_000L;
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Answered, ChatAudienceResponse.ClassifyConnected(200,
            "{\"success\":true,\"connectedAt\":1760000000000,\"restarted\":false,\"restored\":true}", out long at, out bool restarted,
            out bool restored));
        Assert.AreEqual(1760000000000L, at, "the restored connection's earlier moment");
        Assert.IsFalse(restarted);
        Assert.IsTrue(restored, "the server's word: the earlier connection is back");
        Assert.IsFalse(ChatAudienceConnectPolicy.NumberRestarted(restarted, mirrored, at),
            "an earlier moment than the mirrored one is no restart: the server's word decides");
    }

    [Test]
    public void ConnectedAnswer_SaysRestoredOnlyByTheBooleanTrue()
    {
        foreach (string body in new[]
                 {
                     "{\"success\":true,\"connectedAt\":1760000000000,\"restarted\":false,\"restored\":false}",
                     "{\"success\":true,\"connectedAt\":1760000000000,\"restarted\":true}",
                     "{\"success\":true,\"connectedAt\":1760000000000,\"restored\":\"true\"}",
                     "{\"success\":true,\"connectedAt\":1760000000000,\"restored\":1}",
                     "{\"success\":true,\"connectedAt\":1760000000000,\"restored\":null}",
                 })
        {
            Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Answered, ChatAudienceResponse.ClassifyConnected(200, body, out _, out _, out bool not));
            Assert.IsFalse(not, $"'{body}': only the boolean true says the connection was restored");
        }

        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Transient,
            ChatAudienceResponse.ClassifyConnected(200, "{\"success\":true,\"restored\":true}", out _, out _, out bool noMoment));
        Assert.IsFalse(noMoment, "an answer that names no moment says nothing at all");
        Assert.AreEqual(ChatAudienceSyncQueue.BatchStatus.Rejected, ChatAudienceResponse.ClassifyConnected(403, Refusal, out _, out _, out bool refused));
        Assert.IsFalse(refused);
    }

    [TestCase(400, ChatAudienceSyncQueue.BatchStatus.Rejected, TestName = "EveryOp_400_BadRequest_IsARefusal")]
    [TestCase(401, ChatAudienceSyncQueue.BatchStatus.Rejected, TestName = "EveryOp_401_AfterTheRefreshRetry_IsARefusal")]
    [TestCase(403, ChatAudienceSyncQueue.BatchStatus.Rejected, TestName = "EveryOp_403_NotTheTokensProfile_IsARefusal")]
    [TestCase(404, ChatAudienceSyncQueue.BatchStatus.Transient, TestName = "EveryOp_404_WebhookNotDeployed_IsARetry")]
    [TestCase(409, ChatAudienceSyncQueue.BatchStatus.Transient, TestName = "EveryOp_409_IsARetry")]
    [TestCase(500, ChatAudienceSyncQueue.BatchStatus.Transient, TestName = "EveryOp_500_DbUnavailable_IsARetry")]
    [TestCase(502, ChatAudienceSyncQueue.BatchStatus.Transient, TestName = "EveryOp_502_IsARetry")]
    [TestCase(0, ChatAudienceSyncQueue.BatchStatus.Transient, TestName = "EveryOp_NoAnswer_IsARetry")]
    public void EveryOp_TellsARefusalFromAFault(long code, ChatAudienceSyncQueue.BatchStatus expected)
    {
        Assert.AreEqual(expected, ChatAudienceResponse.ClassifyList(code, Refusal, out var profiles));
        Assert.IsNull(profiles);
        Assert.AreEqual(expected, ChatAudienceResponse.ClassifySet(code, Refusal, out var rows));
        Assert.IsNull(rows);
        Assert.AreEqual(expected, ChatAudienceResponse.ClassifyConnected(code, Refusal, out long at));
        Assert.AreEqual(0, at);
    }
}

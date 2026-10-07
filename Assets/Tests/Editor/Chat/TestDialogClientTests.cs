using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Stage 7.1: the test-dialog envelope parses, every failure has a NAMED reason, the target channel is the
/// connected one, and the transport + sheet wiring are source-guarded (authorised transport only, busy latch
/// armed before the call, «тест пройден» written only from a real reply, the sheet is a back-router target).
/// </summary>
public class TestDialogClientTests
{
    private static string Src(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, rel));

    private static string MethodBody(string text, string method)
    {
        int i = text.IndexOf(method + "(", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(i, 0, $"{method} not found");
        int open = text.IndexOf('{', i);
        int depth = 0;
        for (int k = open; k < text.Length; k++)
        {
            if (text[k] == '{') depth++;
            else if (text[k] == '}' && --depth == 0) return text.Substring(open, k - open + 1);
        }
        Assert.Fail($"{method}: unbalanced braces"); return null;
    }

    [Test]
    public void Parse_SuccessEnvelope_IsOkAndNeverSent()
    {
        var r = TestDialogResponse.Parse("{\"success\":true,\"reply\":\"Здравствуйте! Модель авто уточните, пожалуйста.\",\"sources\":{\"catalog\":true,\"contacts\":false,\"businessText\":true,\"documents\":[{\"snippet\":\"Product1: Колодки\",\"fileId\":null}]},\"wouldHandoff\":null,\"wouldSend\":true,\"sent\":false,\"quotaCounted\":false,\"correlationId\":\"1546\"}");
        Assert.IsNotNull(r);
        Assert.IsTrue(r.success && r.wouldSend && !r.sent && !r.quotaCounted);
        Assert.IsTrue(r.sources.catalog && !r.sources.contacts && r.sources.businessText);
        Assert.AreEqual(1, r.sources.documents.Count);
        Assert.IsNull(r.wouldHandoff);
        Assert.AreEqual("1546", r.correlationId);
        Assert.IsNull(TestDialogClient.ClassifyFailure(true, 200, r));
    }

    [Test]
    public void Parse_HandoffEnvelope_ReportsTheActionAsData()
    {
        var r = TestDialogResponse.Parse("{\"success\":true,\"reply\":\"Передал менеджеру\",\"sources\":{},\"wouldHandoff\":{\"reason\":\"order\",\"name\":\"Айдос\",\"phone\":\"+7\",\"summary\":\"колодки\"},\"wouldSend\":true,\"sent\":false,\"quotaCounted\":false,\"correlationId\":\"9\"}");
        Assert.AreEqual("order", r.wouldHandoff.reason);
        Assert.AreEqual(0, r.sources.documents.Count);
        Assert.IsNull(TestDialogResponse.Parse("not json"));
        Assert.IsNull(TestDialogResponse.Parse(""));
    }

    [Test]
    public void ClassifyFailure_NamesEveryReason()
    {
        Assert.AreEqual(TestDialogResult.KindNetwork, TestDialogClient.ClassifyFailure(false, 0, null));
        Assert.AreEqual(TestDialogResult.KindUnauthorized, TestDialogClient.ClassifyFailure(false, 401, null));
        Assert.AreEqual(TestDialogResult.KindForbidden, TestDialogClient.ClassifyFailure(false, 403, TestDialogResponse.Parse("{\"error\":\"forbidden\"}")));
        Assert.AreEqual(TestDialogResult.KindPromptUnavailable, TestDialogClient.ClassifyFailure(true, 200, TestDialogResponse.Parse("{\"success\":false,\"error\":\"bot_prompt_unavailable\"}")));
        Assert.AreEqual(TestDialogResult.KindGenerationFailed, TestDialogClient.ClassifyFailure(true, 200, TestDialogResponse.Parse("{\"success\":false,\"error\":\"generation_failed\",\"correlationId\":\"5\"}")));
        Assert.AreEqual(TestDialogResult.KindBadRequest, TestDialogClient.ClassifyFailure(true, 400, TestDialogResponse.Parse("{\"success\":false,\"error\":\"bad_request\"}")));
        Assert.AreEqual(TestDialogResult.KindUnknown, TestDialogClient.ClassifyFailure(true, 200, TestDialogResponse.Parse("{\"success\":true,\"reply\":\"\"}")), "an empty reply is not a pass");
        foreach (string kind in new[] { TestDialogResult.KindNetwork, TestDialogResult.KindForbidden, TestDialogResult.KindUnauthorized, TestDialogResult.KindPromptUnavailable, TestDialogResult.KindGenerationFailed, TestDialogResult.KindUnknown })
            Assert.IsFalse(string.IsNullOrEmpty(BotReadinessCopy.ErrorFor(kind)) || BotReadinessCopy.ErrorFor(kind).Contains("_"), kind);
        var failed = new TestDialogResult(null, TestDialogResult.KindNetwork);
        Assert.IsFalse(failed.IsOk);
    }

    [Test]
    public void AppendTurn_KeepsTheLastTenTurnsNewestLast()
    {
        var h = TestDialogClient.AppendTurn(null, "привет", "Здравствуйте!");
        Assert.AreEqual(2, h.Count);
        Assert.AreEqual("client", h[0].role);
        Assert.AreEqual("business", h[1].role);
        for (int i = 0; i < 8; i++) h = TestDialogClient.AppendTurn(h, "q" + i, "a" + i);
        Assert.AreEqual(TestDialogClient.MaxHistoryTurns, h.Count);
        Assert.AreEqual("a7", h[h.Count - 1].text);
        Assert.AreEqual(1, TestDialogClient.AppendTurn(null, "x", null).Count, "a failed reply adds only the client turn");
    }

    [Test]
    public void Target_PrefersTheConnectedChannel_NoneWhenNeither()
    {
        var wa = BotTestTarget.Resolve("wa1", "wf1", "tg1", "wf2");
        Assert.AreEqual("whatsapp", wa.Channel); Assert.AreEqual("wa1", wa.ProfileId); Assert.AreEqual("wf1", wa.WorkflowId);
        var tg = BotTestTarget.Resolve("", "-1", "tg1", "wf2");
        Assert.AreEqual("telegram", tg.Channel);
        Assert.IsNull(BotTestTarget.Resolve("wa1", "-1", "", ""), "an authed profile without a bound workflow cannot be tested");
        Assert.IsNull(BotTestTarget.Resolve(null, null, null, null));
    }

    [Test]
    public void Wiring_TransportOnlyThroughAuthorizedRequest()
    {
        string client = Src("Scripts/Chat/TestDialogClient.cs");
        Assert.IsTrue(client.Contains("AuthorizedRequest.Send("), "the test call must carry the install's capability token");
        Assert.IsFalse(client.Contains(".SendWebRequest("), "no bare transport: the gate binds profile/workflow ids to this install");
        Assert.IsTrue(client.Contains("/webhook/TestDialog"));
    }

    [Test]
    public void Wiring_SheetArmsBusyBeforeTheCall_AndPassesOnlyOnARealReply()
    {
        string sheet = Src("Scripts/Main/BotSettings/BotTestSheet.cs");
        string send = MethodBody(sheet, "private static void OnSend");
        int busy = send.IndexOf("busy = true", System.StringComparison.Ordinal);
        int call = send.IndexOf("TestDialogClient.Send(", System.StringComparison.Ordinal);
        Assert.IsTrue(busy >= 0 && call > busy, "the busy latch is armed BEFORE the request (no double send)");
        // «тест пройден» only from a real reply: the verdict is the pure policy's (a reply → 1, a bot-side failure → 0)
        string verdictOf = MethodBody(Src("Scripts/Main/BotReadiness.cs"), "public static int? VerdictOf");
        int ok = verdictOf.IndexOf("if (result.IsOk) return 1;", System.StringComparison.Ordinal);
        Assert.IsTrue(ok >= 0, "«тест пройден» is written only from a real reply");
        Assert.IsFalse(sheet.Contains("sent = true"), "the sheet never claims a client received anything");
        Assert.IsTrue(Src("Scripts/Main/AndroidBackRouter.cs").Contains("BotTestSheet.IsShowing"), "Android Back must close the sheet like its own button");
        Assert.IsTrue(MethodBody(Src("Scripts/Main/BotSettings.cs"), "public void OnEnable").Contains("EnsureTestRow();"), "the row is added when the screen opens");
    }

    /// <summary>
    /// R16 source guard (the behaviour is pinned on the real sheet by BotTestSheetLateResultTests): the callback carries
    /// the request's ticket, the result goes through the pure policy BEFORE anything on the screen changes, a stale
    /// result returns before busy/history/reply are touched, and «тест пройден» is written only under the TICKET's bot.
    /// </summary>
    [Test]
    public void Wiring_ResultBelongsToTheTicketNotToTheScreen()
    {
        string sheet = Src("Scripts/Main/BotSettings/BotTestSheet.cs");
        string send = MethodBody(sheet, "private static void OnSend");
        int ticket = send.IndexOf("new BotTestTicket(++screenSeq, botName, target.ProfileId, target.WorkflowId)", System.StringComparison.Ordinal);
        int call = send.IndexOf("TestDialogClient.Send(", System.StringComparison.Ordinal);
        Assert.IsTrue(ticket >= 0 && call > ticket, "the ticket (seq, bot, profile, workflow) is captured before the call");
        Assert.IsTrue(send.Contains("OnResult(ticket, ticketBot, text, result)"), "the callback carries the ticket, not just the text");

        string result = MethodBody(sheet, "private static void OnResult");
        int decide = result.IndexOf("BotTestResultPolicy.Decide(ticket, screenSeq,", System.StringComparison.Ordinal);
        int stale = result.IndexOf("if (!outcome.ApplyToScreen)", System.StringComparison.Ordinal);
        int staleReturn = result.IndexOf("return;", stale, System.StringComparison.Ordinal);
        int busyOff = result.IndexOf("busy = false", System.StringComparison.Ordinal);
        int firstScreenWrite = result.IndexOf("replyTmp.text", System.StringComparison.Ordinal);
        int historyWrite = result.IndexOf("history = ", System.StringComparison.Ordinal);
        Assert.IsTrue(decide >= 0 && stale > decide, "the policy decides before anything else");
        Assert.IsTrue(staleReturn > stale && busyOff > staleReturn && firstScreenWrite > staleReturn && historyWrite > staleReturn,
            "a stale result returns before the busy latch, the reply or the history of the current screen change");
        Assert.IsFalse(result.Contains("PlayerPrefs.SetInt(botName"), "never under the SCREEN's bot name");
        Assert.IsTrue(MethodBody(sheet, "private static void RecordVerdict").Contains("PlayerPrefs.SetInt(ticket.BotName + LastTestKeySuffix, outcome.Verdict.Value)"),
            "the verdict is written under the ticket's own bot");
        Assert.IsTrue(MethodBody(sheet, "public static void Show").Contains("screenSeq++"), "opening the sheet retires everything still in flight");
        // V11: the screen ends on «Закрыть»/Back, not on the next Show, and the target is re-decided, never kept from Show
        Assert.IsTrue(MethodBody(sheet, "public static void Hide").Contains("screenSeq++"), "closing the sheet retires everything still in flight");
        Assert.IsTrue(MethodBody(sheet, "private static void RefreshReadiness").Contains("BotTestTarget.Resolve("), "the readiness re-decides the target from the bot as it is now");
        int refresh = send.IndexOf("RefreshReadiness();", System.StringComparison.Ordinal);
        int noTarget = send.IndexOf("if (target == null)", System.StringComparison.Ordinal);
        Assert.IsTrue(refresh >= 0 && noTarget > refresh, "a send re-decides the target before it uses it");
        Assert.IsTrue(MethodBody(Src("Scripts/Main/BotReadiness.cs"), "public static BotTestOutcome Decide").Contains("ticket.Seq == currentSeq && botAlive"),
            "the screen belongs to a live bot");
        Assert.IsTrue(MethodBody(Src("Scripts/Main/Bot.cs"), "public void DeleteBotLocally").Contains("BotTestSheet.LastTestKeySuffix"),
            "a deleted bot leaves no «тест пройден» behind");
    }

    [Test]
    public void Policy_VerdictOnlyFromTheBotItself()
    {
        Assert.AreEqual(1, BotTestResultPolicy.VerdictOf(Ok()));
        Assert.AreEqual(0, BotTestResultPolicy.VerdictOf(new TestDialogResult(null, TestDialogResult.KindGenerationFailed)));
        Assert.AreEqual(0, BotTestResultPolicy.VerdictOf(new TestDialogResult(null, TestDialogResult.KindPromptUnavailable)));
        foreach (string kind in new[] { TestDialogResult.KindNetwork, TestDialogResult.KindUnauthorized, TestDialogResult.KindForbidden, TestDialogResult.KindBadRequest, TestDialogResult.KindUnknown })
            Assert.IsNull(BotTestResultPolicy.VerdictOf(new TestDialogResult(null, kind)), kind + " says nothing about the bot");
        Assert.IsNull(BotTestResultPolicy.VerdictOf(null));
    }

    [Test]
    public void Policy_StaleTicket_KeepsItsOwnVerdict_ButNeverTheScreen()
    {
        var a = new BotTestTicket(2, "Bot0", "wa-A", "wfA");
        var targetA = BotTestTarget.Resolve("wa-A", "wfA", "-1", "-1");
        var stale = BotTestResultPolicy.Decide(a, 3, true, targetA, 0, Ok());           // B's sheet opened since (seq 3)
        Assert.IsFalse(stale.ApplyToScreen, "a newer screen owns reply, history and busy");
        Assert.AreEqual(1, stale.Verdict, "the answer is still A's own verdict");
        var current = BotTestResultPolicy.Decide(a, 2, true, targetA, 0, Ok());
        Assert.IsTrue(current.ApplyToScreen);
        Assert.AreEqual(1, current.Verdict);
    }

    [Test]
    public void Policy_NoVerdict_ForADeletedOrRetargetedBot_OrOverANewerVerdict()
    {
        var a = new BotTestTicket(5, "Bot0", "wa-A", "wfA");
        var targetA = BotTestTarget.Resolve("wa-A", "wfA", "-1", "-1");
        Assert.IsNull(BotTestResultPolicy.Decide(a, 5, false, null, 0, Ok()).Verdict, "deleted bot");
        Assert.IsNull(BotTestResultPolicy.Decide(a, 5, true, BotTestTarget.Resolve("wa-A", "wfA2", "-1", "-1"), 0, Ok()).Verdict, "another workflow answers now");
        Assert.IsNull(BotTestResultPolicy.Decide(a, 5, true, BotTestTarget.Resolve("wa-X", "wfA", "-1", "-1"), 0, Ok()).Verdict, "another profile answers now");
        Assert.IsNull(BotTestResultPolicy.Decide(a, 5, true, null, 0, Ok()).Verdict, "no channel connected any more");
        Assert.IsNull(BotTestResultPolicy.Decide(a, 9, true, targetA, 7, Ok()).Verdict, "an older answer never overwrites a newer test's verdict");
        Assert.AreEqual(0, BotTestResultPolicy.Decide(new BotTestTicket(8, "Bot0", "wa-A", "wfA"), 9, true, targetA, 7, new TestDialogResult(null, TestDialogResult.KindGenerationFailed)).Verdict);
        var tg = new BotTestTicket(3, "Bot1", "tg-B", "wfB");
        Assert.AreEqual(1, BotTestResultPolicy.Decide(tg, 3, true, BotTestTarget.Resolve("-1", "-1", "tg-B", "wfB"), 0, Ok()).Verdict, "Telegram-only bot");
    }

    [Test]
    public void PromptVersion_ParsesAndNamesTheTestedVersion()
    {
        var r = TestDialogResponse.Parse("{\"success\":true,\"reply\":\"ok\",\"correlationId\":\"1546\",\"promptVersion\":{\"source\":\"published\",\"versionId\":\"a4ad53ce-e6c3-4307\",\"draftPending\":true}}");
        Assert.AreEqual("published", r.promptVersion.source);
        Assert.IsTrue(r.promptVersion.draftPending);
        Assert.AreEqual("ID: 1546 · версия a4ad53ce, есть неопубликованные изменения",
            BotReadinessCopy.CorrelationLine(r.correlationId, BotReadinessCopy.VersionNote(r.promptVersion.source, r.promptVersion.versionId, r.promptVersion.draftPending)));
        Assert.AreEqual("канал выключен, проверены текущие настройки b2c1ff9e", BotReadinessCopy.VersionNote(TestDialogPromptVersion.SourceDraft, "b2c1ff9e-5ee0", false));
        Assert.AreEqual("версия abc", BotReadinessCopy.VersionNote(TestDialogPromptVersion.SourcePublished, "abc", false));
        Assert.AreEqual("", BotReadinessCopy.VersionNote("published", null, false), "an older server says nothing — no version shown");
        Assert.AreEqual("ID: 7", BotReadinessCopy.CorrelationLine("7", ""));
        Assert.AreEqual("", BotReadinessCopy.CorrelationLine(null, ""));
        Assert.IsNull(TestDialogResponse.Parse("{\"success\":true,\"reply\":\"x\"}").promptVersion, "the field is optional");
    }

    private static TestDialogResult Ok() => new TestDialogResult(new TestDialogResponse { success = true, reply = "ok" }, null);
}

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
        string result = MethodBody(sheet, "private static void OnResult");
        int ok = result.IndexOf("if (result.IsOk)", System.StringComparison.Ordinal);
        int pass = result.IndexOf("LastTestKeySuffix, 1)", System.StringComparison.Ordinal);
        Assert.IsTrue(ok >= 0 && pass > ok, "«тест пройден» is written only from a real reply");
        Assert.IsFalse(sheet.Contains("sent = true"), "the sheet never claims a client received anything");
        Assert.IsTrue(Src("Scripts/Main/AndroidBackRouter.cs").Contains("BotTestSheet.IsShowing"), "Android Back must close the sheet like its own button");
        Assert.IsTrue(MethodBody(Src("Scripts/Main/BotSettings.cs"), "public void OnEnable").Contains("EnsureTestRow();"), "the row is added when the screen opens");
    }
}

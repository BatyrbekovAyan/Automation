using System;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;

/// <summary>
/// R16 (iteration 2026-09-27): a test answer belongs to the bot and the request it was sent for. Drives the REAL
/// «Проверка бота» sheet (Show → type → submit → Hide → Show another bot) with the network replaced by
/// <see cref="BotTestSheet.TransportOverride"/>, which PARKS each request so the test answers it later — a controlled
/// response delay. A late answer for bot A must not touch bot B's screen, history, busy latch or «тест пройден»;
/// the verdict is written only under the ticket's own bot, only on a bot-side outcome, never for a deleted or
/// re-targeted bot, and an older answer never overwrites a newer verdict.
/// </summary>
public class BotTestSheetLateResultTests
{
    private const string Prefix = "R16SheetBot";
    private readonly List<(TestDialogRequest request, Action<TestDialogResult> done)> parked = new();
    private readonly List<GameObject> bots = new();

    [SetUp]
    public void SetUp()
    {
        parked.Clear();
        ClearVerdicts();   // the sheet calls PlayerPrefs.Save(): a previous Editor process can leave a saved verdict behind
        BotTestSheet.TransportOverride = (request, done) => parked.Add((request, done));
    }

    private static void ClearVerdicts()
    {
        foreach (string s in new[] { "A", "B", "C" }) PlayerPrefs.DeleteKey(Prefix + s + BotTestSheet.LastTestKeySuffix);
        PlayerPrefs.Save();
    }

    [TearDown]
    public void TearDown()
    {
        BotTestSheet.TransportOverride = null;
        DG.Tweening.DOTween.KillAll();
        var canvas = GameObject.Find("BotTestSheetCanvas");
        if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
        foreach (var go in bots)
        {
            if (go == null) continue;
            UnityEngine.Object.DestroyImmediate(go);
        }
        ClearVerdicts();
        bots.Clear();
    }

    // ---- helpers -----------------------------------------------------------------------------

    private Bot MakeBot(string suffix)
    {
        var go = new GameObject(Prefix + suffix);
        bots.Add(go);
        var bot = go.AddComponent<Bot>();
        bot.whatsappProfileId = "wa-" + suffix;
        bot.whatsappWorkflowId = "wf" + suffix + "0000001";
        bot.telegramProfileId = "-1";
        bot.telegramWorkflowId = "-1";
        return bot;
    }

    private static void Show(Bot bot) => BotTestSheet.Show(bot.gameObject.name, bot, null, null);

    private static Transform Content => GameObject.Find("BotTestSheetCanvas").transform.Find("Panel/Content");
    private static string Reply => Content.Find("Reply").GetComponent<TextMeshProUGUI>().text;
    private static string Readiness => Content.Find("Readiness").GetComponent<TextMeshProUGUI>().text;
    private static string Meta => Content.Find("Meta").GetComponent<TextMeshProUGUI>().text;

    private static void Submit(string text)
    {
        var field = Content.Find("MessageField").GetComponent<TMP_InputField>();
        field.text = text;
        field.onSubmit.Invoke(text);
    }

    private static int Verdict(string suffix) => PlayerPrefs.GetInt(Prefix + suffix + BotTestSheet.LastTestKeySuffix, -1);

    private static TestDialogResult Ok(string reply, string correlation = "1") =>
        new TestDialogResult(new TestDialogResponse { success = true, reply = reply, correlationId = correlation, sources = new TestDialogSources() }, null);

    private static TestDialogResult Fail(string kind, string wireError = null) =>
        new TestDialogResult(wireError != null ? TestDialogResponse.Parse("{\"success\":false,\"error\":\"" + wireError + "\",\"correlationId\":\"9\"}") : null, kind);

    private static bool TestLineIs(string readiness, string line) => readiness.Contains(BotReadinessCopy.Bullet + line + "</color>");

    // ---- the defect: A's late answer lands on B ------------------------------------------------

    [Test]
    public void LateReplyOfBotA_AfterSwitchingToBotB_CountsForAOnly()
    {
        var a = MakeBot("A");
        var b = MakeBot("B");
        Show(a);
        Submit("Сколько стоит букет?");
        Assert.AreEqual(1, parked.Count);
        BotTestSheet.Hide();
        Show(b);

        parked[0].done(Ok("Букет A стоит 3200"));

        Assert.AreEqual("", Reply, "A's answer must never appear on B's screen");
        Assert.AreEqual("", Meta);
        Assert.AreEqual(-1, Verdict("B"), "B was never tested — A's answer must not mark it passed");
        Assert.IsTrue(TestLineIs(Readiness, BotReadinessCopy.TestNone), "B's readiness still says «not tested»: " + Readiness);
        Assert.AreEqual(1, Verdict("A"), "the answer is A's real verdict and is recorded for A");

        Submit("А у вас?");
        Assert.AreEqual(2, parked.Count, "B's own test is sent");
        Assert.AreEqual("wa-B", parked[1].request.profileId);
        Assert.AreEqual(0, parked[1].request.history.Count, "A's turns must not leak into B's conversation");
    }

    [Test]
    public void LateBotSideFailureOfBotA_DoesNotFailBotB()
    {
        var a = MakeBot("A");
        var b = MakeBot("B");
        PlayerPrefs.SetInt(Prefix + "B" + BotTestSheet.LastTestKeySuffix, 1);
        Show(a);
        Submit("Привет");
        BotTestSheet.Hide();
        Show(b);

        parked[0].done(Fail(TestDialogResult.KindGenerationFailed, "generation_failed"));

        Assert.AreEqual(1, Verdict("B"), "B passed its own test — A's failure must not fail it");
        Assert.AreEqual(0, Verdict("A"), "A's bot-side failure is A's verdict");
        Assert.AreEqual("", Reply, "A's error text must not appear on B's screen");
    }

    [Test]
    public void StaleAnswer_DoesNotReleaseTheCurrentRequestsBusyLatch()
    {
        var a = MakeBot("A");
        var b = MakeBot("B");
        Show(a);
        Submit("Вопрос A");
        BotTestSheet.Hide();
        Show(b);
        Submit("Вопрос B");
        Assert.AreEqual(2, parked.Count);

        parked[0].done(Ok("Ответ A"));

        Assert.AreEqual(BotReadinessCopy.Sending, Reply, "B is still waiting for ITS answer");
        Submit("Вопрос B ещё раз");
        Assert.AreEqual(2, parked.Count, "B's busy latch holds: no second parallel request");

        parked[1].done(Ok("Ответ B"));
        Assert.AreEqual("Ответ B", Reply);
        Assert.AreEqual(1, Verdict("B"));
    }

    [Test]
    public void SameBotReopened_OldAnswerStaysOutOfTheNewConversation_AndNeverOverwritesANewerVerdict()
    {
        var a = MakeBot("A");
        Show(a);
        Submit("q1");
        BotTestSheet.Hide();
        Show(a);
        Submit("q3");
        Assert.AreEqual(2, parked.Count);

        parked[1].done(Fail(TestDialogResult.KindGenerationFailed, "generation_failed"));
        Assert.AreEqual(0, Verdict("A"));
        string shown = Reply;

        parked[0].done(Ok("r1"));
        Assert.AreEqual(0, Verdict("A"), "an OLDER answer must not overwrite the newer test's verdict");
        Assert.AreEqual(shown, Reply, "the old answer stays off the reopened screen");

        Submit("q4");
        Assert.AreEqual(3, parked.Count);
        Assert.IsFalse(parked[2].request.history.Exists(t => t.text == "r1" || t.text == "q1"), "the old exchange must not join the new conversation");
    }

    [Test]
    public void DeletedOrRetargetedBot_GetsNoVerdictFromALateAnswer()
    {
        var a = MakeBot("A");
        var c = MakeBot("C");
        Show(a);
        Submit("q");
        BotTestSheet.Hide();
        UnityEngine.Object.DestroyImmediate(a.gameObject);
        parked[0].done(Ok("r"));
        Assert.AreEqual(-1, Verdict("A"), "a deleted bot gets no verdict (no orphan key)");

        Show(c);
        Submit("q");
        BotTestSheet.Hide();
        c.whatsappWorkflowId = "wfC0000002";          // re-authorised: another workflow answers now
        parked[1].done(Ok("r"));
        Assert.AreEqual(-1, Verdict("C"), "an answer about the old workflow is not a verdict about the new one");
    }

    [Test]
    public void TransportOrAuthFailure_IsNoVerdict()
    {
        var a = MakeBot("A");
        PlayerPrefs.SetInt(Prefix + "A" + BotTestSheet.LastTestKeySuffix, 1);
        Show(a);
        Submit("q");
        parked[0].done(Fail(TestDialogResult.KindNetwork));
        Assert.AreEqual(1, Verdict("A"), "no connection says nothing about the bot");
        Assert.AreEqual(BotReadinessCopy.ErrorNetwork, Reply, "the current screen still names the reason");
        Submit("q");
        parked[1].done(Fail(TestDialogResult.KindUnauthorized, "unauthorized"));
        Assert.AreEqual(1, Verdict("A"));
        Submit("q");
        parked[2].done(Fail(TestDialogResult.KindPromptUnavailable, "bot_prompt_unavailable"));
        Assert.AreEqual(0, Verdict("A"), "a bot whose prompt cannot be read failed the test");
    }

    [Test]
    public void NormalPath_ReplyHistoryAndPass_Unchanged()
    {
        var a = MakeBot("A");
        Show(a);
        Submit("q1");
        Assert.AreEqual(BotReadinessCopy.Sending, Reply);
        parked[0].done(Ok("r1"));
        Assert.AreEqual("r1", Reply);
        Assert.AreEqual(1, Verdict("A"));
        Assert.IsTrue(TestLineIs(Readiness, BotReadinessCopy.TestOk), Readiness);
        Submit("q2");
        Assert.AreEqual(2, parked.Count);
        var h = parked[1].request.history;
        Assert.AreEqual(2, h.Count);
        Assert.AreEqual("q1", h[0].text);
        Assert.AreEqual("r1", h[1].text);
    }
}

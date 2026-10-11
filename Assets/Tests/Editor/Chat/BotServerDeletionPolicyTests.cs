using NUnit.Framework;

/// <summary>
/// V12 (iteration 2026-09-28): the pure verdict of a bot's server deletion (<see cref="BotServerDeletion"/>) and its copy.
/// The bot leaves the phone only on Done / NothingOnServer; a temporary failure keeps it (retry), a final refusal keeps
/// it with the honest phone-only choice (owner decision 2026-10-07).
/// </summary>
public class BotServerDeletionPolicyTests
{
    private const string Done = "{\"success\":true,\"deletedChunks\":3,\"deletedMemoryRows\":316}";
    private const string WorkflowStateOk = "{\"success\":true}";
    private const string Elsewhere = "{\"success\":false,\"error\":\"forbidden\",\"reason\":\"owned_elsewhere\"}";
    private const string Unproven = "{\"success\":false,\"error\":\"forbidden\",\"reason\":\"unproven\"}";

    [Test]
    public void ANeverAuthorisedBot_HasNoServerTrace()
    {
        Assert.IsFalse(BotServerDeletion.HasServerTrace("-1", "-1", "-1", "-1"));
        Assert.IsFalse(BotServerDeletion.HasServerTrace(null, "", "-1", null));
        Assert.IsTrue(BotServerDeletion.HasServerTrace("-1", "-1", "wa-profile", "-1"), "a profile without a clone still has rows to delete");
        Assert.IsTrue(BotServerDeletion.HasServerTrace("-1", "wfCloneTg0001", "-1", "-1"));
    }

    [TestCase(0, TestName = "no answer at all (no connection / timeout)")]
    [TestCase(500)] [TestCase(502)] [TestCase(503)] [TestCase(504)]
    [TestCase(401, TestName = "a session the one refresh did not cure")]
    [TestCase(408)] [TestCase(429)]
    public void TransientAnswers_KeepTheBot_ForARetry(long code)
    {
        Assert.AreEqual(BotServerDeletion.Step.Transient, BotServerDeletion.ClassifyServerDelete(code, ""));
        Assert.AreEqual(BotServerDeletion.Step.Transient, BotServerDeletion.ClassifyProfileDelete(code));
        Assert.AreEqual(BotServerDeletion.Verdict.Retry,
            BotServerDeletion.Combine(new[] { (BotServerDeletion.ClassifyServerDelete(code, ""), "") }).Verdict);
    }

    [Test]
    public void AFinalRefusal_KeepsTheBot_AndNamesWhy()
    {
        Assert.AreEqual(BotServerDeletion.Step.Refused, BotServerDeletion.ClassifyServerDelete(403, Elsewhere));
        Assert.AreEqual(BotServerDeletion.Step.Refused, BotServerDeletion.ClassifyServerDelete(400, "{\"success\":false,\"error\":\"bad_request\"}"));
        var elsewhere = BotServerDeletion.Combine(new[] { (BotServerDeletion.Step.Refused, Elsewhere) });
        var unproven = BotServerDeletion.Combine(new[] { (BotServerDeletion.Step.Refused, Unproven) });
        var opaque = BotServerDeletion.Combine(new[] { (BotServerDeletion.Step.Refused, "<html>forbidden</html>") });
        Assert.AreEqual(BotServerDeletion.Verdict.Refused, elsewhere.Verdict);
        Assert.AreEqual(BotServerDeletion.ReasonOwnedElsewhere, elsewhere.Reason);
        Assert.AreEqual(BotServerDeletion.ReasonUnproven, unproven.Reason);
        Assert.AreEqual(BotServerDeletion.ReasonUnproven, opaque.Reason, "an unreadable refusal is never claimed to be another device's");
        Assert.IsFalse(elsewhere.RemovesBot);
    }

    [Test]
    public void ProfileUnlink_AnyDefinitiveAnswerLetsTheDeletionGoOn()
    {
        // done, Wappi refusing a profile it no longer has, or the proxy's 403 for a profile this install no longer owns
        // live (the sweep already deleted it; or another install owns it — Delete Bot Files then refuses with the reason)
        foreach (long code in new long[] { 200, 400, 403, 404 })
            Assert.AreEqual(BotServerDeletion.Step.Ok, BotServerDeletion.ClassifyProfileDelete(code), $"profile.delete {code}");
    }

    [Test]
    public void Done_OnlyWhenEveryStepSaidSo()
    {
        Assert.AreEqual(BotServerDeletion.Step.Ok, BotServerDeletion.ClassifyServerDelete(200, Done));
        Assert.AreEqual(BotServerDeletion.Step.Transient, BotServerDeletion.ClassifyServerDelete(200, "{\"success\":false}"),
            "a 200 that says it failed is not a deletion");
        var all = BotServerDeletion.Combine(new[] { (BotServerDeletion.Step.Ok, ""), (BotServerDeletion.Step.Ok, Done), (BotServerDeletion.Step.Ok, "") });
        Assert.AreEqual(BotServerDeletion.Verdict.Done, all.Verdict);
        Assert.IsTrue(all.RemovesBot);
        Assert.IsTrue(BotServerDeletion.Outcome.NothingOnServer.RemovesBot);
        Assert.IsFalse(BotServerDeletion.Outcome.Retry.RemovesBot);
    }

    // C1 (independent review 2026-10-09, docs/audits/2026-10-09-independent-review-evidence/auth-client/): a 2xx counted as
    // a deletion unless it SAID success:false — an empty body, {}, n8n's own «Workflow executed successfully» (a run
    // that ended before its Respond node), a proxy's HTML all came out Ok → Done → the bot left the phone with nothing
    // on the server confirming it. A deletion step is done only on the server's positive word; any other 2xx proved
    // nothing and is a retry (the bot stays, «попробуйте ещё раз»), never a refusal — the server named no reason.
    [TestCase(200, "")]
    [TestCase(200, null)]
    [TestCase(200, "   ")]
    [TestCase(200, "{}")]
    [TestCase(200, "{\"message\":\"Workflow executed successfully\"}")]
    [TestCase(200, "<html>upstream error</html>")]
    [TestCase(200, "{\"success\":false}")]
    [TestCase(200, "{\"success\":\"true\"}")]
    [TestCase(200, "{\"success\":1}")]
    [TestCase(200, "{\"success\":null}")]
    [TestCase(200, "{\"Success\":true}")]
    [TestCase(200, "{\"data\":{\"success\":true}}")]
    [TestCase(200, "[{\"success\":true}]")]
    [TestCase(200, "true")]
    [TestCase(200, "{\"success\":true")]
    [TestCase(200, "{\"success\":false,\"success\":true}")]
    [TestCase(201, "{}")]
    [TestCase(204, "")]
    public void AnUnconfirmed2xx_IsARetry_NeverADeletion(long code, string body)
    {
        var step = BotServerDeletion.ClassifyServerDelete(code, body);
        Assert.AreEqual(BotServerDeletion.Step.Transient, step, $"{code} «{body}» is not the server's word that the data is gone");
        // The review's own chain: the profile unlink went on, THIS answer, the clone confirmed.
        var outcome = BotServerDeletion.Combine(new[] { (BotServerDeletion.Step.Ok, ""), (step, body),
                                                        (BotServerDeletion.ClassifyServerDelete(200, WorkflowStateOk), WorkflowStateOk) });
        Assert.AreEqual(BotServerDeletion.Verdict.Retry, outcome.Verdict);
        Assert.IsFalse(outcome.RemovesBot, "the bot must stay on the phone");
    }

    [Test]
    public void OnlyTheServersPositiveWord_IsADeletion()
    {
        // Delete Bot Files: «Respond», reached only past «Deletion Settled?» — success:true + the counters.
        Assert.AreEqual(BotServerDeletion.Step.Ok, BotServerDeletion.ClassifyServerDelete(200, Done));
        // Set Workflow State(delete): «Respond OK». The server turns the n8n API's 404 (the clone is already gone) into
        // this same 200 — a repeated delete is confirmed, the client never sees that 404.
        Assert.AreEqual(BotServerDeletion.Step.Ok, BotServerDeletion.ClassifyServerDelete(200, WorkflowStateOk));
        Assert.AreEqual(BotServerDeletion.Step.Ok, BotServerDeletion.ClassifyServerDelete(200, " { \"success\" : true }\n"));
        var all = BotServerDeletion.Combine(new[]
        {
            (BotServerDeletion.ClassifyProfileDelete(200), "{\"status\":\"done\"}"),
            (BotServerDeletion.ClassifyServerDelete(200, Done), Done),
            (BotServerDeletion.ClassifyServerDelete(200, WorkflowStateOk), WorkflowStateOk),
        });
        Assert.AreEqual(BotServerDeletion.Verdict.Done, all.Verdict);
        Assert.IsTrue(all.RemovesBot);
    }

    [Test]
    public void A4xx_StaysTheServersFinalWord_WhateverItsBody()
    {
        // C1 changes only the 2xx side: a 4xx is still a refusal, its reason read from the body when there is one.
        Assert.AreEqual(BotServerDeletion.Step.Refused, BotServerDeletion.ClassifyServerDelete(403, "<html>forbidden</html>"));
        Assert.AreEqual(BotServerDeletion.Step.Refused, BotServerDeletion.ClassifyServerDelete(403, ""));
        Assert.AreEqual(BotServerDeletion.Step.Refused, BotServerDeletion.ClassifyServerDelete(400, WorkflowStateOk));
        var unreadable = BotServerDeletion.Combine(new[] { (BotServerDeletion.ClassifyServerDelete(403, ""), "") });
        Assert.AreEqual(BotServerDeletion.Verdict.Refused, unreadable.Verdict);
        Assert.AreEqual(BotServerDeletion.ReasonUnproven, unreadable.Reason);
    }

    [Test]
    public void TheFirstStepThatIsNotOk_Decides()
    {
        var retryFirst = BotServerDeletion.Combine(new[] { (BotServerDeletion.Step.Transient, ""), (BotServerDeletion.Step.Refused, Elsewhere) });
        var refusedFirst = BotServerDeletion.Combine(new[] { (BotServerDeletion.Step.Ok, ""), (BotServerDeletion.Step.Refused, Unproven),
                                                             (BotServerDeletion.Step.Transient, "") });
        Assert.AreEqual(BotServerDeletion.Verdict.Retry, retryFirst.Verdict);
        Assert.AreEqual(BotServerDeletion.Verdict.Refused, refusedFirst.Verdict);
        Assert.IsNull(BotServerDeletion.StopAt(BotServerDeletion.Step.Ok, Done));
    }

    [Test]
    public void Copy_IsHonestAboutWhereTheDataStays()
    {
        StringAssert.Contains("поддержк", BotDeletionCopy.RefusedBody(BotServerDeletion.ReasonUnproven));
        StringAssert.Contains("другом устройстве", BotDeletionCopy.RefusedBody(BotServerDeletion.ReasonOwnedElsewhere));
        StringAssert.DoesNotContain("поддержк", BotDeletionCopy.RefusedBody(BotServerDeletion.ReasonOwnedElsewhere),
            "another install's data is not something support should delete on this owner's request");
        Assert.AreEqual("Удалить только с телефона", BotDeletionCopy.PhoneOnlyAction);
    }

    [TestCase(1, "1 бота")]
    [TestCase(2, "2 ботов")]
    [TestCase(5, "5 ботов")]
    [TestCase(11, "11 ботов")]
    [TestCase(21, "21 бота")]
    public void WipeRefusedBody_AgreesWithTheCount(int count, string expected)
    {
        StringAssert.Contains($"удаление {expected}.", BotDeletionCopy.WipeRefusedBody(count, false));
    }
}

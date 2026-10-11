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

    // C1b (2026-10-11, the C1 remainder): a 4xx counted as the server's final word whatever its body — so n8n's own 404
    // for a webhook that is not registered (the workflow switched off), a proxy's 403 page or an empty 403 ended in
    // «Сервер не подтвердил, что бот ваш» and the «Удалить только с телефона» exit, although the server had decided
    // nothing. A refusal is a 4xx that carries OUR server's refusal body: a JSON object with the boolean
    // `"success": false` and an `error` / `reason` the two deleting webhooks refuse with (BotDeletionWiringTests reads
    // them from the canon). Any other 4xx (and any other non-2xx) proves nothing: a retry, the bot stays.
    private const string WebhookNotRegistered =
        "{\"code\":404,\"message\":\"The requested webhook \\\"POST DeleteBotFiles\\\" is not registered.\"," +
        "\"hint\":\"The workflow must be active for a production URL to run successfully.\"}";

    [TestCase(404, WebhookNotRegistered, TestName = "404 n8n webhook not registered")]
    [TestCase(404, "", TestName = "404 empty")]
    [TestCase(403, "", TestName = "403 empty")]
    [TestCase(403, null, TestName = "403 null")]
    [TestCase(403, "<html>forbidden</html>", TestName = "403 proxy page")]
    [TestCase(403, "{}", TestName = "403 empty object")]
    [TestCase(403, "{\"success\":false}", TestName = "403 success false without error or reason")]
    [TestCase(403, "{\"success\":false,\"error\":\"\",\"reason\":\"\"}", TestName = "403 blank error and reason")]
    [TestCase(403, "{\"success\":false,\"error\":\"db_unavailable\",\"reason\":null}", TestName = "403 db fault body")]
    [TestCase(400, "{\"success\":false,\"error\":\"delete_failed\"}", TestName = "400 delete_failed")]
    [TestCase(400, "{\"success\":false,\"error\":\"storage_unavailable\"}", TestName = "400 storage_unavailable")]
    [TestCase(403, "{\"success\":false,\"error\":\"unauthorized\",\"reason\":\"unauthorized:expired\"}", TestName = "403 a session fault")]
    [TestCase(403, "{\"success\":false,\"error\":\"unauthorized\",\"reason\":\"forbidden:\"}", TestName = "403 gate prefix without a field")]
    [TestCase(403, "{\"error\":\"forbidden\",\"reason\":\"owned_elsewhere\"}", TestName = "403 refusal words without success false")]
    [TestCase(403, "{\"success\":\"false\",\"error\":\"forbidden\",\"reason\":\"owned_elsewhere\"}", TestName = "403 success as a string")]
    [TestCase(403, "{\"success\":0,\"error\":\"forbidden\"}", TestName = "403 success as a number")]
    [TestCase(403, "{\"success\":false,\"error\":\"Forbidden\"}", TestName = "403 another case of the error")]
    [TestCase(403, "{\"success\":false,\"error\":[\"forbidden\"]}", TestName = "403 error as an array")]
    [TestCase(403, "{\"data\":{\"success\":false,\"error\":\"forbidden\"}}", TestName = "403 refusal nested")]
    [TestCase(403, "[{\"success\":false,\"error\":\"forbidden\"}]", TestName = "403 refusal in an array")]
    [TestCase(403, "{\"success\":false,\"error\":\"forbidden\",\"success\":false}", TestName = "403 repeated key")]
    [TestCase(403, "{\"success\":false,\"error\":\"forbidden\"", TestName = "403 truncated refusal")]
    [TestCase(400, "{\"success\":true}", TestName = "400 with the success word")]
    [TestCase(409, "{\"message\":\"conflict\"}", TestName = "409 foreign body")]
    [TestCase(413, "<html>Request Entity Too Large</html>", TestName = "413 proxy page")]
    [TestCase(302, "", TestName = "302 redirect")]
    public void A4xxWithoutTheServersRefusal_IsARetry_NeverARefusal(long code, string body)
    {
        var step = BotServerDeletion.ClassifyServerDelete(code, body);
        Assert.AreEqual(BotServerDeletion.Step.Transient, step, $"{code} «{body}» is not the server's refusal");
        var outcome = BotServerDeletion.Combine(new[] { (BotServerDeletion.Step.Ok, ""), (step, body) });
        Assert.AreEqual(BotServerDeletion.Verdict.Retry, outcome.Verdict, "no phone-only exit without the server's own refusal");
        Assert.IsFalse(outcome.RemovesBot, "the bot must stay on the phone");
    }

    [TestCase(403, "{\"success\":false,\"error\":\"forbidden\",\"reason\":\"owned_elsewhere\"}", BotServerDeletion.ReasonOwnedElsewhere,
              TestName = "403 Delete Ownership owned_elsewhere")]
    [TestCase(403, "{\"success\":false,\"error\":\"forbidden\",\"reason\":\"unproven\"}", BotServerDeletion.ReasonUnproven,
              TestName = "403 Delete Ownership unproven")]
    [TestCase(403, "{\"success\":false,\"error\":\"unauthorized\",\"reason\":\"forbidden:workflowId\"}", BotServerDeletion.ReasonUnproven,
              TestName = "403 the gate's forbidden field")]
    [TestCase(400, "{\"success\":false,\"error\":\"bad_request\"}", BotServerDeletion.ReasonUnproven,
              TestName = "400 Set Workflow State bad_request")]
    [TestCase(400, "{\"success\":false,\"error\":\"protected_workflow\"}", BotServerDeletion.ReasonUnproven,
              TestName = "400 Set Workflow State protected_workflow")]
    [TestCase(403, " {\n \"success\" : false , \"error\" : \"forbidden\" , \"reason\" : \"owned_elsewhere\" }\n", BotServerDeletion.ReasonOwnedElsewhere,
              TestName = "403 refusal with whitespace")]
    public void OnlyTheServersRefusalBody_IsARefusal(long code, string body, string reason)
    {
        var step = BotServerDeletion.ClassifyServerDelete(code, body);
        Assert.AreEqual(BotServerDeletion.Step.Refused, step, $"{code} «{body}» is the server's final word");
        var outcome = BotServerDeletion.Combine(new[] { (BotServerDeletion.Step.Ok, ""), (step, body) });
        Assert.AreEqual(BotServerDeletion.Verdict.Refused, outcome.Verdict);
        Assert.AreEqual(reason, outcome.Reason);
        Assert.IsFalse(outcome.RemovesBot, "a refusal keeps the bot until the owner chooses the phone-only exit");
    }

    [Test]
    public void ARefusalBody_UnderASessionFault_IsStillARetry()
    {
        // 401 = a session the one refresh did not cure: whatever the body says, the next attempt may succeed.
        Assert.AreEqual(BotServerDeletion.Step.Transient, BotServerDeletion.ClassifyServerDelete(401, Elsewhere));
        Assert.AreEqual(BotServerDeletion.Step.Transient, BotServerDeletion.ClassifyServerDelete(503, Unproven));
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

    [Test]
    public void RetryCopy_IsTrueForEveryRetry()
    {
        // C1b: a retry is no connection, a 5xx, a session the refresh did not cure, a 2xx without the server's
        // `success: true` and a 4xx without its refusal body — «Нет связи с сервером» was true only for the first.
        // The copy says what is known in every case: no confirmation came, the bot is still on the phone, try again.
        foreach (var (name, text) in new[] { ("RetryBody", BotDeletionCopy.RetryBody), ("WipeRetryBody", BotDeletionCopy.WipeRetryBody) })
        {
            StringAssert.DoesNotContain("связ", text, $"{name}: a retry is not always a lost connection");
            StringAssert.Contains("подтверждение удаления", text.ToLowerInvariant(), $"{name}: says that the server's confirmation did not come");
            StringAssert.Contains("попробуйте ещё раз", text, $"{name}: a retry offers the retry");
            StringAssert.DoesNotContain("поддержк", text, $"{name}: support is the refusal's way out, not the retry's");
            StringAssert.DoesNotContain("Сервер не подтвердил", text,
                $"{name}: that is how the REFUSAL texts open — the retry must not read like the server's decision");
        }
        StringAssert.Contains("Бот остался на телефоне", BotDeletionCopy.RetryBody,
            "the one thing known for sure: the bot is still on the phone (an unconfirmed 2xx may have deleted the server data)");
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

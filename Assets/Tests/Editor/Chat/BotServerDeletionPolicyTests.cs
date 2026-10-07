using NUnit.Framework;

/// <summary>
/// V12 (iteration 2026-09-28): the pure verdict of a bot's server deletion (<see cref="BotServerDeletion"/>) and its copy.
/// The bot leaves the phone only on Done / NothingOnServer; a temporary failure keeps it (retry), a final refusal keeps
/// it with the honest phone-only choice (owner decision 2026-10-07).
/// </summary>
public class BotServerDeletionPolicyTests
{
    private const string Done = "{\"success\":true,\"deletedChunks\":3,\"deletedMemoryRows\":316}";
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

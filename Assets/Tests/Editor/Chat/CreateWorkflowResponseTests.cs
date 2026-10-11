using NUnit.Framework;

/// <summary>
/// F1 + F2 (plan 2026-10-11 §1.7, task T8): the client's reading of a Create{Whatsapp,Telegram}Workflow answer. An id is
/// the ONLY success; channel_limit is the one refusal after which the profile is gone (the server deleted it); every other
/// refusal, an unreadable answer and no answer at all are retries — the profile stays and the Create is repeated (it is
/// idempotent on the server since F1). Plus the two retry decisions of the settings screen.
/// </summary>
public class CreateWorkflowResponseTests
{
    [TestCase("{\"id\":\"wfNewClone0001\"}", TestName = "a pre-F1 server: the id alone")]
    [TestCase("{\"id\":\"wfNewClone0001\",\"created\":true,\"adoptedProfiles\":[]}", TestName = "a new clone")]
    [TestCase("{\"id\":\"wfNewClone0001\",\"created\":false,\"adoptedProfiles\":[]}", TestName = "the bound clone reused")]
    [TestCase("{ \"id\" : \"wfNewClone0001\" , \"name\" : \"Bot0 WhatsApp\" }", TestName = "whitespace and other fields")]
    public void AnId_IsSuccess(string body)
    {
        var result = CreateWorkflowResponse.Classify(200, body);
        Assert.AreEqual(CreateWorkflowResponse.Kind.Success, result.Kind);
        Assert.AreEqual("wfNewClone0001", result.Id);
        Assert.IsNotNull(result.AdoptedProfiles, "never null");
    }

    [Test]
    public void Success_CarriesCreatedAndTheAdoptedProfiles()
    {
        var result = CreateWorkflowResponse.Classify(200,
            "{\"id\":\"wfNewClone0002\",\"created\":true,\"adoptedProfiles\":[\"wa-retired-1\",\"wa-old-2\"]}");
        Assert.AreEqual(CreateWorkflowResponse.Kind.Success, result.Kind);
        Assert.IsTrue(result.Created);
        CollectionAssert.AreEqual(new[] { "wa-retired-1", "wa-old-2" }, result.AdoptedProfiles);

        var legacy = CreateWorkflowResponse.Classify(200, "{\"id\":\"wfNewClone0002\"}");
        Assert.IsFalse(legacy.Created, "an older server that does not say");
        Assert.IsEmpty(legacy.AdoptedProfiles, "nothing adopted — the retired set stays");

        var odd = CreateWorkflowResponse.Classify(200,
            "{\"id\":\"wfNewClone0002\",\"created\":\"yes\",\"adoptedProfiles\":[\"wa-1\",7,null,\"\",{\"x\":1}]}");
        Assert.IsFalse(odd.Created, "only a boolean true is «created»");
        CollectionAssert.AreEqual(new[] { "wa-1" }, odd.AdoptedProfiles, "only non-empty strings name a profile");
        Assert.IsEmpty(CreateWorkflowResponse.Classify(200, "{\"id\":\"w\",\"adoptedProfiles\":\"wa-1\"}").AdoptedProfiles,
            "not an array — nothing adopted");
    }

    [Test]
    public void ChannelLimit_TheServerDeletedTheProfile()
    {
        var result = CreateWorkflowResponse.Classify(200, "{\"success\":false,\"error\":\"channel_limit\"}");
        Assert.AreEqual(CreateWorkflowResponse.Kind.ProfileGone, result.Kind);
        Assert.AreEqual("channel_limit", result.Error);
        Assert.IsNull(result.Id);
        Assert.AreEqual(CreateWorkflowResponse.Kind.ProfileGone,
            CreateWorkflowResponse.Classify(403, "{\"success\":false,\"error\":\"channel_limit\"}").Kind,
            "the server's word decides whatever the status");
    }

    [TestCase(200, "{\"success\":false,\"error\":\"reply_mode_not_saved\"}", TestName = "reply_mode_not_saved")]
    [TestCase(200, "{\"success\":false,\"error\":\"clone_plan_unavailable\"}", TestName = "clone_plan_unavailable")]
    [TestCase(200, "{\"success\":false,\"error\":\"clone_unavailable\"}", TestName = "clone_unavailable")]
    [TestCase(200, "{\"success\":false,\"error\":\"create_failed\"}", TestName = "create_failed")]
    [TestCase(200, "{\"success\":false,\"error\":\"profile_owned\"}", TestName = "profile_owned (legacy)")]
    [TestCase(200, "{\"success\":false,\"error\":\"unauthorized\",\"reason\":\"forbidden:WhatsappProfileId\"}", TestName = "the gate")]
    [TestCase(200, "{\"success\":false,\"error\":\"Channel_Limit\"}", TestName = "another case of channel_limit")]
    [TestCase(200, "{\"success\":false,\"error\":[\"channel_limit\"]}", TestName = "channel_limit in an array")]
    [TestCase(200, "{\"data\":{\"error\":\"channel_limit\"}}", TestName = "channel_limit nested")]
    [TestCase(0, null, TestName = "no answer (no connection / timeout)")]
    [TestCase(0, "", TestName = "no answer, empty body")]
    [TestCase(502, "<html>Bad Gateway</html>", TestName = "a proxy page")]
    [TestCase(500, "{\"message\":\"Error in workflow\"}", TestName = "n8n's own error")]
    [TestCase(200, "", TestName = "an empty 200")]
    [TestCase(200, "{}", TestName = "an empty object")]
    [TestCase(200, "{\"message\":\"Workflow executed successfully\"}", TestName = "n8n's lastNode without the Respond")]
    [TestCase(200, "[{\"id\":\"wfNewClone0001\"}]", TestName = "an array")]
    [TestCase(200, "{\"id\":\"\"}", TestName = "an empty id")]
    [TestCase(200, "{\"id\":null}", TestName = "a null id")]
    [TestCase(200, "{\"id\":12345}", TestName = "a number id")]
    [TestCase(200, "{\"id\":\"wfA\",\"id\":\"wfB\"}", TestName = "a repeated id")]
    [TestCase(200, "{\"id\":\"wfNewClone0001\"", TestName = "a truncated body")]
    [TestCase(200, "{\"success\":false,\"id\":\"wfNewClone0001\"}", TestName = "an id with success false")]
    [TestCase(500, "{\"id\":\"wfNewClone0001\"}", TestName = "an id with a 5xx")]
    public void EverythingElse_IsARetry_TheProfileStays(long code, string body)
    {
        var result = CreateWorkflowResponse.Classify(code, body);
        Assert.AreEqual(CreateWorkflowResponse.Kind.Retryable, result.Kind, $"{code} {body}");
        Assert.IsNull(result.Id);
    }

    [Test]
    public void TheContractConstants()
    {
        Assert.AreEqual(45, CreateWorkflowResponse.TimeoutSeconds, "plan §1.7: 45 s for both paths");
        Assert.AreEqual("CreateOrigin", CreateWorkflowResponse.FieldCreateOrigin);
        Assert.AreEqual("PreviousProfileId", CreateWorkflowResponse.FieldPreviousProfileId);
        Assert.AreEqual("start", CreateWorkflowResponse.OriginStart);
        Assert.AreEqual("edit", CreateWorkflowResponse.OriginEdit);
        Assert.AreEqual("WhatsappCreatePending", CreateWorkflowResponse.PendingSuffix(ChatChannel.WhatsApp));
        Assert.AreEqual("TelegramCreatePending", CreateWorkflowResponse.PendingSuffix(ChatChannel.Telegram));
    }

    [Test]
    public void RetryOnSettingsOpen_OnlyARecordedFailure_OfAnAuthorisedOwnedProfile()
    {
        Assert.IsTrue(CreateWorkflowResponse.RetryOnSettingsOpen(authorized: true, ownedByToken: true, createPending: true));
        Assert.IsFalse(CreateWorkflowResponse.RetryOnSettingsOpen(true, true, false), "nothing failed — nothing to repeat");
        Assert.IsFalse(CreateWorkflowResponse.RetryOnSettingsOpen(false, true, true), "not paired — the auth flow comes first");
        Assert.IsFalse(CreateWorkflowResponse.RetryOnSettingsOpen(true, false, true),
            "the token does not list the profile — Create's gate would refuse it again (the claim flow comes first)");
    }

    [Test]
    public void RetryOnChannelOn_ARecordedFailure_OrNoCloneAtAll()
    {
        Assert.IsTrue(CreateWorkflowResponse.RetryOnChannelOn(true, true, true, "wfClone0001"), "a recorded failure");
        Assert.IsTrue(CreateWorkflowResponse.RetryOnChannelOn(true, true, false, "-1"), "a paired channel without a clone");
        Assert.IsTrue(CreateWorkflowResponse.RetryOnChannelOn(true, true, false, ""));
        Assert.IsTrue(CreateWorkflowResponse.RetryOnChannelOn(true, true, false, null));
        Assert.IsFalse(CreateWorkflowResponse.RetryOnChannelOn(true, true, false, "wfClone0001"), "an ordinary connected channel");
        Assert.IsFalse(CreateWorkflowResponse.RetryOnChannelOn(false, true, true, "-1"), "not paired");
        Assert.IsFalse(CreateWorkflowResponse.RetryOnChannelOn(true, false, true, "-1"), "not this install's profile");
    }
}

using NUnit.Framework;

// Pins the composed VALUE of the two credential-replacement webhook requests
// (WorkflowStateRequest / SupportRelayRequest, Assets/Scripts/Main/N8nWebhookRequests.cs).
// The server-side workflows (Tools/n8n/build-client-webhooks.py) validate exactly
// these shapes: { workflowId, action } with the closed 3-action enum, and { text }.
// A3 (2026-10-14): VerifyIdentityRequest — { appUserId, originalAppUserId } for the identity-heal webhook
// (Tools/n8n/build-verify-identity.py: the gate binds appUserId to the token's billing id, Validate reads the hint).
public class N8nWebhookRequestTests
{
    [Test]
    public void WorkflowState_ComposesExactBody()
    {
        Assert.AreEqual(
            "{\"workflowId\":\"abc123XYZ0\",\"action\":\"activate\"}",
            WorkflowStateRequest.ComposeBody("abc123XYZ0", WorkflowStateRequest.Activate));
    }

    [Test]
    public void WorkflowState_ActionsMatchServerEnum()
    {
        // The webhook's Validate node whitelists exactly these three strings.
        Assert.AreEqual("activate", WorkflowStateRequest.Activate);
        Assert.AreEqual("deactivate", WorkflowStateRequest.Deactivate);
        Assert.AreEqual("delete", WorkflowStateRequest.Delete);
        Assert.AreEqual("activate", WorkflowStateRequest.ToggleAction(true));
        Assert.AreEqual("deactivate", WorkflowStateRequest.ToggleAction(false));
    }

    [Test]
    public void WorkflowState_PathMatchesDeployedWebhook()
    {
        Assert.AreEqual("/webhook/SetWorkflowState", WorkflowStateRequest.Path);
    }

    [Test]
    public void SupportRelay_PathMatchesDeployedWebhook()
    {
        Assert.AreEqual("/webhook/SupportMessage", SupportRelayRequest.Path);
    }

    [Test]
    public void SupportRelay_EscapesUserText()
    {
        // Support messages are free-form user text — quotes and newlines must
        // survive JSON composition (the old WWWForm path had no such hazard).
        Assert.AreEqual(
            "{\"text\":\"a \\\"b\\\"\\nc\"}",
            SupportRelayRequest.ComposeBody("a \"b\"\nc"));
    }

    private const string Current = "$RCAnonymousID:a3a3a3a3c4c4c4c4a3a3a3a3c4c4c4c4";
    private const string Original = "$RCAnonymousID:b5b5b5b5d6d6d6d6b5b5b5b5d6d6d6d6";

    [Test]
    public void VerifyIdentity_PathMatchesDeployedWebhook()
    {
        Assert.AreEqual("/webhook/VerifyIdentity", VerifyIdentityRequest.Path);
    }

    [Test]
    public void VerifyIdentity_ComposesExactBody()
    {
        // The gate compares appUserId with the token's billing id; Validate reads originalAppUserId as the hint.
        Assert.AreEqual(
            "{\"appUserId\":\"" + Current + "\",\"originalAppUserId\":\"" + Original + "\"}",
            VerifyIdentityRequest.ComposeBody(Current, Original));
    }

    [Test]
    public void VerifyIdentity_AMissingHintTravelsAsNull()
    {
        // The server reads a null hint as «no hint» (RevenueCat's own alias list then decides alone).
        Assert.AreEqual(
            "{\"appUserId\":\"" + Current + "\",\"originalAppUserId\":null}",
            VerifyIdentityRequest.ComposeBody(Current, null));
    }

    [TestCase("{\"success\":true,\"aliases\":1,\"consolidated\":1,\"rekeyed\":0,\"truncated\":false}", true)]
    [TestCase("{\"success\":true,\"aliases\":1,\"consolidated\":0,\"rekeyed\":2,\"truncated\":false}", true)]
    [TestCase("{\"success\":true,\"aliases\":1,\"consolidated\":0,\"rekeyed\":0,\"truncated\":false}", false)]   // a repeat: nothing moved
    [TestCase("{\"success\":false,\"reason\":\"hint_not_alias\"}", false)]
    [TestCase("{\"success\":false,\"reason\":\"rc_unavailable\"}", false)]
    [TestCase("{\"success\":false,\"error\":\"unauthorized\",\"reason\":\"unauthorized:missing\"}", false)]
    [TestCase("{\"success\":\"yes\",\"consolidated\":1}", false)]
    [TestCase("[1,2]", false)]
    [TestCase("<html>502</html>", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void VerifyIdentity_OnlyAHealThatMovedSomethingCounts(string body, bool healed)
    {
        Assert.AreEqual(healed, VerifyIdentityRequest.HealedSomething(body));
    }
}

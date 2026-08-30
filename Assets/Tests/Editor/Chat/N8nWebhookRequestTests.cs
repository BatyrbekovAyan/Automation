using NUnit.Framework;

// Pins the composed VALUE of the two credential-replacement webhook requests
// (WorkflowStateRequest / SupportRelayRequest, Assets/Scripts/Main/N8nWebhookRequests.cs).
// The server-side workflows (Tools/n8n/build-client-webhooks.py) validate exactly
// these shapes: { workflowId, action } with the closed 3-action enum, and { text }.
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
}

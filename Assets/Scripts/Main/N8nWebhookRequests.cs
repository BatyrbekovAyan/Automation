using Newtonsoft.Json;

// Pure request composers for the two n8n webhooks that replaced client-side
// credentials (2026-08-31, store-submission blocker): the instance-admin
// X-N8N-API-KEY and the Telegram support-bot token used to ship in plaintext
// inside the APK/IPA via StreamingAssets/secrets.json. Both webhooks follow the
// app's existing URL-is-the-secret posture (see UsageClient); the server-side
// workflows validate the body and hold the real credentials. Deployed by
// Tools/n8n/build-client-webhooks.py.

// POST {n8nBaseUrl}/webhook/SetWorkflowState — activate/deactivate/delete one
// BOT workflow by id. The server refuses the canonical infra workflow ids
// (protected_workflow), so this can only touch workflows the app itself created.
public static class WorkflowStateRequest
{
    public const string Path = "/webhook/SetWorkflowState";
    public const string Activate = "activate";
    public const string Deactivate = "deactivate";
    public const string Delete = "delete";

    public static string ToggleAction(bool active) => active ? Activate : Deactivate;

    public static string ComposeBody(string workflowId, string action) =>
        JsonConvert.SerializeObject(new BodyShape { workflowId = workflowId, action = action });

    [System.Serializable]
    private class BodyShape
    {
        public string workflowId;
        public string action;
    }
}

// POST {n8nBaseUrl}/webhook/SupportMessage — relays a support-form message to the
// owner's Telegram chat. The bot token and chat id live only on the server.
public static class SupportRelayRequest
{
    public const string Path = "/webhook/SupportMessage";

    public static string ComposeBody(string text) =>
        JsonConvert.SerializeObject(new BodyShape { text = text });

    [System.Serializable]
    private class BodyShape
    {
        public string text;
    }
}

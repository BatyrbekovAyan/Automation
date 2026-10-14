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

// POST {n8nBaseUrl}/webhook/VerifyIdentity — A3 identity-heal after a reinstall (crVerifyIdent001, build-verify-identity.py).
// appUserId must be the token's billing id (the gate refuses anything else, and a token without one); originalAppUserId is
// RevenueCat's CustomerInfo.OriginalAppUserId — a HINT the server checks against RevenueCat's own alias list (asked with the
// server's key), never a recipient. The server consolidates the ids RevenueCat lists INTO the token's id and answers
// { success: true, aliases, consolidated, rekeyed, truncated } or { success: false, reason }.
public static class VerifyIdentityRequest
{
    public const string Path = "/webhook/VerifyIdentity";

    public static string ComposeBody(string appUserId, string originalAppUserId) =>
        JsonConvert.SerializeObject(new BodyShape { appUserId = appUserId, originalAppUserId = originalAppUserId });

    /// <summary>
    /// True when the server's answer says the heal MOVED something (an alias consolidated or a bot re-keyed) — the usage
    /// snapshot of this id is stale then. A refusal, a repeat that changed nothing, or an unreadable body changes nothing.
    /// </summary>
    public static bool HealedSomething(string json)
    {
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            var jo = Newtonsoft.Json.Linq.JObject.Parse(json);
            return jo.Value<bool?>("success") == true
                && ((jo.Value<int?>("consolidated") ?? 0) > 0 || (jo.Value<int?>("rekeyed") ?? 0) > 0);
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    [System.Serializable]
    private class BodyShape
    {
        public string appUserId;
        public string originalAppUserId;
    }
}

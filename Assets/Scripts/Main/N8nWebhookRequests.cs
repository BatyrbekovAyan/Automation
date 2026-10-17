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
    /// True when the body is the server's positive answer: a JSON object whose <c>success</c> is the BOOLEAN true (a repeat
    /// that moved nothing included). Anything else — a refusal, a string "true", garbage — is not. The identity-heal latch is
    /// stamped only on this (IdentityHealTrigger.ShouldStamp).
    /// </summary>
    public static bool Succeeded(string json) => Parse(json) is { } jo && IsTrue(jo["success"]);

    /// <summary>
    /// True when the server's answer says the heal MOVED something (an alias consolidated or a bot re-keyed) — the usage
    /// snapshot of this id is stale then. A refusal, a repeat that changed nothing, or an unreadable body changes nothing.
    /// </summary>
    public static bool HealedSomething(string json)
    {
        if (!(Parse(json) is { } jo) || !IsTrue(jo["success"])) return false;
        try
        {
            return (jo.Value<int?>("consolidated") ?? 0) > 0 || (jo.Value<int?>("rekeyed") ?? 0) > 0;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static bool IsTrue(Newtonsoft.Json.Linq.JToken token)
        => token != null && token.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)token;

    private static Newtonsoft.Json.Linq.JObject Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return Newtonsoft.Json.Linq.JObject.Parse(json);
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    [System.Serializable]
    private class BodyShape
    {
        public string appUserId;
        public string originalAppUserId;
    }
}

// POST {n8nBaseUrl}/webhook/SetSchedule — Part B «расписание бота» (crSetSchedule001, build-set-schedule.py). Token-gated:
// every profileIds entry must be one of the token's profiles (403 forbidden:profileIds otherwise) — the client never sends
// a sentinel or an id the token does not list (ScheduleSyncQueue.Sendable). Every field is required, also for a disabled
// schedule (re-enabling restores the window). version: a positive integer, strictly increasing per bot
// (ScheduleSyncQueue.NextVersion) — the server writes a row only over an OLDER version and answers
// { success: true, results: [{ profileId, applied, version }] }; applied:false = it already holds a NEWER one.
// Refusals: 400 bad_request (+ reason) / bad_timezone, 401 / 403 from the gate, 500 db_unavailable.
public static class SetScheduleRequest
{
    public const string Path = "/webhook/SetSchedule";

    public static string ComposeBody(System.Collections.Generic.IReadOnlyList<string> profileIds, bool enabled, int daysMask,
                                     int startMin, int endMin, string timeZone, long version) =>
        JsonConvert.SerializeObject(new BodyShape
        {
            profileIds = profileIds != null ? System.Linq.Enumerable.ToArray(profileIds) : new string[0],
            enabled = enabled,
            daysMask = daysMask,
            startMin = startMin,
            endMin = endMin,
            timeZone = timeZone,
            version = version,
        });

    [System.Serializable]
    private class BodyShape
    {
        public string[] profileIds;
        public bool enabled;
        public int daysMask;
        public int startMin;
        public int endMin;
        public string timeZone;
        public long version;
    }
}

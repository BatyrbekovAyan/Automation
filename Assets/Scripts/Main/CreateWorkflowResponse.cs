using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// F1 + F2 (plan 2026-10-11 §1.7, task T8): the client's half of the Create{Whatsapp,Telegram}Workflow contract — the form
/// fields it adds, how long it waits, and how it reads the answer. Pure, EditMode-tested.
///
/// Since F1 the server's Create is «ensure exactly one bound clone for this channel of this bot»: the same profile reuses
/// its bound clone, a new profile of the same bot gets a new clone that inherits everything the bot had on the server
/// (memory, history, outcomes, price lists), and any failure after the clone was created deletes it again and answers
/// WITHOUT an id. So a repeated Create is harmless — which is what lets a failed one from the settings be retried.
///
/// The answer (HTTP 200, n8n lastNode): success <c>{id, created, adoptedProfiles}</c>; a refusal <c>{success:false, error}</c>
/// with NO id. Only <c>channel_limit</c> means the server deleted the profile; every other refusal and no answer at all
/// keep the paired profile for a retry.
/// </summary>
public static class CreateWorkflowResponse
{
    public enum Kind
    {
        /// <summary>The answer carries the clone id — the channel is connected.</summary>
        Success,
        /// <summary><c>channel_limit</c>: the server deleted the profile (no free channel on the plan).</summary>
        ProfileGone,
        /// <summary>Any other refusal, an unreadable answer, no answer: the profile stays, the Create is repeated later.</summary>
        Retryable,
    }

    /// <summary>Both paths (plan §1.7). A timeout after the server finished is harmless: the retry reuses the clone.</summary>
    public const int TimeoutSeconds = 45;

    public const string FieldCreateOrigin = "CreateOrigin";
    public const string FieldPreviousProfileId = "PreviousProfileId";
    /// <summary>The wizard — a new bot: the server never looks for a predecessor.</summary>
    public const string OriginStart = "start";
    /// <summary>The settings — an existing bot: the server may adopt the channel's proven predecessor.</summary>
    public const string OriginEdit = "edit";

    public const string ErrorChannelLimit = "channel_limit";

    // Per-bot keys (bot-persistence): {bot}{Whatsapp|Telegram}CreatePending — int 1 while a Create from the settings failed
    // and must be repeated for the channel's (kept) profile. Deleted with the bot (Bot.DeleteBotLocally).
    public const string WhatsappPendingSuffix = "WhatsappCreatePending";
    public const string TelegramPendingSuffix = "TelegramCreatePending";

    public static string PendingSuffix(ChatChannel channel) =>
        channel == ChatChannel.Telegram ? TelegramPendingSuffix : WhatsappPendingSuffix;

    public readonly struct Result
    {
        public readonly Kind Kind;
        /// <summary>The bound clone's workflow id (Success only).</summary>
        public readonly string Id;
        /// <summary>The server made a new clone (false = it reused the bound one, or an older server that does not say).</summary>
        public readonly bool Created;
        /// <summary>The retired profiles whose data the server moved to this connection (never null).</summary>
        public readonly IReadOnlyList<string> AdoptedProfiles;
        /// <summary>The server's refusal word, if it named one.</summary>
        public readonly string Error;

        public Result(Kind kind, string id = null, bool created = false, IReadOnlyList<string> adoptedProfiles = null, string error = null)
        {
            Kind = kind;
            Id = id;
            Created = created;
            AdoptedProfiles = adoptedProfiles ?? Array.Empty<string>();
            Error = error;
        }
    }

    private static readonly JsonLoadSettings StrictLoad = new JsonLoadSettings
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
    };

    /// <summary>
    /// Reads one Create answer. <paramref name="code"/> 0 = no answer (no connection, a timeout). Success needs a 2xx and a
    /// JSON object whose own <c>id</c> is a non-empty string (and that does not say <c>success:false</c>); <c>channel_limit</c>
    /// is ProfileGone whatever the status; everything else is Retryable.
    /// </summary>
    public static Result Classify(long code, string body)
    {
        JObject answer = ParseObject(body);
        if (answer == null) return new Result(Kind.Retryable);
        string error = StringField(answer, "error");
        string id = StringField(answer, "id");
        bool refused = answer["success"] is JValue flag && flag.Type == JTokenType.Boolean && !flag.Value<bool>();
        if (code >= 200 && code < 300 && !string.IsNullOrEmpty(id) && !refused)
            return new Result(Kind.Success, id, CreatedFlag(answer), Adopted(answer), error);
        return error == ErrorChannelLimit ? new Result(Kind.ProfileGone, error: error) : new Result(Kind.Retryable, error: error);
    }

    /// <summary>
    /// Settings opened over an authorised profile: only a Create the device RECORDED as failed is repeated (a missing clone
    /// alone is not enough here — that bot may simply have its channel switched off). The token must list the profile:
    /// Create's gate refuses any other, and a retry would only fail again.
    /// </summary>
    public static bool RetryOnSettingsOpen(bool authorized, bool ownedByToken, bool createPending) =>
        authorized && ownedByToken && createPending;

    /// <summary>
    /// The channel switched on over an existing authorised profile: a recorded failed Create, or no clone at all (a Create
    /// that failed before this build recorded failures) — the owner just asked for this channel to work.
    /// </summary>
    public static bool RetryOnChannelOn(bool authorized, bool ownedByToken, bool createPending, string workflowId) =>
        authorized && ownedByToken && (createPending || !ProfileClaimPolicy.IsAuthedId(workflowId));

    private static bool CreatedFlag(JObject answer) =>
        answer["created"] is JValue created && created.Type == JTokenType.Boolean && created.Value<bool>();

    private static IReadOnlyList<string> Adopted(JObject answer)
    {
        var adopted = new List<string>();
        if (answer["adoptedProfiles"] is JArray list)
            foreach (JToken item in list)
                if (item.Type == JTokenType.String && !string.IsNullOrEmpty(item.Value<string>()))
                    adopted.Add(item.Value<string>());
        return adopted;
    }

    private static string StringField(JObject answer, string name) =>
        answer[name] is JValue value && value.Type == JTokenType.String ? value.Value<string>() : null;

    /// <summary>The body as a JSON object; null for no body, not JSON, not an object, or a repeated key.</summary>
    private static JObject ParseObject(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            return JObject.Parse(body, StrictLoad);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// V12 (iteration 2026-09-28): the verdict of a bot's SERVER deletion — what the app may do with the bot on the phone
/// once the server has answered. Before, the bot was wiped locally first and the server calls were fired without a
/// single answer being read: a refused delete (403) left the conversation memory, the history and the clone on the
/// server with nothing left in the app to delete them from.
///
/// The owner's decision (2026-10-07): a TEMPORARY failure keeps the bot («Не удалось удалить — повторите»); a FINAL
/// refusal keeps it too but offers «Удалить только с телефона» with an honest text (the server data stays; support can
/// delete it). A bot that never reached the server goes at once, with no network.
///
/// The server half runs its calls in order and the first answer that is not «done» decides (<see cref="StopAt"/>):
/// the Wappi profile unlink (while its row is still the owner's live row), then Delete Bot Files, then the clones.
/// </summary>
public static class BotServerDeletion
{
    public enum Step { Ok, Transient, Refused }

    public enum Verdict { NothingOnServer, Done, Retry, Refused }

    /// <summary>The server's refusal reason when another install owns one of the ids (Delete Ownership).</summary>
    public const string ReasonOwnedElsewhere = "owned_elsewhere";

    /// <summary>Every other final refusal: the ids could not be proven this install's (an unclaimed legacy bot, …).</summary>
    public const string ReasonUnproven = "unproven";

    public readonly struct Outcome
    {
        public readonly Verdict Verdict;
        public readonly string Reason;

        public Outcome(Verdict verdict, string reason = null)
        {
            Verdict = verdict;
            Reason = reason;
        }

        public static Outcome NothingOnServer => new Outcome(Verdict.NothingOnServer);
        public static Outcome Done => new Outcome(Verdict.Done);
        public static Outcome Retry => new Outcome(Verdict.Retry);
        public static Outcome RefusedBecause(string reason) => new Outcome(Verdict.Refused, reason ?? ReasonUnproven);

        /// <summary>True when the bot may now be removed from the phone without asking the owner again.</summary>
        public bool RemovesBot => Verdict == Verdict.Done || Verdict == Verdict.NothingOnServer;
    }

    public static bool IsSentinel(string id) => string.IsNullOrEmpty(id) || id == Bot.UnauthedProfileSentinel;

    /// <summary>False for a bot that never reached the server (all four ids are the unauthed sentinel).</summary>
    public static bool HasServerTrace(string whatsappWorkflowId, string telegramWorkflowId,
                                      string whatsappProfileId, string telegramProfileId) =>
        !(IsSentinel(whatsappWorkflowId) && IsSentinel(telegramWorkflowId)
          && IsSentinel(whatsappProfileId) && IsSentinel(telegramProfileId));

    /// <summary>
    /// F2 (T8): one channel's ids for the server deletion — the current pair and the retired pair the device moved aside
    /// when the server retired the channel's profile (<see cref="ProfileRetirement"/>). Normally a channel has one or the
    /// other; both only when a reconnect's Create did not adopt the retired profile.
    /// </summary>
    public readonly struct ChannelServerIds
    {
        public readonly string ProfileId;
        public readonly string WorkflowId;
        public readonly string RetiredProfileId;
        public readonly string RetiredWorkflowId;

        public ChannelServerIds(string profileId, string workflowId, string retiredProfileId = null, string retiredWorkflowId = null)
        {
            ProfileId = profileId;
            WorkflowId = workflowId;
            RetiredProfileId = retiredProfileId;
            RetiredWorkflowId = retiredWorkflowId;
        }

        public bool HasCurrent => !IsSentinel(ProfileId) || !IsSentinel(WorkflowId);

        /// <summary>The retired profile unless it IS the current one (an M4 marker of an unfinished app delete names it).</summary>
        public string SeparateRetiredProfileId =>
            !IsSentinel(RetiredProfileId) && RetiredProfileId != ProfileId ? RetiredProfileId : Bot.UnauthedProfileSentinel;

        public string SeparateRetiredWorkflowId =>
            !IsSentinel(RetiredWorkflowId) && RetiredWorkflowId != WorkflowId ? RetiredWorkflowId : Bot.UnauthedProfileSentinel;

        public bool HasSeparateRetired => !IsSentinel(SeparateRetiredProfileId) || !IsSentinel(SeparateRetiredWorkflowId);
    }

    /// <summary>F2 (T8): both channels' ids for the server deletion.</summary>
    public readonly struct BotServerIds
    {
        public readonly ChannelServerIds Whatsapp;
        public readonly ChannelServerIds Telegram;

        public BotServerIds(ChannelServerIds whatsapp, ChannelServerIds telegram)
        {
            Whatsapp = whatsapp;
            Telegram = telegram;
        }
    }

    /// <summary>
    /// False only for a bot that never reached the server: no current id AND no retired one. After the F2 move a bot may
    /// hold retired ids alone — its memory, history and clone are still on the server (else: the V12 regression).
    /// </summary>
    public static bool HasServerTrace(BotServerIds ids) =>
        ids.Whatsapp.HasCurrent || ids.Telegram.HasCurrent || ids.Whatsapp.HasSeparateRetired || ids.Telegram.HasSeparateRetired;

    /// <summary>
    /// The Wappi profiles to unlink: the CURRENT ones only. A retired profile is already deleted at Wappi (the sweep or
    /// the refused Create did it) — the proxy would refuse it anyway.
    /// </summary>
    public static IEnumerable<(string profileId, ChatChannel channel)> ProfilesToUnlink(BotServerIds ids)
    {
        if (!IsSentinel(ids.Whatsapp.ProfileId)) yield return (ids.Whatsapp.ProfileId, ChatChannel.WhatsApp);
        if (!IsSentinel(ids.Telegram.ProfileId)) yield return (ids.Telegram.ProfileId, ChatChannel.Telegram);
    }

    /// <summary>The clones to delete (Set Workflow State, idempotent): the current ones, then each channel's former clone.</summary>
    public static IEnumerable<string> ClonesToDelete(BotServerIds ids)
    {
        var seen = new HashSet<string>();
        foreach (string id in new[] { ids.Whatsapp.WorkflowId, ids.Telegram.WorkflowId,
                                      ids.Whatsapp.RetiredWorkflowId, ids.Telegram.RetiredWorkflowId })
            if (!IsSentinel(id) && seen.Add(id)) yield return id;
    }

    /// <summary>
    /// A failure the next attempt may not repeat: no HTTP answer at all (code 0 — no connection, a timeout), a server
    /// error or a database fault the server reports as 503, a session the one refresh did not cure, a throttle.
    /// </summary>
    public static bool IsTransient(long code) => code == 0 || code >= 500 || code == 401 || code == 408 || code == 429;

    /// <summary>
    /// The Wappi profile unlink (proxy profile.delete). Any definitive answer lets the deletion go on: «done», Wappi's own
    /// refusal of a profile it no longer has, or the proxy's 403 for a profile this install no longer owns live (the
    /// sweep already deleted it; or another install owns it — Delete Bot Files then refuses with the reason).
    /// Classified by status alone on purpose (C1): its «go on» answers include 4xx, the proxy passes Wappi's body through
    /// unchanged, and it never decides Done by itself — Delete Bot Files always runs after it (any server trace composes
    /// a payload) and must give its own positive word.
    /// </summary>
    public static Step ClassifyProfileDelete(long code) => IsTransient(code) ? Step.Transient : Step.Ok;

    /// <summary>
    /// Delete Bot Files / Set Workflow State(delete). A step is done ONLY on the server's positive word: a 2xx whose body
    /// is a JSON object with the boolean <c>"success": true</c> — the one success answer both webhooks give (Delete Bot
    /// Files' «Respond», reached only past «Deletion Settled?»; Set Workflow State's «Respond OK», which also answers a
    /// clone the n8n API no longer has — the server turns that 404 into this 200). Any other 2xx — an empty body, not
    /// JSON, <c>{}</c>, n8n's own «Workflow executed successfully» for a run that ended before its Respond node, an
    /// explicit <c>success:false</c> — proves nothing (C1, review 2026-10-09): it is a retry, the bot stays.
    /// A step is REFUSED only on the server's own refusal (C1b, 2026-10-11): a 4xx whose body is that refusal
    /// (<see cref="IsServerRefusal"/>). A 4xx without it — n8n's 404 for a webhook that is not registered (the workflow
    /// is off), a proxy's 403 page, an empty body — is a failure, not a decision: a retry, like any other answer that is
    /// neither the positive word nor the refusal. Both steps are idempotent, so a retry costs one round trip.
    /// </summary>
    public static Step ClassifyServerDelete(long code, string body)
    {
        if (IsTransient(code)) return Step.Transient;
        if (code >= 200 && code < 300) return ConfirmsSuccess(body) ? Step.Ok : Step.Transient;
        return code >= 400 && code < 500 && IsServerRefusal(body) ? Step.Refused : Step.Transient;
    }

    /// <summary>
    /// The <c>error</c> words the two deleting webhooks refuse with: Delete Ownership's «forbidden» (with its reason),
    /// Set Workflow State's Validate — «bad_request» (a malformed id/action), «protected_workflow» (an infra id). The
    /// faults those webhooks also name (db_unavailable, delete_failed, storage_unavailable…) are deliberately absent:
    /// they travel with a 5xx and are retries whatever their status. BotDeletionWiringTests reads every 4xx answer of
    /// both canonical workflows and fails on one this set (or <see cref="IsRefusalReason"/>) does not cover.
    /// </summary>
    private static readonly HashSet<string> RefusalErrors = new HashSet<string> { "forbidden", "bad_request", "protected_workflow" };

    /// <summary>The auth gate's refusal reason: <c>forbidden:&lt;field&gt;</c> — a body field the token does not own.</summary>
    private const string GateRefusalPrefix = "forbidden:";

    private static readonly JsonLoadSettings StrictLoad = new JsonLoadSettings
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
    };

    /// <summary>True only for a JSON object whose own <c>success</c> is the boolean true; a repeated key is ambiguous.</summary>
    private static bool ConfirmsSuccess(string body) => SuccessFlagIs(ParseObject(body), true);

    /// <summary>
    /// Our server's refusal: a JSON object whose own <c>success</c> is the boolean false and that names a refusal —
    /// an <c>error</c> from <see cref="RefusalErrors"/> or a refusal <c>reason</c>. Anything else (n8n's own error
    /// envelope, a proxy page, a fault word) proves no decision was made.
    /// </summary>
    private static bool IsServerRefusal(string body)
    {
        JObject answer = ParseObject(body);
        return SuccessFlagIs(answer, false) && (IsRefusalError(answer["error"]) || IsRefusalReason(answer["reason"]));
    }

    private static bool IsRefusalError(JToken error) =>
        error != null && error.Type == JTokenType.String && RefusalErrors.Contains(error.Value<string>());

    /// <summary>Delete Ownership's <see cref="ReasonOwnedElsewhere"/> / <see cref="ReasonUnproven"/>, or the gate's <c>forbidden:&lt;field&gt;</c>.</summary>
    private static bool IsRefusalReason(JToken reason)
    {
        if (reason == null || reason.Type != JTokenType.String) return false;
        string text = reason.Value<string>();
        return text == ReasonOwnedElsewhere || text == ReasonUnproven
               || (text.Length > GateRefusalPrefix.Length && text.StartsWith(GateRefusalPrefix, StringComparison.Ordinal));
    }

    private static bool SuccessFlagIs(JObject answer, bool expected)
    {
        JToken flag = answer?["success"];
        return flag != null && flag.Type == JTokenType.Boolean && flag.Value<bool>() == expected;
    }

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

    /// <summary>The reason a final refusal names — <see cref="ReasonOwnedElsewhere"/> or <see cref="ReasonUnproven"/>.</summary>
    public static string RefusalReason(string body)
    {
        if (string.IsNullOrEmpty(body)) return ReasonUnproven;
        try
        {
            return JObject.Parse(body).Value<string>("reason") == ReasonOwnedElsewhere ? ReasonOwnedElsewhere : ReasonUnproven;
        }
        catch
        {
            return ReasonUnproven;
        }
    }

    /// <summary>Null while the deletion may go on; otherwise the outcome this step decides.</summary>
    public static Outcome? StopAt(Step step, string body)
    {
        switch (step)
        {
            case Step.Transient: return Outcome.Retry;
            case Step.Refused: return Outcome.RefusedBecause(RefusalReason(body));
            default: return null;
        }
    }

    /// <summary>The verdict of a whole run: the first step that is not Ok decides, all Ok = done.</summary>
    public static Outcome Combine(IEnumerable<(Step step, string body)> steps)
    {
        foreach (var (step, body) in steps)
        {
            Outcome? stop = StopAt(step, body);
            if (stop.HasValue) return stop.Value;
        }
        return Outcome.Done;
    }
}

/// <summary>
/// V12: the copy of a refused or failed bot deletion (RU only, every glyph in the static SF Pro atlases — pinned by
/// FontGlyphCoverageTests). The texts are honest about where the data stays.
/// The retry texts (C1b, 2026-10-11) say only what holds for EVERY retry — no connection, a 5xx, a session the refresh
/// did not cure, a 2xx without the positive word, a 4xx without the refusal: no confirmation came and the bot is still
/// on the phone. They do not open with «Сервер не подтвердил» — that is how the refusal texts read.
/// </summary>
public static class BotDeletionCopy
{
    public const string RetryTitle = "Не удалось удалить бота";
    public const string RetryBody = "Подтверждение удаления от сервера не получено. Бот остался на телефоне — попробуйте ещё раз.";
    public const string RetryAction = "Повторить";
    public const string RefusedTitle = "Удаление не подтверждено";
    public const string RefusedBodyUnproven =
        "Сервер не подтвердил, что бот ваш. Данные бота на сервере останутся — удалить их можно по запросу в поддержку.";
    public const string RefusedBodyElsewhere =
        "Бот подключён на другом устройстве — данные на сервере принадлежат тому устройству и останутся там.";
    public const string PhoneOnlyAction = "Удалить только с телефона";
    public const string WipeRetryTitle = "Не удалось удалить все данные";
    public const string WipeRetryBody = "Подтверждение удаления от сервера не получено. Часть данных не удалена — попробуйте ещё раз.";

    public static string RefusedBody(string reason) =>
        reason == BotServerDeletion.ReasonOwnedElsewhere ? RefusedBodyElsewhere : RefusedBodyUnproven;

    /// <summary>The full wipe's single question about the bots the server refused (genitive: «удаление 1 бота / 5 ботов»).</summary>
    public static string WipeRefusedBody(int count, bool allOwnedElsewhere) =>
        $"Сервер не подтвердил удаление {count} {RuPlural.Pick(count, "бота", "ботов", "ботов")}. " +
        (allOwnedElsewhere
            ? "Данные на сервере принадлежат другому устройству и останутся там."
            : "Данные на сервере останутся — удалить их можно по запросу в поддержку.");
}

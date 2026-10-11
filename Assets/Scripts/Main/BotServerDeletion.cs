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
    /// explicit <c>success:false</c> — proves nothing (C1, review 2026-10-09): it is a retry, the bot stays. Never a
    /// refusal: the server named no reason, and both steps are idempotent, so retrying a deletion that did happen costs
    /// one round trip. A 4xx is the server's final word.
    /// </summary>
    public static Step ClassifyServerDelete(long code, string body)
    {
        if (IsTransient(code)) return Step.Transient;
        if (code >= 200 && code < 300) return ConfirmsSuccess(body) ? Step.Ok : Step.Transient;
        return Step.Refused;
    }

    private static readonly JsonLoadSettings StrictLoad = new JsonLoadSettings
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
    };

    /// <summary>True only for a JSON object whose own <c>success</c> is the boolean true; a repeated key is ambiguous.</summary>
    private static bool ConfirmsSuccess(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            JToken flag = JObject.Parse(body, StrictLoad)["success"];
            return flag != null && flag.Type == JTokenType.Boolean && flag.Value<bool>();
        }
        catch (JsonException)
        {
            return false;
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
/// </summary>
public static class BotDeletionCopy
{
    public const string RetryTitle = "Не удалось удалить бота";
    public const string RetryBody = "Нет связи с сервером. Бот не удалён — попробуйте ещё раз.";
    public const string RetryAction = "Повторить";
    public const string RefusedTitle = "Удаление не подтверждено";
    public const string RefusedBodyUnproven =
        "Сервер не подтвердил, что бот ваш. Данные бота на сервере останутся — удалить их можно по запросу в поддержку.";
    public const string RefusedBodyElsewhere =
        "Бот подключён на другом устройстве — данные на сервере принадлежат тому устройству и останутся там.";
    public const string PhoneOnlyAction = "Удалить только с телефона";
    public const string WipeRetryTitle = "Не удалось удалить все данные";
    public const string WipeRetryBody = "Нет связи с сервером. Часть данных не удалена — попробуйте ещё раз.";

    public static string RefusedBody(string reason) =>
        reason == BotServerDeletion.ReasonOwnedElsewhere ? RefusedBodyElsewhere : RefusedBodyUnproven;

    /// <summary>The full wipe's single question about the bots the server refused (genitive: «удаление 1 бота / 5 ботов»).</summary>
    public static string WipeRefusedBody(int count, bool allOwnedElsewhere) =>
        $"Сервер не подтвердил удаление {count} {RuPlural.Pick(count, "бота", "ботов", "ботов")}. " +
        (allOwnedElsewhere
            ? "Данные на сервере принадлежат другому устройству и останутся там."
            : "Данные на сервере останутся — удалить их можно по запросу в поддержку.");
}

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// A5 (audit stage 4): a REAL «нужен человек» request the bot created through its create_handoff
/// tool — not a classifier verdict. Wire model of <c>/webhook/Handoffs</c>.
/// </summary>
[Serializable]
public class HandoffRequest
{
    public long id;
    public string profileId;
    public string chatId;
    public string reason;        // order | question | escalation
    public string status;        // open | taken | done
    public string summary;
    public long createdAt;       // unix ms
    public string notifyState;   // pending | sent | failed | skipped
    public bool paused;          // the chat's auto-replies are paused until the owner acts

    [JsonIgnore] public bool IsOpen => status == "open";
}

[Serializable]
public class HandoffPausedChat
{
    public string profileId;
    public string chatId;
}

/// <summary>Stage 7.3: one row of the reply ledger (`reply_events`) — the last outcomes the owner can see.</summary>
[Serializable]
public class ReplyOutcomeRow
{
    public string profileId;
    public string chatTail;      // last 4 digits of the chat id — never the client's text
    public string state;         // reply_events.state
    public long at;              // unix ms
    public string executionId;   // correlation id for support
    public string transportMessageId;
}

[Serializable]
public class HandoffsResponse
{
    public bool success;
    public string error;
    public List<HandoffRequest> requests = new();
    public List<HandoffPausedChat> pausedChats = new();
    public List<ReplyOutcomeRow> recent = new();
    public int changed;

    public static HandoffsResponse Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var r = JsonConvert.DeserializeObject<HandoffsResponse>(json);
            if (r == null) return null;
            r.requests ??= new List<HandoffRequest>();
            r.pausedChats ??= new List<HandoffPausedChat>();
            r.recent ??= new List<ReplyOutcomeRow>();
            return r;
        }
        catch (Exception) { return null; }
    }
}

/// <summary>
/// Pure: which local (bot, chat) pairs must be forced to «Вместе» because the SERVER paused them for a
/// pending request. The mirror only ever pauses (sets the explicit per-chat ON override); it never
/// un-pauses — resuming is the owner's explicit action (the chat's «Авто» tap or «done»), which the
/// server records and which then flows back as an absent pause.
/// </summary>
public static class HandoffPauseMirror
{
    public readonly struct Plan
    {
        public readonly string BotId;
        public readonly string ChatId;
        public Plan(string botId, string chatId) { BotId = botId; ChatId = chatId; }
    }

    /// <param name="overrideOf">(botId, chatId) → (hasExplicitOverride, isOn) — SemiAutoStore.TryGetOverride.</param>
    public static List<Plan> Compute(IEnumerable<HandoffPausedChat> paused,
                                     IReadOnlyDictionary<string, DashboardProfileRef> profileToBot,
                                     Func<string, string, (bool has, bool on)> overrideOf)
    {
        var plans = new List<Plan>();
        if (paused == null || profileToBot == null) return plans;
        var seen = new HashSet<string>();
        foreach (var p in paused)
        {
            if (p == null || string.IsNullOrEmpty(p.profileId) || string.IsNullOrEmpty(p.chatId)) continue;
            if (!DashboardProfileMap.TryResolve(profileToBot, p.profileId, out var botName, out _)) continue;
            if (!seen.Add(botName + "\n" + p.chatId)) continue;
            var (has, on) = overrideOf(botName, p.chatId);
            if (has && on) continue;                 // already explicitly «Вместе» — nothing to do
            plans.Add(new Plan(botName, p.chatId));
        }
        return plans;
    }
}

/// <summary>User-facing strings of the handoff surface (RU-only UI; pinned by FontGlyphCoverageTests).</summary>
public static class HandoffCopy
{
    public const string RequestMarker = "Заявка · ";

    /// <summary>The «Сводка» row summary: a REAL request is marked, a classifier verdict is not.</summary>
    public static string RowSummary(DashboardOutcome r)
    {
        if (r == null) return "";
        string s = r.summary ?? "";
        return r.IsEvent ? RequestMarker + s : s;
    }
}

/// <summary>
/// Stage 7.3: the ledger state as the owner reads it — the mode, the limit, an error and an unconfirmed send are
/// different things and are named as such; technical codes stay out of the label (the correlation id is shown apart).
/// </summary>
public static class ReplyOutcomeCopy
{
    public const string Sent = "Отправлено";
    public const string Suppressed = "Не отправлено: режим «Выбрать»";
    public const string QuotaDenied = "Не отправлено: лимит диалогов";
    public const string SendFailed = "Ошибка отправки";
    public const string SendUnknown = "Отправка не подтверждена";
    public const string Superseded = "Заменено новым сообщением";
    public const string CancelledOwner = "Отменено: ответил владелец";
    public const string CancelledMode = "Отменено: режим изменён";
    public const string Duplicate = "Повтор события";
    public const string GenerationFailed = "Ошибка генерации ответа";
    public const string Pending = "В обработке";
    public const string Cancelled = "Отменено";

    public static string Label(string state)
    {
        switch (state)
        {
            case "sent": return Sent;
            case "suppressed": return Suppressed;
            case "quota_denied": return QuotaDenied;
            case "send_failed": return SendFailed;
            case "send_unknown": return SendUnknown;
            case "superseded": return Superseded;
            case "cancelled_owner": return CancelledOwner;
            case "cancelled_mode": return CancelledMode;
            case "duplicate": return Duplicate;
            case "generation_failed": return GenerationFailed;
            case "claimed": case "generated": case "send_pending": return Pending;
            default: return Cancelled;
        }
    }

    /// <summary>Whether the owner should look at this outcome (something did not go out as intended).</summary>
    public static bool NeedsAttention(string state)
        => state == "send_failed" || state == "send_unknown" || state == "generation_failed" || state == "quota_denied";
}

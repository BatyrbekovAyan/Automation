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

[Serializable]
public class HandoffsResponse
{
    public bool success;
    public string error;
    public List<HandoffRequest> requests = new();
    public List<HandoffPausedChat> pausedChats = new();
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

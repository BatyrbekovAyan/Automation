using System;
using System.Collections.Generic;

/// <summary>One bot's row of Get Usage `readiness` (wire model; field names match the JSON verbatim).</summary>
[Serializable]
public class AutoReadinessEntry
{
    public string botKey;   // the bot's PlayerPrefs key (Bot0, …) = bot_profiles.bot_key
    public int matches;     // card #1 matched the sent text, within the CURRENT window (the last 20 sends), not the whole history
    public int total;       // owner sends WITH a shown set, within the current window (≤ 20)
    public int chats;       // distinct personal chats among them
    public int days;        // distinct calendar days among them
    public bool ready;      // the server's verdict: the best window reached the unlock AND the history spans enough chats and days

    // The learning percent (spec 2026-10-22): everything below is computed over the bot's WHOLE stored history by the server.
    public int bestMatches; // card #1 matches in the best 20-send window of the history
    public int? progress;   // 0..100, the percent to draw. NULL = the field is absent: an older server — "no data", never 0
    public int unlockAt;    // the percent from which «Авто» opens (the server's number; 70 at the time of writing)
    public bool capped;     // good answers, but the percent is held just below unlockAt by the chats/days conditions
    public int needChats;   // more distinct personal chats the conditions still need
    public int needDays;    // more distinct calendar days the conditions still need
}

public enum AutoReadinessVerdict { Open, Locked }

/// <summary>
/// What the client knows about one bot's learning: the percent to draw, and the server's facts the lock sheet words.
/// Built only by <see cref="AutoReadinessPolicy.StateFor"/> — the gate, the sheet and the four «Авто» capsules read this and nothing else.
/// </summary>
public readonly struct LearningState
{
    public LearningState(int percent, int unlockAt, bool capped, int needChats, int needDays, bool locked)
    {
        Percent = percent;
        UnlockAt = unlockAt;
        Capped = capped;
        NeedChats = needChats;
        NeedDays = needDays;
        Locked = locked;
    }

    public int Percent { get; }      // 0..100: the better of the phone's memory and the server's number
    public int UnlockAt { get; }     // 0 when only the phone's memory speaks (no row from the server)
    public bool Capped { get; }
    public int NeedChats { get; }
    public int NeedDays { get; }
    public bool Locked { get; }      // the soft «Авто» lock applies: the server says "not ready" and «Авто» never opened for this bot
    public bool Trained => Percent >= 100;
}

/// <summary>
/// Learning B + the percent: whether enabling «Авто» should first show «ИИ-сотрудник ещё учится». The lock is a HINT, never a
/// guard — Locked only on the server's positive verdict (the bot is listed, the row carries a `progress`, it is not ready, and its
/// «Авто» never opened); no snapshot, no row, an old server (no `progress`) or a legacy caller all read as Open. The thresholds the
/// server decides with live in Tools/n8n/apply-learning-b.py; the client keeps none of its own — Window, MinMatches and Progress
/// only feed the old «N из 20» copy until the percent wording replaces it.
/// </summary>
public static class AutoReadinessPolicy
{
    public const int Window = 20;
    public const int MinMatches = 10;

    /// <summary>Locked ⇔ the row exists, carries a `progress`, is not `ready`, and «Авто» was never unlocked on this phone.</summary>
    public static AutoReadinessVerdict Verdict(AutoReadinessEntry entry, bool unlockedBefore) =>
        entry != null && entry.progress.HasValue && !entry.ready && !unlockedBefore
            ? AutoReadinessVerdict.Locked
            : AutoReadinessVerdict.Open;

    /// <summary>
    /// The percent to draw: the better of the server's number and the phone's remembered peak, each clamped to 0..100
    /// (a garbage value never leaves the scale). Null = no data at all: no `progress` in the row and nothing remembered.
    /// </summary>
    public static int? DisplayPercent(AutoReadinessEntry entry, int? peak)
    {
        int? server = entry?.progress;
        if (!server.HasValue && !peak.HasValue) return null;
        return Math.Max(ClampPercent(server ?? 0), ClampPercent(peak ?? 0));
    }

    /// <summary>
    /// The ONE entry point: <see cref="AutoReadinessStore"/> (the last snapshot) + <see cref="LearnProgressStore"/> (the phone's memory).
    /// A row with a `progress` brings its facts and the verdict; with no such row only the remembered peak speaks (an offline start,
    /// an older server) and it never locks; with neither it is null — no fill, no lock, the ordinary «Авто».
    /// </summary>
    public static LearningState? StateFor(string botKey)
    {
        if (string.IsNullOrEmpty(botKey)) return null;
        AutoReadinessEntry entry = AutoReadinessStore.Current(botKey);
        int? percent = DisplayPercent(entry, LearnProgressStore.Peak(botKey));
        if (!percent.HasValue) return null;
        if (entry == null || !entry.progress.HasValue)
            return new LearningState(percent.Value, unlockAt: 0, capped: false, needChats: 0, needDays: 0, locked: false);

        bool locked = Verdict(entry, LearnProgressStore.Unlocked(botKey)) == AutoReadinessVerdict.Locked;
        return new LearningState(percent.Value, entry.unlockAt, entry.capped, entry.needChats, entry.needDays, locked);
    }

    /// <summary>Matches to print on the capsule and the sheet — clamped to the window the server counted.</summary>
    public static int Progress(AutoReadinessEntry entry) =>
        entry == null ? 0 : Math.Clamp(entry.matches, 0, Window);

    internal static int ClampPercent(int value) => Math.Clamp(value, 0, 100);
}

/// <summary>Reads a bot's readiness row out of the last Get Usage snapshot (<see cref="UsageStore.Current"/>).</summary>
public static class AutoReadinessStore
{
    public static AutoReadinessEntry Current(string botKey)
    {
        if (string.IsNullOrEmpty(botKey)) return null;
        List<AutoReadinessEntry> rows = UsageStore.Current?.readiness;
        if (rows == null) return null;
        foreach (AutoReadinessEntry row in rows)
            if (row != null && row.botKey == botKey) return row;
        return null;
    }
}

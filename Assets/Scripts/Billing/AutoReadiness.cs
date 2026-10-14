using System;
using System.Collections.Generic;

/// <summary>One bot's row of Get Usage `readiness` (wire model; field names match the JSON verbatim).</summary>
[Serializable]
public class AutoReadinessEntry
{
    public string botKey;   // the bot's PlayerPrefs key (Bot0, …) = bot_profiles.bot_key
    public int matches;     // card #1 matched the sent text, within the window
    public int total;       // owner sends WITH a shown set, within the window (≤ Window)
    public int chats;       // distinct personal chats among them
    public int days;        // distinct calendar days among them
    public bool ready;      // the server's verdict: total ≥ 10, matches ≥ 10, chats ≥ 3, days ≥ 2
}

public enum AutoReadinessVerdict { Open, Locked }

/// <summary>
/// Learning B: whether enabling «Авто» should first show «Бот ещё учится». The lock is a HINT, never a guard —
/// Locked only on the server's positive verdict (the bot is listed and not ready); no snapshot, no row, an old server or a
/// legacy caller all read as Open. The thresholds the server decides with live in Tools/n8n/apply-learning-b.py; the two
/// numbers mirrored here are for the copy only (tests/test_learning_b.py keeps them in step).
/// </summary>
public static class AutoReadinessPolicy
{
    public const int Window = 20;
    public const int MinMatches = 10;

    public static AutoReadinessVerdict Verdict(AutoReadinessEntry entry) =>
        entry != null && !entry.ready ? AutoReadinessVerdict.Locked : AutoReadinessVerdict.Open;

    /// <summary>Matches to print on the capsule and the sheet — clamped to the window the server counted.</summary>
    public static int Progress(AutoReadinessEntry entry) =>
        entry == null ? 0 : Math.Clamp(entry.matches, 0, Window);
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

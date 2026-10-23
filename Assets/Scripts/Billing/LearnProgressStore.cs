using UnityEngine;

/// <summary>
/// The phone's own memory of a bot's learning (spec 2026-10-22 §4.1): the best percent the server ever reported and whether
/// «Авто» ever opened for it — two PlayerPrefs ints under the bot's slot name, <c>{bot}LearnPeak | {bot}LearnUnlocked</c>.
/// The ONE reader and writer (BotScheduleStore's pattern); <see cref="AutoReadinessPolicy.StateFor"/> reads it, UsageStore.Apply
/// feeds it through <see cref="Absorb"/>, Bot.DeleteBotLocally calls <see cref="Clear"/>.
///
/// Why it exists: the server's learning marks expire after 180 days, so a bot that has worked in «Авто» for months and gets no new
/// panel answers would otherwise read «Учится» again — the percent never falls and an unlock never un-happens. It also lets a cold,
/// offline start draw the last known percent before the first snapshot lands.
/// </summary>
public static class LearnProgressStore
{
    public const string PeakSuffix = "LearnPeak";
    public const string UnlockedSuffix = "LearnUnlocked";

    /// <summary>The best percent (0..100) ever absorbed for the bot; null = nothing remembered.</summary>
    public static int? Peak(string botName)
    {
        if (string.IsNullOrEmpty(botName) || !PlayerPrefs.HasKey(botName + PeakSuffix)) return null;
        return PlayerPrefs.GetInt(botName + PeakSuffix);
    }

    /// <summary>Whether the server ever reported the bot as ready (its «Авто» opened once).</summary>
    public static bool Unlocked(string botName) =>
        !string.IsNullOrEmpty(botName) && PlayerPrefs.GetInt(botName + UnlockedSuffix, 0) == 1;

    /// <summary>
    /// Folds a freshly parsed Get Usage snapshot into the memory: for every row that carries a <c>progress</c> the peak becomes
    /// max(peak, progress clamped to 0..100) and «unlocked» is OR-ed with <c>ready</c>. A row without a <c>progress</c> is an older
    /// server's — no data, not even its <c>ready</c> — and is skipped. Flushes once, and only when something changed.
    /// </summary>
    public static void Absorb(UsageSnapshot snapshot)
    {
        if (snapshot?.readiness == null) return;
        bool changed = false;
        foreach (AutoReadinessEntry row in snapshot.readiness)
        {
            if (row == null || !row.progress.HasValue || string.IsNullOrEmpty(row.botKey)) continue;

            int percent = AutoReadinessPolicy.ClampPercent(row.progress.Value);
            int? peak = Peak(row.botKey);
            if (!peak.HasValue || percent > peak.Value)
            {
                PlayerPrefs.SetInt(row.botKey + PeakSuffix, percent);
                changed = true;
            }
            if (row.ready && !Unlocked(row.botKey))
            {
                PlayerPrefs.SetInt(row.botKey + UnlockedSuffix, 1);
                changed = true;
            }
        }
        if (changed) PlayerPrefs.Save();   // mobile apps get killed — flush (bot-persistence)
    }

    public static void Clear(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return;
        PlayerPrefs.DeleteKey(botName + PeakSuffix);
        PlayerPrefs.DeleteKey(botName + UnlockedSuffix);
    }
}

using System;
using System.Globalization;
using UnityEngine;

public static class TrialLedger
{
    const string Key = "TrialStartedUtc";

    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    internal static Func<string, string> Load = k => PlayerPrefs.GetString(k, "");
    internal static Action<string, string> Save = (k, v) => { PlayerPrefs.SetString(k, v); PlayerPrefs.Save(); };

    internal static void ResetSeamsForTests()
    {
        UtcNow = () => DateTime.UtcNow;
        Load = k => PlayerPrefs.GetString(k, "");
        Save = (k, v) => { PlayerPrefs.SetString(k, v); PlayerPrefs.Save(); };
    }

    public static bool HasStarted => !string.IsNullOrEmpty(Load(Key));

    public static void StartIfNeeded()
    {
        if (!HasStarted)
            Save(Key, UtcNow().ToString("o", CultureInfo.InvariantCulture));
    }

    public static int DaysLeft()
    {
        if (!HasStarted) return PlanCatalog.TrialDays;
        var start = DateTime.Parse(Load(Key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var elapsedDays = (int)Math.Floor((UtcNow() - start).TotalDays);
        if (elapsedDays < 0) elapsedDays = 0;   // откат часов не удлиняет триал; настоящий enforcement — серверный свип (Task 12)
        return Math.Max(0, PlanCatalog.TrialDays - elapsedDays);
    }

    public static bool IsExpired => HasStarted && DaysLeft() <= 0;
}

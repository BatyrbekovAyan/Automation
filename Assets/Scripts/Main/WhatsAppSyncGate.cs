using System;
using UnityEngine;

/// <summary>
/// Pure, side-effect-free math for the fixed post-creation WhatsApp sync window.
/// No PlayerPrefs or singletons so the logic stays unit-testable in EditMode.
/// </summary>
public static class WhatsAppSyncGate
{
    /// <summary>True while the fixed sync window is still open.</summary>
    public static bool IsSyncing(long syncUntilUnixMs, long nowUnixMs) => nowUnixMs < syncUntilUnixMs;

    /// <summary>Milliseconds left in the window, never negative.</summary>
    public static long RemainingMs(long syncUntilUnixMs, long nowUnixMs) =>
        Math.Max(0L, syncUntilUnixMs - nowUnixMs);

    /// <summary>0..1 fraction of the window elapsed, for the progress bar.</summary>
    public static float ProgressFraction(long syncUntilUnixMs, long nowUnixMs, int windowSeconds)
    {
        if (windowSeconds <= 0) return 1f;
        long windowMs = (long)windowSeconds * 1000L;
        float elapsed = windowMs - RemainingMs(syncUntilUnixMs, nowUnixMs);
        return Mathf.Clamp01(elapsed / windowMs);
    }

    /// <summary>Human-friendly countdown label for the syncing screen.</summary>
    public static string FormatCountdown(long remainingMs)
    {
        if (remainingMs <= 0L) return "Finishing up…";
        int totalSeconds = (int)((remainingMs + 999L) / 1000L); // round up to whole seconds
        if (totalSeconds <= 60) return "Less than a minute left";
        int minutes = (totalSeconds + 59) / 60;                 // round up to whole minutes
        return $"About {minutes} min left";
    }
}

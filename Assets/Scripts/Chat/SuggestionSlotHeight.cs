using System;
using UnityEngine;

/// <summary>
/// The remembered height of the keyboard slot the suggestions panel substitutes for
/// (sketch-003 variant A), in canvas units. The panel wants to be EXACTLY as tall as the
/// native keyboard so the panel ⇄ keyboard swap never moves the composer; the real height
/// is only knowable after the keyboard has been shown once, so it is measured live by the
/// controller's watcher and persisted here. Before the first measurement the fallback
/// approximates a portrait phone keyboard at the 1080-wide reference scale.
/// Injectable prefs seams (NotifPrefs pattern) keep the policy EditMode-testable.
/// </summary>
public static class SuggestionSlotHeight
{
    /// <summary>Pre-first-measurement guess (≈260dp keyboard × 3, sketch 003 proportions).</summary>
    public const float FallbackCanvasPx = 780f;

    // Sanity window: a real portrait keyboard converts to roughly 600–1100 canvas units;
    // values outside it are read glitches (0 mid-animation, a stray full-screen area) and
    // must never become the remembered slot.
    public const float MinValidCanvasPx = 300f;
    public const float MaxValidCanvasPx = 1500f;

    private const string PrefKey = "SuggestSlotHeightCanvasPx";

    public static Func<string, float, float> LoadPref = PlayerPrefs.GetFloat;
    public static Action<string, float> SavePref = DefaultSave;

    /// <summary>The slot height to open the panel at while no keyboard is up.</summary>
    public static float Remembered
    {
        get
        {
            float stored = LoadPref(PrefKey, 0f);
            return IsValid(stored) ? stored : FallbackCanvasPx;
        }
    }

    /// <summary>Persist a live keyboard measurement (ignored when outside the sanity window).</summary>
    public static void Remember(float measuredCanvasPx)
    {
        if (!IsValid(measuredCanvasPx)) return;
        if (Mathf.Approximately(LoadPref(PrefKey, 0f), measuredCanvasPx)) return;   // no redundant disk writes
        SavePref(PrefKey, measuredCanvasPx);
    }

    public static bool IsValid(float canvasPx)
        => canvasPx >= MinValidCanvasPx && canvasPx <= MaxValidCanvasPx;

    private static void DefaultSave(string key, float value)
    {
        PlayerPrefs.SetFloat(key, value);
        PlayerPrefs.Save();   // mobile apps get killed — flush (bot-persistence rule)
    }

    public static void ResetForTests()
    {
        LoadPref = PlayerPrefs.GetFloat;
        SavePref = DefaultSave;
    }
}

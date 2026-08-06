using System;
using UnityEngine;

/// <summary>
/// Keeps the NATIVE system keyboard in step with the app theme. Unity draws
/// every in-app pixel itself, so the IME is the one surface the palette cannot
/// reach — on iOS it is steered by overriding the window interface style
/// (<see cref="IOSBridge.SetDarkKeyboard"/>).
///
/// This type owns that flag app-wide so callers never fight over it. Before it
/// existed, <see cref="AttachmentPreviewScreen"/> set the raw bridge boolean and
/// cleared it to FALSE on exit — which, once the app gained a dark theme, would
/// have kicked the keyboard back to light while the rest of the UI stayed dark.
/// Screens now declare intent (<see cref="SetForcedDark"/>) and the effective
/// value is recomputed from <see cref="KeyboardAppearancePolicy"/>, so leaving an
/// always-dark screen falls back to the THEME, not to light.
///
/// Android is deliberately a no-op: the IME runs in its own process and picks
/// its theme from the system-wide dark-mode setting; no public API lets an app
/// request a dark keyboard (the pre-existing IOSBridge comment records the same
/// finding). On Android the keyboard follows the phone, not our in-app toggle.
///
/// Static Func/Action seams (ThemePrefs / NotifPrefs pattern) let EditMode tests
/// drive the policy without a device or PlayerPrefs.
/// </summary>
public static class SystemKeyboardAppearance
{
    /// <summary>Pushes the resolved value to the platform. Swapped in tests.</summary>
    internal static Action<bool> ApplyToPlatform = IOSBridge.SetDarkKeyboard;

    /// <summary>Reads the active theme. Swapped in tests.</summary>
    internal static Func<ThemeMode> CurrentMode = () => Theme.Mode;

    private static bool forcedDark;
    private static bool? lastApplied;   // null = nothing pushed yet
    private static bool subscribed;

    /// <summary>The value last pushed to the platform (null before the first push).</summary>
    internal static bool? LastApplied => lastApplied;

    /// <summary>
    /// Declare that an always-dark screen is on top (or has left). Safe to call
    /// unbalanced — it is a flag, not a counter, because the attachment preview
    /// clears it from several exit paths (fade complete, OnDisable teardown).
    /// </summary>
    public static void SetForcedDark(bool force)
    {
        forcedDark = force;
        Apply();
    }

    /// <summary>Re-evaluate after a theme change (or on startup).</summary>
    public static void Refresh() => Apply();

    private static void Apply()
    {
        bool dark = KeyboardAppearancePolicy.ShouldBeDark(CurrentMode(), forcedDark);
        if (lastApplied == dark) return;   // the native call touches every UIWindow — only on change

        lastApplied = dark;
        ApplyToPlatform?.Invoke(dark);
    }

    // Runs once per domain reload, before any screen can ask for a keyboard.
    // Statics are reset explicitly so a play session with domain reload disabled
    // does not inherit the previous run's applied value.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        forcedDark = false;
        lastApplied = null;

        if (!subscribed)
        {
            Theme.Changed += Refresh;
            subscribed = true;
        }

        Apply();
    }

    /// <summary>Test seam: restore defaults between EditMode cases.</summary>
    internal static void ResetForTests()
    {
        forcedDark = false;
        lastApplied = null;
        ApplyToPlatform = IOSBridge.SetDarkKeyboard;
        CurrentMode = () => Theme.Mode;
    }
}

using System.Reflection;
using TMPro;

/// Pushes the field's current Unity-side selection into the hidden native
/// TouchScreenKeyboard buffer. TMP only does this on its own pointer paths
/// (2 call sites in this uGUI version), so every PROGRAMMATIC selection
/// change must route through here — otherwise the next keystroke on iOS
/// edits at the native buffer's stale caret instead of replacing the
/// selection. The invoked method carries TMP's own platform/null/
/// canSetSelection guards, so calling it is safe in the Editor and when no
/// keyboard is open.
public static class KeyboardSelectionSync
{
    static readonly MethodInfo PushMethod = typeof(TMP_InputField).GetMethod(
        "UpdateKeyboardStringPosition", BindingFlags.Instance | BindingFlags.NonPublic);

    // TMP's pointer paths call MarkGeometryAsDirty explicitly after selection
    // changes — the public selection setters never repaint on their own, so a
    // programmatic selection would stay INVISIBLE (device-verified: pins
    // showed, highlight didn't). Push therefore also schedules the repaint.
    static readonly MethodInfo MarkDirtyMethod = typeof(TMP_InputField).GetMethod(
        "MarkGeometryAsDirty", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static System.Action<TMP_InputField> PushOverrideForTests;

    public static bool TargetExists => PushMethod != null && MarkDirtyMethod != null;

    public static void Push(TMP_InputField field)
    {
        if (field == null) return;
        if (PushOverrideForTests != null) { PushOverrideForTests(field); return; }
        PushMethod?.Invoke(field, null);
        MarkDirtyMethod?.Invoke(field, null);
    }
}

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

    internal static System.Action<TMP_InputField> PushOverrideForTests;

    public static bool TargetExists => PushMethod != null;

    public static void Push(TMP_InputField field)
    {
        if (field == null) return;
        if (PushOverrideForTests != null) { PushOverrideForTests(field); return; }
        PushMethod?.Invoke(field, null);
    }
}

using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Diagnostic trace for the iOS cross-field text-duplication hunt.
/// Every line is tagged [KB] with the frame number so the exact sequence of
/// focus changes, keyboard-buffer mutations, and text ingestions can be
/// reconstructed from the Xcode console. Flip Enabled off (or strip the
/// call sites) once the mechanism is identified.
/// </summary>
public static class KbTrace
{
    public static bool Enabled = true;

    public static void Log(string message)
    {
        if (!Enabled) return;
        Debug.Log($"[KB f{Time.frameCount} t{Time.unscaledTime:F3}] {message}");
    }

    /// <summary>Truncates a value for logging, keeping ends visible.</summary>
    public static string T(string value)
    {
        if (value == null) return "<null>";
        if (value.Length <= 24) return value;
        return value.Substring(0, 10) + "…" + value.Substring(value.Length - 10)
               + $"({value.Length})";
    }

    public static string Sel()
    {
        var eventSystem = EventSystem.current;
        var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (selected == null) return "<none>";
        var parent = selected.transform.parent;
        return parent != null ? $"{parent.name}/{selected.name}" : selected.name;
    }
}

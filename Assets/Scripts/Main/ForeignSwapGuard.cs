using System;
using System.Collections.Generic;

/// <summary>
/// Detects the iOS shared-keyboard text replay: every Unity input shares one
/// hidden native text field, and a focus switch can commit the PREVIOUS
/// field's content wholesale into the newly focused one (device repro:
/// rapid cross-taps duplicate text between fields, either direction).
///
/// The detection needs no timing heuristics because the app knows every
/// sibling field's current text: a change is bleed if and only if it is a
/// wholesale replacement (not a keystroke) that lands EXACTLY on another
/// field's non-empty content. Typing can never match — a keystroke changes
/// length by at most one — and clearing a field is always allowed.
/// </summary>
public static class ForeignSwapGuard
{
    public static bool IsForeignSwap(
        string newText,
        string prevText,
        IReadOnlyList<string> otherFieldTexts)
    {
        if (otherFieldTexts == null || otherFieldTexts.Count == 0) return false;

        newText = newText ?? "";
        prevText = prevText ?? "";

        if (newText == prevText) return false;
        if (newText.Length == 0) return false; // clearing is always legitimate

        // Keystrokes and single-key IME edits move length by at most one.
        if (Math.Abs(newText.Length - prevText.Length) <= 1) return false;

        // Trimmed, case-insensitive: iOS can auto-capitalize or pad the
        // replayed content on insertion; a keystroke still can't fake a
        // match because of the length rule above.
        var candidate = newText.Trim();
        for (var i = 0; i < otherFieldTexts.Count; i++)
        {
            var other = otherFieldTexts[i];
            if (string.IsNullOrEmpty(other)) continue;
            if (string.Equals(other.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

using System;

/// <summary>
/// Detects the Android IME field-switch text bleed: dismissing one input and
/// quickly focusing another restarts the shared native keyboard session, and
/// the restart race can commit the OLD field's buffer wholesale into the NEW
/// field. Observed on-device in bot settings (2026-07-28).
///
/// The signature is deliberately narrow so ordinary typing can never match:
/// the freshly focused field's text must be REPLACED (not edited char-by-char)
/// with exactly the text of the field dismissed just before, within the first
/// moments of focus. Pure static so the decision is unit-testable.
/// </summary>
public static class KeyboardTextBleedGuard
{
    /// <summary>Seconds after focus during which a wholesale swap to the
    /// previously dismissed field's text is treated as IME bleed.</summary>
    public const float WindowSeconds = 0.4f;

    /// <summary>
    /// True when the change from prevText to newText should be discarded as
    /// keyboard bleed (restore prevText).
    ///
    /// newText            — the field's text this frame.
    /// prevText           — the field's text last frame.
    /// lastDismissedText  — text of the most recently dismissed field.
    /// secondsSinceFocus  — time since this field gained focus.
    /// </summary>
    public static bool ShouldRevert(
        string newText,
        string prevText,
        string lastDismissedText,
        float secondsSinceFocus)
    {
        if (secondsSinceFocus > WindowSeconds) return false;
        if (lastDismissedText == null) return false;

        newText = newText ?? "";
        prevText = prevText ?? "";

        if (newText == prevText) return false;
        if (newText != lastDismissedText) return false;

        // Typing and single-key IME edits change the length by at most one
        // per frame; the bleed is a wholesale replacement. This also spares
        // the legitimate case of typing the one character that makes the
        // texts momentarily equal.
        return Math.Abs(newText.Length - prevText.Length) > 1;
    }
}

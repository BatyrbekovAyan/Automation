using NUnit.Framework;

/// <summary>
/// Pins the Android IME text-bleed detection: a freshly focused field whose
/// text is wholesale-replaced with the previously dismissed field's buffer
/// must be reverted; ordinary typing must never be.
/// </summary>
public class KeyboardTextBleedGuardTests
{
    private const string DismissedText = "Пн–Сб 09:00–19:00";

    [Test]
    public void WholesaleForeignText_RightAfterFocus_Reverts()
    {
        // The reported bug: tap Часы работы, dismiss, tap Email — Email's
        // empty text becomes the hours string in one frame.
        Assert.IsTrue(KeyboardTextBleedGuard.ShouldRevert(
            newText: DismissedText, prevText: "", lastDismissedText: DismissedText,
            secondsSinceFocus: 0.1f));
    }

    [Test]
    public void SameSwap_OutsideWindow_NotReverted()
    {
        Assert.IsFalse(KeyboardTextBleedGuard.ShouldRevert(
            DismissedText, "", DismissedText,
            secondsSinceFocus: KeyboardTextBleedGuard.WindowSeconds + 0.05f));
    }

    [Test]
    public void TypingOneCharacter_NotReverted_EvenIfTextsBecomeEqual()
    {
        // Length delta of 1 = a keystroke, never a wholesale IME restore —
        // even when the keystroke makes the field match the dismissed text.
        Assert.IsFalse(KeyboardTextBleedGuard.ShouldRevert(
            newText: "+7 700", prevText: "+7 70", lastDismissedText: "+7 700",
            secondsSinceFocus: 0.1f));
    }

    [Test]
    public void UnrelatedText_NotReverted()
    {
        Assert.IsFalse(KeyboardTextBleedGuard.ShouldRevert(
            "info@company.kz", "", DismissedText, 0.1f));
    }

    [Test]
    public void NoChange_NotReverted()
    {
        Assert.IsFalse(KeyboardTextBleedGuard.ShouldRevert(
            DismissedText, DismissedText, DismissedText, 0.1f));
    }

    [Test]
    public void ClearedToEmpty_WhenEmptyWasDismissed_RevertsOnlyOnWholesaleClear()
    {
        // Previous field was empty; this field's multi-char text vanishing in
        // one frame is the bleed (empty buffer committed over it).
        Assert.IsTrue(KeyboardTextBleedGuard.ShouldRevert(
            newText: "", prevText: "г. Алматы", lastDismissedText: "",
            secondsSinceFocus: 0.1f));

        // A single backspace from a 1-char value is typing, not bleed.
        Assert.IsFalse(KeyboardTextBleedGuard.ShouldRevert(
            newText: "", prevText: "г", lastDismissedText: "",
            secondsSinceFocus: 0.1f));
    }

    [Test]
    public void NullSafe()
    {
        Assert.IsFalse(KeyboardTextBleedGuard.ShouldRevert(null, null, null, 0.1f));
        Assert.IsFalse(KeyboardTextBleedGuard.ShouldRevert("abc", null, null, 0.1f));
    }
}

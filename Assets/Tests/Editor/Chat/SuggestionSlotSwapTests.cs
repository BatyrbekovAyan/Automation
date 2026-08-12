using NUnit.Framework;

// EditMode coverage for SuggestionSlotSwap — the pure no-dip rules of the panel ⇄ keyboard
// slot handoff (sketch-003 variant A). Pins: opening over a live keyboard adopts ITS height
// (any other slot moves the composer mid-swap), and the held inset releases only when the
// incoming keyboard actually arrived (fraction) or provably never will (timeout) — never
// merely because the old tenant left.
public class SuggestionSlotSwapTests
{
    private const float Remembered = 780f;

    // --- SlotForOpen --------------------------------------------------------

    [Test]
    public void SlotForOpen_NoKeyboard_UsesRemembered()
        => Assert.AreEqual(Remembered, SuggestionSlotSwap.SlotForOpen(false, 0f, Remembered));

    [Test]
    public void SlotForOpen_OverLiveKeyboard_AdoptsTheKeyboardHeight()
        => Assert.AreEqual(906f, SuggestionSlotSwap.SlotForOpen(true, 906f, Remembered));

    [Test]
    public void SlotForOpen_KeyboardVisibleButHeightStillZero_UsesRemembered()
        // TouchScreenKeyboard.visible flips before the area settles — a 0-height read must not
        // produce a zero-height panel.
        => Assert.AreEqual(Remembered, SuggestionSlotSwap.SlotForOpen(true, 0f, Remembered));

    [Test]
    public void SlotForOpen_KeyboardHeightOutsideSanityWindow_UsesRemembered()
        => Assert.AreEqual(Remembered, SuggestionSlotSwap.SlotForOpen(true, 2400f, Remembered));

    // --- ShouldReleaseHold --------------------------------------------------

    [Test]
    public void Hold_ReleasesWhenKeyboardReachesTheFraction()
        => Assert.IsTrue(SuggestionSlotSwap.ShouldReleaseHold(true, 780f * 0.96f, 780f, 0.1f));

    [Test]
    public void Hold_KeepsWhileKeyboardStillRising()
        => Assert.IsFalse(SuggestionSlotSwap.ShouldReleaseHold(true, 300f, 780f, 0.1f));

    [Test]
    public void Hold_NeverReleasesWhileKeyboardAbsent()
        // A keyboard that bounced away mid-handoff → the panel reinstates; dropping the hold
        // here would collapse the composer onto an empty slot.
        => Assert.IsFalse(SuggestionSlotSwap.ShouldReleaseHold(false, 780f, 780f, 5f));

    [Test]
    public void Hold_ReleasesOnTimeout_WhenKeyboardIsShorterThanTheSlot()
        // First-run case: fallback slot 780 but the real keyboard is shorter — it can never
        // reach 95% of the hold, so the timeout settles the composer onto it once.
        => Assert.IsTrue(SuggestionSlotSwap.ShouldReleaseHold(
            true, 600f, 780f, SuggestionSlotSwap.ReleaseTimeoutSeconds));

    [Test]
    public void Hold_ZeroHeld_ReleasesImmediately()
        => Assert.IsTrue(SuggestionSlotSwap.ShouldReleaseHold(true, 10f, 0f, 0f));

    [Test]
    public void Hold_JustUnderBothThresholds_Keeps()
        => Assert.IsFalse(SuggestionSlotSwap.ShouldReleaseHold(
            true, 780f * 0.90f, 780f, SuggestionSlotSwap.ReleaseTimeoutSeconds - 0.05f));
}

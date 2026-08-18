using NUnit.Framework;

using Phase = ChatManager.ChatOpenPhase;

// EditMode coverage for SuggestionSlotOpenTiming — the rule that keeps the suggestions panel out
// of the chat-open animation. The slot claim is made synchronously on the row tap, ~600ms before
// the chat screen is even active, so without this gate the whole rise lands on the slide's first
// frame and the chat appears to open diagonally (device report 2026-08-18).
public class SuggestionSlotOpenTimingTests
{
    private const float AfterBeat = SuggestionSlotOpenTiming.SettleDelaySeconds + 0.01f;

    // --- ChatOpenSettled ----------------------------------------------------

    [Test]
    public void NotSettled_DuringPrep()
        // Prep is the 300ms lead-in: the screen is still deactivated, so a claim made here is
        // invisible until the slide starts and then applies all at once.
        => Assert.IsFalse(SuggestionSlotOpenTiming.ChatOpenSettled(Phase.Prep, false));

    [Test]
    public void NotSettled_DuringSlide()
        => Assert.IsFalse(SuggestionSlotOpenTiming.ChatOpenSettled(Phase.Slide, true));

    [Test]
    public void NotSettled_WhenPhaseSaysSettledButTheSlideIsStillRunning()
        // Both signals are needed: PopulateBubbles runs INSIDE the slide's onComplete, so the
        // phase reaches Populate/Idle a few lines before SwipeToBack lowers IsSliding.
        => Assert.IsFalse(SuggestionSlotOpenTiming.ChatOpenSettled(Phase.Populate, true));

    [Test]
    public void Settled_OncePopulateRunsWithNoSlide()
        // The no-SwipeToBack fallback path in OpenChatRoutine never raises IsSliding at all.
        => Assert.IsTrue(SuggestionSlotOpenTiming.ChatOpenSettled(Phase.Populate, false));

    [Test]
    public void Settled_WhenIdleAndStill()
        => Assert.IsTrue(SuggestionSlotOpenTiming.ChatOpenSettled(Phase.Idle, false));

    [Test]
    public void NotSettled_WhileSlidingOutOfASettledChat()
        // A back-swipe out of an open chat: Idle, but the screen is moving.
        => Assert.IsFalse(SuggestionSlotOpenTiming.ChatOpenSettled(Phase.Idle, true));

    // --- MayTakeSlot --------------------------------------------------------

    [Test]
    public void MayNotTakeSlot_BeforeTheSettleBeatElapses()
        // The thread's first batch is instantiated on the frames right after the slide ends;
        // starting the slot tween into that layout pass is both stuttery and simultaneous.
        => Assert.IsFalse(SuggestionSlotOpenTiming.MayTakeSlot(Phase.Idle, false, 0f));

    [Test]
    public void MayTakeSlot_OnceSettledAndTheBeatHasPassed()
        => Assert.IsTrue(SuggestionSlotOpenTiming.MayTakeSlot(Phase.Idle, false, AfterBeat));

    [Test]
    public void MayTakeSlot_ExactlyAtTheBeat()
        => Assert.IsTrue(SuggestionSlotOpenTiming.MayTakeSlot(
            Phase.Idle, false, SuggestionSlotOpenTiming.SettleDelaySeconds));

    [Test]
    public void MayNotTakeSlot_WithNoSettleInstantRecorded()
        // -1 is the "still opening" sentinel. Reading it as an elapsed beat (it is smaller than
        // any real one, but it is also smaller than zero) must not pass the delay check by
        // accident on some future rewrite of the comparison.
        => Assert.IsFalse(SuggestionSlotOpenTiming.MayTakeSlot(Phase.Idle, false, -1f));

    [Test]
    public void MayNotTakeSlot_LongAfterSettlingIfTheChatStartsOpeningAgain()
        // The controller clears the stamp on every unsettled frame; this pins that a stale long
        // elapsed value alone can never open the slot mid-animation.
        => Assert.IsFalse(SuggestionSlotOpenTiming.MayTakeSlot(Phase.Slide, true, 10f));

    [Test]
    public void SettleBeat_IsShortEnoughToReadAsImmediateAndLongerThanOneFrame()
    {
        Assert.Greater(SuggestionSlotOpenTiming.SettleDelaySeconds, 1f / 60f);
        Assert.Less(SuggestionSlotOpenTiming.SettleDelaySeconds, 0.3f);
    }
}

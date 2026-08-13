using NUnit.Framework;

// EditMode coverage for SuggestionSlotGestures — the pure handle-drag and thread-tap rules of the
// suggestions slot (sketch-005 variant E). Pins: dragging UP grows the slot and the result is
// clamped to [0, max] with no snapping (detents are a release-time decision, not a drag one), a
// dropped pointer frame holds the grab height instead of teleporting, an unknown ceiling clamps the
// floor ONLY while a real ceiling of 0 still binds, and a tap is only a tap when it is stationary,
// unclaimed and on a still thread — distance-tested, so a diagonal scroll that is under tolerance
// on BOTH axes is still a scroll.
public class SuggestionSlotGesturesTests
{
    private const float Grab = 780f;
    private const float Max = 1200f;

    // --- HeightFromDrag -----------------------------------------------------

    [Test]
    public void Drag_Up_GrowsTheSlot()
        // The handle is on the panel's TOP edge, so the caller hands us positive-is-up.
        => Assert.AreEqual(980f, SuggestionSlotGestures.HeightFromDrag(Grab, 200f, Max));

    [Test]
    public void Drag_Down_ShrinksTheSlot()
        => Assert.AreEqual(480f, SuggestionSlotGestures.HeightFromDrag(Grab, -300f, Max));

    [Test]
    public void Drag_ClampsAtTheCeiling()
        => Assert.AreEqual(Max, SuggestionSlotGestures.HeightFromDrag(Grab, 900f, Max));

    [Test]
    public void Drag_ClampsAtTheFloor()
        // Collapsed is reachable by dragging down, but never past it.
        => Assert.AreEqual(0f, SuggestionSlotGestures.HeightFromDrag(Grab, -900f, Max));

    [Test]
    public void Drag_FromAMidAnimationGrabHeight_TracksFreely_NoDetentSnap()
        // Grabbing the handle mid-snap starts from wherever the panel actually is; snapping to a
        // detent happens on release, not while the finger is down.
        => Assert.AreEqual(673.5f, SuggestionSlotGestures.HeightFromDrag(613.5f, 60f, Max));

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Drag_NonFiniteDelta_HoldsTheGrabHeight(float badDelta)
        // A bad pointer frame must not teleport the panel.
        => Assert.AreEqual(Grab, SuggestionSlotGestures.HeightFromDrag(Grab, badDelta, Max));

    [Test]
    public void Drag_NonFiniteDelta_StillClampsTheHeldHeight()
        => Assert.AreEqual(Max, SuggestionSlotGestures.HeightFromDrag(2000f, float.NaN, Max));

    [Test]
    public void Drag_NonFiniteGrabHeight_ResolvesToTheFloor()
        // No grab position at all: the delta is meaningless, so the only defined answer is 0.
        => Assert.AreEqual(0f, SuggestionSlotGestures.HeightFromDrag(float.NaN, 200f, Max));

    [Test]
    public void Drag_NegativeGrabHeight_ResolvesToTheFloor()
        // Finite garbage is rejected by the floor clamp, not carried forward as a negative slot.
        => Assert.AreEqual(0f, SuggestionSlotGestures.HeightFromDrag(-50f, 0f, Max));

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(-1f)]
    public void Drag_UnknownCeiling_ClampsTheFloorOnly(float badMax)
        // A bad max read means "no known ceiling" — collapsing to 0 here would slam the slot shut
        // under the user's finger.
        => Assert.AreEqual(5780f, SuggestionSlotGestures.HeightFromDrag(Grab, 5000f, badMax));

    [Test]
    public void Drag_UnknownCeiling_StillClampsTheFloor()
        => Assert.AreEqual(0f, SuggestionSlotGestures.HeightFromDrag(Grab, -5000f, -1f));

    [Test]
    public void Drag_ZeroCeiling_IsARealCeiling_NotAnUnknownOne()
        // The "no known ceiling" escape hatch is for non-finite/negative reads only: 0 is a finite,
        // legitimate ceiling (the slot may not open at all) and must still bind.
        => Assert.AreEqual(0f, SuggestionSlotGestures.HeightFromDrag(Grab, 200f, 0f));

    // --- IsThreadTap --------------------------------------------------------

    [Test]
    public void Tap_StationaryPressInsideTheThread_IsATap()
        => Assert.IsTrue(SuggestionSlotGestures.IsThreadTap(
            100f, 200f, 102f, 203f, true, false, false));

    [Test]
    public void Tap_PressOutsideTheThread_IsNotATap()
        => Assert.IsFalse(SuggestionSlotGestures.IsThreadTap(
            100f, 200f, 102f, 203f, false, false, false));

    [Test]
    public void Tap_OnAFlingingList_IsAFlingStop_NotATap()
        => Assert.IsFalse(SuggestionSlotGestures.IsThreadTap(
            100f, 200f, 102f, 203f, true, true, false));

    [Test]
    public void Tap_ClaimedByAnotherGesture_IsNotATap()
        // Bubble long-press, swipe-to-reply, a link tap or an open modal already owns this press.
        => Assert.IsFalse(SuggestionSlotGestures.IsThreadTap(
            100f, 200f, 102f, 203f, true, false, true));

    [Test]
    public void Tap_MoveExactlyAtTolerance_StillATap()
        => Assert.IsTrue(SuggestionSlotGestures.IsThreadTap(
            100f, 200f, 100f + SuggestionSlotGestures.TapMoveToleranceCanvasPx, 200f,
            true, false, false));

    [Test]
    public void Tap_MoveJustPastTolerance_IsADrag()
        => Assert.IsFalse(SuggestionSlotGestures.IsThreadTap(
            100f, 200f, 100f + SuggestionSlotGestures.TapMoveToleranceCanvasPx + 1f, 200f,
            true, false, false));

    [Test]
    public void Tap_DiagonalUnderToleranceOnBothAxes_IsStillADrag()
        // Why this is a DISTANCE test and not a per-axis one: each component is inside the
        // tolerance, but the finger travelled ~57 units.
        => Assert.IsFalse(SuggestionSlotGestures.IsThreadTap(
            100f, 200f,
            100f + (SuggestionSlotGestures.TapMoveToleranceCanvasPx - 5f),
            200f + (SuggestionSlotGestures.TapMoveToleranceCanvasPx - 5f),
            true, false, false));

    [TestCase(float.NaN, 200f, 102f, 203f)]
    [TestCase(100f, float.NaN, 102f, 203f)]
    [TestCase(100f, 200f, float.PositiveInfinity, 203f)]
    [TestCase(100f, 200f, 102f, float.NegativeInfinity)]
    public void Tap_NonFiniteCoordinate_IsNeverATap(float pressX, float pressY, float releaseX, float releaseY)
        // A broken pointer frame must never raise the panel.
        => Assert.IsFalse(SuggestionSlotGestures.IsThreadTap(
            pressX, pressY, releaseX, releaseY, true, false, false));
}

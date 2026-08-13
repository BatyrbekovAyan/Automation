using UnityEngine;

/// <summary>
/// Pure gesture rules for the suggestions-panel slot (sketch-005 variant E): the drag handle's
/// arithmetic and the thread-tap eligibility test. The panel is the slot's DEFAULT tenant and only
/// a drag on the handle may collapse it, so these rules protect two invariants — while the finger
/// is down the slot tracks it and nothing else (no detent snapping here, no teleport on a dropped
/// pointer frame, no collapse from a bad ceiling read), and a press only counts as a tap when it is
/// stationary, unclaimed and lands on a still thread. All lengths are canvas reference units in the
/// safe-adjusted space (the space KeyboardAwarePanel.VirtualBottomInset works in), never screen
/// pixels.
/// </summary>
public static class SuggestionSlotGestures
{
    /// <summary>
    /// A press that travels further than this is a scroll/swipe, not a tap — 15 screen px at the
    /// project's 1080-wide reference canvas on a typical 3× device ≈ 45 canvas units, so this
    /// mirrors the runtime EventSystem.pixelDragThreshold that SwipeToBack.Awake forces to 15
    /// (overwriting the serialized 10).
    /// </summary>
    public const float TapMoveToleranceCanvasPx = 45f;

    /// <summary>
    /// Slot height while the handle is dragged, tracking the finger freely — snapping to a detent is
    /// a release-time decision (SuggestionSlotDetents.Snap), never a per-frame one. The handle sits
    /// on the panel's TOP edge, so dragDeltaCanvasPx is POSITIVE-IS-UP (the caller converts pointer
    /// deltas): up grows the slot. A non-finite DELTA is a dropped pointer frame, so the slot holds
    /// its grab height instead of teleporting; a non-finite or negative MAX means "no known ceiling"
    /// and clamps the floor only, because reading a bad ceiling as 0 would slam the slot shut under
    /// the user's finger. A non-finite GRAB height is the one input with no safe stand-in — the
    /// floor is its only defined value, and since the release then snaps that to Collapsed, the
    /// caller must capture a real height on pointer-down rather than lean on this fallback.
    /// </summary>
    public static float HeightFromDrag(float heightAtGrabCanvasPx, float dragDeltaCanvasPx, float maxCanvasPx)
    {
        bool badFrame = !float.IsFinite(heightAtGrabCanvasPx) || !float.IsFinite(dragDeltaCanvasPx);

        // A non-finite grab height is no position at all, so the floor is its only defined value.
        float grab = float.IsFinite(heightAtGrabCanvasPx) ? heightAtGrabCanvasPx : 0f;
        float target = badFrame ? grab : grab + dragDeltaCanvasPx;

        // A max of exactly 0 is a real ceiling (the slot may not open), not an unknown one.
        return float.IsFinite(maxCanvasPx) && maxCanvasPx >= 0f
            ? Mathf.Clamp(target, 0f, maxCanvasPx)
            : Mathf.Max(target, 0f);
    }

    /// <summary>
    /// Whether a press on the message thread counts as a tap (the only gesture that may RAISE the
    /// slot — taps never collapse it). Every veto is a press that already belongs to something
    /// else: outside the thread, a fling-stop on a flying list (the ScrollClickBlocker.IsBlocking
    /// rule), a bubble long-press / swipe-to-reply / link tap / open modal that claimed it, or a
    /// press that travelled far enough to be a scroll. A non-finite coordinate is a broken pointer
    /// frame and must never raise the panel.
    /// </summary>
    public static bool IsThreadTap(
        float pressX, float pressY, float releaseX, float releaseY,
        bool pressWasInsideThread, bool scrollWasFlinging, bool otherGestureOwnedIt)
    {
        if (!pressWasInsideThread) return false;
        if (scrollWasFlinging) return false;
        if (otherGestureOwnedIt) return false;
        if (!float.IsFinite(pressX) || !float.IsFinite(pressY) ||
            !float.IsFinite(releaseX) || !float.IsFinite(releaseY)) return false;

        float dx = releaseX - pressX;
        float dy = releaseY - pressY;

        // Squared compare: both sides are non-negative, so the ordering is identical to comparing
        // distances and the per-pointer-up sqrt buys nothing.
        return dx * dx + dy * dy <= TapMoveToleranceCanvasPx * TapMoveToleranceCanvasPx;
    }
}

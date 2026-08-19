/// <summary>
/// Pure rules for the thread PULL-DOWN — the second way into the collapsed slot (owner request
/// 2026-08-19), next to the 42u handle. It mirrors iOS's interactive keyboard dismissal: while the
/// finger drags the message thread downward nothing moves, and the moment it reaches the composer's
/// top edge the slot starts tracking it 1:1.
/// <para>
/// ENGAGE IS A POSITION TEST, not a delta test. The gesture starts at the composer's top edge —
/// the line the owner actually described — and that choice is what makes the handoff continuous:
/// at the crossing instant the finger IS that edge, so <see cref="HeightFromPull"/> returns exactly
/// the height already on screen and the panel cannot jump under the finger. A delta-based rule
/// would start the gesture wherever the finger happened to be.
/// </para>
/// <para>
/// TRACKING is deliberately the handle's own arithmetic
/// (<see cref="SuggestionSlotGestures.HeightFromDrag"/>) with the ceiling pinned to the height at
/// engage: this gesture may shrink the slot and put it back, never grow it past where it started.
/// It is a dismissal, not a second way to expand — expanding stays the handle's job.
/// </para>
/// <para>
/// All lengths are CANVAS reference units in the safe-adjusted space (the space
/// KeyboardAwarePanel.VirtualBottomInset works in), on Unity's POSITIVE-IS-UP pointer axis.
/// </para>
/// </summary>
public static class SuggestionSlotPullDown
{
    /// <summary>
    /// Does this frame start the pull-down? True only on the crossing frame of an eligible gesture.
    /// <paramref name="alreadyEngaged"/> makes every later frame false so the grab height is
    /// captured exactly once — re-origining mid-gesture would stop the slot following the finger.
    /// A non-finite coordinate is a broken pointer frame or a broken geometry read and must never
    /// take the slot. <paramref name="eligible"/> is the caller's whole veto set (nothing to
    /// dismiss, a modal owning the region, the chat still opening, a back-swipe in flight) folded
    /// into one bit, so this seam stays free of scene knowledge.
    /// </summary>
    public static bool ShouldEngage(
        float fingerCanvasY, float composerTopCanvasY, bool alreadyEngaged, bool eligible)
    {
        if (alreadyEngaged || !eligible) return false;
        if (!float.IsFinite(fingerCanvasY) || !float.IsFinite(composerTopCanvasY)) return false;
        return fingerCanvasY < composerTopCanvasY;
    }

    /// <summary>
    /// Slot height while the pull-down runs. The height at engage is BOTH the origin and the
    /// ceiling. Delegates to the handle's arithmetic on purpose — one implementation of "track the
    /// finger, clamp to the floor and the ceiling, survive a dropped frame" for both entries.
    /// </summary>
    public static float HeightFromPull(
        float heightAtEngageCanvasPx, float fingerCanvasY, float engageFingerCanvasY)
        => SuggestionSlotGestures.HeightFromDrag(
            heightAtEngageCanvasPx, fingerCanvasY - engageFingerCanvasY, heightAtEngageCanvasPx);
}

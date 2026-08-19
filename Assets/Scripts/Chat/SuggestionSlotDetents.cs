using UnityEngine;

/// <summary>The three heights the suggestions slot is allowed to rest at (sketch-005 model E).</summary>
public enum SlotDetent
{
    /// <summary>Slot height 0, composer flush to the screen bottom — reachable ONLY by dragging the handle down, never by a tap.</summary>
    Collapsed,

    /// <summary>The slot's default height: the measured keyboard height, so a panel ⇄ keyboard swap never moves the composer.</summary>
    Standard,

    /// <summary>Tall enough for the whole card stack, capped so the message thread never disappears behind the panel.</summary>
    Expanded
}

/// <summary>
/// Pure detent math for the suggestions slot's drag handle (sketch-005 model E). The slot is the
/// native keyboard's region at the bottom of the messages screen and the panel is its DEFAULT
/// tenant, so a free drag must always settle on one of three defined heights — Collapsed(0) /
/// Standard(the measured keyboard height) / Expanded(the whole card stack). Two invariants live
/// here: an expanded panel always leaves a readable slice of thread above it, and Collapsed is
/// reached only by a deliberate downward drag — so every ambiguous input (an exact midpoint, a
/// NaN, a negative) resolves UPWARD, never to 0. All heights are CANVAS reference units in the
/// safe-adjusted space (the space KeyboardAwarePanel.VirtualBottomInset works in), never screen
/// pixels. The keyboard-measurement sanity window (SuggestionSlotHeight.IsValid) belongs upstream:
/// this seam receives an already-resolved slot height, and drag/expanded heights legitimately live
/// outside that window, so nothing here is re-gated by it.
/// </summary>
public static class SuggestionSlotDetents
{
    /// <summary>The expanded detent must always leave at least this much of the message thread visible.</summary>
    public const float MinThreadVisibleCanvasPx = 360f;

    /// <summary>
    /// Finger speed at which a release counts as a flick rather than a placement (canvas units per
    /// second). A device-tuning knob — it lives here so both drag entries read the same number.
    /// </summary>
    public const float FlickVelocityCanvasPxPerSec = 2200f;

    /// <summary>
    /// How far the SLOT must actually have moved before a fast release is allowed to count as a
    /// flick. Load-bearing, not a nicety: the pull-down's engage line is the composer's top edge —
    /// roughly the lower 40% of the screen — so ordinary "scroll back through history" gestures
    /// routinely cross it at speed. Without this minimum every one of them would read as a flick
    /// and collapse the panel the owner was reading.
    /// </summary>
    public const float MinFlickTravelCanvasPx = 60f;

    /// <summary>
    /// Height of the third detent: the panel's natural size (chrome = handle strip + header, plus
    /// the measured card stack), never shorter than standard and never so tall that less than
    /// MinThreadVisibleCanvasPx of thread survives. The cap itself never drops below standard —
    /// a short screen must still be allowed its standard detent. Content that already fits inside
    /// standard has no third detent, so the answer is exactly standard. Garbage chrome/content/
    /// thread (NaN, Infinity, negative) falls back to standard, never to a silent 0; a standard
    /// that is itself non-finite or non-positive is a caller bug and yields the clamped-to-zero
    /// height rather than propagating the bad value.
    /// </summary>
    public static float ExpandedHeight(
        float chromeCanvasPx, float contentCanvasPx, float standardCanvasPx, float threadRestHeightCanvasPx)
    {
        // Standard is the fallback for every other bad input, so it has to be sane first.
        if (!float.IsFinite(standardCanvasPx)) return 0f;
        if (standardCanvasPx <= 0f) return Mathf.Max(0f, standardCanvasPx);

        if (!IsNonNegativeFinite(chromeCanvasPx) ||
            !IsNonNegativeFinite(contentCanvasPx) ||
            !IsNonNegativeFinite(threadRestHeightCanvasPx))
            return standardCanvasPx;

        float natural = chromeCanvasPx + contentCanvasPx;
        if (natural <= standardCanvasPx) return standardCanvasPx;   // third detent collapses into standard

        float cap = Mathf.Max(standardCanvasPx, threadRestHeightCanvasPx - MinThreadVisibleCanvasPx);
        return Mathf.Clamp(natural, standardCanvasPx, cap);
    }

    /// <summary>The third detent exists only when it is meaningfully taller than standard — within 1u it IS standard.</summary>
    public static bool HasExpandedDetent(float standardCanvasPx, float expandedCanvasPx)
        => expandedCanvasPx > standardCanvasPx + 1f;

    /// <summary>
    /// Where a released drag settles: the nearest AVAILABLE detent height wins, and an exact
    /// midpoint resolves UPWARD to the taller one — model E says Collapsed is reachable only by a
    /// deliberate drag, so a tie must never collapse. A drag past the top settles on the tallest
    /// available detent; a non-finite or negative drag settles on Standard, and so does a
    /// non-finite standard — an unmeasurable slot must never read as a deliberate collapse.
    /// </summary>
    public static SlotDetent Snap(float draggedCanvasPx, float standardCanvasPx, float expandedCanvasPx)
    {
        if (!float.IsFinite(draggedCanvasPx) || draggedCanvasPx < 0f) return SlotDetent.Standard;

        // An infinite standard puts EVERY finite drag nearer to 0 than to the slot, so the pivot
        // below would collapse the panel off a glitched measurement (a 0 canvas scale divides to
        // Infinity). Reject it like a bad drag. NaN needs no guard — it fails both comparisons.
        if (!float.IsFinite(standardCanvasPx)) return SlotDetent.Standard;

        // Standard is the pivot: Collapsed must be STRICTLY closer to win (a tie keeps the taller
        // detent), while Expanded wins on a tie. The detents are ordered, so at most one of the two
        // outer candidates can ever beat the pivot.
        float standardDistance = Mathf.Abs(draggedCanvasPx - standardCanvasPx);

        if (Mathf.Abs(draggedCanvasPx) < standardDistance) return SlotDetent.Collapsed;

        if (HasExpandedDetent(standardCanvasPx, expandedCanvasPx) &&
            Mathf.Abs(draggedCanvasPx - expandedCanvasPx) <= standardDistance)
            return SlotDetent.Expanded;

        return SlotDetent.Standard;
    }

    /// <summary>
    /// Where a released drag settles once speed is taken into account: a genuine flick wins over
    /// the half-way rule, everything else falls through to <see cref="Snap"/> unchanged.
    /// <para>
    /// <paramref name="velocityCanvasPxPerSec"/> is on Unity's POSITIVE-IS-UP pointer axis, so a
    /// flick DOWN is negative. <paramref name="travelCanvasPx"/> is how far the SLOT moved since
    /// the grab (a distance — the sign is ignored), not how far the finger moved.
    /// <paramref name="ceilingCanvasPx"/> is the GESTURE's own ceiling and is what makes the two
    /// entries differ on a flick up: the handle's ceiling is the expanded detent, so it may expand;
    /// the pull-down's ceiling is the height it engaged at, so it restores and stops there — that
    /// gesture is a dismissal and must never grow a panel past where the owner found it.
    /// </para>
    /// </summary>
    public static SlotDetent SnapWithFlick(
        float draggedCanvasPx, float standardCanvasPx, float expandedCanvasPx,
        float velocityCanvasPxPerSec, float travelCanvasPx, float ceilingCanvasPx)
    {
        if (!IsFlick(velocityCanvasPxPerSec, travelCanvasPx))
            return Snap(draggedCanvasPx, standardCanvasPx, expandedCanvasPx);

        return velocityCanvasPxPerSec < 0f
            ? SlotDetent.Collapsed
            : TallestUnderCeiling(standardCanvasPx, expandedCanvasPx, ceilingCanvasPx);
    }

    /// <summary>Both gates must pass. A non-finite reading of either is a broken frame and is never
    /// a flick — the position rule is always the safe fallback.</summary>
    private static bool IsFlick(float velocityCanvasPxPerSec, float travelCanvasPx)
        => float.IsFinite(velocityCanvasPxPerSec) && float.IsFinite(travelCanvasPx)
           && Mathf.Abs(velocityCanvasPxPerSec) >= FlickVelocityCanvasPxPerSec
           && Mathf.Abs(travelCanvasPx) >= MinFlickTravelCanvasPx;

    /// <summary>The tallest detent a gesture with this ceiling is allowed to land on. The 1u slack
    /// matches <see cref="HasExpandedDetent"/>'s epsilon, so a ceiling that IS the expanded detent
    /// still qualifies after float arithmetic.</summary>
    private static SlotDetent TallestUnderCeiling(
        float standardCanvasPx, float expandedCanvasPx, float ceilingCanvasPx)
        => HasExpandedDetent(standardCanvasPx, expandedCanvasPx)
           && float.IsFinite(ceilingCanvasPx)
           && expandedCanvasPx <= ceilingCanvasPx + 1f
            ? SlotDetent.Expanded
            : SlotDetent.Standard;

    /// <summary>The slot height a detent means; asking for Expanded where no third detent exists yields standard, and an unknown detent never silently collapses.</summary>
    public static float HeightFor(SlotDetent detent, float standardCanvasPx, float expandedCanvasPx)
    {
        switch (detent)
        {
            case SlotDetent.Collapsed:
                return 0f;
            case SlotDetent.Expanded:
                return HasExpandedDetent(standardCanvasPx, expandedCanvasPx) ? expandedCanvasPx : standardCanvasPx;
            default:
                return standardCanvasPx;
        }
    }

    private static bool IsNonNegativeFinite(float canvasPx) => float.IsFinite(canvasPx) && canvasPx >= 0f;
}

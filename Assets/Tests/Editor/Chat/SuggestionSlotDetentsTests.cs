using NUnit.Framework;

// EditMode coverage for SuggestionSlotDetents — the pure detent rules of the suggestions slot's
// drag handle (sketch-005 model E). Pins the two load-bearing invariants: an ambiguous drag never
// collapses (exact midpoints resolve UPWARD, garbage resolves to Standard), and the expanded
// detent never eats more thread than MinThreadVisibleCanvasPx allows — while its cap still never
// drops below the standard detent on a short screen. Every "no third detent" case deliberately
// passes an expanded height that DIFFERS from standard, so the fallback cannot pass by echo.
public class SuggestionSlotDetentsTests
{
    private const float Standard = 780f;    // the measured keyboard height
    private const float Expanded = 1200f;   // a third detent that comfortably clears the 1u epsilon
    private const float Chrome = 120f;      // handle strip + header
    private const float ThreadRest = 1800f; // thread height with the slot collapsed

    // Taller than standard yet INSIDE the 1u epsilon: a height that exists numerically but must
    // never behave as a third detent.
    private const float WithinEpsilon = Standard + 0.5f;

    // --- Snap: the detents snap to themselves -------------------------------

    [Test]
    public void Snap_AtCollapsedHeight_StaysCollapsed()
        => Assert.AreEqual(SlotDetent.Collapsed, SuggestionSlotDetents.Snap(0f, Standard, Expanded));

    [Test]
    public void Snap_AtStandardHeight_StaysStandard()
        => Assert.AreEqual(SlotDetent.Standard, SuggestionSlotDetents.Snap(Standard, Standard, Expanded));

    [Test]
    public void Snap_AtExpandedHeight_StaysExpanded()
        => Assert.AreEqual(SlotDetent.Expanded, SuggestionSlotDetents.Snap(Expanded, Standard, Expanded));

    // --- Snap: ties resolve UPWARD ------------------------------------------

    [Test]
    public void Snap_MidpointOfCollapsedAndStandard_ResolvesUpwardToStandard()
        // Model E: Collapsed is reachable only by a deliberate drag, so a tie must never collapse.
        => Assert.AreEqual(SlotDetent.Standard, SuggestionSlotDetents.Snap(Standard / 2f, Standard, Expanded));

    [Test]
    public void Snap_MidpointOfStandardAndExpanded_ResolvesUpwardToExpanded()
        => Assert.AreEqual(
            SlotDetent.Expanded, SuggestionSlotDetents.Snap((Standard + Expanded) / 2f, Standard, Expanded));

    // --- Snap: the rest of the number line ----------------------------------

    [Test]
    public void Snap_BelowTheCollapsedStandardMidpoint_Collapses()
        // Past the midpoint the drag IS deliberate — this is the only way to reach Collapsed.
        => Assert.AreEqual(SlotDetent.Collapsed, SuggestionSlotDetents.Snap(Standard / 2f - 10f, Standard, Expanded));

    [Test]
    public void Snap_DraggedAboveExpanded_ClampsToTheTallestDetent()
        => Assert.AreEqual(SlotDetent.Expanded, SuggestionSlotDetents.Snap(2000f, Standard, Expanded));

    [Test]
    public void Snap_NoExpandedDetent_TallDragStopsAtStandard()
        // Content fits inside standard → the third detent does not exist and cannot be snapped to.
        => Assert.AreEqual(SlotDetent.Standard, SuggestionSlotDetents.Snap(2000f, Standard, Standard));

    [Test]
    public void Snap_NoExpandedDetent_LowDragStillCollapses()
        => Assert.AreEqual(SlotDetent.Collapsed, SuggestionSlotDetents.Snap(50f, Standard, Standard));

    [Test]
    public void Snap_ExpandedWithinTheEpsilon_IsNotASnapTarget()
        // The drag is all but ON the sub-epsilon height and still must land on Standard — the
        // epsilon gates Snap itself, not just the HasExpandedDetent query.
        => Assert.AreEqual(
            SlotDetent.Standard, SuggestionSlotDetents.Snap(WithinEpsilon, Standard, WithinEpsilon));

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(-1f)]
    [TestCase(-5000f)]
    public void Snap_GarbageDrag_ReturnsStandardNeverCollapsed(float draggedCanvasPx)
        => Assert.AreEqual(SlotDetent.Standard, SuggestionSlotDetents.Snap(draggedCanvasPx, Standard, Expanded));

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Snap_NonFiniteStandard_ReturnsStandardNeverCollapsed(float standardCanvasPx)
        // A glitched measurement (a 0 canvas scale divides to Infinity) sits infinitely far from
        // every finite drag, which would make the nearest-detent math read 0 as "nearest" and
        // collapse the panel — the one thing a non-drag is never allowed to do.
        => Assert.AreEqual(
            SlotDetent.Standard, SuggestionSlotDetents.Snap(Standard / 2f, standardCanvasPx, Expanded));

    // --- HasExpandedDetent --------------------------------------------------

    [Test]
    public void HasExpandedDetent_ClearlyTallerThanStandard_True()
        => Assert.IsTrue(SuggestionSlotDetents.HasExpandedDetent(Standard, Expanded));

    [TestCase(Standard)]
    [TestCase(WithinEpsilon)]
    [TestCase(Standard + 1f)]
    public void HasExpandedDetent_WithinTheOneUnitEpsilon_False(float expandedCanvasPx)
        => Assert.IsFalse(SuggestionSlotDetents.HasExpandedDetent(Standard, expandedCanvasPx));

    [TestCase(Standard + 1.05f)]
    [TestCase(Standard + 2f)]
    public void HasExpandedDetent_JustPastTheOneUnitEpsilon_True(float expandedCanvasPx)
        // Pairs with the False cases above to pin the epsilon at 1u and nothing else: a wider one
        // would swallow the 1.05 case, a narrower one would let Standard + 1f through. (Float ULP
        // near 781 is ~6e-5, so the 0.05 margin is exact, not a rounding coincidence.)
        => Assert.IsTrue(SuggestionSlotDetents.HasExpandedDetent(Standard, expandedCanvasPx));

    // --- HeightFor ----------------------------------------------------------

    [Test]
    public void HeightFor_Collapsed_IsZero()
        => Assert.AreEqual(0f, SuggestionSlotDetents.HeightFor(SlotDetent.Collapsed, Standard, Expanded));

    [Test]
    public void HeightFor_Standard_IsTheStandardHeight()
        => Assert.AreEqual(Standard, SuggestionSlotDetents.HeightFor(SlotDetent.Standard, Standard, Expanded));

    [Test]
    public void HeightFor_Expanded_IsTheExpandedHeight()
        => Assert.AreEqual(Expanded, SuggestionSlotDetents.HeightFor(SlotDetent.Expanded, Standard, Expanded));

    [TestCase(Standard)]
    [TestCase(WithinEpsilon)]
    [TestCase(Standard + 1f)]
    public void HeightFor_ExpandedWithinTheEpsilon_FallsBackToStandardNotTheRawValue(float expandedCanvasPx)
        // The sub-epsilon cases must return STANDARD, not the value handed in — an expanded height
        // equal to standard would let a missing fallback pass by echo.
        => Assert.AreEqual(
            Standard, SuggestionSlotDetents.HeightFor(SlotDetent.Expanded, Standard, expandedCanvasPx));

    [Test]
    public void HeightFor_UnknownDetent_FallsBackToStandardNeverZero()
        // The sibling state machine casts out-of-range detents; an unknown one must not silently
        // collapse the slot.
        => Assert.AreEqual(Standard, SuggestionSlotDetents.HeightFor((SlotDetent)99, Standard, Expanded));

    // --- ExpandedHeight -----------------------------------------------------

    [Test]
    public void ExpandedHeight_ContentTallerThanStandard_IsChromePlusContent()
        => Assert.AreEqual(Chrome + 900f, SuggestionSlotDetents.ExpandedHeight(Chrome, 900f, Standard, ThreadRest));

    [Test]
    public void ExpandedHeight_ContentFitsInsideStandard_CollapsesIntoStandardExactly()
        => Assert.AreEqual(Standard, SuggestionSlotDetents.ExpandedHeight(Chrome, 300f, Standard, ThreadRest));

    [Test]
    public void ExpandedHeight_ContentFitsInsideStandard_LeavesNoThirdDetent()
        => Assert.IsFalse(SuggestionSlotDetents.HasExpandedDetent(
            Standard, SuggestionSlotDetents.ExpandedHeight(Chrome, 300f, Standard, ThreadRest)));

    [Test]
    public void ExpandedHeight_HugeContent_IsCappedToKeepTheThreadVisible()
    {
        float expanded = SuggestionSlotDetents.ExpandedHeight(Chrome, 5000f, Standard, ThreadRest);
        Assert.AreEqual(ThreadRest - SuggestionSlotDetents.MinThreadVisibleCanvasPx, expanded);
        Assert.AreEqual(SuggestionSlotDetents.MinThreadVisibleCanvasPx, ThreadRest - expanded);
    }

    [Test]
    public void ExpandedHeight_ShortThread_CapNeverDropsBelowStandard()
    {
        // Surrender MinThreadVisible on THIS thread and what is left is shorter than standard, so
        // the cap would land below standard — a tiny screen must still be allowed its standard detent.
        const float shortThread = Standard + SuggestionSlotDetents.MinThreadVisibleCanvasPx - 240f;
        Assert.Less(shortThread - SuggestionSlotDetents.MinThreadVisibleCanvasPx, Standard);
        Assert.AreEqual(Standard, SuggestionSlotDetents.ExpandedHeight(Chrome, 5000f, Standard, shortThread));
    }

    [Test]
    public void ExpandedHeight_NaturalLandsInsideTheEpsilon_IsInertEverywhere()
    {
        // Nothing forces ExpandedHeight to round a barely-taller-than-standard natural size away,
        // so the epsilon is what has to keep that height from behaving as a third detent.
        float expanded = SuggestionSlotDetents.ExpandedHeight(
            Chrome, WithinEpsilon - Chrome, Standard, ThreadRest);
        Assert.AreEqual(WithinEpsilon, expanded);
        Assert.IsFalse(SuggestionSlotDetents.HasExpandedDetent(Standard, expanded));
        Assert.AreEqual(Standard, SuggestionSlotDetents.HeightFor(SlotDetent.Expanded, Standard, expanded));
        Assert.AreEqual(SlotDetent.Standard, SuggestionSlotDetents.Snap(expanded, Standard, expanded));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void ExpandedHeight_GarbageContent_FallsBackToStandard(float contentCanvasPx)
        => Assert.AreEqual(Standard, SuggestionSlotDetents.ExpandedHeight(Chrome, contentCanvasPx, Standard, ThreadRest));

    [TestCase(float.NaN)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(-1f)]
    public void ExpandedHeight_GarbageChrome_FallsBackToStandard(float chromeCanvasPx)
        => Assert.AreEqual(Standard, SuggestionSlotDetents.ExpandedHeight(chromeCanvasPx, 900f, Standard, ThreadRest));

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void ExpandedHeight_GarbageThread_FallsBackToStandard(float threadRestHeightCanvasPx)
        => Assert.AreEqual(
            Standard, SuggestionSlotDetents.ExpandedHeight(Chrome, 900f, Standard, threadRestHeightCanvasPx));

    [Test]
    public void ExpandedHeight_NonFiniteStandard_IsZeroAndNeverNaN()
    {
        float expanded = SuggestionSlotDetents.ExpandedHeight(Chrome, 900f, float.NaN, ThreadRest);
        Assert.IsFalse(float.IsNaN(expanded));
        Assert.AreEqual(0f, expanded);
    }

    [Test]
    public void ExpandedHeight_NegativeStandard_ClampsToZero()
        // A caller bug either way, but the answer is never a negative slot height.
        => Assert.AreEqual(0f, SuggestionSlotDetents.ExpandedHeight(Chrome, 900f, -50f, ThreadRest));
}

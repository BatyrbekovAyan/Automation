using NUnit.Framework;

/// <summary>
/// Pins ScrollTopInsetMath — the rules that keep the messages thread fully reachable while
/// the MovingArea rides a bottom inset (native keyboard or the suggestions panel's slot
/// claim): the viewport's top edge stays screen-pinned (trim == applied rise, never a
/// negative rise), and a viewport that grows back never leaves the content parked outside
/// its own scroll range.
/// </summary>
public class ScrollTopInsetMathTests
{
    // --- TrimmedTopOffset: top edge pinned to its rest screen position ---

    [Test]
    public void Trim_AtRest_KeepsRestOffset()
        => Assert.AreEqual(-250f, ScrollTopInsetMath.TrimmedTopOffset(-250f, 0f));

    [Test]
    public void Trim_Risen_PullsTopDownByExactlyTheRise()
        => Assert.AreEqual(-1030f, ScrollTopInsetMath.TrimmedTopOffset(-250f, 780f));

    [Test]
    public void Trim_NegativeApplied_NeverGrowsViewport()
        => Assert.AreEqual(-250f, ScrollTopInsetMath.TrimmedTopOffset(-250f, -12f));

    // --- ClampContentY: content stays inside the resized range ---

    [Test]
    public void Clamp_ShortContent_OnlyBottomPinValid()
        => Assert.AreEqual(0f, ScrollTopInsetMath.ClampContentY(-300f, 400f, 1490f));

    [Test]
    public void Clamp_TallContent_InRangePassesThrough()
        => Assert.AreEqual(-200f, ScrollTopInsetMath.ClampContentY(-200f, 2000f, 1490f));

    [Test]
    public void Clamp_TallContent_PastTopSnapsToTop()
        => Assert.AreEqual(-510f, ScrollTopInsetMath.ClampContentY(-900f, 2000f, 1490f));

    [Test]
    public void Clamp_BelowBottom_SnapsToBottomPin()
        => Assert.AreEqual(0f, ScrollTopInsetMath.ClampContentY(120f, 2000f, 1490f));

    [Test]
    public void Clamp_ExactTop_PassesThrough()
        => Assert.AreEqual(-510f, ScrollTopInsetMath.ClampContentY(-510f, 2000f, 1490f));

    // --- ShouldClampContent -------------------------------------------------
    // The clamp exists for a viewport that resized while NOBODY was touching it (keyboard or panel
    // opening/closing). Once the thread pull-down started moving the slot inset DURING a gesture,
    // running it under the finger began tearing away ScrollRect's elastic overscroll — which is by
    // definition outside the range the clamp enforces. Device symptom, short thread at the top: the
    // stretch collapsed and the messages snapped upward the instant the pull-down engaged, then
    // stretched again once the slot bottomed out and the inset stopped changing.

    [Test]
    public void ShouldClampContent_WhileAFingerOwnsTheScroll_IsFalse()
        => Assert.IsFalse(ScrollTopInsetMath.ShouldClampContent(scrollIsDragging: true));

    [Test]
    public void ShouldClampContent_WhenNobodyIsDragging_IsTrue()
        => Assert.IsTrue(ScrollTopInsetMath.ShouldClampContent(scrollIsDragging: false));
}

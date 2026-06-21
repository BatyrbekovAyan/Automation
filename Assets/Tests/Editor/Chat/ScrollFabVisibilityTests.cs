using NUnit.Framework;

// Hysteresis policy for the scroll-to-bottom FAB's visibility. The decision is fed an
// ABSOLUTE pixel gap (how far the list is scrolled up from the newest message), never the
// verticalNormalizedPosition ratio — see ScrollFabMath.ShouldShow and MessageListView.RefreshFab
// for why the ratio flickered the FAB when a newest-area image bubble grew on download.
public class ScrollFabVisibilityTests
{
    const float Show = 160f;
    const float Hide = 48f;

    [Test]
    public void ScrolledWellUp_Shows()
        => Assert.IsTrue(ScrollFabMath.ShouldShow(false, 300f, Show, Hide));

    [Test]
    public void NearBottom_Hides()
        => Assert.IsFalse(ScrollFabMath.ShouldShow(true, 10f, Show, Hide));

    [Test]
    public void DeadBand_HoldsHiddenWhenHidden()
        => Assert.IsFalse(ScrollFabMath.ShouldShow(false, 100f, Show, Hide));

    [Test]
    public void DeadBand_HoldsShownWhenShown()
        => Assert.IsTrue(ScrollFabMath.ShouldShow(true, 100f, Show, Hide));

    // The reported bug: a newest-area image finishes downloading and the bubble grows, briefly
    // nudging the measured gap down into the dead-band. While shown, that must NOT hide the FAB.
    [Test]
    public void ImageGrowthDipIntoDeadBand_StaysShown()
        => Assert.IsTrue(ScrollFabMath.ShouldShow(true, 60f, Show, Hide));

    [Test]
    public void ShowThreshold_IsInclusive()
        => Assert.IsTrue(ScrollFabMath.ShouldShow(false, Show, Show, Hide));

    [Test]
    public void HideThreshold_IsInclusive()
        => Assert.IsFalse(ScrollFabMath.ShouldShow(true, Hide, Show, Hide));

    // Overscroll past the bottom yields a slightly negative gap — treat as "at bottom".
    [Test]
    public void NegativeGap_Hides()
        => Assert.IsFalse(ScrollFabMath.ShouldShow(true, -5f, Show, Hide));

    // Degenerate equal thresholds collapse to a plain >= threshold with no dead-band (no throw).
    [Test]
    public void EqualThresholds_AtThreshold_Shows()
        => Assert.IsTrue(ScrollFabMath.ShouldShow(false, 50f, 50f, 50f));

    [Test]
    public void EqualThresholds_BelowThreshold_Hides()
        => Assert.IsFalse(ScrollFabMath.ShouldShow(true, 49f, 50f, 50f));
}

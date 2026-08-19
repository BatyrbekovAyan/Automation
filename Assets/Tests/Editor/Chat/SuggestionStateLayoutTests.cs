using NUnit.Framework;

// EditMode coverage for SuggestionStateLayout — where the suggestions panel's empty / error block
// sits while the slot collapses under it. Pins the asymmetry that is the entire point: the block is
// CENTRED while the area can hold it, and PINNED TO THE TOP once it cannot, so it slides out through
// the screen bottom instead of climbing over the panel's header and the composer. Nothing masks
// these overlays — the RectMask2D is on the card Viewport and they are its siblings — so a negative
// offset is not "slightly off", it draws over the chat.
public class SuggestionStateLayoutTests
{
    private const float Block = 330f;   // heading + body + the «Обновить» pill

    [Test]
    public void TopOffset_AreaTallerThanTheBlock_Centres()
        => Assert.AreEqual(135f, SuggestionStateLayout.TopOffset(600f, Block), 0.001f);

    [Test]
    public void TopOffset_AreaExactlyTheBlock_IsZero()
        => Assert.AreEqual(0f, SuggestionStateLayout.TopOffset(Block, Block), 0.001f);

    // The regime that fixes the reported bug: the block stops centring and starts sliding.
    [Test]
    public void TopOffset_AreaShorterThanTheBlock_PinsToTheTop()
        => Assert.AreEqual(0f, SuggestionStateLayout.TopOffset(100f, Block), 0.001f);

    [Test]
    public void TopOffset_AreaCollapsedToNothing_PinsToTheTop()
        => Assert.AreEqual(0f, SuggestionStateLayout.TopOffset(0f, Block), 0.001f);

    // Never negative — a negative offset would put the block OVER the header and the composer.
    [Test]
    public void TopOffset_IsNeverNegative()
    {
        for (float area = 0f; area <= Block; area += 10f)
            Assert.GreaterOrEqual(SuggestionStateLayout.TopOffset(area, Block), 0f, $"area {area}");
    }

    [Test]
    public void TopOffset_UnsettledGeometry_PinsToTheTop()
    {
        Assert.AreEqual(0f, SuggestionStateLayout.TopOffset(float.NaN, Block), 0.001f);
        Assert.AreEqual(0f, SuggestionStateLayout.TopOffset(600f, float.NaN), 0.001f);
        Assert.AreEqual(0f, SuggestionStateLayout.TopOffset(float.PositiveInfinity, Block), 0.001f);
    }

    // A block measured as zero (state never laid out) must not push itself half an area down.
    [Test]
    public void TopOffset_UnmeasuredBlock_StillCentresOnTheArea()
        => Assert.AreEqual(300f, SuggestionStateLayout.TopOffset(600f, 0f), 0.001f);
}

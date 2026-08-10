using Automation.BotSettingsUI;
using NUnit.Framework;

/// <summary>
/// Pins the sizing math behind the Bot Settings scrollable cards. The measure
/// width is the value that decides whether a card believes it has anything to
/// scroll — measuring at the card width instead of the text column width
/// under-counts wrapped lines, so the card reports "nothing hidden", clips its
/// own tail, and hands drags to the page.
/// </summary>
public class ScrollableTextAreaMetricsTests
{
    // Real Bot Settings geometry: card 980 wide, TMP Text Area inset 40 per
    // side. Measuring at 980 fits more characters per line than the field
    // ever shows.
    private const float CardWidth = 980f;
    private const float TextColumnWidth = 900f;

    [Test]
    public void MeasuresAtTheTextColumn_NotTheCard()
    {
        Assert.AreEqual(
            TextColumnWidth,
            ScrollableTextAreaMetrics.MeasureWidth(TextColumnWidth, CardWidth));
    }

    // A RectTransform inside a ScrollRect reports ~2px until layout settles;
    // measuring a paragraph at 2px yields a 6000px-tall content rect.
    [Test]
    public void PreLayoutTextWidth_FallsBackToTheCardWidth()
    {
        Assert.AreEqual(CardWidth, ScrollableTextAreaMetrics.MeasureWidth(2f, CardWidth));
        Assert.AreEqual(CardWidth, ScrollableTextAreaMetrics.MeasureWidth(0f, CardWidth));
    }

    [Test]
    public void WidthSettled_GuardsAtHundred_NotAtOne()
    {
        Assert.IsFalse(ScrollableTextAreaMetrics.WidthSettled(99f));
        Assert.IsTrue(ScrollableTextAreaMetrics.WidthSettled(100f));
    }

    // TMP's Text Area is inset 32 top + 32 bottom from the input on these cards.
    private const float Chrome = 64f;

    [Test]
    public void ContentNeverShorterThanTheViewport()
    {
        Assert.AreEqual(360f, ScrollableTextAreaMetrics.ContentHeight(120f, Chrome, 8f, 360f));
    }

    [Test]
    public void OverflowingText_AddsBottomPadding()
    {
        Assert.AreEqual(572f, ScrollableTextAreaMetrics.ContentHeight(500f, Chrome, 8f, 360f));
    }

    // The regression this guards: content sized to the text alone leaves the
    // text viewport `chrome` shorter than the text, so TMP scrolls the text
    // internally to keep the caret visible and the FIRST ROW can never be
    // scrolled back into view — the offset lives on the text component, not on
    // the content our ScrollRect moves.
    [Test]
    public void TextViewportEndsUpTallerThanTheText()
    {
        const float textHeight = 787f;
        var content = ScrollableTextAreaMetrics.ContentHeight(textHeight, Chrome, 8f, 360f);

        Assert.GreaterOrEqual(content - Chrome, textHeight,
            "The text column must fit the text, or TMP takes over scrolling and strands row 1.");
    }

    [Test]
    public void NegativeChromeIsIgnored()
    {
        Assert.AreEqual(508f, ScrollableTextAreaMetrics.ContentHeight(500f, -40f, 8f, 360f));
    }

    // The whole point of the measure-width fix: a paragraph that needs more
    // rows at the true column width must produce content taller than the
    // card, or DragShield concludes there is nothing to scroll.
    [Test]
    public void NarrowerColumn_ProducesTallerContent_ThanTheCardWidthWouldHave()
    {
        // 12 rows at the real column width vs 11 rows measured at the card width.
        const float lineHeight = 32f;
        var atColumn = ScrollableTextAreaMetrics.ContentHeight(12f * lineHeight, Chrome, 8f, 360f);
        var atCard = ScrollableTextAreaMetrics.ContentHeight(11f * lineHeight, Chrome, 8f, 360f);

        Assert.Greater(atColumn, atCard);
        Assert.IsTrue(DragScrollRouting.HasHiddenText(atColumn, 360f),
            "Measured at the text column the card must know its text overflows.");
    }
}

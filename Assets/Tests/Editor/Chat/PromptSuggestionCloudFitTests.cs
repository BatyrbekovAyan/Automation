using Automation.BotSettingsUI;
using NUnit.Framework;

public class PromptSuggestionCloudFitTests
{
    private const float RowWidth = 980f;
    private const float Spacing = 24f;

    [Test]
    public void EmptyInput_TakesNothing()
    {
        Assert.AreEqual(0, PromptSuggestionCloudFit.Take(new float[0], RowWidth, Spacing, 3));
    }

    [Test]
    public void ChipsThatFitOneRow_AreAllTaken()
    {
        var widths = new[] { 300f, 300f, 300f };  // 300+24+300+24+300 = 948 <= 980
        Assert.AreEqual(3, PromptSuggestionCloudFit.Take(widths, RowWidth, Spacing, 3));
        Assert.AreEqual(new[] { 0, 0, 0 }, PromptSuggestionCloudFit.RowOf(widths, RowWidth, Spacing));
    }

    [Test]
    public void ExactlyFullRow_DoesNotWrapSpuriously()
    {
        var widths = new[] { 478f, 478f };        // 478+24+478 = 980 == RowWidth
        Assert.AreEqual(new[] { 0, 0 }, PromptSuggestionCloudFit.RowOf(widths, RowWidth, Spacing));
    }

    [Test]
    public void OverflowPastMaxRows_IsTruncatedAtTheBoundary()
    {
        // 500-wide chips: two per row (500+24+500 = 1024 > 980 -> one per row).
        var widths = new[] { 500f, 500f, 500f, 500f, 500f };
        Assert.AreEqual(new[] { 0, 1, 2, 3, 4 }, PromptSuggestionCloudFit.RowOf(widths, RowWidth, Spacing));
        Assert.AreEqual(3, PromptSuggestionCloudFit.Take(widths, RowWidth, Spacing, 3));
    }

    [Test]
    public void ChipWiderThanTheRow_StillGetsItsOwnRow()
    {
        var widths = new[] { 1200f, 200f };
        Assert.AreEqual(new[] { 0, 1 }, PromptSuggestionCloudFit.RowOf(widths, RowWidth, Spacing));
        Assert.AreEqual(2, PromptSuggestionCloudFit.Take(widths, RowWidth, Spacing, 3));
    }
}

using NUnit.Framework;

/// <summary>
/// Covers AutoChipLook — the pure rule behind every «Авто» capsule: from two facts (is «Авто» ON in this place, and which
/// learning percent does the bot show — null = no data) it names the colour roles of the capsule and how much of it the
/// learning fill covers. The matrix is spec 2026-10-22-learning-progress-percent §4.3.
///
/// The "no data" rows are what ReplyModeToggleBinder.PaintChip drew before the percent existed, role for role: a bot
/// without learning data must not look any different, so those two tests are the "nothing else changes" guard.
///
/// ON at 100 % paints the capsule with LearnFillOn, not PositiveBg: in the dark theme the two are the same colour, so it is
/// exactly today's look there; in the light theme it is the slightly stronger tint the spec allows (§4.3), and it avoids a
/// visible jump from 99 % — the last learning frame, filled with LearnFillOn — to the trained capsule.
/// </summary>
public class AutoChipLookTests
{
    private const float FractionTolerance = 1e-6f;

    // ---------------------------------------------------------------- OFF

    [Test]
    public void Off_NoData_IsTodaysLook()
    {
        AutoChipPaint paint = AutoChipLook.For(false, null);

        AssertOffFace(paint);
        Assert.AreEqual(0f, paint.FillFraction, "No data, no fill");
        Assert.AreEqual(paint.Base, paint.Fill,
            "With nothing to draw the fill role is still a real one — the face's own colour, so painting it anywhere changes nothing");
    }

    [Test]
    public void Off_Learning_FillsBlue()
    {
        AutoChipPaint paint = AutoChipLook.For(false, 45);

        AssertOffFace(paint);
        Assert.AreEqual(ThemeRole.LearnFillOff, paint.Fill);
        Assert.AreEqual(0.45f, paint.FillFraction, FractionTolerance);
    }

    [Test]
    public void Off_Trained_IsAllBlue()
    {
        AutoChipPaint paint = AutoChipLook.For(false, 100);

        AssertOffFace(paint);
        Assert.AreEqual(ThemeRole.LearnFillOff, paint.Fill);
        Assert.AreEqual(1f, paint.FillFraction, "Trained: the blue covers the whole capsule inside its ring");
    }

    // ----------------------------------------------------------------- ON

    [Test]
    public void On_NoData_IsTodaysLook()
    {
        AutoChipPaint paint = AutoChipLook.For(true, null);

        Assert.AreEqual(ThemeRole.PositiveBg, paint.Ring);
        Assert.AreEqual(ThemeRole.PositiveBg, paint.Base);
        AssertOnInk(paint);
        Assert.AreEqual(0f, paint.FillFraction, "No data, no fill");
        Assert.AreEqual(paint.Base, paint.Fill,
            "With nothing to draw the fill role is still a real one — the face's own colour, so painting it anywhere changes nothing");
    }

    [Test]
    public void On_Learning_IsDarkGreenOverLighterGreen()
    {
        AutoChipPaint paint = AutoChipLook.For(true, 30);

        Assert.AreEqual(ThemeRole.LearnTrackOn, paint.Ring);
        Assert.AreEqual(ThemeRole.LearnTrackOn, paint.Base, "The unfilled part is the lighter green");
        Assert.AreEqual(ThemeRole.LearnFillOn, paint.Fill, "The growing part is the dark green");
        Assert.AreEqual(0.30f, paint.FillFraction, FractionTolerance);
        AssertOnInk(paint);
    }

    [Test]
    public void On_Trained_IsTheFullFillColour()
    {
        AutoChipPaint paint = AutoChipLook.For(true, 100);

        Assert.AreEqual(ThemeRole.LearnFillOn, paint.Ring);
        Assert.AreEqual(ThemeRole.LearnFillOn, paint.Base,
            "Trained: the capsule IS the fill colour — nothing is left to grow, so no fill layer is drawn");
        AssertOnInk(paint);
        Assert.AreEqual(0f, paint.FillFraction);
    }

    // ------------------------------------------------------ the scale's ends

    [Test]
    public void Zero_IsLearningWithAnEmptyFill_NotNoData()
    {
        AutoChipPaint on = AutoChipLook.For(true, 0);
        Assert.AreEqual(ThemeRole.LearnTrackOn, on.Ring, "0 % is a bot that has started learning: all track, none of today's PositiveBg");
        Assert.AreEqual(ThemeRole.LearnTrackOn, on.Base);
        Assert.AreEqual(ThemeRole.LearnFillOn, on.Fill);
        Assert.AreEqual(0f, on.FillFraction);
        AssertOnInk(on);

        AutoChipPaint off = AutoChipLook.For(false, 0);
        AssertOffFace(off);
        Assert.AreEqual(ThemeRole.LearnFillOff, off.Fill);
        Assert.AreEqual(0f, off.FillFraction);
    }

    [Test]
    public void NinetyNine_IsStillLearning()
    {
        AutoChipPaint on = AutoChipLook.For(true, 99);
        Assert.AreEqual(ThemeRole.LearnTrackOn, on.Base, "Only 100 is trained");
        Assert.AreEqual(ThemeRole.LearnFillOn, on.Fill);
        Assert.AreEqual(0.99f, on.FillFraction, FractionTolerance);

        Assert.AreEqual(0.99f, AutoChipLook.For(false, 99).FillFraction, FractionTolerance);
        Assert.AreEqual("Учится · 99\u00A0%", AutoChipLook.Caption(99));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PercentAboveTheScale_IsTrained(bool on)
    {
        AssertSamePaint(AutoChipLook.For(on, 100), AutoChipLook.For(on, 150), "150 is trained, not an overflowing fill");
        AssertSamePaint(AutoChipLook.For(on, 100), AutoChipLook.For(on, int.MaxValue), "int.MaxValue is trained too");
        Assert.IsNull(AutoChipLook.Caption(150), "A trained capsule carries no caption, however far past the scale");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PercentBelowTheScale_IsZero(bool on)
    {
        AssertSamePaint(AutoChipLook.For(on, 0), AutoChipLook.For(on, -5), "-5 is 0 %, not a negative width");
        AssertSamePaint(AutoChipLook.For(on, 0), AutoChipLook.For(on, int.MinValue), "int.MinValue is 0 % too");
        Assert.AreEqual(AutoChipLook.Caption(0), AutoChipLook.Caption(-5), "The caption reads the same clamped number the fill does");
    }

    // ------------------------------------------------------------ caption

    [Test]
    public void Caption_HidesAtNoDataAndAtHundred()
    {
        Assert.IsNull(AutoChipLook.Caption(null));
        Assert.IsNull(AutoChipLook.Caption(100));
        Assert.AreEqual("Учится · 0\u00A0%", AutoChipLook.Caption(0));
        Assert.AreEqual("Учится · 45\u00A0%", AutoChipLook.Caption(45));
    }

    // ------------------------------------------------------------ helpers

    // The OFF capsule keeps today's face at every percent — only the fill is added on top.
    private static void AssertOffFace(AutoChipPaint paint)
    {
        Assert.AreEqual(ThemeRole.Border, paint.Ring);
        Assert.AreEqual(ThemeRole.Surface, paint.Base);
        Assert.AreEqual(ThemeRole.InkSecondary, paint.Ink);
        Assert.AreEqual(ThemeRole.InkTertiary, paint.Lamp);
        Assert.IsFalse(paint.LampSolid, "OFF keeps the hollow lamp");
    }

    // The ON capsule's label and lamp stay PositiveInk at every percent, with the lamp filled.
    private static void AssertOnInk(AutoChipPaint paint)
    {
        Assert.AreEqual(ThemeRole.PositiveInk, paint.Ink);
        Assert.AreEqual(ThemeRole.PositiveInk, paint.Lamp);
        Assert.IsTrue(paint.LampSolid, "ON keeps the filled lamp");
    }

    private static void AssertSamePaint(AutoChipPaint expected, AutoChipPaint actual, string why)
    {
        Assert.AreEqual(expected.Ring, actual.Ring, why);
        Assert.AreEqual(expected.Base, actual.Base, why);
        Assert.AreEqual(expected.Ink, actual.Ink, why);
        Assert.AreEqual(expected.Lamp, actual.Lamp, why);
        Assert.AreEqual(expected.LampSolid, actual.LampSolid, why);
        Assert.AreEqual(expected.Fill, actual.Fill, why);
        Assert.AreEqual(expected.FillFraction, actual.FillFraction, why);
    }
}

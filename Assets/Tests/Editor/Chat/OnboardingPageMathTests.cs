using NUnit.Framework;

// Covers OnboardingPageMath — pure nearest-page + page-to-normalized-X arithmetic
// for the 3-page onboarding carousel (mirrors ServerPageMath / ScrollTargetMath).
public class OnboardingPageMathTests
{
    [Test]
    public void NearestPage_Start_ReturnsFirst()
        => Assert.AreEqual(0, OnboardingPageMath.NearestPage(0f, 3));

    [Test]
    public void NearestPage_Middle_ReturnsSecond()
        => Assert.AreEqual(1, OnboardingPageMath.NearestPage(0.5f, 3));

    [Test]
    public void NearestPage_End_ReturnsThird()
        => Assert.AreEqual(2, OnboardingPageMath.NearestPage(1f, 3));

    [Test]
    public void NearestPage_JustBelowMid_RoundsDownToFirst()
        => Assert.AreEqual(0, OnboardingPageMath.NearestPage(0.24f, 3));

    [Test]
    public void NearestPage_JustAboveQuarter_RoundsToSecond()
        => Assert.AreEqual(1, OnboardingPageMath.NearestPage(0.26f, 3));

    [Test]
    public void NearestPage_SinglePage_AlwaysZero()
        => Assert.AreEqual(0, OnboardingPageMath.NearestPage(0.9f, 1));

    [Test]
    public void NearestPage_ZeroPages_GuardsToZero()
        => Assert.AreEqual(0, OnboardingPageMath.NearestPage(0.5f, 0));

    [Test]
    public void PageToNormalizedX_First_IsZero()
        => Assert.AreEqual(0f, OnboardingPageMath.PageToNormalizedX(0, 3), 0.001f);

    [Test]
    public void PageToNormalizedX_Middle_IsHalf()
        => Assert.AreEqual(0.5f, OnboardingPageMath.PageToNormalizedX(1, 3), 0.001f);

    [Test]
    public void PageToNormalizedX_Last_IsOne()
        => Assert.AreEqual(1f, OnboardingPageMath.PageToNormalizedX(2, 3), 0.001f);

    [Test]
    public void PageToNormalizedX_OutOfRange_ClampsToOne()
        => Assert.AreEqual(1f, OnboardingPageMath.PageToNormalizedX(9, 3), 0.001f);

    [Test]
    public void PageToNormalizedX_SinglePage_IsZero()
        => Assert.AreEqual(0f, OnboardingPageMath.PageToNormalizedX(0, 1), 0.001f);
}

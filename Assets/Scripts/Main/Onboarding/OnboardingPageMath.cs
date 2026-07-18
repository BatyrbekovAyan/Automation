using System;

/// <summary>
/// Nearest-page + page-to-normalized-X arithmetic for the horizontal onboarding
/// carousel (mirrors ServerPageMath's pure, XML-doc'd, Math.Clamp discipline).
/// The carousel ScrollRect reports/consumes a horizontal normalized position in
/// [0,1]; this class converts between that and a 0-based page index. Pure logic,
/// unit-tested in OnboardingPageMathTests.
/// </summary>
public static class OnboardingPageMath
{
    /// <summary>Nearest 0-based page index for a horizontal normalized scroll X in [0,1].</summary>
    public static int NearestPage(float normalizedX, int pageCount)
    {
        if (pageCount <= 1) return 0;
        int idx = (int)Math.Round(Math.Clamp(normalizedX, 0f, 1f) * (pageCount - 1));
        return Math.Clamp(idx, 0, pageCount - 1);
    }

    /// <summary>Normalized X target that lands page <paramref name="index"/> under the viewport.</summary>
    public static float PageToNormalizedX(int index, int pageCount) =>
        pageCount <= 1 ? 0f : Math.Clamp(index, 0, pageCount - 1) / (float)(pageCount - 1);
}

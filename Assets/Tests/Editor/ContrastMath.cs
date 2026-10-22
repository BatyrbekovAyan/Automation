using UnityEngine;

/// <summary>
/// WCAG 2.x relative luminance and contrast ratio, shared by every palette test that
/// pins a colour pair to a contrast floor (the nav bar's tab ink, the «Авто» capsule's
/// learning fill). One home so all of them measure with the same arithmetic.
///
/// <see cref="Contrast"/> is symmetric — (lighter + 0.05) / (darker + 0.05) — so a caller
/// never has to know which of the two colours is the ink.
/// </summary>
internal static class ContrastMath
{
    // sRGB channel (0..1) -> linear light.
    private static float Channel(float v) =>
        v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

    public static float Luminance(Color c) =>
        0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);

    public static float Contrast(Color a, Color b)
    {
        float la = Luminance(a), lb = Luminance(b);
        return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
    }
}

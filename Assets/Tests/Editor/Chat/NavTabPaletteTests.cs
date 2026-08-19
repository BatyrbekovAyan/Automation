using System;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Contracts for the bottom nav bar's colours.
///
/// The bar's glyphs are white-on-transparent PNGs that the tab manager tints at
/// runtime, so these are not style preferences — an untinted or badly-chosen
/// tint makes a tab literally invisible. The bar shipped exactly that way: white
/// icons on the light theme's white Surface.
/// </summary>
public class NavTabPaletteTests
{
    private Func<string, int, int> _origGet;

    [SetUp]
    public void SetUp()
    {
        _origGet = ThemePrefs.GetInt;
        ThemePrefs.GetInt = (key, def) => def;
        Theme.ResetForTests();
    }

    [TearDown]
    public void TearDown()
    {
        Theme.ResetForTests();
        ThemePrefs.GetInt = _origGet;
    }

    // WCAG relative luminance / contrast ratio.
    private static float Channel(float v) =>
        v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

    private static float Luminance(Color c) =>
        0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);

    private static float Contrast(Color a, Color b)
    {
        float la = Luminance(a), lb = Luminance(b);
        return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
    }

    [Test]
    public void RoleFor_MapsActiveToAccent_AndInactiveToMutedInk()
    {
        Assert.AreEqual(ThemeRole.AccentText, NavTabPalette.RoleFor(true),
            "the active tab must use the one brand accent, not a per-tab colour");
        Assert.AreEqual(ThemeRole.InkTertiary, NavTabPalette.RoleFor(false));
    }

    [Test]
    public void BothStates_StayVisibleOnTheBar_InBothThemes()
    {
        // 3:1 is the WCAG floor for a graphical object such as an icon. This is
        // the regression guard for the white-on-white bar.
        foreach (var (name, asset) in new[] { ("light", Theme.Light), ("dark", Theme.Dark) })
        {
            Color bar = asset.Resolve(ThemeRole.Surface);
            foreach (bool isActive in new[] { true, false })
            {
                Color ink = asset.Resolve(NavTabPalette.RoleFor(isActive));
                float ratio = Contrast(ink, bar);
                Assert.GreaterOrEqual(ratio, 3f,
                    $"{name} theme, active={isActive}: tab ink only {ratio:F2}:1 against the bar");
            }
        }
    }

    [Test]
    public void ActiveAndInactive_AreDifferentColours_InBothThemes()
    {
        // Note the two are close in LUMINANCE on dark (~0.23 each) and separate
        // mainly by hue, so the outline -> filled sprite swap is what carries
        // selection there; the colour marks it, it does not do the work alone.
        foreach (var (name, asset) in new[] { ("light", Theme.Light), ("dark", Theme.Dark) })
            Assert.AreNotEqual(
                ColorUtility.ToHtmlStringRGB(asset.Resolve(ThemeRole.AccentText)),
                ColorUtility.ToHtmlStringRGB(asset.Resolve(ThemeRole.InkTertiary)),
                $"{name} theme: selected and unselected tabs would look identical");
    }
}

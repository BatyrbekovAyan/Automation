using System;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Contracts for the three colours behind the «Авто» capsule's learning fill
/// (spec 2026-10-22-learning-progress-percent-design.md §4.3):
///   LearnFillOff — the blue fill growing over the capsule while «Авто» is off,
///   LearnFillOn  — the dark-green fill while «Авто» is on,
///   LearnTrackOn — the lighter green that fill grows over (the unfilled part).
///
/// Two of the spec's requirements are legibility, not taste, so they are measured rather
/// than eyeballed: the label drawn over each colour keeps 4.5:1 (the WCAG text floor), and
/// the fill's leading edge stays visible against its track (1.15:1 between the two greens)
/// — without that a 45 % capsule reads as a flat pill. The DARK values are fixed by the
/// spec and compared byte for byte; the LIGHT values are the builder's to pick inside those
/// two requirements, which is why no light hex is hard-coded below.
/// </summary>
public class LearnPaletteTests
{
    private const float LabelFloor = 4.5f;
    private const float EdgeFloor = 1.15f;

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

    // 8-bit channels — the way a hex is written in the spec.
    private static string Hex(ThemeAsset asset, ThemeRole role) =>
        ColorUtility.ToHtmlStringRGB(asset.Resolve(role));

    private static (string name, ThemeAsset asset)[] BothThemes() =>
        new[] { ("light", Theme.Light), ("dark", Theme.Dark) };

    private static void AssertLabelReads(string theme, ThemeAsset asset, ThemeRole ink, ThemeRole ground)
    {
        float ratio = ContrastMath.Contrast(asset.Resolve(ink), asset.Resolve(ground));
        Assert.GreaterOrEqual(ratio, LabelFloor,
            $"{theme} theme: {ink} on {ground} is only {ratio:F2}:1 — the capsule's label would not read");
    }

    // ---------------------------------------------------------------- dark

    [Test]
    public void Dark_UsesTheSpecValues()
    {
        var dark = Theme.Dark;
        Assert.AreEqual("26365F", Hex(dark, ThemeRole.LearnFillOff));
        Assert.AreEqual("123324", Hex(dark, ThemeRole.LearnFillOn));
        Assert.AreEqual("25473D", Hex(dark, ThemeRole.LearnTrackOn));
    }

    [Test]
    public void Dark_FillOn_EqualsPositiveBg()
    {
        // At 100 % the filled ON capsule must land exactly on today's «Авто включено»
        // (PositiveBg) — the reason the dark fill is the DARKER of the two greens.
        var dark = Theme.Dark;
        Assert.AreEqual(Hex(dark, ThemeRole.PositiveBg), Hex(dark, ThemeRole.LearnFillOn));
    }

    [Test]
    public void Dark_TrackIsLighterThanTheFill()
    {
        // The owner asked for the fill «на фоне более светлого зелёного».
        var dark = Theme.Dark;
        Assert.Greater(
            ContrastMath.Luminance(dark.Resolve(ThemeRole.LearnTrackOn)),
            ContrastMath.Luminance(dark.Resolve(ThemeRole.LearnFillOn)));
    }

    // ---------------------------------------------------------------- both themes

    [Test]
    public void Labels_Read_OnEveryLearnColour_InBothThemes()
    {
        foreach (var (name, asset) in BothThemes())
        {
            AssertLabelReads(name, asset, ThemeRole.InkSecondary, ThemeRole.LearnFillOff);
            AssertLabelReads(name, asset, ThemeRole.PositiveInk, ThemeRole.LearnFillOn);
            AssertLabelReads(name, asset, ThemeRole.PositiveInk, ThemeRole.LearnTrackOn);
        }
    }

    [Test]
    public void TheFillEdge_IsVisible_InBothThemes()
    {
        foreach (var (name, asset) in BothThemes())
        {
            float ratio = ContrastMath.Contrast(
                asset.Resolve(ThemeRole.LearnFillOn), asset.Resolve(ThemeRole.LearnTrackOn));
            Assert.GreaterOrEqual(ratio, EdgeFloor,
                $"{name} theme: the green fill and its track are only {ratio:F3}:1 apart — the fill's edge disappears");
        }
    }

    // ---------------------------------------------------------------- contracts

    [Test]
    public void Roles_AreAppendedAfterScrim_InTheSpecOrder()
    {
        // ThemedColor stores the enum ORDINAL in the scene, so the three roles may only
        // ever sit at the END, in this order — an insertion would silently repoint every
        // binding after it.
        Assert.AreEqual((int)ThemeRole.Scrim + 1, (int)ThemeRole.LearnFillOff);
        Assert.AreEqual((int)ThemeRole.LearnFillOff + 1, (int)ThemeRole.LearnFillOn);
        Assert.AreEqual((int)ThemeRole.LearnFillOn + 1, (int)ThemeRole.LearnTrackOn);
    }

    [Test]
    public void Light_AssetEqualsTheCodeDefaults()
    {
        // ThemeAssetsBuilder.SeedLight must stay byte-identical to ThemeAsset's code
        // defaults: tests, and any checkout without the asset, run on the defaults.
        var defaults = ScriptableObject.CreateInstance<ThemeAsset>();
        try
        {
            foreach (var role in new[] { ThemeRole.LearnFillOff, ThemeRole.LearnFillOn, ThemeRole.LearnTrackOn })
                Assert.AreEqual(Hex(defaults, role), Hex(Theme.Light, role),
                    $"{role}: Theme_Light.asset has drifted from the ThemeAsset code default");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(defaults);
        }
    }
}

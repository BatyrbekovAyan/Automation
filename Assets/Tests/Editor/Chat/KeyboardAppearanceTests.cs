using System.Collections.Generic;
using NUnit.Framework;

// Covers the system-keyboard appearance seams:
//   • KeyboardAppearancePolicy — theme baseline OR always-dark screen override.
//   • SystemKeyboardAppearance — the controller that resolves them and pushes to
//     the platform, including the regression that motivated it: leaving the
//     attachment preview must fall back to the THEME, not to light.
public class KeyboardAppearanceTests
{
    private List<bool> pushes;

    [SetUp]
    public void SetUp()
    {
        SystemKeyboardAppearance.ResetForTests();
        pushes = new List<bool>();
        SystemKeyboardAppearance.ApplyToPlatform = dark => pushes.Add(dark);
    }

    [TearDown]
    public void TearDown() => SystemKeyboardAppearance.ResetForTests();

    private void UseTheme(ThemeMode mode) => SystemKeyboardAppearance.CurrentMode = () => mode;

    // --- policy ----------------------------------------------------------

    [Test]
    public void Policy_LightTheme_NoForce_IsLight()
    {
        Assert.IsFalse(KeyboardAppearancePolicy.ShouldBeDark(ThemeMode.Light, forcedDark: false));
    }

    [Test]
    public void Policy_DarkTheme_IsDark()
    {
        Assert.IsTrue(KeyboardAppearancePolicy.ShouldBeDark(ThemeMode.Dark, forcedDark: false));
    }

    [Test]
    public void Policy_ForceWinsOverLightTheme()
    {
        Assert.IsTrue(KeyboardAppearancePolicy.ShouldBeDark(ThemeMode.Light, forcedDark: true),
            "The attachment preview is dark whatever the theme");
    }

    // --- controller ------------------------------------------------------

    [Test]
    public void Refresh_DarkTheme_PushesDark()
    {
        UseTheme(ThemeMode.Dark);
        SystemKeyboardAppearance.Refresh();

        CollectionAssert.AreEqual(new[] { true }, pushes);
    }

    [Test]
    public void Refresh_LightTheme_PushesLight()
    {
        UseTheme(ThemeMode.Light);
        SystemKeyboardAppearance.Refresh();

        CollectionAssert.AreEqual(new[] { false }, pushes);
    }

    [Test]
    public void ClearingForce_InDarkTheme_StaysDark()
    {
        UseTheme(ThemeMode.Dark);
        SystemKeyboardAppearance.Refresh();          // dark
        SystemKeyboardAppearance.SetForcedDark(true);   // preview opens — already dark
        SystemKeyboardAppearance.SetForcedDark(false);  // preview closes

        Assert.IsTrue(SystemKeyboardAppearance.LastApplied,
            "Leaving an always-dark screen must fall back to the THEME, not to light");
        CollectionAssert.AreEqual(new[] { true }, pushes,
            "Nothing changed, so no redundant native call should be made");
    }

    [Test]
    public void ClearingForce_InLightTheme_ReturnsToLight()
    {
        UseTheme(ThemeMode.Light);
        SystemKeyboardAppearance.Refresh();             // light
        SystemKeyboardAppearance.SetForcedDark(true);   // preview opens → dark
        SystemKeyboardAppearance.SetForcedDark(false);  // preview closes → light again

        CollectionAssert.AreEqual(new[] { false, true, false }, pushes);
    }

    [Test]
    public void RepeatedClears_AreIdempotent()
    {
        // The preview clears the force from several exit paths (fade complete,
        // OnDisable teardown) — a flag, not a counter, so extras are harmless.
        UseTheme(ThemeMode.Light);
        SystemKeyboardAppearance.SetForcedDark(true);
        SystemKeyboardAppearance.SetForcedDark(false);
        SystemKeyboardAppearance.SetForcedDark(false);
        SystemKeyboardAppearance.SetForcedDark(false);

        CollectionAssert.AreEqual(new[] { true, false }, pushes);
    }

    [Test]
    public void ThemeFlip_WhileForced_KeepsDark_ThenFollowsTheme()
    {
        UseTheme(ThemeMode.Light);
        SystemKeyboardAppearance.SetForcedDark(true);    // dark

        UseTheme(ThemeMode.Dark);
        SystemKeyboardAppearance.Refresh();              // still dark — no extra push
        CollectionAssert.AreEqual(new[] { true }, pushes);

        SystemKeyboardAppearance.SetForcedDark(false);   // theme is dark → stays dark
        CollectionAssert.AreEqual(new[] { true }, pushes);
    }
}

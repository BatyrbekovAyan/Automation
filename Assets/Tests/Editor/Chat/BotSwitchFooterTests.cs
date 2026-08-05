using NUnit.Framework;
using UnityEngine;

public class BotSwitchFooterTests
{
    [Test]
    public void TextFor_On_IsBotRabotaet() =>
        Assert.AreEqual("Бот работает", BotSwitchFooter.TextFor(true));

    [Test]
    public void TextFor_Off_IsBotNaPauze() =>
        Assert.AreEqual("Бот на паузе", BotSwitchFooter.TextFor(false));

    // ColorFor is theme-routed — it must agree with the label's ThemedColor
    // binding rather than re-stamp a literal over it. So the contract is the
    // ROLE it resolves, under an explicitly pinned theme: reading the ambient
    // mode would make these tests depend on whatever the editor last had on.
    [TearDown]
    public void ResetTheme() => Theme.ResetForTests();

    [Test]
    public void ColorFor_On_IsSecondaryInk_InBothThemes()
    {
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            Theme.OverrideForTests(null, null, mode); // nulls = keep the real assets
            Assert.AreEqual(Theme.Color(ThemeRole.InkSecondary), BotSwitchFooter.ColorFor(true),
                            $"running ink under {mode}");
        }
    }

    [Test]
    public void ColorFor_Off_IsTertiaryInk_AndStaysMutedRelativeToOn()
    {
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            Theme.OverrideForTests(null, null, mode);
            Assert.AreEqual(Theme.Color(ThemeRole.InkTertiary), BotSwitchFooter.ColorFor(false),
                            $"off ink under {mode}");
            Assert.AreNotEqual(BotSwitchFooter.ColorFor(true), BotSwitchFooter.ColorFor(false),
                               $"paused must stay visually distinct from running under {mode}");
        }
    }

    [Test]
    public void RestOffset_NewGeometry_150Track74Handle_Is33() =>
        Assert.AreEqual(33f, BotSwitchFooter.RestOffset(150f, 74f), 0.001f);

    [Test]
    public void RestOffset_OldGeometry_100Track36Handle_Is27() =>
        Assert.AreEqual(27f, BotSwitchFooter.RestOffset(100f, 36f), 0.001f);
}

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

    [Test]
    public void ColorFor_On_IsInk3A3A3C() =>
        Assert.AreEqual((Color)new Color32(0x3A, 0x3A, 0x3C, 0xFF), BotSwitchFooter.ColorFor(true));

    [Test]
    public void ColorFor_Off_IsMuted8E8E93() =>
        Assert.AreEqual((Color)new Color32(0x8E, 0x8E, 0x93, 0xFF), BotSwitchFooter.ColorFor(false));

    [Test]
    public void RestOffset_NewGeometry_150Track74Handle_Is33() =>
        Assert.AreEqual(33f, BotSwitchFooter.RestOffset(150f, 74f), 0.001f);

    [Test]
    public void RestOffset_OldGeometry_100Track36Handle_Is27() =>
        Assert.AreEqual(27f, BotSwitchFooter.RestOffset(100f, 36f), 0.001f);
}

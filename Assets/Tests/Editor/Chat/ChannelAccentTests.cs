using NUnit.Framework;
using UnityEngine;

// Covers ChannelAccent.Resolve — the pure channel→accent-color seam behind the
// Telegram-blue recolor of the chat-row unread badge/time (ChatItemView) and the
// empty-state connect button + icon (EmptyStateView). Rule: Telegram ⇒ brand blue
// #2AABEE with the caller's authored alpha preserved; every other channel ⇒ the
// caller's authored color returned byte-identical (WhatsApp stays exactly as authored).
public class ChannelAccentTests
{
    // The two authored WhatsApp greens the app actually feeds the seam:
    //   #26B25A — ChatItemView.UnreadTimeColor (unread timestamp tint)
    //   #25D366 — the unread pill / empty-state button+icon fill (WhatsApp brand green)
    private static readonly Color UnreadTimeGreen = new Color32(0x26, 0xB2, 0x5A, 0xFF);
    private static readonly Color BrandGreen = new Color32(0x25, 0xD3, 0x66, 0xFF);

    private static Color Blue()
    {
        ColorUtility.TryParseHtmlString("#2AABEE", out Color c);
        return c;
    }

    [Test]
    public void Telegram_UnreadTimeGreen_BecomesBrandBlue()
    {
        Color result = ChannelAccent.Resolve(ChatChannel.Telegram, UnreadTimeGreen);
        Color blue = Blue();
        Assert.AreEqual(blue.r, result.r, 0.001f, "R = Telegram blue");
        Assert.AreEqual(blue.g, result.g, 0.001f, "G = Telegram blue");
        Assert.AreEqual(blue.b, result.b, 0.001f, "B = Telegram blue");
    }

    [Test]
    public void Telegram_BrandGreen_BecomesBrandBlue()
    {
        Color result = ChannelAccent.Resolve(ChatChannel.Telegram, BrandGreen);
        Color blue = Blue();
        Assert.AreEqual(blue.r, result.r, 0.001f);
        Assert.AreEqual(blue.g, result.g, 0.001f);
        Assert.AreEqual(blue.b, result.b, 0.001f);
    }

    [Test]
    public void WhatsApp_ReturnsAuthoredColor_ByteIdentical()
    {
        // The WhatsApp branch must hand back the caller's own color untouched so the
        // channel-accent seam guarantees WhatsApp renders exactly as authored.
        Color result = ChannelAccent.Resolve(ChatChannel.WhatsApp, UnreadTimeGreen);
        Assert.AreEqual(UnreadTimeGreen.r, result.r, 0f);
        Assert.AreEqual(UnreadTimeGreen.g, result.g, 0f);
        Assert.AreEqual(UnreadTimeGreen.b, result.b, 0f);
        Assert.AreEqual(UnreadTimeGreen.a, result.a, 0f);
    }

    [Test]
    public void WhatsApp_PassthroughPreservesEveryChannel_ArbitraryColor()
    {
        var authored = new Color(0.13f, 0.57f, 0.91f, 0.42f);
        Color result = ChannelAccent.Resolve(ChatChannel.WhatsApp, authored);
        Assert.AreEqual(authored.r, result.r, 0f);
        Assert.AreEqual(authored.g, result.g, 0f);
        Assert.AreEqual(authored.b, result.b, 0f);
        Assert.AreEqual(authored.a, result.a, 0f);
    }

    [Test]
    public void Telegram_PreservesAuthoredAlpha_Opaque()
    {
        var authored = new Color(0.15f, 0.70f, 0.35f, 1f);
        Color result = ChannelAccent.Resolve(ChatChannel.Telegram, authored);
        Assert.AreEqual(1f, result.a, 0.0001f, "opaque authored alpha stays opaque on Telegram");
    }

    [Test]
    public void Telegram_PreservesAuthoredAlpha_SemiTransparent()
    {
        var authored = new Color(0.15f, 0.70f, 0.35f, 0.4f);
        Color result = ChannelAccent.Resolve(ChatChannel.Telegram, authored);
        Assert.AreEqual(0.4f, result.a, 0.0001f, "semi-transparent authored alpha is preserved on Telegram");
        // ...while the hue still becomes the full brand blue (only alpha carries over).
        Color blue = Blue();
        Assert.AreEqual(blue.r, result.r, 0.001f);
        Assert.AreEqual(blue.g, result.g, 0.001f);
        Assert.AreEqual(blue.b, result.b, 0.001f);
    }

    [Test]
    public void TelegramBlue_MatchesSwitcherBrandPrecedent()
    {
        // The seam's blue must equal the switcher chip's #2AABEE so the accent recolor is
        // brand-consistent with ChannelSwitcherView.TgSelectedFill / Manager.TelegramBrandColor.
        Color blue = Blue();
        Assert.AreEqual(blue.r, ChannelAccent.TelegramBlue.r, 0.001f);
        Assert.AreEqual(blue.g, ChannelAccent.TelegramBlue.g, 0.001f);
        Assert.AreEqual(blue.b, ChannelAccent.TelegramBlue.b, 0.001f);
    }
}

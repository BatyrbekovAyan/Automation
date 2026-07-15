using UnityEngine;

/// <summary>
/// Pure channel→accent-color seam. On the Telegram channel the WhatsApp-green
/// ACCENTS (chat-row unread badge/time, empty-state connect button + icon) recolor
/// to the Telegram brand blue (#2AABEE — mirrors ChannelSwitcherView.TgSelectedFill /
/// Manager.TelegramBrandColor); on every other channel the caller's own authored
/// color passes through BYTE-IDENTICAL.
///
/// Scope is "accents only" (owner-confirmed): NOT message bubbles, NOT the
/// Авто/Вместе mode toggle, NOT the channel/bot switcher chips (already
/// brand-correct). Side-effect-free + input-only so it stays EditMode-unit-testable
/// alongside ChannelResolver / ChannelSwitcherModel, and callers cache their OWN
/// authored color and pass it in — the seam never hardcodes a scene green.
/// </summary>
public static class ChannelAccent
{
    /// <summary>
    /// Telegram brand blue. Kept identical to ChannelSwitcherView.TgSelectedFill and
    /// Manager.TelegramBrandColor so every Telegram accent in the app reads as one brand.
    /// </summary>
    public static readonly Color TelegramBlue = Hex("#2AABEE");

    /// <summary>
    /// The accent color to paint for <paramref name="channel"/>. Telegram ⇒ brand blue
    /// (carrying the caller's authored alpha, so a semi-transparent accent stays
    /// semi-transparent); every other channel ⇒ <paramref name="whatsappAuthored"/>
    /// returned unchanged — WhatsApp renders its exact authored green, alpha and all.
    /// </summary>
    public static Color Resolve(ChatChannel channel, Color whatsappAuthored)
    {
        if (channel != ChatChannel.Telegram) return whatsappAuthored;

        Color blue = TelegramBlue;
        blue.a = whatsappAuthored.a; // preserve the authored alpha; only the hue shifts
        return blue;
    }

    private static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}

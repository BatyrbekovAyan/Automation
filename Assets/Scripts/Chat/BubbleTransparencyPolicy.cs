/// <summary>
/// Pure (UnityEngine-free, EditMode-testable) policy deciding whether a chat bubble renders
/// chrome-free — transparent fill, no border, no tail — so its media floats directly on the chat
/// background, the way native Telegram shows stickers and video notes (кружки).
///
/// Two message kinds float bubble-free:
///   • a sticker — the sticker artwork IS the content, so a coloured rectangle around it reads wrong;
///   • a Telegram video note (кружок) — a circle cropped from a square bubble; native Telegram shows
///     NO bubble (the circle floats on the chat bg — 05-HUMAN-UAT "note → bubble-free").
///
/// BOTH revert to a visible bubble while a download / expired placeholder card is showing
/// (<paramref name="isPlaceholderActive"/>), so the card has a colour to contrast against — an
/// UNAVAILABLE note/sticker reads as a retry card, not a chrome-free failure state. A jumbo-emoji
/// bubble (<paramref name="hideBubble"/>) is always chrome-free regardless of media kind.
///
/// WhatsApp regression net: a plain incoming/outgoing message (isSticker=false, isVideoNote=false,
/// hideBubble=false) is NEVER transparent. <c>isVideoNote</c> is Telegram-only and defaults false,
/// so this change is byte-identical for every WhatsApp bubble.
/// </summary>
public static class BubbleTransparencyPolicy
{
    /// <summary>
    /// True when the bubble should render transparent (no fill / border / tail).
    /// </summary>
    /// <param name="isSticker">The message is a sticker (WhatsApp webp or the Telegram .tgs placeholder).</param>
    /// <param name="isVideoNote">The message is a Telegram video note (кружок) — circular, chrome-free.</param>
    /// <param name="isPlaceholderActive">A download / expired placeholder card is currently showing.</param>
    /// <param name="hideBubble">Force chrome-free (jumbo-emoji bubbles).</param>
    public static bool IsTransparent(bool isSticker, bool isVideoNote, bool isPlaceholderActive, bool hideBubble)
    {
        return ((isSticker || isVideoNote) && !isPlaceholderActive) || hideBubble;
    }
}

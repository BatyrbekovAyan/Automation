using NUnit.Framework;

/// <summary>
/// Covers <see cref="BubbleTransparencyPolicy.IsTransparent"/> — the pure decision behind a
/// chrome-free chat bubble. Grounded in 05-HUMAN-UAT ("note → bubble-free"): a Telegram video note
/// floats bubble-free like a sticker, but an UNAVAILABLE note/sticker (placeholder card showing)
/// keeps a visible bubble so the card reads. The WA-regression cases assert a plain WhatsApp
/// message is NEVER transparent (isVideoNote is Telegram-only + defaults false → byte-identical).
/// </summary>
public class BubbleTransparencyPolicyTests
{
    // --- WhatsApp regression net: a plain message is never chrome-free ---
    [Test]
    public void PlainIncoming_NotTransparent() =>
        Assert.IsFalse(BubbleTransparencyPolicy.IsTransparent(
            isSticker: false, isVideoNote: false, isPlaceholderActive: false, hideBubble: false));

    [Test]
    public void PlainOutgoing_NotTransparent() =>
        // Outgoing carries the same four inputs as incoming (colour is decided elsewhere);
        // the explicit second case documents that neither WA direction ever goes transparent.
        Assert.IsFalse(BubbleTransparencyPolicy.IsTransparent(
            isSticker: false, isVideoNote: false, isPlaceholderActive: false, hideBubble: false));

    [Test]
    public void PlainMessage_WithPlaceholderActive_StillNotTransparent() =>
        Assert.IsFalse(BubbleTransparencyPolicy.IsTransparent(
            isSticker: false, isVideoNote: false, isPlaceholderActive: true, hideBubble: false));

    // --- Sticker: chrome-free unless its download/expired card is showing ---
    [Test]
    public void Sticker_NoPlaceholder_Transparent() =>
        Assert.IsTrue(BubbleTransparencyPolicy.IsTransparent(
            isSticker: true, isVideoNote: false, isPlaceholderActive: false, hideBubble: false));

    [Test]
    public void Sticker_WithPlaceholderActive_NotTransparent() =>
        Assert.IsFalse(BubbleTransparencyPolicy.IsTransparent(
            isSticker: true, isVideoNote: false, isPlaceholderActive: true, hideBubble: false));

    // --- Video note (кружок): the 05-08 fix — bubble-free like a sticker ---
    [Test]
    public void VideoNote_NoPlaceholder_Transparent() =>
        Assert.IsTrue(BubbleTransparencyPolicy.IsTransparent(
            isSticker: false, isVideoNote: true, isPlaceholderActive: false, hideBubble: false));

    [Test]
    public void VideoNote_WithPlaceholderActive_NotTransparent() =>
        // An UNAVAILABLE note shows its download/retry card against a visible bubble (mirrors stickers).
        Assert.IsFalse(BubbleTransparencyPolicy.IsTransparent(
            isSticker: false, isVideoNote: true, isPlaceholderActive: true, hideBubble: false));

    // --- Jumbo emoji: hideBubble forces chrome-free regardless of everything else ---
    [Test]
    public void HideBubble_ForcesTransparent_EvenPlain() =>
        Assert.IsTrue(BubbleTransparencyPolicy.IsTransparent(
            isSticker: false, isVideoNote: false, isPlaceholderActive: false, hideBubble: true));

    [Test]
    public void HideBubble_ForcesTransparent_EvenWithPlaceholderActive() =>
        // hideBubble is OR'd last, so it wins even when a placeholder would otherwise force a bubble.
        Assert.IsTrue(BubbleTransparencyPolicy.IsTransparent(
            isSticker: true, isVideoNote: false, isPlaceholderActive: true, hideBubble: true));

    // --- Defensive: both media flags set (should never co-occur) still resolves transparent ---
    [Test]
    public void StickerAndVideoNote_NoPlaceholder_Transparent() =>
        Assert.IsTrue(BubbleTransparencyPolicy.IsTransparent(
            isSticker: true, isVideoNote: true, isPlaceholderActive: false, hideBubble: false));
}

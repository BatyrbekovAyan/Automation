using NUnit.Framework;

/// <summary>
/// Covers <see cref="TelegramMediaType.Refine"/> — the Telegram-only media-kind refinement
/// that resolves the real media type from the base <c>type</c> string ⊕ the flat
/// <c>mimetype</c>. Grounded in the 2026-07-13 tapi capture (SHAPES.md Q1/Q2): a phone-sent
/// video arrives as <c>type:"document"</c> + <c>mimetype:"video/mp4"</c>. The audio/* rule
/// is a DEFENSIVE net (TG voice was unobserved) and must not fire for text/reaction rows.
/// WhatsApp never runs this seam (its mime lives in the body JObject).
/// </summary>
public class TelegramMessageTypeTests
{
    // --- Observed: a phone-sent video arrives typed as a document with a video mime ---
    [Test]
    public void Refine_DocumentWithVideoMime_BecomesVideo() =>
        Assert.AreEqual(MessageType.Video, TelegramMediaType.Refine(MessageType.Document, "video/mp4"));

    // --- A real document keeps its type when the mime is not media/audio/video ---
    [TestCase("application/pdf")]
    [TestCase("application/vnd.ms-excel")]
    [TestCase("text/csv")]
    public void Refine_DocumentWithNonMediaMime_StaysDocument(string mime) =>
        Assert.AreEqual(MessageType.Document, TelegramMediaType.Refine(MessageType.Document, mime));

    // --- Image keeps its type: image/* is neither video/ nor audio/ ---
    [Test]
    public void Refine_ImageWithImageMime_StaysImage() =>
        Assert.AreEqual(MessageType.Image, TelegramMediaType.Refine(MessageType.Image, "image/jpeg"));

    // --- Defensive (unobserved): audio/* refines to Voice ---
    [TestCase("audio/ogg")]
    [TestCase("audio/mpeg")]
    public void Refine_AudioMime_BecomesVoice(string mime) =>
        Assert.AreEqual(MessageType.Voice, TelegramMediaType.Refine(MessageType.Document, mime));

    // --- Defensive: any base media type with a video mime becomes Video ---
    [Test]
    public void Refine_UnknownWithVideoMime_BecomesVideo() =>
        Assert.AreEqual(MessageType.Video, TelegramMediaType.Refine(MessageType.Unknown, "video/quicktime"));

    // --- Text is never reclassified, even if a stray mimetype is present ---
    [TestCase("video/mp4")]
    [TestCase("audio/ogg")]
    [TestCase("")]
    [TestCase(null)]
    public void Refine_ChatType_NeverReclassified(string mime) =>
        Assert.AreEqual(MessageType.Chat, TelegramMediaType.Refine(MessageType.Chat, mime));

    // --- Reaction is never reclassified ---
    [Test]
    public void Refine_ReactionType_NeverReclassified() =>
        Assert.AreEqual(MessageType.Reaction, TelegramMediaType.Refine(MessageType.Reaction, "video/mp4"));

    // --- Empty / missing mime leaves the base type untouched (null-tolerant) ---
    [TestCase("")]
    [TestCase(null)]
    public void Refine_EmptyMime_KeepsBaseType(string mime) =>
        Assert.AreEqual(MessageType.Document, TelegramMediaType.Refine(MessageType.Document, mime));

    // --- Observed (05-HUMAN-UAT gap 1): an animated .tgs sticker arrives type:"document" +
    //     mimetype:"application/x-tgsticker" and must refine to Sticker (placeholder path) ---
    [Test]
    public void Refine_DocumentWithTgsStickerMime_BecomesSticker() =>
        Assert.AreEqual(MessageType.Sticker,
            TelegramMediaType.Refine(MessageType.Document, TelegramMediaType.TgsStickerMime));

    [Test]
    public void Refine_TgsStickerMime_IsTheLiteralTapiValue() =>
        Assert.AreEqual("application/x-tgsticker", TelegramMediaType.TgsStickerMime);

    // --- A static webp sticker (type:"sticker" + image/webp) is NOT mis-refined — image/* is
    //     neither the .tgs mime nor video/audio, so it stays Sticker ---
    [Test]
    public void Refine_StickerWithWebpMime_StaysSticker() =>
        Assert.AreEqual(MessageType.Sticker, TelegramMediaType.Refine(MessageType.Sticker, "image/webp"));

    // --- A GIF arrives sticker-typed with a video mime (SHAPES.md Q2) → routed to the video pipeline ---
    [Test]
    public void Refine_StickerWithVideoMime_BecomesVideo() =>
        Assert.AreEqual(MessageType.Video, TelegramMediaType.Refine(MessageType.Sticker, "video/mp4"));

    // --- Text is never reclassified even by the .tgs mime rule ---
    [Test]
    public void Refine_ChatType_TgsMime_NeverReclassified() =>
        Assert.AreEqual(MessageType.Chat,
            TelegramMediaType.Refine(MessageType.Chat, TelegramMediaType.TgsStickerMime));
}

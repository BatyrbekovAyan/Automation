/// <summary>
/// Refines a Telegram (tapi) message's base <see cref="MessageType"/> using its flat
/// <c>mimetype</c>. tapi delivers a phone-sent video as <c>type:"document"</c> with
/// <c>mimetype:"video/mp4"</c> (observed in the 2026-07-13 capture — SHAPES.md Q1/Q2),
/// so the real media kind is <c>type</c> refined by <c>mimetype</c>.
///
/// Pure/static so it is unit-testable without a live server. Channel-scoped: call ONLY
/// for <see cref="ChatChannel.Telegram"/> so WhatsApp document handling stays byte-identical
/// (WhatsApp carries its mime inside the <c>body</c> JObject, not a flat field).
///
/// DEFENSIVE (unobserved in the capture): <c>audio/*</c> refines to Voice. Sticker /
/// voice(ptt) / video-note / GIF were 0 in 384 captured messages, so this prefix rule
/// ships as a safety net — the owner's media re-run (send a sticker/voice/video-note/GIF,
/// re-run capture) must confirm the real shapes before device UAT.
///
/// OBSERVED (2026-07-14 device UAT — SHAPES.md Q2 / 05-HUMAN-UAT gap 1): an animated
/// <c>.tgs</c> sticker arrives as <c>type:"document"</c> + <c>mimetype:"application/x-tgsticker"</c>.
/// It is a gzipped Lottie animation (undecodable in Unity), so it refines to Sticker here —
/// MessageItemView then renders the deliberate borderless placeholder rather than a document card.
/// </summary>
public static class TelegramMediaType
{
    // application/x-tgsticker = Telegram's animated (Lottie) sticker mime. The refined Sticker
    // type routes it through IsMediaMessageType so ApplyTelegramMediaShape stamps this mime onto
    // the NormalizedMessage, letting MessageItemView key the undecodable-.tgs placeholder off it.
    public const string TgsStickerMime = "application/x-tgsticker";

    public static MessageType Refine(MessageType baseType, string mimetype)
    {
        // Never reclassify a text or reaction row: only media-bearing rows carry a mimetype,
        // and a stray value must not flip plain text into a media bubble.
        if (baseType == MessageType.Chat || baseType == MessageType.Reaction) return baseType;
        if (string.IsNullOrEmpty(mimetype)) return baseType;

        // Animated .tgs sticker arrives type:"document" — refine to Sticker (placeholder path).
        if (mimetype == TgsStickerMime) return MessageType.Sticker;

        if (mimetype.StartsWith("video/")) return MessageType.Video;   // observed: video sent as document
        if (mimetype.StartsWith("audio/")) return MessageType.Voice;   // defensive: TG voice unobserved
        return baseType;
    }
}

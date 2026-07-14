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
/// </summary>
public static class TelegramMediaType
{
    public static MessageType Refine(MessageType baseType, string mimetype)
    {
        // Never reclassify a text or reaction row: only media-bearing rows carry a mimetype,
        // and a stray value must not flip plain text into a media bubble.
        if (baseType == MessageType.Chat || baseType == MessageType.Reaction) return baseType;
        if (string.IsNullOrEmpty(mimetype)) return baseType;

        if (mimetype.StartsWith("video/")) return MessageType.Video;   // observed: video sent as document
        if (mimetype.StartsWith("audio/")) return MessageType.Voice;   // defensive: TG voice unobserved
        return baseType;
    }
}

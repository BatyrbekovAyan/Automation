/// <summary>
/// Pure, unit-testable message-type switch shared by ChatManager.ParseMessageType.
/// Maps the Wappi/tapi <c>type</c> string to the <see cref="MessageType"/> enum.
/// Watch the non-obvious ones: voice notes arrive as <c>"ptt"</c> → Voice, and
/// Telegram (tapi) plain text arrives as <c>"text"</c> → Chat (WhatsApp never
/// sends "text"). Anything unrecognized → Unknown (never throws).
/// Extracted per the WhatsAppSyncGate/CrossChatResponseGuard pure-seam precedent.
/// </summary>
public static class MessageTypeParser
{
    public static MessageType From(string type) => type switch
    {
        "chat"     => MessageType.Chat,
        "text"     => MessageType.Chat,   // Telegram text (divergence); WA never sends "text"
        "image"    => MessageType.Image,
        "video"    => MessageType.Video,
        "audio"    => MessageType.Audio,
        "ptt"      => MessageType.Voice,
        "sticker"  => MessageType.Sticker,
        "document" => MessageType.Document,
        "reaction" => MessageType.Reaction,
        _          => MessageType.Unknown
    };
}

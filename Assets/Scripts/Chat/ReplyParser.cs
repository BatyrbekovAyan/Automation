using System;
using Newtonsoft.Json.Linq;

/// <summary>
/// Resolves the quoted-message preview for a reply bubble. Pure/static so it is
/// unit-testable and callable from ChatManager.Normalize without a MonoBehaviour.
/// Resolution order: cache-by-id (freshest, tappable) -> API-embedded reply_message
/// snapshot -> id-only placeholder. Never throws.
/// </summary>
public static class ReplyParser
{
    public static QuotedPreview Resolve(
        RawMessage raw,
        Func<string, MessageViewModel> resolveById,
        Func<string, MessageType> parseType)
    {
        if (raw == null || raw.type == "reaction") return QuotedPreview.None;

        bool isReply = raw.isReply || raw.replyMessage is JObject;
        if (!isReply) return QuotedPreview.None;

        string quotedId = ExtractQuotedId(raw);
        if (string.IsNullOrEmpty(quotedId)) return QuotedPreview.None;

        MessageViewModel cached = resolveById?.Invoke(quotedId);
        if (cached != null) return FromCached(quotedId, cached);

        if (raw.replyMessage is JObject snap) return FromSnapshot(quotedId, snap, parseType);

        return new QuotedPreview { messageId = quotedId, type = MessageType.Unknown };
    }

    private static string ExtractQuotedId(RawMessage raw)
    {
        if (raw.replyMessage is JObject obj)
        {
            string id = obj["id"]?.ToString();
            if (!string.IsNullOrEmpty(id)) return id;
        }
        return raw.stanzaId;
    }

    private static QuotedPreview FromCached(string id, MessageViewModel vm) => new QuotedPreview
    {
        messageId    = id,
        senderName   = SenderLabel(vm.isIncoming, vm.senderName),
        text         = SnippetFor(vm.type, vm.text),
        type         = vm.type,
        thumbnailUrl = string.IsNullOrEmpty(vm.thumbnailUrl) ? vm.mediaUrl : vm.thumbnailUrl
    };

    private static QuotedPreview FromSnapshot(string id, JObject snap, Func<string, MessageType> parseType)
    {
        string typeStr = snap["type"]?.ToString() ?? "chat";
        MessageType t  = parseType != null ? parseType(typeStr) : MessageType.Unknown;
        string caption = snap["caption"]?.ToString();
        string body    = snap["body"]?.ToString();
        bool fromMe    = snap["fromMe"]?.ToObject<bool>() ?? false;
        string sender  = snap["senderName"]?.ToString();
        return new QuotedPreview
        {
            messageId    = id,
            senderName   = SenderLabel(!fromMe, sender),
            text         = SnippetFor(t, string.IsNullOrEmpty(caption) ? body : caption),
            type         = t,
            thumbnailUrl = null
        };
    }

    /// "You" for own messages, else the real sender name (never null).
    public static string SenderLabel(bool isIncoming, string senderName)
        => isIncoming ? (senderName ?? string.Empty) : "You";

    /// Caption/body when present, else a human label for the media type.
    public static string SnippetFor(MessageType type, string text)
    {
        if (!string.IsNullOrEmpty(text)) return text;
        switch (type)
        {
            case MessageType.Image:    return "Photo";
            case MessageType.Video:    return "Video";
            case MessageType.Voice:    return "Voice message";
            case MessageType.Audio:    return "Audio";
            case MessageType.Sticker:  return "Sticker";
            case MessageType.Document: return "Document";
            default:                   return string.Empty;
        }
    }

    /// Strips the leading zero-width space + whitespace UnicodeEmojiConverter prepends,
    /// so an emoji-only snippet doesn't render as visually empty.
    public static string CleanSnippet(string s)
        => string.IsNullOrEmpty(s) ? s : s.TrimStart('​', ' ', '\t', '\n', '\r');
}

using System;
using System.Collections.Generic;
using System.Text;
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

        if (raw.replyMessage is JObject snap) return FromSnapshot(quotedId, snap, parseType, raw.body?.ToString());

        return new QuotedPreview { messageId = quotedId, type = MessageType.Unknown };
    }

    /// <summary>
    /// First-chat-open back-fill. On a cold open the per-message Normalize pass resolves each
    /// reply's quote before <c>_activeChatCache</c> exists, so <c>resolveById</c> misses; and
    /// almost all WhatsApp replies arrive with NO embedded <c>reply_message</c> snapshot, so the
    /// quote lands on the id-only placeholder (empty text) — that is the common case, not an edge.
    /// Once the whole batch is loaded, this fills every still-unresolved reply's quoted preview
    /// from its now-present target. Pure, allocation-light, and idempotent: it only upgrades replies
    /// whose quote is still a placeholder (Unknown type or empty text) and whose target is in the
    /// batch, so a snapshot-resolved snippet is left untouched. Returns true if any reply changed.
    /// </summary>
    public static bool BackfillFromCache(IReadOnlyList<MessageViewModel> messages)
    {
        if (messages == null || messages.Count == 0) return false;

        // id -> message index for the whole batch. Wappi batches are newest-first, so a reply's
        // (older) target sits AFTER it; indexing the full batch first makes order irrelevant.
        var byId = new Dictionary<string, MessageViewModel>(messages.Count);
        for (int i = 0; i < messages.Count; i++)
        {
            var m = messages[i];
            if (m != null && !string.IsNullOrEmpty(m.messageId)) byId[m.messageId] = m;
        }

        bool changed = false;
        for (int i = 0; i < messages.Count; i++)
        {
            var reply = messages[i];
            if (reply == null || string.IsNullOrEmpty(reply.quotedMessageId)) continue;

            bool unresolved = reply.quotedType == MessageType.Unknown || string.IsNullOrEmpty(reply.quotedText);
            if (!unresolved) continue;

            if (!byId.TryGetValue(reply.quotedMessageId, out var target) || target == null) continue;
            if (ReferenceEquals(target, reply)) continue;

            QuotedPreview q = FromCached(reply.quotedMessageId, target);
            reply.quotedSenderName   = q.senderName;
            reply.quotedText         = q.text;
            reply.quotedType         = q.type;
            reply.quotedThumbnailUrl = q.thumbnailUrl;
            changed = true;
        }
        return changed;
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

    private static QuotedPreview FromSnapshot(string id, JObject snap, Func<string, MessageType> parseType, string ownBody)
    {
        string typeStr = snap["type"]?.ToString() ?? "chat";
        MessageType t  = parseType != null ? parseType(typeStr) : MessageType.Unknown;
        string caption = snap["caption"]?.ToString();
        string body    = snap["body"]?.ToString();
        bool fromMe    = snap["fromMe"]?.ToObject<bool>() ?? false;
        // Wappi's reply_message carries the quoted sender in contact_name; a message fetched via
        // messages/id/get carries it in senderName instead. Prefer whichever is non-empty.
        string contact = snap["contact_name"]?.ToString();
        string sender  = !string.IsNullOrEmpty(contact) ? contact : snap["senderName"]?.ToString();

        // Wappi sometimes echoes the replying message's OWN body into the snapshot instead of the
        // real quoted original (which is then unavailable). Detecting it HERE — against the raw own
        // body, only on the snapshot path — is precise: a cache-resolved reply whose text genuinely
        // matches its target is left alone. Drop the bogus text; the caller renders an "unavailable"
        // placeholder while keeping the reply marker (non-empty messageId).
        string snippetSource = string.IsNullOrEmpty(caption) ? body : caption;
        bool echoesOwnBody = QuoteEchoesBody(ownBody, snippetSource);

        return new QuotedPreview
        {
            messageId    = id,
            senderName   = SenderLabel(!fromMe, sender),
            text         = echoesOwnBody ? string.Empty : SnippetFor(t, snippetSource),
            type         = t,
            thumbnailUrl = null
        };
    }

    /// <summary>
    /// Builds a quoted preview from a full message fetched via <c>messages/id/get</c> — used to
    /// recover the real quoted text when Wappi's <c>reply_message</c> snapshot was missing or just
    /// echoed the replying message's own body. The fetched message has the same shape as a
    /// snapshot but is trusted verbatim (no echo check — it IS the real target). Returns
    /// <see cref="QuotedPreview.None"/> when the payload has no id.
    /// </summary>
    public static QuotedPreview FromFetchedMessage(JObject message, Func<string, MessageType> parseType)
    {
        if (message == null) return QuotedPreview.None;
        string id = message["id"]?.ToString();
        if (string.IsNullOrEmpty(id)) return QuotedPreview.None;
        return FromSnapshot(id, message, parseType, ownBody: null);
    }

    /// "You" for own messages, else the real sender name (never null).
    public static string SenderLabel(bool isIncoming, string senderName)
        => isIncoming ? (senderName ?? string.Empty) : "You";

    /// Longest snippet we keep. The quoted card is a single line; this is a data-side guard so
    /// TMP never measures a multi-thousand-pixel string (the visual one-line ellipsis is enforced
    /// by the card's runtime width clamp). Roughly matches WhatsApp's quoted-preview length.
    private const int SnippetMaxLength = 160;

    /// Caption/body when present, else a human label for the media type. The text branch is
    /// sanitized to a single line: a quoted original can be multi-paragraph, and the fixed-height
    /// card must not render it as several clipped lines or as one 4000px line.
    public static string SnippetFor(MessageType type, string text)
    {
        if (!string.IsNullOrEmpty(text)) return SanitizeSnippet(text);
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

    /// Collapses every run of whitespace (newlines, tabs, zero-width spaces) into a single space
    /// and caps pathological lengths. Runs on the RAW body — before emoji→sprite conversion — so
    /// the length cap can never slice a <c>&lt;sprite&gt;</c> tag in half.
    private static string SanitizeSnippet(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool prevWs = false;
        foreach (char c in s)
        {
            bool ws = c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '​';
            if (ws)
            {
                if (!prevWs && sb.Length > 0) sb.Append(' ');
                prevWs = true;
            }
            else
            {
                sb.Append(c);
                prevWs = false;
            }
        }

        string flat = sb.ToString().TrimEnd();
        if (flat.Length > SnippetMaxLength) flat = flat.Substring(0, SnippetMaxLength).TrimEnd() + "…";
        return flat;
    }

    /// True when two texts reduce to the same snippet — used by FromSnapshot to spot the Wappi
    /// quirk where a reply_message snapshot echoes the replying message's OWN body (the real quoted
    /// target being unavailable). Both sides are run through the same whitespace/length
    /// sanitization, so a long, multi-line, or zero-width-space-prefixed body still matches the
    /// collapsed/capped snippet it was echoed into. Only the snapshot path uses this — a
    /// cache-resolved reply whose text legitimately equals its target is never affected.
    public static bool QuoteEchoesBody(string bodyText, string quotedText)
    {
        if (string.IsNullOrEmpty(quotedText) || string.IsNullOrEmpty(bodyText)) return false;
        return SanitizeSnippet(bodyText) == SanitizeSnippet(quotedText);
    }

    /// Strips the leading zero-width space + whitespace UnicodeEmojiConverter prepends,
    /// so an emoji-only snippet doesn't render as visually empty.
    public static string CleanSnippet(string s)
        => string.IsNullOrEmpty(s) ? s : s.TrimStart('​', ' ', '\t', '\n', '\r');
}

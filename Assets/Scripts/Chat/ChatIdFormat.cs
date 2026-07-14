/// <summary>
/// Single home for chat-id string logic shared across channels. Pure static —
/// no I/O — so it is unit-testable. The suffix rules are channel-agnostic: they
/// key off the WhatsApp <c>@c.us</c>/<c>@g.us</c> suffixes when present and treat
/// bare (Telegram numeric) ids verbatim.
///
/// Retires the crash-prone <c>chat.id[..^5]</c> slice (ChatManager.cs display
/// fallback): <see cref="DisplayFallback"/> never slices a numeric/short id and
/// never throws on empty (T-0501-01).
/// </summary>
public static class ChatIdFormat
{
    private const string OneToOneSuffix = "@c.us"; // 5 chars
    private const string GroupSuffix    = "@g.us"; // 5 chars

    /// <summary>
    /// The <c>recipient</c> value the Wappi API expects: strip <c>@c.us</c> ONLY
    /// when that suffix is present (1:1 chats become the bare phone number).
    /// Groups (<c>@g.us</c>) and bare Telegram numeric ids keep their id.
    /// Null/empty pass through unchanged.
    /// </summary>
    public static string Recipient(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        return id.EndsWith(OneToOneSuffix) ? id.Replace(OneToOneSuffix, "") : id;
    }

    /// <summary>
    /// Name fallback when a dialog has no name. Strip a PRESENT <c>@c.us</c>/<c>@g.us</c>
    /// suffix (5 chars); otherwise return the id VERBATIM. NEVER slice a numeric or
    /// short id, NEVER throw on empty. Null → "".
    /// Replaces the crash-prone <c>chat.id[..^5]</c>.
    /// </summary>
    public static string DisplayFallback(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        if (id.EndsWith(OneToOneSuffix) || id.EndsWith(GroupSuffix))
            return id.Substring(0, id.Length - 5);
        return id;
    }

    /// <summary>
    /// Suffix-only groupness check (used by per-bubble message-view checks that
    /// only have a chatId). True when the id carries the WhatsApp <c>@g.us</c> suffix.
    /// </summary>
    public static bool IsGroup(string chatId) =>
        !string.IsNullOrEmpty(chatId) && chatId.EndsWith(GroupSuffix);

    /// <summary>
    /// Full groupness check (used by the chat list, which has the dialog).
    /// WhatsApp: <c>@g.us</c> suffix OR the dialog's isGroup flag.
    /// Telegram: <c>dialogType == "chat"</c> (group) OR <c>"channel"</c> — the capture
    /// (SHAPES.md Q4) confirmed <c>"channel"</c> is a real third dialog type that must render
    /// group-style (sender headers, no per-chat suggestions). Both are trusted ONLY for
    /// suffix-less (Telegram numeric) ids, so a hypothetical WA-side <c>type:"chat"</c> field
    /// can never flip suffixed WhatsApp ids to group rendering (WR-03).
    /// </summary>
    public static bool IsGroup(string chatId, string dialogType, bool dialogIsGroup) =>
        IsGroup(chatId) || dialogIsGroup ||
        // "chat"/"channel" == Telegram group-ish; only trust for suffix-less (Telegram numeric) ids
        ((dialogType == "chat" || dialogType == "channel") && (chatId == null || chatId.IndexOf('@') < 0));
}

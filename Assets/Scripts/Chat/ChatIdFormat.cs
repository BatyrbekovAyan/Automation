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

    /// <summary>
    /// Channel-aware DEDUP KEY for a chat id (D7). Two id-forms of the SAME dialog must yield
    /// ONE key so the chat list (<c>ChatManager.ParseChatsJson</c>) spawns a single row.
    ///
    /// WhatsApp: return the id VERBATIM. A WhatsApp id (<c>&lt;phone&gt;@c.us</c> /
    /// <c>&lt;group&gt;@g.us</c>) IS its identity — the suffix and number are significant, so two
    /// distinct WhatsApp chats never collapse and every key is byte-identical to <c>chat.id</c>
    /// (the WhatsApp list is unchanged).
    ///
    /// Telegram: ids are bare numeric. A trailing <c>@c.us</c>/<c>@g.us</c> is a spurious
    /// WhatsApp-shaped artifact — tapi / the device returning the same dialog under a second id
    /// form (observed on-device for the <c>777000</c> service dialog: a bare row with the
    /// Telegram-logo avatar plus a suffixed twin with the silhouette). Strip that suffix so the
    /// twin collapses onto the bare row. Nothing else is touched (conservative: only forms proven
    /// to be the same dialog collapse). Null/empty pass through unchanged, never throw.
    ///
    /// NOTE (confirm at 08-10 device capture): the exact second id-form is not reproduced in the
    /// read-only capture (which shows only bare <c>777000</c>). If the twin turns out to be a
    /// PREFIX form (e.g. <c>user#777000</c>) rather than a spurious suffix, extend this Telegram
    /// branch — the WhatsApp branch must stay verbatim.
    /// </summary>
    public static string CanonicalKey(string id, ChatChannel channel)
    {
        if (string.IsNullOrEmpty(id)) return id;
        if (channel != ChatChannel.Telegram) return id; // WhatsApp (+ any future channel): verbatim
        if (id.EndsWith(OneToOneSuffix) || id.EndsWith(GroupSuffix))
            return id.Substring(0, id.Length - 5);
        return id;
    }

    /// <summary>
    /// True when a dialog id cannot belong to <paramref name="channel"/>'s chat list — the
    /// cross-channel BLEED defence (D7). A real WhatsApp id ALWAYS carries an <c>@</c> jid suffix
    /// (<c>@c.us</c>, <c>@g.us</c>, <c>@broadcast</c>, <c>@newsletter</c>, <c>@lid</c>, …), so a
    /// bare, suffix-less (Telegram-form numeric) id on the WhatsApp list is a leaked Telegram
    /// dialog — e.g. the <c>777000</c> service dialog persisted under the legacy WhatsApp cache
    /// root before the CHAT-11 isolation. Testing for ANY <c>@</c> (not a <c>@c.us</c>/<c>@g.us</c>
    /// whitelist) keeps every exotic-but-genuine WhatsApp jid, so no real WhatsApp chat is dropped.
    ///
    /// Telegram never rejects: its ids are bare numeric, and a spurious <c>@c.us</c> twin is MERGED
    /// by <see cref="CanonicalKey"/> — never dropped — so the service dialog stays on Telegram.
    /// Null/empty are never foreign (the normal path handles them).
    /// </summary>
    public static bool IsForeignToChannel(string id, ChatChannel channel)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (channel != ChatChannel.WhatsApp) return false; // Telegram: canonicalize the twin, don't drop
        return id.IndexOf('@') < 0;                          // no jid suffix ⇒ a bled Telegram-form id
    }
}

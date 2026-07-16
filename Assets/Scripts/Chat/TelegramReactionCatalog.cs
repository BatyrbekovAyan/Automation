using System.Collections.Generic;

/// <summary>
/// Telegram's standard free reaction set (D1). tapi's <c>message/reaction</c> rejects any
/// emoji outside a chat's allowed reactions with HTTP 400 REACTION_INVALID — and by default a
/// chat allows only Telegram's built-in free set. The WhatsApp quick-bar (😂 😮 …) and the full
/// ~300-emoji <see cref="ReactionEmojiCatalog"/> both offer emoji outside that set, so on the
/// Telegram channel the bar + picker source their emoji here instead. WhatsApp is untouched.
///
/// Pure/UnityEngine-free so the allowed-set logic is unit-testable without a scene.
///
/// STARTING POINT: this is Telegram's documented standard free reaction set; it must be
/// re-confirmed against a live capture at 08-10 (some chats enable custom reactions, which
/// this deliberately does not try to detect — the belt-and-suspenders 400 revert covers those).
/// </summary>
public static class TelegramReactionCatalog
{
    /// <summary>
    /// Telegram's standard free reaction emoji, in the SAME fully-qualified unicode form
    /// (variation selectors included) that <see cref="ReactionEmojiCatalog"/> uses, so filtered
    /// emoji render identically. Membership is queried through <see cref="IsAllowed"/>, which
    /// also matches the base (VS16-stripped) form.
    /// </summary>
    public static readonly HashSet<string> AllowedSet = new HashSet<string>
    {
        "👍", "👎", "❤️", "🔥", "🥰", "👏", "😁", "🤔", "🤯", "😱", "🤬", "😢", "🎉", "🤩",
        "🤮", "💩", "🙏", "👌", "🕊️", "🤡", "🥱", "🥴", "😍", "🐳", "❤️‍🔥", "🌚", "🌭", "💯",
        "🤣", "⚡", "🍌", "🏆", "💔", "🤨", "😐", "🍓", "🍾", "💋", "🖕", "😈", "😴", "😭",
        "🤓", "👻", "👨‍💻", "👀", "🎃", "🙈", "😇", "😨", "🤝", "✍️", "🤗", "🫡", "🎅", "🎄",
        "☃️", "💅", "🤪", "🗿", "🆒", "💘", "🙉", "🦄", "😘", "💊", "🙊", "😎", "👾",
        "🤷‍♂️", "🤷", "🤷‍♀️", "😡",
    };

    /// <summary>
    /// The Telegram-safe quick 6 for the long-press bar. Swaps the WhatsApp bar's tapi-invalid
    /// 😂→😁 and 😮→🔥; every entry is in <see cref="AllowedSet"/>.
    /// </summary>
    public static readonly string[] QuickEmojis = { "👍", "❤️", "😁", "🔥", "😢", "🙏" };

    // Base-form (trailing U+FE0F stripped) allowed keys, so "❤" and "❤️" both resolve.
    private static readonly HashSet<string> NormalizedAllowed = BuildNormalized(AllowedSet);

    /// <summary>True when tapi will accept this emoji as a reaction in a standard chat.
    /// Normalizes the trailing VS16 so the base and fully-qualified forms both match.</summary>
    public static bool IsAllowed(string emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return false;
        return NormalizedAllowed.Contains(StripVariationSelector(emoji));
    }

    /// <summary>
    /// <see cref="ReactionEmojiCatalog.Categories"/> with each category filtered to the
    /// Telegram-allowed emoji; empty categories are dropped so the picker never shows a blank
    /// section. Used to build the "+" picker on the Telegram channel.
    /// </summary>
    public static IReadOnlyList<ReactionEmojiCatalog.Category> FilterCategories()
    {
        var result = new List<ReactionEmojiCatalog.Category>();
        foreach (var category in ReactionEmojiCatalog.Categories)
        {
            var kept = new List<string>();
            foreach (var emoji in category.Emojis)
                if (IsAllowed(emoji)) kept.Add(emoji);
            if (kept.Count > 0)
                result.Add(new ReactionEmojiCatalog.Category(category.Name, kept.ToArray()));
        }
        return result;
    }

    private static HashSet<string> BuildNormalized(HashSet<string> source)
    {
        var set = new HashSet<string>();
        foreach (var emoji in source) set.Add(StripVariationSelector(emoji));
        return set;
    }

    // Telegram accepts the unqualified form, so drop a single trailing U+FE0F (emoji
    // presentation selector). ZWJ sequences and mid-string selectors are left intact.
    private static string StripVariationSelector(string emoji) =>
        emoji.Length > 0 && emoji[emoji.Length - 1] == '\uFE0F'
            ? emoji.Substring(0, emoji.Length - 1)
            : emoji;
}

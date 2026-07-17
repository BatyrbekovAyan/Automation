using System;
using System.Collections.Generic;

/// <summary>
/// Canonical emoji identity for reaction equality, dedup, and display (D2 root cause A).
/// tapi echoes a reaction in the BASE (variation-selector-free) form — the heart as U+2764
/// "❤" — while the app's quick-bar and <see cref="ReactionEmojiCatalog"/> store the
/// FULLY-QUALIFIED form U+2764 U+FE0F "❤️". Every raw-string comparison across that seam
/// misses, so an own reaction renders twice / counts «2» / a change leaves a stale pill.
///
/// Two operations, one per need:
/// <list type="bullet">
/// <item><see cref="CompareKey"/> — the VS16-insensitive equality/dedup KEY (base form).</item>
/// <item><see cref="Canonical"/> — the sprite-renderable DISPLAY form (fully qualified). The
/// TMP reaction sprite name INCLUDES the -fe0f suffix, so display must keep the qualified form
/// or the pill renders a literal text glyph (memory: emoji-sprite-tag-naming). NEVER returns a
/// stripped (FE0F-removed) form for display.</item>
/// </list>
///
/// Pure/UnityEngine-free so it is unit-testable without a scene. Used on both channels for
/// COMPARISON only — WhatsApp already stores/displays the qualified form, so nothing changes
/// there; only tapi's base echo is requalified (at the Telegram mapper's ingest seam).
/// </summary>
public static class ReactionEmoji
{
    // Emoji-presentation selector. A trailing one distinguishes "❤️" from the base "❤"; both
    // are the same reaction, so it is dropped to form the compare key.
    private const char VariationSelector16 = '\uFE0F';

    // Base-form (VS16-stripped) => fully-qualified glyph, built once from the qualified reaction
    // set so tapi's base echo requalifies to the exact string the optimistic entry / catalog use.
    private static readonly Dictionary<string, string> Requalify = BuildRequalify();

    /// <summary>
    /// VS16-insensitive equality/dedup key: drops a single trailing U+FE0F. Null/empty pass
    /// through unchanged. Callers compare the returned strings ordinally.
    /// </summary>
    public static string CompareKey(string emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return emoji;
        return emoji[emoji.Length - 1] == VariationSelector16
            ? emoji.Substring(0, emoji.Length - 1)
            : emoji;
    }

    /// <summary>
    /// True when two emoji are the same reaction ignoring a trailing VS16. Null-safe: two
    /// null/empty inputs compare equal (ordinal comparison of their compare keys).
    /// </summary>
    public static bool SameEmoji(string a, string b) =>
        string.Equals(CompareKey(a), CompareKey(b), StringComparison.Ordinal);

    /// <summary>
    /// The fully-qualified DISPLAY form: if <paramref name="emoji"/> (or its base form) is a
    /// known qualified reaction glyph, return that qualified form; otherwise return
    /// <paramref name="emoji"/> unchanged. Turns tapi's base "❤" into the sprite-renderable
    /// "❤️"; never returns a stripped form (the TMP sprite name needs -fe0f). Null/empty pass
    /// through, and an already-qualified input is idempotent.
    /// </summary>
    public static string Canonical(string emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return emoji;
        return Requalify.TryGetValue(CompareKey(emoji), out string qualified) ? qualified : emoji;
    }

    // Map each qualified reaction glyph's base form back to the qualified glyph. Only glyphs
    // that actually carry a trailing VS16 differ from their base, but mapping every entry keeps
    // lookup uniform and self-heals if the source set changes. Base forms are unique in the set,
    // so there are no collisions.
    private static Dictionary<string, string> BuildRequalify()
    {
        var map = new Dictionary<string, string>();
        foreach (string qualified in TelegramReactionCatalog.AllowedSet)
            map[CompareKey(qualified)] = qualified;
        return map;
    }
}

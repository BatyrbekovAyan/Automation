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
    // Emoji-presentation selector. It distinguishes "❤️" from the base "❤" — trailing on most
    // qualified glyphs, MID-sequence in ZWJ sequences (heart-on-fire is U+2764 U+FE0F U+200D
    // U+1F525, selector before the joiner; 08-REVIEW WR-04). Same reaction either way, so EVERY
    // occurrence is dropped to form the compare key.
    private const char VariationSelector16 = '\uFE0F';

    // string.Replace has no char-remove overload; the selector's string form is cached once so
    // CompareKey's strip path allocates only the result. MUST stay declared before Requalify:
    // static field initializers run in textual order and BuildRequalify calls CompareKey.
    private static readonly string VariationSelector16String = VariationSelector16.ToString();

    // Base-form (VS16-stripped) => fully-qualified glyph, built once from the qualified reaction
    // set so tapi's base echo requalifies to the exact string the optimistic entry / catalog use.
    private static readonly Dictionary<string, string> Requalify = BuildRequalify();

    /// <summary>
    /// VS16-insensitive equality/dedup key: drops EVERY U+FE0F — trailing AND mid-sequence.
    /// Heart-on-fire (U+2764 U+FE0F U+200D U+1F525) carries its selector before the ZWJ, so a
    /// trailing-only strip missed it and reintroduced every D2 symptom for that one emoji
    /// (08-REVIEW WR-04). Verified collision-free: all 73 catalog entries keep unique base forms
    /// after a full strip. Null/empty pass through unchanged; compare results ordinally.
    /// </summary>
    public static string CompareKey(string emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return emoji;
        if (emoji.IndexOf(VariationSelector16) < 0) return emoji;   // fast path, no alloc
        return emoji.Replace(VariationSelector16String, "");
    }

    /// <summary>
    /// True when two emoji are the same reaction ignoring VS16 occurrences. Null-safe: two
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
    // that actually carry a VS16 differ from their base, but mapping every entry keeps
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

using System.Collections.Generic;

/// <summary>
/// Display aggregation for the reaction pill: the distinct emojis to show
/// (first-seen order, capped) and the total number of reactors. Pure/static.
/// </summary>
public static class ReactionSummary
{
    public const int MaxEmojis = 3;

    public static (List<string> emojis, int count) Build(List<MessageReaction> reactions)
    {
        var emojis = new List<string>();
        if (reactions == null || reactions.Count == 0) return (emojis, 0);

        // Empty-emoji entries are removal tombstones (D2), not real reactions — they must not
        // show a glyph OR inflate the reactor count, so the count tracks only visible emoji.
        // Dedup by the VS16-insensitive compare key (D2 root cause A) so tapi's base ❤ and the
        // app's qualified ❤️ collapse to ONE heart glyph — but keep DISPLAYING the stored
        // (qualified, sprite-renderable) glyph.
        int count = 0;
        var seenKeys = new HashSet<string>();
        foreach (var r in reactions)
        {
            if (r == null || string.IsNullOrEmpty(r.emoji)) continue;
            count++;
            if (emojis.Count < MaxEmojis && seenKeys.Add(ReactionEmoji.CompareKey(r.emoji)))
                emojis.Add(r.emoji);
        }
        return (emojis, count);
    }
}

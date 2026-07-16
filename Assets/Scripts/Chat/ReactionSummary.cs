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
        int count = 0;
        foreach (var r in reactions)
        {
            if (r == null || string.IsNullOrEmpty(r.emoji)) continue;
            count++;
            if (emojis.Count < MaxEmojis && !emojis.Contains(r.emoji))
                emojis.Add(r.emoji);
        }
        return (emojis, count);
    }
}

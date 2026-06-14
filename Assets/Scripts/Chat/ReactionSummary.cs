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

        foreach (var r in reactions)
        {
            if (r == null || string.IsNullOrEmpty(r.emoji)) continue;
            if (emojis.Count < MaxEmojis && !emojis.Contains(r.emoji))
                emojis.Add(r.emoji);
        }
        return (emojis, reactions.Count);
    }
}

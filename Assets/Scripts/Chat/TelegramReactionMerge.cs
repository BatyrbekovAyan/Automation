using System.Collections.Generic;

/// <summary>
/// Reconciles a Telegram message's cached reaction list with a freshly-fetched server list.
/// tapi delivers the full <c>reactions[]</c> on every <c>messages/get</c>, so a refresh is
/// authoritative for everyone's reactions — EXCEPT the account owner's just-sent optimistic
/// reaction (stored under <see cref="OutgoingReaction.MeReactorKey"/>) which the server has
/// not echoed yet. Preserving that "me" entry until the server echoes the same emoji stops a
/// just-tapped reaction from flickering off on the next sync and keeps it toggleable.
///
/// Pure/static so the reconcile is unit-testable without a live server. Used only on the
/// Telegram path; WhatsApp reactions flow through <see cref="ReactionStore"/> untouched.
/// </summary>
public static class TelegramReactionMerge
{
    /// <summary>
    /// Server list wins for all non-owner reactions; the cached owner ("me") reaction is kept
    /// only while the server list does not already contain that emoji. Returns null when the
    /// merged result is empty (so "all reactions removed" clears the list).
    /// </summary>
    public static List<MessageReaction> Merge(List<MessageReaction> cached, List<MessageReaction> server)
    {
        var result = server != null ? new List<MessageReaction>(server) : new List<MessageReaction>();

        MessageReaction mine = FindMine(cached);
        if (mine != null && !string.IsNullOrEmpty(mine.emoji) && !ContainsEmoji(result, mine.emoji))
            result.Add(mine);

        return result.Count > 0 ? result : null;
    }

    /// <summary>Order-insensitive equality by the (reactorKey, emoji) multiset — avoids a
    /// spurious re-render when the server returns the same reactions in a different order.</summary>
    public static bool SameReactions(List<MessageReaction> a, List<MessageReaction> b)
    {
        int countA = a?.Count ?? 0;
        int countB = b?.Count ?? 0;
        if (countA != countB) return false;
        if (countA == 0) return true;

        var seen = new Dictionary<string, int>(countA);
        foreach (var r in a) Bump(seen, Key(r), 1);
        foreach (var r in b) Bump(seen, Key(r), -1);
        foreach (var kv in seen)
            if (kv.Value != 0) return false;
        return true;
    }

    private static MessageReaction FindMine(List<MessageReaction> reactions)
    {
        if (reactions == null) return null;
        foreach (var r in reactions)
            if (r != null && r.reactorKey == OutgoingReaction.MeReactorKey) return r;
        return null;
    }

    private static bool ContainsEmoji(List<MessageReaction> reactions, string emoji)
    {
        foreach (var r in reactions)
            if (r != null && r.emoji == emoji) return true;
        return false;
    }

    private static string Key(MessageReaction r) => $"{r?.reactorKey}{r?.emoji}";

    private static void Bump(Dictionary<string, int> map, string key, int delta) =>
        map[key] = (map.TryGetValue(key, out int v) ? v : 0) + delta;
}

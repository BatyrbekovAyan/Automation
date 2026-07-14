using System.Collections.Generic;

/// <summary>
/// Reconciles a Telegram message's cached reaction list with a freshly-fetched server list.
/// tapi delivers the full <c>reactions[]</c> on every <c>messages/get</c>, so a refresh is
/// authoritative for everyone's reactions — EXCEPT the account owner's just-sent optimistic
/// reaction (stored under <see cref="OutgoingReaction.MeReactorKey"/>, carrying the tap's
/// unix time) which the server has not echoed yet. Identity — not emoji presence — decides
/// preservation: the server echo of the owner's reaction is itself mapped to "me" by
/// <see cref="TelegramReactionMapper"/> via the learned own user id, so another user reacting
/// with the same emoji can never consume the owner's entry, and the kept entry always stays
/// toggleable through <see cref="OutgoingReaction.CurrentMyEmoji"/> (05-06-REVIEW WR-01).
///
/// Pure/static so the reconcile is unit-testable without a live server. Used only on the
/// Telegram path; WhatsApp reactions flow through <see cref="ReactionStore"/> untouched.
/// </summary>
public static class TelegramReactionMerge
{
    /// <summary>
    /// How long (seconds) a fresh optimistic "me" entry outranks the server's view of "me".
    /// Past this window the server is authoritative, so a reaction the owner removed from the
    /// phone's Telegram app propagates instead of staying pinned by an un-echoed cache entry
    /// (server-mapped entries carry time=0 and are never "fresh" — their removals apply at once).
    /// </summary>
    public const long OptimisticGraceSeconds = 90;

    /// <summary>
    /// Server list wins for all reactions except a FRESH optimistic owner entry (tap within
    /// <see cref="OptimisticGraceSeconds"/> of <paramref name="nowUnix"/>), which replaces the
    /// server's "me" element (stale echo during an emoji change) or rides alongside when the
    /// echo hasn't landed yet. Returns null when the merged result is empty (so "all reactions
    /// removed" clears the list).
    /// </summary>
    public static List<MessageReaction> Merge(List<MessageReaction> cached, List<MessageReaction> server, long nowUnix)
    {
        var result = server != null ? new List<MessageReaction>(server) : new List<MessageReaction>();

        MessageReaction mine = FindMine(cached);
        if (mine != null && !string.IsNullOrEmpty(mine.emoji) && IsFreshOptimistic(mine, nowUnix))
        {
            int serverMine = IndexOfMine(result);
            if (serverMine >= 0) result[serverMine] = mine;   // fresh local emoji beats a stale echo
            else result.Add(mine);                            // not yet echoed — keep the optimistic entry
        }

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

    /// <summary>True only for the optimistic send entry (real tap time) still inside the grace
    /// window — a server-mapped "me" echo carries time=0 and is therefore never fresh.</summary>
    private static bool IsFreshOptimistic(MessageReaction mine, long nowUnix) =>
        mine.time > 0 && nowUnix - mine.time <= OptimisticGraceSeconds;

    private static MessageReaction FindMine(List<MessageReaction> reactions)
    {
        if (reactions == null) return null;
        foreach (var r in reactions)
            if (r != null && r.reactorKey == OutgoingReaction.MeReactorKey) return r;
        return null;
    }

    private static int IndexOfMine(List<MessageReaction> reactions)
    {
        for (int i = 0; i < reactions.Count; i++)
            if (reactions[i] != null && reactions[i].reactorKey == OutgoingReaction.MeReactorKey) return i;
        return -1;
    }

    private static string Key(MessageReaction r) => $"{r?.reactorKey}{r?.emoji}";

    private static void Bump(Dictionary<string, int> map, string key, int delta) =>
        map[key] = (map.TryGetValue(key, out int v) ? v : 0) + delta;
}

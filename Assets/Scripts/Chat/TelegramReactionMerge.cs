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
    /// <see cref="OptimisticGraceSeconds"/> of <paramref name="nowUnix"/>). A fresh non-empty
    /// "me" replaces the server's "me" element (stale echo during an emoji change) or rides
    /// alongside when the echo hasn't landed yet; a fresh EMPTY "me" is a removal tombstone
    /// (D2) that SUPPRESSES the server's stale "me" echo AND is carried into the result, so
    /// every reconcile within the grace window keeps suppressing — the D5 live poll reconciles
    /// every ~3 s, and a tombstone consumed by its first merge would let the very next poll's
    /// still-echoed "me" resurrect the removed reaction (08-REVIEW WR-03). A tombstone-only
    /// result is returned as-is (it renders as "no reactions" — ReactionSummary skips
    /// empty-emoji entries); returns null only when the merged result is empty, so post-grace
    /// "all reactions removed" still clears the list.
    /// </summary>
    public static List<MessageReaction> Merge(List<MessageReaction> cached, List<MessageReaction> server, long nowUnix)
    {
        var result = server != null ? new List<MessageReaction>(server) : new List<MessageReaction>();

        MessageReaction mine = FindMine(cached);
        if (mine != null && IsFreshOptimistic(mine, nowUnix))
        {
            int serverMine = IndexOfMine(result);
            if (string.IsNullOrEmpty(mine.emoji))
            {
                // Fresh optimistic REMOVAL tombstone: drop the server's stale "me" echo AND keep
                // the tombstone in the result, so the NEXT reconcile (3 s later at the D5 poll
                // cadence — the server keeps echoing for a cycle or more after a successful
                // removal) still suppresses a late echo. Invisible by design: ReactionSummary
                // hides empty-emoji entries and RenderReactions bases clearance on visible emoji.
                // Once the grace lapses, the next merge drops the tombstone naturally (server
                // list wins; a tombstone is never in the server list).
                if (serverMine >= 0) result.RemoveAt(serverMine);
                result.Add(mine);
            }
            else if (serverMine >= 0)
            {
                result[serverMine] = mine;   // fresh local emoji beats a stale echo
            }
            else
            {
                // No server "me". Before keeping the optimistic entry as a SECOND row, try to FOLD
                // a single un-mapped same-emoji server echo into "me" (D2 root cause B): when the
                // owner reacted before their id was learned, tapi keys their echo by the numeric
                // user_id, so it looks like a stranger's same-emoji reaction and rides alongside
                // the optimistic "me" → count «2» (symptom 1). Same-canonical-emoji + a fresh
                // optimistic "mine" ⇒ it is the owner's own echo — replace it in place, don't Add.
                int echoIdx = IndexOfUnmappedSameEmoji(result, mine.emoji);
                if (echoIdx >= 0)
                    result[echoIdx] = mine;  // fold the owner's un-mapped echo into "me"
                else
                    result.Add(mine);        // genuinely not yet echoed — keep the optimistic entry
            }
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Stamp a FRESH optimistic-removal tombstone for the owner ("me") after a toggle-off: an
    /// empty-emoji "me" entry carrying the tap time. <see cref="Merge"/> reads it as a removal
    /// and suppresses the server's stale "me" echo within <see cref="OptimisticGraceSeconds"/>,
    /// so a just-removed reaction cannot resurrect (D2). Reuses an existing "me" slot (blanking
    /// it) rather than duplicating. Empty-emoji entries never render (ReactionSummary skips them).
    /// No-ops on a null list — the caller owns the field and guarantees it is non-null.
    /// </summary>
    public static void StampRemovalTombstone(List<MessageReaction> reactions, long nowUnix)
    {
        if (reactions == null) return;

        int idx = IndexOfMine(reactions);
        if (idx >= 0)
        {
            reactions[idx].emoji = "";
            reactions[idx].time = nowUnix;
            reactions[idx].fromMe = true;
            return;
        }

        reactions.Add(new MessageReaction
        {
            emoji = "",
            reactorKey = OutgoingReaction.MeReactorKey,
            senderName = "Me",
            fromMe = true,
            time = nowUnix
        });
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

    /// <summary>
    /// Index of a SINGLE server entry that is the owner's own echo keyed by a numeric user_id
    /// instead of "me" (D2 root cause B: _tgOwnUserId was unlearned when the reaction was sent).
    /// Matches the owner's optimistic emoji by canonical form (VS16-insensitive) and is NOT already
    /// "me". Returns the first match, or -1. Callers invoke this only when a fresh optimistic "me"
    /// is present and there is no server "me", consuming at most one entry — so a stranger's
    /// same-emoji reaction on a message the owner did NOT react to is never folded (T-08-11-01).
    /// </summary>
    private static int IndexOfUnmappedSameEmoji(List<MessageReaction> reactions, string mineEmoji)
    {
        for (int i = 0; i < reactions.Count; i++)
        {
            MessageReaction r = reactions[i];
            if (r == null || r.reactorKey == OutgoingReaction.MeReactorKey) continue;
            if (ReactionEmoji.SameEmoji(r.emoji, mineEmoji)) return i;
        }
        return -1;
    }

    // IN-06: a U+0001 separator (a control char that can never appear in a reactorKey or emoji)
    // so "me"+"❤" can't collide with a "me❤"-shaped reactorKey; the emoji is reduced to its
    // VS16-insensitive compare key so base ❤ and qualified ❤️ count as the same reaction.
    private static string Key(MessageReaction r) => $"{r?.reactorKey}\u0001{ReactionEmoji.CompareKey(r?.emoji)}";

    private static void Bump(Dictionary<string, int> map, string key, int delta) =>
        map[key] = (map.TryGetValue(key, out int v) ? v : 0) + delta;
}

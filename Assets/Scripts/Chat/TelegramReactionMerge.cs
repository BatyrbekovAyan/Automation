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
    /// <see cref="OptimisticGraceSeconds"/> of <paramref name="nowUnix"/>). The suppression is
    /// DISCRIMINATED by the DISPLACED pre-tap emoji the optimistic entry replaced (CR-01a): for a
    /// fresh non-empty "me", a SAME-emoji echo confirms the owner's reaction (the server element is
    /// adopted, non-fresh); a DIFFERING echo is suppressed (fresh local wins) ONLY when it equals the
    /// displaced pre-tap value — the stale old-emoji echo the grace exists to defeat; any THIRD value
    /// (neither the optimistic nor the displaced emoji) is a genuinely newer external own-change and is
    /// adopted at once, so an own-reaction change made in the Telegram app repaints immediately. An
    /// un-echoed entry rides alongside until the server catches up. A fresh EMPTY "me" is a removal
    /// tombstone (D2): WHILE the server STILL echoes the DISPLACED (just-removed) emoji it SUPPRESSES
    /// that stale echo AND is carried so the next ~3 s poll keeps suppressing (08-REVIEW WR-03), but a
    /// DIFFERING "me" echo (a genuine external re-add of a third emoji) or a CONFIRMED absence (no "me"
    /// echo) DROPS the tombstone so the re-add applies / the list clears (WR-01). A tombstone-only
    /// result is returned as-is (it renders as "no reactions" — ReactionSummary skips empty-emoji
    /// entries); returns null when the merged result is empty, so a confirmed-absence removal (and any
    /// post-grace "all reactions removed") clears the list.
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
                if (serverMine >= 0 && ReactionEmoji.SameEmoji(result[serverMine].emoji, mine.displacedEmoji))
                {
                    // Server STILL echoes the DISPLACED (just-removed) emoji: suppress it and CARRY the tombstone
                    // so the next ~3 s poll keeps suppressing a late echo (WR-03). Invisible by design:
                    // ReactionSummary hides empty-emoji entries and RenderReactions bases clearance on visible emoji.
                    result.RemoveAt(serverMine);
                    result.Add(mine);
                }
                // else: no "me" echo (absence confirmed, WR-01) OR a DIFFERING "me" echo (a genuine external
                // re-add of a THIRD emoji) — DROP the tombstone; a differing server "me" stays in the result so
                // the external re-add applies, a confirmed absence clears to null.
            }
            else if (serverMine >= 0)
            {
                // CR-01a (D2-view): a differing echo is the stale pre-tap echo ONLY when it equals the DISPLACED
                // emoji this optimistic entry replaced; any THIRD value is a genuinely newer external own-change.
                if (!ReactionEmoji.SameEmoji(result[serverMine].emoji, mine.emoji))
                {
                    if (ReactionEmoji.SameEmoji(result[serverMine].emoji, mine.displacedEmoji))
                    {
#if UNITY_EDITOR
                        UnityEngine.Debug.Log($"[D2-merge] suppressed stale displaced echo '{result[serverMine].emoji}' under fresh local '{mine.emoji}' age={nowUnix - mine.time}s");
#endif
                        result[serverMine] = mine;   // stale echo of the displaced pre-tap state (round-2 D2 defense)
                    }
                    // else: a THIRD value (neither optimistic nor displaced) — a genuinely newer external
                    // own-change: keep the server element (time=0 ⇒ freshness consumed).
                }
                // else: same-emoji echo confirmed the optimistic set — keep the server element (freshness consumed).
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
                {
                    // CR-01: a same-emoji un-mapped echo IS the owner's server-confirmed reaction —
                    // adopt it as "me" (re-key so it stays toggleable via OutgoingReaction.CurrentMyEmoji)
                    // instead of pinning the fresh optimistic entry, so the grace is consumed and the
                    // next external own-change applies.
                    result[echoIdx].reactorKey = OutgoingReaction.MeReactorKey;
                    result[echoIdx].fromMe = true;
                }
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
    public static void StampRemovalTombstone(List<MessageReaction> reactions, long nowUnix, string displacedEmoji)
    {
        if (reactions == null) return;

        int idx = IndexOfMine(reactions);
        if (idx >= 0)
        {
            reactions[idx].emoji = "";
            reactions[idx].time = nowUnix;
            reactions[idx].fromMe = true;
            reactions[idx].displacedEmoji = displacedEmoji;
            return;
        }

        reactions.Add(new MessageReaction
        {
            emoji = "",
            reactorKey = OutgoingReaction.MeReactorKey,
            senderName = "Me",
            fromMe = true,
            time = nowUnix,
            displacedEmoji = displacedEmoji
        });
    }

    /// <summary>
    /// Stamp the displaced pre-tap emoji onto the owner's fresh optimistic entry (add/change path). Merge
    /// reads it to suppress a stale echo of the pre-tap state but adopt a genuinely newer external own-change.
    /// No-ops when there is no own entry (guarded by the caller's optimistic apply).
    /// </summary>
    public static void StampDisplaced(List<MessageReaction> reactions, string displacedEmoji)
    {
        if (reactions == null) return;
        int idx = IndexOfMine(reactions);
        if (idx >= 0) reactions[idx].displacedEmoji = displacedEmoji;
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

/// <summary>
/// Pure, UnityEngine-free decision for the D2-ext loaded-window reaction reconcile. The D5 live
/// poll re-fetches only the latest page (<c>messages/get?...&amp;limit=MessagesPerPage&amp;offset=0</c>),
/// so a reaction changed or removed IN the Telegram app on a LOADED-but-older message — the owner
/// scrolled up, or the cache alone already holds more than one page — is never re-synced by the
/// poll and its pill never reconciles. This seam answers two questions the poll needs: does the
/// loaded window spill past the latest page (<see cref="NeedsWiderPass"/>), and how many server
/// pages does it span (<see cref="PagesToCover"/>) so a bounded, throttled background pass can walk
/// the older pages. Telegram-only (WhatsApp reactions flow through <see cref="ReactionStore"/>);
/// side-effect-free so the window math is fully EditMode-testable (mirrors
/// <see cref="OpenChatLivePollGate"/> / <see cref="TelegramReactionMerge"/>).
/// </summary>
public static class ReactionReconcileWindow
{
    /// <summary>
    /// True when the loaded message window exceeds the latest page, so older loaded messages need
    /// a wider reaction-reconcile pass the latest-window poll can never reach. False for an empty
    /// window, a single (or partial) page, or a non-positive page size.
    /// </summary>
    public static bool NeedsWiderPass(int loadedCount, int latestPageSize) =>
        latestPageSize > 0 && loadedCount > latestPageSize;

    /// <summary>
    /// Number of server pages the loaded window spans — <c>ceil(loadedCount / pageSize)</c> — so the
    /// wider pass knows the last older page to cover. 0 for an empty window or a non-positive page
    /// size (nothing to walk).
    /// </summary>
    public static int PagesToCover(int loadedCount, int pageSize) =>
        (pageSize <= 0 || loadedCount <= 0) ? 0 : (loadedCount + pageSize - 1) / pageSize;
}

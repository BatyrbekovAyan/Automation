using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Covers the removal-tombstone reconcile (D2): removing an own Telegram reaction succeeds
/// server-side, but tapi keeps echoing the owner's reaction on <c>messages/get</c> for a cycle.
/// A bare removal (RemoveAt) leaves NO "me" entry, so <see cref="TelegramReactionMerge.Merge"/>
/// can't tell "just removed" from "never reacted" and the stale echo resurrects the reaction.
/// A fresh empty-emoji "me" tombstone (<see cref="TelegramReactionMerge.StampRemovalTombstone"/>)
/// lets Merge suppress that echo within the grace window. All pure — no scene. The WhatsApp
/// ReactionStore path never reaches Merge and is unaffected.
/// </summary>
public class TelegramReactionMergeTests
{
    private const long Now = 1_752_000_000;

    private static MessageReaction Me(string emoji, long time = Now) => new MessageReaction
    { emoji = emoji, reactorKey = OutgoingReaction.MeReactorKey, fromMe = true, time = time };

    // Fresh optimistic-removal tombstone: empty emoji, "me", real tap time.
    private static MessageReaction Removal(long time = Now) => new MessageReaction
    { emoji = "", reactorKey = OutgoingReaction.MeReactorKey, fromMe = true, time = time };

    private static MessageReaction Other(string emoji, string key) => new MessageReaction
    { emoji = emoji, reactorKey = key, fromMe = false };

    // --- Merge: fresh removal tombstone suppresses the stale server echo ---

    [Test]
    public void Merge_FreshRemoval_SuppressesServerEcho_NoResurrection()
    {
        // Owner just removed 👍; the server still echoes it as "me". The tombstone must drop it.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now) },
            new List<MessageReaction> { Me("👍", time: 0) },
            Now);

        Assert.IsNull(merged);   // owner was the only reactor → list clears, no resurrection
    }

    [Test]
    public void Merge_FreshRemoval_WithOtherReactor_DropsOnlyMe()
    {
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now) },
            new List<MessageReaction> { Me("👍", time: 0), Other("❤", "999") },
            Now);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("999", merged[0].reactorKey);
        Assert.IsFalse(merged.Exists(r => r.reactorKey == OutgoingReaction.MeReactorKey));
    }

    [Test]
    public void Merge_AgedRemoval_ServerWins_SelfHeal()
    {
        // Past the grace window the tombstone stops suppressing — if the server still reports the
        // reaction it comes back (bounded suppression; normally the server has cleared it by then).
        long agedTap = Now - TelegramReactionMerge.OptimisticGraceSeconds - 1;
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(agedTap) },
            new List<MessageReaction> { Me("👍", time: 0) },
            Now);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, merged[0].reactorKey);
        Assert.AreEqual("👍", merged[0].emoji);
    }

    [Test]
    public void Merge_FreshAdd_StillPreserved()
    {
        // Existing behavior intact: a fresh optimistic ADD is preserved when unechoed.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("👍", Now) }, null, Now);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, merged[0].reactorKey);
        Assert.AreEqual("👍", merged[0].emoji);
    }

    [Test]
    public void Merge_OtherUserSameEmoji_NotConsumedByTombstone()
    {
        // The tombstone is scoped to "me": another user's same-emoji reaction is never dropped.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now) },
            new List<MessageReaction> { Other("👍", "999") },
            Now);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("999", merged[0].reactorKey);
    }

    [Test]
    public void Merge_LoneFreshRemoval_NoServer_IsNull()
    {
        // A tombstone with nothing to suppress never lingers as its own entry.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now) }, null, Now);

        Assert.IsNull(merged);
    }

    // --- StampRemovalTombstone: the marker SendReaction leaves on toggle-off ---

    [Test]
    public void StampRemovalTombstone_AddsFreshEmptyMeMarker()
    {
        var reactions = new List<MessageReaction>();
        TelegramReactionMerge.StampRemovalTombstone(reactions, Now);

        Assert.AreEqual(1, reactions.Count);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, reactions[0].reactorKey);
        Assert.AreEqual("", reactions[0].emoji);
        Assert.AreEqual(Now, reactions[0].time);
        Assert.IsTrue(reactions[0].fromMe);
    }

    [Test]
    public void StampRemovalTombstone_ReusesExistingMeSlot_NoDuplicate()
    {
        var reactions = new List<MessageReaction> { Me("👍", 1), Other("❤", "999") };
        TelegramReactionMerge.StampRemovalTombstone(reactions, Now);

        // Still one "me" entry, now blanked; the other reactor is untouched.
        Assert.AreEqual(1, reactions.FindAll(r => r.reactorKey == OutgoingReaction.MeReactorKey).Count);
        var mine = reactions.Find(r => r.reactorKey == OutgoingReaction.MeReactorKey);
        Assert.AreEqual("", mine.emoji);
        Assert.AreEqual(Now, mine.time);
        Assert.IsTrue(reactions.Exists(r => r.reactorKey == "999"));
    }

    [Test]
    public void StampThenMerge_EndToEnd_RemovedReactionStaysRemoved()
    {
        // The full toggle-off flow: owner had 👍 (optimistic), stamps a tombstone, then the next
        // reconcile against a still-echoing server clears it instead of resurrecting.
        var reactions = new List<MessageReaction> { Me("👍", Now - 5) };
        TelegramReactionMerge.StampRemovalTombstone(reactions, Now);

        var merged = TelegramReactionMerge.Merge(
            reactions,
            new List<MessageReaction> { Me("👍", time: 0) },
            Now);

        Assert.IsNull(merged);
    }
}

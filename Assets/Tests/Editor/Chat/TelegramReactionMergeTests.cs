using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Covers the removal-tombstone reconcile (D2): removing an own Telegram reaction succeeds
/// server-side, but tapi keeps echoing the owner's reaction on <c>messages/get</c> for a cycle.
/// A bare removal (RemoveAt) leaves NO "me" entry, so <see cref="TelegramReactionMerge.Merge"/>
/// can't tell "just removed" from "never reacted" and the stale echo resurrects the reaction.
/// A fresh empty-emoji "me" tombstone (<see cref="TelegramReactionMerge.StampRemovalTombstone"/>)
/// lets Merge suppress that echo within the grace window — and is CARRIED through each merge
/// (08-REVIEW WR-03): the D5 live poll reconciles every ~3 s, so a tombstone consumed by its
/// first merge would let the very next poll resurrect the reaction. A tombstone-only list is
/// invisible (ReactionSummary skips empty-emoji entries). All pure — no scene. The WhatsApp
/// ReactionStore path never reaches Merge and is unaffected.
/// </summary>
public class TelegramReactionMergeTests
{
    private const long Now = 1_752_000_000;

    private static MessageReaction Me(string emoji, long time = Now, string displaced = null) => new MessageReaction
    { emoji = emoji, reactorKey = OutgoingReaction.MeReactorKey, fromMe = true, time = time, displacedEmoji = displaced };

    // Fresh optimistic-removal tombstone: empty emoji, "me", real tap time.
    private static MessageReaction Removal(long time = Now, string displaced = null) => new MessageReaction
    { emoji = "", reactorKey = OutgoingReaction.MeReactorKey, fromMe = true, time = time, displacedEmoji = displaced };

    private static MessageReaction Other(string emoji, string key) => new MessageReaction
    { emoji = emoji, reactorKey = key, fromMe = false };

    // --- Merge: fresh removal tombstone suppresses the stale server echo ---

    [Test]
    public void Merge_FreshRemoval_SuppressesServerEcho_NoResurrection()
    {
        // Owner just removed 👍; the server still echoes it as "me". The tombstone must drop the
        // echo AND survive the merge, staying armed for the next poll's reconcile (WR-03).
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now, "👍") },
            new List<MessageReaction> { Me("👍", time: 0) },
            Now);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, merged[0].reactorKey);
        Assert.AreEqual("", merged[0].emoji);                            // tombstone, not a reaction
        Assert.AreEqual(0, ReactionSummary.Build(merged).emojis.Count);  // renders as "no reactions"
    }

    [Test]
    public void Merge_FreshRemoval_WithOtherReactor_DropsOnlyMyEcho()
    {
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now, "👍") },
            new List<MessageReaction> { Me("👍", time: 0), Other("❤", "999") },
            Now);

        Assert.AreEqual(2, merged.Count);   // other reactor + carried tombstone
        Assert.IsTrue(merged.Exists(r => r.reactorKey == "999" && r.emoji == "❤"));
        var mine = merged.Find(r => r.reactorKey == OutgoingReaction.MeReactorKey);
        Assert.AreEqual("", mine.emoji);    // my echo suppressed down to the invisible tombstone
        Assert.AreEqual(1, ReactionSummary.Build(merged).count);   // only ❤ visible
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
    public void Merge_OtherUserSameEmoji_MyAbsenceConfirmed_DropsTombstone_OtherKept()
    {
        // WR-01 + spoof guard: my "me" echo is absent (removal confirmed) so the tombstone drops, and the
        // stranger's same-emoji reaction is neither dropped nor claimed as "me".
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now) },
            new List<MessageReaction> { Other("👍", "999") },
            Now);

        Assert.AreEqual(1, merged.Count);
        Assert.IsTrue(merged.Exists(r => r.reactorKey == "999" && r.emoji == "👍"));
        Assert.IsFalse(merged.Exists(r => r.reactorKey == OutgoingReaction.MeReactorKey));   // no "me" carried
    }

    [Test]
    public void Merge_LoneFreshRemoval_NoServerEcho_DropsTombstone_AbsenceConfirmed()
    {
        // WR-01: with NO server "me" the server has CONFIRMED the removal — drop the tombstone instead of
        // carrying it for the full 90 s (carrying it would suppress an external own re-add). A lone
        // removal against an absent echo therefore clears to null.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now) }, null, Now);
        Assert.IsNull(merged);
    }

    [Test]
    public void Merge_TwoSuccessivePolls_TombstoneKeepsSuppressing_NoResurrection()
    {
        // The D5 live poll reconciles every ~3 s and tapi's stale echo can outlive one interval.
        // The tombstone must survive poll 1 so poll 2's still-echoed "me" is suppressed too —
        // consumed-on-first-merge shrank the 90 s grace window to a single cycle (WR-03).
        var afterPoll1 = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now, "👍") },
            new List<MessageReaction> { Me("👍", time: 0) },
            Now);

        var afterPoll2 = TelegramReactionMerge.Merge(
            afterPoll1,
            new List<MessageReaction> { Me("👍", time: 0) },   // echo still not cleared
            Now + 3);

        Assert.IsNotNull(afterPoll2);
        Assert.IsFalse(afterPoll2.Exists(r => !string.IsNullOrEmpty(r.emoji)));   // no resurrection
        Assert.AreEqual(0, ReactionSummary.Build(afterPoll2).emojis.Count);       // renders as none
    }

    [Test]
    public void Merge_AgedTombstone_NoServer_DropsNaturally()
    {
        // Once the grace lapses, the next merge drops the carried tombstone (server list wins;
        // a tombstone is never in the server list) — it can't linger forever.
        long agedTap = Now - TelegramReactionMerge.OptimisticGraceSeconds - 1;
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(agedTap) }, null, Now);

        Assert.IsNull(merged);
    }

    [Test]
    public void Merge_FreshRemoval_AbsenceConfirmed_ThenExternalReAdd_Applies()
    {
        // WR-01 end-to-end: removal against no server echo drops the tombstone (null), then the owner
        // RE-ADDS ❤ in the Telegram app — the re-add applies (not suppressed by a lingering tombstone).
        var afterRemoval = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now) }, null, Now + 3);
        Assert.IsNull(afterRemoval);

        var afterReAdd = TelegramReactionMerge.Merge(
            afterRemoval, new List<MessageReaction> { Me("❤", time: 0) }, Now + 6);
        Assert.AreEqual(1, afterReAdd.Count);
        Assert.AreEqual("❤", afterReAdd[0].emoji);
    }

    // --- StampRemovalTombstone: the marker SendReaction leaves on toggle-off ---

    [Test]
    public void StampRemovalTombstone_AddsFreshEmptyMeMarker()
    {
        var reactions = new List<MessageReaction>();
        TelegramReactionMerge.StampRemovalTombstone(reactions, Now, "👍");

        Assert.AreEqual(1, reactions.Count);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, reactions[0].reactorKey);
        Assert.AreEqual("", reactions[0].emoji);
        Assert.AreEqual(Now, reactions[0].time);
        Assert.IsTrue(reactions[0].fromMe);
        Assert.AreEqual("👍", reactions[0].displacedEmoji);
    }

    [Test]
    public void StampRemovalTombstone_ReusesExistingMeSlot_NoDuplicate()
    {
        var reactions = new List<MessageReaction> { Me("👍", 1), Other("❤", "999") };
        TelegramReactionMerge.StampRemovalTombstone(reactions, Now, "👍");

        // Still one "me" entry, now blanked; the other reactor is untouched.
        Assert.AreEqual(1, reactions.FindAll(r => r.reactorKey == OutgoingReaction.MeReactorKey).Count);
        var mine = reactions.Find(r => r.reactorKey == OutgoingReaction.MeReactorKey);
        Assert.AreEqual("", mine.emoji);
        Assert.AreEqual(Now, mine.time);
        Assert.AreEqual("👍", mine.displacedEmoji);
        Assert.IsTrue(reactions.Exists(r => r.reactorKey == "999"));
    }

    [Test]
    public void StampThenMerge_EndToEnd_RemovedReactionStaysRemoved()
    {
        // The full toggle-off flow: owner had 👍 (optimistic), stamps a tombstone, then the next
        // reconcile against a still-echoing server suppresses the echo and keeps the invisible
        // tombstone armed for the following poll instead of resurrecting.
        var reactions = new List<MessageReaction> { Me("👍", Now - 5) };
        TelegramReactionMerge.StampRemovalTombstone(reactions, Now, "👍");

        var merged = TelegramReactionMerge.Merge(
            reactions,
            new List<MessageReaction> { Me("👍", time: 0) },
            Now);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("", merged[0].emoji);
        Assert.AreEqual(0, ReactionSummary.Build(merged).count);   // nothing visible, no reactor count
    }

    // --- Fold: an un-mapped own echo collapsed into "me" (D2 root cause B) ---
    // When the owner reacts in a chat/page with no own message loaded, _tgOwnUserId is unlearned,
    // so tapi echoes the owner's reaction keyed by the numeric user_id (not "me") and it rides
    // ALONGSIDE the optimistic "me" → count «2» (symptom 1). Merge folds a SINGLE same-canonical-
    // emoji un-mapped server entry into the fresh optimistic "me" — belt-and-suspenders for the
    // first-ever reaction, before the persisted-id fix maps the echo to "me" upstream.

    [Test]
    public void Merge_FreshOptimisticMe_PlusUnmappedSameEmojiEcho_CollapsesToOneMe()
    {
        // Owner reacted ❤️ (optimistic "me"); tapi's echo comes back as base ❤ keyed by a numeric
        // user_id (id unlearned). Fold it into "me": exactly one glyph, count 1 (symptom 1 fixed).
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("❤️", Now) },
            new List<MessageReaction> { Other("❤", "999") },
            Now);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, merged[0].reactorKey);
        Assert.AreEqual(1, ReactionSummary.Build(merged).count);
        Assert.AreEqual(1, ReactionSummary.Build(merged).emojis.Count);
    }

    [Test]
    public void Merge_FreshChange_MappedEcho_LeavesOneMe()
    {
        // change-leaves-one: owner changed 👍→❤️; once _tgOwnUserId is persisted, tapi's stale 👍
        // echo is mapped to "me" upstream, so the fresh ❤️ cleanly REPLACES it — one pill, not two.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("❤️", Now, "👍") },
            new List<MessageReaction> { Me("👍", time: 0) },   // old echo, mapped to "me" via persisted id
            Now);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, merged[0].reactorKey);
        Assert.AreEqual("❤️", merged[0].emoji);
    }

    // --- CR-01: the optimistic grace ENDS on server confirmation (D2-view root fix) ---

    [Test]
    public void Merge_SameEmojiEcho_ConsumesGrace_ThenExternalOwnChangeApplies()
    {
        // CR-01 (D2-view, milestone #1): the grace must END on server CONFIRMATION. Owner taps 👍
        // in-app (fresh optimistic "me"); tapi echoes the SAME 👍 (grace's job done). If the grace is
        // NOT consumed, a later EXTERNAL own-change to 😁 (made in the Telegram app) is discarded as a
        // "stale echo" for the rest of the 90 s window — the captured echo-without-event.
        var afterEcho = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("👍", Now) },
            new List<MessageReaction> { Me("👍", time: 0) },   // echo lands => grace ends
            Now + 3);
        var afterExternal = TelegramReactionMerge.Merge(
            afterEcho,
            new List<MessageReaction> { Me("😁", time: 0) },   // owner changes to 😁 IN the Telegram app
            Now + 8);

        Assert.IsFalse(TelegramReactionMerge.SameReactions(afterEcho, afterExternal)); // FAILS today
        Assert.AreEqual(1, afterExternal.Count);
        Assert.AreEqual("😁", afterExternal[0].emoji);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, afterExternal[0].reactorKey);   // still toggleable
    }

    [Test]
    public void Merge_DifferingEchoWithinGrace_StaleOldEmojiStillSuppressed()
    {
        // CR-01 boundary: do NOT clear the grace on a DIFFERING echo. Owner changed 👍→❤️ in-app; tapi
        // still echoes the OLD 👍 mapped to "me" during the window — the fresh ❤️ must win or the pill
        // flickers back to 👍 (the original D2 defect). Clearing on differ would regress it.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("❤️", Now, "👍") },
            new List<MessageReaction> { Me("👍", time: 0) },
            Now + 3);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("❤️", merged[0].emoji);   // fresh local wins; stale echo suppressed
    }

    [Test]
    public void Merge_DifferingEcho_NoDisplacedMatch_AdoptsExternalOwnChange()
    {
        // CR-01a (D2-view, the round-6 capture): owner tapped 🥺 in-app (no prior reaction ⇒ displaced null);
        // then changed their OWN reaction to 🔥 in the Telegram app. 🔥 is neither the optimistic 🥺 nor a
        // displaced value ⇒ a genuinely newer external own-change ⇒ adopt at once (RED today: suppressed age=9s).
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("🥺", Now) },       // displaced null
            new List<MessageReaction> { Me("🔥", time: 0) },
            Now + 9);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("🔥", merged[0].emoji);
        Assert.AreEqual(0, merged[0].time);   // adopted server element ⇒ freshness consumed
    }

    [Test]
    public void Merge_DifferingEcho_ThirdValue_DisplacedSet_AdoptsExternalOwnChange()
    {
        // Even with a displaced value set, a THIRD emoji (neither optimistic nor displaced) is a genuine
        // external own-change ⇒ adopt. Owner had 👍, changed to 🥺 in-app (displaced=👍), then to 🔥 in the TG app.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("🥺", Now, "👍") },
            new List<MessageReaction> { Me("🔥", time: 0) },
            Now + 9);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("🔥", merged[0].emoji);
    }

    [Test]
    public void Merge_FreshRemoval_DifferentEmojiEcho_ExternalReAddAdopts_TombstoneDropped()
    {
        // Tombstone displaced = the just-removed 👍. The server "me" is a DIFFERENT emoji 🔥 ⇒ a genuine
        // external re-add ⇒ keep the server element and DROP the tombstone (not suppressed).
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Removal(Now, "👍") },
            new List<MessageReaction> { Me("🔥", time: 0) },
            Now);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("🔥", merged[0].emoji);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, merged[0].reactorKey);
    }

    [Test]
    public void Merge_RevertShapedFreshMe_NullDisplaced_DifferingEchoAdopts()
    {
        // WR-01 / null-displaced property: a FRESH optimistic "me" with displacedEmoji null — a first-ever
        // reaction (no pre-tap state to displace) or a failed-POST revert entry (ReactionStore.ApplyToMessage
        // never sets displaced) — ADOPTS any differing echo (ghost-landed sent emoji, mid-flight external change)
        // instead of being pinned for the window. (A real Telegram revert of a NON-null prior hits the CHANGE
        // branch with displaced already stamped; this pins the null-displaced case.)
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("👍", Now) },       // Me() leaves displacedEmoji null
            new List<MessageReaction> { Me("🔥", time: 0) },
            Now + 5);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("🔥", merged[0].emoji);
    }

    [Test]
    public void Merge_DisplacedBaseHeart_SuppressesQualifiedHeartEcho()
    {
        // VS16 seam: displaced stored as base "❤" (U+2764) still matches a qualified "❤️" echo — the fresh
        // local wins (stale displaced echo suppressed) despite the variation-selector mismatch.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("😁", Now, "❤") },
            new List<MessageReaction> { Me("❤️", time: 0) },
            Now + 3);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("😁", merged[0].emoji);   // displaced-match ⇒ suppressed
    }

    [Test]
    public void MessageReaction_JsonUtility_MissingDisplacedEmoji_DefaultsNull()
    {
        // Old cached entries predate the field — JsonUtility leaves the missing key at its default (null),
        // which Merge reads as "absence" ⇒ adopt-on-differ. No migration needed.
        var legacy = UnityEngine.JsonUtility.FromJson<MessageReaction>(
            "{\"emoji\":\"👍\",\"reactorKey\":\"me\",\"time\":5}");
        Assert.IsNull(legacy.displacedEmoji);
    }

    [Test]
    public void Merge_FreshOptimisticMe_DifferentEmojiEcho_NotFolded()
    {
        // Scope guard: the fold is same-emoji ONLY. A DIFFERENT-emoji un-mapped entry (e.g. the
        // owner's stale 👍 echo mid-change, still numeric-keyed) is NOT folded here — the
        // persisted-id path maps it to "me" so it is cleanly replaced (Merge_FreshChange above).
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("❤️", Now) },
            new List<MessageReaction> { Other("👍", "999") },
            Now);

        Assert.AreEqual(2, merged.Count);
        Assert.IsTrue(merged.Exists(r => r.reactorKey == "999" && r.emoji == "👍"));
        Assert.IsTrue(merged.Exists(r => r.reactorKey == OutgoingReaction.MeReactorKey && r.emoji == "❤️"));
    }

    [Test]
    public void Merge_OtherUserSameEmoji_NoOptimisticMe_NotFolded()
    {
        // Spoofing guard (T-08-11-01): with NO fresh optimistic "me", a stranger's same-emoji
        // reaction is never folded into "me" — a reaction the owner never made stays theirs.
        var merged = TelegramReactionMerge.Merge(
            null,
            new List<MessageReaction> { Other("❤", "999") },
            Now);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("999", merged[0].reactorKey);
    }

    // --- ReactionReconcileWindow: the D2-ext loaded-window decision (candidate A) ---
    // The D5 live poll re-fetches only the latest page (messages/get offset=0 limit=MessagesPerPage),
    // so a reaction changed/removed IN the Telegram app on a LOADED-but-older message is never
    // re-synced by the poll. This pure seam decides whether the loaded window spills past the latest
    // page and, if so, how many server pages it spans — driving a bounded, throttled background
    // reconcile over the older pages. Telegram-only; WhatsApp reactions flow through ReactionStore.

    [Test]
    public void NeedsWiderPass_LoadedEqualsLatestPage_False()
    {
        // Exactly one page loaded — the latest-window poll already covers it.
        Assert.IsFalse(ReactionReconcileWindow.NeedsWiderPass(50, 50));
    }

    [Test]
    public void NeedsWiderPass_LoadedExceedsLatestPage_True()
    {
        // One message past the latest page → that older message needs a wider pass.
        Assert.IsTrue(ReactionReconcileWindow.NeedsWiderPass(51, 50));
    }

    [Test]
    public void NeedsWiderPass_LoadedBelowLatestPage_False()
    {
        Assert.IsFalse(ReactionReconcileWindow.NeedsWiderPass(30, 50));
    }

    [Test]
    public void NeedsWiderPass_EmptyCache_False()
    {
        Assert.IsFalse(ReactionReconcileWindow.NeedsWiderPass(0, 50));
    }

    [Test]
    public void NeedsWiderPass_NonPositivePageSize_False()
    {
        // Guard: a zero/negative page size can never define a "wider" window.
        Assert.IsFalse(ReactionReconcileWindow.NeedsWiderPass(50, 0));
    }

    [Test]
    public void PagesToCover_ExactlyOnePage_ReturnsOne()
    {
        Assert.AreEqual(1, ReactionReconcileWindow.PagesToCover(50, 50));
    }

    [Test]
    public void PagesToCover_JustOverOnePage_ReturnsTwo()
    {
        // Ceil: 51 and a full 100 both span two server pages.
        Assert.AreEqual(2, ReactionReconcileWindow.PagesToCover(51, 50));
        Assert.AreEqual(2, ReactionReconcileWindow.PagesToCover(100, 50));
    }

    [Test]
    public void PagesToCover_JustOverTwoPages_ReturnsThree()
    {
        Assert.AreEqual(3, ReactionReconcileWindow.PagesToCover(101, 50));
    }

    [Test]
    public void PagesToCover_EmptyCache_ReturnsZero()
    {
        Assert.AreEqual(0, ReactionReconcileWindow.PagesToCover(0, 50));
    }

    [Test]
    public void PagesToCover_NonPositivePageSize_ReturnsZero()
    {
        Assert.AreEqual(0, ReactionReconcileWindow.PagesToCover(50, 0));
    }
}

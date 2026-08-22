using NUnit.Framework;

// BATCH-03: the pure, STATEFUL, injectable-clock debounce gate that coalesces a burst of rapid
// incoming fragments into ONE «Вместе» suggestions request. Reset on each incoming (Poke); fires
// EXACTLY once when the ~2.5s window settles (ShouldFire); Cancel drops a pending window on chat
// close / bot switch / same-bot chat switch / toggle-off.
//
// Because the clock is injected (synthetic float seconds), "3 rapid pokes -> 1 fire after the
// window" and "a chat switch cancels a pending window so no stale request lands in the wrong chat"
// are both EditMode-provable with NO real time. Mirrors the pure-seam NUnit style of
// DashboardRefreshGateTests / OpenChatLivePollGateTests (no scene, no MonoBehaviour) — but the gate
// is STATEFUL, so each test drives a fresh instance.
public class IncomingDebounceGateTests
{
    private const float Window = IncomingDebounceGate.WindowSeconds;

    // Disarmed by default: a brand-new gate never fires until it is Poked.
    [Test] public void DoesNotFire_WhenNeverPoked()
    {
        var gate = new IncomingDebounceGate();
        Assert.IsFalse(gate.ShouldFire(0f));
    }

    // IsArmed is the chat-open request park's decision input (SuggestionOpenRequestPolicy): armed
    // at settle means the open's own sync staged a coalesced fire, so no second request is issued.
    // It must therefore be true for EXACTLY the Poke->fire/Cancel span — a Poke that leaks armed
    // past its fire would silently swallow every later chat-open request in that chat.
    [Test] public void IsArmed_TrueOnlyBetweenPokeAndFireOrCancel()
    {
        var gate = new IncomingDebounceGate();
        Assert.IsFalse(gate.IsArmed, "a fresh gate is disarmed");

        gate.Poke(0f);
        Assert.IsTrue(gate.IsArmed, "Poke arms");
        Assert.IsFalse(gate.ShouldFire(Window - 0.01f));
        Assert.IsTrue(gate.IsArmed, "an unfired probe inside the window must NOT disarm");

        Assert.IsTrue(gate.ShouldFire(Window));
        Assert.IsFalse(gate.IsArmed, "the fire consumes the window");

        gate.Poke(10f);
        gate.Cancel();
        Assert.IsFalse(gate.IsArmed, "Cancel disarms without firing");
    }

    // One poke: silent until the window elapses, then fires EXACTLY once (disarms after firing).
    [Test] public void FiresOnce_AfterWindow_ThenDisarms()
    {
        var gate = new IncomingDebounceGate();
        gate.Poke(0f);
        Assert.IsFalse(gate.ShouldFire(Window - 0.01f), "still inside the window -> no fire");
        Assert.IsTrue(gate.ShouldFire(Window), "window settled -> fires");
        Assert.IsFalse(gate.ShouldFire(Window), "already fired -> disarmed, never fires twice");
    }

    // Three rapid pokes keep RESETTING the window: exactly ONE fire, timed off the LAST poke.
    [Test] public void ThreeRapidPokes_CoalesceToOneFire()
    {
        var gate = new IncomingDebounceGate();
        gate.Poke(0f);
        gate.Poke(0.1f);
        gate.Poke(0.2f);
        // Derived from the LAST poke + Window, never a literal: WindowSeconds is the documented
        // single tunable, and a hard-coded 2.4f would flip this assert (failing the suite for the
        // wrong reason) the moment the window is tuned below ~2.3s at e2e.
        Assert.IsFalse(gate.ShouldFire(0.2f + Window - 0.1f), "window keeps resetting off the latest poke -> not yet");
        Assert.IsTrue(gate.ShouldFire(0.2f + Window), "fires once, WindowSeconds after the LAST poke");
        Assert.IsFalse(gate.ShouldFire(0.2f + Window), "coalesced to a SINGLE fire");
    }

    // Cancel mid-window (chat close / toggle-off): the pending window never fires.
    [Test] public void Cancel_MidWindow_NeverFires()
    {
        var gate = new IncomingDebounceGate();
        gate.Poke(0f);
        gate.Cancel();
        Assert.IsFalse(gate.ShouldFire(Window + 5f), "a cancelled window must never fire");
    }

    // BLOCKER regression — models RestoreForActiveChat cancelling a pending window on a SAME-BOT
    // chat switch (chat A -> chat B). Two fragments buffer for chat A, then the switch Cancel()s the
    // window; advancing the clock well past the window yields ZERO fire (== zero IssueRequest carrying
    // chat A's _pendingIncomingText into chat B). The gate then re-arms cleanly for chat B's own poke,
    // proving there is no cross-chat carryover.
    [Test] public void BurstThenChatSwitch_CancelsPending_ThenReArmsForNewChat()
    {
        var gate = new IncomingDebounceGate();
        gate.Poke(0f);      // chat A fragment 1
        gate.Poke(0.1f);    // chat A fragment 2
        gate.Cancel();      // the same-bot chat switch drops chat A's pending window
        Assert.IsFalse(gate.ShouldFire(Window + 5f), "no stale fire lands in the newly-opened chat B");
        gate.Poke(10f);     // chat B's own incoming re-arms the gate
        Assert.IsTrue(gate.ShouldFire(10f + Window), "re-arms cleanly for chat B — no cross-chat carryover");
    }

    // After a fire the gate re-arms on the next poke (a later burst fires again).
    [Test] public void ReArms_AfterAFire()
    {
        var gate = new IncomingDebounceGate();
        gate.Poke(0f);
        Assert.IsTrue(gate.ShouldFire(Window), "first window fires");
        gate.Poke(100f);
        Assert.IsTrue(gate.ShouldFire(100f + Window), "a fresh poke after a fire re-arms the window");
    }
}

// BATCH-03 content fix: the pure burst accumulator on SuggestionsController. The WHOLE burst — not
// just the last fragment — must ride the coalesced request's lastIncomingText, because the payload's
// history snapshot re-syncs on chat fetch (not live poll) and can lag behind the burst indefinitely;
// a fragment in neither place is silently dropped from the suggestions (observed live: exec 1103
// lost «бампер на бмв х5» entirely). Same pure-seam NUnit style as the gate tests above.
public class SuggestionsBurstTextTests
{
    [Test] public void AppendBurst_FirstFragment_StartsThePending()
    {
        Assert.AreEqual("а", SuggestionsController.AppendBurst(null, "а"));
    }

    [Test] public void AppendBurst_SecondFragment_JoinsWithNewline()
    {
        Assert.AreEqual("а\nб", SuggestionsController.AppendBurst("а", "б"));
    }

    [Test] public void AppendBurst_ThreeFragments_PreserveArrivalOrder()
    {
        string pending = SuggestionsController.AppendBurst(null, "есть колодки");
        pending = SuggestionsController.AppendBurst(pending, "на камри 70");
        pending = SuggestionsController.AppendBurst(pending, "2007 года");
        Assert.AreEqual("есть колодки\nна камри 70\n2007 года", pending);
    }

    // Live-poll re-delivery guard: the SAME tail fragment arriving again must not duplicate.
    [Test] public void AppendBurst_RedeliveredTailFragment_NotDuplicated()
    {
        string pending = SuggestionsController.AppendBurst("а", "б");
        Assert.AreEqual("а\nб", SuggestionsController.AppendBurst(pending, "б"));
        Assert.AreEqual("б", SuggestionsController.AppendBurst("б", "б"), "single-line tail also guarded");
    }

    [Test] public void AppendBurst_NullOrEmptyFragment_LeavesPendingUntouched()
    {
        Assert.AreEqual("а", SuggestionsController.AppendBurst("а", null));
        Assert.AreEqual("а", SuggestionsController.AppendBurst("а", ""));
        Assert.IsNull(SuggestionsController.AppendBurst(null, null));
    }
}

// BATCH-03 run-boundary rule (the subtlest rule of the phase, previously untested — see
// 10-LEARNINGS.md). The pending burst deliberately SURVIVES a debounce fire, because a burst
// straddling the ~2.5s window fires twice and the second fire must still carry the earlier
// fragments (live exec 1168 lost a fragment when the fire cleared it). It therefore clears at
// exactly one in-batch place: an OUTGOING echo — the owner or the bot replied — which bounds the
// un-replied run. `FoldLiveBatch` is the pure, clock-free seam holding that rule.
public class SuggestionsLiveBatchFoldTests
{
    private static MessageViewModel In(string text) => new MessageViewModel { isIncoming = true, text = text };
    private static MessageViewModel Out(string text) => new MessageViewModel { isIncoming = false, text = text };

    // Timestamped variants. The fixtures above deliberately share order keys (all zero) so the
    // rules read as written; these carry real keys so the DIRECTION of the batch is expressible.
    private static MessageViewModel InAt(string text, long ts) =>
        new MessageViewModel { isIncoming = true, text = text, timestamp = ts, messageId = "in" + ts };
    private static MessageViewModel OutAt(string text, long ts) =>
        new MessageViewModel { isIncoming = false, text = text, timestamp = ts, messageId = "out" + ts };

    // A burst of incoming fragments accumulates and arms the window.
    [Test] public void IncomingFragments_AccumulateAndArm()
    {
        var fold = SuggestionsController.FoldLiveBatch(null, new[] { In("есть колодки"), In("на камри 70") });
        Assert.AreEqual("есть колодки\nна камри 70", fold.Pending);
        Assert.IsTrue(fold.Arm, "an incoming fragment must (re)start the coalesce window");
        Assert.IsFalse(fold.Cancel);
    }

    // THE RULE: a reply (owner or bot) bounds the run — pending is dropped and the window cancelled.
    [Test] public void OutgoingEcho_ClearsPendingAndCancels()
    {
        var fold = SuggestionsController.FoldLiveBatch("есть колодки", new[] { Out("Да, есть") });
        Assert.IsNull(fold.Pending, "an answered burst must not linger");
        Assert.IsFalse(fold.Arm);
        Assert.IsTrue(fold.Cancel, "a pending window must be dropped once the run is answered");
    }

    // Incoming then a reply within one batch: the reply still wins — no fire follows an answer.
    [Test] public void IncomingThenOutgoing_EndsAnswered_NoArm()
    {
        var fold = SuggestionsController.FoldLiveBatch(null, new[] { In("есть колодки"), Out("Да, есть") });
        Assert.IsNull(fold.Pending);
        Assert.IsFalse(fold.Arm, "the batch ended answered → nothing to suggest for");
        Assert.IsTrue(fold.Cancel);
    }

    // A reply followed by a NEW question starts a fresh run — the old burst must not leak into it.
    [Test] public void OutgoingThenIncoming_StartsFreshRun()
    {
        var fold = SuggestionsController.FoldLiveBatch("старый вопрос", new[] { Out("Ответил"), In("а есть дверь?") });
        Assert.AreEqual("а есть дверь?", fold.Pending, "the answered burst must not survive into the new run");
        Assert.IsTrue(fold.Arm);
        Assert.IsFalse(fold.Cancel, "a re-arm supersedes the cancel — Poke() re-arms unconditionally");
    }

    // The straddle guarantee, at fold level: with no reply between them, a later batch ADDS to the
    // pending burst rather than replacing it — so a second fire still carries the first fragment.
    [Test] public void PendingSurvivesAcrossBatches_WhenNoReplyIntervenes()
    {
        var first = SuggestionsController.FoldLiveBatch(null, new[] { In("есть колодки") });
        var second = SuggestionsController.FoldLiveBatch(first.Pending, new[] { In("на камри 70") });
        Assert.AreEqual("есть колодки\nна камри 70", second.Pending,
            "a fire between batches must not cost the earlier fragments (live exec 1168 regression)");
        Assert.IsTrue(second.Arm);
    }

    // An outgoing bounds the run regardless of its text (media replies carry no text).
    [Test] public void OutgoingWithoutText_StillBoundsTheRun()
    {
        var fold = SuggestionsController.FoldLiveBatch("вопрос", new[] { Out(null) });
        Assert.IsNull(fold.Pending);
        Assert.IsTrue(fold.Cancel);
    }

    // Live-poll re-delivery of the same tail fragment must not duplicate it.
    [Test] public void RedeliveredFragment_DoesNotDuplicate()
    {
        var fold = SuggestionsController.FoldLiveBatch("есть колодки", new[] { In("есть колодки") });
        Assert.AreEqual("есть колодки", fold.Pending);
        Assert.IsTrue(fold.Arm, "a re-delivery still counts as activity and resets the window");
    }

    // Defensive: a null batch or null entries are no-ops, never a clear.
    [Test] public void NullBatchOrEntries_LeavePendingIntact()
    {
        var nullBatch = SuggestionsController.FoldLiveBatch("вопрос", null);
        Assert.AreEqual("вопрос", nullBatch.Pending);
        Assert.IsFalse(nullBatch.Arm);
        Assert.IsFalse(nullBatch.Cancel);

        var withNulls = SuggestionsController.FoldLiveBatch("вопрос", new MessageViewModel[] { null, null });
        Assert.AreEqual("вопрос", withNulls.Pending, "null entries must never bound the run");
        Assert.IsFalse(withNulls.Arm);
        Assert.IsFalse(withNulls.Cancel);
    }

    // Audit F11: an in-flight request issued BEFORE the owner's reply must be superseded on the
    // echo (the controller bumps its seq on this flag). Cancel alone cannot carry that signal —
    // it goes false when a new question re-arms in the same batch, yet the pre-reply in-flight
    // request still answers a burst that is already handled.
    [Test] public void SawOutgoing_TrueOnAnyOutgoingEcho_EvenWhenReArmed()
    {
        Assert.IsFalse(SuggestionsController.FoldLiveBatch(null, new[] { In("вопрос") }).SawOutgoing);
        Assert.IsTrue(SuggestionsController.FoldLiveBatch("вопрос", new[] { Out("ответ") }).SawOutgoing);
        Assert.IsTrue(SuggestionsController.FoldLiveBatch(null, new[] { In("q"), Out("a") }).SawOutgoing);
        Assert.IsTrue(
            SuggestionsController.FoldLiveBatch("старый", new[] { Out("ответ"), In("новый") }).SawOutgoing,
            "a re-arm (Cancel=false) must not hide the reply from the supersede signal");
        Assert.IsFalse(SuggestionsController.FoldLiveBatch("вопрос", new MessageViewModel[] { null }).SawOutgoing);
    }

    // --- Batch DIRECTION (2026-08-20 fix) ----------------------------------
    // ChatManager builds the live batch in raw Wappi response order, which is NEWEST-FIRST, and
    // hands that list to every subscriber (MessageListView sorts its own copy; this fold did not).
    // The fold is last-wins, so direction decides the verdict — these pin that it sorts.

    // THE BUG, in its exact shape: the owner answered from the phone's WhatsApp app and the client
    // then wrote again; both land together when the chat is opened. Newest-first that reads
    // [incoming, outgoing] and used to fold to Cancel — which superseded the in-flight chat-open
    // request, latched _answeredIdle and collapsed the slot, so the panel offered nothing at all.
    [Test] public void NewestFirstBatch_OutgoingThenIncomingInTime_Arms()
    {
        var fold = SuggestionsController.FoldLiveBatch(
            null, new[] { InAt("а есть дверь?", 200), OutAt("Ответил", 100) });

        Assert.AreEqual("а есть дверь?", fold.Pending, "the client's unanswered question must survive");
        Assert.IsTrue(fold.Arm, "the run ends on the CLIENT — the window must arm");
        Assert.IsFalse(fold.Cancel, "collapsing here is what left the panel empty on chat open");
        Assert.IsTrue(fold.SawOutgoing, "the echo is still reported so in-flight requests supersede");
    }

    // The mirror: chronologically the owner answered LAST, so the run really is closed. Fed
    // newest-first this reads [outgoing, incoming] and used to arm — cards for an answered run.
    [Test] public void NewestFirstBatch_IncomingThenOutgoingInTime_Cancels()
    {
        var fold = SuggestionsController.FoldLiveBatch(
            null, new[] { OutAt("Да, есть", 200), InAt("есть колодки", 100) });

        Assert.IsNull(fold.Pending, "the owner's reply bounds the run");
        Assert.IsFalse(fold.Arm);
        Assert.IsTrue(fold.Cancel, "the run is answered — the sheet closes");
    }

    // A multi-fragment burst arriving newest-first must still compose in reading order, or
    // lastIncomingText reaches the model backwards (and the tail-based re-delivery guard misses).
    [Test] public void NewestFirstBurst_ComposesInChronologicalOrder()
    {
        var fold = SuggestionsController.FoldLiveBatch(
            null, new[] { InAt("на камри 70", 200), InAt("есть колодки", 100) });

        Assert.AreEqual("есть колодки\nна камри 70", fold.Pending);
        Assert.IsTrue(fold.Arm);
    }
}

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
        Assert.IsFalse(gate.ShouldFire(2.4f), "window keeps resetting off the latest poke -> not yet");
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

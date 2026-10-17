using NUnit.Framework;

/// <summary>
/// B2 fix rounds 3–4. D1 (owner ruling 2026-10-17): a chip lit only by a fallback — the bot's schedule is closed, or the
/// quota is spent — is not the chat's mode, so its tap hides the panel for this chat-open and records nothing; only THAT
/// no-write dismissal may be undone without a write (round 4: a tap back after a WRITTEN «Авто» is the owner's
/// «Вместе» and is written). M1: the fallback is re-read while the chat stays open (every live batch, once a minute).
/// The pure halves are tested on behaviour here; the controller's wiring is pinned in ScheduleFallbackPolicyTests.
/// </summary>
public class SuggestionFallbackPolicyTests
{
    // ── D1: the tap (wantOn = the panel ON, «Вместе»; dismissedWithoutWrite = the hide being undone wrote nothing) ──────

    [TestCase(false, false, true, false, SuggestionToggleTap.HideOnly, TestName = "Off_FallbackOnlyChip_HidesOnly")]
    [TestCase(false, false, true, true, SuggestionToggleTap.HideOnly, TestName = "Off_FallbackOnlyChip_AlreadyDismissedOnce_HidesOnly")]
    [TestCase(false, true, true, false, SuggestionToggleTap.Write, TestName = "Off_StoredTogether_WithAFallback_IsTheOwnersAuto")]
    [TestCase(false, true, true, true, SuggestionToggleTap.Write, TestName = "Off_StoredTogether_StaleLatch_IsTheOwnersAuto")]
    [TestCase(false, true, false, false, SuggestionToggleTap.Write, TestName = "Off_StoredTogether_NoFallback_IsTheOwnersAuto")]
    [TestCase(false, true, false, true, SuggestionToggleTap.Write, TestName = "Off_StoredTogether_NoFallback_StaleLatch_Writes")]
    [TestCase(false, false, false, false, SuggestionToggleTap.Write, TestName = "Off_NothingLit_TodaysPath")]
    [TestCase(false, false, false, true, SuggestionToggleTap.Write, TestName = "Off_NothingLit_StaleLatch_TodaysPath")]
    [TestCase(true, false, true, true, SuggestionToggleTap.ShowOnly, TestName = "On_UndoesANoWriteDismissal_ShowsOnly")]
    [TestCase(true, true, true, true, SuggestionToggleTap.ShowOnly, TestName = "On_NoWriteDismissal_StoredChangedMeanwhile_ShowsOnly")]
    [TestCase(true, false, true, false, SuggestionToggleTap.Write, TestName = "On_AfterAWrittenAuto_OrNoDismissal_IsTheOwnersTogether")]
    [TestCase(true, true, true, false, SuggestionToggleTap.Write, TestName = "On_StoredTogether_Writes")]
    [TestCase(true, false, false, true, SuggestionToggleTap.Write, TestName = "On_FallbackOver_StaleLatch_IsTheOwnersTogether")]
    [TestCase(true, false, false, false, SuggestionToggleTap.Write, TestName = "On_NoFallback_IsTheOwnersTogether")]
    [TestCase(true, true, false, true, SuggestionToggleTap.Write, TestName = "On_StoredTogether_NoFallback_StaleLatch_Writes")]
    [TestCase(true, true, false, false, SuggestionToggleTap.Write, TestName = "On_StoredTogether_NoFallback_Writes")]
    public void TheTap(bool wantOn, bool storedOn, bool fallbackOn, bool dismissedWithoutWrite, SuggestionToggleTap expected)
        => Assert.AreEqual(expected, SuggestionToggleTapPolicy.Decide(wantOn, storedOn, fallbackOn, dismissedWithoutWrite));

    // ── M1: the effective mode and its dismissal ────────────────────────────────────────────────────────

    [TestCase(true, false, false, true, TestName = "Effective_StoredTogether")]
    [TestCase(true, true, true, true, TestName = "Effective_StoredTogether_OutranksADismissal")]
    [TestCase(false, true, false, true, TestName = "Effective_AFallback_LightsAnAutoChat")]
    [TestCase(false, true, true, false, TestName = "Effective_ADismissedFallback_StaysHidden")]
    [TestCase(false, false, false, false, TestName = "Effective_AutoChat_NoFallback")]
    [TestCase(false, false, true, false, TestName = "Effective_ADismissalWithoutFallback_ChangesNothing")]
    public void TheEffectiveMode(bool storedOn, bool fallbackOn, bool dismissed, bool expected)
        => Assert.AreEqual(expected, FallbackRecheckPolicy.Effective(storedOn, fallbackOn, dismissed));

    [Test]
    public void ADismissal_LastsAsLongAsItsFallback()
    {
        Assert.IsTrue(FallbackRecheckPolicy.KeepDismissal(true, fallbackOn: true), "the night goes on — the panel stays hidden");
        Assert.IsFalse(FallbackRecheckPolicy.KeepDismissal(true, fallbackOn: false), "the window opened — the next night shows it again");
        Assert.IsFalse(FallbackRecheckPolicy.KeepDismissal(false, fallbackOn: true));
    }

    [Test]
    public void TheMinuteTick_IsDueEveryMinute_AndNeverBlockedByAClockThatWentBack()
    {
        Assert.IsTrue(FallbackRecheckPolicy.IsDue(10f, -1f), "never read — read now");
        Assert.IsFalse(FallbackRecheckPolicy.IsDue(10f + FallbackRecheckPolicy.IntervalSeconds - 0.01f, 10f));
        Assert.IsTrue(FallbackRecheckPolicy.IsDue(10f + FallbackRecheckPolicy.IntervalSeconds, 10f));
        Assert.IsTrue(FallbackRecheckPolicy.IsDue(5f, 10f), "a clock behind the stamp never blocks");
        Assert.AreEqual(60f, FallbackRecheckPolicy.IntervalSeconds);
    }

    // ── the round trips, on the seam ───────────────────────────────────────────────────────────────────

    [Test]
    public void RoundTrip_StoredTogether_OffThenOn_WritesBothWays()
    {
        // (a) the chat is «Вместе», a fallback holds: the owner's off-tap (past the confirm) writes «Авто», and the tap back
        // writes «Вместе» again — the written «Авто» is NOT a dismissal the second tap may silently undo.
        Assert.AreEqual(SuggestionToggleTap.Write, SuggestionToggleTapPolicy.Decide(false, storedOn: true, fallbackOn: true, dismissedWithoutWrite: false));
        Assert.AreEqual(SuggestionToggleTap.Write, SuggestionToggleTapPolicy.Decide(true, storedOn: false, fallbackOn: true, dismissedWithoutWrite: false));
    }

    [Test]
    public void RoundTrip_FallbackOnly_OffThenOn_WritesNothing()
    {
        // (b) the chat is «Авто», only a fallback lights it: hide, then show — nothing written either way.
        Assert.AreEqual(SuggestionToggleTap.HideOnly, SuggestionToggleTapPolicy.Decide(false, storedOn: false, fallbackOn: true, dismissedWithoutWrite: false));
        Assert.AreEqual(SuggestionToggleTap.ShowOnly, SuggestionToggleTapPolicy.Decide(true, storedOn: false, fallbackOn: true, dismissedWithoutWrite: true));
    }

    [Test]
    public void ADeliberateTap_BeforeTheTickSeesTheWindowClose_IsAWrite()
    {
        // (c) the window closed seconds ago, the minute tick has not caught it: the panel is still down in an «Авто» chat
        // and nothing was dismissed — the owner's tap is a deliberate «Вместе», and it is written.
        Assert.AreEqual(SuggestionToggleTap.Write, SuggestionToggleTapPolicy.Decide(true, storedOn: false, fallbackOn: true, dismissedWithoutWrite: false));
    }

    // ── the same, as the controller walks it (resolve → flip?; tap → policy → write?) ─────────────────────────

    private sealed class OpenChat
    {
        public bool Stored;
        public bool Fallback;
        public bool Dismissed;              // _fallbackDismissed
        public bool DismissedWithoutWrite;  // _dismissedWithoutWrite
        public bool PanelUp;

        /// <summary>RecheckFallback / ResolveSemiAutoForOpenChat: re-read the inputs; true when the panel flipped.</summary>
        public bool Recheck()
        {
            Dismissed = FallbackRecheckPolicy.KeepDismissal(Dismissed, Fallback);
            if (!Dismissed) DismissedWithoutWrite = false;
            bool on = FallbackRecheckPolicy.Effective(Stored, Fallback, Dismissed);
            bool flipped = on != PanelUp;
            PanelUp = on;
            return flipped;
        }

        /// <summary>A chip tap through the policy (HandleToggle); true when it wrote the chat's mode.</summary>
        public bool Tap()
        {
            bool wantOn = !PanelUp;
            switch (SuggestionToggleTapPolicy.Decide(wantOn, Stored, Fallback, DismissedWithoutWrite))
            {
                case SuggestionToggleTap.HideOnly:   // DismissFallbackForThisOpen
                    Dismissed = true;
                    DismissedWithoutWrite = true;
                    PanelUp = false;
                    return false;
                case SuggestionToggleTap.ShowOnly:   // RestoreFallbackForThisOpen
                    Dismissed = false;
                    DismissedWithoutWrite = false;
                    PanelUp = true;
                    return false;
                default:                             // the owner's decision: SemiAutoStore + the server desire
                    Stored = wantOn;
                    PanelUp = wantOn;
                    Dismissed = !wantOn;             // an off-tap also hides a fallback for this open
                    DismissedWithoutWrite = false;
                    return true;
            }
        }
    }

    [Test]
    public void ClosingEdge_ANightMessageInAnOpenAutoChat_BringsThePanel()
    {
        var chat = new OpenChat { Stored = false, Fallback = false };
        Assert.IsFalse(chat.Recheck(), "inside the window: «Авто», no panel");
        chat.Fallback = true;   // 21:00 — the window closed while the chat stayed open
        Assert.IsTrue(chat.Recheck() && chat.PanelUp, "the next live batch (or the minute tick) raises the panel");
    }

    [Test]
    public void OpeningEdge_ANightPanelGoesAway_WhenTheWindowOpens()
    {
        var chat = new OpenChat { Stored = false, Fallback = true };
        chat.Recheck();
        Assert.IsTrue(chat.PanelUp);
        chat.Fallback = false;   // 09:00
        Assert.IsTrue(chat.Recheck() && !chat.PanelUp, "the bot answers again — its panel goes");
    }

    [Test]
    public void FallbackOnly_HideThenShow_WritesNothing_AndTheTickNeverUndoesTheHide()
    {
        // (b) on the controller's walk.
        var chat = new OpenChat { Stored = false, Fallback = true };
        chat.Recheck();
        Assert.IsFalse(chat.Tap(), "hide only — nothing written");
        Assert.IsFalse(chat.Stored, "the chat is still «Авто», exactly as the owner left it");
        Assert.IsFalse(chat.Recheck() || chat.PanelUp, "a new night message / the tick must not undo the dismissal");
        Assert.IsFalse(chat.Tap(), "tapping it back: still nothing written");
        Assert.IsTrue(chat.PanelUp && !chat.Stored, "the panel is back, the chat still «Авто»");
    }

    [Test]
    public void StoredTogether_OffThenOn_WritesBothWays_AndTheOffTapHoldsForThisOpen()
    {
        // (a) on the controller's walk — the round-4 defect: the tap back used to be a silent ShowOnly.
        var chat = new OpenChat { Stored = true, Fallback = true };
        chat.Recheck();
        Assert.IsTrue(chat.Tap(), "the owner's «Авто» for this chat is recorded, as today");
        Assert.IsFalse(chat.Stored || chat.PanelUp);
        Assert.IsFalse(chat.Recheck() || chat.PanelUp, "the fallback still holding does not raise the panel back a minute later");
        Assert.IsTrue(chat.Tap(), "the tap back is the owner's «Вместе» — written, not a silent show");
        Assert.IsTrue(chat.Stored && chat.PanelUp, "stored back to «Вместе»: once the fallback ends the bot still stays out of this chat");
        chat.Fallback = false;
        Assert.IsFalse(chat.Recheck());
        Assert.IsTrue(chat.PanelUp, "after the night: «Вместе», as the owner left it");
    }

    [Test]
    public void ATapBeforeTheTickCatchesTheWindowClosing_IsWritten()
    {
        // (c) on the controller's walk: the window closed, no re-read yet — the panel is still down in the «Авто» chat.
        var chat = new OpenChat { Stored = false, Fallback = false };
        chat.Recheck();
        chat.Fallback = true;                        // 21:00:10 — the tick comes at 21:00:50
        Assert.IsTrue(chat.Tap(), "a deliberate «Вместе» — written");
        Assert.IsTrue(chat.Stored && chat.PanelUp);
        chat.Fallback = false;                       // the next morning
        Assert.IsFalse(chat.Recheck());
        Assert.IsTrue(chat.PanelUp, "still «Вместе» in the window — the owner chose it");
    }

    [Test]
    public void AfterTheWindowOpens_TheNextNightShowsThePanelAgain()
    {
        var chat = new OpenChat { Stored = false, Fallback = true };
        chat.Recheck();
        chat.Tap();                    // dismissed
        chat.Fallback = false;         // morning
        chat.Recheck();
        Assert.IsFalse(chat.DismissedWithoutWrite, "the latch goes with the dismissal it belongs to");
        chat.Fallback = true;          // the next evening, the chat still open
        Assert.IsTrue(chat.Recheck() && chat.PanelUp, "a dismissal hides ONE fallback, not every one after it");
    }
}

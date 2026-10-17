using NUnit.Framework;

/// <summary>
/// B2 fix round 3. D1 (owner ruling 2026-10-17): a chip lit only by a fallback — the bot's schedule is closed, or the
/// quota is spent — is not the chat's mode, so its tap hides the panel for this chat-open and records nothing. M1: the
/// fallback is re-read while the chat stays open (every live batch, once a minute), so the panel follows the window.
/// The pure halves are tested on behaviour here; the controller's wiring is pinned in ScheduleFallbackPolicyTests.
/// </summary>
public class SuggestionFallbackPolicyTests
{
    // ── D1: the tap ──────────────────────────────────────────────────────────────────────────────────────

    [TestCase(false, false, true, SuggestionToggleTap.HideOnly, TestName = "Tap_Off_OnAFallbackOnlyChip_HidesOnly")]
    [TestCase(true, false, true, SuggestionToggleTap.ShowOnly, TestName = "Tap_On_AfterAFallbackDismissal_ShowsOnly")]
    [TestCase(false, true, true, SuggestionToggleTap.Write, TestName = "Tap_Off_OnTheStoredTogether_IsTheOwnersDecision")]
    [TestCase(false, true, false, SuggestionToggleTap.Write, TestName = "Tap_Off_NoFallback_IsTheOwnersDecision")]
    [TestCase(true, false, false, SuggestionToggleTap.Write, TestName = "Tap_On_NoFallback_IsTheOwnersDecision")]
    [TestCase(true, true, true, SuggestionToggleTap.Write, TestName = "Tap_On_StoredTogether_WithAFallback_Writes")]
    [TestCase(true, true, false, SuggestionToggleTap.Write, TestName = "Tap_On_StoredTogether_Writes")]
    [TestCase(false, false, false, SuggestionToggleTap.Write, TestName = "Tap_Off_NothingLit_TodaysPath")]
    public void TheTap(bool desiredOn, bool storedOn, bool fallbackOn, SuggestionToggleTap expected)
        => Assert.AreEqual(expected, SuggestionToggleTapPolicy.Decide(desiredOn, storedOn, fallbackOn));

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

    // ── the edges, as the controller walks them (resolve → flip?) ───────────────────────────────────────────

    private sealed class OpenChat
    {
        public bool Stored;
        public bool Fallback;
        public bool Dismissed;
        public bool PanelUp;

        /// <summary>RecheckFallback: re-read the inputs; true when the panel flipped.</summary>
        public bool Recheck()
        {
            Dismissed = FallbackRecheckPolicy.KeepDismissal(Dismissed, Fallback);
            bool on = FallbackRecheckPolicy.Effective(Stored, Fallback, Dismissed);
            bool flipped = on != PanelUp;
            PanelUp = on;
            return flipped;
        }

        /// <summary>A chip tap through the policy; true when it wrote the chat's mode.</summary>
        public bool Tap()
        {
            bool desired = !PanelUp;
            switch (SuggestionToggleTapPolicy.Decide(desired, Stored, Fallback))
            {
                case SuggestionToggleTap.HideOnly: Dismissed = true; PanelUp = false; return false;
                case SuggestionToggleTap.ShowOnly: Dismissed = false; PanelUp = true; return false;
                default:
                    Stored = desired;
                    PanelUp = desired;
                    if (!desired) Dismissed = true;   // HandleToggle: an off-tap also hides a fallback for this open
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
    public void ADismissedNightPanel_IsNotReRaisedByTheTick_AndTheTapWroteNothing()
    {
        var chat = new OpenChat { Stored = false, Fallback = true };
        chat.Recheck();
        Assert.IsFalse(chat.Tap(), "hide only — nothing written");
        Assert.IsFalse(chat.Stored, "the chat is still «Авто», exactly as the owner left it");
        Assert.IsFalse(chat.Recheck() || chat.PanelUp, "a new night message / the tick must not undo the dismissal");
        Assert.IsFalse(chat.Tap(), "tapping it back: still nothing written");
        Assert.IsTrue(chat.PanelUp && !chat.Stored);
    }

    [Test]
    public void AfterTheWindowOpens_TheNextNightShowsThePanelAgain()
    {
        var chat = new OpenChat { Stored = false, Fallback = true };
        chat.Recheck();
        chat.Tap();                    // dismissed
        chat.Fallback = false;         // morning
        chat.Recheck();
        chat.Fallback = true;          // the next evening, the chat still open
        Assert.IsTrue(chat.Recheck() && chat.PanelUp, "a dismissal hides ONE fallback, not every one after it");
    }

    [Test]
    public void AStoredTogetherChat_KeepsTodaysTap_AndItsOffTapHoldsForThisOpen()
    {
        var chat = new OpenChat { Stored = true, Fallback = true };
        chat.Recheck();
        Assert.IsTrue(chat.Tap(), "the owner's «Авто» for this chat is recorded, as today");
        Assert.IsFalse(chat.Stored || chat.PanelUp);
        Assert.IsFalse(chat.Recheck() || chat.PanelUp, "…and the fallback still holding does not raise the panel back a minute later");
    }
}

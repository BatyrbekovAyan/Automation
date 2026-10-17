using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Part B «расписание бота»: outside the bot's hours the server answers exactly as in «Вместе» (no auto-reply), so the
/// suggestions panel must be there for an «Авто» bot too — otherwise the chat sits in silence with no panel. An
/// EFFECTIVE-mode override, like the quota fallback: the same predicate and zone as the server, nothing written.
/// </summary>
public class ScheduleFallbackPolicyTests
{
    // 2026-10-12 is a Monday. In Almaty (UTC+5): 07:00Z = 12:00, 14:00Z = 19:00, 20:00Z = 01:00 Tuesday.
    private static readonly DateTimeOffset MondayNoonAlmaty = new DateTimeOffset(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MondayEveningAlmaty = new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TuesdayNightAlmaty = new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero);

    private static readonly BotSchedule Weekdays9To18 = new BotSchedule(true, 31, 540, 1080);

    [Test]
    public void OutsideAnEnabledWindow_TheChatBehavesAsTogether()
    {
        Assert.IsTrue(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, MondayEveningAlmaty, "Asia/Almaty"));
        Assert.IsTrue(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, TuesdayNightAlmaty, "Asia/Almaty"));
    }

    [Test]
    public void InsideTheWindow_TheStoredModeStands()
        => Assert.IsFalse(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, MondayNoonAlmaty, "Asia/Almaty"));

    [Test]
    public void ADisabledSchedule_NeverFallsBack()
    {
        var disabled = new BotSchedule(false, 31, 540, 1080);
        Assert.IsFalse(ScheduleFallbackPolicy.ShouldFallBackToSemi(disabled, MondayEveningAlmaty, "Asia/Almaty"));
        Assert.IsFalse(ScheduleFallbackPolicy.ShouldFallBackToSemi(BotSchedule.Default, MondayEveningAlmaty, "Asia/Almaty"),
            "a bot without a schedule answers as today");
    }

    [Test]
    public void TheBotsZoneDecides_NotUtc()
    {
        // 07:00Z: noon in Almaty (inside), 07:00 in UTC (outside).
        Assert.IsFalse(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, MondayNoonAlmaty, "Asia/Almaty"));
        Assert.IsTrue(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, MondayNoonAlmaty, "UTC"));
        Assert.IsFalse(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, MondayNoonAlmaty, null),
            "a legacy bot without a zone is judged in Asia/Almaty, the server's default");
    }

    [Test]
    public void AnEnabledScheduleWithNoDay_IsNeverOpen()
        => Assert.IsTrue(ScheduleFallbackPolicy.ShouldFallBackToSemi(new BotSchedule(true, 0, 0, 0), MondayNoonAlmaty, "Asia/Almaty"));

    [Test]
    public void ThePanel_AsksThePolicy_AndThePolicyWritesNoMode()
    {
        string controller = Src("Scripts/Chat/SuggestionsController.cs");
        string fallback = MethodBody(controller, "private static bool FallbackSemiAutoForOpenBot()");
        Assert.IsTrue(fallback.Contains("QuotaFallbackPolicy.ShouldFallBackToSemi(UsageStore.Current)") && fallback.Contains("IsOffHoursForOpenBot()"),
            "the two fallbacks: the spent quota and the open bot's closed schedule");
        string resolve = MethodBody(controller, "private bool ResolveSemiAutoForOpenChat(");
        Assert.IsTrue(resolve.Contains("SemiAutoStore.IsOn(") && resolve.Contains("FallbackSemiAutoForOpenBot()")
                      && resolve.Contains("FallbackRecheckPolicy.Effective("), "the chat's stored mode OR a fallback the owner has not hidden");
        foreach (var write in Writes)
            Assert.IsFalse(resolve.Contains(write), $"resolving the mode writes nothing ({write})");
        string helper = MethodBody(controller, "private static bool IsOffHoursForOpenBot()");
        Assert.IsTrue(helper.Contains("ScheduleFallbackPolicy.ShouldFallBackToSemi(") && helper.Contains("BotScheduleStore.Read(")
                      && helper.Contains("BusinessTimeZone.ForBot("), "the open bot's saved schedule in the bot's own zone");

        string policy = Src("Scripts/Billing/ScheduleFallbackPolicy.cs");
        foreach (var store in new[] { "SemiAutoStore", "ReplyModeToggleBinder", "SyncReplyMode", "PlayerPrefs" })
            Assert.IsFalse(Regex.Replace(policy, @"(///|//)[^\n]*", "").Contains(store),
                $"the fallback is an EFFECTIVE mode — it must never touch {store}");
    }

    // ── fix round 3 (D1, owner ruling 2026-10-17): a chip lit only by a fallback is not the chat's mode ──────────

    [Test]
    public void AFallbackLitChip_OnlyHidesThePanel_TheTapWritesNothing()
    {
        string controller = Src("Scripts/Chat/SuggestionsController.cs");
        string toggle = MethodBody(controller, "private void HandleToggle(");
        int decide = toggle.IndexOf("SuggestionToggleTapPolicy.Decide(", StringComparison.Ordinal);
        int store = toggle.IndexOf("SemiAutoStore.Set(", StringComparison.Ordinal);
        int push = toggle.IndexOf("PushReplyModeForActiveChat(desiredOn)", StringComparison.Ordinal);
        Assert.IsTrue(decide >= 0 && store > decide && push > decide, "every tap asks the policy BEFORE anything is written");
        Assert.IsTrue(toggle.Contains("SuggestionToggleTap.HideOnly") && toggle.Contains("SuggestionToggleTap.ShowOnly"),
            "both no-write outcomes are handled");
        foreach (var method in new[] { "private void DismissFallbackForThisOpen()", "private void RestoreFallbackForThisOpen()" })
        {
            string body = MethodBody(controller, method);
            foreach (var write in Writes)
                Assert.IsFalse(body.Contains(write), $"{method} writes nothing ({write}) — the fallback is what lit the chip");
        }
        Assert.IsTrue(MethodBody(controller, "private void DismissFallbackForThisOpen()").Contains("_fallbackDismissed = true"));
        Assert.IsTrue(MethodBody(controller, "private void DismissFallbackForThisOpen()").Contains("TurnPanelOff()"),
            "the same supersede + hide as a toggle-off");
        Assert.IsTrue(controller.Contains("_toggle.OnDismissed += DismissFallbackForThisOpen")
                      && controller.Contains("_toggle.OnDismissed -= DismissFallbackForThisOpen"),
            "the chip's quiet dismiss lands on the no-write path");

        string chip = MethodBody(Src("Scripts/UI/SemiAutoToggle.cs"), "private void HandlePressed()");
        int quiet = chip.IndexOf("OnDismissed?.Invoke()", StringComparison.Ordinal);
        int gate = chip.IndexOf("AutoLockGate.TryEnableChat(", StringComparison.Ordinal);
        Assert.IsTrue(quiet >= 0 && gate > quiet,
            "a fallback-only chip skips the learning gate and the confirm — nothing is being enabled, nothing is written");
        Assert.IsTrue(chip.Substring(0, quiet).Contains("_fallbackOnly"), "...and only a chip lit by a fallback alone");
        string lit = MethodBody(Src("Scripts/UI/SemiAutoToggle.cs"), "public void SetLit(");
        Assert.IsTrue(lit.Contains("_fallbackOnly = on && fallbackOnly"),
            "every paint states the flag, and an unlit chip never carries it — a stale flag would turn the owner's real «Авто» into a no-op");
    }

    // ── fix round 3 (M1): the fallback follows the clock and the quota while the chat stays open ─────────────────

    [Test]
    public void TheFallback_IsReEvaluatedLive_OnEveryBatch_AndOnceAMinute()
    {
        string controller = Src("Scripts/Chat/SuggestionsController.cs");
        string live = MethodBody(controller, "private void HandleLive(");
        int recheck = live.IndexOf("RecheckFallback();", StringComparison.Ordinal);
        int semi = live.IndexOf("if (!_semiAutoOn) return;", StringComparison.Ordinal);
        Assert.IsTrue(recheck >= 0 && semi > recheck,
            "a night message in an open «Авто» chat must reach the panel — re-evaluate BEFORE the SEMI-03 return");

        string update = MethodBody(controller, "void Update()");
        Assert.IsTrue(Regex.IsMatch(update, @"if \(FallbackRecheckPolicy\.IsDue\(Time\.unscaledTime, _fallbackCheckedAt\)\)\s*RecheckFallback\(\);"),
            "the minute tick — the window opens and closes with no message at all");
        Assert.Less(update.IndexOf("RecheckFallback();", StringComparison.Ordinal),
                    update.IndexOf("if (_keyboardMover == null) return;", StringComparison.Ordinal), "ahead of Update's early returns");

        string body = MethodBody(controller, "private void RecheckFallback()");
        Assert.IsTrue(body.Contains("ResolveSemiAutoForOpenChat(") && body.Contains("ShowPanel(claimSlotFromKeyboard: false)")
                      && body.Contains("TurnPanelOff()"), "a flip runs the server reconcile's show / hide path");
        Assert.IsTrue(body.Contains("_pendingOpenRequest = true"), "a panel that appears with no new message still gets its set");
        Assert.IsTrue(body.Contains("_chatResolved"), "never against a chat that was not resolved (a bot switch leaves the old chat id sticky)");
        foreach (var write in Writes)
            Assert.IsFalse(body.Contains(write), $"re-evaluating writes nothing ({write})");

        string restore = MethodBody(controller, "private void RestoreForActiveChat()");
        Assert.IsTrue(restore.Contains("_fallbackDismissed = false") && restore.Contains("_chatResolved = true"),
            "a dismissal lasts one chat-open; the open chat is resolved");
        Assert.IsTrue(MethodBody(controller, "private void ResetForNoOpenChat()").Contains("_chatResolved = false"));
        Assert.IsTrue(MethodBody(controller, "void OnDisable()").Contains("_chatResolved = false"));
    }

    // Every way the client records a reply mode — none of them may run off a fallback.
    private static readonly string[] Writes =
    {
        "SemiAutoStore.Set(", "PushReplyModeForActiveChat(", "SyncReplyMode(", "ReassertReplyMode", "ReplyModeToggleBinder.CommitMode(",
    };

    private static string Src(string relative) => File.ReadAllText(Path.Combine(Application.dataPath, relative));

    /// <summary>The member's body from its signature: a brace-matched block, or an expression body up to its `;`.</summary>
    private static string MethodBody(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.GreaterOrEqual(at, 0, $"{signature} not found");
        int open = source.IndexOf('{', at);
        int arrow = source.IndexOf("=>", at, StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open)) return source.Substring(arrow, source.IndexOf(';', arrow) - arrow + 1);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"{signature}: unbalanced braces");
        return null;
    }
}

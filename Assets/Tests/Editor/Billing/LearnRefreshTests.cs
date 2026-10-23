using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Learning percent, spec 2026-10-22 §4.1 «Обновление во время работы». The «Авто» chips paint a percent that comes from
/// Get Usage, and the app reads that only at boot and on a few screens — so while the owner answers clients in a chat
/// the percent would stand still. <see cref="LearnRefresh"/> re-reads it ~5 s after a SUCCESSFUL send that carried a
/// shown suggestion set, at most once a minute for the whole app — and a burst of answers inside the minute gets ONE
/// trailing re-read when the minute ends (final review 2026-10-23), so the last answers of the burst still show.
///
/// The throttle is a pure rule and is pinned by value (the ChannelHealth.ShouldRun pattern): <see cref="LearnRefresh.ShouldFetch"/>
/// and <see cref="LearnRefresh.Decide"/>. Everything else acts only where the call sites put it, and EditMode can run neither
/// the send coroutine nor a request against the server, so the wiring is pinned at the source (the SendPathWiringTests
/// idiom): brace-match the method, assert on its body, comments stripped so a sentence that names a call can never stand
/// in for the call.
/// </summary>
public class LearnRefreshTests
{
    private const long T = 1_700_000_000_000L;   // an arbitrary «now», Unix ms

    private const string RefreshFile = "Scripts/Billing/LearnRefresh.cs";
    private const string ChatManagerFile = "Scripts/Main/ChatManager.cs";

    // The trigger's WHOLE condition: an inverted kind (== None), an `||` or a dropped null check is a different rule.
    private static readonly Regex LearningSendCondition =
        new Regex(@"^\s*origin\s*!=\s*null\s*&&\s*origin\.kind\s*!=\s*SendOriginKind\.None\s*$");

    // --- the rule ----------------------------------------------------------------------------------------------------

    [TestCase(0L, T, true, TestName = "ShouldFetch_AtMostOncePerMinute: never read before")]
    [TestCase(T, T + 59_999L, false, TestName = "ShouldFetch_AtMostOncePerMinute: within a minute — no")]
    [TestCase(T, T + 60_000L, true, TestName = "ShouldFetch_AtMostOncePerMinute: a minute later — yes")]
    [TestCase(T, T - 1L, true, TestName = "ShouldFetch_AtMostOncePerMinute: the clock moved back — never blocks")]
    public void ShouldFetch_AtMostOncePerMinute(long lastMs, long nowMs, bool expected)
        => Assert.AreEqual(expected, LearnRefresh.ShouldFetch(nowMs, lastMs));

    [TestCase(0L, T, 0L, LearnRefreshStep.FetchNow, 0L,
        TestName = "Decide: never read before — fetch now")]
    [TestCase(T, T + 30_000L, 0L, LearnRefreshStep.ScheduleTrailing, T + 60_000L,
        TestName = "Decide: inside the minute, nothing pending — one trailing read when the minute ends")]
    [TestCase(T, T + 40_000L, T + 60_000L, LearnRefreshStep.Skip, 0L,
        TestName = "Decide: inside the minute, the trailing read already pending — nothing more")]
    [TestCase(T, T + 60_000L, 0L, LearnRefreshStep.FetchNow, 0L,
        TestName = "Decide: a minute later — fetch now")]
    [TestCase(T, T - 1L, 0L, LearnRefreshStep.FetchNow, 0L,
        TestName = "Decide: the clock moved back — fetch now, never blocks")]
    [TestCase(T, T - 1L, T + 60_000L, LearnRefreshStep.FetchNow, 0L,
        TestName = "Decide: the clock moved back past a pending read — fetch now (a due more than a minute ahead is not a pending read)")]
    [TestCase(T, T + 65_000L, T + 60_000L, LearnRefreshStep.Skip, 0L,
        TestName = "Decide: the minute ended, the trailing read is late but within its grace — still pending")]
    [TestCase(T, T + 70_000L, T + 60_000L, LearnRefreshStep.Skip, 0L,
        TestName = "Decide: the trailing read at the very end of its grace — still pending")]
    [TestCase(T, T + 70_001L, T + 60_000L, LearnRefreshStep.FetchNow, 0L,
        TestName = "Decide: the trailing read's coroutine was lost (due + 2 x delay passed) — fetch now, it never blocks forever")]
    public void Decide_OneReadAMinute_AndOneTrailingReadForTheRest(long lastMs, long nowMs, long trailingDueMs,
        LearnRefreshStep step, long dueMs)
    {
        (LearnRefreshStep gotStep, long gotDue) = LearnRefresh.Decide(nowMs, lastMs, trailingDueMs);
        Assert.AreEqual(step, gotStep);
        Assert.AreEqual(dueMs, gotDue, "the trailing read is due the moment the minute ends; the other steps carry no due time");
    }

    [Test]
    public void TheTrailingReadsGrace_IsTwoDelays()
        => Assert.AreEqual(10_000L, LearnRefresh.TrailingGraceMs, "a due time older than due + 2 × DelaySeconds is a lost read, not a pending one");

    // --- the wiring (source guards) -----------------------------------------------------------------------------------

    [Test]
    public void TheSendAck_RefreshesOnlyAfterASendWithAShownSet()
    {
        string post = MethodBody(Source(ChatManagerFile), "PostTextMessageRoutine");

        Assert.AreEqual(1, Regex.Matches(post, @"LearnRefresh\.AfterLearningSend\(\)").Count,
            "one trigger in the text send — the shared success branch, which a Telegram reply and a retry from the outbox come through too");

        var guarded = Regex.Match(post, @"if\s*\((?<condition>[^;{}]*)\)\s*\{?\s*LearnRefresh\.AfterLearningSend\(\)\s*;");
        Assert.IsTrue(guarded.Success, "the trigger must sit directly under an `if (…)`");
        string condition = guarded.Groups["condition"].Value;
        Assert.IsTrue(LearningSendCondition.IsMatch(condition),
            "only a send that carried a shown set (a kind other than None) is new learning data, and origin defaults to null in this " +
            $"method (a send without a snapshot), so the kind is read only behind that check — got `{condition.Trim()}`");

        int done = post.IndexOf("response.status == \"done\"", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(done, 0, "the success condition moved — re-derive where the trigger belongs");
        string success = BlockFrom(post, post.IndexOf('{', done));
        StringAssert.Contains("LearnRefresh.AfterLearningSend()", success,
            "…and only on the SUCCESSFUL ack: a failed send taught the server nothing");
    }

    [TestCase("origin != null && origin.kind != SendOriginKind.None", true, TestName = "TriggerCondition: the rule itself")]
    [TestCase("origin != null && origin.kind == SendOriginKind.None", false, TestName = "TriggerCondition: an inverted kind fails the guard")]
    [TestCase("origin != null || origin.kind != SendOriginKind.None", false, TestName = "TriggerCondition: an || fails the guard")]
    [TestCase("origin.kind != SendOriginKind.None", false, TestName = "TriggerCondition: a dropped null check fails the guard")]
    [TestCase("origin?.kind != SendOriginKind.None", false, TestName = "TriggerCondition: a null origin read as «not None» fails the guard")]
    public void TheTriggerConditionGuard_AcceptsTheRule_AndRefusesItsMutants(string condition, bool accepted)
        => Assert.AreEqual(accepted, LearningSendCondition.IsMatch(condition));

    [Test]
    public void TheRefresh_IsHostedOnTheManager_AndUsesTheUsageClient()
    {
        string source = Source(RefreshFile);

        // Whole identifiers: «Manager.Instance» is a substring of «ChatManager.Instance», the very host to avoid.
        Assert.IsTrue(Regex.IsMatch(source, @"\bUsageClient\.FetchRoutine\(\)"),
            "the one entry point that applies a snapshot (UsageStore.Apply) and keeps single-flight");
        Assert.AreEqual(1, Regex.Matches(source, @"\bStartCoroutine\(").Count);
        Assert.IsTrue(Regex.IsMatch(source, @"\bManager\.Instance\.StartCoroutine\("),
            "hosted on the always-active Manager, never on a component of the chat screen: Unity kills a coroutine whose host goes inactive");
    }

    [Test]
    public void AfterLearningSend_GuardsTheManager_ThenDecides_ThenStampsAndSchedules_ThenStarts()
    {
        string body = MethodBody(Source(RefreshFile), "AfterLearningSend");

        Match manager = Regex.Match(body, @"\bManager\.Instance\s*==\s*null");
        Match decide = Regex.Match(body, @"\bDecide\(\s*now\s*,\s*_lastScheduledMs\s*,\s*_trailingDueMs\s*\)");
        Match skip = Regex.Match(body, @"if\s*\(\s*step\s*==\s*LearnRefreshStep\.Skip\s*\)\s*return\s*;");
        Match stamp = Regex.Match(body, @"if\s*\(\s*step\s*==\s*LearnRefreshStep\.FetchNow\s*\)\s*_lastScheduledMs\s*=\s*now\s*;");
        Match pending = Regex.Match(body, @"\b_trailingDueMs\s*=\s*dueMs\s*;");
        Match start = Regex.Match(body, @"\bStartCoroutine\(\s*ReadAfter\(\s*dueMs\s*\)\s*\)");

        Assert.IsTrue(manager.Success, "Manager.Instance is null in EditMode and early in boot — guarded");
        Assert.IsTrue(decide.Success && decide.Index > manager.Index,
            "…BEFORE the decision: with no host nothing starts, and the minute must not be spent on it");
        Assert.IsTrue(skip.Success && skip.Index > decide.Index,
            "a pending trailing read already covers this send — nothing more may start (a second read inside the minute)");
        Assert.IsTrue(stamp.Success && stamp.Index > skip.Index,
            "a read now stamps the minute — without the stamp ShouldFetch(now, 0) is true forever");
        Assert.IsTrue(pending.Success && pending.Index > skip.Index,
            "the trailing read's due time is recorded, so the next send in the minute skips (a read now records 0)");
        Assert.IsTrue(start.Success && start.Index > stamp.Index && start.Index > pending.Index,
            "…and both are set before the coroutine starts");
    }

    [Test]
    public void TheTrailingRead_WaitsOutTheMinute_FiresOnlyIfStillPending_StampsTheThrottle_ThenFetches()
    {
        string body = MethodBody(Source(RefreshFile), "ReadAfter");

        Match minute = Regex.Match(body, @"WaitForSecondsRealtime\([^;]*\btrailingDueMs\b");
        Match stillPending = Regex.Match(body, @"if\s*\(\s*_trailingDueMs\s*!=\s*trailingDueMs\s*\)\s*yield\s+break\s*;");
        Match clear = Regex.Match(body, @"\b_trailingDueMs\s*=\s*0\s*;");
        Match stamp = Regex.Match(body, @"\b_lastScheduledMs\s*=\s*NowMs\(\)\s*;");
        Match delay = Regex.Match(body, @"WaitForSecondsRealtime\(\s*DelaySeconds\s*\)");
        Match fetch = Regex.Match(body, @"\bUsageClient\.FetchRoutine\(\)");

        Assert.IsTrue(minute.Success, "a trailing read first waits for the minute to end (its due time)");
        Assert.IsTrue(stillPending.Success && stillPending.Index > minute.Index,
            "…then fires only if it is still the pending read: one that outlived its grace was replaced by a read now");
        Assert.IsTrue(clear.Success && clear.Index > stillPending.Index, "firing clears the pending read");
        Assert.IsTrue(stamp.Success && stamp.Index > stillPending.Index,
            "…and stamps the throttle like any read, so the next one is a minute away: still at most one Get Usage a minute");
        Assert.IsTrue(delay.Success && delay.Index > stamp.Index && delay.Index > clear.Index,
            "then the same pause as every read — the server records the send just after the ack");
        Assert.IsTrue(fetch.Success && fetch.Index > delay.Index, "…and the one entry point that applies the snapshot");
    }

    [Test]
    public void TheRequest_GoesOutOnlyAfterTheServerHadTimeToRecordTheSend()
    {
        Assert.AreEqual(5f, LearnRefresh.DelaySeconds, "spec §4.1: about 5 s");

        string source = Source(RefreshFile);
        int wait = source.IndexOf("WaitForSecondsRealtime(DelaySeconds)", System.StringComparison.Ordinal);
        int fetch = source.IndexOf("UsageClient.FetchRoutine()", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(wait, 0, "the pause is gone");
        Assert.Greater(fetch, wait,
            "the server records the send just after the ack: asking at once would read the snapshot WITHOUT it");
        Assert.IsFalse(Regex.IsMatch(source, @"\bnew\s+WaitForSeconds\s*\("),
            "every wait is a WaitForSecondsRealtime — the codebase's idiom; a scaled wait stretches with Time.timeScale");
    }

    // --- source helpers ----------------------------------------------------------------------------------------------

    /// <summary>The file with its // comments cut away — a comment can name a call without making it.</summary>
    private static string Source(string relativePath)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"{relativePath} moved — update LearnRefreshTests");
        return Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
    }

    /// <summary>
    /// The declaration's block — never a call site: «yield return Name(» and «else Name();» have the declaration's shape
    /// (a word, a space, the name, a parenthesis), so the return type must not be a statement keyword.
    /// </summary>
    private static string MethodBody(string source, string method)
    {
        var declaration = Regex.Match(source,
            @"^[ \t]*(?:(?:public|private|internal|protected|static)[ \t]+)*(?!(?:return|else|new|throw|await|yield)\b)[\w<>.]+[ \t]+" +
            Regex.Escape(method) + @"[ \t]*\(",
            RegexOptions.Multiline);
        Assert.IsTrue(declaration.Success, $"declaration of {method} not found");

        // The first of «{», «=>», «;» after the name decides the shape: an expression-bodied member has no block, and
        // searching on for a «{» would silently hand back the NEXT method's body.
        string shape = Regex.Match(source.Substring(declaration.Index), @"\{|=>|;").Value;
        Assert.AreEqual("{", shape, $"{method} must keep a block body — this guard reads the block");
        return BlockFrom(source, source.IndexOf('{', declaration.Index));
    }

    private static string BlockFrom(string source, int open)
    {
        Assert.GreaterOrEqual(open, 0, "no opening brace");
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        Assert.Fail("unbalanced braces");
        return null;
    }
}

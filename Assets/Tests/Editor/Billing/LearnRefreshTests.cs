using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Learning percent, spec 2026-10-22 §4.1 «Обновление во время работы». The «Авто» chips paint a percent that comes from
/// Get Usage, and the app reads that only at boot and on a few screens — so while the owner answers clients in a chat
/// the percent would stand still. <see cref="LearnRefresh"/> re-reads it ~5 s after a SUCCESSFUL send that carried a
/// shown suggestion set, at most once a minute for the whole app.
///
/// The throttle is a pure rule and is pinned by value (the ChannelHealth.ShouldRun pattern). Everything else acts only
/// where the call sites put it, and EditMode can run neither the send coroutine nor a request against the server, so
/// the wiring is pinned at the source (the SendPathWiringTests idiom): brace-match the method, assert on its body,
/// comments stripped so a sentence that names a call can never stand in for the call.
/// </summary>
public class LearnRefreshTests
{
    private const long T = 1_700_000_000_000L;   // an arbitrary «now», Unix ms

    private const string RefreshFile = "Scripts/Billing/LearnRefresh.cs";
    private const string ChatManagerFile = "Scripts/Main/ChatManager.cs";

    // --- the rule ----------------------------------------------------------------------------------------------------

    [TestCase(0L, T, true, TestName = "ShouldFetch_AtMostOncePerMinute: never read before")]
    [TestCase(T, T + 59_999L, false, TestName = "ShouldFetch_AtMostOncePerMinute: within a minute — no")]
    [TestCase(T, T + 60_000L, true, TestName = "ShouldFetch_AtMostOncePerMinute: a minute later — yes")]
    [TestCase(T, T - 1L, true, TestName = "ShouldFetch_AtMostOncePerMinute: the clock moved back — never blocks")]
    public void ShouldFetch_AtMostOncePerMinute(long lastMs, long nowMs, bool expected)
        => Assert.AreEqual(expected, LearnRefresh.ShouldFetch(nowMs, lastMs));

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
        Assert.IsTrue(Regex.IsMatch(condition, @"\bSendOriginKind\.None\b"),
            "only a send that carried a shown set (a kind other than None) is new learning data");
        Assert.IsTrue(Regex.IsMatch(condition, @"\borigin\s*!=\s*null\b"),
            "origin defaults to null in this method (a send without a snapshot) — the kind is read only behind that check");

        int done = post.IndexOf("response.status == \"done\"", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(done, 0, "the success condition moved — re-derive where the trigger belongs");
        string success = BlockFrom(post, post.IndexOf('{', done));
        StringAssert.Contains("LearnRefresh.AfterLearningSend()", success,
            "…and only on the SUCCESSFUL ack: a failed send taught the server nothing");
    }

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
    public void AfterLearningSend_GuardsTheManager_ThenThrottles_ThenStamps_ThenStarts()
    {
        string body = MethodBody(Source(RefreshFile), "AfterLearningSend");

        Match manager = Regex.Match(body, @"\bManager\.Instance\s*==\s*null");
        Match throttle = Regex.Match(body, @"\bShouldFetch\(");
        Match stamp = Regex.Match(body, @"\b_lastScheduledMs\s*=\s*now\b");
        Match start = Regex.Match(body, @"\bStartCoroutine\(");

        Assert.IsTrue(manager.Success, "Manager.Instance is null in EditMode and early in boot — guarded");
        Assert.IsTrue(throttle.Success && throttle.Index > manager.Index,
            "…BEFORE the throttle: with no host nothing starts, and the minute must not be spent on it");
        Assert.IsTrue(stamp.Success && stamp.Index > throttle.Index,
            "the throttle only works if the check is followed by the stamp — without it ShouldFetch(now, 0) is true forever");
        Assert.IsTrue(start.Success && start.Index > stamp.Index, "…and the stamp is set before the coroutine starts");
    }

    [Test]
    public void TheRequest_GoesOutOnlyAfterTheServerHadTimeToRecordTheSend()
    {
        Assert.AreEqual(5f, LearnRefresh.DelaySeconds, "spec §4.1: about 5 s");

        string source = Source(RefreshFile);
        int wait = source.IndexOf("WaitForSeconds(DelaySeconds)", System.StringComparison.Ordinal);
        int fetch = source.IndexOf("UsageClient.FetchRoutine()", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(wait, 0, "the pause is gone");
        Assert.Greater(fetch, wait,
            "the server records the send just after the ack: asking at once would read the snapshot WITHOUT it");
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

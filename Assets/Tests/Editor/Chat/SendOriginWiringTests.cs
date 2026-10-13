using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Source-invariant guard for learning A (the SendPathWiringTests pattern). The seams are pure and
/// tested, but they act only where the call sites put them: the tap must snapshot (not count), the
/// send must resolve BEFORE any yield and ride the proxy call, and the preference counter must move
/// to the successful send. Revert any one site and every seam test stays green while the data lies.
/// </summary>
public class SendOriginWiringTests
{
    [Test]
    public void Tap_Snapshots_AndNoLongerCounts()
    {
        string tap = MethodBody("Scripts/Chat/SuggestionsController.cs", "HandleCardTapped");
        Assert.IsTrue(tap.Contains("SendOriginTracker.SetPick("), "a tap must record the picked card for the send");
        Assert.IsFalse(tap.Contains("RecordPick("), "a tap is not a preference — the counter moved to the successful send (learning A)");
        string src = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Chat/SuggestionsController.cs"));
        Assert.IsFalse(Regex.IsMatch(src, @"void\s+RecordPick\s*\("), "RecordPick lives in SuggestionPickStats now");
    }

    [Test]
    public void ShownSets_AreSnapshotted_AndAFreshRoundClears()
    {
        Assert.IsTrue(MethodBody("Scripts/Chat/SuggestionsController.cs", "OnResult").Contains("SendOriginTracker.SetShown("), "a rendered set must be snapshotted");
        Assert.IsTrue(MethodBody("Scripts/Chat/SuggestionsController.cs", "StartFreshRound").Contains("SendOriginTracker.Clear("), "a fresh round starts a new turn");
        Assert.IsTrue(MethodBody("Scripts/Chat/SuggestionsController.cs", "HandleToggle").Contains("SendOriginTracker.Clear("), "toggle-OFF hides the set — it can no longer explain a send");
        Assert.IsTrue(MethodBody("Scripts/Chat/SuggestionsController.cs", "HandleReplyModeServerNewer").Contains("SendOriginTracker.Clear("), "a server-side switch to «Авто» hides the set too");
    }

    [Test]
    public void Send_ResolvesBeforeOptimisticEcho_RidesTheProxyCall_AndCountsOnSuccess()
    {
        string send = MethodBody("Scripts/Main/ChatManager.cs", "SendTextMessageRoutine");
        int resolveAt = send.IndexOf("SendOriginTracker.ResolveAndClear(");
        int echoAt    = send.IndexOf("OnLiveMessagesReceived?.Invoke(");
        int yieldAt   = send.IndexOf("yield return");
        Assert.IsTrue(resolveAt >= 0, "the send must classify its origin");
        Assert.IsTrue(echoAt >= 0, "SendTextMessageRoutine no longer announces the optimistic bubble — re-derive where the snapshot is cleared");
        Assert.IsTrue(resolveAt < echoAt, "classify BEFORE the optimistic bubble is announced — SuggestionsController.HandleLive folds it as an answered run and StartFreshRound clears the snapshot SYNCHRONOUSLY, in the same call stack");
        Assert.IsTrue(yieldAt < 0 || resolveAt < yieldAt, "…and before the first yield (the poll's own echo is the second, later reset)");
        Assert.IsTrue(Regex.IsMatch(send, @"originJson\s*="), "the resolved origin must be persisted in the outbox entry so a retry carries it");   // the initializer aligns its `=` columns
        string post = MethodBody("Scripts/Main/ChatManager.cs", "PostTextMessageRoutine");
        Assert.IsTrue(post.Contains("SendOriginWire.Append("), "origin_* must ride the same WappiProxy.Send args");
        Assert.IsTrue(post.Contains("chat_id = ChatIdFormat.Recipient(chatId)"), "a Telegram reply must name its chat in meta (the client sends no recipient there)");
        Assert.IsTrue(post.Contains("SuggestionPickStats.RecordFromOrigin("), "the preference counter fires on the SUCCESSFUL send");
        Assert.IsTrue(post.IndexOf("SuggestionPickStats.RecordFromOrigin(") > post.IndexOf("response.status == \"done\""), "…and only inside the success branch");
        string retry = MethodBody("Scripts/Main/ChatManager.Outbox.cs", "RetryRoutine");
        Assert.IsTrue(retry.Contains("retryOrigin"), "a retry must carry the origin saved at the first attempt (it is never re-resolved: the snapshot was consumed)");
    }

    // ---- source helpers (SendPathWiringTests) --------------------------------------------

    private static string MethodBody(string relativePath, string method)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"{relativePath} moved — update SendOriginWiringTests");
        string source = File.ReadAllText(path);
        var decl = Regex.Match(source,
            @"^[ \t]*(?:(?:public|private|internal|protected|static)[ \t]+)*(?!return\b)[\w<>.]+[ \t]+" +
            Regex.Escape(method) + @"[ \t]*\(",
            RegexOptions.Multiline);
        Assert.IsTrue(decl.Success, $"{method} not found in {relativePath}");
        int open = source.IndexOf('{', decl.Index);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"unbalanced braces after {method}");
        return null;
    }
}

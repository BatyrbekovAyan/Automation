using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Source-invariant guard (the TailOutlineShaderTests pattern) for the 2026-09-04 dedupe fix.
///
/// The bug was LIST IDENTITY: five send-path sites read-modify-wrote a DETACHED
/// <c>ChatHistoryCache.LoadHistory</c> list, so the optimistic bubble never reached
/// <c>ChatManager._activeChatCache</c> — the list the live poll's echo-reconcile searches — and
/// the echo rendered as a second bubble. OutgoingSendCacheTests pins the seam, but the seam
/// operates on whatever list it is handed: revert ONE call site to a bare LoadHistory and every
/// seam test stays green while the duplicate returns on device. EditMode cannot run the live
/// poll, so the wiring is pinned at the source instead: each of the five methods must obtain
/// its list through <c>LiveCacheFor</c> and write it back through <c>PersistSendCache</c> (the
/// delete-safe writer), and must not touch ChatHistoryCache directly.
/// </summary>
public class SendPathWiringTests
{
    private static readonly (string file, string method)[] SendSites =
    {
        ("Scripts/Main/ChatManager.cs",           "SendTextMessageRoutine"),
        ("Scripts/Main/ChatManager.cs",           "PostTextMessageRoutine"),
        ("Scripts/Main/ChatManager.MediaSend.cs", "StageLocalMedia"),
        ("Scripts/Main/ChatManager.MediaSend.cs", "PostMediaMessageRoutine"),
        ("Scripts/Main/ChatManager.MediaSend.cs", "CancelMediaSend"),
    };

    [Test]
    public void EverySendSite_ReadsTheLiveListAndWritesThroughTheDeleteSafePersister()
    {
        foreach (var (file, method) in SendSites)
        {
            string body = MethodBody(file, method);

            Assert.IsTrue(body.Contains("LiveCacheFor("),
                $"{method} no longer resolves its list through LiveCacheFor — a detached LoadHistory list " +
                "is invisible to ReconcileGhostSend and the echo renders as a duplicate bubble.");
            Assert.IsTrue(body.Contains("PersistSendCache("),
                $"{method} no longer writes through PersistSendCache — a late ack after «Удалить чат» " +
                "would rewrite the deleted chat's history to disk.");
            Assert.IsFalse(body.Contains("ChatHistoryCache.LoadHistory("),
                $"{method} loads a detached list from ChatHistoryCache — the 2026-09-04 duplicate-bubble wiring.");
            Assert.IsFalse(body.Contains("ChatHistoryCache.SaveHistory("),
                $"{method} writes ChatHistoryCache directly, bypassing the deleted-chat guard.");
        }
    }

    [Test]
    public void LiveCacheFor_DelegatesToThePurePredicate()
    {
        string body = MethodBody("Scripts/Main/ChatManager.Outbox.cs", "LiveCacheFor");
        Assert.IsTrue(body.Contains("OutgoingSendCache.UsesLiveList("),
            "LiveCacheFor's guard must be the tested predicate, not a private re-derivation of it");
    }

    [Test]
    public void GhostReconcile_GoesThroughTheSeam()
    {
        string body = MethodBody("Scripts/Main/ChatManager.cs", "ReconcileGhostSend");
        Assert.IsTrue(body.Contains("OutgoingSendCache.AdoptServerId("),
            "ReconcileGhostSend swaps the id with its own loop — the seam's echo test then pins code the echo never runs");
    }

    // ---- source helpers -----------------------------------------------------------------

    private static string MethodBody(string relativePath, string method)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"{relativePath} moved — update SendPathWiringTests");
        string source = File.ReadAllText(path);

        // The declaration: a return type, the name, an argument list — not a call site.
        // Modifiers, then ANY return type (IEnumerator, void, bool, List<…>), then the name and
        // its parameter list. `(?!return\b)` keeps a `yield return Name(` call site from matching.
        var decl = Regex.Match(source,
            @"^[ \t]*(?:(?:public|private|internal|protected|static)[ \t]+)*(?!return\b)[\w<>.]+[ \t]+" +
            Regex.Escape(method) + @"[ \t]*\(",
            RegexOptions.Multiline);
        Assert.IsTrue(decl.Success, $"declaration of {method} not found in {relativePath}");

        int open = source.IndexOf('{', decl.Index);
        Assert.Greater(open, 0, $"no body after {method}");

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"unbalanced braces after {method}");
        return null;
    }
}

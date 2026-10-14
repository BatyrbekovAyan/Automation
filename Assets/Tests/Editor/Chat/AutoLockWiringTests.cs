using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Learning B source guard: every path that turns «Авто» ON for the owner goes through AutoLockGate.TryEnable BEFORE it
/// commits — the card capsule, the chats header + bot-switcher row (RequestEnableAuto), the per-chat toggle. Disabling and
/// the server reconciliation stay outside the gate. EditMode cannot tap a capsule, so the wiring is pinned by text.
/// </summary>
public class AutoLockWiringTests
{
    [Test]
    public void TheCapsule_TheHeaderPaths_AndThePerChatToggle_GoThroughTheGate_BeforeTheyCommit()
    {
        string card = MethodBody("Scripts/Main/Bot.cs", "OnAutoPillTapped");
        Assert.GreaterOrEqual(card.IndexOf("AutoLockGate.TryEnable("), 0, "card capsule");
        Assert.Greater(card.IndexOf("ReplyMode.Auto"), card.IndexOf("AutoLockGate.TryEnable("), "card capsule: the gate, then CommitMode(Auto)");

        string header = MethodBody("Scripts/UI/ReplyModeToggleBinder.cs", "RequestEnableAuto");
        Assert.GreaterOrEqual(header.IndexOf("AutoLockGate.TryEnable("), 0, "chats header + switcher row share RequestEnableAuto");
        Assert.Greater(header.IndexOf("ReplyMode.Auto"), header.IndexOf("AutoLockGate.TryEnable("), "header: the gate, then CommitMode(Auto)");

        string chat = MethodBody("Scripts/UI/SemiAutoToggle.cs", "HandlePressed");
        Assert.GreaterOrEqual(chat.IndexOf("AutoLockGate.TryEnableChat("), 0, "per-chat toggle goes through the chat door of the gate");
        Assert.IsTrue(chat.Contains("ReplyModeToggleBinder.GetMode("), "the chat door needs the bot's own mode: a resume inside «Авто» is never locked (spec §0)");
        Assert.Greater(chat.IndexOf("OnToggled?.Invoke(false)"), chat.IndexOf("AutoLockGate.TryEnableChat("), "per-chat: the gate, then the enable");
    }

    [Test]
    public void DisablingAndServerReconcile_StayOutsideTheGate()
    {
        Assert.IsFalse(MethodBody("Scripts/UI/ReplyModeToggleBinder.cs", "DisableAuto").Contains("AutoLockGate"), "switching back to «Вместе» is instant");
        Assert.IsFalse(MethodBody("Scripts/UI/ReplyModeToggleBinder.cs", "ReconcileFromServer").Contains("AutoLockGate"), "a server-side truth is not the owner's tap");
    }

    [Test]
    public void OnlyTheGateAndTheCard_ReadTheReadinessStore()
    {
        var users = new List<string>();
        foreach (string path in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string file = Path.GetFileName(path);
            if (file == "AutoLockGate.cs" || file == "Bot.cs" || file == "AutoReadiness.cs") continue;
            if (File.ReadAllText(path).Contains("AutoReadinessStore.Current(")) users.Add(file);
        }
        Assert.IsEmpty(users, "only the gate (the lock) and the card (its face) read readiness: " + string.Join(", ", users));
    }

    /// <summary>
    /// Review 2026-10-14 #6: the «one door» claim, pinned globally — every CommitMode(…, ReplyMode.Auto) statement in the
    /// client lives inside one of the two gated bodies, AFTER its AutoLockGate call. A new enable path anywhere else fails here.
    /// (ReconcileFromServer commits a VARIABLE mode — a server truth, not the owner's tap — and never names ReplyMode.Auto.)
    /// </summary>
    [Test]
    public void EveryCommitModeAuto_LivesInsideAGatedBody_AfterTheGateCall()
    {
        var allowed = new List<(string file, string method)> { ("Scripts/Main/Bot.cs", "OnAutoPillTapped"), ("Scripts/UI/ReplyModeToggleBinder.cs", "RequestEnableAuto") };
        var offenders = new List<string>();
        foreach (string path in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(path);
            string relative = path.Substring(Application.dataPath.Length + 1).Replace('\\', '/');
            foreach (Match call in Regex.Matches(source, @"CommitMode\s*\("))
            {
                int end = source.IndexOf(';', call.Index);
                string statement = source.Substring(call.Index, (end < 0 ? source.Length : end) - call.Index);
                if (!statement.Contains("ReplyMode.Auto")) continue;
                bool inside = false;
                foreach (var (file, method) in allowed)
                {
                    if (file != relative) continue;
                    var (open, close) = MethodRange(source, method, relative);
                    int gate = source.IndexOf("AutoLockGate.TryEnable", open, System.StringComparison.Ordinal);
                    inside |= call.Index > open && call.Index < close && gate >= 0 && gate < call.Index;
                }
                if (!inside) offenders.Add($"{relative}@{call.Index}: {statement.Trim()}");
            }
        }
        Assert.IsEmpty(offenders, "an enable of «Авто» outside the gate: " + string.Join(" | ", offenders));
    }

    /// <summary>The method's body: a brace-matched block, or — for an expression-bodied member — its `=> …;` expression.</summary>
    private static string MethodBody(string relativePath, string method)
    {
        string path = Path.Combine(Application.dataPath, relativePath);
        Assert.IsTrue(File.Exists(path), $"{relativePath} moved — update AutoLockWiringTests");
        string source = File.ReadAllText(path);
        var (open, close) = MethodRange(source, method, relativePath);
        return source.Substring(open, close - open + 1);
    }

    /// <summary>[open, close] indices of the method's body in <paramref name="source"/> (the `=>` and the `;` for an expression body).</summary>
    private static (int open, int close) MethodRange(string source, string method, string relativePath)
    {
        var decl = Regex.Match(source,
            @"^[ \t]*(?:(?:public|private|internal|protected|static)[ \t]+)+(?!return\b)[\w<>.]+[ \t]+" + Regex.Escape(method) + @"[ \t]*\(",
            RegexOptions.Multiline);
        Assert.IsTrue(decl.Success, $"{method} not found in {relativePath}");
        int open = source.IndexOf('{', decl.Index);
        int arrow = source.IndexOf("=>", decl.Index, System.StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open))
            return (arrow, source.IndexOf(';', arrow));
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return (open, i);
        }
        Assert.Fail($"unbalanced braces after {method}");
        return (open, source.Length - 1);
    }
}

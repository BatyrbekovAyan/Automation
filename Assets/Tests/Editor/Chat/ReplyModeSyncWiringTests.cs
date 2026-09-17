using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Source-invariant guard for A2 (audit stage 2): every reply-mode write travels through the
/// persisted, confirmable queue, and the one blocking site (workflow activation) waits for the
/// server's CONFIRMATION, never for a bare HTTP 200.
/// </summary>
public class ReplyModeSyncWiringTests
{
    private static string Src(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, rel));

    private static string MethodBody(string text, string method)
    {
        int i = text.IndexOf(method + "(", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(i, 0, $"{method} not found");
        int open = text.IndexOf('{', i);
        int depth = 0;
        for (int k = open; k < text.Length; k++)
        {
            if (text[k] == '{') depth++;
            else if (text[k] == '}' && --depth == 0) return text.Substring(open, k - open + 1);
        }
        Assert.Fail($"{method}: unbalanced braces"); return null;
    }

    [Test]
    public void SaveWorkflows_WaitsForTheServersConfirmation_BeforeActivating()
    {
        string body = MethodBody(Src("Scripts/Main/Manager.cs"), "IEnumerator SaveWorkflows");
        Assert.IsTrue(body.Contains("ReplyModeSyncService.Instance.SendAndConfirm("), "the activation gate must yield on SendAndConfirm (per-profile confirmation), not on a fire-and-forget write");
        Assert.IsFalse(body.Contains("SyncReplyModeRoutine("), "the old HTTP-200-only routine must be gone");
        int gate = body.IndexOf("SendAndConfirm(", System.StringComparison.Ordinal);
        int enable = body.IndexOf("EnableWhatsappWorkflow(", System.StringComparison.Ordinal);
        Assert.IsTrue(gate >= 0 && (enable < 0 || gate < enable), "confirmation must precede the workflow activation calls");
    }

    [Test]
    public void EveryWriteSite_GoesThroughTheQueue()
    {
        string sync = Src("Scripts/Main/Manager.ReplyModeSync.cs");
        Assert.IsTrue(MethodBody(sync, "void SyncReplyMode").Contains("ReplyModeSyncService.Instance?.Desire("), "SyncReplyMode must record a desire in the queue");
        Assert.IsFalse(sync.Contains("SyncReplyModeRoutine"), "no direct write path may remain");
        Assert.IsTrue(MethodBody(sync, "void OnBotReplyModeChanged").Contains("ReplyModeToggleBinder.ReconcilingFromServer"), "a server-reconciled value must not become a new desire");
        string sc = Src("Scripts/Chat/SuggestionsController.cs");
        Assert.IsTrue(MethodBody(sc, "void PushReplyModeForActiveChat").Contains("Manager.Instance.SyncReplyMode(cm.CurrentBotId"), "the per-chat push must name its bot so the queue can scope pending state");
        Assert.IsTrue(sc.Contains("ReplyModeSyncService.OnServerNewer += HandleReplyModeServerNewer"), "the controller must adopt a newer server value for the open chat");
        Assert.IsFalse(Regex.IsMatch(Src("Scripts/Main/Manager.cs") + sc, @"/webhook/SetReplyMode"), "only ReplyModeSyncService may address the SetReplyMode webhook");
        Assert.IsTrue(Src("Scripts/Chat/ReplyModeSyncService.cs").Contains("/webhook/SetReplyMode"));
    }

    [Test]
    public void CreateForms_CarryTheInitialReplyMode_ForTheServerSeed()
    {
        string m = Src("Scripts/Main/Manager.cs");
        Assert.AreEqual(4, Regex.Matches(m, @"AddReplyModeFields\(form").Count, "all four Create* forms (WA/TG × start/edit) must state the mode");
        string helper = MethodBody(m, "void AddReplyModeFields");
        Assert.IsTrue(helper.Contains("\"ReplyMode\"") && helper.Contains("\"ReplyModeVersion\"") && helper.Contains("ReplyModeSyncQueue.NextVersion("));
    }

    [Test]
    public void Service_ConfirmsOnlyOnThePerProfileReport()
    {
        string svc = MethodBody(Src("Scripts/Chat/ReplyModeSyncService.cs"), "IEnumerator SendBatch");
        Assert.IsTrue(svc.Contains("ReplyModeSyncResponse.TryParse(") && svc.Contains("res.Success"), "a 200 without a success:true envelope must not confirm");
        Assert.IsTrue(svc.Contains("ReplyModeSyncQueue.OnResponse(e, sentVersion, row.Applied, row.Suppressed, row.Version)"), "confirmation is per profile, by the sent version");
        Assert.IsTrue(svc.Contains("www.responseCode == 400 || www.responseCode == 403") && svc.Contains("OnRejected("), "definitive refusals stop retrying");
        Assert.IsTrue(svc.Contains("OnSendFailed(e, now)"), "transient failures back off and retry");
    }
}

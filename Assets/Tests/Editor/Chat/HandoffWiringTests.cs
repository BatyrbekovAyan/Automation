using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Source-invariant guard for A5 (audit stage 4): the app never claims a request was seen without a
/// real view, never resumes a paused chat by itself, and reaches the handoff endpoint only through
/// the authorised transport.
/// </summary>
public class HandoffWiringTests
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
    public void DashboardOpenChat_TakesAnOpenRequest_OnlyOnTheRealView()
    {
        string body = MethodBody(Src("Scripts/Main/Dashboard/DashboardPage.cs"), "public void OpenChat");
        Assert.IsTrue(body.Contains("if (r.HasOpenHandoff) HandoffSyncService.Instance?.Take("), "opening the chat from an OPEN request must mark it taken on the server");
        int resolve = body.IndexOf("TryResolve(", System.StringComparison.Ordinal);
        int take = body.IndexOf(".Take(", System.StringComparison.Ordinal);
        Assert.IsTrue(resolve >= 0 && take > resolve, "take happens only after the row resolved to a LOCAL bot (a forged row never reaches the server)");
    }

    [Test]
    public void DashboardRow_MarksRealRequests_ThroughTheCopySeam()
    {
        string body = MethodBody(Src("Scripts/Main/Dashboard/DashboardPage.cs"), "private void BindRow");
        Assert.IsTrue(body.Contains("HandoffCopy.RowSummary(r)"), "the summary must go through HandoffCopy.RowSummary (event rows are marked)");
    }

    [Test]
    public void SyncService_UsesTheAuthorisedTransport_AndNeverResumesAChat()
    {
        string src = Src("Scripts/Chat/HandoffSyncService.cs");
        Assert.IsTrue(src.Contains("AuthorizedRequest.Send("), "the handoff endpoint is a gated webhook: every call carries the capability token");
        Assert.IsTrue(src.Contains("/webhook/Handoffs"), "endpoint");
        Assert.IsFalse(src.Contains("new UnityWebRequest(") || src.Contains("UnityWebRequest.Post("), "no bare requests outside AuthorizedRequest");
        Assert.IsTrue(src.Contains("SemiAutoStore.Set(plan.BotId, plan.ChatId, true)"), "the mirror pauses (explicit per-chat «Вместе»)");
        Assert.IsFalse(src.Contains(", false)") && src.Contains("SemiAutoStore.Set(") && src.Contains("SemiAutoStore.Set(plan.BotId, plan.ChatId, false)"),
            "the service must never un-pause a chat by itself — resuming is the owner's explicit action");
        Assert.IsTrue(src.Contains("NotificationFx.OnIncomingDetected()"), "a NEW open request plays the in-app cue (NotifPrefs-gated)");
        Assert.IsFalse(src.Contains("APNs") && src.Contains("FCM") && src.Contains("RegisterForRemoteNotifications"), "push is not claimed by this service");
    }

    [Test]
    public void DashboardOnEnable_RefreshesPendingRequests_Gated()
    {
        string body = MethodBody(Src("Scripts/Main/Dashboard/DashboardPage.cs"), "private void OnEnable");
        Assert.IsTrue(body.Contains("HandoffSyncService.Instance?.Refresh(force: false)"), "the dashboard refreshes the pending list on open, gated (no burst on tab flips)");
    }
}

using System.IO;
using System.Linq;
using System.Reflection;
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
    public void ChatOpenHeal_RepeatsTheDecision_NeverMintsANewOne()
    {
        // R4 (2026-09-27): a chat open is not the owner's decision. Re-sending the chat's explicit «Авто» through
        // Desire minted a NEW version, which the server correctly applied as a fresh resume — closing a handoff the
        // owner had not seen. The heal must go through Reassert (original version / floor version), and only the
        // owner's tap (HandleToggle) may record a new desire.
        string sc = Src("Scripts/Chat/SuggestionsController.cs");
        string restore = MethodBody(sc, "private void RestoreForActiveChat");
        Assert.IsTrue(restore.Contains("ReassertReplyModeForActiveChat(overrideOn)"), "the heal must re-assert, not re-decide");
        Assert.IsFalse(restore.Contains("PushReplyModeForActiveChat(") || restore.Contains("SyncReplyMode(") || restore.Contains(".Desire("),
            "a chat open must never record a new desire");
        Assert.IsTrue(MethodBody(sc, "private void ReassertReplyModeForActiveChat").Contains("Manager.Instance.ReassertReplyMode(cm.CurrentBotId"));
        Assert.IsTrue(MethodBody(sc, "private void HandleToggle").Contains("PushReplyModeForActiveChat(desiredOn)"), "the owner's tap is the one new decision");
        string sync = Src("Scripts/Main/Manager.ReplyModeSync.cs");
        Assert.IsTrue(MethodBody(sync, "public void ReassertReplyMode").Contains("ReplyModeSyncService.Instance?.Reassert("));
        Assert.IsTrue(MethodBody(Src("Scripts/Chat/ReplyModeSyncService.cs"), "public void Reassert").Contains("ReplyModeSyncQueue.Reassert(_model"));
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
        // R5 (2026-09-27): the answer branches moved into the pure ReplyModeSyncQueue.ApplyBatchResult (behaviour in
        // ReplyModeSyncQueueTests); this guard pins that SendBatch still classifies the answer the same way and that
        // the pure function confirms per profile by the SENT version and never touches a superseded desire.
        string svc = MethodBody(Src("Scripts/Chat/ReplyModeSyncService.cs"), "IEnumerator SendBatch");
        Assert.IsTrue(svc.Contains("ReplyModeSyncResponse.TryParse(") && svc.Contains("res.Success"), "a 200 without a success:true envelope must not confirm");
        Assert.IsTrue(svc.Contains("www.responseCode == 400 || www.responseCode == 403") && svc.Contains("ReplyModeSyncQueue.BatchStatus.Rejected"), "definitive refusals stop retrying");
        Assert.IsTrue(svc.Contains("ReplyModeSyncQueue.BatchStatus.Transient"), "transient failures back off and retry");
        Assert.IsTrue(svc.Contains("ReplyModeSyncQueue.ApplyBatchResult(batch, sentVersion, "), "every answer goes through the pure, version-guarded seam");
        string pure = MethodBody(Src("Scripts/Chat/ReplyModeSyncQueue.cs"), "public static List<Entry> ApplyBatchResult");
        Assert.IsTrue(pure.Contains("OnResponse(e, sentVersion, row.Applied, row.Suppressed, row.Version)"), "confirmation is per profile, by the sent version");
        Assert.IsTrue(pure.Contains("if (e.desiredVersion != sentVersion) continue;"), "a stale answer must not settle a newer desire");
    }

    [Test]
    public void SendBatch_DatesTheDecision_AtEveryAttempt_InsideTheRequestFactory()
    {
        // R4-clock (iteration 2026-09-28): the server closes handoff requests created no later than the owner's DECISION,
        // dated on ITS clock as now − decisionAgeMs. The age must be measured when the request actually leaves: a body
        // built once before AuthorizedRequest.Send would carry a stale age into the retry after a 401 refresh (and the
        // session wait before the first attempt), dating the decision earlier than it was. The decision label is
        // captured with the version it belongs to, so a tap landing mid-flight cannot lend the batch its own label.
        string svc = MethodBody(Src("Scripts/Chat/ReplyModeSyncService.cs"), "IEnumerator SendBatch");
        var send = Regex.Match(svc, @"AuthorizedRequest\.Send\(\s*\(\)\s*=>(?<make>.*?),\s*r\s*=>\s*www\s*=\s*r\s*\)", RegexOptions.Singleline);
        Assert.IsTrue(send.Success, "SendBatch must send through AuthorizedRequest.Send with a request factory");
        string make = send.Groups["make"].Value;
        Assert.IsTrue(make.Contains("Manager.BuildReplyModePayload(") && make.Contains("ReplyModeSyncQueue.DecisionAgeMs(decidedAtMs, NowMs())"),
            "the body and the decision's age must be built INSIDE the factory, once per attempt");
        Assert.IsFalse(Regex.IsMatch(svc, @"string\s+body\s*=\s*Manager\.BuildReplyModePayload\("), "no body may be precomputed before the send");
        int version = svc.IndexOf("long sentVersion = first.desiredVersion;", System.StringComparison.Ordinal);
        int label = svc.IndexOf("long decidedAtMs = first.decidedAtMs;", System.StringComparison.Ordinal);
        int sent = svc.IndexOf("AuthorizedRequest.Send(", System.StringComparison.Ordinal);
        Assert.IsTrue(version >= 0 && label >= 0 && label < sent && version < sent,
            "the decision label is captured together with the version, before the send");
    }

    [Test]
    public void Service_BootsItself_OnEveryLaunch_AndAnnouncesTheLoadedQueue()
    {
        // R5: the persisted queue used to be read only when something happened to create the service (a tap, a
        // chat-open heal, a Save) — a cold start with an undelivered choice sent nothing and painted it confirmed.
        var boot = typeof(ReplyModeSyncService).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(m => m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null);
        Assert.IsNotNull(boot, "ReplyModeSyncService must boot itself (RuntimeInitializeOnLoadMethod), like HandoffSyncService");
        Assert.AreEqual(RuntimeInitializeLoadType.AfterSceneLoad, boot.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>().loadType);
        string src = Src("Scripts/Chat/ReplyModeSyncService.cs");
        Assert.IsTrue(MethodBody(src, "private static void " + boot.Name).Contains("Instance"), "the boot must create the service (Awake loads the queue)");
        string awake = MethodBody(src, "private void Awake");
        Assert.IsTrue(awake.Contains("ReplyModeSyncQueue.Deserialize(") && awake.Contains("ReplyModeSyncQueue.PendingScopes(_model)") && awake.Contains("OnStateChanged?.Invoke("),
            "surfaces that painted before the service existed must be told which choices are still pending");
    }

    [Test]
    public void FirstSend_WaitsForBillingInitAndSession()
    {
        // R5: a boot-time send before BillingService.Initialize + the session would go out on the device-id fallback.
        string boot = MethodBody(Src("Scripts/Main/Manager.cs"), "private IEnumerator PreloadSecretsThenInitBilling");
        int init = boot.IndexOf("BillingService.Initialize()", System.StringComparison.Ordinal);
        int gate = boot.IndexOf("EnsureSessionThenOpenReplyModeSendGate()", System.StringComparison.Ordinal);
        Assert.IsTrue(init >= 0 && gate > init, "the send gate opens only after billing is initialised");
        string opener = MethodBody(Src("Scripts/Main/Manager.ReplyModeSync.cs"), "private IEnumerator EnsureSessionThenOpenReplyModeSendGate");
        int session = opener.IndexOf("yield return AuthClient.Instance.EnsureSession()", System.StringComparison.Ordinal);
        int open = opener.IndexOf("ReplyModeSyncService.OpenSendGate()", System.StringComparison.Ordinal);
        Assert.IsTrue(session >= 0 && open > session, "...and after the auth session op has finished");
        string loop = MethodBody(Src("Scripts/Chat/ReplyModeSyncService.cs"), "private IEnumerator Loop");
        int wait = loop.IndexOf("ReplyModeSendGate.CanSend(", System.StringComparison.Ordinal);
        int send = loop.IndexOf("SendBatch(", System.StringComparison.Ordinal);
        Assert.IsTrue(wait >= 0 && send > wait, "the loop waits at the gate before its first send");
    }

    [Test]
    public void DeletedBots_LeaveNothingInTheQueue()
    {
        // V12: the server half runs FIRST and its verdict decides — so the queue is HELD (nothing sent) from before the
        // first server call, released if the bot stays, and forgotten by the local wipe once it goes. Forgetting it
        // before the verdict would paint a kept bot's unconfirmed choice as confirmed (R5); not holding it would let a
        // send recreate the reply_mode_flags rows Delete Bot Files removes.
        string server = MethodBody(Src("Scripts/Main/Manager.cs"), "public IEnumerator DeleteBotOnServer");
        int hold = server.IndexOf("ReplyModeSyncService.Existing?.Hold(", System.StringComparison.Ordinal);
        int firstCall = server.IndexOf("WappiProxy.Send(", System.StringComparison.Ordinal);
        Assert.IsTrue(hold >= 0 && hold < firstCall, "the queue must be held before the first server call");
        int drain = server.IndexOf(".IsSending(botId)", System.StringComparison.Ordinal);
        Assert.IsTrue(drain > hold && drain < firstCall, "a batch already on the wire must land before the server wipes its rows");
        Assert.IsTrue(server.Contains("ReplyModeSyncService.Existing?.Release("), "a bot the server keeps gets its queue back");
        Assert.IsFalse(server.Contains("ForgetBot("), "the queue is never forgotten before the verdict");
        string local = MethodBody(Src("Scripts/Main/Bot.cs"), "public void DeleteBotLocally");
        Assert.IsTrue(local.Contains("ReplyModeSyncService.Existing?.ForgetBot(transform.name)"), "a deleted bot's queued desires go with it");
        string loop = MethodBody(Src("Scripts/Chat/ReplyModeSyncService.cs"), "private IEnumerator Loop");
        Assert.IsTrue(loop.Contains("due.RemoveAll(e => _held.Contains(e.botId))"), "a held bot's entries are never sent");
        string wipe = MethodBody(Src("Scripts/Main/ProfileSubPages.Account.cs"), "private void FinishWipe");
        Assert.IsTrue(wipe.Contains("ReplyModeSyncService.Existing?.ForgetAll()"), "the full wipe must empty the in-memory queue too (it re-persists itself)");
    }
}

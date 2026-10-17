using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Part B «расписание бота»: the owner's schedule reaches the server through a persisted, version-guarded queue — the
/// ReplyModeSyncService pattern with one desire per bot. The pure half (<see cref="ScheduleSyncQueue"/>,
/// <see cref="ScheduleSyncResponse"/>, <see cref="SetScheduleRequest"/>) is tested on behaviour; the MonoBehaviour half
/// (boot, gate, transport, lifecycle hooks) is pinned at the source — EditMode cannot send a request.
/// </summary>
public class ScheduleSyncServiceTests
{
    private static readonly BotSchedule Weekdays = new BotSchedule(true, 31, 540, 1080);
    private static readonly BotSchedule Night = new BotSchedule(true, 127, 1320, 360);

    private static ScheduleSyncQueue.Model M() => new ScheduleSyncQueue.Model();

    private static List<ScheduleSyncResponse.Row> Rows(params (string id, bool applied, long version)[] rows)
        => rows.Select(r => new ScheduleSyncResponse.Row { ProfileId = r.id, Applied = r.applied, Version = r.version }).ToList();

    private static string Src(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, rel));

    private static string MethodBody(string text, string signature)
    {
        int i = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.GreaterOrEqual(i, 0, $"{signature} not found");
        int open = text.IndexOf('{', i);
        int depth = 0;
        for (int k = open; k < text.Length; k++)
        {
            if (text[k] == '{') depth++;
            else if (text[k] == '}' && --depth == 0) return text.Substring(open, k - open + 1);
        }
        Assert.Fail($"{signature}: unbalanced braces");
        return null;
    }

    // ── versions and desires ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public void NextVersion_IsStrictlyIncreasing_EvenWhenTheClockStallsOrStepsBack()
    {
        Assert.AreEqual(1000, ScheduleSyncQueue.NextVersion(1000, 0));
        Assert.AreEqual(1001, ScheduleSyncQueue.NextVersion(1000, 1000), "same ms → +1");
        Assert.AreEqual(2001, ScheduleSyncQueue.NextVersion(500, 2000), "clock stepped back → still above the last");
    }

    [Test]
    public void Desire_KeepsOneEntryPerBot_ANewerDesireReplacesTheQueuedOne()
    {
        var m = M();
        long v1 = ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        long v2 = ScheduleSyncQueue.Desire(m, "Bot0", Night, "Europe/Moscow", 1000);   // a second Save in the same ms
        Assert.AreEqual(1, m.entries.Count, "one desire per bot");
        Assert.Greater(v2, v1);
        var e = ScheduleSyncQueue.Find(m, "Bot0");
        Assert.AreEqual(Night, e.Schedule, "the newer desire's values win");
        Assert.AreEqual("Europe/Moscow", e.timeZone);
        Assert.AreEqual(v2, e.version);
        Assert.IsTrue(e.IsPending);
        Assert.AreEqual(0, e.attempts);

        ScheduleSyncQueue.Desire(m, "Bot1", Weekdays, "Asia/Almaty", 1000);
        Assert.AreEqual(2, m.entries.Count, "bots never share a desire");
        Assert.AreEqual(2, ScheduleSyncQueue.Due(m, 1000).Count);
    }

    [Test]
    public void Answered_EveryProfileReported_SettlesTheDesire()
    {
        var m = M();
        long v = ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        var e = ScheduleSyncQueue.Find(m, "Bot0");
        ScheduleSyncQueue.ApplyResult(e, v, ScheduleSyncQueue.BatchStatus.Answered, new[] { "wa", "tg" },
            Rows(("wa", true, v), ("tg", true, v)), 2000);
        Assert.AreEqual(ScheduleSyncQueue.StateDone, e.state);
        Assert.AreEqual(0, ScheduleSyncQueue.Due(m, 99_999).Count, "a settled desire is never sent again");
        Assert.IsFalse(ScheduleSyncQueue.AnyPending(m));
    }

    [Test]
    public void NotApplied_IsSuperseded_NoRetryNoError_AndTheNextDesireBeatsTheServersVersion()
    {
        var m = M();
        long v = ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        var e = ScheduleSyncQueue.Find(m, "Bot0");
        long newer = v + 50_000;
        ScheduleSyncQueue.ApplyResult(e, v, ScheduleSyncQueue.BatchStatus.Answered, new[] { "wa" }, Rows(("wa", false, newer)), 2000);
        Assert.AreEqual(ScheduleSyncQueue.StateDone, e.state, "applied:false = the server holds a NEWER version — settled, not failed");
        Assert.AreEqual(Weekdays, e.Schedule, "the local values are kept (multi-device is not supported in v1)");
        long next = ScheduleSyncQueue.Desire(m, "Bot0", Night, "Asia/Almaty", 3000);
        Assert.Greater(next, newer, "a later Save must not be superseded by the same server row again");
    }

    [Test]
    public void AProfileTheServerDidNotReport_IsRetried()
    {
        var m = M();
        long v = ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        var e = ScheduleSyncQueue.Find(m, "Bot0");
        ScheduleSyncQueue.ApplyResult(e, v, ScheduleSyncQueue.BatchStatus.Answered, new[] { "wa", "tg" }, Rows(("wa", true, v)), 2000);
        Assert.IsTrue(e.IsPending);
        Assert.AreEqual(1, e.attempts);
        Assert.AreEqual(2000 + ScheduleSyncQueue.BackoffMs(0), e.nextAttemptAtMs);
    }

    [Test]
    public void Rejected_StopsRetrying_ThisLaunch_ThenTheNextLaunchTriesAgain()
    {
        var m = M();
        long v = ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        var e = ScheduleSyncQueue.Find(m, "Bot0");
        ScheduleSyncQueue.ApplyResult(e, v, ScheduleSyncQueue.BatchStatus.Rejected, new[] { "wa" }, null, 2000);
        Assert.AreEqual(ScheduleSyncQueue.StateFailed, e.state);
        Assert.AreEqual(0, ScheduleSyncQueue.Due(m, long.MaxValue).Count, "a refusal is never retried in the same launch");
        Assert.IsFalse(ScheduleSyncQueue.AnyPending(m));

        var reloaded = ScheduleSyncQueue.Deserialize(ScheduleSyncQueue.Serialize(m), 5000);
        var again = ScheduleSyncQueue.Find(reloaded, "Bot0");
        Assert.IsTrue(again.IsPending && again.attempts == 0 && again.nextAttemptAtMs == 5000,
            "the ReplyModeSyncQueue rule: a parked desire is retried on the next launch");
        Assert.AreEqual(v, again.version, "...with the version it was minted with");
    }

    [Test]
    public void Transient_BacksOff_Boundedly_ThenParks()
    {
        var m = M();
        long v = ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        var e = ScheduleSyncQueue.Find(m, "Bot0");
        long now = 1000;
        ScheduleSyncQueue.ApplyResult(e, v, ScheduleSyncQueue.BatchStatus.Transient, new[] { "wa" }, null, now);
        Assert.AreEqual(now + 2000, e.nextAttemptAtMs);
        Assert.AreEqual(0, ScheduleSyncQueue.Due(m, now + 1999).Count);
        Assert.AreEqual(1, ScheduleSyncQueue.Due(m, now + 2000).Count);
        for (int i = 1; i < ScheduleSyncQueue.MaxAttemptsPerLaunch; i++)
            ScheduleSyncQueue.ApplyResult(e, v, ScheduleSyncQueue.BatchStatus.Transient, new[] { "wa" }, null, now);
        Assert.AreEqual(ScheduleSyncQueue.StateFailed, e.state, "bounded per launch");
        Assert.AreEqual(300_000, ScheduleSyncQueue.BackoffMs(10), "the longest backoff is five minutes");
    }

    [Test]
    public void AnAnswerForAnOlderVersion_NeverTouchesTheNewerDesire()
    {
        var m = M();
        long v1 = ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        long v2 = ScheduleSyncQueue.Desire(m, "Bot0", Night, "Asia/Almaty", 2000);   // the owner saved again while v1 was in flight
        var e = ScheduleSyncQueue.Find(m, "Bot0");
        foreach (var status in new[] { ScheduleSyncQueue.BatchStatus.Answered, ScheduleSyncQueue.BatchStatus.Rejected, ScheduleSyncQueue.BatchStatus.Transient })
        {
            ScheduleSyncQueue.ApplyResult(e, v1, status, new[] { "wa" }, Rows(("wa", true, v1)), 3000);
            Assert.IsTrue(e.IsPending && e.version == v2 && e.attempts == 0 && e.nextAttemptAtMs == 2000,
                $"a stale {status} must leave the unsent v2 due");
        }
        Assert.AreEqual(Night, e.Schedule);
    }

    [Test]
    public void NothingToSend_SettlesTheDesire_UntilTheNextSaveOrCreate()
    {
        var m = M();
        ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        var e = ScheduleSyncQueue.Find(m, "Bot0");
        ScheduleSyncQueue.OnNothingToSend(e);
        Assert.AreEqual(ScheduleSyncQueue.StateDone, e.state);
        Assert.AreEqual(0, ScheduleSyncQueue.Due(m, 99_999).Count);
        ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 2000);
        Assert.AreEqual(1, ScheduleSyncQueue.Due(m, 2000).Count, "a later desire re-arms it");
    }

    [Test]
    public void AHeldBot_SendsNothing_UntilReleased_OtherBotsAreUnaffected()
    {
        var m = M();
        ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        ScheduleSyncQueue.Desire(m, "Bot1", Night, "Asia/Almaty", 1000);
        var held = new HashSet<string> { "Bot0" };   // Manager.DeleteBotOnServer is deleting Bot0
        CollectionAssert.AreEqual(new[] { "Bot1" }, ScheduleSyncQueue.Due(m, 5000, held).Select(e => e.botId).ToArray(),
            "a held bot's desire never leaves — it would land after Delete Bot Files and recreate its bot_schedule rows");
        var bot0 = ScheduleSyncQueue.Find(m, "Bot0");
        Assert.IsTrue(bot0.IsPending && bot0.attempts == 0, "held, not failed: untouched until the server's verdict");

        held.Add("Bot1");
        Assert.IsFalse(ScheduleSyncQueue.AnyPending(m, held), "only held desires left: the loop rests until a Release re-kicks it");
        Assert.IsTrue(ScheduleSyncQueue.AnyPending(m), "…they are still owed");

        held.Remove("Bot0");                         // the server kept Bot0 (retry / refused): Release
        CollectionAssert.AreEqual(new[] { "Bot0" }, ScheduleSyncQueue.Due(m, 5000, held).Select(e => e.botId).ToArray(),
            "a released bot's desire goes out after all");
        Assert.AreEqual(Weekdays, ScheduleSyncQueue.Due(m, 5000, held)[0].Schedule, "with the values the owner saved");
    }

    [Test]
    public void Sendable_OnlyRealProfilesTheTokenLists_Deduplicated_AtMostTen()
    {
        var owned = new HashSet<string> { "wa", "tg", "x" };
        CollectionAssert.AreEqual(new[] { "wa", "tg" },
            ScheduleSyncQueue.Sendable(new[] { "wa", "-1", "", null, "tg", "wa", "gone" }, owned.Contains));
        CollectionAssert.IsEmpty(ScheduleSyncQueue.Sendable(new[] { "-1", "-1" }, _ => true), "a never-paired bot sends nothing");
        CollectionAssert.IsEmpty(ScheduleSyncQueue.Sendable(new[] { "retired" }, owned.Contains),
            "a retired channel is not in the token — the gate would refuse the whole request");
        var many = Enumerable.Range(0, 15).Select(i => "p" + i).ToList();
        Assert.AreEqual(ScheduleSyncQueue.MaxProfilesPerRequest, ScheduleSyncQueue.Sendable(many, _ => true).Count);
        Assert.AreEqual(10, ScheduleSyncQueue.MaxProfilesPerRequest, "the server's limit");
    }

    [Test]
    public void Persistence_SurvivesARestart_PendingResumes_DoneStaysDone()
    {
        var m = M();
        long v = ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        ScheduleSyncQueue.Desire(m, "Bot1", Night, "Europe/Moscow", 1000);
        ScheduleSyncQueue.ApplyResult(ScheduleSyncQueue.Find(m, "Bot0"), v, ScheduleSyncQueue.BatchStatus.Answered,
            new[] { "wa" }, Rows(("wa", true, v)), 1500);
        var bot1 = ScheduleSyncQueue.Find(m, "Bot1");
        ScheduleSyncQueue.OnSendFailed(bot1, 1500);

        var reloaded = ScheduleSyncQueue.Deserialize(ScheduleSyncQueue.Serialize(m), 9000);
        Assert.AreEqual(ScheduleSyncQueue.StateDone, ScheduleSyncQueue.Find(reloaded, "Bot0").state);
        var r1 = ScheduleSyncQueue.Find(reloaded, "Bot1");
        Assert.IsTrue(r1.IsPending && r1.attempts == 0 && r1.nextAttemptAtMs == 9000, "a restart resumes at once");
        Assert.AreEqual(Night, r1.Schedule);
        Assert.AreEqual("Europe/Moscow", r1.timeZone);

        Assert.AreEqual(0, ScheduleSyncQueue.Deserialize("{not json", 1).entries.Count, "a corrupt store is an empty queue");
        Assert.AreEqual(0, ScheduleSyncQueue.Deserialize(null, 1).entries.Count);
        StringAssert.DoesNotContain("IsPending", ScheduleSyncQueue.Serialize(m), "only data is persisted");
    }

    [Test]
    public void ForgetBot_AndForgetAll_DropTheDesires()
    {
        var m = M();
        ScheduleSyncQueue.Desire(m, "Bot0", Weekdays, "Asia/Almaty", 1000);
        ScheduleSyncQueue.Desire(m, "Bot1", Weekdays, "Asia/Almaty", 1000);
        ScheduleSyncQueue.ForgetBot(m, "Bot0");
        Assert.IsNull(ScheduleSyncQueue.Find(m, "Bot0"));
        Assert.IsNotNull(ScheduleSyncQueue.Find(m, "Bot1"));
        ScheduleSyncQueue.ForgetAll(m);
        Assert.AreEqual(0, m.entries.Count);
    }

    // ── the wire ──────────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void ComposeBody_CarriesExactlyTheServersFields_WithTheirTypes()
    {
        var jo = JObject.Parse(SetScheduleRequest.ComposeBody(new[] { "wa", "tg" }, true, 31, 540, 1080, "Asia/Almaty", 1760000000123));
        CollectionAssert.AreEquivalent(new[] { "profileIds", "enabled", "daysMask", "startMin", "endMin", "timeZone", "version" },
            jo.Properties().Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "wa", "tg" }, jo["profileIds"].Select(t => (string)t).ToArray());
        Assert.AreEqual(JTokenType.Boolean, jo["enabled"].Type);
        Assert.AreEqual(JTokenType.Integer, jo["daysMask"].Type);
        Assert.AreEqual(JTokenType.Integer, jo["version"].Type, "the server refuses a non-integer version");
        Assert.AreEqual(31, (int)jo["daysMask"]);
        Assert.AreEqual(540, (int)jo["startMin"]);
        Assert.AreEqual(1080, (int)jo["endMin"]);
        Assert.AreEqual("Asia/Almaty", (string)jo["timeZone"]);
        Assert.AreEqual(1760000000123L, (long)jo["version"]);
        Assert.AreEqual("/webhook/SetSchedule", SetScheduleRequest.Path);

        var disabled = JObject.Parse(SetScheduleRequest.ComposeBody(new[] { "wa" }, false, 0, 540, 1260, "Asia/Almaty", 7));
        Assert.IsFalse((bool)disabled["enabled"], "a disabled schedule still carries every field (re-enabling restores the window)");
        Assert.AreEqual(0, (int)disabled["daysMask"]);
    }

    [TestCase(400, ScheduleSyncQueue.BatchStatus.Rejected, TestName = "Classify_400_BadRequest_IsRejected")]
    [TestCase(401, ScheduleSyncQueue.BatchStatus.Rejected, TestName = "Classify_401_AfterTheRefreshRetry_IsRejected")]
    [TestCase(403, ScheduleSyncQueue.BatchStatus.Rejected, TestName = "Classify_403_NotTheTokensProfile_IsRejected")]
    [TestCase(500, ScheduleSyncQueue.BatchStatus.Transient, TestName = "Classify_500_DbUnavailable_IsTransient")]
    [TestCase(502, ScheduleSyncQueue.BatchStatus.Transient, TestName = "Classify_502_IsTransient")]
    [TestCase(404, ScheduleSyncQueue.BatchStatus.Transient, TestName = "Classify_404_WebhookNotDeployed_IsTransient")]
    [TestCase(0, ScheduleSyncQueue.BatchStatus.Transient, TestName = "Classify_NoAnswer_IsTransient")]
    public void Classify_RefusalsAndFaults(long code, ScheduleSyncQueue.BatchStatus expected)
    {
        Assert.AreEqual(expected, ScheduleSyncResponse.Classify(code, "{\"success\":false,\"error\":\"bad_timezone\"}", out var rows));
        Assert.IsNull(rows);
    }

    [Test]
    public void Classify_OnlyAPositiveSuccessEnvelopeIsAnAnswer()
    {
        const string ok = "{\"success\":true,\"results\":[{\"profileId\":\"wa\",\"applied\":true,\"version\":17},"
                          + "{\"profileId\":\"tg\",\"applied\":false,\"version\":99}]}";
        Assert.AreEqual(ScheduleSyncQueue.BatchStatus.Answered, ScheduleSyncResponse.Classify(200, ok, out var rows));
        Assert.AreEqual(2, rows.Count);
        Assert.IsTrue(rows[0].ProfileId == "wa" && rows[0].Applied && rows[0].Version == 17);
        Assert.IsTrue(rows[1].ProfileId == "tg" && !rows[1].Applied && rows[1].Version == 99);

        foreach (var body in new[] { "", "<html>proxy</html>", "{}", "{\"success\":\"true\",\"results\":[]}",
                                     "{\"success\":false,\"error\":\"db_unavailable\"}", "Workflow executed successfully" })
        {
            Assert.AreEqual(ScheduleSyncQueue.BatchStatus.Transient, ScheduleSyncResponse.Classify(200, body, out var none),
                $"'{body}' is not the server's word — retried, never settled");
            Assert.IsNull(none);
        }
    }

    // ── the service, at the source ────────────────────────────────────────────────────────────────────────

    [Test]
    public void Service_BootsItself_AndWaitsAtTheSessionGate_BeforeItsFirstSend()
    {
        var boot = typeof(ScheduleSyncService).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(m => m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null);
        Assert.IsNotNull(boot, "the queue must resume on every launch (RuntimeInitializeOnLoadMethod), like ReplyModeSyncService");
        Assert.AreEqual(RuntimeInitializeLoadType.AfterSceneLoad, boot.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>().loadType);
        Assert.AreEqual("CrSchedule.Queue", ScheduleSyncService.StoreKey);

        string svc = Src("Scripts/Main/ScheduleSyncService.cs");
        Assert.IsTrue(MethodBody(svc, "private static void " + boot.Name).Contains("Instance"), "the boot creates the service");
        Assert.IsTrue(MethodBody(svc, "private void Awake").Contains("ScheduleSyncQueue.Deserialize(PlayerPrefs.GetString(StoreKey"));
        string loop = MethodBody(svc, "private IEnumerator Loop");
        int gate = loop.IndexOf("ReplyModeSendGate.CanSend(", StringComparison.Ordinal);
        int send = loop.IndexOf("yield return Send(", StringComparison.Ordinal);
        Assert.IsTrue(gate >= 0 && send > gate, "no send before billing + the session (the reply-mode gate)");

        string opener = MethodBody(Src("Scripts/Main/Manager.ReplyModeSync.cs"), "private IEnumerator EnsureSessionThenOpenReplyModeSendGate");
        int session = opener.IndexOf("yield return AuthClient.Instance.EnsureSession()", StringComparison.Ordinal);
        int open = opener.IndexOf("ScheduleSyncService.OpenSendGate()", StringComparison.Ordinal);
        Assert.IsTrue(session >= 0 && open > session, "the schedule's gate opens with the reply-mode one, after the session");
    }

    [Test]
    public void Send_GoesThroughAuthorizedRequest_AndOnlyForProfilesTheTokenLists()
    {
        string send = MethodBody(Src("Scripts/Main/ScheduleSyncService.cs"), "private IEnumerator Send(");
        Assert.IsTrue(Regex.IsMatch(send,
                @"AuthorizedRequest\.Send\(\s*\(\)\s*=>\s*AuthorizedRequest\.JsonPost\(\$""\{Manager\.n8nBaseUrl\}\{SetScheduleRequest\.Path\}"",\s*SetScheduleRequest\.ComposeBody\("),
            "the request is built by the factory AuthorizedRequest.Send stamps (A1)");
        int sendable = send.IndexOf("ScheduleSyncQueue.Sendable(", StringComparison.Ordinal);
        Assert.IsTrue(sendable >= 0 && send.Contains(".Owns"), "only profiles the token lists travel");
        Assert.IsTrue(sendable < send.IndexOf("AuthorizedRequest.Send(", StringComparison.Ordinal));
        Assert.IsTrue(send.Contains("ScheduleSyncResponse.Classify(www.responseCode")
                      && send.Contains("ScheduleSyncQueue.ApplyResult(entry, sentVersion,"),
            "every answer goes through the pure, version-guarded seam");
        Assert.IsFalse(Regex.IsMatch(Src("Scripts/Main/ScheduleSyncService.cs"), @"\.SendWebRequest\(\)"), "never a direct send");
    }

    [Test]
    public void SaveAndSettingsCreate_QueueTheSavedSchedule()
    {
        string manager = Src("Scripts/Main/Manager.cs");
        string save = MethodBody(manager, "public void SaveSettings()");
        int persist = save.IndexOf("PlayerPrefs.Save();", StringComparison.Ordinal);
        int desire = save.IndexOf("ScheduleSyncService.DesireSaved(openBot.name)", StringComparison.Ordinal);
        Assert.IsTrue(persist >= 0 && desire > persist, "the desire follows a successful local Save");
        Assert.IsTrue(save.Contains("BotScheduleStore.Write(openBot.name, editedSchedule)"));
        Assert.IsTrue(Regex.IsMatch(save, @"if \(scheduleChanged\)\s*ScheduleSyncService\.DesireSaved"),
            "only a Save that CHANGED the schedule is queued");

        foreach (var create in new[] { "private IEnumerator CreateWhatsappWorkflowFromEdit()", "private IEnumerator CreateTelegramWorkflowFromEdit()" })
            Assert.IsTrue(MethodBody(manager, create).Contains("ScheduleSyncService.DesireSavedForNewChannel(bot.name)"),
                $"{create}: a channel added from the settings must get the bot's schedule");
        // The wizard's second channel can finish its Create AFTER the owner saved a schedule on the new bot (its settings
        // open once the first channel is live): that channel must get it too — a no-op while nothing was saved.
        foreach (var create in new[] { "private IEnumerator CreateWhatsappWorkflowFromStart(", "private IEnumerator CreateTelegramWorkflowFromStart(" })
        {
            string body = MethodBody(manager, create);
            int seed = body.IndexOf("SeedReplyModeDefaultForProfile(bot.name, startedBot.", StringComparison.Ordinal);
            int queued = body.IndexOf("ScheduleSyncService.DesireSavedForNewChannel(bot.name)", StringComparison.Ordinal);
            Assert.IsTrue(seed >= 0 && queued > seed, $"{create}: the new channel gets the saved schedule after its profile id is stored");
        }
    }

    [Test]
    public void DeletedBots_AndTheFullWipe_LeaveNothingBehind()
    {
        string local = MethodBody(Src("Scripts/Main/Bot.cs"), "public void DeleteBotLocally");
        Assert.IsTrue(local.Contains("ScheduleSyncService.Existing?.ForgetBot(transform.name)"), "a deleted bot's queued desire goes with it");
        Assert.IsTrue(local.Contains("BotScheduleStore.Clear(transform.name)"), "the four schedule keys");
        Assert.IsTrue(local.Contains("BusinessTimeZone.Clear(transform.name)"), "{bot}TimeZone used to leak on every per-bot delete");

        string wipe = MethodBody(Src("Scripts/Main/ProfileSubPages.Account.cs"), "private void FinishWipe");
        int forget = wipe.IndexOf("ScheduleSyncService.Existing?.ForgetAll()", StringComparison.Ordinal);
        int deleteAll = wipe.IndexOf("PlayerPrefs.DeleteAll()", StringComparison.Ordinal);
        Assert.IsTrue(forget >= 0 && forget < deleteAll, "the in-memory queue re-persists itself — empty it before the wipe");
    }

    [Test]
    public void ServerDeletion_HoldsTheQueue_DrainsIt_AndReleasesItWhenTheBotStays()
    {
        // The V12 mirror: Delete Bot Files removes the bot's bot_schedule rows, so a schedule send must never land after it —
        // held from before the first server call (a request already on the wire drained first), released when the server
        // keeps the bot, forgotten by the local wipe once it goes (DeletedBots_AndTheFullWipe_LeaveNothingBehind).
        string server = MethodBody(Src("Scripts/Main/Manager.cs"), "public IEnumerator DeleteBotOnServer");
        int hold = server.IndexOf("ScheduleSyncService.Existing?.Hold(botId)", StringComparison.Ordinal);
        int firstCall = server.IndexOf("WappiProxy.Send(", StringComparison.Ordinal);
        Assert.IsTrue(hold >= 0 && hold < firstCall, "the schedule queue is held before the first server call");
        int drain = server.IndexOf("ScheduleSyncService.Existing.IsSending(botId)", StringComparison.Ordinal);
        Assert.IsTrue(drain > hold && drain < firstCall, "a request already on the wire lands before the server wipes the rows");
        int keep = server.IndexOf("if (!outcome.RemovesBot)", StringComparison.Ordinal);
        int release = server.IndexOf("ScheduleSyncService.Existing?.Release(botId)", StringComparison.Ordinal);
        Assert.IsTrue(keep >= 0 && release > keep, "a bot the server keeps gets its schedule queue back");
        Assert.IsFalse(server.Contains("ScheduleSyncService.Existing?.ForgetBot("), "never forgotten before the verdict");

        string svc = Src("Scripts/Main/ScheduleSyncService.cs");
        string loop = MethodBody(svc, "private IEnumerator Loop");
        Assert.IsTrue(loop.Contains("ScheduleSyncQueue.Due(_model, NowMs(), _held)") && loop.Contains("ScheduleSyncQueue.AnyPending(_model, _held)"),
            "a held bot's desire is never picked up, and does not keep the loop spinning");
        string send = MethodBody(svc, "private IEnumerator Send(");
        int recheck = send.IndexOf("if (_held.Contains(entry.botId)) yield break;", StringComparison.Ordinal);
        int marked = send.IndexOf("_sendingBot = entry.botId;", StringComparison.Ordinal);
        int request = send.IndexOf("AuthorizedRequest.Send(", StringComparison.Ordinal);
        Assert.IsTrue(recheck >= 0 && marked > recheck && request > marked,
            "re-checked after the session wait, then marked as on the wire right before the request");
        Assert.IsFalse(send.Substring(recheck, marked - recheck).Contains("yield return"),
            "nothing suspends between the re-check and the mark — a hold either stops the send or finds it on the wire");
        Assert.IsTrue(Regex.IsMatch(send, @"finally\s*\{\s*_sendingBot = null;\s*\}"), "the mark is cleared however the request ends");
        Assert.IsTrue(MethodBody(svc, "public void ForgetBot").Contains("_held.Remove("), "forgetting a bot also drops its hold");
        Assert.IsTrue(MethodBody(svc, "public void Release").Contains("Kick()"), "a release sends the kept bot's desire");
    }
}

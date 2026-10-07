using System.Collections.Generic;
using NUnit.Framework;

public class ReplyModeSyncQueueTests
{
    private static ReplyModeSyncQueue.Model M() => new ReplyModeSyncQueue.Model();

    [Test]
    public void NextVersion_IsStrictlyIncreasing_EvenWhenTheClockStallsOrStepsBack()
    {
        Assert.AreEqual(1000, ReplyModeSyncQueue.NextVersion(1000, 0));
        Assert.AreEqual(1001, ReplyModeSyncQueue.NextVersion(1000, 1000), "same ms → +1");
        Assert.AreEqual(2001, ReplyModeSyncQueue.NextVersion(500, 2000), "clock stepped back → still above the last");
    }

    [Test]
    public void Desire_CreatesPendingEntries_WithOneSharedVersion_AndSupersedesOlderDesires()
    {
        var m = M();
        long v1 = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa", "tg" }, "*", true, 1000);
        Assert.AreEqual(2, m.entries.Count);
        Assert.IsTrue(m.entries.TrueForAll(e => e.IsPending && e.desiredVersion == v1 && e.desiredSuppressed));
        long v2 = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "*", false, 1000);   // same ms: a faster second tap
        Assert.Greater(v2, v1);
        var wa = ReplyModeSyncQueue.Find(m, "wa", "*");
        Assert.IsFalse(wa.desiredSuppressed);
        Assert.AreEqual(v2, wa.desiredVersion);
        Assert.AreEqual(0, wa.attempts);
        Assert.AreEqual(2, m.entries.Count, "no duplicate entry per (profile, chat)");
    }

    [Test]
    public void OnResponse_Applied_ConfirmsTheDesire()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "*", true, 1000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "*");
        Assert.AreEqual(ReplyModeSyncQueue.Outcome.Confirmed, ReplyModeSyncQueue.OnResponse(e, v, applied: true, serverSuppressed: true, serverVersion: v));
        Assert.IsTrue(e.hasConfirmed && e.confirmedSuppressed && e.state == ReplyModeSyncQueue.StateConfirmed);
    }

    [Test]
    public void OnResponse_NotApplied_AdoptsTheServersNewerValue()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "*", true, 1000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "*");
        var outcome = ReplyModeSyncQueue.OnResponse(e, v, applied: false, serverSuppressed: false, serverVersion: v + 500);
        Assert.AreEqual(ReplyModeSyncQueue.Outcome.AdoptedServer, outcome);
        Assert.IsFalse(e.desiredSuppressed, "the UI must now show the server's newer choice");
        Assert.AreEqual(v + 500, e.confirmedVersion);
        Assert.AreEqual(ReplyModeSyncQueue.StateConfirmed, e.state);
        // a desire made AFTER adopting mints a version above the server's
        long v3 = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "*", true, 1000);
        Assert.Greater(v3, v + 500);
    }

    [Test]
    public void OnResponse_ForAnOlderRequest_IsIgnored_TheNewerDesireStaysPending()
    {
        var m = M();
        long v1 = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "*", true, 1000);
        long v2 = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "*", false, 2000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "*");
        Assert.AreEqual(ReplyModeSyncQueue.Outcome.Ignored, ReplyModeSyncQueue.OnResponse(e, v1, true, true, v1), "reordered response for the first tap");
        Assert.IsTrue(e.IsPending && e.desiredVersion == v2 && !e.desiredSuppressed);
    }

    [Test]
    public void Backoff_IsBounded_AndParksAfterMaxAttempts_ThenRetriesNextLaunch()
    {
        var m = M();
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "*", true, 1000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "*");
        long now = 1000;
        Assert.AreEqual(1, ReplyModeSyncQueue.Due(m, now).Count);
        ReplyModeSyncQueue.OnSendFailed(e, now);
        Assert.AreEqual(now + 2000, e.nextAttemptAtMs);
        Assert.AreEqual(0, ReplyModeSyncQueue.Due(m, now + 1999).Count);
        Assert.AreEqual(1, ReplyModeSyncQueue.Due(m, now + 2000).Count);
        for (int i = 1; i < ReplyModeSyncQueue.MaxAttemptsPerLaunch; i++) ReplyModeSyncQueue.OnSendFailed(e, now);
        Assert.IsTrue(e.IsFailed, "parked after the per-launch cap");
        Assert.IsTrue(ReplyModeSyncQueue.AnyFailed(m, "Bot0"));
        var reloaded = ReplyModeSyncQueue.Deserialize(ReplyModeSyncQueue.Serialize(m), 5000);
        var r = ReplyModeSyncQueue.Find(reloaded, "wa", "*");
        Assert.IsTrue(r.IsPending && r.attempts == 0 && r.nextAttemptAtMs == 5000, "next launch retries the parked desire");
        Assert.IsTrue(r.desiredSuppressed);
    }

    [Test]
    public void Rejected_StopsRetrying_ButSurvivesForTheNextLaunch()
    {
        var m = M();
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        ReplyModeSyncQueue.OnRejected(e);
        Assert.IsTrue(e.IsFailed);
        Assert.AreEqual(0, ReplyModeSyncQueue.Due(m, 99999).Count);
    }

    [Test]
    public void Batches_GroupBySameChatValueAndVersion()
    {
        var m = M();
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa", "tg" }, "*", true, 1000);
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "chat9", false, 1000);
        var batches = ReplyModeSyncQueue.Batches(ReplyModeSyncQueue.Due(m, 1000));
        Assert.AreEqual(2, batches.Count);
        Assert.AreEqual(2, batches[0].Count, "the bot-default write for both channels travels as ONE request");
        Assert.AreEqual(1, batches[1].Count);
    }

    [Test]
    public void AnyPending_ScopesByBotAndOptionalChat()
    {
        var m = M();
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "chat9", true, 1000);
        Assert.IsTrue(ReplyModeSyncQueue.AnyPending(m, "Bot0"));
        Assert.IsTrue(ReplyModeSyncQueue.AnyPending(m, "Bot0", "chat9"));
        Assert.IsFalse(ReplyModeSyncQueue.AnyPending(m, "Bot0", "*"));
        Assert.IsFalse(ReplyModeSyncQueue.AnyPending(m, "Bot1"));
    }

    // ---- R4 (2026-09-27): the chat-open heal REPEATS a decision, it never makes a new one ----------------------
    // A fresh version on every chat open made the server read a mere open as the owner's new «Авто» and close a
    // handoff the owner had not even seen. The heal (Reassert) now re-sends only what the queue still owes, with the
    // ORIGINAL version, and probes an unrecorded «Авто» with the floor version (applies only where the server has no
    // row, never resolves a request).

    [Test]
    public void Reassert_SameValueConfirmed_SendsNothing_AndKeepsTheOriginalVersion()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);        // the owner's «Авто», a day ago
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        ReplyModeSyncQueue.OnResponse(e, v, applied: true, serverSuppressed: false, serverVersion: v);
        ReplyModeSyncQueue.Reassert(m, "Bot0", "wa", "c1", false, 86_401_000);                       // chat opened after a new handoff
        Assert.AreEqual(v, e.desiredVersion, "a chat open must not mint a new decision");
        Assert.AreEqual(ReplyModeSyncQueue.StateConfirmed, e.state);
        Assert.AreEqual(0, ReplyModeSyncQueue.Due(m, 86_401_000).Count, "nothing is owed: nothing is sent");
    }

    [Test]
    public void Reassert_PendingSameValue_DoesNotMintANewVersion()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);
        ReplyModeSyncQueue.Reassert(m, "Bot0", "wa", "c1", false, 50_000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        Assert.AreEqual(v, e.desiredVersion, "the queued decision keeps its version");
        Assert.IsTrue(e.IsPending);
    }

    [Test]
    public void Reassert_FailedSameValue_RequeuesTheOriginalVersion()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        for (int i = 0; i < ReplyModeSyncQueue.MaxAttemptsPerLaunch; i++) ReplyModeSyncQueue.OnSendFailed(e, 1000);
        Assert.IsTrue(e.IsFailed);
        ReplyModeSyncQueue.Reassert(m, "Bot0", "wa", "c1", false, 90_000);
        Assert.IsTrue(e.IsPending && e.attempts == 0 && e.nextAttemptAtMs == 90_000, "a parked decision is retried");
        Assert.AreEqual(v, e.desiredVersion, "...with the version it was decided at");
    }

    [Test]
    public void Reassert_AutoWithoutAnEntry_UsesTheFloorVersion()
    {
        var m = M();
        ReplyModeSyncQueue.Reassert(m, "Bot0", "wa", "c1", false, 90_000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        Assert.IsNotNull(e);
        Assert.IsFalse(e.desiredSuppressed);
        Assert.AreEqual(ReplyModeSyncQueue.FloorVersion, e.desiredVersion, "an unrecorded «Авто» is probed with the floor version, never «now»");
        Assert.IsTrue(e.IsPending && e.botId == "Bot0");
        // the server answers with its stored (newer) row: adopted, and a later real tap still mints above it
        ReplyModeSyncQueue.OnResponse(e, ReplyModeSyncQueue.FloorVersion, applied: false, serverSuppressed: true, serverVersion: 5000);
        Assert.IsTrue(e.desiredSuppressed && e.confirmedVersion == 5000);
        Assert.Greater(ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 100), 5000);
    }

    [Test]
    public void Reassert_SemiDiffering_MintsAFreshVersion()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        ReplyModeSyncQueue.OnResponse(e, v, true, false, v);
        ReplyModeSyncQueue.Reassert(m, "Bot0", "wa", "c1", true, 90_000);          // e.g. the handoff mirror paused the chat
        Assert.IsTrue(e.desiredSuppressed && e.IsPending);
        Assert.Greater(e.desiredVersion, v, "«Вместе» resolves nothing on the server, so it may be a fresh decision");
    }

    [Test]
    public void Reassert_AutoOverAPendingSemi_LeavesTheQueuedDecisionAlone()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", true, 1000);
        ReplyModeSyncQueue.Reassert(m, "Bot0", "wa", "c1", false, 90_000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        Assert.IsTrue(e.desiredSuppressed && e.desiredVersion == v && e.IsPending, "an undelivered decision is on its way; its answer reconciles");
    }

    // ---- R5 (2026-09-27): cold-start resume — stale answers, deleted bots, the boot announcement, the send gate ----

    private static ReplyModeSyncResponse.Row Row(string pid, bool applied, bool suppressed, long version)
        => new ReplyModeSyncResponse.Row { ProfileId = pid, Applied = applied, Suppressed = suppressed, Version = version };

    [Test]
    public void StaleRefusal_DoesNotSettleANewerDesire()
    {
        var m = M();
        long v1 = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);
        var batch = ReplyModeSyncQueue.Batches(ReplyModeSyncQueue.Due(m, 1000))[0];      // v1 is in flight
        long v2 = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", true, 2000);   // the owner taps again meanwhile
        ReplyModeSyncQueue.ApplyBatchResult(batch, v1, ReplyModeSyncQueue.BatchStatus.Rejected, null, 3000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        Assert.IsTrue(e.IsPending && e.desiredVersion == v2 && e.desiredSuppressed, "a refusal of v1 must not park the unsent v2");
        Assert.AreEqual(1, ReplyModeSyncQueue.Due(m, 3000).Count, "v2 is still due");
    }

    [Test]
    public void StaleTransientFailure_DoesNotBackOffANewerDesire()
    {
        var m = M();
        long v1 = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);
        var batch = ReplyModeSyncQueue.Batches(ReplyModeSyncQueue.Due(m, 1000))[0];
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", true, 2000);
        ReplyModeSyncQueue.ApplyBatchResult(batch, v1, ReplyModeSyncQueue.BatchStatus.Transient, null, 3000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        Assert.AreEqual(0, e.attempts, "the newer desire has not been tried yet");
        Assert.AreEqual(1, ReplyModeSyncQueue.Due(m, 3000).Count, "...and is due now, not after a backoff");
    }

    [Test]
    public void BatchResult_AnsweredConfirmsOrAdopts_PerProfile_AndAMissingRowIsTransient()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa", "tg", "x" }, "*", true, 1000);
        var batch = ReplyModeSyncQueue.Batches(ReplyModeSyncQueue.Due(m, 1000))[0];
        var adopted = ReplyModeSyncQueue.ApplyBatchResult(batch, v, ReplyModeSyncQueue.BatchStatus.Answered,
            new[] { Row("wa", true, true, v), Row("tg", false, false, v + 9) }, 2000);
        Assert.AreEqual(ReplyModeSyncQueue.StateConfirmed, ReplyModeSyncQueue.Find(m, "wa", "*").state);
        Assert.AreEqual(1, adopted.Count);
        Assert.AreEqual("tg", adopted[0].profileId, "the server's newer value is adopted and reported");
        var x = ReplyModeSyncQueue.Find(m, "x", "*");
        Assert.IsTrue(x.IsPending && x.attempts == 1, "a profile the server did not report is retried");
    }

    [Test]
    public void BatchResult_RejectedAndTransient_ApplyToTheSentVersion()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);
        var batch = ReplyModeSyncQueue.Batches(ReplyModeSyncQueue.Due(m, 1000))[0];
        ReplyModeSyncQueue.ApplyBatchResult(batch, v, ReplyModeSyncQueue.BatchStatus.Transient, null, 1000);
        Assert.AreEqual(1, ReplyModeSyncQueue.Find(m, "wa", "c1").attempts);
        ReplyModeSyncQueue.ApplyBatchResult(batch, v, ReplyModeSyncQueue.BatchStatus.Rejected, null, 1000);
        Assert.IsTrue(ReplyModeSyncQueue.Find(m, "wa", "c1").IsFailed);
    }

    [Test]
    public void ForgetBot_DropsOnlyThatBotsEntries()
    {
        var m = M();
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa0" }, "*", true, 1000);
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa0" }, "c1", false, 1000);
        ReplyModeSyncQueue.Desire(m, "Bot1", new[] { "wa1" }, "*", true, 1000);
        ReplyModeSyncQueue.ForgetBot(m, "Bot0");
        Assert.AreEqual(1, m.entries.Count, "a deleted bot's desires must never be sent again");
        Assert.AreEqual("Bot1", m.entries[0].botId);
        ReplyModeSyncQueue.ForgetAll(m);
        Assert.AreEqual(0, m.entries.Count, "the full wipe leaves nothing for a new Bot0 to inherit");
    }

    [Test]
    public void PendingScopes_NamesEveryUnconfirmedBotChat_OnceEach()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa", "tg" }, "*", true, 1000);
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);
        ReplyModeSyncQueue.Desire(m, "Bot1", new[] { "wb" }, "*", false, 1000);
        var confirmed = ReplyModeSyncQueue.Find(m, "wb", "*");
        ReplyModeSyncQueue.OnResponse(confirmed, confirmed.desiredVersion, true, false, confirmed.desiredVersion);
        var reloaded = ReplyModeSyncQueue.Deserialize(ReplyModeSyncQueue.Serialize(m), 5000);
        var scopes = ReplyModeSyncQueue.PendingScopes(reloaded);
        CollectionAssert.AreEquivalent(new[] { ("Bot0", "*"), ("Bot0", "c1") }, scopes,
            "the boot announcement repaints each surface that shows a still-unconfirmed choice, once");
    }

    [Test]
    public void SendGate_ClosedUntilOpened_OrUntilTheSafetyTimeout()
    {
        Assert.IsFalse(ReplyModeSendGate.CanSend(false, 0f), "before billing + session exist nothing is sent");
        Assert.IsFalse(ReplyModeSendGate.CanSend(false, ReplyModeSendGate.SafetySeconds - 0.1f));
        Assert.IsTrue(ReplyModeSendGate.CanSend(true, 0.5f), "opened by the boot sequence");
        Assert.IsTrue(ReplyModeSendGate.CanSend(false, ReplyModeSendGate.SafetySeconds), "a boot that never opens it cannot strand the queue");
    }

    // ---- R4-clock (iteration 2026-09-28): the decision is dated by its own label, the version only orders ----

    [Test]
    public void Desire_StampsTheTap_NotTheMonotonicVersion()
    {
        // B6: a phone whose clock ran 5 min fast confirmed v = T+2min; the owner fixed the clock and taps again at T-2min.
        // The version must stay above the stored one (T+2min+1) — but dating the decision by it closed requests made
        // after the real tap. The label is the tap itself.
        var m = M();
        const long fastStamp = 1_000_120_000;
        long v1 = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, fastStamp);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        ReplyModeSyncQueue.OnResponse(e, v1, true, false, v1);
        const long realTap = 999_880_000;                                              // the clock was corrected meanwhile
        long v2 = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, realTap);
        Assert.AreEqual(fastStamp + 1, v2, "ordering is unchanged: last + 1");
        Assert.AreEqual(realTap, e.decidedAtMs, "the decision is dated by the tap, not by the version");
    }

    [Test]
    public void DecisionAge_IsMeasuredOnTheSameClock_AndOmittedWhenItCannotBeTrusted()
    {
        Assert.AreEqual(240_000L, ReplyModeSyncQueue.DecisionAgeMs(1_000_000, 1_240_000), "queued 4 min");
        Assert.AreEqual(0L, ReplyModeSyncQueue.DecisionAgeMs(1_000_000, 1_000_000), "sent at once");
        Assert.IsNull(ReplyModeSyncQueue.DecisionAgeMs(0, 1_240_000), "no decision to date (floor probe / adopted / older build)");
        Assert.IsNull(ReplyModeSyncQueue.DecisionAgeMs(1_000_000, 700_000), "the clock stepped back: no age is trustworthy, the server keeps the version");
    }

    [Test]
    public void Reassert_KeepsTheOriginalDecisionTime_AndTheFloorProbeHasNone()
    {
        var m = M();
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        for (int i = 0; i < ReplyModeSyncQueue.MaxAttemptsPerLaunch; i++) ReplyModeSyncQueue.OnSendFailed(e, 2000);
        Assert.IsTrue(e.IsFailed);
        ReplyModeSyncQueue.Reassert(m, "Bot0", "wa", "c1", false, 90_000);            // chat open re-arms the parked «Авто»
        Assert.IsTrue(e.IsPending && e.decidedAtMs == 1000, "a heal repeats the decision — with its original time");
        ReplyModeSyncQueue.Reassert(m, "Bot0", "wa", "c2", false, 90_000);            // an «Авто» the queue never saw
        var probe = ReplyModeSyncQueue.Find(m, "wa", "c2");
        Assert.AreEqual(ReplyModeSyncQueue.FloorVersion, probe.desiredVersion);
        Assert.AreEqual(0L, probe.decidedAtMs, "the floor probe is not a decision: it is sent without an age and closes nothing");
        ReplyModeSyncQueue.Reassert(m, "Bot0", "wa", "c1", true, 95_000);             // a differing «Вместе» is a fresh desire
        Assert.AreEqual(95_000L, e.decidedAtMs);
    }

    [Test]
    public void AdoptingTheServersValue_ClearsTheDecisionTime()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1000);
        var e = ReplyModeSyncQueue.Find(m, "wa", "c1");
        ReplyModeSyncQueue.OnResponse(e, v, applied: false, serverSuppressed: true, serverVersion: v + 500);
        Assert.AreEqual(0L, e.decidedAtMs, "a value adopted from the server was not decided on this phone");
    }

    [Test]
    public void Batches_NeverMergeTwoDecisionTimes()
    {
        var m = M();
        long v = ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa", "tg" }, "c1", false, 1000);
        Assert.AreEqual(1, ReplyModeSyncQueue.Batches(ReplyModeSyncQueue.Due(m, 1000)).Count, "one tap, one request");
        ReplyModeSyncQueue.Find(m, "tg", "c1").decidedAtMs = 0;                     // e.g. an entry persisted by an older build
        Assert.AreEqual(v, ReplyModeSyncQueue.Find(m, "tg", "c1").desiredVersion);
        Assert.AreEqual(2, ReplyModeSyncQueue.Batches(ReplyModeSyncQueue.Due(m, 1000)).Count, "a request carries ONE decision age");
    }

    [Test]
    public void DecisionTime_SurvivesARestart_AndAnOlderBuildsQueueLoadsWithoutOne()
    {
        var m = M();
        ReplyModeSyncQueue.Desire(m, "Bot0", new[] { "wa" }, "c1", false, 1234);
        Assert.AreEqual(1234L, ReplyModeSyncQueue.Find(ReplyModeSyncQueue.Deserialize(ReplyModeSyncQueue.Serialize(m), 9999), "wa", "c1").decidedAtMs);
        var old = ReplyModeSyncQueue.Deserialize("{\"entries\":[{\"botId\":\"Bot0\",\"profileId\":\"wa\",\"chatId\":\"c1\",\"desiredSuppressed\":false,\"desiredVersion\":5000,\"state\":\"pending\"}]}", 9999);
        Assert.AreEqual(0L, old.entries[0].decidedAtMs, "pre-R4-clock entries are resent without an age (the version decides, as before)");
    }

    [Test]
    public void Deserialize_ToleratesGarbage_AndEmpty()
    {
        Assert.AreEqual(0, ReplyModeSyncQueue.Deserialize(null, 1).entries.Count);
        Assert.AreEqual(0, ReplyModeSyncQueue.Deserialize("{not json", 1).entries.Count);
    }

    [Test]
    public void Response_ParsesTheV2Envelope_AndRejectsGarbage()
    {
        Assert.IsTrue(ReplyModeSyncResponse.TryParse("{\"success\":true,\"allApplied\":false,\"applied\":[{\"profileId\":\"wa\",\"applied\":false,\"suppressed\":true,\"version\":200}]}", out var r));
        Assert.IsTrue(r.Success); Assert.IsFalse(r.AllApplied);
        Assert.AreEqual(1, r.Applied.Count);
        Assert.AreEqual("wa", r.Applied[0].ProfileId); Assert.IsFalse(r.Applied[0].Applied); Assert.IsTrue(r.Applied[0].Suppressed); Assert.AreEqual(200, r.Applied[0].Version);
        Assert.IsTrue(ReplyModeSyncResponse.TryParse("{\"success\":false,\"error\":\"db_error\"}", out var f));
        Assert.IsFalse(f.Success); Assert.AreEqual("db_error", f.Error);
        Assert.IsFalse(ReplyModeSyncResponse.TryParse("nope", out _));
        Assert.IsFalse(ReplyModeSyncResponse.TryParse("", out _));
    }
}

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

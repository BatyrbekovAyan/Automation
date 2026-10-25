using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

/// <summary>
/// «Личные и рабочие чаты»: the owner's moves (a chat made business or personal) and the app's connection reports reach the
/// server through ONE persisted queue — the <see cref="ReplyModeSyncQueue"/> pattern. A move is a DESIRE per (profile, chat)
/// with a version the client mints, retried with a bounded backoff and kept across restarts; the server's answer settles it
/// (applied, or the server's newer view adopted); a refusal takes it back. Connection reports are a separate list of the
/// same queue. Pure model, no Unity: everything here runs without a request.
/// </summary>
public class ChatAudienceSyncQueueTests
{
    private const string Bot0 = "Bot0";
    private static readonly string[] OneChat = { "a@c.us" };

    private static ChatAudienceSyncQueue.Model M() => new ChatAudienceSyncQueue.Model();

    private static List<AudienceSetRow> Rows(params (string chatId, bool applied, AudienceKind kind, long version, string refused)[] rows)
        => rows.Select(r => new AudienceSetRow { ChatId = r.chatId, Applied = r.applied, Kind = r.kind, Version = r.version, Refused = r.refused })
               .ToList();

    private static List<AudienceSetRow> AllApplied(IEnumerable<ChatAudienceRequest.SetChat> sent)
        => sent.Select(s => new AudienceSetRow { ChatId = s.ChatId, Applied = true, Kind = s.Kind, Version = s.Version }).ToList();

    private static ChatAudienceSyncQueue.SetOutcome Answer(ChatAudienceSyncQueue.Model m, string profileId, ChatAudienceSyncQueue.BatchStatus status,
                                                           List<AudienceSetRow> rows, long nowMs)
        => ChatAudienceSyncQueue.ApplySetResult(m, profileId, ChatAudienceSyncQueue.Snapshot(ChatAudienceSyncQueue.NextDueBatch(m, long.MaxValue)),
                                                status, rows, nowMs);

    // ── desires and versions ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Queue_OneDesirePerChat_VersionGrows()
    {
        var m = M();
        long v1 = ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", new[] { "a@c.us", "b@c.us", "a@c.us", "*", "" },
                                                   AudienceKind.Business, 1000, out var accepted);
        CollectionAssert.AreEqual(new[] { "a@c.us", "b@c.us" }, accepted, "each chat once; the wildcard and an empty id are no chats");
        Assert.AreEqual(2, m.desires.Count);
        Assert.AreEqual(1000, v1, "max(now, last + 1)");

        long v2 = ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", OneChat, AudienceKind.Personal, 1000, out _);   // the same ms
        Assert.AreEqual(1001, v2, "strictly above the last version even in the same millisecond");
        Assert.AreEqual(2, m.desires.Count, "one desire per (profile, chat): the newer move replaces the queued one");
        var a = ChatAudienceSyncQueue.FindDesire(m, "wa", "a@c.us");
        Assert.AreEqual(AudienceKind.Personal, a.Kind, "the newer move's kind");
        Assert.AreEqual(v2, a.version);
        Assert.AreEqual(Bot0, a.botId);
        Assert.IsTrue(a.IsPending && a.attempts == 0 && a.nextAttemptAtMs == 1000, "due at once");
        Assert.AreEqual(v1, ChatAudienceSyncQueue.FindDesire(m, "wa", "b@c.us").version, "the other chat keeps its own version");

        long v3 = ChatAudienceSyncQueue.AddDesires(m, Bot0, "tg", OneChat, AudienceKind.Business, 500, out _);    // the clock stepped back
        Assert.AreEqual(1002, v3, "a clock that stepped back never mints an older version");
        Assert.AreEqual(3, m.desires.Count, "the same chat id on another profile is another chat");

        Assert.AreEqual(0, ChatAudienceSyncQueue.AddDesires(m, Bot0, Bot.UnauthedProfileSentinel, OneChat, AudienceKind.Business, 2000, out var none));
        Assert.IsEmpty(none, "a never-paired channel has no chat to move");
        Assert.AreEqual(0, ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", null, AudienceKind.Business, 2000, out _));
        Assert.AreEqual(3, m.desires.Count);
    }

    [Test]
    public void Queue_SurvivesRestart()
    {
        var m = M();
        long v = ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", OneChat, AudienceKind.Business, 1000, out _);
        ChatAudienceSyncQueue.AddConnection(m, Bot0, "tg", 1000);
        for (int i = 0; i < ChatAudienceSyncQueue.MaxAttemptsPerLaunch; i++)
            Answer(m, "wa", ChatAudienceSyncQueue.BatchStatus.Transient, null, 1500);                     // parked for this launch
        var connection = ChatAudienceSyncQueue.FindConnection(m, "tg");
        ChatAudienceSyncQueue.ApplyConnectedResult(m, connection, connection.reports, ChatAudienceSyncQueue.BatchStatus.Rejected, 1500);
        Assert.AreEqual(ChatAudienceSyncQueue.StateFailed, ChatAudienceSyncQueue.FindDesire(m, "wa", "a@c.us").state);

        var reloaded = ChatAudienceSyncQueue.Deserialize(ChatAudienceSyncQueue.Serialize(m), 9000);
        var desire = ChatAudienceSyncQueue.FindDesire(reloaded, "wa", "a@c.us");
        Assert.IsNotNull(desire, "an undelivered move survives a restart");
        Assert.IsTrue(desire.IsPending && desire.attempts == 0 && desire.nextAttemptAtMs == 9000, "a launch retries it at once");
        Assert.AreEqual(v, desire.version, "…with the version it was minted with");
        Assert.AreEqual(AudienceKind.Business, desire.Kind);
        Assert.AreEqual(Bot0, desire.botId);
        var report = ChatAudienceSyncQueue.FindConnection(reloaded, "tg");
        Assert.IsTrue(report.IsPending && report.attempts == 0 && report.nextAttemptAtMs == 9000, "a refused report is re-armed by a launch");
        Assert.AreEqual(Bot0, report.botId);
        Assert.Greater(ChatAudienceSyncQueue.AddDesires(reloaded, Bot0, "wa", OneChat, AudienceKind.Personal, 10, out _), v,
            "the version floor survives too: a later move is never older, whatever the clock says");

        Assert.IsEmpty(ChatAudienceSyncQueue.Deserialize("{not json", 1).desires, "a corrupt store is an empty queue");
        Assert.IsEmpty(ChatAudienceSyncQueue.Deserialize(null, 1).connections);
        const string garbage = "{\"desires\":[null,{\"profileId\":\"wa\",\"chatId\":\"\",\"kind\":\"business\"},"
                               + "{\"profileId\":\"wa\",\"chatId\":\"x@c.us\",\"kind\":\"Business\"},"
                               + "{\"profileId\":\"-1\",\"chatId\":\"y@c.us\",\"kind\":\"personal\"},"
                               + "{\"profileId\":\"wa\",\"chatId\":\"z@c.us\",\"kind\":\"personal\"}],"
                               + "\"connections\":[null,{\"profileId\":\"\"},{\"profileId\":\"tg\"}]}";
        var cleaned = ChatAudienceSyncQueue.Deserialize(garbage, 1);
        CollectionAssert.AreEqual(new[] { "z@c.us" }, cleaned.desires.Select(d => d.chatId).ToArray(), "entries that could never travel are dropped");
        CollectionAssert.AreEqual(new[] { "tg" }, cleaned.connections.Select(c => c.profileId).ToArray());
        StringAssert.DoesNotContain("IsPending", ChatAudienceSyncQueue.Serialize(m), "only data is persisted");
    }

    [Test]
    public void Queue_RejectedOn403_NotRetriedUntilRearmed()
    {
        var m = M();
        ChatAudienceSyncQueue.AddConnection(m, Bot0, "wa", 1000);
        var connection = ChatAudienceSyncQueue.FindConnection(m, "wa");
        Assert.IsFalse(ChatAudienceSyncQueue.ApplyConnectedResult(m, connection, connection.reports, ChatAudienceSyncQueue.BatchStatus.Rejected, 1500));
        Assert.AreEqual(ChatAudienceSyncQueue.StateRejected, connection.state, "the server's word, told apart from a network fault");
        Assert.IsNull(ChatAudienceSyncQueue.NextDueConnection(m, long.MaxValue), "a refused report is not retried in this launch");
        Assert.IsFalse(ChatAudienceSyncQueue.AnyPending(m), "…and does not keep the loop waiting");
        Assert.IsTrue(ChatAudienceSyncQueue.Rearm(m, 9000), "a return to the app / a launch re-arms it");
        Assert.AreSame(connection, ChatAudienceSyncQueue.NextDueConnection(m, 9000));
        Assert.AreEqual(0, connection.attempts);

        // A move the server refused is the write failure: it is taken off and never comes back on its own.
        ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", new[] { "a@c.us", "b@c.us" }, AudienceKind.Business, 2000, out _);
        var outcome = Answer(m, "wa", ChatAudienceSyncQueue.BatchStatus.Rejected, null, 2500);
        CollectionAssert.AreEquivalent(new[] { "a@c.us", "b@c.us" }, outcome.Rejected);
        CollectionAssert.AreEquivalent(new[] { "a@c.us", "b@c.us" }, outcome.NotAccepted(), "the chats go back to where they were");
        Assert.IsEmpty(outcome.Settled);
        Assert.IsEmpty(m.desires, "the server did not accept them: the moves are taken off");
        ChatAudienceSyncQueue.Rearm(m, 10_000);
        Assert.IsEmpty(ChatAudienceSyncQueue.NextDueBatch(m, 10_000), "…and a re-arm never brings them back");
    }

    [Test]
    public void Queue_Transient_BacksOff_ThenParks_NeverDropsTheMove()
    {
        var m = M();
        ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", OneChat, AudienceKind.Personal, 1000, out _);
        var desire = ChatAudienceSyncQueue.FindDesire(m, "wa", "a@c.us");
        var outcome = Answer(m, "wa", ChatAudienceSyncQueue.BatchStatus.Transient, null, 1000);
        Assert.IsEmpty(outcome.NotAccepted(), "no answer is no verdict");
        Assert.AreEqual(3000, desire.nextAttemptAtMs, "2 s, 5 s, 15 s, 60 s, then every 5 min");
        Assert.IsEmpty(ChatAudienceSyncQueue.NextDueBatch(m, 2999));
        Assert.AreSame(desire, ChatAudienceSyncQueue.NextDueBatch(m, 3000).Single());
        for (int i = 1; i < ChatAudienceSyncQueue.MaxAttemptsPerLaunch; i++)
            Answer(m, "wa", ChatAudienceSyncQueue.BatchStatus.Transient, null, 1000);
        Assert.AreEqual(ChatAudienceSyncQueue.StateFailed, desire.state, "at most 8 attempts per launch");
        Assert.Contains(desire, m.desires, "a fault never drops the owner's move — it waits for a re-arm");
        Assert.IsFalse(ChatAudienceSyncQueue.AnyPending(m));
        Assert.AreEqual(300_000, ChatAudienceSyncQueue.BackoffMs(10), "the longest pause is five minutes");
        Assert.AreEqual(8, ChatAudienceSyncQueue.MaxAttemptsPerLaunch);

        var reArmed = M();
        ChatAudienceSyncQueue.AddDesires(reArmed, Bot0, "wa", new[] { "a@c.us", "b@c.us" }, AudienceKind.Business, 1000, out _);
        var partial = Answer(reArmed, "wa", ChatAudienceSyncQueue.BatchStatus.Answered,
            Rows(("a@c.us", true, AudienceKind.Business, 1000, null)), 2000);
        Assert.AreEqual(1, partial.Settled.Count);
        var unnamed = ChatAudienceSyncQueue.FindDesire(reArmed, "wa", "b@c.us");
        Assert.IsTrue(unnamed.IsPending && unnamed.attempts == 1, "a chat the answer did not name is retried like a fault");
    }

    [Test]
    public void Queue_Answer_SettlesEachChat_AndAdoptsTheServersView()
    {
        var m = M();
        long v = ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", new[] { "a@c.us", "b@c.us", "120363000000000000@g.us" },
                                                  AudienceKind.Business, 1000, out _);
        var outcome = Answer(m, "wa", ChatAudienceSyncQueue.BatchStatus.Answered, Rows(
            ("a@c.us", true, AudienceKind.Business, v, null),
            ("b@c.us", false, AudienceKind.Personal, v + 5000, null),
            ("120363000000000000@g.us", false, AudienceKind.Personal, 0, "nonhuman")), 2000);

        Assert.IsEmpty(m.desires, "every chat the server answered for is settled");
        Assert.AreEqual(3, outcome.Settled.Count);
        var applied = outcome.Settled.Single(s => s.ChatId == "a@c.us");
        Assert.IsTrue(applied.Applied && applied.Kind == AudienceKind.Business && !applied.Refused);
        var newer = outcome.Settled.Single(s => s.ChatId == "b@c.us");
        Assert.IsTrue(!newer.Applied && newer.Kind == AudienceKind.Personal && !newer.Refused,
            "applied:false — the server holds a newer decision: its view is adopted");
        var group = outcome.Settled.Single(s => s.ChatId == "120363000000000000@g.us");
        Assert.IsTrue(!group.Applied && group.Refused && group.Kind == AudienceKind.Personal);
        CollectionAssert.AreEqual(new[] { "120363000000000000@g.us" }, outcome.NotAccepted(),
            "a refused chat did not take the move; a newer server decision is adopted without a word");
        Assert.IsEmpty(outcome.Rejected);
    }

    [Test]
    public void Queue_AnAnswerForAnOlderVersion_NeverTouchesTheNewerMove()
    {
        foreach (var status in new[] { ChatAudienceSyncQueue.BatchStatus.Answered, ChatAudienceSyncQueue.BatchStatus.Rejected,
                                       ChatAudienceSyncQueue.BatchStatus.Transient })
        {
            var m = M();
            long v1 = ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", OneChat, AudienceKind.Business, 1000, out _);
            var sent = ChatAudienceSyncQueue.Snapshot(ChatAudienceSyncQueue.NextDueBatch(m, 1000));
            long v2 = ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", OneChat, AudienceKind.Personal, 2000, out _);   // moved back meanwhile
            var outcome = ChatAudienceSyncQueue.ApplySetResult(m, "wa", sent, status, Rows(("a@c.us", true, AudienceKind.Business, v1, null)), 3000);
            var desire = ChatAudienceSyncQueue.FindDesire(m, "wa", "a@c.us");
            Assert.IsNotNull(desire, $"{status}: the newer move is still owed");
            Assert.IsTrue(desire.version == v2 && desire.Kind == AudienceKind.Personal && desire.attempts == 0 && desire.nextAttemptAtMs == 2000,
                $"a stale {status} must leave the unsent move due, untouched");
            Assert.IsEmpty(outcome.Settled, status.ToString());
            Assert.IsEmpty(outcome.NotAccepted(), status.ToString());
        }
    }

    [Test]
    public void Queue_ServerVersions_RaiseTheFloorOfTheNextMove()
    {
        var m = M();
        ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", OneChat, AudienceKind.Business, 1000, out _);
        Answer(m, "wa", ChatAudienceSyncQueue.BatchStatus.Answered, Rows(("a@c.us", false, AudienceKind.Personal, 5_000_000, null)), 2000);
        Assert.Greater(ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", OneChat, AudienceKind.Business, 3000, out _), 5_000_000,
            "a newer server row is never fought with an older version again");

        Assert.IsTrue(ChatAudienceSyncQueue.RaiseFloor(m, 9_000_000), "a version the list reported");
        Assert.IsFalse(ChatAudienceSyncQueue.RaiseFloor(m, 10), "a lower one changes nothing");
        Assert.Greater(ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", OneChat, AudienceKind.Personal, 4000, out _), 9_000_000);
    }

    // ── batching, holds, reports ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Queue_OneRequestPerProfile_AtMostFiftyChats()
    {
        var m = M();
        var chats = Enumerable.Range(0, 120).Select(i => $"7701{i:0000000}@c.us").ToList();
        ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", chats, AudienceKind.Business, 1000, out _);
        ChatAudienceSyncQueue.AddDesires(m, Bot0, "tg", new[] { "555" }, AudienceKind.Personal, 1000, out _);

        var sizes = new List<int>();
        var profiles = new List<string>();
        for (var batch = ChatAudienceSyncQueue.NextDueBatch(m, 1000); batch.Count > 0; batch = ChatAudienceSyncQueue.NextDueBatch(m, 1000))
        {
            Assert.AreEqual(1, batch.Select(d => d.profileId).Distinct().Count(), "one request names one profile");
            sizes.Add(batch.Count);
            profiles.Add(batch[0].profileId);
            var sent = ChatAudienceSyncQueue.Snapshot(batch);
            ChatAudienceSyncQueue.ApplySetResult(m, batch[0].profileId, sent, ChatAudienceSyncQueue.BatchStatus.Answered, AllApplied(sent), 1100);
        }
        CollectionAssert.AreEqual(new[] { 50, 50, 20, 1 }, sizes, "at most fifty chats per request, in the order they were moved");
        CollectionAssert.AreEqual(new[] { "wa", "wa", "wa", "tg" }, profiles);
        Assert.IsEmpty(m.desires);
    }

    [Test]
    public void Queue_HeldBot_SendsNothing_UntilReleased()
    {
        var m = M();
        ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa0", OneChat, AudienceKind.Business, 1000, out _);
        ChatAudienceSyncQueue.AddConnection(m, Bot0, "tg0", 1000);
        ChatAudienceSyncQueue.AddDesires(m, "Bot1", "wa1", OneChat, AudienceKind.Business, 1000, out _);
        var held = new HashSet<string> { Bot0 };   // Manager.DeleteBotOnServer is deleting Bot0

        Assert.IsNull(ChatAudienceSyncQueue.NextDueConnection(m, 5000, held), "no report of a bot being deleted");
        CollectionAssert.AreEqual(new[] { "wa1" }, ChatAudienceSyncQueue.NextDueBatch(m, 5000, held).Select(d => d.profileId).ToArray(),
            "no move of a bot being deleted — it would land after Delete Bot Files and recreate its rows");
        held.Add("Bot1");
        Assert.IsFalse(ChatAudienceSyncQueue.AnyPending(m, held), "only held entries left: the loop rests until a release");
        Assert.IsTrue(ChatAudienceSyncQueue.AnyPending(m), "…they are still owed");

        held.Clear();                               // the server kept the bots: Release
        Assert.IsNotNull(ChatAudienceSyncQueue.NextDueConnection(m, 5000, held));
        var bot0 = ChatAudienceSyncQueue.FindDesire(m, "wa0", "a@c.us");
        Assert.IsTrue(bot0.IsPending && bot0.attempts == 0, "held, not failed: untouched until the server's verdict");
    }

    [Test]
    public void Queue_ConnectedAnswer_SettlesOnlyTheReportItWasSentFor()
    {
        var m = M();
        ChatAudienceSyncQueue.AddConnection(m, Bot0, "wa", 1000);
        var connection = ChatAudienceSyncQueue.FindConnection(m, "wa");
        int sent = connection.reports;
        ChatAudienceSyncQueue.AddConnection(m, Bot0, "wa", 1500);   // paired again while the first report was on the wire
        Assert.AreEqual(1, m.connections.Count, "one report per profile");
        Assert.IsFalse(ChatAudienceSyncQueue.ApplyConnectedResult(m, connection, sent, ChatAudienceSyncQueue.BatchStatus.Answered, 2000));
        Assert.AreSame(connection, ChatAudienceSyncQueue.FindConnection(m, "wa"), "the newer report still goes");
        Assert.IsTrue(connection.IsPending);

        Assert.IsTrue(ChatAudienceSyncQueue.ApplyConnectedResult(m, connection, connection.reports, ChatAudienceSyncQueue.BatchStatus.Answered, 2500));
        Assert.IsNull(ChatAudienceSyncQueue.FindConnection(m, "wa"), "answered: nothing more to report");

        ChatAudienceSyncQueue.AddConnection(m, Bot0, "tg", 3000);
        var tg = ChatAudienceSyncQueue.FindConnection(m, "tg");
        for (int i = 0; i < ChatAudienceSyncQueue.MaxAttemptsPerLaunch; i++)
            ChatAudienceSyncQueue.ApplyConnectedResult(m, tg, tg.reports, ChatAudienceSyncQueue.BatchStatus.Transient, 3000);
        Assert.AreEqual(ChatAudienceSyncQueue.StateFailed, tg.state, "a report backs off and parks like a move");
        Assert.IsTrue(ChatAudienceSyncQueue.Rearm(m, 4000));
        Assert.IsTrue(tg.IsPending && tg.attempts == 0);

        ChatAudienceSyncQueue.DropConnection(m, tg);
        Assert.IsNull(ChatAudienceSyncQueue.FindConnection(m, "tg"), "a report for a profile the session no longer lists goes");
        ChatAudienceSyncQueue.AddConnection(m, Bot0, Bot.UnauthedProfileSentinel, 5000);
        Assert.IsEmpty(m.connections, "a never-paired channel has nothing to report");
    }

    [Test]
    public void Queue_Rearm_RetriesEverythingUndelivered_AndFocusAtMostOnceAMinute()
    {
        var m = M();
        ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa", OneChat, AudienceKind.Business, 1000, out _);
        for (int i = 0; i < 3; i++) Answer(m, "wa", ChatAudienceSyncQueue.BatchStatus.Transient, null, 1000);   // waiting out a backoff
        Assert.IsTrue(ChatAudienceSyncQueue.Rearm(m, 2000));
        var desire = ChatAudienceSyncQueue.FindDesire(m, "wa", "a@c.us");
        Assert.IsTrue(desire.IsPending && desire.attempts == 0 && desire.nextAttemptAtMs == 2000, "a move in a backoff is tried at once");
        Assert.IsFalse(ChatAudienceSyncQueue.Rearm(M(), 2000), "an empty queue re-arms nothing");

        Assert.IsTrue(ChatAudienceSyncQueue.ShouldRearmOnFocus(true, 100_000, 0), "the first return to the app");
        Assert.IsFalse(ChatAudienceSyncQueue.ShouldRearmOnFocus(true, 100_000 + ChatAudienceSyncQueue.FocusRearmGapMs - 1, 100_000));
        Assert.IsTrue(ChatAudienceSyncQueue.ShouldRearmOnFocus(true, 100_000 + ChatAudienceSyncQueue.FocusRearmGapMs, 100_000));
        Assert.IsFalse(ChatAudienceSyncQueue.ShouldRearmOnFocus(false, 999_999_999, 0), "losing focus re-arms nothing");
        Assert.IsTrue(ChatAudienceSyncQueue.ShouldRearmOnFocus(true, 50_000, 100_000), "a clock that moved back never blocks it");
        Assert.AreEqual(60_000, ChatAudienceSyncQueue.FocusRearmGapMs);
    }

    [Test]
    public void Queue_ForgetBot_DropsTheBotsEntries_ByNameOrProfile()
    {
        var m = M();
        ChatAudienceSyncQueue.AddDesires(m, Bot0, "wa0", OneChat, AudienceKind.Business, 1000, out _);
        ChatAudienceSyncQueue.AddConnection(m, Bot0, "tg0", 1000);
        ChatAudienceSyncQueue.AddConnection(m, "", "wa0-old", 1000);   // a report whose bot was not known when it was made
        long v = ChatAudienceSyncQueue.AddDesires(m, "Bot1", "wa1", OneChat, AudienceKind.Business, 1000, out _);

        var touched = ChatAudienceSyncQueue.ForgetBot(m, Bot0, new[] { "wa0-old" });
        CollectionAssert.AreEquivalent(new[] { "wa0", "tg0", "wa0-old" }, touched, "the profiles whose entries went");
        CollectionAssert.AreEqual(new[] { "wa1" }, m.desires.Select(d => d.profileId).ToArray(), "another bot keeps its moves");
        Assert.IsEmpty(m.connections);

        ChatAudienceSyncQueue.ForgetAll(m);
        Assert.IsEmpty(m.desires);
        Assert.IsEmpty(m.connections);
        Assert.Greater(ChatAudienceSyncQueue.AddDesires(m, "Bot2", "wa2", OneChat, AudienceKind.Business, 1, out _), v,
            "versions never go back, even after the full wipe");
    }
}

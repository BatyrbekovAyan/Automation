using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Pins the outgoing-echo dedupe rule (bug 2026-09-04: a picked «Вместе» suggestion sent from
/// the composer rendered TWICE — one bubble ✓ (the optimistic send's ack) and an identical one
/// ✓✓ (the 3s live poll's echo of the same server message) — and deduplicated only on reopen).
///
/// The rule: an outgoing send's optimistic bubble MUST live in the SAME list the echo-reconcile
/// searches (ChatManager._activeChatCache, which the live poll hands to SyncLatestMessages as
/// cachedList). The send path used to mutate a detached ChatHistoryCache.LoadHistory list
/// instead, so ReconcileGhostSend could not find the optimistic bubble, reported "not
/// recovered", and the merge appended the echo as a second bubble.
/// </summary>
public class OutgoingSendCacheTests
{
    private static MessageViewModel Msg(string id, long time, int sequence = 0,
                                        DeliveryStatus status = DeliveryStatus.Delivered)
        => new MessageViewModel
        {
            messageId      = id,
            chatId         = "77010000000@c.us",
            type           = MessageType.Chat,
            text           = id,
            timestamp      = time,
            sequence       = sequence,
            deliveryStatus = status,
        };

    // _activeChatCache is NEWEST-FIRST at every assignment site (see RecentMessageWindow).
    private static List<MessageViewModel> NewestFirstCache()
        => new List<MessageViewModel> { Msg("SRV_NEW", 2_000), Msg("SRV_OLD", 1_000) };

    // ── Insert: keeps the newest-first invariant ──────────────────────────

    [Test]
    public void Insert_PutsAJustSentMessageAtTheFront()
    {
        var cache = NewestFirstCache();

        OutgoingSendCache.Insert(cache, Msg("sending_1", 3_000, sequence: 7, status: DeliveryStatus.Pending));

        Assert.AreEqual(new[] { "sending_1", "SRV_NEW", "SRV_OLD" }, Ids(cache),
            "A just-sent message is the newest; a blind Add would append it to the TAIL of a " +
            "newest-first list and break RecentMessageWindow.TakeNewest.");
    }

    [Test]
    public void Insert_PlacesALaggingTimestampAtItsCanonicalPosition()
    {
        var cache = NewestFirstCache();

        // Device clock behind the server's: the message still belongs between the two.
        OutgoingSendCache.Insert(cache, Msg("sending_2", 1_500, status: DeliveryStatus.Pending));

        Assert.AreEqual(new[] { "SRV_NEW", "sending_2", "SRV_OLD" }, Ids(cache));
    }

    [Test]
    public void Insert_SameSecondBreaksTheTieOnSequence()
    {
        // The worst same-second server sequence is one below the page size (WithinSecondSequence
        // counts older ties within one response); the optimistic send starts at the floor.
        var cache = new List<MessageViewModel> { Msg("SRV_A", 2_000, sequence: ChatManager.MessagesPerPage - 1) };

        OutgoingSendCache.Insert(cache, Msg("sending_3", 2_000,
            sequence: OutgoingSendCache.LocalSendSequenceFloor, status: DeliveryStatus.Pending));

        Assert.AreEqual(new[] { "sending_3", "SRV_A" }, Ids(cache),
            "Optimistic sends carry a high local sequence, which orders them newest within the second.");
    }

    [Test]
    public void LocalSendSequenceFloor_OutranksAnyWithinSecondServerSequence()
    {
        // A pathological page where every message shares one second: the newest carries the
        // highest within-second sequence a response can produce.
        long[] times = new long[ChatManager.MessagesPerPage];
        for (int i = 0; i < times.Length; i++) times[i] = 2_000;

        Assert.Greater(OutgoingSendCache.LocalSendSequenceFloor, MessageOrder.WithinSecondSequence(times, 0),
            "If the floor ever drops below the page size, a same-second server message sorts NEWER than the " +
            "just-sent bubble and Insert places the send behind it.");
    }

    [Test]
    public void Insert_IntoAnEmptyCacheIsTheOnlyEntry()
    {
        var cache = new List<MessageViewModel>();
        OutgoingSendCache.Insert(cache, Msg("sending_4", 1_000, status: DeliveryStatus.Pending));
        Assert.AreEqual(new[] { "sending_4" }, Ids(cache));
    }

    [Test]
    public void Insert_NullListOrMessageIsANoOp()
    {
        Assert.DoesNotThrow(() => OutgoingSendCache.Insert(null, Msg("x", 1)));
        var cache = NewestFirstCache();
        OutgoingSendCache.Insert(cache, null);
        Assert.AreEqual(2, cache.Count);
    }

    // ── AdoptServerId: the reconcile the merge asks about ─────────────────

    [Test]
    public void AdoptServerId_SwapsTheOptimisticIdInPlace()
    {
        var cache = NewestFirstCache();
        OutgoingSendCache.Insert(cache, Msg("sending_5", 3_000, status: DeliveryStatus.Pending));

        bool found = OutgoingSendCache.AdoptServerId(cache, "sending_5", "SRV_ECHO", DeliveryStatus.Sent);

        Assert.IsTrue(found);
        Assert.AreEqual(new[] { "SRV_ECHO", "SRV_NEW", "SRV_OLD" }, Ids(cache));
        Assert.AreEqual(DeliveryStatus.Sent, cache[0].deliveryStatus);
    }

    [Test]
    public void AdoptServerId_ReturnsFalseWhenTheOptimisticBubbleIsAbsent()
    {
        // THE BUG, expressed as a rule: this false is what ReconcileGhostSend returned, and
        // the merge reads a false as "not a ghost recovery" and appends the echo as a second
        // bubble. It must only ever be reachable when the bubble genuinely is not cached
        // (evicted past the 100-message cap) — never because the send wrote to another list.
        var cache = NewestFirstCache();

        Assert.IsFalse(OutgoingSendCache.AdoptServerId(cache, "sending_6", "SRV_ECHO", DeliveryStatus.Sent));
        Assert.AreEqual(new[] { "SRV_NEW", "SRV_OLD" }, Ids(cache), "A miss must not mutate the cache.");
    }

    [Test]
    public void OptimisticSendThenEcho_ReconcilesInsteadOfAppending()
    {
        // End-to-end shape of the live race: the poll's echo lands BEFORE the send's own ack.
        var cache = NewestFirstCache();
        var optimistic = Msg("sending_7", 3_000, sequence: 4, status: DeliveryStatus.Pending);

        OutgoingSendCache.Insert(cache, optimistic);                       // send fires
        int countAfterSend = cache.Count;

        bool reconciled = OutgoingSendCache.AdoptServerId(                 // poll echoes it back
            cache, "sending_7", "SRV_ECHO", DeliveryStatus.Delivered);

        Assert.IsTrue(reconciled, "The echo must land ON the optimistic bubble.");
        Assert.AreEqual(countAfterSend, cache.Count, "Reconciling must never grow the cache.");
        Assert.AreEqual(1, CountOf(cache, "SRV_ECHO"), "Exactly one bubble carries the server id.");
        Assert.AreEqual(0, CountOf(cache, "sending_7"), "The temp id is gone once adopted.");
        Assert.AreSame(optimistic, cache[0], "Reconcile mutates the existing VM, never replaces it.");
    }

    [Test]
    public void AdoptServerId_NullOrEmptyArgumentsAreANoOp()
    {
        var cache = NewestFirstCache();
        Assert.IsFalse(OutgoingSendCache.AdoptServerId(null, "t", "s", DeliveryStatus.Sent));
        Assert.IsFalse(OutgoingSendCache.AdoptServerId(cache, null, "s", DeliveryStatus.Sent));
        Assert.IsFalse(OutgoingSendCache.AdoptServerId(cache, "t", "", DeliveryStatus.Sent));
        Assert.AreEqual(2, cache.Count);
    }

    // ── UsesLiveList: which list a send-path mutation lands in ──────────

    [Test]
    public void UsesLiveList_OpenChatOnTheActiveBot_TakesTheLiveList()
        => Assert.IsTrue(OutgoingSendCache.UsesLiveList(hasLiveList: true, "chatA", "chatA", "/bot1/wa", "/bot1/wa"));

    [Test]
    public void UsesLiveList_AnotherChat_FallsBackToDisk()
        => Assert.IsFalse(OutgoingSendCache.UsesLiveList(true, "chatB", "chatA", "/bot1/wa", "/bot1/wa"));

    [Test]
    public void UsesLiveList_SendCompletingAfterABotSwitch_FallsBackToDisk()
    {
        // The send snapshotted bot 1's root; the live list now belongs to bot 2's open chat —
        // which can carry the SAME chat id (one customer, two bots).
        Assert.IsFalse(OutgoingSendCache.UsesLiveList(true, "chatA", "chatA", "/bot1/wa", "/bot2/wa"));
    }

    [Test]
    public void UsesLiveList_NoLiveListYet_FallsBackToDisk()
        => Assert.IsFalse(OutgoingSendCache.UsesLiveList(hasLiveList: false, "chatA", "chatA", "/bot1/wa", "/bot1/wa"));

    [Test]
    public void UsesLiveList_EmptyIds_FallBackToDisk()
    {
        Assert.IsFalse(OutgoingSendCache.UsesLiveList(true, null, null, "/bot1/wa", "/bot1/wa"));
        Assert.IsFalse(OutgoingSendCache.UsesLiveList(true, "", "", "/bot1/wa", "/bot1/wa"));
        Assert.IsFalse(OutgoingSendCache.UsesLiveList(true, "chatA", "chatA", "", ""));
    }

    // ── the echo-reconcile overload adopts the server's order keys ──────

    [Test]
    public void AdoptServerId_EchoForm_AdoptsTimestampAndSequence()
    {
        var cache = NewestFirstCache();
        OutgoingSendCache.Insert(cache, Msg("sending_8", 3_000, sequence: 1000, status: DeliveryStatus.Pending));

        bool found = OutgoingSendCache.AdoptServerId(cache, "sending_8", "SRV_ECHO", DeliveryStatus.Delivered,
            timestamp: 2_990, sequence: 2);

        Assert.IsTrue(found);
        Assert.AreEqual(2_990, cache[0].timestamp, "the device clock ran 10s ahead — the server's time wins");
        Assert.AreEqual(2, cache[0].sequence);
        Assert.AreEqual(DeliveryStatus.Delivered, cache[0].deliveryStatus);
    }

    // ── StatusToAnnounce: a late ack must never step ✓✓ back to ✓ ───────

    [Test]
    public void EchoThenLateAck_AnnouncesTheEchoesFresherStatus()
    {
        // The exact ordering the fix makes work: the poll's echo lands first (Delivered), the
        // send's own ack second. The ack's adopt misses (the temp id is gone) — and must not
        // announce a hard-coded Sent over the ✓✓ the echo already painted.
        var cache = NewestFirstCache();
        OutgoingSendCache.Insert(cache, Msg("sending_9", 3_000, sequence: 1000, status: DeliveryStatus.Pending));
        Assert.IsTrue(OutgoingSendCache.AdoptServerId(cache, "sending_9", "SRV_ECHO", DeliveryStatus.Delivered, 3_000, 0));

        bool adopted = OutgoingSendCache.AdoptServerId(cache, "sending_9", "SRV_ECHO", DeliveryStatus.Sent);

        Assert.IsFalse(adopted, "the echo already renamed the bubble");
        Assert.AreEqual(DeliveryStatus.Delivered,
            OutgoingSendCache.StatusToAnnounce(cache, adopted, "SRV_ECHO", DeliveryStatus.Sent));
        Assert.AreEqual(DeliveryStatus.Delivered, cache[0].deliveryStatus, "a missed adopt must not touch the VM");
    }

    [Test]
    public void StatusToAnnounce_AckThatAdopted_AnnouncesTheAck()
    {
        var cache = NewestFirstCache();
        OutgoingSendCache.Insert(cache, Msg("sending_10", 3_000, status: DeliveryStatus.Pending));
        bool adopted = OutgoingSendCache.AdoptServerId(cache, "sending_10", "SRV_1", DeliveryStatus.Sent);

        Assert.IsTrue(adopted);
        Assert.AreEqual(DeliveryStatus.Sent, OutgoingSendCache.StatusToAnnounce(cache, adopted, "SRV_1", DeliveryStatus.Sent));
    }

    [TestCase(DeliveryStatus.Read, DeliveryStatus.Read)]
    [TestCase(DeliveryStatus.Delivered, DeliveryStatus.Delivered)]
    [TestCase(DeliveryStatus.Sent, DeliveryStatus.Sent)]
    [TestCase(DeliveryStatus.Pending, DeliveryStatus.Sent)]   // an ack always lifts a bubble out of Pending
    [TestCase(DeliveryStatus.Failed, DeliveryStatus.Sent)]    // and out of a stale Failed
    [TestCase(DeliveryStatus.None, DeliveryStatus.Sent)]
    public void StatusToAnnounce_MissedAdopt_NeverDescendsTheDeliveryLadder(DeliveryStatus cached, DeliveryStatus expected)
    {
        var cache = new List<MessageViewModel> { Msg("SRV_X", 3_000, status: cached) };
        Assert.AreEqual(expected, OutgoingSendCache.StatusToAnnounce(cache, adopted: false, "SRV_X", DeliveryStatus.Sent));
    }

    [Test]
    public void StatusToAnnounce_UncachedBubble_StillAnnouncesTheAck()
    {
        // Evicted past the cap: the rendered bubble must still leave Pending.
        Assert.AreEqual(DeliveryStatus.Sent,
            OutgoingSendCache.StatusToAnnounce(NewestFirstCache(), adopted: false, "SRV_GONE", DeliveryStatus.Sent));
        Assert.AreEqual(DeliveryStatus.Sent,
            OutgoingSendCache.StatusToAnnounce(null, adopted: false, "SRV_GONE", DeliveryStatus.Sent));
    }

    // ── MergeOptimisticRows: the first-open window ──────────────────────

    private static MessageViewModel Outgoing(string id, long time, string text, MessageType type = MessageType.Chat)
    {
        var m = Msg(id, time, status: DeliveryStatus.Pending);
        m.isIncoming = false;
        m.text = text;
        m.type = type;
        return m;
    }

    private static MessageViewModel Incoming(string id, long time, string text)
    {
        var m = Msg(id, time);
        m.isIncoming = true;
        m.text = text;
        return m;
    }

    [Test]
    public void Merge_CarriesAnOptimisticRowThePageDoesNotHave()
    {
        // The window: the send wrote its row to disk while the first page was in flight; the
        // page arrives without it and is about to become the live list.
        var page = new List<MessageViewModel> { Incoming("SRV_2", 2_000, "hi"), Incoming("SRV_1", 1_000, "hello") };
        var disk = new List<MessageViewModel> { Outgoing("sending_1", 3_000, "reply") };

        int carried = OutgoingSendCache.MergeOptimisticRows(page, disk);

        Assert.AreEqual(1, carried);
        Assert.AreEqual(new[] { "sending_1", "SRV_2", "SRV_1" }, Ids(page), "carried in newest-first order");
    }

    [Test]
    public void Merge_DropsATextRowWhoseEchoIsAlreadyInThePage()
    {
        // The page already contains the server's copy of the send (same text, seconds apart):
        // carrying the temp row too would render the bubble twice.
        var page = new List<MessageViewModel> { Outgoing("SRV_ECHO", 3_001, "reply"), Incoming("SRV_1", 1_000, "hello") };
        var disk = new List<MessageViewModel> { Outgoing("sending_1", 3_000, "reply") };

        Assert.AreEqual(0, OutgoingSendCache.MergeOptimisticRows(page, disk));
        Assert.AreEqual(new[] { "SRV_ECHO", "SRV_1" }, Ids(page));
    }

    [Test]
    public void Merge_KeepsATextRowWhoseLookalikeIsOutsideTheEchoWindow()
    {
        var page = new List<MessageViewModel> { Outgoing("SRV_OLD", 3_000 - OutgoingSendCache.EchoWindowSeconds - 1, "reply") };
        var disk = new List<MessageViewModel> { Outgoing("sending_1", 3_000, "reply") };

        Assert.AreEqual(1, OutgoingSendCache.MergeOptimisticRows(page, disk));
    }

    [Test]
    public void Merge_KeepsAMediaRow_ACaptionIsTooWeakAKeyToDropOn()
    {
        var page = new List<MessageViewModel> { Outgoing("SRV_IMG", 3_000, "", MessageType.Image) };
        var disk = new List<MessageViewModel> { Outgoing("staging_1", 3_001, "", MessageType.Image) };

        Assert.AreEqual(1, OutgoingSendCache.MergeOptimisticRows(page, disk));
    }

    [Test]
    public void Merge_KeepsAnAckedRowThePageIsTooOldToHave()
    {
        // The ack landed before the page did: the row already carries its server id, and the
        // page (fetched earlier) does not contain it — a real message, not a duplicate.
        var page = new List<MessageViewModel> { Incoming("SRV_1", 1_000, "hello") };
        var acked = Outgoing("SRV_9", 3_000, "reply");
        acked.deliveryStatus = DeliveryStatus.Sent;

        Assert.AreEqual(1, OutgoingSendCache.MergeOptimisticRows(page, new List<MessageViewModel> { acked }));
        Assert.AreEqual("SRV_9", page[0].messageId);
    }

    [Test]
    public void Merge_SkipsRowsThePageAlreadyHasById_AndIncomingRows()
    {
        var page = new List<MessageViewModel> { Outgoing("SRV_9", 3_000, "reply"), Incoming("SRV_1", 1_000, "hello") };
        var disk = new List<MessageViewModel> { Outgoing("SRV_9", 3_000, "reply"), Incoming("SRV_0", 500, "stale") };

        Assert.AreEqual(0, OutgoingSendCache.MergeOptimisticRows(page, disk));
        Assert.AreEqual(2, page.Count);
    }

    [Test]
    public void Merge_NullArgumentsAreANoOp()
    {
        Assert.AreEqual(0, OutgoingSendCache.MergeOptimisticRows(null, new List<MessageViewModel>()));
        Assert.AreEqual(0, OutgoingSendCache.MergeOptimisticRows(new List<MessageViewModel>(), null));
    }

    [Test]
    public void IsTempId_RecognisesBothOptimisticKinds()
    {
        Assert.IsTrue(OutgoingSendCache.IsTempId("sending_1725000000000"));
        Assert.IsTrue(OutgoingSendCache.IsTempId("staging_1725000000000"));
        Assert.IsFalse(OutgoingSendCache.IsTempId("3EB0ABCDEF"));
        Assert.IsFalse(OutgoingSendCache.IsTempId(null));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string[] Ids(List<MessageViewModel> list)
    {
        var ids = new string[list.Count];
        for (int i = 0; i < list.Count; i++) ids[i] = list[i].messageId;
        return ids;
    }

    private static int CountOf(List<MessageViewModel> list, string id)
    {
        int n = 0;
        foreach (var m in list) if (m.messageId == id) n++;
        return n;
    }
}

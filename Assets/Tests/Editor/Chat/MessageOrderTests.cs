using System.Collections.Generic;
using NUnit.Framework;

public class MessageOrderTests
{
    private static MessageViewModel Vm(long ts, int seq = 0, string id = "m")
        => new MessageViewModel { timestamp = ts, sequence = seq, messageId = id };

    private static RawMessage Raw(long time) => new RawMessage { time = time };

    // --- ResponseTimes -------------------------------------------------------

    [Test]
    public void ResponseTimes_ExtractsTimesInResponseOrder()
    {
        var raws = new List<RawMessage> { Raw(300), Raw(200), Raw(100) };
        CollectionAssert.AreEqual(new long[] { 300, 200, 100 }, MessageOrder.ResponseTimes(raws));
    }

    // --- WithinSecondSequence ------------------------------------------------
    // Responses are newest-first. Sequence = position counted from the OLDEST
    // member of the same-second tie group, so a message keeps the same value
    // when a later (overlapping) fetch window sees the group again with newer
    // messages prepended above it.

    [Test]
    public void WithinSecondSequence_UniqueTimes_ReturnsZero()
    {
        var times = new long[] { 100, 90, 80 };
        Assert.AreEqual(0, MessageOrder.WithinSecondSequence(times, 0));
        Assert.AreEqual(0, MessageOrder.WithinSecondSequence(times, 1));
        Assert.AreEqual(0, MessageOrder.WithinSecondSequence(times, 2));
    }

    [Test]
    public void WithinSecondSequence_TieGroup_CountsFromOldestMember()
    {
        var times = new long[] { 100, 90, 90, 90, 80 };
        Assert.AreEqual(2, MessageOrder.WithinSecondSequence(times, 1)); // newest of the tie
        Assert.AreEqual(1, MessageOrder.WithinSecondSequence(times, 2));
        Assert.AreEqual(0, MessageOrder.WithinSecondSequence(times, 3)); // oldest of the tie
    }

    [Test]
    public void WithinSecondSequence_ConsistentAcrossOverlappingWindows()
    {
        // First fetch sees the tie group at the top of the window...
        var earlierWindow = new long[] { 90, 90, 80 };
        Assert.AreEqual(1, MessageOrder.WithinSecondSequence(earlierWindow, 0));
        Assert.AreEqual(0, MessageOrder.WithinSecondSequence(earlierWindow, 1));

        // ...a later fetch sees the same group shifted down by a newer arrival.
        var laterWindow = new long[] { 95, 90, 90, 80 };
        Assert.AreEqual(1, MessageOrder.WithinSecondSequence(laterWindow, 1));
        Assert.AreEqual(0, MessageOrder.WithinSecondSequence(laterWindow, 2));
    }

    // --- Compare (ascending conversation order) -------------------------------

    [Test]
    public void Compare_EarlierTimestamp_SortsFirst()
    {
        Assert.Less(MessageOrder.Compare(Vm(10), Vm(20)), 0);
        Assert.Greater(MessageOrder.Compare(Vm(20), Vm(10)), 0);
    }

    [Test]
    public void Compare_SameSecond_LowerSequence_SortsFirst()
    {
        Assert.Less(MessageOrder.Compare(Vm(10, seq: 0), Vm(10, seq: 1)), 0);
        Assert.Greater(MessageOrder.Compare(Vm(10, seq: 3), Vm(10, seq: 1)), 0);
    }

    [Test]
    public void Compare_SameKeys_FallsBackToMessageIdOrdinal()
    {
        Assert.Less(MessageOrder.Compare(Vm(10, 0, "AAA"), Vm(10, 0, "BBB")), 0);
        Assert.Greater(MessageOrder.Compare(Vm(10, 0, "BBB"), Vm(10, 0, "AAA")), 0);
    }

    [Test]
    public void Compare_IdenticalKeys_ReturnsZero()
        => Assert.AreEqual(0, MessageOrder.Compare(Vm(10, 0, "AAA"), Vm(10, 0, "AAA")));

    [Test]
    public void Descending_IsExactInverseOfAscending()
    {
        var older = Vm(10, 0, "AAA");
        var newer = Vm(10, 1, "BBB");
        Assert.AreEqual(MessageOrder.Ascending(older, newer), -MessageOrder.Descending(older, newer));
        Assert.Greater(MessageOrder.Descending(older, newer), 0);
    }

    [Test]
    public void SortDescending_SameResultForAnyInitialPermutation()
    {
        // Same-second ties everywhere: the comparer must impose a total order so
        // List<T>.Sort (unstable introsort) cannot scramble ties between reopens.
        List<MessageViewModel> Build() => new List<MessageViewModel>
        {
            Vm(100, 0, "a"), Vm(90, 2, "b"), Vm(90, 1, "c"),
            Vm(90, 0, "d"), Vm(90, 0, "e"), Vm(80, 0, "f"),
        };

        var first = Build();
        var second = Build();
        second.Reverse();

        first.Sort(MessageOrder.Descending);
        second.Sort(MessageOrder.Descending);

        var firstIds = first.ConvertAll(m => m.messageId);
        var secondIds = second.ConvertAll(m => m.messageId);
        CollectionAssert.AreEqual(firstIds, secondIds);
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "e", "d", "f" }, firstIds);
    }

    // --- InsertIndex (live-append placement) ----------------------------------
    // Returns the index of the first existing bubble that sorts strictly AFTER
    // the incoming message, or -1 when the incoming message belongs at the end.

    [Test]
    public void InsertIndex_EmptyList_ReturnsMinusOne()
        => Assert.AreEqual(-1, MessageOrder.InsertIndex(new List<MessageViewModel>(), Vm(10)));

    [Test]
    public void InsertIndex_NewerThanAll_ReturnsMinusOne()
    {
        var existing = new List<MessageViewModel> { Vm(10, 0, "a"), Vm(20, 0, "b") };
        Assert.AreEqual(-1, MessageOrder.InsertIndex(existing, Vm(30, 0, "c")));
    }

    [Test]
    public void InsertIndex_OlderArrival_ReturnsFirstNewerBubbleIndex()
    {
        var existing = new List<MessageViewModel> { Vm(10, 0, "a"), Vm(20, 0, "b"), Vm(30, 0, "c") };
        Assert.AreEqual(1, MessageOrder.InsertIndex(existing, Vm(15, 0, "x")));
    }

    [Test]
    public void InsertIndex_PlacementMatchesComparer_OnFullTies()
    {
        // Placement must agree with Compare so the live view and a re-sorted
        // reopen produce the same order — the id tiebreak decides full ties.
        var existing = new List<MessageViewModel> { Vm(10, 0, "BBB") };
        Assert.AreEqual(0, MessageOrder.InsertIndex(existing, Vm(10, 0, "AAA")));
        Assert.AreEqual(-1, MessageOrder.InsertIndex(existing, Vm(10, 0, "CCC")));
    }
}

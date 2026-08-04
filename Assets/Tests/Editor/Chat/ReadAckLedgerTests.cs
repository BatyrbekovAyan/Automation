using System.Collections.Generic;
using NUnit.Framework;

// Contract for the local read-ack bookkeeping that keeps a chat the owner just read from
// coming back with an unread badge (and an incoming cue) the moment they navigate back to
// the list, while Wappi's chats/filter unread_count is still catching up with our ack.
public class ReadAckLedgerTests
{
    private const string Chat = "111@c.us";

    private static MessageViewModel Msg(string id, bool isIncoming = true, long timestamp = 1000, int sequence = 0)
        => new MessageViewModel { messageId = id, isIncoming = isIncoming, timestamp = timestamp, sequence = sequence };

    // --- EffectiveUnread -----------------------------------------------------------------

    [Test]
    public void UnknownChat_ReportsServerCount()
    {
        var ledger = new ReadAckLedger();
        Assert.AreEqual(3, ledger.EffectiveUnread(Chat, "m9", 3));
    }

    [Test]
    public void AckedMessageIsStillTheLastOne_ReportsRead()
    {
        var ledger = new ReadAckLedger();
        ledger.Record(Chat, "m9");

        // The exact regression: the ack landed, the owner backed out, and chats/filter is
        // still answering with the pre-ack count for the very same last message.
        Assert.AreEqual(0, ledger.EffectiveUnread(Chat, "m9", 3));
    }

    [Test]
    public void NewerMessageArrivedAfterTheAck_ReportsServerCount()
    {
        var ledger = new ReadAckLedger();
        ledger.Record(Chat, "m9");

        // Self-clearing: a genuinely new arrival no longer matches, so the badge and the
        // cue both work exactly as before. This is what keeps the correction honest.
        Assert.AreEqual(1, ledger.EffectiveUnread(Chat, "m10", 1));
    }

    [Test]
    public void AckIsScopedToItsOwnChat()
    {
        var ledger = new ReadAckLedger();
        ledger.Record(Chat, "m9");
        Assert.AreEqual(2, ledger.EffectiveUnread("222@c.us", "m9", 2));
    }

    [Test]
    public void LaterAckSupersedesTheEarlierOne()
    {
        var ledger = new ReadAckLedger();
        ledger.Record(Chat, "m9");
        ledger.Record(Chat, "m10");

        Assert.AreEqual(0, ledger.EffectiveUnread(Chat, "m10", 4));
        Assert.AreEqual(4, ledger.EffectiveUnread(Chat, "m9", 4));
    }

    [Test]
    public void RowWithNoLastMessageId_ReportsServerCount()
    {
        var ledger = new ReadAckLedger();
        ledger.Record(Chat, "m9");
        Assert.AreEqual(5, ledger.EffectiveUnread(Chat, "", 5));
        Assert.AreEqual(5, ledger.EffectiveUnread(Chat, null, 5));
    }

    [Test]
    public void EmptyRecordInputIsIgnored()
    {
        var ledger = new ReadAckLedger();
        ledger.Record(Chat, "");
        ledger.Record(Chat, null);
        ledger.Record("", "m9");
        ledger.Record(null, "m9");

        Assert.AreEqual(2, ledger.EffectiveUnread(Chat, "m9", 2));
    }

    [Test]
    public void ClearDropsEveryEntry()
    {
        var ledger = new ReadAckLedger();
        ledger.Record(Chat, "m9");
        ledger.Clear();

        Assert.AreEqual(3, ledger.EffectiveUnread(Chat, "m9", 3));
    }

    // --- NewestIncomingId ----------------------------------------------------------------

    [Test]
    public void NoBatch_AcksNothing()
    {
        Assert.IsNull(ReadAckLedger.NewestIncomingId(null));
        Assert.IsNull(ReadAckLedger.NewestIncomingId(new List<MessageViewModel>()));
    }

    [Test]
    public void OwnEchoesOnly_AckNothing()
    {
        var batch = new List<MessageViewModel> { Msg("m1", isIncoming: false), Msg("m2", isIncoming: false) };
        Assert.IsNull(ReadAckLedger.NewestIncomingId(batch));
    }

    [Test]
    public void PicksNewestIncomingByTimestamp()
    {
        var batch = new List<MessageViewModel>
        {
            Msg("old", timestamp: 100),
            Msg("new", timestamp: 300),
            Msg("mid", timestamp: 200),
        };
        Assert.AreEqual("new", ReadAckLedger.NewestIncomingId(batch));
    }

    [Test]
    public void SameSecondArrivals_BreakTieOnSequence()
    {
        var batch = new List<MessageViewModel>
        {
            Msg("first", timestamp: 100, sequence: 0),
            Msg("second", timestamp: 100, sequence: 1),
        };
        Assert.AreEqual("second", ReadAckLedger.NewestIncomingId(batch));
    }

    [Test]
    public void NewerOwnEchoDoesNotWinOverIncoming()
    {
        // A reply sent from the app right after the incoming one must not become the acked
        // id — marking our own message read is meaningless to Wappi.
        var batch = new List<MessageViewModel>
        {
            Msg("theirs", timestamp: 100),
            Msg("mine", isIncoming: false, timestamp: 200),
        };
        Assert.AreEqual("theirs", ReadAckLedger.NewestIncomingId(batch));
    }

    [Test]
    public void SkipsNullEntriesAndIdlessMessages()
    {
        var batch = new List<MessageViewModel>
        {
            null,
            Msg("", timestamp: 900),
            Msg("real", timestamp: 100),
        };
        Assert.AreEqual("real", ReadAckLedger.NewestIncomingId(batch));
    }

    [Test]
    public void OrderOfTheBatchDoesNotMatter()
    {
        var newestFirst = new List<MessageViewModel> { Msg("b", timestamp: 200), Msg("a", timestamp: 100) };
        var oldestFirst = new List<MessageViewModel> { Msg("a", timestamp: 100), Msg("b", timestamp: 200) };

        Assert.AreEqual("b", ReadAckLedger.NewestIncomingId(newestFirst));
        Assert.AreEqual("b", ReadAckLedger.NewestIncomingId(oldestFirst));
    }
}

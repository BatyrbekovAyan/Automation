using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// The window handed to the «Вместе» suggestions payload. These assertions exist because the
/// accessor and its cache disagreed about direction for two months: _activeChatCache is
/// NEWEST-FIRST, while TryGetRecentMessages took the LAST n of it and called the result
/// "oldest->newest" — so the payload carried the chat's OLDEST messages, reversed, and the
/// server's backward trailing-client-run walk anchored on the wrong end and abstained.
///
/// The load-bearing assertion in every case below is which element lands LAST: that is the one
/// the server reads as "the newest message in this conversation".
/// </summary>
public class RecentMessageWindowTests
{
    // Newest-first, exactly as _activeChatCache holds it. Distinct timestamps so the ordering
    // assertions cannot pass by accident on a tie.
    private static List<MessageViewModel> NewestFirst(int count)
    {
        var list = new List<MessageViewModel>();
        for (int i = count; i >= 1; i--)
            list.Add(new MessageViewModel { messageId = "m" + i, timestamp = i, isIncoming = i % 2 == 1 });
        return list;
    }

    [Test]
    public void TakeNewest_ReturnsTheNewestN_OldestToNewest()
    {
        var window = RecentMessageWindow.TakeNewest(NewestFirst(5), 3);

        CollectionAssert.AreEqual(new[] { "m3", "m4", "m5" }, Ids(window));
        Assert.AreEqual("m5", window[window.Count - 1].messageId,
            "the LAST element is what the server's run-walk reads as the newest message");
        Assert.AreEqual("m3", window[0].messageId, "the window starts at the oldest of the slice");
    }

    // The bug in one line: a "last n" slice of a newest-first list returns the OLDEST n.
    [Test]
    public void TakeNewest_DoesNotReturnTheOldestEnd()
    {
        var window = RecentMessageWindow.TakeNewest(NewestFirst(30), 24);

        CollectionAssert.Contains(Ids(window), "m30", "the newest message must be in the window");
        CollectionAssert.DoesNotContain(Ids(window), "m1", "the oldest end must be dropped, not kept");
        Assert.AreEqual(24, window.Count);
    }

    [Test]
    public void TakeNewest_NLargerThanSource_ReturnsWholeListAscending()
    {
        var window = RecentMessageWindow.TakeNewest(NewestFirst(3), 24);

        CollectionAssert.AreEqual(new[] { "m1", "m2", "m3" }, Ids(window));
        Assert.AreEqual("m3", window[window.Count - 1].messageId);
    }

    // n == 1 is CurrentTailKey / SuggestionCache.TailKey: the chat's freshest message, which is
    // what makes the F9 cache key move when a new message lands.
    [Test]
    public void TakeNewest_One_IsTheFreshestMessage()
    {
        var window = RecentMessageWindow.TakeNewest(NewestFirst(5), 1);

        Assert.AreEqual(1, window.Count);
        Assert.AreEqual("m5", window[0].messageId);
    }

    [Test]
    public void TakeNewest_EmptyOrNullOrNonPositiveN_YieldsEmptyListNeverNull()
    {
        Assert.IsNotNull(RecentMessageWindow.TakeNewest(null, 24));
        Assert.IsEmpty(RecentMessageWindow.TakeNewest(null, 24));
        Assert.IsEmpty(RecentMessageWindow.TakeNewest(new List<MessageViewModel>(), 24));
        Assert.IsEmpty(RecentMessageWindow.TakeNewest(NewestFirst(5), 0));
        Assert.IsEmpty(RecentMessageWindow.TakeNewest(NewestFirst(5), -1));
    }

    // _activeChatCache must stay newest-first: ChatManager's first-screen slice GetRange(0, n),
    // the pagination queue and the 100-message cap all read the newest end at index 0.
    [Test]
    public void TakeNewest_NeverMutatesTheSource()
    {
        var source = NewestFirst(5);
        var before = Ids(source);

        RecentMessageWindow.TakeNewest(source, 3);

        CollectionAssert.AreEqual(before, Ids(source), "the cache must remain newest-first");
    }

    [Test]
    public void TakeNewest_ResultIsAscendingUnderMessageOrder()
    {
        var window = RecentMessageWindow.TakeNewest(NewestFirst(10), 6);

        for (int i = 1; i < window.Count; i++)
            Assert.Less(MessageOrder.Compare(window[i - 1], window[i]), 0,
                $"element {i - 1} must sort before element {i}");
    }

    // Same-second ties are ordered by `sequence` (WithinSecondSequence counts OLDER ties), so a
    // burst inside one second must still come out oldest-first.
    [Test]
    public void TakeNewest_SameSecondBurst_KeepsWithinSecondOrder()
    {
        // Newest-first: sequence 2 is the newest of the three (most older ties below it).
        var cache = new List<MessageViewModel>
        {
            new MessageViewModel { messageId = "c", timestamp = 100, sequence = 2 },
            new MessageViewModel { messageId = "b", timestamp = 100, sequence = 1 },
            new MessageViewModel { messageId = "a", timestamp = 100, sequence = 0 },
        };

        var window = RecentMessageWindow.TakeNewest(cache, 3);

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Ids(window));
    }

    private static List<string> Ids(IEnumerable<MessageViewModel> messages)
    {
        var ids = new List<string>();
        foreach (var m in messages) ids.Add(m.messageId);
        return ids;
    }
}

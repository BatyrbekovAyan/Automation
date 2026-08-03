using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Covers the pure chat-list ordering seam (ChatListOrder.Apply) that ChatListView's
/// resort pass uses. Order must derive from data (last-message time, newest first),
/// not from event firing order — the old per-row insert-at-top merge REVERSED every
/// chat that changed within one sync pass (ParseChatsJson iterates newest-first, so
/// the newest row was raised first and each older row landed above it). Ties and
/// unknown (zero) times keep their current visual order so a resort never churns
/// rows the data can't rank.
/// </summary>
public class ChatListOrderTests
{
    private static List<(string id, long time)> Rows(params (string id, long time)[] rows) =>
        new List<(string id, long time)>(rows);

    private static string[] Ids(List<(string id, long time)> ordered)
    {
        var ids = new string[ordered.Count];
        for (int i = 0; i < ordered.Count; i++) ids[i] = ordered[i].id;
        return ids;
    }

    [Test]
    public void Apply_RowsLeftReversedByInsertAtTopMerge_RestoresNewestFirst()
    {
        // The regression: chats A(100), B(200), C(300) all changed in one sync pass.
        // The old merge raised C first, then B above it, then A above B — leaving the
        // list oldest-first. The resort must restore newest-first from the timestamps.
        var reversedByOldMerge = Rows(("A", 100), ("B", 200), ("C", 300));

        var ordered = ChatListOrder.Apply(reversedByOldMerge, r => r.time);

        Assert.AreEqual(new[] { "C", "B", "A" }, Ids(ordered));
    }

    [Test]
    public void Apply_MixedTimes_OrdersNewestFirst()
    {
        var rows = Rows(("mid", 500), ("newest", 900), ("oldest", 100), ("older", 300));

        var ordered = ChatListOrder.Apply(rows, r => r.time);

        Assert.AreEqual(new[] { "newest", "mid", "older", "oldest" }, Ids(ordered));
    }

    [Test]
    public void Apply_EqualTimes_KeepCurrentRelativeOrder()
    {
        // Stability: same-second messages (or a stale cache pass) must not swap rows.
        var rows = Rows(("first", 200), ("second", 200), ("third", 200));

        var ordered = ChatListOrder.Apply(rows, r => r.time);

        Assert.AreEqual(new[] { "first", "second", "third" }, Ids(ordered));
    }

    [Test]
    public void Apply_ZeroTimes_SinkToBottom_KeepingTheirRelativeOrder()
    {
        // ChatDialogTime.Resolve yields 0 when neither RFC3339 field parses — those
        // rows can't be ranked, so they sink below every dated row, in current order.
        var rows = Rows(("unknownA", 0), ("dated", 400), ("unknownB", 0));

        var ordered = ChatListOrder.Apply(rows, r => r.time);

        Assert.AreEqual(new[] { "dated", "unknownA", "unknownB" }, Ids(ordered));
    }

    [Test]
    public void Apply_AlreadySortedNewestFirst_IsUnchanged()
    {
        var rows = Rows(("C", 300), ("B", 200), ("A", 100));

        var ordered = ChatListOrder.Apply(rows, r => r.time);

        Assert.AreEqual(new[] { "C", "B", "A" }, Ids(ordered));
    }

    [Test]
    public void Apply_EmptyList_ReturnsEmpty()
    {
        var ordered = ChatListOrder.Apply(Rows(), r => r.time);

        Assert.AreEqual(0, ordered.Count);
    }

    [Test]
    public void Apply_SingleRow_IsUnchanged()
    {
        var ordered = ChatListOrder.Apply(Rows(("only", 42)), r => r.time);

        Assert.AreEqual(new[] { "only" }, Ids(ordered));
    }

    // --- The resort TRIGGER contract. ChatItemView requests a list resort from
    // OnLastMessageChanged, so the whole fix hinges on ChatViewModel firing that
    // event exactly when the last message genuinely changes. (The reaction-path
    // counterpart — SetReactionPreview must NOT fire it — is already pinned in
    // ChatViewModelReactionTests.)

    [Test]
    public void UpdateLastMessage_Changed_FiresResortTrigger()
    {
        var vm = new ChatViewModel("c1", "Title", "", "old", 100);
        bool fired = false;
        vm.OnLastMessageChanged += _ => fired = true;

        vm.UpdateLastMessage("new message", 200);

        Assert.IsTrue(fired, "a genuinely new last message must request a resort");
    }

    [Test]
    public void UpdateLastMessage_SameTextNewTime_StillFiresResortTrigger()
    {
        // Same body, later timestamp — a repeated "ok" a minute later must still move.
        var vm = new ChatViewModel("c1", "Title", "", "ok", 100);
        bool fired = false;
        vm.OnLastMessageChanged += _ => fired = true;

        vm.UpdateLastMessage("ok", 160);

        Assert.IsTrue(fired, "an unchanged body with a newer time is still a new message");
    }

    [Test]
    public void UpdateLastMessage_Unchanged_DoesNotFireResortTrigger()
    {
        // A sync pass re-delivering identical data must not churn the list.
        var vm = new ChatViewModel("c1", "Title", "", "same", 100);
        bool fired = false;
        vm.OnLastMessageChanged += _ => fired = true;

        vm.UpdateLastMessage("same", 100);

        Assert.IsFalse(fired, "identical text + time must not request a resort");
    }
}

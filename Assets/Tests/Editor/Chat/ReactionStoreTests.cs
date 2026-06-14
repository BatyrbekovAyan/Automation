using System.Collections.Generic;
using NUnit.Framework;

public class ReactionStoreTests
{
    private static MessageViewModel Msg(string id) => new MessageViewModel { messageId = id };

    private static ReactionEvent Ev(string target, string emoji, string reactor, long time = 1) =>
        new ReactionEvent { targetId = target, emoji = emoji, reactorKey = reactor, time = time };

    [Test]
    public void Apply_AddsReactionToFoundTarget()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel> { Msg("A") };

        var hit = store.Apply(Ev("A", "❤️", "111"), msgs);

        Assert.AreSame(msgs[0], hit);
        Assert.AreEqual(1, msgs[0].reactions.Count);
        Assert.AreEqual("❤️", msgs[0].reactions[0].emoji);
    }

    [Test]
    public void Apply_ReplacesSameReactorsEmoji()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel> { Msg("A") };

        store.Apply(Ev("A", "❤️", "111", 1), msgs);
        var hit = store.Apply(Ev("A", "😂", "111", 2), msgs);

        Assert.AreSame(msgs[0], hit);
        Assert.AreEqual(1, msgs[0].reactions.Count);     // replaced, not appended
        Assert.AreEqual("😂", msgs[0].reactions[0].emoji);
    }

    [Test]
    public void Apply_RemovalDeletesReactorsEntry()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel> { Msg("A") };

        store.Apply(Ev("A", "❤️", "111"), msgs);
        var hit = store.Apply(Ev("A", "", "111"), msgs);   // empty emoji = un-react

        Assert.AreSame(msgs[0], hit);
        Assert.AreEqual(0, msgs[0].reactions.Count);
    }

    [Test]
    public void Apply_DifferentReactorsAggregate()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel> { Msg("A") };

        store.Apply(Ev("A", "❤️", "me"), msgs);
        store.Apply(Ev("A", "👍", "111"), msgs);

        Assert.AreEqual(2, msgs[0].reactions.Count);
    }

    [Test]
    public void Apply_SameEmojiTwice_IsIdempotentNoOp()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel> { Msg("A") };

        store.Apply(Ev("A", "❤️", "111"), msgs);
        var second = store.Apply(Ev("A", "❤️", "111"), msgs);   // re-delivered on next sync

        Assert.IsNull(second);                                  // no change => null
        Assert.AreEqual(1, msgs[0].reactions.Count);
    }

    [Test]
    public void Apply_TargetNotLoaded_BuffersAndDrainsOnArrival()
    {
        var store = new ReactionStore();
        var msgs = new List<MessageViewModel>();                // target not present yet

        var hit = store.Apply(Ev("A", "❤️", "111"), msgs);
        Assert.IsNull(hit);                                     // buffered

        var late = Msg("A");
        Assert.IsTrue(store.DrainInto(late));                   // applies buffered reaction
        Assert.AreEqual(1, late.reactions.Count);
        Assert.AreEqual("❤️", late.reactions[0].emoji);

        Assert.IsFalse(store.DrainInto(Msg("A")));              // buffer consumed
    }

    [Test]
    public void Buffer_CollapsesByReactor_LatestEmojiWins()
    {
        var store = new ReactionStore();
        store.Apply(Ev("A", "❤️", "111", 1), new List<MessageViewModel>());
        store.Apply(Ev("A", "😂", "111", 2), new List<MessageViewModel>());

        var late = Msg("A");
        store.DrainInto(late);
        Assert.AreEqual(1, late.reactions.Count);
        Assert.AreEqual("😂", late.reactions[0].emoji);
    }

    [Test]
    public void Clear_DropsPending()
    {
        var store = new ReactionStore();
        store.Apply(Ev("A", "❤️", "111"), new List<MessageViewModel>());
        store.Clear();
        Assert.IsFalse(store.DrainInto(Msg("A")));
    }
}

using System.Collections.Generic;
using NUnit.Framework;

public class OutgoingReactionTests
{
    private static MessageViewModel MsgWith(params MessageReaction[] reactions)
        => new MessageViewModel { messageId = "MSG1", chatId = "C@c.us", reactions = new List<MessageReaction>(reactions) };

    private static MessageReaction Me(string emoji)
        => new MessageReaction { emoji = emoji, reactorKey = "me", fromMe = true, time = 1 };

    private static MessageReaction Other(string emoji)
        => new MessageReaction { emoji = emoji, reactorKey = "79991234567@c.us", fromMe = false, time = 1 };

    [Test]
    public void CurrentMyEmoji_NoReactions_ReturnsNull()
        => Assert.IsNull(OutgoingReaction.CurrentMyEmoji(new MessageViewModel { messageId = "M" }));

    [Test]
    public void CurrentMyEmoji_OnlyOthers_ReturnsNull()
        => Assert.IsNull(OutgoingReaction.CurrentMyEmoji(MsgWith(Other("👍"))));

    [Test]
    public void CurrentMyEmoji_HasMine_ReturnsMyEmoji()
        => Assert.AreEqual("❤️", OutgoingReaction.CurrentMyEmoji(MsgWith(Other("👍"), Me("❤️"))));

    [Test]
    public void Resolve_NoExisting_ReturnsAddEvent()
    {
        var ev = OutgoingReaction.Resolve(MsgWith(), "👍", 100);
        Assert.AreEqual("MSG1", ev.targetId);
        Assert.AreEqual("👍", ev.emoji);
        Assert.AreEqual("me", ev.reactorKey);
        Assert.IsTrue(ev.fromMe);
        Assert.IsFalse(ev.IsRemoval);
        Assert.AreEqual(100, ev.time);
    }

    [Test]
    public void Resolve_SameEmoji_ReturnsRemoval()
    {
        var ev = OutgoingReaction.Resolve(MsgWith(Me("👍")), "👍", 100);
        Assert.AreEqual("", ev.emoji);
        Assert.IsTrue(ev.IsRemoval);
    }

    [Test]
    public void Resolve_DifferentEmoji_ReturnsReplace()
    {
        var ev = OutgoingReaction.Resolve(MsgWith(Me("👍")), "❤️", 100);
        Assert.AreEqual("❤️", ev.emoji);
        Assert.IsFalse(ev.IsRemoval);
    }

    [Test]
    public void Resolve_ThenApply_AddsMyReaction()
    {
        var msg = MsgWith(Other("👍"));
        Assert.IsTrue(ReactionStore.ApplyToMessage(msg, OutgoingReaction.Resolve(msg, "❤️", 100)));
        Assert.AreEqual(2, msg.reactions.Count);
        Assert.AreEqual("❤️", OutgoingReaction.CurrentMyEmoji(msg));
    }

    [Test]
    public void Resolve_ThenApply_ToggleOff_RemovesMine()
    {
        var msg = MsgWith(Me("👍"));
        Assert.IsTrue(ReactionStore.ApplyToMessage(msg, OutgoingReaction.Resolve(msg, "👍", 100)));
        Assert.IsNull(OutgoingReaction.CurrentMyEmoji(msg));
        Assert.AreEqual(0, msg.reactions.Count);
    }
}

using System.Collections.Generic;
using NUnit.Framework;

public class ReactionSummaryTests
{
    private static MessageReaction R(string emoji, string reactor) =>
        new MessageReaction { emoji = emoji, reactorKey = reactor };

    [Test]
    public void Build_NullOrEmpty_IsZero()
    {
        var (e1, c1) = ReactionSummary.Build(null);
        Assert.AreEqual(0, e1.Count);
        Assert.AreEqual(0, c1);

        var (e2, c2) = ReactionSummary.Build(new List<MessageReaction>());
        Assert.AreEqual(0, e2.Count);
        Assert.AreEqual(0, c2);
    }

    [Test]
    public void Build_SingleReactor()
    {
        var (emojis, count) = ReactionSummary.Build(new List<MessageReaction> { R("❤️", "me") });
        CollectionAssert.AreEqual(new[] { "❤️" }, emojis);
        Assert.AreEqual(1, count);
    }

    [Test]
    public void Build_TwoReactorsSameEmoji_OneDistinctCountTwo()
    {
        var (emojis, count) = ReactionSummary.Build(new List<MessageReaction>
        {
            R("❤️", "me"), R("❤️", "111")
        });
        CollectionAssert.AreEqual(new[] { "❤️" }, emojis);
        Assert.AreEqual(2, count);
    }

    [Test]
    public void Build_PreservesFirstSeenOrder()
    {
        var (emojis, _) = ReactionSummary.Build(new List<MessageReaction>
        {
            R("😂", "a"), R("❤️", "b"), R("👍", "c")
        });
        CollectionAssert.AreEqual(new[] { "😂", "❤️", "👍" }, emojis);
    }

    [Test]
    public void Build_CapsDistinctEmojisAtThree_CountReflectsAll()
    {
        var (emojis, count) = ReactionSummary.Build(new List<MessageReaction>
        {
            R("😂", "a"), R("❤️", "b"), R("👍", "c"), R("🔥", "d")
        });
        Assert.AreEqual(3, emojis.Count);                 // capped
        CollectionAssert.AreEqual(new[] { "😂", "❤️", "👍" }, emojis);
        Assert.AreEqual(4, count);                        // count still counts everyone visible
    }

    [Test]
    public void Build_EmptyEmojiTombstone_ExcludedFromEmojisAndCount()
    {
        // D2: an empty-emoji "me" removal tombstone is not a real reaction — it must neither
        // add a glyph nor inflate the reactor count.
        var (emojis, count) = ReactionSummary.Build(new List<MessageReaction>
        {
            R("❤️", "999"), R("", "me")
        });
        CollectionAssert.AreEqual(new[] { "❤️" }, emojis);
        Assert.AreEqual(1, count);
    }

    [Test]
    public void Build_LoneTombstone_IsEmpty()
    {
        var (emojis, count) = ReactionSummary.Build(new List<MessageReaction> { R("", "me") });
        Assert.AreEqual(0, emojis.Count);
        Assert.AreEqual(0, count);
    }

    [Test]
    public void Build_SameEmojiDifferentForms_DedupedAcrossForms()
    {
        // D2 root cause A: tapi's base ❤ and the app's qualified ❤️ are ONE reaction — one heart
        // glyph, not two (symptom 3) — while the reactor count still reflects both people.
        var (emojis, count) = ReactionSummary.Build(new List<MessageReaction>
        {
            R("❤️", "999"), R("❤", "me")
        });
        Assert.AreEqual(1, emojis.Count);   // deduped across forms
        Assert.AreEqual(2, count);          // both reactors still counted
    }
}

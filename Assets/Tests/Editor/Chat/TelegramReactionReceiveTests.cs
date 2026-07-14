using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

/// <summary>
/// Covers the Telegram (tapi) receive-side reaction seams (SHAPES.md Q3 = GO): reactions ride
/// ON the target message as a <c>reactions[]</c> array, so they are mapped at Normalize time
/// (<see cref="TelegramReactionMapper"/>) and reconciled onto already-cached messages via
/// <see cref="TelegramReactionMerge"/>. All JSON is SYNTHETIC/PII-free, mirroring the recorded
/// element shape <c>{reaction,count,user_id,contact_name,type:"emoji"}</c>. The WhatsApp
/// ReactionStore transport is unaffected (separate suite).
/// </summary>
public class TelegramReactionReceiveTests
{
    private static JToken Reactions(string json) => JToken.Parse(json);

    private static MessageReaction Me(string emoji) => new MessageReaction
    { emoji = emoji, reactorKey = OutgoingReaction.MeReactorKey, fromMe = true };

    private static MessageReaction Other(string emoji, string key) => new MessageReaction
    { emoji = emoji, reactorKey = key, fromMe = false };

    // --- Mapper: array element -> MessageReaction ---

    [Test]
    public void Map_SingleReaction_MapsFields()
    {
        var list = TelegramReactionMapper.Map(Reactions(
            "[{\"reaction\":\"👍\",\"count\":0,\"user_id\":\"12345\",\"contact_name\":\"Ivan\",\"type\":\"emoji\"}]"));

        Assert.IsNotNull(list);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("👍", list[0].emoji);
        Assert.AreEqual("12345", list[0].reactorKey);   // user_id is the stable reactor key
        Assert.AreEqual("Ivan", list[0].senderName);
        Assert.IsFalse(list[0].fromMe);                 // tapi has no per-reaction fromMe
    }

    [Test]
    public void Map_MultipleReactors_AllMapped()
    {
        var list = TelegramReactionMapper.Map(Reactions(
            "[{\"reaction\":\"👍\",\"user_id\":\"1\"},{\"reaction\":\"❤\",\"user_id\":\"2\"}]"));

        Assert.AreEqual(2, list.Count);
    }

    [Test]
    public void Map_MissingUserId_FallsBackToEmojiScopedKey()
    {
        var list = TelegramReactionMapper.Map(Reactions("[{\"reaction\":\"👍\"}]"));
        Assert.AreEqual("👍@tg", list[0].reactorKey);
    }

    [Test]
    public void Map_EmptyEmojiEntry_Skipped()
    {
        var list = TelegramReactionMapper.Map(Reactions(
            "[{\"reaction\":\"\",\"user_id\":\"1\"},{\"reaction\":\"👍\",\"user_id\":\"2\"}]"));
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("👍", list[0].emoji);
    }

    [Test]
    public void Map_NullOrEmpty_ReturnsNull()
    {
        Assert.IsNull(TelegramReactionMapper.Map(null));
        Assert.IsNull(TelegramReactionMapper.Map(Reactions("[]")));
        Assert.IsNull(TelegramReactionMapper.Map(Reactions("\"notanarray\"")));
    }

    // --- Merge: server authoritative, owner "me" preserved until echoed ---

    [Test]
    public void Merge_ServerEmpty_PreservesOptimisticMe()
    {
        var merged = TelegramReactionMerge.Merge(new List<MessageReaction> { Me("👍") }, null);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, merged[0].reactorKey);
    }

    [Test]
    public void Merge_ServerEchoesMyEmoji_NoDuplicate()
    {
        // Owner reacted 👍 (optimistic "me"); the server now echoes the same emoji under a user id.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("👍") },
            new List<MessageReaction> { Other("👍", "999") });

        Assert.AreEqual(1, merged.Count);                 // no double-count
        Assert.AreEqual("999", merged[0].reactorKey);     // server entry wins
    }

    [Test]
    public void Merge_MyEmojiNotYetEchoed_KeptAlongsideOthers()
    {
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("❤") },
            new List<MessageReaction> { Other("👍", "999") });

        Assert.AreEqual(2, merged.Count);                 // other's 👍 + my not-yet-echoed ❤️
    }

    [Test]
    public void Merge_OtherReactionRemovedServerSide_Propagates()
    {
        // No "me" locally; server dropped the reaction -> merged clears to null.
        var merged = TelegramReactionMerge.Merge(new List<MessageReaction> { Other("👍", "1") }, null);
        Assert.IsNull(merged);
    }

    [Test]
    public void Merge_NoMeLocally_EqualsServer()
    {
        var server = new List<MessageReaction> { Other("👍", "1"), Other("❤", "2") };
        var merged = TelegramReactionMerge.Merge(null, server);
        Assert.AreEqual(2, merged.Count);
    }

    // --- SameReactions: order-insensitive multiset equality (no spurious re-render) ---

    [Test]
    public void SameReactions_SameSetDifferentOrder_True()
    {
        var a = new List<MessageReaction> { Other("👍", "1"), Other("❤", "2") };
        var b = new List<MessageReaction> { Other("❤", "2"), Other("👍", "1") };
        Assert.IsTrue(TelegramReactionMerge.SameReactions(a, b));
    }

    [Test]
    public void SameReactions_DifferentEmoji_False()
    {
        var a = new List<MessageReaction> { Other("👍", "1") };
        var b = new List<MessageReaction> { Other("❤", "1") };
        Assert.IsFalse(TelegramReactionMerge.SameReactions(a, b));
    }

    [Test]
    public void SameReactions_DifferentCount_False()
    {
        var a = new List<MessageReaction> { Other("👍", "1") };
        var b = new List<MessageReaction> { Other("👍", "1"), Other("❤", "2") };
        Assert.IsFalse(TelegramReactionMerge.SameReactions(a, b));
    }

    [Test]
    public void SameReactions_BothEmpty_True()
    {
        Assert.IsTrue(TelegramReactionMerge.SameReactions(null, null));
        Assert.IsTrue(TelegramReactionMerge.SameReactions(new List<MessageReaction>(), null));
    }
}

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
    // Fixed "current" unix time for merge calls — keeps freshness deterministic.
    private const long Now = 1_752_000_000;

    private static JToken Reactions(string json) => JToken.Parse(json);

    // Optimistic send entry by default (real tap time = fresh); pass time: 0 to model a
    // server-mapped "me" echo (TelegramReactionMapper emits time = 0).
    private static MessageReaction Me(string emoji, long time = Now) => new MessageReaction
    { emoji = emoji, reactorKey = OutgoingReaction.MeReactorKey, fromMe = true, time = time };

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

    [Test]
    public void Map_OwnUserId_MapsOwnElementToMe_OthersKeepTheirIds()
    {
        // The owner's echoed element (user_id == learned own id, SHAPES.md Q4) adopts the
        // "me" identity; every other reactor stays keyed by their user_id.
        var list = TelegramReactionMapper.Map(Reactions(
            "[{\"reaction\":\"👍\",\"user_id\":\"555\",\"contact_name\":\"\"}," +
            "{\"reaction\":\"❤\",\"user_id\":\"12345\",\"contact_name\":\"Ivan\"}]"),
            ownUserId: "555");

        Assert.AreEqual(2, list.Count);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, list[0].reactorKey);
        Assert.IsTrue(list[0].fromMe);
        Assert.AreEqual("12345", list[1].reactorKey);
        Assert.IsFalse(list[1].fromMe);
    }

    // --- Merge: server authoritative, owner "me" identity-preserved (05-06-REVIEW WR-01) ---

    [Test]
    public void Merge_ServerEmpty_PreservesOptimisticMe()
    {
        var merged = TelegramReactionMerge.Merge(new List<MessageReaction> { Me("👍") }, null, Now);
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, merged[0].reactorKey);
    }

    [Test]
    public void Merge_ServerEchoesMyEmoji_AdoptsMeIdentity_ToggleOffSurvives()
    {
        // Owner reacted 👍 (optimistic "me"); the server echo is itself mapped to "me"
        // (TelegramReactionMapper matched user_id to the learned own id, time=0).
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("👍") },
            new List<MessageReaction> { Me("👍", time: 0) },
            Now);

        Assert.AreEqual(1, merged.Count);                                     // no double-count
        Assert.AreEqual(OutgoingReaction.MeReactorKey, merged[0].reactorKey); // identity survives the echo

        // Regression (WR-01): post-echo, the owner can still toggle the reaction off —
        // CurrentMyEmoji finds the kept "me" entry and a same-emoji tap resolves to a removal.
        var vm = new MessageViewModel { messageId = "TG-M1", reactions = merged };
        Assert.AreEqual("👍", OutgoingReaction.CurrentMyEmoji(vm));
        Assert.IsTrue(OutgoingReaction.Resolve(vm, "👍", Now).IsRemoval);
    }

    [Test]
    public void Merge_OtherUserSameEmoji_DoesNotConsumeMyEntry()
    {
        // A DIFFERENT user reacted with the same emoji before my echo arrived. The old
        // emoji-presence heuristic dropped my optimistic entry here (flicker-off + dead
        // toggle); identity-keyed preservation keeps both reactors.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("👍") },
            new List<MessageReaction> { Other("👍", "999") },
            Now);

        Assert.AreEqual(2, merged.Count);
        Assert.IsTrue(merged.Exists(r => r.reactorKey == OutgoingReaction.MeReactorKey));
        Assert.IsTrue(merged.Exists(r => r.reactorKey == "999"));
    }

    [Test]
    public void Merge_MyEmojiNotYetEchoed_KeptAlongsideOthers()
    {
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("❤") },
            new List<MessageReaction> { Other("👍", "999") },
            Now);

        Assert.AreEqual(2, merged.Count);                 // other's 👍 + my not-yet-echoed ❤️
    }

    [Test]
    public void Merge_StaleUnechoedMe_DroppedAfterGraceWindow()
    {
        // An optimistic "me" the server never echoed stops being preserved once the grace
        // window lapses — e.g. the owner removed the reaction from the phone's Telegram app.
        long staleTap = Now - TelegramReactionMerge.OptimisticGraceSeconds - 1;
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("👍", staleTap) }, null, Now);
        Assert.IsNull(merged);
    }

    [Test]
    public void Merge_EchoedMeRemovedServerSide_PropagatesImmediately()
    {
        // A previously-echoed "me" (adopted from the server, time=0) disappears the moment
        // the server stops reporting it — phone-side removals apply on the next refresh.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("👍", time: 0) }, null, Now);
        Assert.IsNull(merged);
    }

    [Test]
    public void Merge_FreshEmojiChange_BeatsStaleServerEcho()
    {
        // Owner changed 👍→❤ post-echo; a stale in-flight snapshot still echoes 👍 as "me".
        // The fresh optimistic ❤ wins (no revert flicker); the server catches up next poll.
        var merged = TelegramReactionMerge.Merge(
            new List<MessageReaction> { Me("❤") },
            new List<MessageReaction> { Me("👍", time: 0) },
            Now);

        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("❤", merged[0].emoji);
        Assert.AreEqual(OutgoingReaction.MeReactorKey, merged[0].reactorKey);
    }

    [Test]
    public void Merge_OtherReactionRemovedServerSide_Propagates()
    {
        // No "me" locally; server dropped the reaction -> merged clears to null.
        var merged = TelegramReactionMerge.Merge(new List<MessageReaction> { Other("👍", "1") }, null, Now);
        Assert.IsNull(merged);
    }

    [Test]
    public void Merge_NoMeLocally_EqualsServer()
    {
        var server = new List<MessageReaction> { Other("👍", "1"), Other("❤", "2") };
        var merged = TelegramReactionMerge.Merge(null, server, Now);
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

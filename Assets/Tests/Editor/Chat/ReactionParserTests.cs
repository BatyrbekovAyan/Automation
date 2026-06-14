using NUnit.Framework;
using Newtonsoft.Json.Linq;

public class ReactionParserTests
{
    private static RawMessage Raw(string type, string stanzaId, object body,
                                  bool fromMe = false, string from = "", string sender = "") =>
        new RawMessage
        {
            type = type,
            stanzaId = stanzaId,
            body = body == null ? null : JToken.FromObject(body),
            fromMe = fromMe,
            from = from,
            senderName = sender,
            time = 42
        };

    [Test]
    public void FromRaw_ParsesIncomingReaction()
    {
        var ev = ReactionParser.FromRaw(Raw("reaction", "T1", "😘", fromMe: false, from: "111@c.us", sender: "Zhanym"));

        Assert.IsNotNull(ev);
        Assert.AreEqual("T1", ev.targetId);
        Assert.AreEqual("😘", ev.emoji);
        Assert.AreEqual("111@c.us", ev.reactorKey);   // not-fromMe keys on jid
        Assert.IsFalse(ev.IsRemoval);
        Assert.AreEqual(42, ev.time);
    }

    [Test]
    public void FromRaw_FromMe_KeysOnMe()
    {
        var ev = ReactionParser.FromRaw(Raw("reaction", "T1", "👍", fromMe: true, from: "999@c.us", sender: "Ayan"));
        Assert.AreEqual("me", ev.reactorKey);
    }

    [Test]
    public void FromRaw_EmptyBody_IsRemoval()
    {
        var ev = ReactionParser.FromRaw(Raw("reaction", "T1", "", from: "111@c.us"));
        Assert.IsNotNull(ev);
        Assert.IsTrue(ev.IsRemoval);
        Assert.AreEqual("", ev.emoji);
    }

    [Test]
    public void FromRaw_NullBody_IsRemoval()
    {
        var ev = ReactionParser.FromRaw(Raw("reaction", "T1", null, from: "111@c.us"));
        Assert.IsNotNull(ev);
        Assert.IsTrue(ev.IsRemoval);
    }

    [Test]
    public void FromRaw_NonReactionType_ReturnsNull()
    {
        Assert.IsNull(ReactionParser.FromRaw(Raw("chat", "T1", "hi")));
    }

    [Test]
    public void FromRaw_MissingStanzaId_ReturnsNull()
    {
        Assert.IsNull(ReactionParser.FromRaw(Raw("reaction", "", "😘")));
    }

    [Test]
    public void ReactorKey_FallsBackJidThenSenderThenUnknown()
    {
        Assert.AreEqual("me", ReactionParser.ReactorKey(true, "x", "y"));
        Assert.AreEqual("jid", ReactionParser.ReactorKey(false, "jid", "name"));
        Assert.AreEqual("name", ReactionParser.ReactorKey(false, "", "name"));
        Assert.AreEqual("unknown", ReactionParser.ReactorKey(false, "", ""));
    }
}

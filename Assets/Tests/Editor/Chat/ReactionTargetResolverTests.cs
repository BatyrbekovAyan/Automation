using System.Collections.Generic;
using NUnit.Framework;
using Newtonsoft.Json.Linq;

public class ReactionTargetResolverTests
{
    private static RawMessage Msg(string id, string type, object body = null,
                                  string stanzaId = null, string caption = null, string sender = null) =>
        new RawMessage
        {
            id = id,
            type = type,
            body = body == null ? null : JToken.FromObject(body),
            stanzaId = stanzaId,
            caption = caption,
            senderName = sender
        };

    [Test]
    public void ResolvesTextTarget()
    {
        var msgs = new List<RawMessage>
        {
            Msg("R", "reaction", "❤️", stanzaId: "T"),
            Msg("T", "chat", "See you tomorrow"),
        };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("See you tomorrow", r.text);
        Assert.AreEqual("chat", r.type);
    }

    [Test]
    public void PrefersCaptionOverBody()
    {
        var msgs = new List<RawMessage>
        {
            Msg("R", "reaction", "❤️", stanzaId: "T"),
            Msg("T", "image", new { url = "x" }, caption: "A caption"),
        };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("A caption", r.text);
        Assert.AreEqual("image", r.type);
    }

    [Test]
    public void MediaTargetWithoutCaption_EmptyTextKeepsType()
    {
        var msgs = new List<RawMessage>
        {
            Msg("R", "reaction", "❤️", stanzaId: "T"),
            Msg("T", "image", new { url = "x" }),
        };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("", r.text);
        Assert.AreEqual("image", r.type);
    }

    [Test]
    public void TargetNotInWindow_ReturnsEmpty()
    {
        var msgs = new List<RawMessage> { Msg("R", "reaction", "❤️", stanzaId: "GONE") };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("", r.text);
        Assert.AreEqual("", r.type);
    }

    [Test]
    public void ReactionNotFound_ReturnsEmpty()
    {
        var msgs = new List<RawMessage> { Msg("T", "chat", "hi") };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("", r.text);
        Assert.AreEqual("", r.type);
    }

    [Test]
    public void NullArgs_ReturnEmpty()
    {
        var r = ReactionTargetResolver.Resolve(null, "R");
        Assert.AreEqual("", r.text);
        Assert.AreEqual("", r.type);
    }

    [Test]
    public void ResolvesSenderNameOfNormalMessage_NoTarget()
    {
        var msgs = new List<RawMessage> { Msg("M1", "chat", "Hi", sender: "Bumer") };
        var r = ReactionTargetResolver.Resolve(msgs, "M1");
        Assert.AreEqual("Bumer", r.senderName);
        Assert.AreEqual("", r.text);
        Assert.AreEqual("", r.type);
    }

    [Test]
    public void ResolvesReactorNameAndTargetText()
    {
        var msgs = new List<RawMessage>
        {
            Msg("R", "reaction", "❤️", stanzaId: "T", sender: "Alibek"),
            Msg("T", "chat", "See you"),
        };
        var r = ReactionTargetResolver.Resolve(msgs, "R");
        Assert.AreEqual("Alibek", r.senderName);
        Assert.AreEqual("See you", r.text);
        Assert.AreEqual("chat", r.type);
    }

    [Test]
    public void MessageNotInWindow_EmptySenderName()
    {
        var msgs = new List<RawMessage> { Msg("OTHER", "chat", "x", sender: "Z") };
        var r = ReactionTargetResolver.Resolve(msgs, "M1");
        Assert.AreEqual("", r.senderName);
    }
}

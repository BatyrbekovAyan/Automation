using NUnit.Framework;
using Newtonsoft.Json.Linq;

public class ReplyParserTests
{
    private static MessageType StubParse(string t)
    {
        switch (t)
        {
            case "image": return MessageType.Image;
            case "video": return MessageType.Video;
            case "ptt":   return MessageType.Voice;
            case "audio": return MessageType.Audio;
            case "document": return MessageType.Document;
            case "chat":  return MessageType.Chat;
            default:       return MessageType.Unknown;
        }
    }

    private static RawMessage Reply(string quotedId, string type = "chat", string body = "orig", string caption = null)
    {
        var snap = new JObject { ["id"] = quotedId, ["type"] = type, ["body"] = body };
        if (caption != null) snap["caption"] = caption;
        return new RawMessage { type = "chat", isReply = true, replyMessage = snap };
    }

    [Test]
    public void NullRaw_ReturnsNone()
        => Assert.IsTrue(ReplyParser.Resolve(null, _ => null, StubParse).IsEmpty);

    [Test]
    public void ReactionRaw_ReturnsNone()
    {
        var raw = Reply("Q1");
        raw.type = "reaction";
        Assert.IsTrue(ReplyParser.Resolve(raw, _ => null, StubParse).IsEmpty);
    }

    [Test]
    public void NoReplyIndicator_ReturnsNone()
    {
        var raw = new RawMessage { type = "chat", isReply = false };
        Assert.IsTrue(ReplyParser.Resolve(raw, _ => null, StubParse).IsEmpty);
    }

    [Test]
    public void CacheHit_UsesCachedContent()
    {
        var cached = new MessageViewModel { messageId = "Q1", senderName = "Aisha", text = "cached body", type = MessageType.Chat, isIncoming = true };
        var preview = ReplyParser.Resolve(Reply("Q1", body: "stale snapshot"), id => id == "Q1" ? cached : null, StubParse);
        Assert.AreEqual("Q1", preview.messageId);
        Assert.AreEqual("Aisha", preview.senderName);
        Assert.AreEqual("cached body", preview.text);
    }

    [Test]
    public void CacheMiss_UsesSnapshot()
    {
        var preview = ReplyParser.Resolve(Reply("Q2", type: "image", body: "", caption: "Beach"), _ => null, StubParse);
        Assert.AreEqual("Q2", preview.messageId);
        Assert.AreEqual(MessageType.Image, preview.type);
        Assert.AreEqual("Beach", preview.text);
        Assert.AreEqual(string.Empty, preview.senderName);
    }

    [Test]
    public void CacheMiss_NoSnapshotContent_ReturnsPlaceholderWithId()
    {
        var raw = new RawMessage { type = "chat", isReply = true };
        raw.stanzaId = "Q3";
        var preview = ReplyParser.Resolve(raw, _ => null, StubParse);
        Assert.AreEqual("Q3", preview.messageId);
        Assert.AreEqual(MessageType.Unknown, preview.type);
    }

    [Test]
    public void NullResolver_DoesNotThrow_FallsToSnapshot()
        => Assert.AreEqual("snap", ReplyParser.Resolve(Reply("Q4", body: "snap"), null, StubParse).text);

    [Test]
    public void OwnMessage_SenderLabelIsYou()
    {
        var cached = new MessageViewModel { messageId = "Q5", senderName = "Me", text = "hi", type = MessageType.Chat, isIncoming = false };
        Assert.AreEqual("You", ReplyParser.Resolve(Reply("Q5"), id => cached, StubParse).senderName);
    }

    [Test]
    public void MediaNoCaption_SnippetIsTypeLabel()
    {
        Assert.AreEqual("Photo", ReplyParser.SnippetFor(MessageType.Image, null));
        Assert.AreEqual("Voice message", ReplyParser.SnippetFor(MessageType.Voice, ""));
        Assert.AreEqual("hello", ReplyParser.SnippetFor(MessageType.Image, "hello"));
    }

    [Test]
    public void CleanSnippet_TrimsLeadingZeroWidthSpace()
        => Assert.AreEqual("hi", ReplyParser.CleanSnippet("​  hi"));

    [Test]
    public void Snapshot_FromMe_SenderLabelIsYou()
    {
        var snap = new JObject { ["id"] = "Q6", ["type"] = "chat", ["body"] = "hi", ["fromMe"] = true };
        var raw = new RawMessage { type = "chat", isReply = true, replyMessage = snap };
        Assert.AreEqual("You", ReplyParser.Resolve(raw, _ => null, StubParse).senderName);
    }

    [Test]
    public void CacheHit_PrefersThumbnailUrlOverMediaUrl()
    {
        var vm = new MessageViewModel { messageId = "QT", type = MessageType.Image, thumbnailUrl = "thumb://QT", mediaUrl = "https://cdn/img.jpg", isIncoming = true };
        Assert.AreEqual("thumb://QT", ReplyParser.Resolve(Reply("QT"), id => vm, StubParse).thumbnailUrl);
    }

    [Test]
    public void CacheHit_FallsBackToMediaUrlWhenNoThumbnail()
    {
        var vm = new MessageViewModel { messageId = "QU", type = MessageType.Image, thumbnailUrl = null, mediaUrl = "https://cdn/img.jpg", isIncoming = true };
        Assert.AreEqual("https://cdn/img.jpg", ReplyParser.Resolve(Reply("QU"), id => vm, StubParse).thumbnailUrl);
    }
}

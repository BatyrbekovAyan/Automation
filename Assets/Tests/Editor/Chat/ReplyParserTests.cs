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
    public void SnippetFor_CollapsesNewlinesToSingleLine()
        => Assert.AreEqual("line1 line2 line3", ReplyParser.SnippetFor(MessageType.Chat, "line1\nline2\n\n  line3"));

    [Test]
    public void SnippetFor_CollapsesTabsAndRuns()
        => Assert.AreEqual("a b c", ReplyParser.SnippetFor(MessageType.Chat, "a\t\tb   c"));

    [Test]
    public void SnippetFor_CapsPathologicalLength()
    {
        string huge = new string('x', 500);
        string snip = ReplyParser.SnippetFor(MessageType.Chat, huge);
        Assert.LessOrEqual(snip.Length, 161, "snippet should be capped so TMP never measures a multi-thousand-px line");
        StringAssert.EndsWith("…", snip);
    }

    [Test]
    public void SnippetFor_ShortTextUnchanged()
        => Assert.AreEqual("hello world", ReplyParser.SnippetFor(MessageType.Chat, "hello world"));

    [Test]
    public void BackfillFromCache_FillsPlaceholderFromTarget()
    {
        var target = new MessageViewModel { messageId = "Q1", senderName = "Aisha", text = "the full original", type = MessageType.Chat, isIncoming = true };
        var reply  = new MessageViewModel { messageId = "M1", quotedMessageId = "Q1", quotedText = null, quotedType = MessageType.Unknown };
        bool changed = ReplyParser.BackfillFromCache(new[] { reply, target });

        Assert.IsTrue(changed);
        Assert.AreEqual("the full original", reply.quotedText);
        Assert.AreEqual("Aisha", reply.quotedSenderName);
        Assert.AreEqual(MessageType.Chat, reply.quotedType);
    }

    [Test]
    public void BackfillFromCache_TargetAfterReply_NewestFirstOrder()
    {
        // Wappi batches are newest-first: the quoted (older) target sits AFTER the reply.
        var reply  = new MessageViewModel { messageId = "M1", quotedMessageId = "Q1", quotedText = null, quotedType = MessageType.Unknown };
        var target = new MessageViewModel { messageId = "Q1", senderName = "Bo", text = "older msg", type = MessageType.Chat, isIncoming = true };
        Assert.IsTrue(ReplyParser.BackfillFromCache(new[] { reply, target }));
        Assert.AreEqual("older msg", reply.quotedText);
    }

    [Test]
    public void BackfillFromCache_LeavesResolvedSnippetUntouched()
    {
        var target = new MessageViewModel { messageId = "Q1", senderName = "Aisha", text = "full cached", type = MessageType.Chat, isIncoming = true };
        var reply  = new MessageViewModel { messageId = "M1", quotedMessageId = "Q1", quotedText = "snapshot snippet", quotedSenderName = "Aisha", quotedType = MessageType.Chat };
        Assert.IsFalse(ReplyParser.BackfillFromCache(new[] { reply, target }));
        Assert.AreEqual("snapshot snippet", reply.quotedText);
    }

    [Test]
    public void BackfillFromCache_NoTarget_LeavesPlaceholder()
    {
        var reply = new MessageViewModel { messageId = "M1", quotedMessageId = "Q1", quotedText = null, quotedType = MessageType.Unknown };
        Assert.IsFalse(ReplyParser.BackfillFromCache(new[] { reply }));
        Assert.IsTrue(string.IsNullOrEmpty(reply.quotedText));
    }

    [Test]
    public void BackfillFromCache_NonReplyMessages_NoChange()
    {
        var a = new MessageViewModel { messageId = "A", text = "hi", type = MessageType.Chat };
        var b = new MessageViewModel { messageId = "B", text = "yo", type = MessageType.Chat };
        Assert.IsFalse(ReplyParser.BackfillFromCache(new[] { a, b }));
    }

    [Test]
    public void BackfillFromCache_OwnTargetSenderIsYou()
    {
        var target = new MessageViewModel { messageId = "Q1", senderName = "Me", text = "mine", type = MessageType.Chat, isIncoming = false };
        var reply  = new MessageViewModel { messageId = "M1", quotedMessageId = "Q1", quotedType = MessageType.Unknown };
        ReplyParser.BackfillFromCache(new[] { reply, target });
        Assert.AreEqual("You", reply.quotedSenderName);
    }

    // Wappi sometimes returns a reply_message snapshot whose body equals the replying message's
    // OWN text (the quoted target is unavailable), so the quote resolves to a duplicate of the
    // bubble. The card must be suppressed in that case.
    [Test]
    public void QuoteEchoesBody_IdenticalText_True()
        => Assert.IsTrue(ReplyParser.QuoteEchoesBody("Кана ты мог бы даже не отвечать", "Кана ты мог бы даже не отвечать"));

    [Test]
    public void QuoteEchoesBody_BodyHasLeadingZeroWidthSpace_True()
        => Assert.IsTrue(ReplyParser.QuoteEchoesBody("​hello there", "hello there"));

    [Test]
    public void QuoteEchoesBody_MultilineBodyMatchesCollapsedSnippet_True()
        => Assert.IsTrue(ReplyParser.QuoteEchoesBody("line one\nline two", "line one line two"));

    [Test]
    public void QuoteEchoesBody_DifferentText_False()
        => Assert.IsFalse(ReplyParser.QuoteEchoesBody("Да", "Привет"));

    [Test]
    public void QuoteEchoesBody_EmptyQuote_False()
        => Assert.IsFalse(ReplyParser.QuoteEchoesBody("hello", ""));

    [Test]
    public void QuoteEchoesBody_EmptyBody_False()
        => Assert.IsFalse(ReplyParser.QuoteEchoesBody("", "hello"));

    [Test]
    public void QuoteEchoesBody_ShortReplyToLongOriginal_False()
    {
        string quoted = ReplyParser.SnippetFor(MessageType.Chat, new string('z', 400)); // capped snippet of a long original
        Assert.IsFalse(ReplyParser.QuoteEchoesBody("ok", quoted));
    }

    // --- Snapshot echo: drop a snapshot whose body equals the message's OWN body, but ONLY there ---

    [Test]
    public void Snapshot_EchoesOwnBody_TextDropped_KeepsReplyMarker()
    {
        var snap = new JObject { ["id"] = "Q", ["type"] = "chat", ["body"] = "Кана ты мог бы" };
        var raw  = new RawMessage { type = "chat", isReply = true, replyMessage = snap, body = "Кана ты мог бы" };
        var preview = ReplyParser.Resolve(raw, _ => null, StubParse);
        Assert.AreEqual("Q", preview.messageId);            // still a reply
        Assert.IsTrue(string.IsNullOrEmpty(preview.text));  // bogus echo text dropped
    }

    [Test]
    public void Snapshot_DifferentBody_KeepsText()
    {
        var snap = new JObject { ["id"] = "Q", ["type"] = "chat", ["body"] = "the original" };
        var raw  = new RawMessage { type = "chat", isReply = true, replyMessage = snap, body = "my reply" };
        Assert.AreEqual("the original", ReplyParser.Resolve(raw, _ => null, StubParse).text);
    }

    [Test]
    public void Snapshot_EchoIgnoresWhitespaceDifference()
    {
        var snap = new JObject { ["id"] = "Q", ["type"] = "chat", ["body"] = "hello  world" };
        var raw  = new RawMessage { type = "chat", isReply = true, replyMessage = snap, body = "hello world" };
        Assert.IsTrue(string.IsNullOrEmpty(ReplyParser.Resolve(raw, _ => null, StubParse).text));
    }

    [Test]
    public void CacheResolvedReplyToIdenticalText_KeepsText()
    {
        // The false-positive guard: a reply whose target genuinely has the SAME text resolves from
        // cache (not snapshot) and must KEEP its text — it is a real reply-to-identical, not an echo.
        var cached = new MessageViewModel { messageId = "Q", senderName = "Bo", text = "Поздравляю!", type = MessageType.Chat, isIncoming = true };
        var raw    = new RawMessage { type = "chat", isReply = true, body = "Поздравляю!",
                                      replyMessage = new JObject { ["id"] = "Q", ["type"] = "chat", ["body"] = "Поздравляю!" } };
        var preview = ReplyParser.Resolve(raw, id => id == "Q" ? cached : null, StubParse);
        Assert.AreEqual("Поздравляю!", preview.text);   // resolved from cache, not blanked
        Assert.AreEqual("Bo", preview.senderName);
    }

    // --- FromFetchedMessage: recover the real quote from a messages/id/get payload ---

    [Test]
    public void FromFetchedMessage_UsesBodyAndSenderName()
    {
        // Shape returned by messages/id/get: senderName populated, contact_name empty.
        var msg = new JObject
        {
            ["id"] = "ACFDF36", ["type"] = "poll", ["fromMe"] = false,
            ["senderName"] = "Baurzhan Urgunshbayev", ["contact_name"] = "",
            ["body"] = "Гоу смотреть финал лиги чемпионов", ["caption"] = ""
        };
        var preview = ReplyParser.FromFetchedMessage(msg, StubParse);
        Assert.AreEqual("ACFDF36", preview.messageId);
        Assert.AreEqual("Гоу смотреть финал лиги чемпионов", preview.text);
        Assert.AreEqual("Baurzhan Urgunshbayev", preview.senderName); // falls back from empty contact_name
    }

    [Test]
    public void FromFetchedMessage_PrefersContactNameWhenPresent()
    {
        var msg = new JObject { ["id"] = "Q", ["type"] = "chat", ["fromMe"] = false,
                                ["contact_name"] = "Aisha", ["senderName"] = "ignored", ["body"] = "hi" };
        Assert.AreEqual("Aisha", ReplyParser.FromFetchedMessage(msg, StubParse).senderName);
    }

    [Test]
    public void FromFetchedMessage_FromMe_SenderIsYou()
    {
        var msg = new JObject { ["id"] = "Q", ["type"] = "chat", ["fromMe"] = true, ["body"] = "mine" };
        Assert.AreEqual("You", ReplyParser.FromFetchedMessage(msg, StubParse).senderName);
    }

    [Test]
    public void FromFetchedMessage_MediaCaption_UsesCaptionAndType()
    {
        var msg = new JObject { ["id"] = "Q", ["type"] = "image", ["fromMe"] = false,
                                ["senderName"] = "Bo", ["body"] = "", ["caption"] = "At the beach" };
        var preview = ReplyParser.FromFetchedMessage(msg, StubParse);
        Assert.AreEqual(MessageType.Image, preview.type);
        Assert.AreEqual("At the beach", preview.text);
    }

    [Test]
    public void FromFetchedMessage_NullOrNoId_ReturnsNone()
    {
        Assert.IsTrue(ReplyParser.FromFetchedMessage(null, StubParse).IsEmpty);
        Assert.IsTrue(ReplyParser.FromFetchedMessage(new JObject { ["body"] = "x" }, StubParse).IsEmpty);
    }

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

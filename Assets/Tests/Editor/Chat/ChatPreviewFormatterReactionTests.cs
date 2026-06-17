using NUnit.Framework;

public class ChatPreviewFormatterReactionTests
{
    [Test]
    public void Mine_WithText_RendersYouReactedToQuote()
    {
        string s = ChatPreviewFormatter.Format("❤️", "reaction", "read", true, "See you tomorrow", "chat");
        Assert.AreEqual("You reacted ❤️ to “See you tomorrow”", s);
    }

    [Test]
    public void Incoming_WithText_RendersReactedToQuote()
    {
        string s = ChatPreviewFormatter.Format("❤️", "reaction", null, false, "See you tomorrow", "chat");
        Assert.AreEqual("Reacted ❤️ to “See you tomorrow”", s);
    }

    [Test]
    public void Mine_NoText_RendersYouReactedEmojiOnly()
    {
        string s = ChatPreviewFormatter.Format("❤️", "reaction", "sent", true, null, null);
        Assert.AreEqual("You reacted ❤️", s);
    }

    [Test]
    public void Incoming_NoText_RendersReactedEmojiOnly()
    {
        string s = ChatPreviewFormatter.Format("👍", "reaction", null, false, null, null);
        Assert.AreEqual("Reacted 👍", s);
    }

    [Test]
    public void MediaTarget_RendersTypeLabelUnquoted()
    {
        string s = ChatPreviewFormatter.Format("❤️", "reaction", null, true, "", "image");
        Assert.AreEqual("You reacted ❤️ to 📷 Photo", s);
    }

    [Test]
    public void EmptyEmoji_RendersReactionRemoved()
    {
        string s = ChatPreviewFormatter.Format("", "reaction", "sent", true, null, null);
        Assert.AreEqual("Reaction removed", s);
    }

    [Test]
    public void ZeroWidthSpaceOnlyEmoji_RendersReactionRemoved()
    {
        // The emoji converter prepends U+200B; a removed reaction must still read as empty.
        string s = ChatPreviewFormatter.Format("​", "reaction", "sent", true, null, null);
        Assert.AreEqual("Reaction removed", s);
    }

    [Test]
    public void LongText_TruncatesWithEllipsis()
    {
        string s = ChatPreviewFormatter.Format("❤️", "reaction", null, false,
            "This is a very long message that exceeds the cap", "chat");
        Assert.AreEqual("Reacted ❤️ to “This is a very long mess…”", s);
    }

    [Test]
    public void NonReactionType_Unaffected()
    {
        string s = ChatPreviewFormatter.Format("Hello", "chat", null, false);
        Assert.AreEqual("Hello", s);
    }
}

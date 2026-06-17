using NUnit.Framework;

public class ChatPreviewFormatterGroupTests
{
    [Test]
    public void Group_IncomingMessage_PrefixesSenderName()
    {
        string s = ChatPreviewFormatter.Format("Hello", "chat", null, false, null, null, "Aliya", true);
        Assert.AreEqual("Aliya: Hello", s);
    }

    [Test]
    public void Group_IncomingMedia_PrefixesSenderBeforeLabel()
    {
        string s = ChatPreviewFormatter.Format("", "image", null, false, null, null, "Aliya", true);
        Assert.AreEqual("Aliya: 📷 Photo", s);
    }

    [Test]
    public void Group_OwnMessage_UsesYouPrefix()
    {
        string s = ChatPreviewFormatter.Format("Hello", "chat", null, true, null, null, "Ayan", true);
        Assert.AreEqual("You: Hello", s);
    }

    [Test]
    public void Group_OwnMessage_TickPrecedesYouPrefix()
    {
        string s = ChatPreviewFormatter.Format("Hello", "chat", "read", true, null, null, "Ayan", true);
        Assert.AreEqual("<sprite name=\"tick_double_blue\"> You: Hello", s);
    }

    [Test]
    public void Group_IncomingReaction_AttributesReactor()
    {
        string s = ChatPreviewFormatter.Format("❤️", "reaction", null, false, "Hi", "chat", "Aliya", true);
        Assert.AreEqual("Aliya reacted ❤️ to “Hi”", s);
    }

    [Test]
    public void Group_OwnReaction_UsesYou()
    {
        string s = ChatPreviewFormatter.Format("❤️", "reaction", "read", true, "Hi", "chat", "Ayan", true);
        Assert.AreEqual("You reacted ❤️ to “Hi”", s);
    }

    [Test]
    public void Group_ReactionRemoved_NoName()
    {
        string s = ChatPreviewFormatter.Format("reaction_remove", "reaction_remove", null, false, null, null, "Aliya", true);
        Assert.AreEqual("Reaction removed", s);
    }

    [Test]
    public void Group_MissingSenderName_NoPrefix()
    {
        string s = ChatPreviewFormatter.Format("Hello", "chat", null, false, null, null, null, true);
        Assert.AreEqual("Hello", s);
    }

    [Test]
    public void Group_MissingSenderName_ReactionFallsBackToReacted()
    {
        string s = ChatPreviewFormatter.Format("❤️", "reaction", null, false, "Hi", "chat", null, true);
        Assert.AreEqual("Reacted ❤️ to “Hi”", s);
    }

    [Test]
    public void NonGroup_SenderNameIgnored()
    {
        // 1:1 rows: a senderName must not add a prefix, and reactions stay "Reacted".
        Assert.AreEqual("Hello",
            ChatPreviewFormatter.Format("Hello", "chat", null, false, null, null, "Aliya", false));
        Assert.AreEqual("Reacted ❤️",
            ChatPreviewFormatter.Format("❤️", "reaction", null, false, null, null, "Aliya", false));
    }

    [Test]
    public void Group_OwnMedia_TickThenYouThenLabel()
    {
        string s = ChatPreviewFormatter.Format("", "image", "read", true, null, null, "Ayan", true);
        Assert.AreEqual("<sprite name=\"tick_double_blue\"> You: 📷 Photo", s);
    }

    [Test]
    public void Group_IncomingReaction_MediaTarget_UnquotedLabel()
    {
        string s = ChatPreviewFormatter.Format("❤️", "reaction", null, false, "", "image", "Aliya", true);
        Assert.AreEqual("Aliya reacted ❤️ to 📷 Photo", s);
    }

    [Test]
    public void Group_IncomingSticker_PrefixThenLabel()
    {
        string s = ChatPreviewFormatter.Format("", "sticker", null, false, null, null, "Aliya", true);
        Assert.AreEqual("Aliya: Sticker", s);
    }

    [Test]
    public void Group_IncomingVoice_PrefixThenEmojiLabel()
    {
        string s = ChatPreviewFormatter.Format("", "voice", null, false, null, null, "Aliya", true);
        Assert.AreEqual("Aliya: 🎤 Voice", s);
    }

    [Test]
    public void Group_EmptyBody_NoDanglingPrefix()
    {
        string s = ChatPreviewFormatter.Format("", "chat", null, false, null, null, "Aliya", true);
        Assert.AreEqual("", s);
    }
}

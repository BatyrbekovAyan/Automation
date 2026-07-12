using NUnit.Framework;

/// <summary>
/// Covers the pure message-type switch seam (MessageTypeParser.From) that
/// ChatManager.ParseMessageType delegates to. Telegram (tapi) sends "text"
/// for plain messages; WhatsApp never does — both map to MessageType.Chat.
/// Anything unrecognized is Unknown (never throws).
/// </summary>
public class ParseMessageTypeTests
{
    [TestCase("chat",     MessageType.Chat)]     // WhatsApp text
    [TestCase("text",     MessageType.Chat)]     // Telegram text (divergence)
    [TestCase("image",    MessageType.Image)]
    [TestCase("video",    MessageType.Video)]
    [TestCase("audio",    MessageType.Audio)]
    [TestCase("ptt",      MessageType.Voice)]    // voice note
    [TestCase("sticker",  MessageType.Sticker)]
    [TestCase("document", MessageType.Document)]
    [TestCase("reaction", MessageType.Reaction)]
    public void From_KnownType_MapsToEnum(string type, MessageType expected)
    {
        Assert.AreEqual(expected, MessageTypeParser.From(type));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("carousel")]
    [TestCase("Text")]   // case-sensitive: only lowercase "text" is Telegram's key
    public void From_UnknownOrEmpty_ReturnsUnknown(string type)
    {
        Assert.AreEqual(MessageType.Unknown, MessageTypeParser.From(type));
    }
}

/// <summary>
/// In-memory resolved preview of a quoted (replied-to) message. Flattened onto
/// NormalizedMessage/MessageViewModel by ChatManager. Never holds a JToken.
/// </summary>
public struct QuotedPreview
{
    public string      messageId;
    public string      senderName;   // "You" for own messages, else the real sender.
    public string      text;         // Snippet or a type label ("Photo", "Voice message", ...).
    public MessageType type;
    public string      thumbnailUrl; // Null for text / when no cached thumb is available.

    public bool IsEmpty => string.IsNullOrEmpty(messageId);
    public static readonly QuotedPreview None = new QuotedPreview();
}

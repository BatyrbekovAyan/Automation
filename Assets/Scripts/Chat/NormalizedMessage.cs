public class NormalizedMessage
{
    public string id;
    public string chatId;
    public string senderName;
    public MessageType messageType;
    public bool fromMe;
    public long time;

    public string text;        // Chat text or Document filename
    public string thumbnailUrl;
    public string mediaUrl;    // URL for image/video/audio
    public string mimeType;    // To distinguish voice from audio
    public float aspectRatio; // ⬅️ Add this (Width / Height)
    public string fileName;
    
    public string videoUrl;  // Real link to the .mp4 file
    public int duration;     // In seconds (for Audio/Video)
    public bool isSticker;   // To toggle transparent background
    
    public long expireTime;
    public long fileSize;
    public int pageCount;

    // Reply quote (resolved in ChatManager.Normalize via ReplyParser).
    public string      quotedMessageId;
    public string      quotedSenderName;
    public string      quotedText;
    public MessageType quotedType;
    public string      quotedThumbnailUrl;

    public DeliveryStatus deliveryStatus;
}
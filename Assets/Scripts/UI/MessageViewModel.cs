using System;

[Serializable]
public class MessageViewModel
{
    public string messageId;
    public string chatId;
    public MessageType type;
    public string text;
    
    public string thumbnailUrl;
    public string mediaUrl; // Image/Thumbnail
    public string videoUrl; // MP4 Link
    
    public string mimeType;
    public bool isIncoming;
    public long timestamp;
    public string fileName;

    public float aspectRatio; 
    public int duration;
    public bool isSticker;
    public string senderName;
    
    public long expireTime;
    public long fileSize;
    public int pageCount;

    public DeliveryStatus deliveryStatus;
}
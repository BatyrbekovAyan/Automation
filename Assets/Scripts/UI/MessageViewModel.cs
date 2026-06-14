using System;
using System.Collections.Generic;

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
    // Within-second order tiebreak — see MessageOrder. Persisted with the
    // cache so same-second messages keep their server order across reopens.
    public int sequence;
    public string fileName;

    public float aspectRatio;
    public int duration;
    public float videoRotation; // degrees (0/90/180/270) from NativeGallery; 0 = unknown -> viewer heuristic
    public bool isSticker;
    public string senderName;
    
    public long expireTime;
    public long fileSize;
    public int pageCount;

    public DeliveryStatus deliveryStatus;

    // Reactions targeting this message, keyed per-reactor by ReactionStore.
    // Null or empty == no reactions. JsonUtility serializes List<MessageReaction>.
    public List<MessageReaction> reactions;
}
using System;
using UnityEngine;

public class ChatViewModel
{
    public string ChatId { get; }
    public string Title { get; }
    public string AvatarUrl { get; }

    public Sprite AvatarSprite { get; set; }
    public string LastMessage { get; private set; }
    public long LastMessageTime { get; private set; }
    public int UnreadCount { get; private set; }
    public string LastMessageId { get; private set; }
    public string LastMessageType { get; private set; }
    public string LastMessageDeliveryStatus { get; private set; }
    public bool IsLastMessageMine { get; private set; }

    // Added for UI display
    public string LastMessageTimeString { get; private set; }
    // public string OnlineStatus { get; set; }
    public event Action<ChatViewModel> OnUpdated;
    public event Action<ChatViewModel> OnLastMessageChanged;

    public ChatViewModel(string chatId, string title, string avatarUrl,
                         string lastMessage, long lastTime, int unreadCount = 0,
                         string lastMessageId = null,
                         string lastMessageType = null,
                         string lastMessageDeliveryStatus = null,
                         bool isLastMessageMine = false)
    {
        ChatId = chatId;
        Title = title;
        AvatarUrl = avatarUrl;
        LastMessage = lastMessage;
        LastMessageTime = lastTime;
        UnreadCount = unreadCount;
        LastMessageId = lastMessageId;
        LastMessageType = lastMessageType;
        LastMessageDeliveryStatus = lastMessageDeliveryStatus;
        IsLastMessageMine = isLastMessageMine;
        LastMessageTimeString = FormatTimestamp(lastTime);

        // OnlineStatus = "tap here for contact info";
    }

    public void UpdateLastMessage(string message, long time)
    {
        if (this.LastMessage == message && this.LastMessageTime == time) return;

        LastMessage = message;
        LastMessageTime = time;
        LastMessageTimeString = FormatTimestamp(time);

        // This will now ONLY fire if a chat genuinely received a new message!
        NotifyUpdated();
        OnLastMessageChanged?.Invoke(this);
    }

    public void UpdateUnreadCount(int count)
    {
        if (count < 0) count = 0;
        if (UnreadCount == count) return;
        UnreadCount = count;
        NotifyUpdated();
    }

    public void UpdateLastMessageId(string id)
    {
        if (LastMessageId == id) return;
        LastMessageId = id;
        // No NotifyUpdated — metadata for API calls, not visual state.
    }

    public void UpdateLastMessageMeta(string type, string deliveryStatus, bool isMine)
    {
        bool changed =
            LastMessageType != type ||
            LastMessageDeliveryStatus != deliveryStatus ||
            IsLastMessageMine != isMine;

        if (!changed) return;

        LastMessageType = type;
        LastMessageDeliveryStatus = deliveryStatus;
        IsLastMessageMine = isMine;
        NotifyUpdated();
    }

    private string FormatTimestamp(long timestamp)
    {
        if (timestamp <= 0) return "";
        DateTime dt = DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime().DateTime;
        DateTime now = DateTime.Now.Date;
        TimeSpan diff = now - dt.Date;

        if (diff.Days == 0) return dt.ToString("HH:mm");
        if (diff.Days == 1) return "Yesterday";
        if (diff.Days < 7) return dt.ToString("dddd");
        return dt.ToString("dd.MM.yy");
    }

    public void NotifyUpdated()
    {
        OnUpdated?.Invoke(this);
    }
}

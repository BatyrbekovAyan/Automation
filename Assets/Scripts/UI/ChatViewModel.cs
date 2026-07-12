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

    // Display name of the last message's sender (Wappi last_message_sender.pushname).
    // Used to prefix group chat-list rows ("Aliya: …"); "You" is substituted for own.
    public string LastMessageSenderName { get; private set; }

    // Group chats: WhatsApp uses the "@g.us" jid suffix; Telegram groups are numeric-id
    // dialogs flagged by their dialog type at construction. Set once so a Telegram group
    // (no suffix) can still be a group. Callers that omit the flag keep the @g.us behavior.
    // Only group rows get a sender prefix (same rule MessageListView uses for bubble headers).
    public bool IsGroup { get; }

    // Reacted-to message context for a reaction last-message. Null = unknown
    // (formatter omits the "to …" clause). Phase 2 backfills these from a fetch.
    public string ReactionTargetText { get; private set; }
    public string ReactionTargetType { get; private set; }

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
                         bool isLastMessageMine = false,
                         string lastMessageSenderName = null,
                         bool isGroup = false)
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
        LastMessageSenderName = lastMessageSenderName;
        // Preserve the @g.us behavior when a caller doesn't pass the flag (existing tests),
        // and let ParseChatsJson pass true for Telegram groups (numeric id, no suffix).
        IsGroup = isGroup || ChatIdFormat.IsGroup(chatId);
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

    /// <summary>
    /// Authoritative last-message sender name from the chat-list payload
    /// (last_message_sender.pushname). Set only when the last message itself changes; may be
    /// empty for WhatsApp LID group participants, in which case the resolver backfills it.
    /// </summary>
    public void SetLastMessageSenderName(string name)
    {
        name ??= "";
        if (LastMessageSenderName == name) return;
        LastMessageSenderName = name;
        NotifyUpdated();
    }

    /// <summary>
    /// Applies resolver-backfilled row details in one shot (a single NotifyUpdated) so the
    /// two-field update can't re-enter the resolver mid-apply. Target text/type apply only to
    /// reaction rows; the sender name only when non-empty so it never wipes a known name.
    /// </summary>
    public void ApplyResolvedRowDetails(string targetText, string targetType, string senderName)
    {
        bool changed = false;
        if (LastMessageType == "reaction" && (ReactionTargetText != targetText || ReactionTargetType != targetType))
        {
            ReactionTargetText = targetText;
            ReactionTargetType = targetType;
            changed = true;
        }
        if (!string.IsNullOrEmpty(senderName) && LastMessageSenderName != senderName)
        {
            LastMessageSenderName = senderName;
            changed = true;
        }
        if (changed) NotifyUpdated();
    }

    /// <summary>
    /// Sets a reaction as the last message and refreshes the row in place — fires
    /// OnUpdated only, never OnLastMessageChanged, so the chat list does NOT reorder.
    /// Used by the live send/receive reaction paths, which hold the target message.
    /// </summary>
    public void SetReactionPreview(string emoji, bool fromMe, string targetText, string targetType,
                                   string senderName = null, string lastMessageId = null)
    {
        LastMessage = emoji ?? "";
        LastMessageType = "reaction";
        IsLastMessageMine = fromMe;
        LastMessageSenderName = senderName;
        ReactionTargetText = targetText;
        ReactionTargetType = targetType;
        // Advance the id to the reaction's own id (incoming live path) so a later bulk merge
        // sees the same id (lastIdChanged=false) and preserves the resolved name/target
        // instead of clobbering them back to the empty LID pushname.
        if (!string.IsNullOrEmpty(lastMessageId)) LastMessageId = lastMessageId;
        NotifyUpdated();
    }

    /// <summary>
    /// Sets the reaction target context alone (used by the bulk fetch to clear stale
    /// live-set text so a newer emoji-only reaction can't inherit an older quote).
    /// </summary>
    public void UpdateReactionContext(string targetText, string targetType)
    {
        if (ReactionTargetText == targetText && ReactionTargetType == targetType) return;
        ReactionTargetText = targetText;
        ReactionTargetType = targetType;
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

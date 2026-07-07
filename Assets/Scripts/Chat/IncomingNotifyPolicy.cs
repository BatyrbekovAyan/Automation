/// <summary>
/// Decides whether a chat-list sync result represents a fresh incoming message
/// worth a local sound/vibration cue. Pure so the guard set is unit-testable:
/// never on the initial cache load, only when the last message actually changed,
/// never for our own outgoing echoes, only while the server reports unread, and
/// suppressed for the chat currently open on screen (the user is already reading it).
/// </summary>
public static class IncomingNotifyPolicy
{
    public static bool ShouldNotify(
        bool isInitialLoad,
        bool lastIdChanged,
        bool lastMessageIsMine,
        int unreadCount,
        string chatId,
        string openChatId,
        bool chatPanelVisible)
    {
        if (isInitialLoad) return false;
        if (!lastIdChanged) return false;
        if (lastMessageIsMine) return false;
        if (unreadCount <= 0) return false;
        if (chatPanelVisible && !string.IsNullOrEmpty(chatId) && chatId == openChatId) return false;
        return true;
    }
}

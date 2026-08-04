using System.Collections.Generic;

/// <summary>
/// The client's own read bookkeeping for chat rows: which message id was last acked to
/// Wappi as read per chat, and which arrival of a fresh batch is the one worth acking.
///
/// Wappi's message/mark/read is fire-and-forget, and chats/filter keeps answering with the
/// pre-ack unread_count for a moment afterwards. That moment lands exactly on the
/// back-navigation sync: SwipeToBack raises OnSlideOutComplete (→ RefreshActiveBotChats)
/// and then hides the panel, so the response parses with chatPanelVisible already false and
/// IncomingNotifyPolicy's open-chat suppression no longer applies. Correcting the server's
/// count against the acked id closes that window for both the badge and the cue.
///
/// Entries are local read TRUTH, not a cache of the server's answer: the owner really did
/// read those messages (they were rendered in the open chat), so a failed or slow ack POST
/// must not resurrect the badge. An entry self-clears the instant a genuinely newer message
/// becomes the row's last one, so a real new arrival is never swallowed.
///
/// Plain C# — no Unity lifetime — so the whole contract is unit-testable.
/// </summary>
public class ReadAckLedger
{
    private readonly Dictionary<string, string> _ackedByChat = new Dictionary<string, string>();

    /// <summary>
    /// Remembers that <paramref name="messageId"/> has been read in this chat. Ignores empty
    /// input so a caller never has to pre-check.
    /// </summary>
    public void Record(string chatId, string messageId)
    {
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(messageId)) return;
        _ackedByChat[chatId] = messageId;
    }

    /// <summary>
    /// The unread count to act on for a chats/filter row: 0 while the row's last message is
    /// the one already acked, otherwise the server's count verbatim — it stays authoritative
    /// for everything else, including a newer unread message arriving after the ack.
    /// </summary>
    public int EffectiveUnread(string chatId, string lastMessageId, int serverUnread)
    {
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(lastMessageId)) return serverUnread;
        if (!_ackedByChat.TryGetValue(chatId, out string ackedId)) return serverUnread;
        return ackedId == lastMessageId ? 0 : serverUnread;
    }

    /// <summary>Drops every entry — the list is being rebuilt for a different bot/channel.</summary>
    public void Clear() => _ackedByChat.Clear();

    /// <summary>
    /// The message a freshly-arrived batch should ack: the newest INCOMING one in canonical
    /// conversation order (<see cref="MessageOrder"/>), or null when the batch is empty or
    /// holds only our own echoes — there is nothing to mark read about an outgoing message.
    /// Ordering is by composite key rather than batch position, so it doesn't depend on the
    /// caller handing them over newest-first.
    /// </summary>
    public static string NewestIncomingId(IReadOnlyList<MessageViewModel> batch)
    {
        if (batch == null) return null;

        MessageViewModel newest = null;
        for (int i = 0; i < batch.Count; i++)
        {
            MessageViewModel candidate = batch[i];
            if (candidate == null || !candidate.isIncoming || string.IsNullOrEmpty(candidate.messageId)) continue;
            if (newest == null || MessageOrder.Compare(candidate, newest) > 0) newest = candidate;
        }

        return newest?.messageId;
    }
}

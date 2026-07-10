using System.Collections.Generic;
using UnityEngine;

// Partial-class accessor (DATA-04) exposing the OPEN chat's recent messages to the
// Phase-2 N8nSuggestionsProvider without widening ChatManager's private state. Mirrors
// ChatManager.Dashboard.cs — this file can read the private _activeChatCache / currentChatId
// that a separate provider class cannot reach. Additive only; ChatManager.cs is untouched.
public partial class ChatManager
{
    /// <summary>
    /// Returns the LAST <paramref name="n"/> messages of the open chat, oldest->newest,
    /// for the suggestions payload. Returns false (messages = null) when no chat is open,
    /// <paramref name="chatId"/> doesn't match the open chat, the cache is null, or the
    /// resulting slice is empty. Reads the private _activeChatCache (ChatManager.cs L157)
    /// scoped to the private currentChatId (ChatManager.cs L139).
    /// </summary>
    public bool TryGetRecentMessages(string chatId, int n, out List<MessageViewModel> messages)
    {
        messages = null;
        if (string.IsNullOrEmpty(chatId) || chatId != currentChatId || _activeChatCache == null)
            return false;

        int start = Mathf.Max(0, _activeChatCache.Count - n);
        messages = _activeChatCache.GetRange(start, _activeChatCache.Count - start);   // oldest->newest
        return messages.Count > 0;
    }
}

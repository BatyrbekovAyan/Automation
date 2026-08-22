using System.Collections.Generic;

// Partial-class accessor (DATA-04) exposing the OPEN chat's recent messages to the
// Phase-2 N8nSuggestionsProvider without widening ChatManager's private state. Mirrors
// ChatManager.Dashboard.cs — this file can read the private _activeChatCache / currentChatId
// that a separate provider class cannot reach. Additive only; ChatManager.cs is untouched.
public partial class ChatManager
{
    /// <summary>
    /// Returns the NEWEST <paramref name="n"/> messages of the open chat, oldest->newest,
    /// for the suggestions payload. Returns false (messages = null) when no chat is open,
    /// <paramref name="chatId"/> doesn't match the open chat, the cache is null, or the
    /// resulting window is empty. Reads the private _activeChatCache (ChatManager.cs L157)
    /// scoped to the private currentChatId (ChatManager.cs L139).
    ///
    /// The window is built by <see cref="RecentMessageWindow.TakeNewest"/> because
    /// _activeChatCache is NEWEST-FIRST: a plain "last n" slice here took the OLDEST n and
    /// handed them back reversed, which anchored the server's trailing-client-run walk on the
    /// chat's oldest message and made a chat opened on an unanswered message abstain. Never
    /// sort or reverse _activeChatCache to "simplify" this — its newest-first order is
    /// load-bearing for the first-screen slice, the pagination queue and the 100-message cap.
    /// </summary>
    public bool TryGetRecentMessages(string chatId, int n, out List<MessageViewModel> messages)
    {
        messages = null;
        if (string.IsNullOrEmpty(chatId) || chatId != currentChatId || _activeChatCache == null)
            return false;

        messages = RecentMessageWindow.TakeNewest(_activeChatCache, n);   // oldest->newest
        return messages.Count > 0;
    }
}

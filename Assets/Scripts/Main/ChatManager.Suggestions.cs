using System.Collections;

// Partial class extension exposing the minimal, additive accessors the reply-suggestions
// feature needs (DATA-04). Additive only — the underlying members stay private in ChatManager.cs.
public partial class ChatManager
{
    /// <summary>
    /// DATA-04: read-only accessor over the private currentChatId (ChatManager.cs L139), so
    /// suggestions can scope to the open chat and the sequence guard can capture it.
    /// </summary>
    public string CurrentChatId => currentChatId;

    /// <summary>
    /// «Личные и рабочие чаты»: the open chat's newest message as its live list holds it (newest first — the app's own sends
    /// are put in at once, the live poll adds what it finds), or null when <paramref name="chatId"/> is not the open chat or
    /// nothing is loaded. «Сделать чат рабочим?» reads who wrote last from it: the chat row lags behind the open chat.
    /// </summary>
    public MessageViewModel NewestOpenChatMessage(string chatId) =>
        !string.IsNullOrEmpty(chatId) && chatId == currentChatId && _activeChatCache != null && _activeChatCache.Count > 0
            ? _activeChatCache[0]
            : null;

    /// <summary>
    /// DATA-04: public hook over the private WaitForChatFetchesToDrain (ChatManager.cs L1300).
    /// Phase-2's live provider WAITS on this before its request — it must NEVER increment
    /// _chatFetchesInFlight (Pitfall 2 / CONCERNS.md: it is not a messages/get caller).
    /// </summary>
    public IEnumerator WaitForChatFetchesDrain() => WaitForChatFetchesToDrain();
}

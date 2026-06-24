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
    /// DATA-04: public hook over the private WaitForChatFetchesToDrain (ChatManager.cs L1300).
    /// Phase-2's live provider WAITS on this before its request — it must NEVER increment
    /// _chatFetchesInFlight (Pitfall 2 / CONCERNS.md: it is not a messages/get caller).
    /// </summary>
    public IEnumerator WaitForChatFetchesDrain() => WaitForChatFetchesToDrain();
}

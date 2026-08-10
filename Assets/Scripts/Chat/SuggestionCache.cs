using System.Collections.Generic;

/// <summary>
/// Session-scope memo of the last rendered suggestion set per chat (audit F9). A chat re-open
/// whose history TAIL hasn't moved renders the cached set instantly and skips the paid LLM
/// call entirely — the morning-rush fix (10 chat opens used to mean 10 skeleton waits for
/// sets that hadn't changed). Plain C# like <see cref="IncomingDebounceGate"/>: no Unity
/// lifetime, no persistence — the controller owns one instance and clears it on bot switch
/// (chat ids can recur across bots, so entries must never outlive the bot they were made for).
///
/// Consistency rule (enforced by the CALLER, SuggestionsController): the tail key is captured
/// when the request is ISSUED and the result is stored only if the tail still matches at
/// result time — either drift direction (message landed mid-flight, or the provider's
/// post-drain snapshot outran the capture) degrades to a cache MISS, never to stale cards.
/// One entry per chat: the latest stored set wins.
/// </summary>
public sealed class SuggestionCache
{
    private readonly Dictionary<string, (string tailKey, SuggestionResult result)> _byChat = new();

    /// <summary>
    /// Identity of a chat's last message. Prefers the server message id (timestamps get
    /// re-normalized between polls); falls back to a ts|sequence|direction|text composite.
    /// An outgoing echo in the same second must read as a NEW tail — direction is part of
    /// the identity. Null message → null (callers treat that as "cannot cache").
    /// </summary>
    public static string TailKey(MessageViewModel tail)
    {
        if (tail == null) return null;
        if (!string.IsNullOrEmpty(tail.messageId)) return "id:" + tail.messageId;
        return $"c:{tail.timestamp}|{tail.sequence}|{(tail.isIncoming ? 1 : 0)}|{tail.text}";
    }

    /// <summary>True + the stored set only when the chat's entry exists AND its tail matches.</summary>
    public bool TryGet(string chatId, string tailKey, out SuggestionResult result)
    {
        result = null;
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(tailKey)) return false;
        if (!_byChat.TryGetValue(chatId, out var entry) || entry.tailKey != tailKey) return false;
        result = entry.result;
        return true;
    }

    /// <summary>
    /// Remember the latest set for a chat. Ok-only by policy — caching an Error/Empty would
    /// freeze that state across re-opens with no request to heal it. Null args are no-ops.
    /// </summary>
    public void Store(string chatId, string tailKey, SuggestionResult result)
    {
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(tailKey)) return;
        if (result == null || result.status != SuggestionStatus.Ok) return;
        _byChat[chatId] = (tailKey, result);
    }

    /// <summary>Drop everything — bot switch (chat ids recur across bots).</summary>
    public void Clear() => _byChat.Clear();
}

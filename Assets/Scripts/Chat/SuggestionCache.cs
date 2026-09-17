using System;
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
/// One entry per (scope, chat): the latest stored set wins.
///
/// S6 (audit stage 5): an entry is valid only for the SCOPE it was made in (bot + channel +
/// profile — a chat id is not globally unique), the KNOWLEDGE VERSION it was generated from
/// (a price / contact / prompt edit changes it, so «сколько стоит» never survives a price
/// change), and a TTL (a «сейчас открыты» answer must not outlive the hour). All three are
/// set by the controller; the useful re-open hit is kept when nothing changed.
/// </summary>
public sealed class SuggestionCache
{
    public const long DefaultTtlMs = 10 * 60 * 1000;

    /// <summary>Injectable clock (tests); defaults to UTC now.</summary>
    public Func<long> NowMs = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public long TtlMs = DefaultTtlMs;

    private static readonly char Sep = (char)31;     // unit separator: never in a bot id, channel or profile id
    private static readonly char KeySep = (char)30;  // record separator between scope and chat id

    private string _scope = "";
    private string _knowledgeVersion = "";
    private readonly Dictionary<string, Entry> _byKey = new();

    private readonly struct Entry
    {
        public readonly string TailKey; public readonly string Version; public readonly long StoredAtMs; public readonly SuggestionResult Result;
        public Entry(string tailKey, string version, long storedAtMs, SuggestionResult result)
        { TailKey = tailKey; Version = version; StoredAtMs = storedAtMs; Result = result; }
    }

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

    /// <summary>The (bot, channel, profile) the next Store/TryGet belong to. Switching scopes does not drop entries — switching back still hits.</summary>
    public void SetScope(string botId, string channel, string profileId)
        => _scope = (botId ?? "") + Sep + (channel ?? "") + Sep + (profileId ?? "");

    /// <summary>The knowledge version the next Store is generated from / the next TryGet must match.</summary>
    public void SetKnowledgeVersion(string version) => _knowledgeVersion = version ?? "";

    /// <summary>True + the stored set only when the entry exists for this scope AND chat, its tail, its knowledge version and its age all still match.</summary>
    public bool TryGet(string chatId, string tailKey, out SuggestionResult result)
    {
        result = null;
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(tailKey)) return false;
        if (!_byKey.TryGetValue(Key(chatId), out var e) || e.TailKey != tailKey) return false;
        if (e.Version != _knowledgeVersion) return false;
        if (NowMs() - e.StoredAtMs > TtlMs) return false;
        result = e.Result;
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
        _byKey[Key(chatId)] = new Entry(tailKey, _knowledgeVersion, NowMs(), result);
    }

    /// <summary>Drop everything — bot switch (chat ids recur across bots).</summary>
    public void Clear() => _byKey.Clear();

    private string Key(string chatId) => _scope + KeySep + chatId;
}

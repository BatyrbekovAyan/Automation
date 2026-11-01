using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// How one chat stands for the «Личные и рабочие чаты» screens (<see cref="ChatAudienceStore.ViewOf"/>): its kind and why,
/// what kind of non-person it is, whether the server holds a decision for it and whether the owner's move is still on its
/// way. A non-person (a group, a channel, a bot, the service account, the chat with oneself) can never be moved.
/// </summary>
public sealed class AudienceView
{
    public AudienceKind Kind;
    public AudienceReason Reason;
    public NonHumanKind NonHuman;
    /// <summary>The server holds a decision for the chat that holds.</summary>
    public bool Decided;
    /// <summary>The owner's move is not confirmed by the server yet.</summary>
    public bool Pending;
    public bool CanMove => NonHuman == NonHumanKind.None;
}

/// <summary>
/// «Личные и рабочие чаты»: the app's mirror of the server's per-chat decisions — the source of truth is the server (the
/// assistant must keep to a decision while the phone is off). Per profile: the moment its channel counts as connected and
/// the decisions that hold, as the ChatAudience list returned them, cached at
/// <c>persistentDataPath/audience_cache.json</c>. Over them: the owner's moves the server has not confirmed yet, drawn at
/// once — kept in memory only, the queue (<see cref="ChatAudienceSyncService"/>, PlayerPrefs) redraws them on launch. Beside
/// them, in the same cache file: the app's own observation — per profile and chat, the EARLIEST last-message time the chat
/// list has ever shown (<see cref="NoteLastMessage"/>; never sent anywhere).
///
/// Two readings, deliberately different:
///   • what a chat IS for the assistant — <see cref="KindOf"/> / <see cref="AllowsAssistant"/>: the owner's move, else the
///     server's decision, else nothing. Never the rule: a chat the server has not decided gets no assistant here.
///   • how a chat is SHOWN — <see cref="ViewOf"/>: the same, and for an undecided chat the shared rule
///     (<see cref="ChatAudienceRule.Decide"/>) over its facts and an estimate of its history: the earliest last message the
///     app has seen for the chat against the connection moment. A chat the app listed before its first message after the
///     connection therefore stays a conversation from before it when that message comes — it does not flip to working, ring
///     or leave «Кто из них клиенты?» while the server decides it. Only an estimate — the server looks at the chat's history
///     itself and decides on the next incoming message; a chat the app first lists after such a message reads as new.
/// Every change is told once through <see cref="OnChanged"/> (a <see cref="Batch"/> of changes once in all).
/// </summary>
public static class ChatAudienceStore
{
    public const string CacheFileName = "audience_cache.json";

    /// <summary>A decision, a moment or a move changed: re-filter and repaint.</summary>
    public static event Action OnChanged;

    private sealed class Mirror
    {
        public long? ConnectedAtMs;
        public readonly Dictionary<string, AudienceDecision> Decisions = new Dictionary<string, AudienceDecision>(StringComparer.Ordinal);
    }

    // profile → chat → the kind the owner moved it to, not yet confirmed by the server
    private static readonly Dictionary<string, Dictionary<string, AudienceKind>> Desires =
        new Dictionary<string, Dictionary<string, AudienceKind>>(StringComparer.Ordinal);

    private static Dictionary<string, Mirror> _mirrors;   // the server's view; null until first read from the cache
    // The app's own: profile → chat → the earliest last-message time (unix seconds) the chat list showed; read with the mirror.
    private static Dictionary<string, Dictionary<string, long>> _seen;
    private static string _cachePathOverride;
    private static int _batchDepth;
    private static bool _changedInBatch;
    private static bool _dirtyInBatch;

    private static string CachePath => _cachePathOverride ?? Path.Combine(Application.persistentDataPath, CacheFileName);

    private static Dictionary<string, Mirror> Mirrors
    {
        get
        {
            EnsureLoaded();
            return _mirrors;
        }
    }

    private static Dictionary<string, Dictionary<string, long>> Seen
    {
        get
        {
            EnsureLoaded();
            return _seen;
        }
    }

    // ---- the server's view ----

    /// <summary>A list answer: each profile it names is replaced by what the server holds now; the others are kept.</summary>
    public static void ApplyList(IReadOnlyList<AudienceProfileState> profiles)
    {
        if (profiles == null) return;
        bool changed = false;
        foreach (var state in profiles)
        {
            if (state == null || !ChatAudienceRequest.IsRealProfileId(state.ProfileId)) continue;
            var mirror = new Mirror { ConnectedAtMs = Positive(state.ConnectedAtMs) };
            if (state.Decisions != null)
                foreach (var decision in state.Decisions)
                    if (decision != null && !string.IsNullOrEmpty(decision.ChatId)) mirror.Decisions[decision.ChatId] = Copy(decision);
            if (Mirrors.TryGetValue(state.ProfileId, out Mirror old) && Same(old, mirror)) continue;
            Mirrors[state.ProfileId] = mirror;
            changed = true;
        }
        if (changed) MirrorChanged();
    }

    /// <summary>The server's moment for the profile's connection (a connected answer): another number starts a new one.</summary>
    public static void ApplyConnected(string profileId, long connectedAtMs)
    {
        if (!ChatAudienceRequest.IsRealProfileId(profileId) || connectedAtMs <= 0) return;
        var mirror = MirrorFor(profileId);
        if (mirror.ConnectedAtMs == connectedAtMs) return;
        mirror.ConnectedAtMs = connectedAtMs;
        MirrorChanged();
    }

    // ---- the owner's moves ----

    /// <summary>The owner moved the chat: drawn at once, until the server answers (or refuses).</summary>
    public static void ApplyDesire(string profileId, string chatId, AudienceKind kind)
    {
        if (!ChatAudienceRequest.IsRealProfileId(profileId) || string.IsNullOrEmpty(chatId)) return;
        if (!Desires.TryGetValue(profileId, out var chats))
        {
            chats = new Dictionary<string, AudienceKind>(StringComparer.Ordinal);
            Desires[profileId] = chats;
        }
        if (chats.TryGetValue(chatId, out AudienceKind drawn) && drawn == kind) return;
        chats[chatId] = kind;
        Changed();
    }

    /// <summary>
    /// The server answered for the move: it is no longer drawn over the decision, and the decision is the server's kind —
    /// the owner's when <paramref name="applied"/>; otherwise the server's own view (a newer decision, or a refused chat),
    /// which keeps a decision of that kind the mirror already holds. The list read after a delivery settles the details.
    /// </summary>
    public static void ApplyDesireResult(string profileId, string chatId, AudienceKind serverKind, bool applied)
    {
        if (!ChatAudienceRequest.IsRealProfileId(profileId) || string.IsNullOrEmpty(chatId)) return;
        bool moveGone = RemoveDesire(profileId, chatId);
        var mirror = MirrorFor(profileId);
        mirror.Decisions.TryGetValue(chatId, out AudienceDecision old);
        bool rewrite = old == null || old.Kind != serverKind || (applied && !old.ByOwner);
        if (rewrite)
        {
            mirror.Decisions[chatId] = new AudienceDecision
            {
                ChatId = chatId, Kind = serverKind, Reason = AudienceReason.Owner, ByOwner = true, Version = old?.Version ?? 0,
            };
            MirrorChanged();
        }
        else if (moveGone)
        {
            Changed();
        }
    }

    /// <summary>The server did not take the move: it is taken off, and the chat is shown where it was.</summary>
    public static void RetractDesire(string profileId, string chatId)
    {
        if (RemoveDesire(profileId, chatId)) Changed();
    }

    // ---- what the app has seen ----

    /// <summary>
    /// The chat list showed the chat with its last message at <paramref name="lastMessageUnixSec"/> (ChatManager.ParseChatsJson,
    /// every row, the cache load included). Kept when it is the EARLIEST such time for the chat — a later one changes nothing —
    /// and written down with the mirror. Tells nothing: the chat's own last-message time is always part of what
    /// <see cref="ViewOf"/> reads, so the time just seen cannot change a view; it matters once a newer message moves the
    /// chat's own time past the connection moment. No time (≤ 0), no chat or no real profile — nothing to keep.
    /// </summary>
    public static void NoteLastMessage(string profileId, string chatId, long lastMessageUnixSec)
    {
        if (!ChatAudienceRequest.IsRealProfileId(profileId) || string.IsNullOrEmpty(chatId) || lastMessageUnixSec <= 0) return;
        if (!Seen.TryGetValue(profileId, out var chats))
        {
            chats = new Dictionary<string, long>(StringComparer.Ordinal);
            Seen[profileId] = chats;
        }
        if (chats.TryGetValue(chatId, out long earliest) && earliest <= lastMessageUnixSec) return;
        chats[chatId] = lastMessageUnixSec;
        if (_batchDepth > 0) _dirtyInBatch = true;
        else WriteCache();
    }

    // ---- reading ----

    /// <summary>The server's moment for the profile's connection (unix ms), null when not known.</summary>
    public static long? ConnectedAtMs(string profileId) =>
        !string.IsNullOrEmpty(profileId) && Mirrors.TryGetValue(profileId, out Mirror mirror) ? mirror.ConnectedAtMs : null;

    /// <summary>The owner's move, else the server's decision, else null — the rule is never asked here.</summary>
    public static AudienceKind? KindOf(string profileId, string chatId)
    {
        if (TryGetDesire(profileId, chatId, out AudienceKind desired)) return desired;
        return DecisionOf(profileId, chatId)?.Kind;
    }

    /// <summary>
    /// How the chat is shown: the owner's move, else the server's decision, else the shared rule over the chat's facts and
    /// its history estimate — the earliest last message the app has seen for the chat (<see cref="NoteLastMessage"/>, or the
    /// chat's own when that is earlier or nothing was noted) against the connection moment (unknown moment → unknown history
    /// → personal).
    /// </summary>
    public static AudienceView ViewOf(string profileId, ChatViewModel chat)
    {
        var view = new AudienceView { Kind = AudienceKind.Personal, Reason = AudienceReason.Unknown };
        if (chat == null) return view;
        AudienceFacts facts = chat.Facts;
        view.NonHuman = ChatAudienceRule.NonHuman(facts);
        AudienceDecision decision = DecisionOf(profileId, chat.ChatId);
        view.Decided = decision != null;
        if (TryGetDesire(profileId, chat.ChatId, out AudienceKind desired))
        {
            view.Kind = desired;
            view.Reason = AudienceReason.Owner;
            view.Pending = true;
        }
        else if (decision != null)
        {
            view.Kind = decision.Kind;
            view.Reason = decision.Reason;
        }
        else
        {
            bool? history = ChatAudienceRule.HistoryBeforeConnection(EarliestLastMessageSec(profileId, chat), ConnectedAtMs(profileId));
            (view.Kind, view.Reason) = ChatAudienceRule.Decide(facts, history, null);
        }
        return view;
    }

    /// <summary>The assistant may work in the chat: it is business by the owner's move or the server's decision.</summary>
    public static bool AllowsAssistant(string profileId, string chatId) => KindOf(profileId, chatId) == AudienceKind.Business;

    // ---- forgetting ----

    /// <summary>A profile that is gone (its bot was deleted): its decisions, its moment, its moves and what the app saw for it.</summary>
    public static void ForgetProfile(string profileId)
    {
        if (string.IsNullOrEmpty(profileId)) return;
        bool mirror = Mirrors.Remove(profileId);
        bool seen = Seen.Remove(profileId);
        bool moves = Desires.Remove(profileId);
        if (mirror || seen) MirrorChanged();
        else if (moves) Changed();
    }

    /// <summary>The full local wipe: everything in memory and the cache file.</summary>
    public static void ClearAll()
    {
        _mirrors = new Dictionary<string, Mirror>(StringComparer.Ordinal);
        _seen = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        Desires.Clear();
        _dirtyInBatch = false;
        try
        {
            if (File.Exists(CachePath)) File.Delete(CachePath);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Debug.LogWarning($"[ChatAudienceStore] could not delete the cache: {e.Message}");
        }
        Changed();
    }

    /// <summary>Changes made inside are told — and written down — once, at the end (many chats moved or settled at once).</summary>
    internal static void Batch(Action changes)
    {
        _batchDepth++;
        try
        {
            changes?.Invoke();
        }
        finally
        {
            _batchDepth--;
            if (_batchDepth == 0)
            {
                if (_dirtyInBatch)
                {
                    _dirtyInBatch = false;
                    WriteCache();
                }
                if (_changedInBatch)
                {
                    _changedInBatch = false;
                    OnChanged?.Invoke();
                }
            }
        }
    }

    // ---- test seams ----

    /// <summary>Points the cache at a test file and starts from nothing (the event's subscribers too).</summary>
    internal static void UseCacheFileForTests(string path)
    {
        _cachePathOverride = path;
        ResetMemory();
        OnChanged = null;
    }

    /// <summary>A restart: the in-memory mirror and the moves go; the next read loads the cache file again.</summary>
    internal static void DropMemoryForTests() => ResetMemory();

    internal static void ResetForTests()
    {
        _cachePathOverride = null;
        ResetMemory();
        OnChanged = null;
    }

    private static void ResetMemory()
    {
        _mirrors = null;
        _seen = null;
        Desires.Clear();
        _batchDepth = 0;
        _changedInBatch = false;
        _dirtyInBatch = false;
    }

    // ---- internals ----

    private static Mirror MirrorFor(string profileId)
    {
        if (!Mirrors.TryGetValue(profileId, out Mirror mirror))
        {
            mirror = new Mirror();
            Mirrors[profileId] = mirror;
        }
        return mirror;
    }

    // The earliest last-message time known for the chat: the one noted for it, or its own when that is earlier or nothing was
    // noted; 0 when there is neither.
    private static long EarliestLastMessageSec(string profileId, ChatViewModel chat)
    {
        long own = chat.LastMessageTime;
        if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(chat.ChatId)
            || !Seen.TryGetValue(profileId, out var chats) || !chats.TryGetValue(chat.ChatId, out long noted))
            return own;
        return own > 0 && own < noted ? own : noted;
    }

    private static AudienceDecision DecisionOf(string profileId, string chatId)
    {
        if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(chatId)) return null;
        return Mirrors.TryGetValue(profileId, out Mirror mirror) && mirror.Decisions.TryGetValue(chatId, out AudienceDecision decision)
            ? decision
            : null;
    }

    private static bool TryGetDesire(string profileId, string chatId, out AudienceKind kind)
    {
        kind = AudienceKind.Personal;
        return !string.IsNullOrEmpty(profileId) && !string.IsNullOrEmpty(chatId)
               && Desires.TryGetValue(profileId, out var chats) && chats.TryGetValue(chatId, out kind);
    }

    private static bool RemoveDesire(string profileId, string chatId)
    {
        if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(chatId) || !Desires.TryGetValue(profileId, out var chats)) return false;
        bool removed = chats.Remove(chatId);
        if (chats.Count == 0) Desires.Remove(profileId);
        return removed;
    }

    private static long? Positive(long? ms) => ms > 0 ? ms : null;

    private static AudienceDecision Copy(AudienceDecision d) => new AudienceDecision
    {
        ChatId = d.ChatId, Kind = d.Kind, Reason = d.Reason, ByOwner = d.ByOwner, Version = d.Version,
    };

    private static bool Same(Mirror a, Mirror b)
    {
        if (a.ConnectedAtMs != b.ConnectedAtMs || a.Decisions.Count != b.Decisions.Count) return false;
        foreach (var pair in a.Decisions)
        {
            if (!b.Decisions.TryGetValue(pair.Key, out AudienceDecision other)) return false;
            AudienceDecision mine = pair.Value;
            if (mine.Kind != other.Kind || mine.Reason != other.Reason || mine.ByOwner != other.ByOwner || mine.Version != other.Version)
                return false;
        }
        return true;
    }

    // The server's view changed: written down (once per batch) and told.
    private static void MirrorChanged()
    {
        if (_batchDepth > 0) _dirtyInBatch = true;
        else WriteCache();
        Changed();
    }

    private static void Changed()
    {
        if (_batchDepth > 0)
        {
            _changedInBatch = true;
            return;
        }
        OnChanged?.Invoke();
    }

    // ---- the cache file ----

    // A cache written before the app kept its last-message times has no `seen`: it reads as nothing seen yet.
    [Serializable]
    private class CacheFile
    {
        public List<CacheProfile> profiles = new List<CacheProfile>();
        public List<CacheSeen> seen = new List<CacheSeen>();
    }

    [Serializable]
    private class CacheSeen
    {
        public string profileId;
        public Dictionary<string, long> chats = new Dictionary<string, long>();
    }

    [Serializable]
    private class CacheProfile
    {
        public string profileId;
        public long? connectedAtMs;
        public List<CacheDecision> decisions = new List<CacheDecision>();
    }

    [Serializable]
    private class CacheDecision
    {
        public string chatId;
        public string kind;
        public string reason;
        public bool byOwner;
        public long version;
    }

    // The mirror and what the app saw, from the cache file — both empty when there is none or it does not read.
    private static void EnsureLoaded()
    {
        if (_mirrors != null && _seen != null) return;
        _mirrors = new Dictionary<string, Mirror>(StringComparer.Ordinal);
        _seen = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
        try
        {
            string path = CachePath;
            if (!File.Exists(path)) return;
            var file = JsonConvert.DeserializeObject<CacheFile>(File.ReadAllText(path));
            LoadMirrors(file?.profiles, _mirrors);
            LoadSeen(file?.seen, _seen);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ChatAudienceStore] the cache is unreadable, starting empty: {e.Message}");
            _mirrors.Clear();
            _seen.Clear();
        }
    }

    private static void LoadMirrors(List<CacheProfile> profiles, Dictionary<string, Mirror> mirrors)
    {
        if (profiles == null) return;
        foreach (var profile in profiles)
        {
            if (profile == null || !ChatAudienceRequest.IsRealProfileId(profile.profileId)) continue;
            var mirror = new Mirror { ConnectedAtMs = Positive(profile.connectedAtMs) };
            foreach (var row in profile.decisions ?? new List<CacheDecision>())
            {
                if (row == null || string.IsNullOrEmpty(row.chatId) || !ChatAudienceRequest.TryParseKind(row.kind, out AudienceKind kind)) continue;
                mirror.Decisions[row.chatId] = new AudienceDecision
                {
                    ChatId = row.chatId, Kind = kind, Reason = ChatAudienceRequest.ParseReason(row.reason),
                    ByOwner = row.byOwner, Version = row.version,
                };
            }
            mirrors[profile.profileId] = mirror;
        }
    }

    private static void LoadSeen(List<CacheSeen> rows, Dictionary<string, Dictionary<string, long>> seen)
    {
        if (rows == null) return;
        foreach (var row in rows)
        {
            if (row?.chats == null || !ChatAudienceRequest.IsRealProfileId(row.profileId)) continue;
            var chats = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in row.chats)
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value > 0) chats[pair.Key] = pair.Value;
            if (chats.Count > 0) seen[row.profileId] = chats;
        }
    }

    private static void WriteCache()
    {
        var file = new CacheFile();
        foreach (var pair in Mirrors)
        {
            var profile = new CacheProfile { profileId = pair.Key, connectedAtMs = pair.Value.ConnectedAtMs };
            foreach (var decision in pair.Value.Decisions.Values)
                profile.decisions.Add(new CacheDecision
                {
                    chatId = decision.ChatId, kind = ChatAudienceRequest.KindWord(decision.Kind),
                    reason = ChatAudienceRequest.ReasonWord(decision.Reason), byOwner = decision.ByOwner, version = decision.Version,
                });
            file.profiles.Add(profile);
        }
        foreach (var pair in Seen)
            file.seen.Add(new CacheSeen { profileId = pair.Key, chats = new Dictionary<string, long>(pair.Value) });
        try
        {
            File.WriteAllText(CachePath, JsonConvert.SerializeObject(file));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Debug.LogWarning($"[ChatAudienceStore] could not write the cache: {e.Message}");
        }
    }
}

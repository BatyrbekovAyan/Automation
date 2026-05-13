using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Per-bot, per-chat persisted queue of unresolved outgoing sends.
/// Entries are added when SendTextMessage fires its optimistic UI update,
/// removed when Wappi acknowledges with a real message id, and left in
/// place (so the bubble can render as Failed and offer tap-to-retry) when
/// the POST fails.
///
/// Storage layout: {cacheRoot}/outbox_{sanitizedChatId}.json — mirrors the
/// per-bot pattern of ChatHistoryCache. Writes are atomic via .tmp +
/// File.Replace.
/// </summary>
public class OutboxStore
{
    /// <summary>
    /// Mutable DTO. Always pass a modified entry back through OutboxStore.Update()
    /// to ensure disk persistence — mutating fields directly on a reference from
    /// GetFor() or Find() will update the in-memory cache but NOT the on-disk file.
    /// </summary>
    [Serializable]
    public class OutboxEntry
    {
        public string tempId;
        public string chatId;
        public string text;
        public long timestamp;
        public int attemptCount;
        public string profileId;
    }

    [Serializable]
    private class OutboxFile
    {
        public List<OutboxEntry> entries = new();
    }

    private readonly Func<string> _getCacheRoot;
    private readonly Dictionary<string, List<OutboxEntry>> _byChatId = new();

    public OutboxStore(Func<string> getCacheRoot)
    {
        _getCacheRoot = getCacheRoot ?? throw new ArgumentNullException(nameof(getCacheRoot));
    }

    public IReadOnlyList<OutboxEntry> GetFor(string chatId) => LoadOrCache(chatId);

    /// <summary>
    /// Searches all currently-loaded chats for the given tempId. Only finds
    /// entries whose chat has been loaded via GetFor(), Add(), or Update() in
    /// the current session — entries persisted on disk but never loaded will
    /// return null. In the production flow, OnChatSelected calls GetFor(chatId)
    /// during the chat-open splice, which preloads the relevant chat before
    /// any tap-to-retry path calls Find.
    /// </summary>
    public OutboxEntry Find(string tempId)
    {
        foreach (var list in _byChatId.Values)
            foreach (var entry in list)
                if (entry.tempId == tempId) return entry;
        return null;
    }

    public void Add(OutboxEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.chatId) || string.IsNullOrEmpty(entry.tempId))
            return;
        var list = LoadOrCache(entry.chatId);
        list.Add(entry);
        Persist(entry.chatId, list);
    }

    /// <summary>
    /// Removes the entry with the given tempId. Same loaded-chat contract as
    /// Find — only operates on chats already loaded into the in-memory cache.
    /// In the production flow, the success path of PostTextMessageRoutine
    /// runs after Add(), so the chat is always loaded when Remove is called.
    /// </summary>
    public void Remove(string tempId)
    {
        foreach (var kvp in _byChatId)
        {
            var list = kvp.Value;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].tempId == tempId)
                {
                    list.RemoveAt(i);
                    Persist(kvp.Key, list);
                    return;
                }
            }
        }
    }

    public void Update(OutboxEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.chatId) || string.IsNullOrEmpty(entry.tempId))
            return;
        var list = LoadOrCache(entry.chatId);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].tempId == entry.tempId)
            {
                list[i] = entry;
                Persist(entry.chatId, list);
                return;
            }
        }
    }

    private List<OutboxEntry> LoadOrCache(string chatId)
    {
        if (_byChatId.TryGetValue(chatId, out var cached)) return cached;

        var list = LoadFromDisk(chatId);
        _byChatId[chatId] = list;
        return list;
    }

    private List<OutboxEntry> LoadFromDisk(string chatId)
    {
        string path = FilePath(chatId);
        if (!File.Exists(path)) return new List<OutboxEntry>();

        try
        {
            string json = File.ReadAllText(path);
            var parsed = JsonUtility.FromJson<OutboxFile>(json);
            return parsed?.entries ?? new List<OutboxEntry>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[OutboxStore] Corrupted outbox at {path}: {ex.Message}. Treating as empty.");
            return new List<OutboxEntry>();
        }
    }

    private void Persist(string chatId, List<OutboxEntry> list)
    {
        string path = FilePath(chatId);
        string tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = JsonUtility.ToJson(new OutboxFile { entries = list }, prettyPrint: false);
            File.WriteAllText(tmp, json);

            if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
            else File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OutboxStore] Failed to persist outbox for {chatId}: {ex.Message}");
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private string FilePath(string chatId)
    {
        string root = _getCacheRoot();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("OutboxStore: getCacheRoot returned null or empty.");
        return Path.Combine(root, $"outbox_{SanitizeChatId(chatId)}.json");
    }

    private static string SanitizeChatId(string chatId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(chatId.Length + 9);
        foreach (var c in chatId)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        // Hash suffix prevents filename collisions when two chat IDs differ
        // only by invalid-filename chars (e.g. "foo/bar" and "foo_bar" both
        // sanitize to "foo_bar"). Mono and IL2CPP both produce stable
        // string.GetHashCode() across processes; this is safe on Android + iOS.
        sb.Append('_');
        sb.Append(Math.Abs(chatId.GetHashCode()).ToString("x8"));
        return sb.ToString();
    }
}

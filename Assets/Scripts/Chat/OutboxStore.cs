using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Discriminator for OutboxEntry. Append-only; persisted as int ordinal.</summary>
public enum OutboxKind { Text = 0, Media = 1 }

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
        // --- existing (unchanged) ---
        public string tempId;
        public string chatId;
        public string text;          // caption for media entries
        public long   timestamp;
        public int    attemptCount;
        public string profileId;

        // --- appended for part c (append-only; JsonUtility fills missing as default) ---
        public int    kind;            // OutboxKind ordinal; 0 = Text (back-compat default)
        public int    attachmentKind;  // AttachmentKind ordinal (Photo=0..Document=3)
        public string mediaPath;       // upload byte source: staged-JPEG path (image) | pick.Path (video/doc)
        public string mimeType;
        public string fileName;
        public string mediaUrl;        // staged://image/{tempId} or staged://document/{tempId}
        public string thumbnailUrl;    // thumb://staged/{tempId} (video)
        public string videoUrl;        // file://{pick.Path} (video, in-session playback)
        public float  aspectRatio;
        public int    duration;
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
    /// In the production flow, the success path of an outgoing send runs
    /// after Add(), so the chat is always loaded when Remove is called.
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

        Debug.LogWarning($"[OutboxStore] Remove called with tempId '{tempId}' but no matching entry found in any loaded chat. " +
                         "If the chat wasn't loaded yet, the entry on disk will be orphaned.");
    }

    /// <summary>
    /// Removes a tempId entry directly from a specific bot's outbox file on disk,
    /// bypassing the in-memory cache. Used by PostTextMessageRoutine's success
    /// path so a send that started on bot A and completed after a switch to bot B
    /// still clears bot A's outbox file (the cacheRoot snapshot taken at send time
    /// points to bot A's folder, not the currently-active bot's).
    /// </summary>
    public void RemoveAt(string cacheRoot, string chatId, string tempId)
    {
        if (string.IsNullOrEmpty(cacheRoot) || string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(tempId))
            return;

        string path = Path.Combine(cacheRoot, $"outbox_{SanitizeChatId(chatId)}.json");
        if (!File.Exists(path)) return;

        try
        {
            string json = File.ReadAllText(path);
            var parsed = JsonUtility.FromJson<OutboxFile>(json);
            if (parsed?.entries == null) return;

            bool removed = false;
            for (int i = parsed.entries.Count - 1; i >= 0; i--)
            {
                if (parsed.entries[i].tempId == tempId)
                {
                    parsed.entries.RemoveAt(i);
                    removed = true;
                    break;
                }
            }

            if (!removed) return;

            // Atomic write through the same .tmp + File.Replace pattern as Persist.
            string tmp = path + ".tmp";
            string newJson = JsonUtility.ToJson(new OutboxFile { entries = parsed.entries }, prettyPrint: false);
            File.WriteAllText(tmp, newJson);

            if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
            else File.Move(tmp, path);

            // If this cacheRoot happens to match the currently-active bot AND we have
            // the chat loaded in memory, also evict the stale entry so subsequent
            // GetFor returns the right list without a disk re-read.
            if (_byChatId.TryGetValue(chatId, out var inMemory))
            {
                for (int i = inMemory.Count - 1; i >= 0; i--)
                {
                    if (inMemory[i].tempId == tempId)
                    {
                        inMemory.RemoveAt(i);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[OutboxStore] RemoveAt failed at {path}: {ex.Message}");
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

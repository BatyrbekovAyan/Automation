using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists the per-bot soft-delete watermark map (chatId -> last-message unix at deletion) to
/// {cacheRoot}/deleted_chats.json. Null/empty/corrupt-safe; never throws. Mirrors ChatHistoryCache.
/// </summary>
public static class DeletedChatStore
{
    private const string FileName = "deleted_chats.json";

    [System.Serializable] private class Entry { public string id; public long ts; }
    [System.Serializable] private class Wrapper { public List<Entry> entries = new List<Entry>(); }

    public static Dictionary<string, long> Load(string cacheRoot)
    {
        var map = new Dictionary<string, long>();
        if (string.IsNullOrEmpty(cacheRoot)) return map;

        string path = Path.Combine(cacheRoot, FileName);
        if (!File.Exists(path)) return map;

        try
        {
            var wrapper = JsonUtility.FromJson<Wrapper>(File.ReadAllText(path));
            if (wrapper?.entries != null)
                foreach (var e in wrapper.entries)
                    if (e != null && !string.IsNullOrEmpty(e.id)) map[e.id] = e.ts;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DeletedChatStore] Corrupt {path}: {ex.Message}. Treating as empty.");
        }
        return map;
    }

    public static void Save(string cacheRoot, Dictionary<string, long> map)
    {
        if (string.IsNullOrEmpty(cacheRoot) || map == null) return;

        var wrapper = new Wrapper();
        foreach (var kvp in map) wrapper.entries.Add(new Entry { id = kvp.Key, ts = kvp.Value });

        try
        {
            if (!Directory.Exists(cacheRoot)) Directory.CreateDirectory(cacheRoot);
            string path = Path.Combine(cacheRoot, FileName);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(wrapper));
            if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
            else File.Move(tmp, path);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DeletedChatStore] Failed to save to {cacheRoot}: {ex.Message}");
        }
    }
}

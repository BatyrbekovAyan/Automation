using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists resolved chat-list row preview details — the reacted-to target text/type and the
/// last message's sender display name — keyed by the row's last-message id, so a group "Name:"
/// prefix and a reaction's "… to “msg”" survive restarts and are fetched at most once per TTL.
/// (Keyed by the reaction's own id for reaction rows, which is the same as last_message_id.)
///
/// Bounded: at most Capacity entries — Put evicts the oldest-resolved entries past the cap.
/// Fresh: an entry that carries a resolved target text older than TtlSeconds is treated as a
/// miss so it re-resolves on a later view, picking up an edit to the reacted-to message.
/// Entries with no target text (sender-name-only / "nothing to show") are stable and exempt.
/// The clock is passed in so callers stay deterministic and the cache stays unit-testable.
/// File: {baseDir}/reaction_targets.json (baseDir is the bot-scoped cache root).
/// </summary>
public static class ReactionTargetCache
{
    private const int Capacity = 500;
    private const long TtlSeconds = 7L * 24 * 60 * 60; // 7 days

    [Serializable]
    public class Entry { public string reactionId; public string text; public string type; public string senderName; public long resolvedAt; }
    [Serializable] private class FileShape { public List<Entry> entries = new List<Entry>(); }

    // In-memory layer keyed by baseDir → (id → Entry); avoids disk IO per row bind.
    private static readonly Dictionary<string, Dictionary<string, Entry>> _mem =
        new Dictionary<string, Dictionary<string, Entry>>();

    public static bool TryGet(string baseDir, string id, long nowUnix,
                              out string text, out string type, out string senderName)
    {
        text = null; type = null; senderName = null;
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(id)) return false;

        var map = LoadMap(baseDir);
        if (!map.TryGetValue(id, out var e)) return false;

        // Only entries carrying a resolved target text can go stale (the target may be edited);
        // sender-name-only / empty entries are stable and exempt from the TTL.
        if (!string.IsNullOrEmpty(e.text) && nowUnix - e.resolvedAt > TtlSeconds) return false;

        text = e.text; type = e.type; senderName = e.senderName;
        return true;
    }

    public static void Put(string baseDir, string id, string text, string type, string senderName, long nowUnix)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(id)) return;

        var map = LoadMap(baseDir);
        map[id] = new Entry { reactionId = id, text = text ?? "", type = type ?? "", senderName = senderName ?? "", resolvedAt = nowUnix };
        EvictToCapacity(map);
        Save(baseDir, map);
    }

    /// <summary>
    /// Drops the in-memory map AND the on-disk file for a cache root (privacy
    /// clear). Both must go together: a surviving _mem entry would keep serving
    /// stale targets and the next Put would re-persist the whole old map.
    /// </summary>
    public static void Clear(string baseDir)
    {
        if (string.IsNullOrEmpty(baseDir)) return;
        _mem.Remove(baseDir);
        try
        {
            string path = PathFor(baseDir);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) { Debug.LogWarning($"[ReactionTargetCache] clear failed: {ex.Message}"); }
    }

    private static void EvictToCapacity(Dictionary<string, Entry> map)
    {
        while (map.Count > Capacity)
        {
            string oldestId = null;
            long oldest = long.MaxValue;
            foreach (var kv in map)
                if (kv.Value.resolvedAt < oldest) { oldest = kv.Value.resolvedAt; oldestId = kv.Key; }
            if (oldestId == null) break;
            map.Remove(oldestId);
        }
    }

    private static Dictionary<string, Entry> LoadMap(string baseDir)
    {
        if (_mem.TryGetValue(baseDir, out var cached)) return cached;

        var map = new Dictionary<string, Entry>();
        try
        {
            string path = PathFor(baseDir);
            if (File.Exists(path))
            {
                var shape = JsonUtility.FromJson<FileShape>(File.ReadAllText(path));
                if (shape?.entries != null)
                    foreach (var e in shape.entries)
                        if (e != null && !string.IsNullOrEmpty(e.reactionId)) map[e.reactionId] = e;
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[ReactionTargetCache] load failed: {ex.Message}"); }

        _mem[baseDir] = map;
        return map;
    }

    private static void Save(string baseDir, Dictionary<string, Entry> map)
    {
        try
        {
            var shape = new FileShape { entries = new List<Entry>(map.Values) };
            string path = PathFor(baseDir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(shape));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch (Exception ex) { Debug.LogWarning($"[ReactionTargetCache] save failed: {ex.Message}"); }
    }

    private static string PathFor(string baseDir) => Path.Combine(baseDir, "reaction_targets.json");
}

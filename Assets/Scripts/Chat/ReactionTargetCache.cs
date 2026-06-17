using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists the resolved reacted-to text/type per reaction message id so a chat-list
/// reaction row's "… to “msg”" survives restarts and is fetched at most once per TTL.
/// Keyed by the reaction's own id (a new reaction = new id = new entry).
///
/// Bounded: at most Capacity entries — Put evicts the oldest-resolved entries past the cap;
/// an evicted reaction just re-resolves next time its row is shown.
/// Fresh: a resolved entry older than TtlSeconds is treated as a miss so it re-resolves on a
/// later view (e.g. next launch), picking up an edit to the reacted-to message. An empty
/// text+type entry records a definitive "nothing to show" (target was beyond the fetch
/// window) and is exempt from the TTL since it cannot change. The clock is passed in so
/// callers stay deterministic and the cache stays unit-testable.
/// File: {baseDir}/reaction_targets.json (baseDir is the bot-scoped cache root).
/// </summary>
public static class ReactionTargetCache
{
    private const int Capacity = 500;
    private const long TtlSeconds = 7L * 24 * 60 * 60; // 7 days

    [Serializable]
    public class Entry { public string reactionId; public string text; public string type; public long resolvedAt; }
    [Serializable] private class FileShape { public List<Entry> entries = new List<Entry>(); }

    // In-memory layer keyed by baseDir → (reactionId → Entry); avoids disk IO per row bind.
    private static readonly Dictionary<string, Dictionary<string, Entry>> _mem =
        new Dictionary<string, Dictionary<string, Entry>>();

    public static bool TryGet(string baseDir, string reactionId, long nowUnix, out string text, out string type)
    {
        text = null; type = null;
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(reactionId)) return false;

        var map = LoadMap(baseDir);
        if (!map.TryGetValue(reactionId, out var e)) return false;

        // "Not found" outcomes (target beyond the window) are stable — never expire.
        bool isNotFound = string.IsNullOrEmpty(e.text) && string.IsNullOrEmpty(e.type);
        if (!isNotFound && nowUnix - e.resolvedAt > TtlSeconds) return false; // stale → re-resolve

        text = e.text; type = e.type;
        return true;
    }

    public static void Put(string baseDir, string reactionId, string text, string type, long nowUnix)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(reactionId)) return;

        var map = LoadMap(baseDir);
        map[reactionId] = new Entry { reactionId = reactionId, text = text ?? "", type = type ?? "", resolvedAt = nowUnix };
        EvictToCapacity(map);
        Save(baseDir, map);
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

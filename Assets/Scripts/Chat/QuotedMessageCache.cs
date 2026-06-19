using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists quoted-message previews recovered by fetching the original via <c>messages/id/get</c>
/// (used when Wappi's <c>reply_message</c> snapshot was missing or echoed the reply's own body).
/// Keyed by the quoted message's id, so a recovered preview survives restarts and is fetched at
/// most once per TTL. Mirrors <see cref="ReactionTargetCache"/>.
///
/// Bounded: at most Capacity entries — Put evicts the oldest-resolved past the cap.
/// Fresh: an entry carrying resolved text older than TtlSeconds is treated as a miss so it
/// re-resolves later, picking up an edit to the quoted message. A not-found entry (empty text)
/// is stable and exempt — it stops us refetching a genuinely unrecoverable (e.g. deleted) target.
/// The clock is passed in so callers stay deterministic and the cache stays unit-testable.
/// File: {baseDir}/quoted_messages.json (baseDir is the bot-scoped cache root).
/// </summary>
public static class QuotedMessageCache
{
    private const int Capacity = 500;
    private const long TtlSeconds = 7L * 24 * 60 * 60; // 7 days

    [Serializable]
    public class Entry { public string id; public string text; public string senderName; public int type; public long resolvedAt; }
    [Serializable] private class FileShape { public List<Entry> entries = new List<Entry>(); }

    // In-memory layer keyed by baseDir → (id → Entry); avoids disk IO per bubble bind.
    private static readonly Dictionary<string, Dictionary<string, Entry>> _mem =
        new Dictionary<string, Dictionary<string, Entry>>();

    public static bool TryGet(string baseDir, string id, long nowUnix,
                              out string text, out string senderName, out MessageType type)
    {
        text = null; senderName = null; type = MessageType.Unknown;
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(id)) return false;

        var map = LoadMap(baseDir);
        if (!map.TryGetValue(id, out var e)) return false;

        // Only entries carrying resolved text can go stale (the target may be edited); a not-found
        // entry is stable and exempt from the TTL.
        if (!string.IsNullOrEmpty(e.text) && nowUnix - e.resolvedAt > TtlSeconds) return false;

        text = e.text; senderName = e.senderName; type = (MessageType)e.type;
        return true;
    }

    public static void Put(string baseDir, string id, string text, string senderName, MessageType type, long nowUnix)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(id)) return;

        var map = LoadMap(baseDir);
        map[id] = new Entry { id = id, text = text ?? "", senderName = senderName ?? "", type = (int)type, resolvedAt = nowUnix };
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
                        if (e != null && !string.IsNullOrEmpty(e.id)) map[e.id] = e;
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[QuotedMessageCache] load failed: {ex.Message}"); }

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
        catch (Exception ex) { Debug.LogWarning($"[QuotedMessageCache] save failed: {ex.Message}"); }
    }

    private static string PathFor(string baseDir) => Path.Combine(baseDir, "quoted_messages.json");
}

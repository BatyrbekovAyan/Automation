using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists the resolved reacted-to text/type per reaction message id so a chat-list
/// reaction row's "… to “msg”" survives restarts and is fetched at most once ever.
/// Keyed by the reaction's own id (a new reaction = new id = new entry). An empty
/// text+type entry records a definitive "nothing to show" so the row is never refetched.
/// File: {baseDir}/reaction_targets.json (baseDir is the bot-scoped cache root).
/// </summary>
public static class ReactionTargetCache
{
    [Serializable] public class Entry { public string reactionId; public string text; public string type; }
    [Serializable] private class FileShape { public List<Entry> entries = new List<Entry>(); }

    // In-memory layer keyed by baseDir → (reactionId → Entry); avoids disk IO per row bind.
    private static readonly Dictionary<string, Dictionary<string, Entry>> _mem =
        new Dictionary<string, Dictionary<string, Entry>>();

    public static bool TryGet(string baseDir, string reactionId, out string text, out string type)
    {
        text = null; type = null;
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(reactionId)) return false;
        var map = LoadMap(baseDir);
        if (map.TryGetValue(reactionId, out var e)) { text = e.text; type = e.type; return true; }
        return false;
    }

    public static void Put(string baseDir, string reactionId, string text, string type)
    {
        if (string.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(reactionId)) return;
        var map = LoadMap(baseDir);
        map[reactionId] = new Entry { reactionId = reactionId, text = text ?? "", type = type ?? "" };
        Save(baseDir, map);
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

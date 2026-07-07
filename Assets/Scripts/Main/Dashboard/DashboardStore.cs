using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public static class DashboardStore
{
    [System.Serializable]
    private class Payload
    {
        public long lastFetchMs;
        public List<DashboardOutcome> outcomes = new();
    }

    private static string Path => System.IO.Path.Combine(Application.persistentDataPath, "dashboard_cache.json");

    public static long LastFetchMs { get; private set; }

    public static void Save(List<DashboardOutcome> outcomes, long nowMs)
    {
        try
        {
            var p = new Payload { lastFetchMs = nowMs, outcomes = outcomes ?? new List<DashboardOutcome>() };
            File.WriteAllText(Path, JsonConvert.SerializeObject(p));
            // Advance only after a successful persist so in-memory and disk agree —
            // a failed write leaves LastFetchMs where Load() set it, so the next
            // visit refetches and re-persists instead of trusting an unwritten cache.
            LastFetchMs = nowMs;
        }
        catch (IOException e) { Debug.LogWarning($"[DashboardStore] save failed: {e.Message}"); }
    }

    public static List<DashboardOutcome> Load()
    {
        try
        {
            if (!File.Exists(Path)) return new List<DashboardOutcome>();
            var p = JsonConvert.DeserializeObject<Payload>(File.ReadAllText(Path));
            if (p == null) return new List<DashboardOutcome>();
            LastFetchMs = p.lastFetchMs;
            return p.outcomes ?? new List<DashboardOutcome>();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[DashboardStore] load failed: {e.Message}");
            return new List<DashboardOutcome>();
        }
    }
}

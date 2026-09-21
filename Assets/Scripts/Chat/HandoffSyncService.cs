using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// A5 (audit stage 4): the app's view of pending handoff requests. Lazy always-active host (the
/// ReplyModeSyncService pattern): polls <c>/webhook/Handoffs</c> on boot, every few minutes and on
/// demand (gated), mirrors the server's per-chat PAUSES into the local per-chat «Вместе» override
/// (never the other way — resuming is the owner's explicit action), plays the in-app cue when a NEW
/// open request appears, and carries the owner's «taken» (chat opened from the request) to the server.
/// Push delivery (APNs/FCM) is NOT here: without keys and a device test it stays an external blocker —
/// the closed-app path is the self-message the server's notifier sends on the bot's own channel.
/// </summary>
public class HandoffSyncService : MonoBehaviour
{
    public const int GateSeconds = 60;
    public const int PeriodicSeconds = 180;
    public const int BootDelaySeconds = 6;
    public const int RequestTimeoutSeconds = 20;

    private static HandoffSyncService instance;

    public static HandoffSyncService Instance
    {
        get
        {
            if (instance != null) return instance;
            if (!Application.isPlaying) return null;
            instance = new GameObject(nameof(HandoffSyncService)).AddComponent<HandoffSyncService>();
            return instance;
        }
    }

    public static HandoffSyncService Existing => instance != null ? instance : null;

    /// <summary>The pending list changed (fetched / taken / done).</summary>
    public static event Action Changed;

    private readonly List<HandoffRequest> _pending = new();
    private readonly List<ReplyOutcomeRow> _recent = new();
    private readonly HashSet<long> _knownOpen = new();
    private long _lastFetchMs;
    private bool _fetching;
    private bool _everFetched;

    public IReadOnlyList<HandoffRequest> Pending => _pending;
    /// <summary>Stage 7.3: the last reply outcomes (ledger rows) for the owner's profiles; filled by <see cref="RefreshStatus"/>.</summary>
    public IReadOnlyList<ReplyOutcomeRow> Recent => _recent;
    public int OpenCount { get { int n = 0; foreach (var r in _pending) if (r.IsOpen) n++; return n; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Application.isPlaying) return;
        var s = Instance;
        if (s != null) s.StartCoroutine(s.BootRoutine());
    }

    private void Awake()
    {
        instance ??= this;
        DontDestroyOnLoad(gameObject);
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private IEnumerator BootRoutine()
    {
        yield return new WaitForSeconds(BootDelaySeconds);     // let the auth bootstrap and LoadBots settle
        while (true)
        {
            Refresh(force: true);
            yield return new WaitForSeconds(PeriodicSeconds);
        }
    }

    /// <summary>Fetch the pending list; gated to one call per <see cref="GateSeconds"/> unless forced.</summary>
    public void Refresh(bool force)
    {
        if (_fetching) return;
        if (!force && !DashboardRefreshGate.ShouldFetch(_lastFetchMs, NowMs(), GateSeconds * 1000L)) return;
        StartCoroutine(FetchRoutine("list"));
    }

    /// <summary>Stage 7.3: fetch the pending list AND the last reply outcomes (op status) — not gated; a screen asks for it explicitly.</summary>
    public void RefreshStatus() { if (!_fetching) StartCoroutine(FetchRoutine("status")); }

    /// <summary>The owner opened the chat from the request: the server may now say «taken» (a real view, never assumed).</summary>
    public void Take(long id, string profileId) => StartCoroutine(MutateRoutine("take", id, profileId));

    /// <summary>The owner closed the request explicitly; the server lifts the chat's pause.</summary>
    public void Done(long id, string profileId) => StartCoroutine(MutateRoutine("done", id, profileId));

    // ---- impure adapters --------------------------------------------------------------------

    private static List<BotProfiles> BotDescriptors()
    {
        var list = new List<BotProfiles>();
        var parent = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (parent == null) return list;
        foreach (Transform t in parent)
        {
            var bot = t.GetComponent<Bot>();
            if (bot == null) continue;
            list.Add(new BotProfiles { botName = t.name, whatsappProfileId = bot.whatsappProfileId, telegramProfileId = bot.telegramProfileId });
        }
        return list;
    }

    private IEnumerator FetchRoutine(string op)
    {
        _fetching = true;
        try
        {
            var descriptors = BotDescriptors();
            var profiles = DashboardProfileMap.AuthedProfiles(descriptors);
            if (profiles.Count == 0) yield break;
            string url = $"{Manager.n8nBaseUrl}/webhook/Handoffs";
            string body = JsonConvert.SerializeObject(new { op, profileIds = profiles });
            UnityWebRequest req = null;
            yield return AuthorizedRequest.Send(() => AuthorizedRequest.JsonPost(url, body, RequestTimeoutSeconds), r => req = r);
            using var scope = req;
            if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning($"[Handoffs] list failed [{req.responseCode}] {req.error}"); yield break; }
            var parsed = HandoffsResponse.Parse(req.downloadHandler.text);
            if (parsed == null || !parsed.success) yield break;
            _lastFetchMs = NowMs();
            Apply(parsed, DashboardProfileMap.ProfileToBot(descriptors));
        }
        finally { _fetching = false; }
    }

    private void Apply(HandoffsResponse parsed, IReadOnlyDictionary<string, DashboardProfileRef> map)
    {
        // Mirror the server's pauses into the local per-chat «Вместе» override — pause only, never resume.
        foreach (var plan in HandoffPauseMirror.Compute(parsed.pausedChats, map,
                     (bot, chat) => (SemiAutoStore.TryGetOverride(bot, chat, out var on), on)))
            SemiAutoStore.Set(plan.BotId, plan.ChatId, true);

        bool newOpen = false;
        var open = new HashSet<long>();
        foreach (var r in parsed.requests)
        {
            if (r == null) continue;
            if (r.IsOpen) { open.Add(r.id); if (_everFetched && !_knownOpen.Contains(r.id)) newOpen = true; }
        }
        _pending.Clear();
        _pending.AddRange(parsed.requests);
        if (parsed.recent != null && parsed.recent.Count > 0) { _recent.Clear(); _recent.AddRange(parsed.recent); }
        _knownOpen.Clear();
        _knownOpen.UnionWith(open);
        _everFetched = true;
        if (newOpen) NotificationFx.OnIncomingDetected();     // the in-app cue (sound/vibration per NotifPrefs)
        Changed?.Invoke();
    }

    private IEnumerator MutateRoutine(string op, long id, string profileId)
    {
        if (id <= 0 || string.IsNullOrEmpty(profileId)) yield break;
        string url = $"{Manager.n8nBaseUrl}/webhook/Handoffs";
        string body = JsonConvert.SerializeObject(new { op, id, profileIds = new[] { profileId } });
        UnityWebRequest req = null;
        yield return AuthorizedRequest.Send(() => AuthorizedRequest.JsonPost(url, body, RequestTimeoutSeconds), r => req = r);
        using var scope = req;
        if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning($"[Handoffs] {op} failed [{req.responseCode}] {req.error}"); yield break; }
        var parsed = HandoffsResponse.Parse(req.downloadHandler.text);
        if (parsed == null || !parsed.success) yield break;
        _lastFetchMs = NowMs();
        Apply(parsed, DashboardProfileMap.ProfileToBot(BotDescriptors()));
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Reliable delivery of the owner's reply-mode choices (A2). Lives on a GameObject nothing
/// deactivates (AuthClient/UploadCenter pattern). Every write goes through the persisted
/// <see cref="ReplyModeSyncQueue"/>: a tap records a DESIRE (and is painted as pending), the
/// loop sends due entries in batches through <see cref="AuthorizedRequest"/>, and only the
/// server's per-profile confirmation flips an entry to CONFIRMED. Transient failures back off
/// and survive a restart; a server value NEWER than the desire is adopted and announced through
/// <see cref="OnServerNewer"/> so the UI reflects the server rather than the stale tap.
/// </summary>
public class ReplyModeSyncService : MonoBehaviour
{
    public const string StoreKey = "CrReplyMode.Queue";
    public const int RequestTimeoutSeconds = 30;

    private static ReplyModeSyncService instance;

    public static ReplyModeSyncService Instance
    {
        get
        {
            if (instance != null) return instance;
            if (!Application.isPlaying) return null;
            instance = new GameObject(nameof(ReplyModeSyncService)).AddComponent<ReplyModeSyncService>();
            return instance;
        }
    }

    public static ReplyModeSyncService Existing => instance != null ? instance : null;

    /// <summary>(botId, chatId) — a desire's state changed (pending → confirmed / failed): repaint.</summary>
    public static event Action<string, string> OnStateChanged;

    /// <summary>(botId, profileId, chatId, suppressed) — the server holds a NEWER choice; the UI must adopt it.</summary>
    public static event Action<string, string, string, bool> OnServerNewer;

    private ReplyModeSyncQueue.Model _model;
    private bool _looping;

    private void Awake()
    {
        instance ??= this;
        _model = ReplyModeSyncQueue.Deserialize(PlayerPrefs.GetString(StoreKey, ""), NowMs());
        Persist();
        Kick();
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void Persist()
    {
        PlayerPrefs.SetString(StoreKey, ReplyModeSyncQueue.Serialize(_model));
        PlayerPrefs.Save();
    }

    public bool IsPending(string botId, string chatId = null) => _model != null && ReplyModeSyncQueue.AnyPending(_model, botId, chatId);
    public bool HasFailed(string botId) => _model != null && ReplyModeSyncQueue.AnyFailed(_model, botId);

    /// <summary>Record the owner's choice and start delivering it. Returns the minted version.</summary>
    public long Desire(string botId, IReadOnlyList<string> profileIds, string chatId, bool suppressed)
    {
        if (profileIds == null || profileIds.Count == 0) return 0;
        long v = ReplyModeSyncQueue.Desire(_model, botId, profileIds, chatId ?? ReplyModeSyncQueue.BotDefaultChat, suppressed, NowMs());
        Persist();
        OnStateChanged?.Invoke(botId, chatId ?? ReplyModeSyncQueue.BotDefaultChat);
        Kick();
        return v;
    }

    /// <summary>
    /// Desire + wait until every one of these entries is confirmed or gives up (the SaveWorkflows
    /// gate: a «Вместе» bot's suppression row must be CONFIRMED before its workflow activates).
    /// </summary>
    public IEnumerator SendAndConfirm(string botId, IReadOnlyList<string> profileIds, string chatId, bool suppressed, Action<bool> done, float timeoutSeconds = 45f)
    {
        long v = Desire(botId, profileIds, chatId, suppressed);
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            bool allSettled = true, allConfirmed = true;
            foreach (var pid in profileIds)
            {
                var e = ReplyModeSyncQueue.Find(_model, pid, chatId ?? ReplyModeSyncQueue.BotDefaultChat);
                if (e == null) continue;
                if (e.desiredVersion != v) { allConfirmed = false; break; }          // superseded by a newer tap
                if (e.IsPending) { allSettled = false; break; }
                if (!(e.state == ReplyModeSyncQueue.StateConfirmed && e.confirmedSuppressed == suppressed)) allConfirmed = false;
            }
            if (allSettled) { done?.Invoke(allConfirmed); yield break; }
            yield return null;
        }
        done?.Invoke(false);
    }

    private void Kick()
    {
        if (!_looping && isActiveAndEnabled) StartCoroutine(Loop());
    }

    private IEnumerator Loop()
    {
        _looping = true;
        try
        {
            while (true)
            {
                var due = ReplyModeSyncQueue.Due(_model, NowMs());
                if (due.Count == 0)
                {
                    bool anyPending = false;
                    foreach (var e in _model.entries) if (e.IsPending) { anyPending = true; break; }
                    if (!anyPending) yield break;                 // queue drained: the next Desire re-kicks
                    yield return new WaitForSecondsRealtime(1f);  // waiting for a backoff to elapse
                    continue;
                }
                foreach (var batch in ReplyModeSyncQueue.Batches(due))
                    yield return SendBatch(batch);
            }
        }
        finally
        {
            _looping = false;
        }
    }

    private IEnumerator SendBatch(List<ReplyModeSyncQueue.Entry> batch)
    {
        var first = batch[0];
        var ids = new List<string>(batch.Count);
        foreach (var e in batch) ids.Add(e.profileId);
        long sentVersion = first.desiredVersion;
        string url = $"{Manager.n8nBaseUrl}/webhook/SetReplyMode";
        string body = Manager.BuildReplyModePayload(ids, first.chatId, first.desiredSuppressed, sentVersion);

        UnityWebRequest www = null;
        yield return AuthorizedRequest.Send(() => AuthorizedRequest.JsonPost(url, body, RequestTimeoutSeconds), r => www = r);
        using var scope = www;

        long now = NowMs();
        bool parsed = ReplyModeSyncResponse.TryParse(www.downloadHandler?.text, out var res);
        if (www.result == UnityWebRequest.Result.Success && parsed && res.Success)
        {
            foreach (var e in batch)
            {
                var row = res.Applied.Find(r => r.ProfileId == e.profileId);
                if (row == null) { ReplyModeSyncQueue.OnSendFailed(e, now); continue; }   // not reported: treat as transient
                var outcome = ReplyModeSyncQueue.OnResponse(e, sentVersion, row.Applied, row.Suppressed, row.Version);
                if (outcome == ReplyModeSyncQueue.Outcome.AdoptedServer)
                    OnServerNewer?.Invoke(e.botId, e.profileId, e.chatId, row.Suppressed);
            }
        }
        else if (www.responseCode == 400 || www.responseCode == 403)
        {
            Debug.LogWarning($"[ReplyModeSync] refused [{www.responseCode}] {www.downloadHandler?.text}");
            foreach (var e in batch) ReplyModeSyncQueue.OnRejected(e);
        }
        else
        {
            Debug.LogWarning($"[ReplyModeSync] transient failure [{www.responseCode}] {www.error} — will retry");
            foreach (var e in batch) ReplyModeSyncQueue.OnSendFailed(e, now);
        }
        Persist();
        OnStateChanged?.Invoke(first.botId, first.chatId);
    }
}

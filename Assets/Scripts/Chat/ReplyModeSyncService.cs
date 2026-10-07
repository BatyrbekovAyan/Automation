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
/// R5 (2026-09-27): the service boots itself on every launch (it used to exist only once a tap, a chat-open heal or
/// a Save created it — a cold start with an undelivered choice sent nothing and painted it as confirmed), announces
/// the loaded unconfirmed choices to the UI at once, and holds its first send behind <see cref="ReplyModeSendGate"/>
/// until Manager has initialised billing and the auth session.
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

    private static bool sendGateOpen;

    private ReplyModeSyncQueue.Model _model;
    private bool _looping;
    private float _bootedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Application.isPlaying) return;
        sendGateOpen = false;               // every launch starts closed; Manager opens it after billing + session
        _ = Instance;                       // Awake resumes the persisted queue
    }

    /// <summary>Called by the boot sequence once billing is initialised and the auth session op has finished.</summary>
    public static void OpenSendGate()
    {
        sendGateOpen = true;
        Existing?.Kick();
    }

    private void Awake()
    {
        instance ??= this;
        DontDestroyOnLoad(gameObject);
        _bootedAt = Time.realtimeSinceStartup;
        _model = ReplyModeSyncQueue.Deserialize(PlayerPrefs.GetString(StoreKey, ""), NowMs());
        Persist();
        // Surfaces that painted before the service existed (the chats header, bot cards) read «no service» as
        // «confirmed»: tell each one whose choice is still unconfirmed, before anything is sent.
        foreach (var (botId, chatId) in ReplyModeSyncQueue.PendingScopes(_model)) OnStateChanged?.Invoke(botId, chatId);
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
    /// The chat-open heal (R4): re-deliver what the queue still owes for this chat WITHOUT making a new decision —
    /// see <see cref="ReplyModeSyncQueue.Reassert"/>. Only the owner's tap goes through <see cref="Desire"/>.
    /// </summary>
    public void Reassert(string botId, string profileId, string chatId, bool suppressed)
    {
        if (string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(chatId)) return;
        ReplyModeSyncQueue.Reassert(_model, botId, profileId, chatId, suppressed, NowMs());
        Persist();
        OnStateChanged?.Invoke(botId, chatId);
        Kick();
    }

    /// <summary>A deleted bot's queued choices must never be sent again (they would recreate wiped server rows).</summary>
    public void ForgetBot(string botId)
    {
        ReplyModeSyncQueue.ForgetBot(_model, botId);
        Persist();
    }

    /// <summary>The full local wipe: the in-memory queue would otherwise re-persist itself after PlayerPrefs.DeleteAll.</summary>
    public void ForgetAll()
    {
        ReplyModeSyncQueue.ForgetAll(_model);
        Persist();
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
            while (!ReplyModeSendGate.CanSend(sendGateOpen, Time.realtimeSinceStartup - _bootedAt))
                yield return null;                                 // R5: no send before billing + session (or the safety timeout)
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
        long decidedAtMs = first.decidedAtMs;        // R4-clock: captured with the version it belongs to
        string chatId = first.chatId;
        bool suppressed = first.desiredSuppressed;
        string url = $"{Manager.n8nBaseUrl}/webhook/SetReplyMode";

        // R4-clock: the body is built INSIDE the factory — the decision's age is measured when each attempt leaves
        // (after the session wait, and again for the retry after a 401 refresh), never once up front.
        UnityWebRequest www = null;
        yield return AuthorizedRequest.Send(() => AuthorizedRequest.JsonPost(url,
                Manager.BuildReplyModePayload(ids, chatId, suppressed, sentVersion, ReplyModeSyncQueue.DecisionAgeMs(decidedAtMs, NowMs())),
                RequestTimeoutSeconds), r => www = r);
        using var scope = www;

        bool parsed = ReplyModeSyncResponse.TryParse(www.downloadHandler?.text, out var res);
        var status = ReplyModeSyncQueue.BatchStatus.Transient;
        if (www.result == UnityWebRequest.Result.Success && parsed && res.Success)
            status = ReplyModeSyncQueue.BatchStatus.Answered;
        else if (www.responseCode == 400 || www.responseCode == 403)
        {
            status = ReplyModeSyncQueue.BatchStatus.Rejected;
            Debug.LogWarning($"[ReplyModeSync] refused [{www.responseCode}] {www.downloadHandler?.text}");
        }
        else
            Debug.LogWarning($"[ReplyModeSync] transient failure [{www.responseCode}] {www.error} — will retry");

        // Version-guarded (R5): an answer for v1 never settles a desire the owner made while it was in flight.
        var adopted = ReplyModeSyncQueue.ApplyBatchResult(batch, sentVersion, status, res?.Applied, NowMs());
        Persist();
        foreach (var e in adopted)
            if (_model.entries.Contains(e))                 // a bot deleted meanwhile (ForgetBot) is not reconciled back to life
                OnServerNewer?.Invoke(e.botId, e.profileId, e.chatId, e.confirmedSuppressed);
        OnStateChanged?.Invoke(first.botId, first.chatId);
    }
}

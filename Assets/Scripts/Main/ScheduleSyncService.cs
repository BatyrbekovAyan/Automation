using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Part B «расписание бота» (spec 2026-10-14 §4 B2): reliable delivery of the owner's schedule to
/// <c>/webhook/SetSchedule</c> — the <see cref="ReplyModeSyncService"/> pattern. Lives on a GameObject nothing deactivates.
/// A Save that changed the schedule (or a settings Create that gave the bot a new channel) records a DESIRE in the
/// persisted <see cref="ScheduleSyncQueue"/> (PlayerPrefs <c>CrSchedule.Queue</c>, one desire per bot); the loop delivers
/// it through <see cref="AuthorizedRequest"/>, retries transient failures with a bounded backoff, stops on a refusal,
/// and settles it on the server's per-profile answer (applied, or superseded by a newer server version).
///
/// It boots itself on every launch (an undelivered Save survives a restart) and holds its first send behind the same
/// gate as the reply-mode queue (<see cref="ReplyModeSendGate"/>, opened by Manager after billing + the session op).
/// The profile ids are resolved at SEND time from the bot's stored channels and the session token
/// (<see cref="ScheduleSyncQueue.Sendable"/>) — a re-paired channel is followed, a retired one is never sent.
/// V12 mirror: while Manager.DeleteBotOnServer runs, the bot is HELD (<see cref="Hold"/>; a request already on the wire
/// is drained first through <see cref="IsSending"/>), so no schedule send can land after Delete Bot Files removed the
/// bot's rows; <see cref="Release"/> when the server keeps the bot, <see cref="ForgetBot"/> once it goes.
/// Fix round 3 (I-1): every change of a desire is announced (<see cref="OnStateChanged"/>) and readable
/// (<see cref="StateOf"/>) — the settings section shows a schedule that is still on its way or failed — and an
/// undelivered desire is re-armed on a return to the app (at most once a minute) and when the bot's settings open
/// (<see cref="Rearm"/>), not only on the next launch.
/// </summary>
public class ScheduleSyncService : MonoBehaviour
{
    public const string StoreKey = "CrSchedule.Queue";
    public const int RequestTimeoutSeconds = 30;

    private static ScheduleSyncService instance;
    private static bool sendGateOpen;

    /// <summary>(botId) — a bot's desire was queued, answered, failed, re-armed or forgotten: repaint. null = every bot.</summary>
    public static event Action<string> OnStateChanged;

    private ScheduleSyncQueue.Model _model;
    private bool _looping;
    private float _bootedAt;
    private readonly HashSet<string> _held = new HashSet<string>();   // bots whose server deletion is in flight (V12)
    private string _sendingBot;                                         // the bot whose request is on the wire right now
    private long _lastFocusRearmMs;                                     // I-1: the last return-to-the-app re-arm

    public static ScheduleSyncService Instance
    {
        get
        {
            if (instance != null) return instance;
            if (!Application.isPlaying) return null;
            instance = new GameObject(nameof(ScheduleSyncService)).AddComponent<ScheduleSyncService>();
            return instance;
        }
    }

    public static ScheduleSyncService Existing => instance != null ? instance : null;

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

    /// <summary>Queue the bot's SAVED schedule (BotScheduleStore) in the bot's zone — after a Save that changed it.</summary>
    public static void DesireSaved(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return;
        Instance?.Desire(botName, BotScheduleStore.Read(botName), ScheduleModel.ResolveZone(BusinessTimeZone.ForBot(botName)));
    }

    /// <summary>
    /// A settings Create gave the bot a channel: the saved schedule must reach that profile too (the bot's line moves a
    /// predecessor's row on a re-pair, but a channel added to the bot has none). Nothing when the owner never saved one —
    /// no row reads as «no schedule» on the server.
    /// </summary>
    public static void DesireSavedForNewChannel(string botName)
    {
        if (BotScheduleStore.HasSaved(botName)) DesireSaved(botName);
    }

    private void Awake()
    {
        instance ??= this;
        DontDestroyOnLoad(gameObject);
        _bootedAt = Time.realtimeSinceStartup;
        _model = ScheduleSyncQueue.Deserialize(PlayerPrefs.GetString(StoreKey, ""), NowMs());
        Changed(null);   // a section painted before the service existed read «no service» as «confirmed»
        Kick();
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void Persist()
    {
        PlayerPrefs.SetString(StoreKey, ScheduleSyncQueue.Serialize(_model));
        PlayerPrefs.Save();
    }

    // Every change of a desire's state is persisted AND announced — the settings section repaints off the event.
    private void Changed(string botId)
    {
        Persist();
        OnStateChanged?.Invoke(botId);
    }

    /// <summary>Where the bot's schedule delivery stands (the settings section's line).</summary>
    public ScheduleDelivery StateOf(string botId) => ScheduleSyncQueue.StateOf(_model, botId);

    public bool IsPending(string botId) => StateOf(botId) == ScheduleDelivery.Pending;
    public bool HasFailed(string botId) => StateOf(botId) == ScheduleDelivery.Failed;

    /// <summary>
    /// I-1: back in the app — a desire a dead network parked (or a refusal since fixed) is tried again, at most once a
    /// minute (<see cref="ScheduleSyncQueue.ShouldRearmOnFocus"/>, the AuthClient.OnApplicationFocus pattern). The boot
    /// gate still holds a cold start's first send.
    /// </summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        long now = NowMs();
        if (!ScheduleSyncQueue.ShouldRearmOnFocus(hasFocus, now, _lastFocusRearmMs)) return;
        _lastFocusRearmMs = now;
        RearmAndKick(null, now);
    }

    /// <summary>I-1: the bot's settings opened — its undelivered schedule is tried again now; a settled one is untouched.</summary>
    public void Rearm(string botId)
    {
        if (string.IsNullOrEmpty(botId)) return;
        RearmAndKick(botId, NowMs());
    }

    private void RearmAndKick(string botId, long nowMs)
    {
        if (!ScheduleSyncQueue.Rearm(_model, botId, nowMs)) return;
        Changed(botId);
        Kick();
    }

    /// <summary>Record the bot's schedule as its one desire and start delivering it. Returns the minted version.</summary>
    public long Desire(string botId, BotSchedule schedule, string timeZone)
    {
        if (string.IsNullOrEmpty(botId)) return 0;
        long version = ScheduleSyncQueue.Desire(_model, botId, schedule, timeZone, NowMs());
        Changed(botId);
        Kick();
        return version;
    }

    /// <summary>A deleted bot's desire must never be sent again (it would recreate the rows Delete Bot Files removed).</summary>
    public void ForgetBot(string botId)
    {
        ScheduleSyncQueue.ForgetBot(_model, botId);
        _held.Remove(botId ?? "");
        Changed(botId);
    }

    /// <summary>
    /// V12: the bot's server deletion is in flight — send nothing for it meanwhile. The desire is NOT forgotten yet: if
    /// the server keeps the bot, <see cref="Release"/> delivers it after all. In memory only (a restart mid-deletion
    /// leaves the bot on the phone, unheld).
    /// </summary>
    public void Hold(string botId)
    {
        if (!string.IsNullOrEmpty(botId)) _held.Add(botId);
    }

    /// <summary>V12: a request for this bot is on the wire (a hold stops the NEXT send, not one already sent).</summary>
    public bool IsSending(string botId) => _sendingBot != null && _sendingBot == botId;

    /// <summary>V12: the server kept the bot (retry / refused) — its queued desire goes out again.</summary>
    public void Release(string botId)
    {
        if (_held.Remove(botId ?? "")) Kick();
    }

    /// <summary>The full local wipe: the in-memory queue would otherwise re-persist itself after PlayerPrefs.DeleteAll.</summary>
    public void ForgetAll()
    {
        ScheduleSyncQueue.ForgetAll(_model);
        Changed(null);
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
                yield return null;                                 // no send before billing + session (or the safety timeout)
            while (true)
            {
                var due = ScheduleSyncQueue.Due(_model, NowMs(), _held);    // V12: a bot being deleted sends nothing
                if (due.Count == 0)
                {
                    if (!ScheduleSyncQueue.AnyPending(_model, _held)) yield break;   // drained: a Desire / Release re-kicks
                    yield return new WaitForSecondsRealtime(1f);             // waiting for a backoff to elapse
                    continue;
                }
                foreach (var entry in due)
                    yield return Send(entry);
            }
        }
        finally
        {
            _looping = false;
        }
    }

    private IEnumerator Send(ScheduleSyncQueue.Entry entry)
    {
        yield return AuthClient.Instance.EnsureSession();         // the token's profile list decides what may travel
        if (!_model.entries.Contains(entry)) yield break;         // forgotten meanwhile (bot deleted / full wipe)
        // V12: held while the session op ran — nothing yields from here to the request, so a hold either stops this send
        // or finds it on the wire (IsSending) and waits for it. Release re-kicks the untouched desire.
        if (_held.Contains(entry.botId)) yield break;
        var auth = AuthClient.Existing;
        if (auth == null || auth.Claims == null)
        {
            ScheduleSyncQueue.OnSendFailed(entry, NowMs());       // no session yet: nothing can be authorised
            Changed(entry.botId);
            yield break;
        }
        var ids = ScheduleSyncQueue.Sendable(CurrentProfileIds(entry.botId), auth.Owns);
        if (ids.Count == 0)
        {
            ScheduleSyncQueue.OnNothingToSend(entry);             // no live channel: the next Save / settings Create re-arms it
            Changed(entry.botId);
            yield break;
        }

        // Captured with the version they belong to: a Save landing mid-flight must not lend this request its values.
        long sentVersion = entry.version;
        BotSchedule sent = entry.Schedule;
        string zone = entry.timeZone;
        UnityWebRequest www = null;
        _sendingBot = entry.botId;
        try
        {
            yield return AuthorizedRequest.Send(() => AuthorizedRequest.JsonPost($"{Manager.n8nBaseUrl}{SetScheduleRequest.Path}",
                    SetScheduleRequest.ComposeBody(ids, sent.Enabled, sent.DaysMask, sent.StartMin, sent.EndMin, zone, sentVersion),
                    RequestTimeoutSeconds), r => www = r);
        }
        finally
        {
            _sendingBot = null;
        }
        using var scope = www;

        string body = www.downloadHandler?.text;
        var status = ScheduleSyncResponse.Classify(www.responseCode, body, out var rows);
        if (status == ScheduleSyncQueue.BatchStatus.Rejected)
            Debug.LogWarning($"[ScheduleSync] refused [{www.responseCode}] {body}");
        else if (status == ScheduleSyncQueue.BatchStatus.Transient)
            Debug.LogWarning($"[ScheduleSync] transient failure [{www.responseCode}] {www.error} — will retry");
        ScheduleSyncQueue.ApplyResult(entry, sentVersion, status, ids, rows, NowMs());
        Changed(entry.botId);
    }

    // The bot's channels as stored NOW — the keys LoadBots reads, so a channel re-paired since the Save is followed.
    private static List<string> CurrentProfileIds(string botId) => new List<string>
    {
        PlayerPrefs.GetString(botId + "WhatsappProfileId", Bot.UnauthedProfileSentinel),
        PlayerPrefs.GetString(botId + "TelegramProfileId", Bot.UnauthedProfileSentinel),
    };
}

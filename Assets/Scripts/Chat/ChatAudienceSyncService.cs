using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// «Личные и рабочие чаты»: keeps the app's mirror of the server's chat decisions (<see cref="ChatAudienceStore"/>) and
/// delivers what the owner and the app tell the server, through the ChatAudience webhook — the
/// <see cref="ScheduleSyncService"/> pattern. Lives on a GameObject nothing deactivates and boots itself on every launch.
///
///   • Moves — <see cref="MakeBusiness"/> / <see cref="MakePersonal"/>: drawn at once, queued as desires in the persisted
///     <see cref="ChatAudienceSyncQueue"/> (PlayerPrefs <c>CrAudience.Queue</c>), sent per profile through
///     <see cref="AuthorizedRequest"/>, retried with a bounded backoff, settled on the server's per-chat answer. A move
///     the server did not take goes back on the screen first, then <see cref="OnWriteFailed"/> tells the toast.
///   • Connection reports — <see cref="ReportConnected"/>, after every confirmed pairing: the same queue, sent first; the
///     answer is the profile's connection moment — and when another number restarted it (the server's word, or a moment later
///     than the one mirrored), «Кто из них клиенты?» is owed again (<see cref="PersonalChatsOnboarding.RequestForProfile"/>).
///   • The list — <see cref="RequestList"/>: read at launch, on a bot switch, on a return to the app (at most once a
///     minute) and 2 s after every delivered write. A list answered while a write was delivered is stale and is dropped.
///     And <see cref="RequestListSoon"/>: 3 s after an incoming message in a chat the app holds no verdict for — the
///     server decides that chat on the message — one read for a whole burst.
///
/// Nothing is sent before the reply-mode send gate opens (billing + the session; <see cref="ReplyModeSendGate"/>). V12
/// mirror: while Manager.DeleteBotOnServer runs, the bot is HELD (<see cref="Hold"/>; a request already on the wire is
/// drained through <see cref="IsSending"/>), so nothing can land after Delete Bot Files removed the bot's decisions;
/// <see cref="Release"/> when the server keeps the bot, <see cref="ForgetBot"/> once it goes, <see cref="ForgetAll"/> on
/// the full wipe.
/// </summary>
public class ChatAudienceSyncService : MonoBehaviour
{
    public const string StoreKey = "CrAudience.Queue";
    public const int RequestTimeoutSeconds = 30;

    /// <summary>The list is read again this long after each delivered write — the server's own view settles it.</summary>
    public const float ListAfterDeliverySeconds = 2f;

    /// <summary>A return to the app reads the list at most once in this long.</summary>
    public const long ListFocusGapMs = 60_000;

    /// <summary>
    /// <see cref="RequestListSoon"/> reads the list this long after the incoming message that asked for it — time for the
    /// server to decide the chat on that message.
    /// </summary>
    public const float ListSoonSeconds = 3f;

    private static ChatAudienceSyncService instance;
    private static bool sendGateOpen;

    /// <summary>(botName, chatIds) — the server did not accept these moves: the chats are back where they were.</summary>
    public static event Action<string, IReadOnlyList<string>> OnWriteFailed;

    private ChatAudienceSyncQueue.Model _model;
    private readonly HashSet<string> _held = new HashSet<string>();   // bots whose server deletion is in flight (V12)
    private string _sendingBot;                                         // the bot whose request is on the wire right now
    private bool _looping;
    private bool _listing;
    private bool _listWanted;
    private float _bootedAt;
    private long _lastFocusRearmMs;
    private long _lastListMs;
    private int _deliveries;          // writes delivered — a list answered across one of them is stale
    private int _forgets;             // bots forgotten — a list answered across one must not bring them back
    private Coroutine _listAfterDelivery;
    private Coroutine _listSoon;      // the one pending «read soon» — every request meanwhile shares it
    private ChatManager _chatManager;

    /// <summary>Raised on every <see cref="RequestListSoon"/>, before any timer — the parse path's request, observable in tests.</summary>
    internal static event Action ListSoonRequested;

    public static ChatAudienceSyncService Instance
    {
        get
        {
            if (instance != null) return instance;
            if (!Application.isPlaying) return null;
            instance = new GameObject(nameof(ChatAudienceSyncService)).AddComponent<ChatAudienceSyncService>();
            return instance;
        }
    }

    public static ChatAudienceSyncService Existing => instance != null ? instance : null;

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

    // ---- the contract ----

    /// <summary>The owner made these chats of the profile business (the assistant works there).</summary>
    public static void MakeBusiness(string botName, string profileId, IReadOnlyList<string> chatIds) =>
        Instance?.Desire(botName, profileId, chatIds, AudienceKind.Business);

    /// <summary>The owner made these chats of the profile personal (the assistant stays out).</summary>
    public static void MakePersonal(string botName, string profileId, IReadOnlyList<string> chatIds) =>
        Instance?.Desire(botName, profileId, chatIds, AudienceKind.Personal);

    /// <summary>A pairing of the profile's channel was confirmed — the server takes the moment and the number from it.</summary>
    public static void ReportConnected(string profileId) => Instance?.Connected(profileId);

    /// <summary>Read the server's decisions for the bots' profiles; an unforced read waits out <see cref="ListFocusGapMs"/>.</summary>
    public static void RequestList(bool force = false) => Instance?.ReadList(force);

    /// <summary>
    /// An incoming message reached a chat the app holds no verdict for (ChatManager.ParseChatsJson): the server decides
    /// that chat on the message, so read the list once, <see cref="ListSoonSeconds"/> later, forced — the minute's throttle
    /// belongs to the other triggers. One timer: requests while it runs share its read, so a burst of messages costs one
    /// read, and a request after it fired starts the next one (a steady stream is read every few seconds, never starved).
    /// </summary>
    public static void RequestListSoon()
    {
        ListSoonRequested?.Invoke();
        Instance?.ScheduleListSoon();
    }

    /// <summary>
    /// V12: the bot's server deletion is in flight — send nothing for it meanwhile. Nothing is forgotten yet: if the server
    /// keeps the bot, <see cref="Release"/> delivers it after all. In memory only (a restart mid-deletion leaves it unheld).
    /// </summary>
    public static void Hold(string botName)
    {
        var service = Instance;
        if (service != null && !string.IsNullOrEmpty(botName)) service._held.Add(botName);
    }

    /// <summary>V12: a request for this bot is on the wire (a hold stops the NEXT send, not one already sent).</summary>
    public static bool IsSending(string botName)
    {
        var service = Existing;
        return service != null && service._sendingBot != null && service._sendingBot == botName;
    }

    /// <summary>V12: the server kept the bot (retry / refused) — its queued entries go out again.</summary>
    public static void Release(string botName)
    {
        var service = Existing;
        if (service != null && service._held.Remove(botName ?? "")) service.Kick();
    }

    /// <summary>A deleted bot: its queued entries are never sent, and the mirror of its profiles goes.</summary>
    public static void ForgetBot(string botName) => Instance?.Forget(botName);

    /// <summary>The full local wipe: the in-memory queue would otherwise re-persist itself after PlayerPrefs.DeleteAll.</summary>
    public static void ForgetAll() => Instance?.ForgetEverything();

    // ---- lifecycle ----

    private void Awake()
    {
        instance ??= this;
        DontDestroyOnLoad(gameObject);
        _bootedAt = Time.realtimeSinceStartup;
        _model = ChatAudienceSyncQueue.Deserialize(PlayerPrefs.GetString(StoreKey, ""), NowMs());
        Persist();
        DrawUndelivered();
        Kick();
        ReadList(force: true);              // the launch read waits at the send gate like every send
    }

    private void Start()
    {
        _chatManager = ChatManager.Instance;
        if (_chatManager != null) _chatManager.OnActiveBotChanged += HandleActiveBotChanged;
    }

    private void OnDestroy()
    {
        if (_chatManager != null) _chatManager.OnActiveBotChanged -= HandleActiveBotChanged;
    }

    private void HandleActiveBotChanged(string botId) => ReadList(force: true);

    /// <summary>
    /// Back in the app — undelivered entries are tried again and the list is read, at most once a minute
    /// (<see cref="ChatAudienceSyncQueue.ShouldRearmOnFocus"/>, the AuthClient.OnApplicationFocus pattern).
    /// </summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        long now = NowMs();
        if (!ChatAudienceSyncQueue.ShouldRearmOnFocus(hasFocus, now, _lastFocusRearmMs)) return;
        _lastFocusRearmMs = now;
        if (ChatAudienceSyncQueue.Rearm(_model, now))
        {
            Persist();
            Kick();
        }
        ReadList(force: false);
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void Persist()
    {
        PlayerPrefs.SetString(StoreKey, ChatAudienceSyncQueue.Serialize(_model));
        PlayerPrefs.Save();
    }

    // The moves still on their way are drawn where the owner put them (the mirror keeps none across a restart).
    private void DrawUndelivered()
    {
        ChatAudienceStore.Batch(() =>
        {
            foreach (var desire in _model.desires) ChatAudienceStore.ApplyDesire(desire.profileId, desire.chatId, desire.Kind);
        });
    }

    // ---- recording ----

    private void Desire(string botName, string profileId, IReadOnlyList<string> chatIds, AudienceKind kind)
    {
        ChatAudienceSyncQueue.AddDesires(_model, botName, profileId, chatIds, kind, NowMs(), out List<string> accepted);
        if (accepted.Count == 0) return;
        Persist();
        ChatAudienceStore.Batch(() =>
        {
            foreach (string chatId in accepted) ChatAudienceStore.ApplyDesire(profileId, chatId, kind);
        });
        Kick();
    }

    private void Connected(string profileId)
    {
        if (!ChatAudienceRequest.IsRealProfileId(profileId)) return;
        ChatAudienceSyncQueue.AddConnection(_model, BotHolding(profileId), profileId, NowMs());
        Persist();
        Kick();
    }

    private void Forget(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return;
        var profiles = new HashSet<string>(ProfilesOf(botName));
        profiles.UnionWith(ChatAudienceSyncQueue.ForgetBot(_model, botName, profiles));
        _held.Remove(botName);
        _forgets++;
        Persist();
        ChatAudienceStore.Batch(() =>
        {
            foreach (string profileId in profiles) ChatAudienceStore.ForgetProfile(profileId);
        });
    }

    private void ForgetEverything()
    {
        ChatAudienceSyncQueue.ForgetAll(_model);
        _held.Clear();
        _forgets++;
        Persist();
        ChatAudienceStore.ClearAll();
    }

    // The bot that holds the profile now — its name scopes the queue's hold and forget; "" when no bot on the phone does.
    private static string BotHolding(string profileId)
    {
        Transform root = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (root == null) return "";
        for (int i = 0; i < root.childCount; i++)
        {
            var bot = root.GetChild(i).GetComponent<Bot>();
            if (bot != null && (bot.whatsappProfileId == profileId || bot.telegramProfileId == profileId)) return bot.name;
        }
        return "";
    }

    // Every profile the phone holds for the bot: both channels' current ids and the ones the server retired.
    private static List<string> ProfilesOf(string botName)
    {
        var ids = new List<string>();
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(botName) : null;
        if (bot != null)
        {
            ids.Add(bot.whatsappProfileId);
            ids.Add(bot.telegramProfileId);
        }
        ids.Add(ProfileRetirement.MarkerId(botName, ChatChannel.WhatsApp));
        ids.Add(ProfileRetirement.MarkerId(botName, ChatChannel.Telegram));
        ids.RemoveAll(id => !ChatAudienceRequest.IsRealProfileId(id));
        return ids;
    }

    // The profiles the list reads: the bots' current channels that the session token lists.
    private static List<string> ListedProfiles(AuthClient auth)
    {
        var ids = new List<string>();
        Transform root = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (root == null) return ids;
        for (int i = 0; i < root.childCount; i++)
        {
            var bot = root.GetChild(i).GetComponent<Bot>();
            if (bot == null) continue;
            foreach (string id in new[] { bot.whatsappProfileId, bot.telegramProfileId })
                if (ChatAudienceRequest.IsRealProfileId(id) && auth.Owns(id) && !ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    // ---- delivering ----

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
                yield return null;                                  // no send before billing + session (or the safety timeout)
            while (true)
            {
                long now = NowMs();
                var connection = ChatAudienceSyncQueue.NextDueConnection(_model, now, _held);
                if (connection != null)
                {
                    yield return SendConnected(connection);           // connection reports go before any move
                    continue;
                }
                var batch = ChatAudienceSyncQueue.NextDueBatch(_model, now, _held);
                if (batch.Count > 0)
                {
                    yield return SendSet(batch);
                    continue;
                }
                if (!ChatAudienceSyncQueue.AnyPending(_model, _held)) yield break;   // drained: a new entry / Release re-kicks
                yield return new WaitForSecondsRealtime(1f);                         // waiting for a backoff to elapse
            }
        }
        finally
        {
            _looping = false;
        }
    }

    private IEnumerator SendConnected(ChatAudienceSyncQueue.Connection entry)
    {
        yield return AuthClient.Instance.EnsureSession();         // the token's profile list decides what may travel
        if (!_model.connections.Contains(entry)) yield break;     // forgotten meanwhile (bot deleted / full wipe)
        // V12: held while the session op ran — nothing yields from here to the request, so a hold either stops this send
        // or finds it on the wire (IsSending) and waits for it. Release re-kicks the untouched report.
        if (_held.Contains(entry.botId)) yield break;
        var auth = AuthClient.Existing;
        if (auth == null || auth.Claims == null)
        {
            ChatAudienceSyncQueue.ApplyConnectedResult(_model, entry, entry.reports, ChatAudienceSyncQueue.BatchStatus.Transient, NowMs());
            Persist();                                            // no session yet: nothing can be authorised
            yield break;
        }
        if (!auth.Owns(entry.profileId) || BotHolding(entry.profileId).Length == 0)
        {
            // Not a profile of ours any more — the token does not list it, or no bot on the phone holds it (a wizard Create
            // that failed deleted it, a re-pairing replaced it): nothing to report.
            ChatAudienceSyncQueue.DropConnection(_model, entry);
            Persist();
            yield break;
        }

        string profileId = entry.profileId;
        int sentReport = entry.reports;
        int forgets = _forgets;
        UnityWebRequest www = null;
        _sendingBot = entry.botId;
        try
        {
            yield return AuthorizedRequest.Send(() => AuthorizedRequest.JsonPost($"{Manager.n8nBaseUrl}{ChatAudienceRequest.Path}",
                    ChatAudienceRequest.ConnectedBody(profileId), RequestTimeoutSeconds), r => www = r);
        }
        finally
        {
            _sendingBot = null;
        }
        using var scope = www;

        var status = ChatAudienceResponse.ClassifyConnected(www.responseCode, www.downloadHandler?.text, out long connectedAtMs, out bool restarted);
        LogUnanswered(ChatAudienceRequest.OpConnected, status, www);
        ChatAudienceSyncQueue.ApplyConnectedResult(_model, entry, sentReport, status, NowMs());
        Persist();
        if (status != ChatAudienceSyncQueue.BatchStatus.Answered || forgets != _forgets) yield break;
        _deliveries++;
        long? mirrored = ChatAudienceStore.ConnectedAtMs(profileId);   // read before the answer replaces it
        ChatAudienceStore.ApplyConnected(profileId, connectedAtMs);
        // Another number on the channel is a new connection: «Кто из них клиенты?» is owed again to the bot holding the profile.
        if (ChatAudienceConnectPolicy.NumberRestarted(restarted, mirrored, connectedAtMs))
            PersonalChatsOnboarding.RequestForProfile(profileId, BotNames());
        ListAfterDelivery();
    }

    // The names of the bots on the phone.
    private static List<string> BotNames()
    {
        var names = new List<string>();
        Transform root = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        if (root == null) return names;
        for (int i = 0; i < root.childCount; i++) names.Add(root.GetChild(i).name);
        return names;
    }

    private IEnumerator SendSet(List<ChatAudienceSyncQueue.Desire> batch)
    {
        yield return AuthClient.Instance.EnsureSession();
        batch.RemoveAll(d => !_model.desires.Contains(d));        // forgotten meanwhile (bot deleted / full wipe)
        // V12: held while the session op ran — see SendConnected.
        if (batch.Count == 0 || _held.Contains(batch[0].botId)) yield break;
        string botId = batch[0].botId;
        string profileId = batch[0].profileId;
        var sent = ChatAudienceSyncQueue.Snapshot(batch);         // each move's version, captured with its kind
        var auth = AuthClient.Existing;
        if (auth == null || auth.Claims == null)
        {
            Settle(botId, profileId, ChatAudienceSyncQueue.ApplySetResult(_model, profileId, sent,
                ChatAudienceSyncQueue.BatchStatus.Transient, null, NowMs()));   // no session yet: retried later
            yield break;
        }

        UnityWebRequest www = null;
        _sendingBot = botId;
        try
        {
            yield return AuthorizedRequest.Send(() => AuthorizedRequest.JsonPost($"{Manager.n8nBaseUrl}{ChatAudienceRequest.Path}",
                    ChatAudienceRequest.SetBody(profileId, sent), RequestTimeoutSeconds), r => www = r);
        }
        finally
        {
            _sendingBot = null;
        }
        using var scope = www;

        var status = ChatAudienceResponse.ClassifySet(www.responseCode, www.downloadHandler?.text, out List<AudienceSetRow> rows);
        LogUnanswered(ChatAudienceRequest.OpSet, status, www);
        Settle(botId, profileId, ChatAudienceSyncQueue.ApplySetResult(_model, profileId, sent, status, rows, NowMs()));
        if (status != ChatAudienceSyncQueue.BatchStatus.Answered) yield break;
        _deliveries++;
        ListAfterDelivery();
    }

    // One answer's effect: the queue's verdict is persisted, the mirror takes the server's view, and the moves the server
    // did not take go back on the screen — before the event the toast listens to.
    private void Settle(string botId, string profileId, ChatAudienceSyncQueue.SetOutcome outcome)
    {
        Persist();
        ChatAudienceStore.Batch(() =>
        {
            foreach (var chat in outcome.Settled) ChatAudienceStore.ApplyDesireResult(profileId, chat.ChatId, chat.Kind, chat.Applied);
            foreach (string chatId in outcome.Rejected) ChatAudienceStore.RetractDesire(profileId, chatId);
        });
        List<string> notAccepted = outcome.NotAccepted();
        if (notAccepted.Count > 0) OnWriteFailed?.Invoke(botId, notAccepted);
    }

    private static void LogUnanswered(string op, ChatAudienceSyncQueue.BatchStatus status, UnityWebRequest www)
    {
        if (status == ChatAudienceSyncQueue.BatchStatus.Rejected)
            Debug.LogWarning($"[ChatAudienceSync] {op} refused [{www.responseCode}] {www.url}: {www.downloadHandler?.text}");
        else if (status == ChatAudienceSyncQueue.BatchStatus.Transient)
            Debug.LogWarning($"[ChatAudienceSync] {op} not answered [{www.responseCode}] {www.url}: {www.error} — will retry");
    }

    // ---- reading the list ----

    private void ReadList(bool force)
    {
        long now = NowMs();
        if (!force && now >= _lastListMs && now - _lastListMs < ListFocusGapMs) return;
        _listWanted = true;
        if (!_listing && isActiveAndEnabled) StartCoroutine(ListLoop());
    }

    private IEnumerator ListLoop()
    {
        _listing = true;
        try
        {
            while (!ReplyModeSendGate.CanSend(sendGateOpen, Time.realtimeSinceStartup - _bootedAt))
                yield return null;                                  // the list waits for the session like every send
            while (_listWanted)
            {
                _listWanted = false;
                _lastListMs = NowMs();
                yield return FetchList();
            }
        }
        finally
        {
            _listing = false;
        }
    }

    private IEnumerator FetchList()
    {
        yield return AuthClient.Instance.EnsureSession();
        var auth = AuthClient.Existing;
        if (auth == null || auth.Claims == null) yield break;
        List<string> ids = ListedProfiles(auth);
        for (int start = 0; start < ids.Count; start += ChatAudienceRequest.MaxListProfiles)
        {
            var chunk = ids.GetRange(start, Math.Min(ChatAudienceRequest.MaxListProfiles, ids.Count - start));
            int deliveries = _deliveries, forgets = _forgets;
            UnityWebRequest www = null;
            yield return AuthorizedRequest.Send(() => AuthorizedRequest.JsonPost($"{Manager.n8nBaseUrl}{ChatAudienceRequest.Path}",
                    ChatAudienceRequest.ListBody(chunk), RequestTimeoutSeconds), r => www = r);
            using var scope = www;

            var status = ChatAudienceResponse.ClassifyList(www.responseCode, www.downloadHandler?.text, out List<AudienceProfileState> profiles);
            LogUnanswered(ChatAudienceRequest.OpList, status, www);
            if (status != ChatAudienceSyncQueue.BatchStatus.Answered) continue;
            // A write delivered (or a bot forgotten) while this read was on its way: its answer may predate it — the read
            // 2 s after that write is the fresher one.
            if (deliveries != _deliveries || forgets != _forgets) continue;
            if (ChatAudienceSyncQueue.RaiseFloor(_model, HighestOwnerVersion(profiles))) Persist();
            ChatAudienceStore.ApplyList(profiles);
        }
    }

    private static long HighestOwnerVersion(List<AudienceProfileState> profiles)
    {
        long highest = 0;
        foreach (var profile in profiles)
            foreach (var decision in profile.Decisions)
                if (decision.ByOwner) highest = Math.Max(highest, decision.Version);
        return highest;
    }

    private void ListAfterDelivery()
    {
        if (_listAfterDelivery != null) StopCoroutine(_listAfterDelivery);
        _listAfterDelivery = StartCoroutine(ListAfterDeliveryRoutine());
    }

    private IEnumerator ListAfterDeliveryRoutine()
    {
        yield return new WaitForSecondsRealtime(ListAfterDeliverySeconds);
        _listAfterDelivery = null;
        ReadList(force: true);
    }

    private void ScheduleListSoon()
    {
        if (_listSoon != null || !isActiveAndEnabled) return;   // a read is already on its way — this request shares it
        _listSoon = StartCoroutine(ListSoonRoutine());
    }

    private IEnumerator ListSoonRoutine()
    {
        yield return new WaitForSecondsRealtime(ListSoonSeconds);
        _listSoon = null;
        ReadList(force: true);
    }
}

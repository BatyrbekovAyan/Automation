using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

[DefaultExecutionOrder(-100)]
public partial class ChatManager : MonoBehaviour
{
    // Settings
    public static int MessagesPerPage = 50;

    /// <summary>
    /// Three-phase chat-open state machine. Prep runs cache load and queues sync results
    /// without touching UI. Slide is the slide-in animation with all heavy main-thread
    /// work gated. Populate fires OnBatchMessagesLoaded and drains queued sync results.
    /// Idle is the steady state (chat list visible, or chat fully open and settled).
    /// Slide-out is also represented by Idle — IsSliding handles its own gating.
    /// </summary>
    public enum ChatOpenPhase { Idle, Prep, Slide, Populate }

    /// <summary>
    /// Public read-only access to the chat-open phase. Subscribers (MessageListView,
    /// MessageItemView.AcquireDecodeSlot, SyncLatestMessages) gate their heavy work on this.
    /// </summary>
    public ChatOpenPhase Phase => _phase;
    private ChatOpenPhase _phase = ChatOpenPhase.Idle;

    [Header("UI Panels")]
    public GameObject ChatListPanel;
    public GameObject MessageListPanel;
    
    public List<ChatViewModel> Chats = new();
    private Dictionary<string, ChatViewModel> chatLookup = new();
    private HashSet<string> seenMessageIds = new HashSet<string>();

    public static ChatManager Instance;
    
    // Events
    public event Action<ChatViewModel> OnChatAdded;
    public event Action OnChatListCleared;
    public event Action<string> OnChatSelected;
    public event Action<List<MessageViewModel>, bool, bool> OnBatchMessagesLoaded;
    public event Action<List<MessageViewModel>> OnLiveMessagesReceived;

    /// <summary>
    /// Fires when an outgoing message's delivery status changes.
    /// oldMessageId matches the bubble's current MessageViewModel.messageId.
    /// newMessageId is the post-change id — for the optimistic-send → server-ack
    /// transition it's the real Wappi id; for in-place status updates it's the
    /// same as oldMessageId.
    /// </summary>
    public event Action<string, string, DeliveryStatus> OnMessageStatusChanged;

    /// <summary>
    /// Fires when a cached message's media URL is refreshed from a later
    /// server fetch. Subscribers receive the same MessageViewModel reference
    /// that lives in cachedList — mediaUrl / videoUrl / thumbnailUrl /
    /// expireTime have already been mutated when the event fires, so a
    /// listener can simply re-bind to pick up the new URL.
    /// </summary>
    public event Action<MessageViewModel> OnMessageMediaRefreshed;

    /// <summary>
    /// Fires repeatedly during an outgoing media send with whole-pipeline
    /// progress in 0..1 (see SendProgress). tempId matches the optimistic
    /// bubble's MessageViewModel.messageId until the server ack swaps it.
    /// Video bubbles render this as a radial ring; other kinds ignore it.
    /// </summary>
    public event Action<string, float> OnMediaSendProgress;

    /// <summary>
    /// Fires when a single message must be removed from the open transcript
    /// (currently only a cancelled in-flight media send). Carries the bubble's
    /// current messageId (the send tempId). Fills the gap left by there being
    /// no per-message removal — OnLiveMessagesReceived only ever adds.
    /// </summary>
    public event Action<string> OnMessageRemoved;

    public event Action<string> OnActiveBotChanged;
    public event Action<EmptyStateReason> OnEmptyState;
    
    // State
    public int currentPage = 1;
    private string currentChatId;

    /// <summary>
    /// The MessageViewModel list currently powering the open chat's bubbles.
    /// The references in this list are the same ones held by each rendered
    /// MessageItemView.currentVm, so mutating an entry's mediaUrl in place
    /// is observable in the UI. Set by OpenChatRoutine and re-synced by
    /// SyncLatestMessages when new messages merge in. SyncLatestMessages and
    /// GetMessagesRoutine both read it to refresh stale URLs.
    /// </summary>
    private List<MessageViewModel> _activeChatCache;

    /// <summary>
    /// Cached messages that haven't been rendered yet. We only show the newest
    /// MessagesPerPage on chat open; the rest sit here and get drained by
    /// LoadNextPage one batch at a time. Each cache-drain pairs with a parallel
    /// server fetch (ValidateCachePageAgainstServer) so the bubbles render
    /// with freshly-validated URLs instead of whatever the cache file held.
    /// </summary>
    private List<MessageViewModel> _cachedQueue;

    /// <summary>
    /// The in-flight SyncLatestMessages coroutine for the current chat. We
    /// cancel this when a new chat opens (or the same chat re-opens) so a
    /// stale sync's OnLiveMessagesReceived fire can't leak into a different
    /// view, and so two concurrent syncs of the same chat don't both append
    /// the same brand-new messages.
    /// </summary>
    private Coroutine _activeSync;

    /// <summary>
    /// The in-flight OpenChatRoutine. Held so SelectChat can cancel a Prep-phase open
    /// when the user taps another chat before the 300 ms timer elapses.
    /// </summary>
    private Coroutine _activeOpen;

    /// <summary>
    /// First-screen batch staged during Prep, fired via OnBatchMessagesLoaded at the
    /// start of Populate. Null until Prep populates it; reset on SelectChat.
    /// </summary>
    private List<MessageViewModel> _pendingFirstBatch;

    /// <summary>
    /// Brand-new messages from SyncLatestMessages that arrived before Populate began.
    /// Fired via OnLiveMessagesReceived during Populate, after OnBatchMessagesLoaded.
    /// Null when no queued result is waiting.
    /// </summary>
    private List<MessageViewModel> _pendingLiveSyncMessages;

    public void Awake()
    {
        Instance = this;

        // Activate MessageListPanel here so SwipeToBack (which lives on/under it) has
        // its Awake invoked and registers SwipeToBack.Instance BEFORE any chat-open
        // can run. Without this, the very first SelectChat finds SwipeToBack.Instance
        // null and OpenChatRoutine's else branch skips the slide entirely — bubbles
        // appear instantly with no animation. ChatManager.Start's ShowChatList(true)
        // below will slide-out-then-deactivate properly now that SwipeToBack.Instance
        // is wired up. All of this completes before Unity's first frame render, so
        // the user never sees the panel in its activated-pre-slide state.
        if (MessageListPanel != null && !MessageListPanel.activeSelf)
        {
            MessageListPanel.SetActive(true);
        }
    }

    public void Start()
    {
        ShowChatList(true);
        StartCoroutine(InitializeActiveBotNextFrame());
    }

    public ChatViewModel GetChat(string chatId)
    {
        if (chatLookup.TryGetValue(chatId, out var chat))
        {
            return chat;
        }
        return null;
    }
    
    // Notice the new 'isInitialLoad' boolean parameter!
    void ParseChatsJson(string json, bool isInitialLoad)
    {
        if (string.IsNullOrEmpty(json)) return;

        ChatsResponse response = JsonUtility.FromJson<ChatsResponse>(json);
        if (response?.dialogs == null) return;

        // ONLY clear the UI and memory if this is the very first instant load
        if (isInitialLoad)
        {
            Chats.Clear();
            chatLookup.Clear();
            OnChatListCleared?.Invoke(); 
        }

        foreach (var chat in response.dialogs)
        {
            long unixTime = 0;
            if (DateTimeOffset.TryParse(chat.last_timestamp, out var dto)) unixTime = dto.ToUnixTimeSeconds();

            string displayName = string.IsNullOrEmpty(chat.name) ? chat.id[..^5] : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.name);
            string lastMsg = string.IsNullOrEmpty(chat.last_message_data) ? "" : UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.last_message_data);

            if (chatLookup.TryGetValue(chat.id, out var existingVm))
            {
                // --- THE SMART MERGE ---
                // The chat is already on the screen! Do not destroy the prefab!
                // Just quietly update the text, time, and unread count. The UI will catch the event and refresh seamlessly.
                existingVm.UpdateLastMessage(lastMsg, unixTime);
                existingVm.UpdateUnreadCount(chat.unread_count);
                existingVm.UpdateLastMessageId(chat.last_message_id);
                bool mergedIsMine = chat.last_message_sender != null && chat.last_message_sender.isMe;
                existingVm.UpdateLastMessageMeta(chat.last_message_type, chat.last_message_delivery_status, mergedIsMine);
            }
            else
            {
                // This is a brand new chat we haven't seen before, spawn it!
                bool isMine = chat.last_message_sender != null && chat.last_message_sender.isMe;
                var chatVM = new ChatViewModel(chat.id, displayName, chat.thumbnail, lastMsg, unixTime,
                                               unreadCount: chat.unread_count,
                                               lastMessageId: chat.last_message_id,
                                               lastMessageType: chat.last_message_type,
                                               lastMessageDeliveryStatus: chat.last_message_delivery_status,
                                               isLastMessageMine: isMine);
                Chats.Add(chatVM);
                chatLookup[chat.id] = chatVM;
                OnChatAdded?.Invoke(chatVM);
            }
        }
    }

    IEnumerator SyncAllChats(string cachePath, string cachedJson)
    {
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            OnEmptyState?.Invoke(EmptyStateReason.BotHasNoWhatsApp);
            yield break;
        }
        string url = $"https://wappi.pro/api/sync/chats/filter?profile_id={activeProfileId}";

        using UnityWebRequest www = UnityWebRequest.Get(url);
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success) yield break;

        // var text = www.downloadHandler.text;
        // System.IO.File.WriteAllText(
        //     Application.persistentDataPath + "/response.txt",
        //     text
        // );
        // Debug.Log("Saved to: " + Application.persistentDataPath);

        string newJson = www.downloadHandler.text;

        if (newJson != cachedJson)
        {
            System.IO.File.WriteAllTextAsync(cachePath, newJson);
            ParseChatsJson(newJson, false); // FALSE = Background sync, DO NOT CLEAR THE UI!
        }
    }
    
// --- ADDED the bool parameter here! ---
    public void ShowChatList(bool instant = false)
    {
        bool isAlreadySlidingOut = false;
        if (SwipeToBack.Instance != null && SwipeToBack.Instance.chatPanelToSlide != null)
        {
            isAlreadySlidingOut = SwipeToBack.Instance.chatPanelToSlide.anchoredPosition.x > 50f;
        }

        if (SwipeToBack.Instance != null && MessageListPanel.activeSelf && !isAlreadySlidingOut)
        {
            // Pass the parameter to the swipe script!
            SwipeToBack.Instance.SlideOutToChatList(instant);
        }
        else
        {
            MessageListPanel.SetActive(false);
        }

        if (AudioController.Instance != null)
        {
            AudioController.Instance.Stop();
        }
    }

    /// <summary>
    /// Server-reported unread count captured at the instant a chat is opened, BEFORE the
    /// optimistic local zeroing in SelectChat. MessageListView reads this when it builds
    /// bubbles to place the "N unread" separator and seed the scroll-to-bottom badge.
    /// 0 when the chat was already read (or unknown).
    /// </summary>
    public int UnreadOnOpen { get; private set; }

    public void SelectChat(string chatId)
    {
        if (ScrollClickBlocker.IsBlocking) return;

        // Lock out re-taps while the slide-in animation is running. If we allowed a new
        // SelectChat during Slide, the new OpenChatRoutine would later trigger another
        // SlideInToMessages which snaps the panel off-screen — a visible jump while the
        // first slide is still finishing. Slide is brief (~300 ms); the lockout is short.
        if (_phase == ChatOpenPhase.Slide) return;

        float tapTime = Time.realtimeSinceStartup;

        // Cancel any in-flight open. If the user re-tapped during Prep we restart from
        // scratch with the new chat. (Slide-phase re-taps are blocked above.)
        if (_activeOpen != null) StopCoroutine(_activeOpen);
        _pendingFirstBatch = null;
        _pendingLiveSyncMessages = null;

        // Optimistic local reset — match WhatsApp's instant feel. If the next sync returns
        // a non-zero count, the badge re-appears.
        if (chatLookup.TryGetValue(chatId, out var selectedVm))
        {
            UnreadOnOpen = selectedVm.UnreadCount;
            bool hadUnread = selectedVm.UnreadCount > 0;
            selectedVm.UpdateUnreadCount(0);

            if (hadUnread)
            {
                StartCoroutine(MarkChatAsRead(chatId));
            }
        }
        else
        {
            UnreadOnOpen = 0;
        }

        currentChatId = chatId;
        currentPage = 1;
        seenMessageIds.Clear();
        _activeChatCache = null;
        _cachedQueue = null;

        // Fire OnChatSelected so MessageListView clears its bubbles synchronously. Each
        // destroyed bubble's OnDestroy releases its owned Texture2D + Sprite refs — this
        // is the leak fix's enforcement point. MessageListView subscribes to this in
        // Awake (not OnEnable) so the event delivery works even though the panel
        // is currently inactive — SlideInToMessages is the sole activation point.
        OnChatSelected?.Invoke(chatId);

        // Enter Prep. SlideInToMessages (inside OpenChatRoutine, after the 300 ms wait)
        // will re-activate the panel and atomically begin the slide animation.
        _phase = ChatOpenPhase.Prep;
        _activeOpen = StartCoroutine(OpenChatRoutine(chatId, tapTime));
    }

    IEnumerator SyncLatestMessages(string chatId, List<MessageViewModel> cachedList)
    {
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            Debug.LogWarning("[ChatManager] SyncLatestMessages aborted: no valid profile for active bot.");
            yield break;
        }
        string escapedId = UnityWebRequest.EscapeURL(chatId);
        // We strictly only check offset 0 (the absolute newest messages)
        string url = $"https://wappi.pro/api/sync/messages/get?profile_id={activeProfileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset=0";

        using UnityWebRequest www = UnityWebRequest.Get(url);
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success) yield break;

        // If the user has navigated to a different chat while the sync was in
        // flight, abort entirely. Firing OnLiveMessagesReceived or
        // OnMessageStatusChanged now would leak this chat's data into whatever
        // view is currently shown, and mutating shared state (seenMessageIds,
        // _activeChatCache) would corrupt the now-active chat's session. The
        // next open of this chat will run sync again with fresh state.
        if (currentChatId != chatId) yield break;

        // Park sync processing while the chat-open phase has not yet reached Populate
        // (covers Prep, Slide, plus any future intermediate phases). The
        // JsonConvert.DeserializeObject + foreach + CreateViewModel pass below costs
        // ~100-300ms on phone hardware — landing it during Prep or mid-slide would stall
        // the animation by ~5-10 frames. Capped at 500ms so a stuck phase transition
        // can't block sync indefinitely.
        float syncWaitStart = Time.realtimeSinceStartup;
        while ((SwipeToBack.IsSliding
                || (_phase != ChatOpenPhase.Populate && _phase != ChatOpenPhase.Idle))
               && Time.realtimeSinceStartup - syncWaitStart < 0.5f)
        {
            yield return null;
        }

        // Re-check chat-id after the wait — user may have switched chats during the
        // phase we were waiting on.
        if (currentChatId != chatId) yield break;

#if UNITY_EDITOR
        var text = www.downloadHandler.text;
        System.IO.File.WriteAllText(
            Application.persistentDataPath + "/response.txt",
            text
        );
        Debug.Log("Saved to: " + Application.persistentDataPath);
#endif
        
        List<MessageViewModel> newMessages = new List<MessageViewModel>();
        bool hasStatusUpdates = false;

        try
        {
            MessagesResponseRaw response = JsonConvert.DeserializeObject<MessagesResponseRaw>(www.downloadHandler.text);

            if (response?.messages != null)
            {
                foreach (var raw in response.messages)
                {
                    // BRAND NEW message we missed: ghost-send recovery dedup.
                    // If a previous-session POST reached Wappi but the client
                    // never got the response, the outbox holds the tempId AND
                    // the server has the same message under its real id. We
                    // detect that here by matching text + timestamp, then mutate
                    // the cached VM in place (swap id, status, timestamp) and
                    // fire OnMessageStatusChanged so the rendered bubble updates
                    // its tick immediately — no close+reopen required.
                    if (seenMessageIds.Add(raw.id))
                    {
                        NormalizedMessage norm = Normalize(raw);
                        if (norm.messageType == MessageType.Unknown) continue;

                        bool isGhostRecovery = false;

                        if (norm.fromMe)
                        {
                            // Cross-session ghost-recovery: a previous-session send reached
                            // Wappi but the client never saw the ack, so the outbox still holds
                            // the tempId while the server now echoes the same message under its
                            // real id. Match the unresolved outbox entry, then reconcile the
                            // cached bubble in place. Text keys on the raw body; media keys on
                            // attachment kind + timestamp (captions are frequently empty and
                            // can't disambiguate a photo from a video sent seconds apart).
                            string ghostTempId = null;
                            var unresolved = Outbox.GetFor(chatId);

                            if (norm.messageType == MessageType.Chat)
                            {
                                // Compare against the RAW server body, not norm.text. Normalize()
                                // rewrites Unicode emoji into TMP <sprite name="..."> tags via
                                // UnicodeEmojiConverter — the outbox entry's text is the raw user
                                // input and only matches the raw body, not the converted form.
                                string rawBody = raw.body?.ToString();
                                if (!string.IsNullOrEmpty(rawBody))
                                    ghostTempId = BestGhostMatch(unresolved, norm.time, e => e.text == rawBody);
                            }
                            else if (norm.messageType == MessageType.Image ||
                                     norm.messageType == MessageType.Video ||
                                     norm.messageType == MessageType.Document)
                            {
                                ghostTempId = BestGhostMatch(unresolved, norm.time,
                                                             e => MediaGhostMatch.IsKindMatch(e, norm.messageType));
                            }

                            if (!string.IsNullOrEmpty(ghostTempId))
                            {
                                isGhostRecovery = ReconcileGhostSend(ghostTempId, raw, norm, cachedList, chatId);
                                if (isGhostRecovery) hasStatusUpdates = true;
                            }
                        }

                        // If we already absorbed this server message into a recovered
                        // cached VM, don't also append a duplicate to newMessages.
                        if (isGhostRecovery) continue;

                        newMessages.Add(CreateViewModel(norm));
                        continue;
                    }

                    // Already-cached message: refresh stale media URLs and
                    // delivery_status. Wappi can return s3Info: null on first
                    // arrival and populate it later, or — seen in production —
                    // hand out an s3 URL whose file path points at another
                    // message's bytes. Without this pass the cached entry
                    // stays wrong forever and the on-disk media cache (keyed
                    // by URL MD5) serves the wrong file on every reload.
                    if (RefreshCachedMessageMedia(Normalize(raw), cachedList))
                    {
                        hasStatusUpdates = true;
                    }

                    // Server may also have a fresher delivery_status for an
                    // outgoing message. Apply the same Sent-fallback as
                    // Normalize so cached None entries from self-chat sends
                    // get migrated on the next sync.
                    if (!raw.fromMe) continue;

                    DeliveryStatus parsedRaw = DeliveryTickFormatter.ParseWappiString(raw.deliveryStatusRaw);
                    DeliveryStatus serverStatus = (parsedRaw == DeliveryStatus.None) ? DeliveryStatus.Sent : parsedRaw;

                    for (int i = 0; i < cachedList.Count; i++)
                    {
                        if (cachedList[i].messageId == raw.id && cachedList[i].deliveryStatus != serverStatus)
                        {
                            cachedList[i].deliveryStatus = serverStatus;
                            hasStatusUpdates = true;
                            OnMessageStatusChanged?.Invoke(raw.id, raw.id, serverStatus);
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Sync JSON Parse Error: {e.Message}");
        }

        // If we found new messages while the app was closed, merge them!
        if (newMessages.Count > 0)
        {
            // Snapshot the brand-new server messages before merging — these are
            // the ids the rendered list hasn't seen yet. The merged list below
            // goes to the cache file and pagination tracker, but the UI must
            // only be told about the brand-new ones. Firing OnBatchMessagesLoaded
            // with the full merged list re-spawns the already-rendered cached
            // bubbles on top of themselves (HandleBatchMessages does not Clear
            // outside OnChatSelected), producing visible duplicates.
            var brandNew = new List<MessageViewModel>(newMessages);

            // Add the old cached messages to the bottom of the brand new ones
            newMessages.AddRange(cachedList);

            // Keep the cache file size healthy (max 100 messages)
            if (newMessages.Count > 100) newMessages = newMessages.GetRange(0, 100);

            // Re-register everything so LoadNextPage() works perfectly later
            seenMessageIds.Clear();
            foreach (var m in newMessages) seenMessageIds.Add(m.messageId);

            // Save the newly merged list permanently
            ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, newMessages);

            // Track the merged list for future paginated refreshes.
            _activeChatCache = newMessages;

            // Brand-new messages: queue if we're not in a settled state (Prep or Slide
            // would spawn into an empty/closing list; IsSliding covers both slide-in
            // and slide-out drag/snap). Otherwise fire immediately. Queued messages
            // are either drained by PopulateBubbles on next open of the same chat, or
            // dropped if the user navigates to a different chat (the cache has them).
            bool isSettled = (_phase == ChatOpenPhase.Populate || _phase == ChatOpenPhase.Idle)
                             && !SwipeToBack.IsSliding;
            if (isSettled)
            {
                OnLiveMessagesReceived?.Invoke(brandNew);
            }
            else
            {
                if (_pendingLiveSyncMessages == null) _pendingLiveSyncMessages = new List<MessageViewModel>();
                _pendingLiveSyncMessages.AddRange(brandNew);
            }
        }
        else if (hasStatusUpdates)
        {
            // Status-only updates: save the mutated cachedList to disk. No
            // OnBatchMessagesLoaded fire needed — OnMessageStatusChanged
            // already refreshed any visible bubbles in place.
            ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, cachedList);
        }
    }

    /// <summary>
    /// Shared swap/remove/fire tail for cross-session ghost-recovery (text + media).
    /// Swaps the cached optimistic bubble's tempId → the server's real id, adopts the
    /// server delivery status + timestamp, removes the resolved outbox entry, clears the
    /// tempId from seenMessageIds, and fires OnMessageStatusChanged so the rendered bubble
    /// re-renders its tick in place. Returns true iff a cached bubble was found and mutated
    /// (caller then skips appending a duplicate to newMessages). RemoveAt + the seenMessageIds
    /// clear run even when no cached bubble is found, so a stale outbox entry for an
    /// already-evicted bubble (>100-msg cap) is still cleaned up.
    /// </summary>
    private bool ReconcileGhostSend(string ghostTempId, RawMessage raw, NormalizedMessage norm,
                                    List<MessageViewModel> cachedList, string chatId)
    {
        bool found = false;
        for (int j = 0; j < cachedList.Count; j++)
        {
            if (cachedList[j].messageId == ghostTempId)
            {
                cachedList[j].messageId      = raw.id;
                cachedList[j].deliveryStatus = norm.deliveryStatus;
                cachedList[j].timestamp      = norm.time;
                found = true;
                break;
            }
        }

        Outbox.RemoveAt(GetCacheRoot(), chatId, ghostTempId);
        seenMessageIds.Remove(ghostTempId);

        if (found) OnMessageStatusChanged?.Invoke(ghostTempId, raw.id, norm.deliveryStatus);
        return found;
    }

    /// <summary>
    /// Finds the unresolved outbox entry that best matches a server message: the smallest
    /// |entry.timestamp - serverTime| within ±120s among entries the predicate accepts.
    /// Returns the winning tempId, or null if none match. Shared by the text matcher
    /// (predicate = raw-body equality) and the media matcher (predicate = MediaGhostMatch.IsKindMatch).
    /// </summary>
    private static string BestGhostMatch(IReadOnlyList<OutboxStore.OutboxEntry> unresolved,
                                         long serverTime, Func<OutboxStore.OutboxEntry, bool> predicate)
    {
        int bestIndex = -1;
        long bestDelta = long.MaxValue;
        for (int i = 0; i < unresolved.Count; i++)
        {
            if (!predicate(unresolved[i])) continue;
            long delta = Math.Abs(unresolved[i].timestamp - serverTime);
            if (delta > 120) continue;
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestIndex = i;
            }
        }
        return bestIndex >= 0 ? unresolved[bestIndex].tempId : null;
    }

    /// <summary>
    /// Phase A (Prep) of chat-open. Runs cache load + sort + first-screen split synchronously
    /// inside the coroutine, kicks off sync (whose results buffer into _pendingLiveSyncMessages),
    /// then waits until 300 ms has elapsed from tap time before triggering the slide-in animation.
    /// On slide-in completion the callback transitions to Phase C (Populate).
    /// </summary>
    private IEnumerator OpenChatRoutine(string chatId, float tapTime)
    {
        const float PrepDurationSeconds = 0.300f;

        // On device only — releasing orphaned natives can take 30-80 ms but Prep has the
        // budget. Editor skips this; the cost shows up as iteration friction in play mode.
        if (!Application.isEditor)
        {
            Resources.UnloadUnusedAssets();
        }

        List<MessageViewModel> cachedMessages = ChatHistoryCache.LoadHistory(GetCacheRoot(), chatId);

        // Always load the outbox — populates OutboxStore's in-memory _byChatId map so
        // tap-to-retry's Find() can resolve the tempId, even if the message cache was
        // purged but the outbox file survived.
        var unresolved = Outbox.GetFor(chatId);

        if (cachedMessages != null && cachedMessages.Count > 0)
        {
            // Promote stale-Pending cached messages to Failed for any tempId still in
            // the outbox. An unresolved entry means the in-flight POST from a previous
            // session never completed — without this pass the user would see a phantom
            // clock that never resolves.
            if (unresolved.Count > 0)
            {
                var unresolvedIds = new HashSet<string>();
                foreach (var entry in unresolved) unresolvedIds.Add(entry.tempId);

                foreach (var msg in cachedMessages)
                {
                    if (!msg.isIncoming && unresolvedIds.Contains(msg.messageId))
                        msg.deliveryStatus = DeliveryStatus.Failed;
                }
            }

            cachedMessages.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));
            foreach (var msg in cachedMessages) seenMessageIds.Add(msg.messageId);
            _activeChatCache = cachedMessages;

            int initialCount = FirstScreenBudget.MessageCount(cachedMessages);
            if (cachedMessages.Count > initialCount)
            {
                _pendingFirstBatch = cachedMessages.GetRange(0, initialCount);
                _cachedQueue = cachedMessages.GetRange(initialCount, cachedMessages.Count - initialCount);
            }
            else
            {
                _pendingFirstBatch = cachedMessages;
                _cachedQueue = new List<MessageViewModel>();
            }

            // Kick off sync. Its callback fires OnLiveMessagesReceived only after Populate
            // begins (gated by Phase != Populate inside SyncLatestMessages).
            if (_activeSync != null) StopCoroutine(_activeSync);
            _activeSync = StartCoroutine(SyncLatestMessages(chatId, cachedMessages));
        }
        else
        {
            // No cache: kick the network fetch. Its callback writes _pendingFirstBatch
            // and _cachedQueue if the response arrives before slide-in completes; otherwise
            // the slide reveals an empty content and the bubbles land in Populate.
            StartCoroutine(GetMessagesRoutine(chatId, 1, (newMessages, hasMore) =>
            {
                if (chatId != currentChatId) return; // stale fetch — user switched chats

                if (newMessages.Count > 0)
                    ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, newMessages);

                _activeChatCache = newMessages;

                newMessages.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));

                int initialCount = FirstScreenBudget.MessageCount(newMessages);
                if (newMessages.Count > initialCount)
                {
                    _pendingFirstBatch = newMessages.GetRange(0, initialCount);
                    _cachedQueue = newMessages.GetRange(initialCount, newMessages.Count - initialCount);
                }
                else
                {
                    _pendingFirstBatch = newMessages;
                    _cachedQueue = new List<MessageViewModel>();
                }

                // Fire immediately if the open has already settled — and this MUST accept Idle
                // as well as Populate. PopulateBubbles advances _phase Populate->Idle
                // synchronously within a single call (no yield between lines 758 and 776), so by
                // the time this async network callback resumes the phase is Idle, never Populate.
                // Checking only ==Populate made this branch dead code: a slow fetch on an
                // uncached chat staged _pendingFirstBatch but never fired it, leaving the chat
                // blank until the next open rendered it from the cache saved above. Mirrors the
                // isSettled gate in SyncLatestMessages.
                if (_phase == ChatOpenPhase.Populate || _phase == ChatOpenPhase.Idle)
                {
                    OnBatchMessagesLoaded?.Invoke(_pendingFirstBatch, false, hasMore);
                    _pendingFirstBatch = null;
                }
            }));
        }

        // Wait until 300 ms has elapsed since the tap. If Prep finished early, this is
        // intentional lead-in so the slide doesn't start before the user's eye has had
        // time to register the row tap.
        while (Time.realtimeSinceStartup - tapTime < PrepDurationSeconds)
        {
            yield return null;
        }

        _phase = ChatOpenPhase.Slide;

        if (SwipeToBack.Instance != null)
        {
            SwipeToBack.Instance.SlideInToMessages(() =>
            {
                PopulateBubbles(chatId);
            });
        }
        else
        {
            // No SwipeToBack instance (shouldn't happen in production but safe fallback):
            // skip the animation and go straight to Populate.
            MessageListPanel.SetActive(true);
            PopulateBubbles(chatId);
        }
    }

    /// <summary>
    /// Phase C (Populate). Fires OnBatchMessagesLoaded with the staged first batch,
    /// then drains any sync results that landed during Prep or Slide.
    /// </summary>
    private void PopulateBubbles(string chatId)
    {
        if (currentChatId != chatId)
        {
            // User switched chats during the slide. SelectChat already reset state for
            // the new chat — bail out cleanly.
            return;
        }

        _phase = ChatOpenPhase.Populate;

        if (_pendingFirstBatch != null)
        {
            OnBatchMessagesLoaded?.Invoke(_pendingFirstBatch, false, true);
            _pendingFirstBatch = null;
        }

        if (_pendingLiveSyncMessages != null && _pendingLiveSyncMessages.Count > 0)
        {
            OnLiveMessagesReceived?.Invoke(_pendingLiveSyncMessages);
            _pendingLiveSyncMessages = null;
        }

        // Transition to Idle (settled state). Downstream consumers (SyncLatestMessages,
        // AcquireDecodeSlot) treat Idle and Populate identically, so this is a doc-accuracy
        // fix not a behavior change: the enum's doc says "Idle is the steady state (chat
        // list visible, or chat fully open and settled)" and we've now actually settled.
        _phase = ChatOpenPhase.Idle;
    }

    public void LoadNextPage()
    {
        if (string.IsNullOrEmpty(currentChatId)) return;

        currentPage++;

        // Drain the cache queue first — those bubbles render instantly and
        // we kick off a parallel server fetch to validate (and overwrite
        // via OnMessageMediaRefreshed) any stale URLs in the batch. Only
        // after the queue is empty do we go to the server for genuinely
        // older history.
        if (_cachedQueue != null && _cachedQueue.Count > 0)
        {
            int take = Math.Min(MessagesPerPage, _cachedQueue.Count);
            var batch = _cachedQueue.GetRange(0, take);
            _cachedQueue.RemoveRange(0, take);

            // hasMore stays armed while either the queue still has entries
            // or the server might have older messages we haven't touched.
            bool moreToCome = _cachedQueue.Count > 0 || true;
            OnBatchMessagesLoaded?.Invoke(batch, true, moreToCome);

            // Parallel URL validation against the matching server page.
            StartCoroutine(ValidateCachePageAgainstServer(currentChatId, currentPage));
            return;
        }

        StartCoroutine(GetMessagesRoutine(currentChatId, currentPage, (messages, hasMore) =>
        {
            OnBatchMessagesLoaded?.Invoke(messages, true, hasMore);
        }));
    }

    /// <summary>
    /// Background URL-validation pass for a cache-served page. Fetches the
    /// matching server page and feeds every returned message through
    /// RefreshCachedMessageMedia, which patches any cached entry whose URL
    /// path has shifted on the server. Saves the cache file if anything
    /// changed. Unlike GetMessagesRoutine this never fires
    /// OnBatchMessagesLoaded — the bubbles for these messages have already
    /// been rendered from cache, and the only thing we want from the server
    /// is fresh URLs, not extra entries.
    /// </summary>
    private IEnumerator ValidateCachePageAgainstServer(string chatId, int page)
    {
        int offset = (page - 1) * MessagesPerPage;
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId)) yield break;

        string escapedId = UnityWebRequest.EscapeURL(chatId);
        string url = $"https://wappi.pro/api/sync/messages/get?profile_id={activeProfileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset={offset}";

        using UnityWebRequest www = UnityWebRequest.Get(url);
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success) yield break;

        bool cacheDirty = false;
        try
        {
            MessagesResponseRaw response = JsonConvert.DeserializeObject<MessagesResponseRaw>(www.downloadHandler.text);
            if (response?.messages != null)
            {
                foreach (var raw in response.messages)
                {
                    if (string.IsNullOrEmpty(raw.id)) continue;
                    if (RefreshCachedMessageMedia(Normalize(raw), _activeChatCache))
                    {
                        cacheDirty = true;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ValidateCachePage JSON Parse Error: {e.Message}");
        }

        if (cacheDirty && _activeChatCache != null)
        {
            ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, _activeChatCache);
        }
    }

// Notice the Action signature now includes a bool!
    IEnumerator GetMessagesRoutine(string chatId, int page, Action<List<MessageViewModel>, bool> onComplete)
    {
        int offset = (page - 1) * MessagesPerPage;

        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            onComplete?.Invoke(new List<MessageViewModel>(), false);
            yield break;
        }
        string escapedId = UnityWebRequest.EscapeURL(chatId);
        string url = $"https://wappi.pro/api/sync/messages/get?profile_id={activeProfileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset={offset}";

        using UnityWebRequest www = UnityWebRequest.Get(url);
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;
        yield return www.SendWebRequest();

#if UNITY_EDITOR
        var text = www.downloadHandler.text;
        System.IO.File.WriteAllText(
            Application.persistentDataPath + "/response.txt",
            text
        );
        Debug.Log("Saved to: " + Application.persistentDataPath);
#endif

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[ChatManager] Error loading messages: {www.error}");
            onComplete?.Invoke(new List<MessageViewModel>(), false); 
            yield break;
        }
        
        List<MessageViewModel> loadedMessages = new List<MessageViewModel>();
        int rawServerCount = 0; // --- THE FIX: Track the raw count before filtering! ---
        bool cacheDirty = false;

        try
        {
            MessagesResponseRaw response = JsonConvert.DeserializeObject<MessagesResponseRaw>(www.downloadHandler.text);

            if (response?.messages != null)
            {
                foreach (var raw in response.messages)
                {
                    rawServerCount++; // Count every single message the server gave us

                    if (string.IsNullOrEmpty(raw.id)) continue;

                    if (!seenMessageIds.Add(raw.id))
                    {
                        // Already in cache: trust the server's fresh URLs over
                        // whatever the cache still holds. Mainly catches the
                        // case where an older message in _activeChatCache has
                        // a stale or wrong s3 URL from a prior sync — once the
                        // user paginates past it, we patch the cache so the
                        // next render uses the right file.
                        if (RefreshCachedMessageMedia(Normalize(raw), _activeChatCache))
                        {
                            cacheDirty = true;
                        }
                        continue;
                    }

                    NormalizedMessage norm = Normalize(raw);
                    if (norm.messageType == MessageType.Unknown) continue;

                    MessageViewModel vm = CreateViewModel(norm);
                    loadedMessages.Add(vm);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"JSON Parse Error: {e.Message}");
        }

        if (cacheDirty && _activeChatCache != null)
        {
            ChatHistoryCache.SaveHistory(GetCacheRoot(), chatId, _activeChatCache);
        }

        bool hasMore = rawServerCount >= MessagesPerPage;

        // --- THE GHOST PAGE FIX ---
        // If the server gave us 50 messages, but we filtered ALL of them out,
        // we must silently fetch the next page so the UI doesn't freeze!
        if (loadedMessages.Count == 0 && hasMore)
        {
            currentPage++;
            StartCoroutine(GetMessagesRoutine(chatId, currentPage, onComplete));
            yield break;
        }

        onComplete?.Invoke(loadedMessages, hasMore);
    }

    MessageViewModel CreateViewModel(NormalizedMessage msg)
    {
        var vm = new MessageViewModel
        {
            messageId = msg.id,
            chatId = msg.chatId,
            senderName = msg.senderName,
            type = msg.messageType,
            text = msg.text,
            fileName = msg.fileName, // <--- Add this line!
            mediaUrl = msg.mediaUrl,
            thumbnailUrl = msg.thumbnailUrl,
            aspectRatio = msg.aspectRatio,
            expireTime = msg.expireTime,
            mimeType = msg.mimeType,
            videoUrl = msg.videoUrl,
            duration = msg.duration,
            isSticker = msg.isSticker,
            isIncoming = !msg.fromMe,
            timestamp = msg.time,
            fileSize = msg.fileSize,
            pageCount = msg.pageCount,
            deliveryStatus = msg.deliveryStatus
        };

        // Proactively extract a native thumbnail for videos (replaces the server
        // JPEGThumbnail when ready). No-op off-iOS / already-cached / urlless.
        if (vm.type == MessageType.Video) EnqueueIncomingVideoThumb(vm);

        return vm;
    }

    NormalizedMessage Normalize(RawMessage raw)
    {
        NormalizedMessage msg = new NormalizedMessage
        {
            id = raw.id,
            chatId = raw.chatId,
            senderName = raw.senderName,
            messageType = ParseMessageType(raw.type),
            fromMe = raw.fromMe,
            time = raw.time
        };

        // Outgoing messages carry a Wappi delivery_status string. Incoming
        // messages never render a tick — leave at DeliveryStatus.None.
        // Wappi sometimes omits the field entirely (self-chat sends, very
        // fresh messages awaiting delivery ack, etc.). Fall back to Sent
        // for fromMe + empty/unknown — the message is in messages/get so
        // Wappi has at least received it.
        if (raw.fromMe)
        {
            DeliveryStatus parsed = DeliveryTickFormatter.ParseWappiString(raw.deliveryStatusRaw);
            msg.deliveryStatus = (parsed == DeliveryStatus.None) ? DeliveryStatus.Sent : parsed;
        }

        // --- 1. SEPARATE TEXT FROM CAPTIONS ---
        if (msg.messageType == MessageType.Chat)
        {
            msg.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(raw.body?.ToString());
        }
        else 
        {
            // Extract hidden captions for media objects!
            string captionStr = raw.caption?.ToString();
            if (string.IsNullOrEmpty(captionStr) && raw.body is JObject bodyObj && bodyObj["caption"] != null)
            {
                captionStr = bodyObj["caption"].ToString();
            }

            if (!string.IsNullOrEmpty(captionStr))
            {
                msg.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(captionStr);
            }
        }

        JToken dimSource = raw.mediaInfo ?? raw.body;
        if (dimSource is JObject dObj)
        {
            float width = 0, height = 0;
            if (dObj["width"] != null) float.TryParse(dObj["width"].ToString(), out width);
            if (dObj["height"] != null) float.TryParse(dObj["height"].ToString(), out height);
        
            if (width > 0 && height > 0) 
                msg.aspectRatio = width / height;
            else
                msg.aspectRatio = 1.0f; 
        }
        else 
        {
            msg.aspectRatio = 1.0f; 
        }
        
if (msg.messageType == MessageType.Video)
        {
            // 1. ALWAYS grab the thumbnail first!
            if (raw.body is JObject bodyObj && bodyObj["JPEGThumbnail"] != null)
            {
                // Wappi's video JPEGThumbnail arrives in the same inconsistent base64
                // shapes as the image one (data-URI prefix, whitespace, URL-safe, no
                // padding). StageServerThumbnail sanitizes + verifies the cache write so
                // a payload we can't decode leaves thumbnailUrl empty (loading/black
                // placeholder) instead of pointing at a file that never got written.
                msg.thumbnailUrl = StageServerThumbnail(msg.id, bodyObj["JPEGThumbnail"].ToString());
            }

            // 2. THEN grab the S3 URL
            if (raw.s3Info is JObject s3 && s3["url"] != null)
            {
                msg.videoUrl = s3["url"].ToString();
                if (s3["expire"] != null) long.TryParse(s3["expire"].ToString(), out msg.expireTime);
            }
        }
        else if (msg.messageType == MessageType.Image || msg.messageType == MessageType.Sticker)
        {
            if (msg.messageType == MessageType.Sticker) 
            {
                msg.isSticker = true;
                if (msg.aspectRatio <= 0) msg.aspectRatio = 1.0f;
            }

            // 1. ALWAYS grab the thumbnail first!
            if (raw.body is JObject bodyObj)
            {
                if (bodyObj["JPEGThumbnail"] != null)
                {
                    msg.thumbnailUrl = StageServerThumbnail(msg.id, bodyObj["JPEGThumbnail"].ToString());
                }

                if (bodyObj["url"] != null) msg.mediaUrl = bodyObj["url"].ToString();
            }

            // 2. THEN grab the best HD S3 URL to overwrite the fallback
            if (raw.s3Info is JObject s3 && s3["url"] != null)
            {
                msg.mediaUrl = s3["url"].ToString();
                if (s3["expire"] != null) long.TryParse(s3["expire"].ToString(), out msg.expireTime);
            }
        }
        else if (msg.messageType == MessageType.Audio || msg.messageType == MessageType.Voice)
        {
             if (raw.s3Info is JObject s3 && s3["url"] != null)
             {
                 msg.mediaUrl = s3["url"].ToString(); 
                 if (s3["expire"] != null) long.TryParse(s3["expire"].ToString(), out msg.expireTime);
             }
             
             if (raw.mediaInfo is JObject info && info["duration"] != null)
                 int.TryParse(info["duration"].ToString(), out msg.duration);
        }
        else if (msg.messageType == MessageType.Document)
        {
            if (raw.body is JObject bodyObj)
            {
                // THE FIX: Save to fileName! Leave msg.text alone so the caption survives!
                string fName = bodyObj["fileName"]?.ToString() ?? bodyObj["title"]?.ToString();
                msg.fileName = fName;

                msg.mimeType = bodyObj["mimetype"]?.ToString();

                // --- ADDED: Extract the file length and page count right here! ---
                if (bodyObj["fileLength"] != null)
                {
                    long.TryParse(bodyObj["fileLength"].ToString(), out msg.fileSize);
                }

                if (bodyObj["pageCount"] != null)
                {
                    int.TryParse(bodyObj["pageCount"].ToString(), out msg.pageCount);
                }

                // Wappi sometimes echoes fileName into the caption field. Treat caption == fileName
                // as "no caption" so it doesn't render as a chat-text line under the document card.
                // ConvertRealEmojisToSprites prepends ​ (zero-width space) — plain Trim()
                // doesn't strip it, so compare with ZWS-aware trim chars.
                if (!string.IsNullOrEmpty(msg.text) && !string.IsNullOrEmpty(fName))
                {
                    char[] trimChars = { '​', ' ', '\t', '\n', '\r' };
                    string normalizedText = msg.text.Trim(trimChars);
                    string normalizedName = fName.Trim(trimChars);
                    if (string.Equals(normalizedText, normalizedName, StringComparison.OrdinalIgnoreCase))
                    {
                        msg.text = null;
                    }
                }
            }

            if (raw.s3Info is JObject s3 && s3["url"] != null)
            {
                msg.mediaUrl = s3["url"].ToString();
                if (s3["expire"] != null) long.TryParse(s3["expire"].ToString(), out msg.expireTime);
            }
        }

        return msg;
    }

    /// <summary>
    /// Decodes a server <c>JPEGThumbnail</c> base64 payload, stages it in the media
    /// cache under <c>thumb://{id}</c>, and returns that key — or "" when the payload
    /// is missing, undecodable, or failed to persist. Returning "" (rather than a key
    /// that points at a non-existent / unwritten file) lets the bubble fall back to its
    /// loading/black state and, for videos, to the native HD frame instead of rendering
    /// a permanent black card. Shared by the image and video branches of Normalize.
    /// </summary>
    private static string StageServerThumbnail(string id, string rawBase64)
    {
        if (string.IsNullOrEmpty(id)) return "";
        if (!JpegThumbnailDecoder.TryDecodeBase64(rawBase64, out byte[] bytes)) return "";
        if (MediaCacheManager.Instance == null) return "";

        string thumbUrl = "thumb://" + id;
        MediaCacheManager.Instance.SaveImageToCache(thumbUrl, bytes);
        // SaveImageToCache silently no-ops on empty data; only claim the thumbnail
        // once the file is actually on disk so ShowSmartThumbnail can load it.
        return MediaCacheManager.Instance.IsImageCached(thumbUrl) ? thumbUrl : "";
    }

    MessageType ParseMessageType(string type)
    {
        return type switch
        {
            "chat" => MessageType.Chat,
            "image" => MessageType.Image,
            "video" => MessageType.Video,
            "audio" => MessageType.Audio,
            "ptt" => MessageType.Voice,
            "sticker" => MessageType.Sticker,
            "document" => MessageType.Document,
            _ => MessageType.Unknown
        };
    }

    static bool IsMediaMessageType(MessageType type) =>
        type == MessageType.Image
        || type == MessageType.Video
        || type == MessageType.Audio
        || type == MessageType.Voice
        || type == MessageType.Sticker
        || type == MessageType.Document;

    /// <summary>
    /// If `refreshed` matches an entry in `cachedList`, copy any
    /// newly-available media URLs onto the cached entry in place. Used by
    /// both SyncLatestMessages (latest-50 window) and GetMessagesRoutine
    /// (paginated older pages) — anywhere we re-encounter an already-cached
    /// message, we trust the server's fresh URL over whatever the cache
    /// holds. Returns true if any field changed (caller should mark its
    /// cache dirty). Fires OnMessageMediaRefreshed so rendered bubbles
    /// re-bind under the new URL.
    /// </summary>
    private bool RefreshCachedMessageMedia(NormalizedMessage refreshed, List<MessageViewModel> cachedList)
    {
        if (refreshed == null || cachedList == null) return false;
        if (refreshed.messageType == MessageType.Unknown) return false;
        if (!IsMediaMessageType(refreshed.messageType)) return false;

        for (int i = 0; i < cachedList.Count; i++)
        {
            if (cachedList[i].messageId != refreshed.id) continue;

            var cached = cachedList[i];
            bool mediaRefreshed = false;

            if (!string.IsNullOrEmpty(refreshed.mediaUrl) && UrlPathDiffers(refreshed.mediaUrl, cached.mediaUrl))
            {
                cached.mediaUrl = refreshed.mediaUrl;
                cached.expireTime = refreshed.expireTime;
                mediaRefreshed = true;
            }
            if (!string.IsNullOrEmpty(refreshed.videoUrl) && UrlPathDiffers(refreshed.videoUrl, cached.videoUrl))
            {
                cached.videoUrl = refreshed.videoUrl;
                cached.expireTime = refreshed.expireTime;
                mediaRefreshed = true;
            }
            if (!string.IsNullOrEmpty(refreshed.thumbnailUrl) && string.IsNullOrEmpty(cached.thumbnailUrl))
            {
                cached.thumbnailUrl = refreshed.thumbnailUrl;
                mediaRefreshed = true;
            }

            // Cached videos bypass CreateViewModel (first screen + scrolled history are
            // served straight from ChatHistoryCache), so enqueue native thumbnail
            // extraction here too. Covers incoming and outgoing; dedup via the vthumb
            // cache + queue makes the repeated calls during sync cheap.
            if (cached.type == MessageType.Video) EnqueueIncomingVideoThumb(cached);

            if (mediaRefreshed) OnMessageMediaRefreshed?.Invoke(cached);
            return mediaRefreshed;
        }
        return false;
    }

    /// <summary>
    /// Compare two URLs by file path only (ignoring query string). S3
    /// signed URLs get a fresh X-Amz-Signature on every fetch even when
    /// the underlying file is unchanged — those refreshes are harmless to
    /// skip in cache, the rendering layer falls back to Wappi's
    /// /media/download endpoint when the local expireTime is past. We only
    /// care when the FILE behind the URL changes (different uuid path).
    /// Treats empty/non-empty as differing so a server URL can fill a hole.
    /// </summary>
    static bool UrlPathDiffers(string a, string b)
    {
        bool aEmpty = string.IsNullOrEmpty(a);
        bool bEmpty = string.IsNullOrEmpty(b);
        if (aEmpty && bEmpty) return false;
        if (aEmpty != bEmpty) return true;

        int ai = a.IndexOf('?');
        int bi = b.IndexOf('?');
        string aPath = ai >= 0 ? a.Substring(0, ai) : a;
        string bPath = bi >= 0 ? b.Substring(0, bi) : b;
        return !string.Equals(aPath, bPath, StringComparison.Ordinal);
    }

    public void DownloadMediaForMessage(string messageId, Action<string> onSuccess, Action onFailure)
    {
        StartCoroutine(DownloadMediaRoutine(messageId, onSuccess, onFailure));
    }

    IEnumerator DownloadMediaRoutine(string messageId, Action<string> onSuccess, Action onFailure)
    {
        string activeProfileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(activeProfileId))
        {
            onFailure?.Invoke();
            yield break;
        }
        string url = $"https://wappi.pro/api/sync/message/media/download?profile_id={activeProfileId}&message_id={messageId}";
        using UnityWebRequest www = UnityWebRequest.Get(url);
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            // if (www.responseCode == 400) Debug.LogWarning($"[ChatManager] Media expired: {messageId}");
            onFailure?.Invoke();
            yield break;
        }
        
        try
        {
            JObject json = JObject.Parse(www.downloadHandler.text);
            string fileLink = json["file_link"]?.ToString();
            string fileB64 = json["file_b64"]?.ToString();

            if (!string.IsNullOrEmpty(fileLink)) onSuccess?.Invoke(fileLink);
            else if (!string.IsNullOrEmpty(fileB64)) onSuccess?.Invoke("base64://" + fileB64);
            else onFailure?.Invoke();
        }
        catch { onFailure?.Invoke(); }
    }
    
    public void SendTextMessage(string text)
    {
        // Don't send empty blanks or if no chat is selected
        if (string.IsNullOrEmpty(currentChatId) || string.IsNullOrWhiteSpace(text)) return;

        // Run on Manager.Instance so SetActiveBot's StopAllCoroutines on this
        // object can't strand the optimistic message with a temp id when the
        // user switches bots mid-send. Falls back to this if Manager isn't ready.
        MonoBehaviour runner = Manager.Instance != null ? (MonoBehaviour)Manager.Instance : this;
        runner.StartCoroutine(SendTextMessageRoutine(currentChatId, text));
    }

IEnumerator SendTextMessageRoutine(string chatId, string text)
{
    string activeProfileId = GetActiveProfileId();
    if (string.IsNullOrEmpty(activeProfileId))
    {
        Debug.LogWarning("[ChatManager] SendTextMessageRoutine aborted: no valid profile for active bot.");
        yield break;
    }

    // Snapshot the originating bot's cache root so the temp-id swap below always
    // lands in the bot the message was sent on, even if the user switches bots
    // while the request is in flight.
    string sendCacheRoot = GetCacheRoot();

    // --- INSTANT UI: Fire before ANY network call ---
    string tempId = "sending_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    seenMessageIds.Add(tempId);

    var instantMessage = new MessageViewModel
    {
        messageId = tempId,
        chatId = chatId,
        senderName = "Me",
        type = MessageType.Chat,
        text = text,
        isIncoming = false,
        timestamp = now,
        deliveryStatus = DeliveryStatus.Pending
    };

    OnLiveMessagesReceived?.Invoke(new List<MessageViewModel> { instantMessage });

    var chatVm = GetChat(chatId);
    if (chatVm != null) chatVm.UpdateLastMessage(text, now);

    List<MessageViewModel> cachedList = ChatHistoryCache.LoadHistory(sendCacheRoot, chatId);
    cachedList.Add(instantMessage);
    ChatHistoryCache.SaveHistory(sendCacheRoot, chatId, cachedList);

    Outbox.Add(new OutboxStore.OutboxEntry
    {
        tempId       = tempId,
        chatId       = chatId,
        text         = text,
        timestamp    = now,
        attemptCount = 1,
        profileId    = activeProfileId
    });

    // --- BACKGROUND: Send to server silently ---
    yield return PostTextMessageRoutine(chatId, text, tempId, activeProfileId, sendCacheRoot);
}

/// <summary>
/// Network half of an outgoing text send. Shared between the initial
/// optimistic send (SendTextMessageRoutine) and tap-to-retry
/// (RetryOutboxMessage). Fires OnMessageStatusChanged on both success
/// and failure paths; does NOT touch the outbox itself — callers own
/// outbox lifecycle.
/// </summary>
private IEnumerator PostTextMessageRoutine(
    string chatId,
    string text,
    string tempId,
    string profileId,
    string sendCacheRoot)
{
    string recipient = chatId.EndsWith("@c.us") ? chatId.Replace("@c.us", "") : chatId;
    string url = $"https://wappi.pro/api/sync/message/send?profile_id={profileId}";

    var requestData = new WappiSendTextRequest { body = text, recipient = recipient };
    string jsonPayload = JsonConvert.SerializeObject(requestData);

    using UnityWebRequest www = new UnityWebRequest(url, "POST");
    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
    www.uploadHandler = new UploadHandlerRaw(bodyRaw);
    www.downloadHandler = new DownloadHandlerBuffer();
    www.SetRequestHeader("Content-Type", "application/json");
    www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
    www.timeout = 30;

    yield return www.SendWebRequest();

    if (www.result != UnityWebRequest.Result.Success)
    {
        Debug.LogError($"[Wappi] message/send failed: {www.error}\n{www.downloadHandler?.text}");
        OnMessageStatusChanged?.Invoke(tempId, tempId, DeliveryStatus.Failed);
        yield break;
    }

    WappiSendTextResponse response = null;
    try
    {
        response = JsonConvert.DeserializeObject<WappiSendTextResponse>(www.downloadHandler.text);
    }
    catch (Exception ex)
    {
        Debug.LogError($"[Wappi] message/send response parse failed: {ex.Message}\n{www.downloadHandler.text}");
        OnMessageStatusChanged?.Invoke(tempId, tempId, DeliveryStatus.Failed);
        yield break;
    }

    if (response != null && response.status == "done" && !string.IsNullOrEmpty(response.message_id))
    {
        seenMessageIds.Remove(tempId);
        seenMessageIds.Add(response.message_id);

        // Update cached optimistic message so a chat reopen picks up the
        // real id and Sent status instead of a stranded tempId / Pending.
        List<MessageViewModel> cachedList = ChatHistoryCache.LoadHistory(sendCacheRoot, chatId);
        for (int i = 0; i < cachedList.Count; i++)
        {
            if (cachedList[i].messageId == tempId)
            {
                cachedList[i].messageId = response.message_id;
                cachedList[i].deliveryStatus = DeliveryStatus.Sent;
                break;
            }
        }
        ChatHistoryCache.SaveHistory(sendCacheRoot, chatId, cachedList);

        Outbox.RemoveAt(sendCacheRoot, chatId, tempId);
        OnMessageStatusChanged?.Invoke(tempId, response.message_id, DeliveryStatus.Sent);
    }
    else
    {
        Debug.LogWarning($"[Wappi] message/send returned non-done status: {www.downloadHandler.text}");
        OnMessageStatusChanged?.Invoke(tempId, tempId, DeliveryStatus.Failed);
    }
}

/// <summary>
/// Tells Wappi the user has read the given chat. Fire-and-forget — on failure,
/// the next /chats/filter sync corrects any drift.
/// </summary>
private IEnumerator MarkChatAsRead(string chatId)
{
    if (string.IsNullOrEmpty(chatId)) yield break;

    string activeProfileId = GetActiveProfileId();
    if (string.IsNullOrEmpty(activeProfileId))
    {
        Debug.LogWarning($"[ChatManager.MarkChatAsRead] No active profile_id; skipping for chat {chatId}.");
        yield break;
    }

    if (!chatLookup.TryGetValue(chatId, out var vm))
    {
        Debug.LogWarning($"[ChatManager.MarkChatAsRead] Chat {chatId} not in lookup; skipping.");
        yield break;
    }

    if (string.IsNullOrEmpty(vm.LastMessageId))
    {
        Debug.LogWarning($"[ChatManager.MarkChatAsRead] Chat {chatId} has no LastMessageId; skipping.");
        yield break;
    }

    string url = $"https://wappi.pro/api/sync/message/mark/read?profile_id={activeProfileId}&mark_all=true";
    string jsonPayload = JsonConvert.SerializeObject(new { message_id = vm.LastMessageId });

    using UnityWebRequest www = new UnityWebRequest(url, "POST");
    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
    www.uploadHandler = new UploadHandlerRaw(bodyRaw);
    www.downloadHandler = new DownloadHandlerBuffer();
    www.SetRequestHeader("Content-Type", "application/json");
    www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
    www.timeout = 30;

    yield return www.SendWebRequest();

    if (www.result != UnityWebRequest.Result.Success)
    {
        Debug.LogError($"[ChatManager.MarkChatAsRead] {www.responseCode} {url}: {www.error}\n{www.downloadHandler.text}");
        yield break;
    }

    // Success — server will return unread_count=0 on next sync.
}
}

[Serializable]
public class WappiSendTextRequest
{
    public string body;
    public string recipient;
}

[Serializable]
public class WappiSendTextResponse
{
    public string status;
    public string message_id;
    public long timestamp;
}

public enum EmptyStateReason
{
    NoBotsExist,
    BotHasNoWhatsApp,
}
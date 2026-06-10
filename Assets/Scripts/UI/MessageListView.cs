using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections; 
using System.Collections.Generic;
using System.Linq; 
using UnityEngine.InputSystem;
using DG.Tweening;

public class MessageListView : MonoBehaviour
{
    public Transform content;
    public ScrollRect scrollRect; 
    
    [Header("UI Controls")]
    public GameObject loadingMessagesSpinner; 

    [Header("Prefabs")]
    public MessageItemView textIncoming;
    public MessageItemView textOutgoing;
    
    // --- NEW: The prefab for the date separator ---
    public DateSeparatorView dateSeparatorPrefab;

    [Header("Unread Markers")]
    [SerializeField] private UnreadSeparatorView unreadSeparatorPrefab;
    [SerializeField] private ScrollToBottomFab scrollToBottomFab;

    [Header("Settings")]
    [Tooltip("Percentage of a single page's height to use as the load trigger threshold (e.g., 0.85 for 85%).")]
    [Range(0f, 1f)]
    public float loadTriggerPercentage = 0.85f; 

    private string activeChatId;

    // Infinite Scroll State Variables
    private bool isLoadingData = false;
    private bool hasMoreMessages = true;
    private int loadedPagesCount = 1;

    private ScrollRect.MovementType defaultMovementType;

    // Live-message deferral: when SyncLatestMessages returns mid-load with
    // brand-new messages, OnLiveMessagesReceived would otherwise fire
    // AppendLiveMessagesRoutine in parallel with the in-progress
    // UpdateListRoutine. Both touch content, and AppendLiveMessagesRoutine's
    // synchronous ForceRebuildLayoutImmediate(content) caused batch 2 settle
    // to roughly double in profiling runs. We queue brand-new messages here
    // and drain them after the initial cache UpdateListRoutine completes.
    private bool isInitialLoadInProgress;
    private readonly List<MessageViewModel> pendingLiveMessages = new List<MessageViewModel>();

    // --- Unread markers state (per chat visit) ---
    // Incoming bubbles below the open-snapshot separator + any live incoming arrivals,
    // tracked by RectTransform so pagination prepends (which shift sibling indices) don't
    // break the badge. The separator instance is a content child destroyed by Clear().
    private readonly List<RectTransform> _unreadBubbles = new List<RectTransform>();
    private RectTransform _unreadSeparatorInstance;
    private float _lastFabRefreshTime;
    private Tween _scrollToBottomTween;

    // Reusable scratch buffers for the throttled below-fold recompute — avoids
    // per-tick GC while scrolling (ComputeBelowFoldCount runs ~20 Hz).
    private readonly Vector3[] _viewportCorners = new Vector3[4];
    private readonly Vector3[] _bubbleCorners = new Vector3[4];
    private readonly List<float> _bubbleTopsBuffer = new List<float>();

    void Awake()
    {
        if (scrollRect != null)
        {
            defaultMovementType = scrollRect.movementType;
        }

        // OnChatSelected subscription lives in Awake (not OnEnable) so the event
        // delivery works even when the chat panel is inactive — which is the case
        // on cold-open and between chats (slide-out deactivates the panel).
        // Other handlers (HandleBatchMessages, HandleLiveMessages) stay in
        // OnEnable because they call StartCoroutine and would throw on an inactive
        // GameObject.
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected += OnChatSelected;
        }
    }

    void OnDestroy()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected -= OnChatSelected;
        }
    }

    void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnBatchMessagesLoaded += HandleBatchMessages;
            ChatManager.Instance.OnLiveMessagesReceived += HandleLiveMessages;
            ChatManager.Instance.OnMessageRemoved += HandleMessageRemoved;
        }

        SwipeToBack.OnSlideOutComplete += HandleSlideOutComplete;

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.AddListener(OnScroll);
        }

        if (scrollToBottomFab != null)
        {
            scrollToBottomFab.OnClicked += HandleScrollToBottomClicked;
        }

        if (loadingMessagesSpinner)
        {
            loadingMessagesSpinner.SetActive(false);
        }
    }

    void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnBatchMessagesLoaded -= HandleBatchMessages;
            ChatManager.Instance.OnLiveMessagesReceived -= HandleLiveMessages;
            ChatManager.Instance.OnMessageRemoved -= HandleMessageRemoved;
        }

        SwipeToBack.OnSlideOutComplete -= HandleSlideOutComplete;

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.RemoveListener(OnScroll);
        }

        if (scrollToBottomFab != null)
        {
            scrollToBottomFab.OnClicked -= HandleScrollToBottomClicked;
        }

        // Kill any in-flight auto-scroll so it can't run against a deactivated ScrollRect.
        _scrollToBottomTween?.Kill();
        _scrollToBottomTween = null;
    }

    /// <summary>
    /// Fires from SwipeToBack after a slide-out snap finishes (BEFORE the panel
    /// is deactivated). Destroys all spawned bubbles immediately — each
    /// MessageItemView.OnDestroy frees its owned textures and sprites, so the
    /// memory of the chat the user just left is recovered now rather than
    /// waiting for the next chat open to clear it.
    /// </summary>
    void HandleSlideOutComplete()
    {
        StopAllCoroutines();
        Clear();
        ResetUnreadState();
        activeChatId = null;
        isInitialLoadInProgress = false;
        pendingLiveMessages.Clear();
    }

    void OnChatSelected(string chatId)
    {
        activeChatId = chatId;

        hasMoreMessages = true;
        isLoadingData = true;
        loadedPagesCount = 1;

        // Reset live-message deferral state for the new chat. Any pending
        // entries from a prior chat (in case StopAllCoroutines killed the
        // drain mid-flight) belong to that chat and must be discarded.
        isInitialLoadInProgress = true;
        pendingLiveMessages.Clear();

        if (scrollRect != null) scrollRect.movementType = defaultMovementType;

        if (loadingMessagesSpinner) loadingMessagesSpinner.SetActive(false);

        // Kill any in-flight UpdateListRoutine or AppendLiveMessagesRoutine
        // from a previous chat open. Without this, those coroutines keep
        // running and Instantiate bubbles into the (about-to-be-cleared)
        // content right after Clear() runs — producing duplicate bubbles or
        // bubbles from the previous chat leaking into the new view.
        StopAllCoroutines();

        Clear();
        ResetUnreadState();
    }

    void ResetUnreadState()
    {
        _unreadBubbles.Clear();
        _unreadSeparatorInstance = null; // its GameObject is a content child destroyed by Clear()
        _scrollToBottomTween?.Kill();
        _scrollToBottomTween = null;

        if (scrollToBottomFab != null)
        {
            scrollToBottomFab.SetCount(0);
            scrollToBottomFab.Hide();
        }
    }

    void OnScroll(Vector2 scrollPos)
    {
        // If the user grabs the list mid auto-scroll, cancel the tween so we don't fight them.
        if (_scrollToBottomTween != null && _scrollToBottomTween.IsActive()
            && Pointer.current != null && Pointer.current.press.isPressed)
        {
            _scrollToBottomTween.Kill();
            _scrollToBottomTween = null;
        }

        if (!isLoadingData && hasMoreMessages)
        {
            float scrollableHeight = Mathf.Max(0, scrollRect.content.rect.height - scrollRect.viewport.rect.height);
            float pixelsFromTop = (1.0f - scrollPos.y) * scrollableHeight;

            float singlePageHeight = scrollRect.content.rect.height / Mathf.Max(1, loadedPagesCount);
            float dynamicThreshold = singlePageHeight * (1.0f - loadTriggerPercentage);

            if (pixelsFromTop <= dynamicThreshold)
            {
                isLoadingData = true;

                if (scrollRect != null)
                {
                    scrollRect.movementType = ScrollRect.MovementType.Clamped;
                }

                if (loadingMessagesSpinner)
                {
                    loadingMessagesSpinner.SetActive(true);
                }

                ChatManager.Instance.LoadNextPage();
            }
        }

        RefreshFab();
    }

    void RefreshFab()
    {
        if (scrollToBottomFab == null || scrollRect == null) return;

        var contentRt = (RectTransform)content;
        bool scrollable = contentRt.rect.height > scrollRect.viewport.rect.height + 1f;
        bool scrolledUp = scrollRect.verticalNormalizedPosition > 0.05f;

        if (scrollable && scrolledUp) scrollToBottomFab.Show();
        else scrollToBottomFab.Hide();

        // Throttle the heavier below-fold recompute (~20 Hz). Show/Hide above is cheap
        // (no-op when already in the target state) so it stays responsive every event.
        if (Time.unscaledTime - _lastFabRefreshTime < 0.05f) return;
        _lastFabRefreshTime = Time.unscaledTime;

        scrollToBottomFab.SetCount(ComputeBelowFoldCount());
    }

    int ComputeBelowFoldCount()
    {
        if (scrollRect == null || _unreadBubbles.Count == 0) return 0;

        scrollRect.viewport.GetWorldCorners(_viewportCorners); // 0=BL, 1=TL, 2=TR, 3=BR
        float viewportBottomWorldY = _viewportCorners[0].y;

        _bubbleTopsBuffer.Clear();
        for (int i = 0; i < _unreadBubbles.Count; i++)
        {
            var rt = _unreadBubbles[i];
            if (rt == null) continue;
            rt.GetWorldCorners(_bubbleCorners);
            _bubbleTopsBuffer.Add(_bubbleCorners[1].y); // top-left world Y
        }

        return ScrollFabMath.CountBelowFold(_bubbleTopsBuffer, viewportBottomWorldY);
    }

    void HandleScrollToBottomClicked()
    {
        if (scrollRect == null) return;

        _scrollToBottomTween?.Kill();
        scrollRect.velocity = Vector2.zero;

        _scrollToBottomTween = DOTween.To(
                () => scrollRect.verticalNormalizedPosition,
                v => scrollRect.verticalNormalizedPosition = v,
                0f, 0.3f)
            .SetEase(Ease.OutCubic)
            .OnComplete(() =>
            {
                scrollRect.verticalNormalizedPosition = 0f;
                scrollRect.velocity = Vector2.zero;
                _scrollToBottomTween = null;
                if (scrollToBottomFab != null)
                {
                    scrollToBottomFab.SetCount(0);
                    scrollToBottomFab.Hide();
                }
            });
    }

// Note the new signature!
    void HandleBatchMessages(List<MessageViewModel> messages, bool isLoadMore, bool hasMoreFromServer)
    {
        if (messages == null || messages.Count == 0)
        {
            // Only stop the scrolling if the server explicitly tells us we hit the end!
            if (!hasMoreFromServer)
            {
                hasMoreMessages = false;
            }
            
            isLoadingData = false;

            // Defense-in-depth: an empty INITIAL batch must still release the live-message
            // gate. OnChatSelected set isInitialLoadInProgress = true, and the only other
            // reset lives inside UpdateListRoutine — which we never start here. Without this,
            // every subsequent HandleLiveMessages would park into pendingLiveMessages forever,
            // leaving the chat stuck empty until it is closed and reopened. Drain anything
            // already queued so late-arriving messages still render this visit.
            if (!isLoadMore && isInitialLoadInProgress)
            {
                isInitialLoadInProgress = false;
                if (pendingLiveMessages.Count > 0)
                {
                    var drained = pendingLiveMessages.OrderBy(x => x, MessageOrder.AscendingComparer).ToList();
                    pendingLiveMessages.Clear();
                    StartCoroutine(AppendLiveMessagesRoutine(drained));
                }
            }

            if (scrollRect != null) scrollRect.movementType = defaultMovementType;
            if(loadingMessagesSpinner) loadingMessagesSpinner.SetActive(false);
            return;
        }

        if (isLoadMore)
        {
            loadedPagesCount++;
        }

        // --- THE FIX: Trust the server, ignore our local count! ---
        hasMoreMessages = hasMoreFromServer;
        
        if(loadingMessagesSpinner) loadingMessagesSpinner.SetActive(false); 

        var sortedMessages = messages.OrderBy(x => x, MessageOrder.AscendingComparer).ToList();

        StartCoroutine(UpdateListRoutine(sortedMessages, isLoadMore));
    }

    // --- UPDATED: Route live messages to the dedicated appending routine ---
    void HandleLiveMessages(List<MessageViewModel> newMessages)
    {
        if (newMessages == null || newMessages.Count == 0) return;

        // If the initial cache UpdateListRoutine is still spawning bubbles, queue these
        // and drain after it completes. Running both routines in parallel made batch 2
        // settle ~280ms instead of ~120ms because AppendLiveMessagesRoutine's synchronous
        // ForceRebuildLayoutImmediate raced UpdateListRoutine's per-batch rebuild.
        //
        // The Phase check in ChatManager.SyncLatestMessages already filters out Prep/Slide
        // arrivals (they get queued in _pendingLiveSyncMessages and drained by
        // PopulateBubbles AFTER OnBatchMessagesLoaded). isInitialLoadInProgress here
        // covers the in-process window from "OnBatchMessagesLoaded fired" to
        // "UpdateListRoutine actually finished spawning everything" — which the phase
        // model alone does NOT cover because phase becomes Populate at the start of
        // OnBatchMessagesLoaded, not the end of UpdateListRoutine.
        if (isInitialLoadInProgress)
        {
            pendingLiveMessages.AddRange(newMessages);
            return;
        }

        var sortedMessages = newMessages.OrderBy(x => x, MessageOrder.AscendingComparer).ToList();
        StartCoroutine(AppendLiveMessagesRoutine(sortedMessages));
    }

    // Destroys the bubble for a cancelled in-flight send. There may be a single
    // match (the optimistic bubble), but we scan defensively. Mirrors the
    // clear-list destroy + ForceRebuild pattern used elsewhere in this view.
    void HandleMessageRemoved(string tempId)
    {
        if (string.IsNullOrEmpty(tempId) || content == null) return;

        bool removed = false;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i);
            var view = child.GetComponent<MessageItemView>();
            if (view != null && view.BoundVm != null && view.BoundVm.messageId == tempId)
            {
                Destroy(child.gameObject);
                removed = true;
            }
        }

        if (removed)
            LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
    }

// --- UPDATED: Beautiful Smooth Scroll & Fade Animation ---
// Maps a live arrival to the sibling index it must occupy when it sorts
// before the newest rendered bubble; -1 for the normal append case. Walks
// the content children so separators/spacers are skipped but still counted
// in the returned sibling index.
private int FindOutOfOrderSiblingIndex(MessageViewModel vm)
{
    var bubbleVms = new List<MessageViewModel>();
    var bubbleTransforms = new List<Transform>();
    for (int i = 0; i < content.childCount; i++)
    {
        var view = content.GetChild(i).GetComponent<MessageItemView>();
        if (view == null || view.BoundVm == null) continue;
        bubbleVms.Add(view.BoundVm);
        bubbleTransforms.Add(view.transform);
    }

    int insertAt = MessageOrder.InsertIndex(bubbleVms, vm);
    return insertAt >= 0 ? bubbleTransforms[insertAt].GetSiblingIndex() : -1;
}

IEnumerator AppendLiveMessagesRoutine(List<MessageViewModel> messages, bool suppressLanding = false)
{
    List<MessageItemView> newlyAddedItems = new List<MessageItemView>();
    List<CanvasGroup> newlyAddedCanvasGroups = new List<CanvasGroup>();

    // suppressLanding (initial-load drain): leave the scroll position untouched so
    // PlaceUnreadSeparatorAndLand owns the final landing. Forcing wasAtBottom false
    // disables every scroll/slide branch below; the bubbles still spawn and fade in.
    bool wasAtBottom = !suppressLanding && scrollRect != null && scrollRect.verticalNormalizedPosition <= 0.05f;

    foreach (var vm in messages)
    {
        bool isGroup = vm.chatId != null && vm.chatId.EndsWith("@g.us");

        // Out-of-order arrival (sorts before the newest rendered bubble, e.g.
        // a late Wappi delivery): insert at its canonical position so the live
        // view matches what a reopen would render. It joins an existing date
        // section, so no separator/spacer bookkeeping — the common newest-case
        // stays on the append path below. Pending = the user's own optimistic
        // send, which always appends (its device-clock timestamp may lag the
        // server; sync adopts the real keys later).
        int insertSiblingIndex = (vm.deliveryStatus == DeliveryStatus.Pending)
            ? -1
            : FindOutOfOrderSiblingIndex(vm);
        if (insertSiblingIndex >= 0)
        {
            var inserted = Instantiate(ResolvePrefab(vm), content);
            inserted.Bind(vm, true, true, (vm.isIncoming && isGroup));
            inserted.transform.SetSiblingIndex(insertSiblingIndex);

            CanvasGroup insertedCg = inserted.GetComponent<CanvasGroup>();
            if (insertedCg == null) insertedCg = inserted.gameObject.AddComponent<CanvasGroup>();
            insertedCg.alpha = 0f;
            newlyAddedCanvasGroups.Add(insertedCg);
            newlyAddedItems.Add(inserted);
            continue;
        }

        bool needDateSeparator = false;
        bool needSpacer = false;

        MessageItemView lastItem = null;
        if (content.childCount > 0)
            lastItem = content.GetChild(content.childCount - 1).GetComponent<MessageItemView>();

        if (lastItem != null)
        {
            string lastDate = GetDateString(lastItem.BoundVm.timestamp);
            string newDate  = GetDateString(vm.timestamp);
            if (lastDate != newDate) needDateSeparator = true;
            else if (lastItem.BoundVm.isIncoming != vm.isIncoming) needSpacer = true;
        }
        else needDateSeparator = true;

        if (needSpacer)
        {
            GameObject spacer = new GameObject("SenderSpacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(content, false);
            spacer.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 8f);
            LayoutElement le = spacer.GetComponent<LayoutElement>();
            le.minHeight = 8f; le.preferredHeight = 8f; le.flexibleHeight = 0f;
            spacer.transform.SetAsLastSibling();
        }

        if (needDateSeparator && dateSeparatorPrefab != null)
        {
            var sep = Instantiate(dateSeparatorPrefab, content);
            sep.SetDate(GetDateString(vm.timestamp));
            sep.transform.SetAsLastSibling();
        }

        if (lastItem != null && lastItem.BoundVm.isIncoming == vm.isIncoming && !needDateSeparator)
        {
            lastItem.Bind(lastItem.BoundVm, false, true, (lastItem.BoundVm.isIncoming && isGroup));
            lastItem.FinalizeCustomVisuals();
        }

        var item = Instantiate(ResolvePrefab(vm), content);
        item.Bind(vm, true, true, (vm.isIncoming && isGroup));
        item.transform.SetAsLastSibling();

        // Hide for exactly one frame — prevents the prefab flashing at a wrong position
        // before the layout group has placed it correctly
        CanvasGroup cg = item.GetComponent<CanvasGroup>();
        if (cg == null) cg = item.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        newlyAddedCanvasGroups.Add(cg);
        newlyAddedItems.Add(item);
    }

    // Calculate layout synchronously THIS frame — content height is now accurate
    Canvas.ForceUpdateCanvases();
    LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());

    // Set the scroll offset NOW before yielding — existing messages never get a chance to jump
    float startNorm = 0f;
    if (scrollRect && wasAtBottom)
    {
        float contentH  = scrollRect.content.rect.height;
        float viewportH = scrollRect.viewport.rect.height;
        float scrollableH = contentH - viewportH;

        if (scrollableH > 1f)
        {
            float slidePixels = Mathf.Min(viewportH * 0.25f, 180f);
            startNorm = Mathf.Clamp01(slidePixels / scrollableH);
            scrollRect.verticalNormalizedPosition = startNorm;
        }
    }

    // Single frame wait — layout group places items at their correct positions
    yield return null;

    foreach (var item in newlyAddedItems)
        if (item != null) item.FinalizeCustomVisuals();

    // Items are now in the right place — reveal them
    foreach (var cg in newlyAddedCanvasGroups)
        if (cg != null) cg.alpha = 1f;

    if (scrollRect) scrollRect.velocity = Vector2.zero;

    if (scrollRect && wasAtBottom && startNorm > 0f)
        yield return StartCoroutine(SlideUpRevealRoutine(startNorm));
    else if (scrollRect && wasAtBottom)
        scrollRect.verticalNormalizedPosition = 0f;

    // Track live incoming arrivals for the badge. When at bottom they slide into view and
    // sit above the fold (not counted); when scrolled up they're below the fold (counted).
    foreach (var item in newlyAddedItems)
    {
        if (item != null && item.BoundVm != null && item.BoundVm.isIncoming)
            _unreadBubbles.Add((RectTransform)item.transform);
    }

    Canvas.ForceUpdateCanvases();
    // On the initial-load drain, PlaceUnreadSeparatorAndLand runs next and refreshes the
    // FAB after it positions the separator — refreshing here would read a half-placed list.
    if (!suppressLanding) RefreshFab();
}

IEnumerator SlideUpRevealRoutine(float startNorm)
{
    float duration = 0.32f;
    float elapsed  = 0f;

    while (elapsed < duration)
    {
        elapsed += Time.deltaTime;
        float ease = 1f - Mathf.Pow(1f - elapsed / duration, 3f);
        scrollRect.verticalNormalizedPosition = Mathf.Lerp(startNorm, 0f, ease);
        yield return null;
    }

    scrollRect.verticalNormalizedPosition = 0f;
}
    
    private string GetDateString(long unixTimestamp)
    {
        DateTime messageDate = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).LocalDateTime.Date;
        DateTime today = DateTime.Today;

        TimeSpan difference = today - messageDate;

        if (difference.Days == 0) 
        {
            return "Today";
        }
        else if (difference.Days == 1) 
        {
            return "Yesterday";
        }
        else if (difference.Days >= 2 && difference.Days < 7) 
        {
            // "dddd" returns the full localized name of the day (e.g., "Monday", "Tuesday")
            return messageDate.ToString("dddd"); 
        }
        else 
        {
            // Older than a week
            return messageDate.ToString("dd MMM yyyy"); // e.g., "24 Feb 2026"
        }
    }

IEnumerator UpdateListRoutine(List<MessageViewModel> sortedMessages, bool isLoadMore)
    {
        if (scrollRect && !isLoadMore) scrollRect.velocity = Vector2.zero;

        // 2-frame yield removed: the slide-in callback already finished before
        // this coroutine ran, so there's nothing left to wait for. Profiling
        // showed this gate cost ~40ms on every chat-open with no benefit.

        Transform anchorItem = null;
        float oldAnchorY = 0;
        MessageItemView existingOldestView = null;
        
        // --- 2. Handle Infinite Scroll Date Boundaries ---
        if (isLoadMore && content.childCount > 0 && sortedMessages.Count > 0)
        {
            for (int i = 0; i < content.childCount; i++)
            {
                existingOldestView = content.GetChild(i).GetComponent<MessageItemView>();
                if (existingOldestView != null) break;
            }

            if (existingOldestView != null)
            {
                string oldDate = GetDateString(existingOldestView.BoundVm.timestamp);
                string newBatchYoungestDate = GetDateString(sortedMessages.Last().timestamp);

                if (oldDate == newBatchYoungestDate)
                {
                    DateSeparatorView existingSep = content.GetChild(0).GetComponent<DateSeparatorView>();
                    if (existingSep != null)
                    {
                        Destroy(existingSep.gameObject); 
                        Canvas.ForceUpdateCanvases(); 
                        LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
                    }
                }
                
                anchorItem = existingOldestView.transform;
                oldAnchorY = anchorItem.position.y;
            }

            var newestInOldBatch = sortedMessages.Last();
            var oldestInCurrentBatch = existingOldestView.BoundVm;

            if (newestInOldBatch.isIncoming != oldestInCurrentBatch.isIncoming && 
                GetDateString(newestInOldBatch.timestamp) == GetDateString(oldestInCurrentBatch.timestamp))
            {
                GameObject spacer = new GameObject("SenderSpacer", typeof(RectTransform), typeof(LayoutElement));
                spacer.transform.SetParent(content, false);
                
                RectTransform rt = spacer.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(0, 8f);
                LayoutElement le = spacer.GetComponent<LayoutElement>();
                le.minHeight = 8f; le.preferredHeight = 8f; le.flexibleHeight = 0f; 
                
                spacer.transform.SetAsFirstSibling(); 
            }
        }

        List<MessageItemView> batchItems = new List<MessageItemView>();
        
        // --- NEW: Keep track of the visibility controllers! ---
        List<CanvasGroup> batchCanvasGroups = new List<CanvasGroup>(); 
        
        int countSinceYield = 0;

        // --- 3. THE MAGIC LOOP: BACKWARDS (Newest to Oldest) ---
        for (int i = sortedMessages.Count - 1; i >= 0; i--)
        {
            var vm = sortedMessages[i];
            string currentDate = GetDateString(vm.timestamp);

            bool showTail = true;
            if (i < sortedMessages.Count - 1)
            {
                var nextVm = sortedMessages[i + 1];
                if (nextVm.isIncoming == vm.isIncoming && GetDateString(nextVm.timestamp) == currentDate)
                    showTail = false;
            }
            else if (isLoadMore && existingOldestView != null)
            {
                if (existingOldestView.BoundVm.isIncoming == vm.isIncoming && GetDateString(existingOldestView.BoundVm.timestamp) == currentDate)
                    showTail = false;
            }

            bool needDateSeparator = false;
            if (i > 0)
            {
                if (GetDateString(sortedMessages[i - 1].timestamp) != currentDate) needDateSeparator = true;
            }
            else 
            {
                needDateSeparator = true; 
            }

            bool isGroup = vm.chatId != null && vm.chatId.EndsWith("@g.us");
            bool showSenderName = false;
            
            if (isGroup && vm.isIncoming && !string.IsNullOrEmpty(vm.senderName))
            {
                if (i == 0) showSenderName = true;
                else if (sortedMessages[i - 1].senderName != vm.senderName || needDateSeparator) showSenderName = true;
            }

            var item = Instantiate(ResolvePrefab(vm), content);
            item.Bind(vm, showTail, true, showSenderName); 
            item.transform.SetAsFirstSibling(); 
            
            CanvasGroup itemCanvasGroup = item.GetComponent<CanvasGroup>();
            if (itemCanvasGroup == null) itemCanvasGroup = item.gameObject.AddComponent<CanvasGroup>();
            
            // --- THE FIX: KEEP IT INVISIBLE WHILE WE DO MATH ---
            itemCanvasGroup.alpha = 0f; 
            batchCanvasGroups.Add(itemCanvasGroup);
            batchItems.Add(item);

            bool needSpacer = false;
            if (i > 0 && sortedMessages[i - 1].isIncoming != vm.isIncoming) needSpacer = true;
            if (needDateSeparator) needSpacer = false; 

            if (needSpacer)
            {
                GameObject spacer = new GameObject("SenderSpacer", typeof(RectTransform), typeof(LayoutElement));
                spacer.transform.SetParent(content, false);
                
                RectTransform rt = spacer.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(0, 8f);
                LayoutElement le = spacer.GetComponent<LayoutElement>();
                le.minHeight = 8f; le.preferredHeight = 8f; le.flexibleHeight = 0f; 
                
                CanvasGroup spacerCg = spacer.AddComponent<CanvasGroup>();
                spacerCg.alpha = 0f; // Hidden
                batchCanvasGroups.Add(spacerCg);
                
                spacer.transform.SetAsFirstSibling(); 
            }

            if (needDateSeparator && dateSeparatorPrefab != null)
            {
                var sep = Instantiate(dateSeparatorPrefab, content);
                sep.SetDate(currentDate);
                
                CanvasGroup sepCanvasGroup = sep.GetComponent<CanvasGroup>();
                if (sepCanvasGroup == null) sepCanvasGroup = sep.gameObject.AddComponent<CanvasGroup>();
                sepCanvasGroup.alpha = 0f; // Hidden
                batchCanvasGroups.Add(sepCanvasGroup);

                sep.transform.SetAsFirstSibling(); 
            }

            countSinceYield++;

            // Yield after every spawned item so each frame's spawn cost
            // (~10ms with the decode-budget fix) fits alongside the slide-in
            // animation tick (~5ms). This is what makes parallel-load viable
            // now where it previously lagged the slide — we used to do 15
            // synchronous spawns per frame which blew the 16ms frame budget
            // immediately. Items still reveal in batches of 15 to maintain
            // the "chat populates as slide ends" feel.
            //
            // Every 15 items, settle the layout and reveal that batch. The
            // ForceRebuildLayoutImmediate + two yields below let TMP mesh
            // updates and nested CSF chains finish before alpha=1 — one yield
            // is not enough for TMP rich-text-with-sprites (items would pop
            // and resize as the natural layout pass catches up). Scroll
            // snap-to-bottom is intentionally only done in the final block
            // below, so progressive reveal doesn't fight a user's drag.
            if (!isLoadMore && countSinceYield % 15 == 0)
            {
                // No explicit ForceRebuildLayoutImmediate here. The per-item
                // yield above ran Unity's natural layout pass between every
                // spawn — by the time we reach the 15th item, each bubble's
                // CSF/VLG chain has had 1-15 frames to settle, so positions
                // and sizes are already correct. The synchronous rebuild
                // used to cost ~30-50ms and was what made the slide-in
                // animation feel laggy. We yield a couple extra frames just
                // to give TMP rich-text-with-sprites a chance to finalize
                // its mesh update before alpha=1.
                yield return null;
                yield return null;

                foreach (var msg in batchItems) if (msg != null) msg.FinalizeCustomVisuals();
                foreach (var cg in batchCanvasGroups) if (cg != null) cg.alpha = 1f;

                batchItems.Clear();
                batchCanvasGroups.Clear();
            }
            else if (!isLoadMore)
            {
                // Per-item yield: lets the slide-in animation render its tick
                // each frame while the spawn loop spreads its ~10ms work
                // across frames. Total spawn for 15 items = ~15 frames =
                // ~250ms, which lines up with the slide's ~290ms duration so
                // the first batch reveals around the same time the slide
                // settles. Skipped on isLoadMore because pagination spawn
                // happens after first paint is done — no need to spread cost.
                yield return null;
            }
        }

        // --- 4. Final Cleanup for Remaining Items ---
        // Handles the trailing (count % 15) items that didn't form a full
        // batch, or the entire list when isLoadMore is true (load-more skips
        // the inner-loop batched reveal). Same two-yield settle pattern as
        // the inner batches to keep TMP sprites stable on reveal.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());

        // Anchor adjustment must happen SYNCHRONOUSLY right after the layout rebuild,
        // before any yield. The rebuild has pushed the previously-oldest bubble (anchorItem)
        // down to make room for the new bubbles spawned above it. If we yield first,
        // for those frames the content is rendered in its displaced position — the user
        // sees existing bubbles jump down, then 2 frames later the adjustment snaps them
        // back. That's the load-more flicker. Doing the adjustment in the same frame as
        // the rebuild means the first rendered frame already has correct content position.
        if (isLoadMore && anchorItem != null)
        {
            float newAnchorY = anchorItem.position.y;
            float diff = newAnchorY - oldAnchorY;

            Vector2 finalPos = content.GetComponent<RectTransform>().anchoredPosition;
            finalPos.y -= diff;
            content.GetComponent<RectTransform>().anchoredPosition = finalPos;
        }

        yield return null;
        yield return null;

        foreach (var msg in batchItems) if (msg != null) msg.FinalizeCustomVisuals();
        foreach (var cg in batchCanvasGroups) if (cg != null) cg.alpha = 1f;

        batchItems.Clear();
        batchCanvasGroups.Clear();

        if (!isLoadMore)
        {
            // Release the live-message gate and fold in the first server sync's brand-new
            // messages BEFORE placing the unread separator. SyncLatestMessages runs in
            // parallel with this cache build and buffers its newer messages into
            // pendingLiveMessages while the gate is up. The old order placed the separator
            // on the cache alone and drained afterward, so those synced messages appended
            // BELOW the just-placed line — inflating the messages-below-the-line count past
            // the separator's printed count (the "says 3 unread but shows 6 below" bug).
            // Folding them in first lets PlaceUnreadSeparatorAndLand position the line
            // against the complete initial set (cache + first sync), so its count matches
            // the messages beneath it.
            //
            // Safe to spawn here: the chat-open flow is sequential, so the slide-in is over
            // by now and AppendLiveMessagesRoutine won't compete with the animation. Landing
            // is suppressed because PlaceUnreadSeparatorAndLand owns the final scroll.
            // isLoadingData stays true through placement so ScrollSeparatorToTop's scroll
            // can't trip OnScroll pagination.
            isInitialLoadInProgress = false;

            if (pendingLiveMessages.Count > 0)
            {
                var drained = pendingLiveMessages.OrderBy(x => x, MessageOrder.AscendingComparer).ToList();
                pendingLiveMessages.Clear();
                yield return StartCoroutine(AppendLiveMessagesRoutine(drained, suppressLanding: true));
            }

            PlaceUnreadSeparatorAndLand();

            // First-screen corner re-bake. The first batch bakes its rounded
            // corners early (the per-15 FinalizeCustomVisuals above), BEFORE the
            // final content rebuild and PlaceUnreadSeparatorAndLand settle its
            // rects — so those bubbles can be left showing corners baked against a
            // still-transient size. Below-the-fold bubbles dodge this because they
            // re-render (and re-bake) when they un-cull on scroll into view, and
            // paginated batches force their own rebuild; only the first screen is
            // left stale. Sweep every spawned bubble once the open has settled so
            // their corners are re-baked against the final rect — the same refresh
            // a manual scroll would have triggered.
            yield return null;
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < content.childCount; i++)
            {
                var settledItem = content.GetChild(i).GetComponent<MessageItemView>();
                if (settledItem != null) settledItem.FinalizeCustomVisuals();
            }
        }

        isLoadingData = false;
        if (scrollRect != null) scrollRect.movementType = defaultMovementType;
    }

    // Called at the end of the initial build (!isLoadMore). Reads ChatManager.UnreadOnOpen,
    // inserts the separator above the oldest unread incoming message, tracks the unread
    // bubbles for the badge, and either lands at the separator (N > 0) or jumps to the
    // newest message (N == 0).
    void PlaceUnreadSeparatorAndLand()
    {
        int n = ChatManager.Instance != null ? ChatManager.Instance.UnreadOnOpen : 0;

        _unreadBubbles.Clear();

        if (n <= 0 || unreadSeparatorPrefab == null)
        {
            if (scrollRect) scrollRect.verticalNormalizedPosition = 0f;
            RefreshFab();
            return;
        }

        // Content is ordered oldest→newest by sibling index (backwards spawn + SetAsFirstSibling),
        // so walk children high→low to build a newest-first view, skipping spacers/date separators.
        var bubblesNewestFirst = new List<RectTransform>();
        var isIncomingNewestFirst = new List<bool>();
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var bubble = content.GetChild(i).GetComponent<MessageItemView>();
            if (bubble == null || bubble.BoundVm == null) continue;
            bubblesNewestFirst.Add((RectTransform)bubble.transform);
            isIncomingNewestFirst.Add(bubble.BoundVm.isIncoming);
        }

        int belowCount = UnreadSeparatorPlacement.IndexForUnreadCount(isIncomingNewestFirst, n);

        // Track ONLY the unread incoming bubbles below the separator (own messages aren't unread).
        for (int i = 0; i < belowCount && i < bubblesNewestFirst.Count; i++)
        {
            if (isIncomingNewestFirst[i]) _unreadBubbles.Add(bubblesNewestFirst[i]);
        }

        var sep = Instantiate(unreadSeparatorPrefab, content);
        sep.SetCount(n);
        _unreadSeparatorInstance = (RectTransform)sep.transform;

        bool placeAtTop = bubblesNewestFirst.Count == 0
                          || belowCount <= 0
                          || belowCount >= bubblesNewestFirst.Count;
        if (placeAtTop)
        {
            sep.transform.SetAsFirstSibling();
        }
        else
        {
            // Insert immediately above the oldest unread bubble (= just below the newest read one).
            var oldestUnread = bubblesNewestFirst[belowCount - 1];
            sep.transform.SetSiblingIndex(oldestUnread.GetSiblingIndex());
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());

        ScrollSeparatorToTop();

        Canvas.ForceUpdateCanvases();
        RefreshFab();
    }

    // Scrolls so the separator's top edge sits at the viewport's top edge. Pivot- and
    // canvas-scale-agnostic: convert the separator's top world corner into content-local
    // space, measure its distance from the content's top edge, normalize against scrollable
    // height. verticalNormalizedPosition: 1 = top, 0 = bottom.
    void ScrollSeparatorToTop()
    {
        if (scrollRect == null || _unreadSeparatorInstance == null) return;

        Canvas.ForceUpdateCanvases();

        var contentRt = (RectTransform)content;
        float scrollableH = contentRt.rect.height - scrollRect.viewport.rect.height;
        if (scrollableH <= 1f)
        {
            scrollRect.verticalNormalizedPosition = 0f; // too short to scroll; stay at bottom
            return;
        }

        Vector3[] corners = new Vector3[4];
        _unreadSeparatorInstance.GetWorldCorners(corners); // 1 = top-left
        Vector3 sepTopLocal = contentRt.InverseTransformPoint(corners[1]);

        float distanceFromTop = Mathf.Clamp(contentRt.rect.yMax - sepTopLocal.y, 0f, scrollableH);
        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(1f - distanceFromTop / scrollableH);
    }

    void Clear()
    {
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }
    }
    
    MessageItemView ResolvePrefab(MessageViewModel vm)
    {
        return vm.isIncoming ? textIncoming : textOutgoing;
    }
}
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections; 
using System.Collections.Generic;
using System.Linq; 
using UnityEngine.InputSystem;

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
        }

        SwipeToBack.OnSlideOutComplete += HandleSlideOutComplete;

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.AddListener(OnScroll);
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
        }

        SwipeToBack.OnSlideOutComplete -= HandleSlideOutComplete;

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.RemoveListener(OnScroll);
        }
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
    }
    
    void OnScroll(Vector2 scrollPos)
    {
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

        var sortedMessages = messages.OrderBy(x => x.timestamp).ToList();

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

        var sortedMessages = newMessages.OrderBy(x => x.timestamp).ToList();
        StartCoroutine(AppendLiveMessagesRoutine(sortedMessages));
    }

// --- UPDATED: Beautiful Smooth Scroll & Fade Animation ---
IEnumerator AppendLiveMessagesRoutine(List<MessageViewModel> messages)
{
    List<MessageItemView> newlyAddedItems = new List<MessageItemView>();
    List<CanvasGroup> newlyAddedCanvasGroups = new List<CanvasGroup>();

    bool wasAtBottom = scrollRect != null && scrollRect.verticalNormalizedPosition <= 0.05f;

    foreach (var vm in messages)
    {
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

        bool isGroup = vm.chatId != null && vm.chatId.EndsWith("@g.us");

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
        if (!isLoadMore) ChatManager.ChatOpenLog($"UpdateListRoutine start ({sortedMessages.Count} msgs)");

        if (scrollRect && !isLoadMore) scrollRect.velocity = Vector2.zero;

        // 2-frame yield removed: the slide-in callback already finished before
        // this coroutine ran, so there's nothing left to wait for. Profiling
        // showed this gate cost ~40ms on every chat-open with no benefit.
        if (!isLoadMore) ChatManager.ChatOpenLog("Spawn loop start (no yield gate)");

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
                ChatManager.ChatOpenLog($"Batch settle start (item #{countSinceYield})");

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
                ChatManager.ChatOpenLog($"Batch reveal done (item #{countSinceYield})");

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
        if (!isLoadMore) ChatManager.ChatOpenLog($"Final settle start ({batchItems.Count} trailing)");
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
        if (!isLoadMore) ChatManager.ChatOpenLog("Final reveal done (all visible)");

        batchItems.Clear();
        batchCanvasGroups.Clear();

        if (!isLoadMore)
        {
            if (scrollRect) scrollRect.verticalNormalizedPosition = 0f;
        }

        isLoadingData = false;
        if (scrollRect != null) scrollRect.movementType = defaultMovementType;

        // Initial cache load is done — release the live-message gate and drain
        // anything SyncLatestMessages queued while we were spawning. Safe to do
        // here because the chat-open flow is sequential: Prep + Slide both run
        // before OnBatchMessagesLoaded fires (PopulateBubbles fires it during
        // the slide-in completion callback), so by the time we reach this point,
        // the slide is already over and AppendLiveMessagesRoutine won't compete
        // with the slide animation.
        if (!isLoadMore)
        {
            isInitialLoadInProgress = false;

            if (pendingLiveMessages.Count > 0)
            {
                var drained = pendingLiveMessages.OrderBy(x => x.timestamp).ToList();
                pendingLiveMessages.Clear();
                ChatManager.ChatOpenLog($"Drain pending live ({drained.Count} new)");
                StartCoroutine(AppendLiveMessagesRoutine(drained));
            }
        }
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
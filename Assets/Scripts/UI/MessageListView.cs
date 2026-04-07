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

    
    void Awake()
    {
        if (scrollRect != null)
        {
            defaultMovementType = scrollRect.movementType; 
        }
    }

    void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatSelected += OnChatSelected;
            ChatManager.Instance.OnBatchMessagesLoaded += HandleBatchMessages;
            ChatManager.Instance.OnLiveMessagesReceived += HandleLiveMessages;
        }
        
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
            ChatManager.Instance.OnChatSelected -= OnChatSelected;
            ChatManager.Instance.OnBatchMessagesLoaded -= HandleBatchMessages;
            ChatManager.Instance.OnLiveMessagesReceived -= HandleLiveMessages;
        }
        
        if (scrollRect != null)
        {
            scrollRect.onValueChanged.RemoveListener(OnScroll);
        }
    }

    void OnChatSelected(string chatId)
    {
        activeChatId = chatId;
        
        hasMoreMessages = true;
        isLoadingData = true; 
        loadedPagesCount = 1; 
        
        if (scrollRect != null) scrollRect.movementType = defaultMovementType;
        
        if (loadingMessagesSpinner) loadingMessagesSpinner.SetActive(false);
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

        var sortedMessages = newMessages.OrderBy(x => x.timestamp).ToList();

        // Use the new appending routine instead of the batch loading routine!
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
            string lastDate = GetDateString(lastItem.currentVm.timestamp);
            string newDate  = GetDateString(vm.timestamp);
            if (lastDate != newDate) needDateSeparator = true;
            else if (lastItem.currentVm.isIncoming != vm.isIncoming) needSpacer = true;
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

        if (lastItem != null && lastItem.currentVm.isIncoming == vm.isIncoming && !needDateSeparator)
        {
            lastItem.Bind(lastItem.currentVm, false, true, (lastItem.currentVm.isIncoming && isGroup));
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
        if (scrollRect && !isLoadMore) scrollRect.velocity = Vector2.zero;

        // 1. INSTANT UI FEEDBACK (No Freeze)
        if (!isLoadMore)
        {
            yield return null;
            yield return null; 
        }

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
                string oldDate = GetDateString(existingOldestView.currentVm.timestamp);
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
            var oldestInCurrentBatch = existingOldestView.currentVm;

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
                if (existingOldestView.currentVm.isIncoming == vm.isIncoming && GetDateString(existingOldestView.currentVm.timestamp) == currentDate)
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

            // G. STAGGERED YIELD & OUTLINE FIX
            if (!isLoadMore && countSinceYield % 15 == 0)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
                
                if (scrollRect) scrollRect.verticalNormalizedPosition = 0f; 
                
                // 1. Wait for Unity to actually draw the layouts
                yield return null; 
                
                // 2. Fix the outlines!
                foreach (var msg in batchItems) if (msg != null) msg.FinalizeCustomVisuals();
                
                // 3. POP THEM ON SCREEN NOW THAT THEY ARE PERFECT!
                foreach (var cg in batchCanvasGroups) if (cg != null) cg.alpha = 1f;

                batchItems.Clear();
                batchCanvasGroups.Clear();
            }
        }
        
        // --- 4. Final Cleanup for Remaining Items ---
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
        yield return null;

        // Fix outlines and reveal any leftovers
        foreach (var msg in batchItems) if (msg != null) msg.FinalizeCustomVisuals();
        foreach (var cg in batchCanvasGroups) if (cg != null) cg.alpha = 1f;
        
        batchItems.Clear();
        batchCanvasGroups.Clear();

        if (isLoadMore && anchorItem != null)
        {
            float newAnchorY = anchorItem.position.y;
            float diff = newAnchorY - oldAnchorY; 
            
            Vector2 finalPos = content.GetComponent<RectTransform>().anchoredPosition;
            finalPos.y -= diff; 
            content.GetComponent<RectTransform>().anchoredPosition = finalPos;
        }
        else
        {
            if (scrollRect) scrollRect.verticalNormalizedPosition = 0f;
        }

        isLoadingData = false; 
        if (scrollRect != null) scrollRect.movementType = defaultMovementType;
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
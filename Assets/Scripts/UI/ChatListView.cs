using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class ChatListView : MonoBehaviour
{
    [Header("Containers")]
    public Transform content;
    
    public ChatItemView prefab;

    [SerializeField] private ChatDeleteConfirm deleteConfirm;

    private Dictionary<string, ChatItemView> itemsByChatId = new();

    // Coalesced data-driven ordering: any number of same-frame triggers (a multi-chat
    // sync pass, the initial cache rebuild, a live send) collapse into ONE resort,
    // applied in LateUpdate before the frame renders.
    private bool resortPending;

    private ChatSearchBar searchBar;
    private string currentQuery = "";
    private static readonly CompareInfo Ci = CultureInfo.InvariantCulture.CompareInfo;

    void Start()
    {
        var manager = ChatManager.Instance;
        manager.OnChatAdded += AddChat;
        manager.OnChatListCleared += ClearChatList;
        manager.OnEmptyState += HandleEmptyState;
        manager.OnActiveBotChanged += HandleActiveBotChanged;
        manager.OnChatSelected += HandleChatSelected;
        manager.OnChatRemoved += RemoveChat;

        searchBar = GetComponentInChildren<ChatSearchBar>(true);
        if (searchBar != null)
            searchBar.OnQueryChanged += ApplyFilter;

        foreach (var chat in manager.Chats)
            AddChat(chat);
    }

    void ClearChatList()
    {
        // Destroy only the items this view tracks — leaves any non-item
        // siblings (e.g. ChatsSearchBar header) intact across bot switches.
        foreach (var item in itemsByChatId.Values)
        {
            if (item != null) Destroy(item.gameObject);
        }
        itemsByChatId.Clear();
    }

    void AddChat(ChatViewModel vm)
    {
        // Real data came in — make sure our content panel is visible.
        if (content != null && !content.gameObject.activeSelf)
        {
            content.gameObject.SetActive(true);
        }

        // --- THE FIX: Everything goes into the normalContent now! ---
        var item = Instantiate(prefab, content);
        item.Bind(vm);
        itemsByChatId[vm.ChatId] = item;

        // Provisional position only — the coalesced resort below places the row by
        // last-message time. Server order is NOT trusted: a brand-new chat arriving
        // mid-session would otherwise land at the bottom of the list.
        item.transform.SetAsLastSibling();
        item.transform.localScale = Vector3.one;

        // Apply the active filter so newly-arriving chats respect any query
        // the user has typed (e.g. after a bot switch with a query still set).
        ApplyMatchToItem(item, vm);

        RequestResort();

        // Row movement on update is handled inside ChatItemView.OnLastMessageChanged,
        // which unsubscribes itself in OnDestroy. Don't re-subscribe here — that leaks closures.
    }

    private void HandleEmptyState(EmptyStateReason _)
    {
        // The EmptyStateView surface activates itself; we just hide the list area.
        if (content != null)
        {
            content.gameObject.SetActive(false);
        }
    }

    private void HandleActiveBotChanged(string _)
    {
        if (content != null)
        {
            content.gameObject.SetActive(true);
        }
    }

    private void HandleChatSelected(string _)
    {
        // User opened a chat — drop search focus so TMP's caret can't linger
        // on top of the placeholder when they swipe back to the list.
        if (searchBar != null) searchBar.ReleaseFocus();
        // ...and put away any open swipe-to-delete reveal.
        SwipeToDelete.CloseAnyOpen();
    }

    private void ApplyFilter(string query)
    {
        currentQuery = query ?? "";
        foreach (var kvp in itemsByChatId)
        {
            var item = kvp.Value;
            if (item == null) continue;
            ApplyMatchToItem(item, item.Vm);
        }
    }

    private void ApplyMatchToItem(ChatItemView item, ChatViewModel vm)
    {
        if (item == null) return;
        bool match = Matches(vm, currentQuery);
        if (item.gameObject.activeSelf != match)
            item.gameObject.SetActive(match);
    }

    private static bool Matches(ChatViewModel vm, string q)
    {
        if (string.IsNullOrEmpty(q)) return true;
        if (vm == null) return false;

        if (!string.IsNullOrEmpty(vm.Title)
            && Ci.IndexOf(vm.Title, q, CompareOptions.IgnoreCase) >= 0)
            return true;

        if (!string.IsNullOrEmpty(vm.LastMessage)
            && Ci.IndexOf(vm.LastMessage, q, CompareOptions.IgnoreCase) >= 0)
            return true;

        return false;
    }

    /// <summary>
    /// Schedules a data-driven resort of the list (newest last-message first, via
    /// ChatListOrder). Coalesced: any number of same-frame requests — one per changed
    /// chat in a sync pass, one per row of the initial rebuild — apply as a single
    /// pass in LateUpdate, before the frame renders. Replaces the old per-row
    /// insert-at-top (RaiseToTop), which REVERSED every chat that changed within one
    /// multi-chat sync pass: ParseChatsJson iterates newest-first, so the newest row
    /// was raised first and each older row then landed above it.
    /// </summary>
    public void RequestResort()
    {
        resortPending = true;
    }

    void LateUpdate()
    {
        if (!resortPending) return;
        resortPending = false;
        ApplyOrderNow();
    }

    private void ApplyOrderNow()
    {
        if (content == null) return;

        // Rows in current visual order. Skips non-row children (the pinned
        // ChatsSearchBar header) and rows already collapsing out after a delete
        // (removed from itemsByChatId but alive until their tween completes).
        var rows = new List<ChatItemView>();
        for (int i = 0; i < content.childCount; i++)
        {
            var item = TrackedRowAt(i);
            if (item != null) rows.Add(item);
        }

        var ordered = ChatListOrder.Apply(rows, r => r.Vm.LastMessageTime);

        // Fill ONLY the slots currently held by tracked rows, top-down, re-scanning
        // trackedness live because slots shift as rows move. Untracked children —
        // the pinned ChatsSearchBar header and any delete-collapsing row — keep
        // their own slots; assigning a contiguous block instead would expel a
        // mid-collapse row to the list bottom whenever a resort lands inside its
        // 0.2s tween (deterministically so on the same-frame RollbackDelete path).
        // Rows only ever move UP to their slot, so placed slots are never disturbed.
        int orderedIdx = 0;
        for (int slot = 0; slot < content.childCount && orderedIdx < ordered.Count; slot++)
        {
            if (TrackedRowAt(slot) == null) continue;

            var row = ordered[orderedIdx++];
            if (row.transform.GetSiblingIndex() != slot)
                row.transform.SetSiblingIndex(slot);

            // A row's last message may have changed since the previous pass — its
            // visibility under the active search query can flip either way.
            ApplyMatchToItem(row, row.Vm);
        }
    }

    /// <summary>
    /// The tracked chat row at a content child index, or null when that child is
    /// not a row this view owns (the ChatsSearchBar header, a row mid-collapse
    /// after RemoveChat, or a stale row a rollback re-add has already superseded).
    /// </summary>
    private ChatItemView TrackedRowAt(int childIndex)
    {
        var item = content.GetChild(childIndex).GetComponent<ChatItemView>();
        if (item == null || item.Vm == null) return null;
        return itemsByChatId.TryGetValue(item.Vm.ChatId, out var tracked) && tracked == item
            ? item
            : null;
    }

    // Collapse the row out, then destroy it. The scroll content uses a layout group
    // (rows reorder by sibling index), so animating LayoutElement.preferredHeight reflows.
    private void RemoveChat(string chatId)
    {
        if (!itemsByChatId.TryGetValue(chatId, out var item))
            return;
        itemsByChatId.Remove(chatId);
        if (item == null) return;

        var rt = (RectTransform)item.transform;
        var le = item.GetComponent<LayoutElement>();
        if (le == null) le = item.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = rt.rect.height;

        var cg = item.GetComponent<CanvasGroup>();
        if (cg == null) cg = item.gameObject.AddComponent<CanvasGroup>();

        var go = item.gameObject;
        DOTween.To(() => le.preferredHeight, v => le.preferredHeight = v, 0f, 0.2f)
            .SetEase(Ease.InCubic)
            .OnComplete(() => { if (go != null) Destroy(go); });
        cg.DOFade(0f, 0.2f);
    }

    // Called by a row's Delete button (via ChatItemView) — raises the confirm dialog.
    public void RequestDelete(ChatViewModel vm)
    {
        if (vm == null) return;
        if (deleteConfirm != null) deleteConfirm.Ask(vm.ChatId, vm.Title);
        else ChatManager.Instance?.DeleteChat(vm.ChatId); // fallback: no dialog wired
    }

    void OnDestroy()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatAdded -= AddChat;
            ChatManager.Instance.OnChatListCleared -= ClearChatList;
            ChatManager.Instance.OnEmptyState -= HandleEmptyState;
            ChatManager.Instance.OnActiveBotChanged -= HandleActiveBotChanged;
            ChatManager.Instance.OnChatSelected -= HandleChatSelected;
            ChatManager.Instance.OnChatRemoved -= RemoveChat;
        }

        if (searchBar != null)
            searchBar.OnQueryChanged -= ApplyFilter;
    }
}
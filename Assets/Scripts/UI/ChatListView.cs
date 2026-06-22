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

        // Since Manager sends them in order, SetAsLastSibling
        // puts them in the correct sequence. Empty chats will naturally pile at the bottom.
        item.transform.SetAsLastSibling();
        item.transform.localScale = Vector3.one;

        // Apply the active filter so newly-arriving chats respect any query
        // the user has typed (e.g. after a bot switch with a query still set).
        ApplyMatchToItem(item, vm);

        // Row movement on update is handled inside ChatItemView.OnVmUpdated, which
        // unsubscribes itself in OnDestroy. Don't re-subscribe here — that leaks closures.
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

    // Header-aware bubble-to-top. When a ChatsSearchBar header sits at
    // sibling index 0, chat rows must land at index 1 so the search row
    // stays pinned to the top of the scroll content.
    public void RaiseToTop(ChatItemView item)
    {
        if (item == null || content == null) return;

        int firstChatIndex = 0;
        if (content.childCount > 0)
        {
            var first = content.GetChild(0);
            if (first != null && first.GetComponent<ChatSearchBar>() != null)
                firstChatIndex = 1;
        }

        item.transform.SetSiblingIndex(firstChatIndex);

        // The chat's last message just changed — its visibility under the
        // active query may have flipped (e.g. it now matches and should
        // appear, or no longer matches and should hide).
        ApplyMatchToItem(item, item.Vm);
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
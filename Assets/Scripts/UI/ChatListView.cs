using System.Collections.Generic;
using UnityEngine;

public class ChatListView : MonoBehaviour
{
    [Header("Containers")]
    public Transform content;
    
    public ChatItemView prefab;

    private Dictionary<string, ChatItemView> itemsByChatId = new();

    void Start()
    {
        var manager = ChatManager.Instance;
        manager.OnChatAdded += AddChat;
        manager.OnChatListCleared += ClearChatList;
        manager.OnEmptyState += HandleEmptyState;
        manager.OnActiveBotChanged += HandleActiveBotChanged;

        foreach (var chat in manager.Chats)
            AddChat(chat);
    }

    void ClearChatList()
    {
        // 1. Wipe the unified chat list
        foreach (Transform child in content) 
        {
            Destroy(child.gameObject);
        }
        
        // 2. Clear the dictionary so we don't hold "ghost" references in memory!
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
    }

    void OnDestroy()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatAdded -= AddChat;
            ChatManager.Instance.OnChatListCleared -= ClearChatList;
            ChatManager.Instance.OnEmptyState -= HandleEmptyState;
            ChatManager.Instance.OnActiveBotChanged -= HandleActiveBotChanged;
        }
    }
}
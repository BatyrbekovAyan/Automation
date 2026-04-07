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
        // --- THE FIX: Everything goes into the normalContent now! ---
        var item = Instantiate(prefab, content);
        item.Bind(vm);
        itemsByChatId[vm.ChatId] = item;

        // Since Manager sends them in order, SetAsLastSibling 
        // puts them in the correct sequence. Empty chats will naturally pile at the bottom.
        item.transform.SetAsLastSibling();
        item.transform.localScale = Vector3.one;

        vm.OnUpdated += (updatedVm) => HandleChatMovement(updatedVm, item);
    }

    void HandleChatMovement(ChatViewModel vm, ChatItemView item)
    {
        // --- THE FIX: Unified Movement Logic ---
        // Any time a chat receives a new message, it simply jumps to the top of the unified list!
        item.transform.SetAsFirstSibling();
    }    
    
    void OnDestroy()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnChatAdded -= AddChat;
            ChatManager.Instance.OnChatListCleared -= ClearChatList; 
        }
    }
}
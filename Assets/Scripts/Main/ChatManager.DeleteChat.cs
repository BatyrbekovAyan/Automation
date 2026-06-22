using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    /// <summary>Fired after a chat is removed from the in-memory list (optimistic or confirmed).</summary>
    public event Action<string> OnChatRemoved;

    // Suppresses re-add of chats deleted this session until the server confirms (see ParseChatsJson).
    private readonly DeletedChatGuard _deletedChats = new DeletedChatGuard();

    /// <summary>
    /// Optimistically removes the chat + its caches, then deletes it on the Wappi server.
    /// Rolls back (re-adds the row) if the server call fails.
    /// </summary>
    public void DeleteChat(string chatId)
    {
        if (string.IsNullOrEmpty(chatId)) return;
        if (!chatLookup.TryGetValue(chatId, out var vm)) return;

        int index = Chats.IndexOf(vm);

        _deletedChats.MarkDeleted(chatId);
        RemoveChatLocally(chatId);
        EvictChatCaches(chatId);

        StartCoroutine(DeleteChatRoutine(chatId, vm, index));
    }

    private void RemoveChatLocally(string chatId)
    {
        if (chatLookup.TryGetValue(chatId, out var vm))
        {
            Chats.Remove(vm);
            chatLookup.Remove(chatId);
        }
        OnChatRemoved?.Invoke(chatId);
    }

    private void EvictChatCaches(string chatId)
    {
        string root = GetCacheRoot();
        ChatHistoryCache.DeleteHistory(root, chatId);

        string cachePath = Path.Combine(root, "chats.json");
        if (!File.Exists(cachePath)) return;
        try
        {
            string updated = ChatListCacheEditor.RemoveChat(File.ReadAllText(cachePath), chatId);
            File.WriteAllText(cachePath, updated);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ChatManager] chats.json rewrite failed for {chatId}: {ex.Message}");
        }
    }

    private IEnumerator DeleteChatRoutine(string chatId, ChatViewModel vm, int index)
    {
        string profileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(profileId))
        {
            Debug.LogWarning($"[ChatManager] DeleteChat: no active profile_id; rolling back {chatId}.");
            RollbackDelete(chatId, vm, index);
            yield break;
        }

        string recipient = WappiRecipient.FromChatId(chatId);
        string url = $"https://wappi.pro/api/sync/chat/delete?profile_id={profileId}";
        string body = JsonConvert.SerializeObject(new WappiDeleteChatRequest { recipient = recipient });

        using UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] raw = System.Text.Encoding.UTF8.GetBytes(body);
        www.uploadHandler = new UploadHandlerRaw(raw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Wappi] chat/delete failed [{www.responseCode}] {url}: {www.error}\n{www.downloadHandler?.text}");
            RollbackDelete(chatId, vm, index);
            yield break;
        }

        WappiDeleteChatResponse resp = null;
        try { resp = JsonConvert.DeserializeObject<WappiDeleteChatResponse>(www.downloadHandler.text); }
        catch (Exception ex)
        {
            Debug.LogError($"[Wappi] chat/delete parse failed: {ex.Message}\n{www.downloadHandler.text}");
        }

        if (resp == null || resp.status != "done")
        {
            Debug.LogWarning($"[Wappi] chat/delete returned non-done status: {www.downloadHandler?.text}");
            RollbackDelete(chatId, vm, index);
            yield break;
        }

        // Success: keep the guard until a later /chats/filter no longer lists chatId
        // (cleared by ParseChatsJson's reconcile).
    }

    private void RollbackDelete(string chatId, ChatViewModel vm, int index)
    {
        _deletedChats.Clear(chatId);
        if (vm == null) return;
        if (chatLookup.ContainsKey(chatId)) return; // already restored / never gone

        Chats.Insert(Mathf.Clamp(index, 0, Chats.Count), vm);
        chatLookup[chatId] = vm;
        OnChatAdded?.Invoke(vm); // re-spawns the row (appends)
    }
}

[Serializable]
public class WappiDeleteChatRequest
{
    public string recipient;
}

[Serializable]
public class WappiDeleteChatResponse
{
    public string status;
}

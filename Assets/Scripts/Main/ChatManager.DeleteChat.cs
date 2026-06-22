using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    /// <summary>Fired after a chat is removed from the in-memory list (optimistic delete, server sync, or rollback re-add via OnChatAdded).</summary>
    public event Action<string> OnChatRemoved;

    // Per-bot soft-delete watermarks: chatId -> the chat's last-message unix timestamp at deletion.
    // Loaded in LoadChatsForActiveBot, consulted in ParseChatsJson (DeletedChatRule), persisted via
    // DeletedChatStore. A deleted chat stays hidden until a newer message arrives (then it revives).
    private Dictionary<string, long> _deletedWatermarks = new Dictionary<string, long>();

    /// <summary>
    /// Optimistically removes the chat + its message cache, records a deletion watermark, then
    /// soft-deletes it on the Wappi server. Rolls back (re-adds the row, drops the watermark) on failure.
    /// </summary>
    public void DeleteChat(string chatId)
    {
        if (string.IsNullOrEmpty(chatId)) return;
        if (!chatLookup.TryGetValue(chatId, out var vm)) return;

        int index = Chats.IndexOf(vm);

        // Watermark at the current last message: the chat hides until something newer arrives.
        _deletedWatermarks[chatId] = vm.LastMessageTime;
        DeletedChatStore.Save(GetCacheRoot(), _deletedWatermarks);

        RemoveChatLocally(chatId);
        ChatHistoryCache.DeleteHistory(GetCacheRoot(), chatId);

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

        // Success: the watermark keeps the chat hidden until newer activity revives it. Nothing else to do.
    }

    private void RollbackDelete(string chatId, ChatViewModel vm, int index)
    {
        _deletedWatermarks.Remove(chatId);
        DeletedChatStore.Save(GetCacheRoot(), _deletedWatermarks);
        if (vm == null) return;
        if (chatLookup.ContainsKey(chatId)) return; // already restored / never gone

        Chats.Insert(Mathf.Clamp(index, 0, Chats.Count), vm);
        chatLookup[chatId] = vm;
        OnChatAdded?.Invoke(vm); // re-spawns the row (appends)
    }
}

[Serializable]
public class WappiDeleteChatRequest { public string recipient; }

[Serializable]
public class WappiDeleteChatResponse { public string status; }

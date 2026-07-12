using System;
using System.Collections;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    /// <summary>Fired after a chat is removed from the in-memory list (optimistic delete, server sync, or rollback re-add via OnChatAdded).</summary>
    public event Action<string> OnChatRemoved;

    /// <summary>
    /// Whether the active channel exposes a chat/delete endpoint. Only WhatsApp does —
    /// wappi tapi has NO Telegram chat/delete, so DeleteChat is a hard no-op on Telegram
    /// (the swipe-action UI gate lands in Phase 6; this guard prevents any code path from
    /// attempting a destructive call where no endpoint exists — CHAT-10 / T-0503-04).
    /// </summary>
    public bool ActiveChannelSupportsChatDelete => ActiveChannel == ChatChannel.WhatsApp;

    /// <summary>
    /// Optimistically removes the chat + its message cache, then deletes it on the Wappi server.
    /// A successful chat/delete removes the chat in WhatsApp and reports it back as isDeleted, which
    /// ParseChatsJson honors so it stays gone across syncs and bot-switches. Rolls back (re-adds the
    /// row) if the server call fails. On Telegram this is a no-op (no tapi chat/delete endpoint):
    /// no optimistic removal, no cache eviction, no server coroutine.
    /// </summary>
    public void DeleteChat(string chatId)
    {
        if (!ActiveChannelSupportsChatDelete) return;
        if (string.IsNullOrEmpty(chatId)) return;
        if (!chatLookup.TryGetValue(chatId, out var vm)) return;

        int index = Chats.IndexOf(vm);

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
        string url = WappiEndpoints.Sync(ActiveChannel, $"chat/delete?profile_id={profileId}");
        string body = JsonConvert.SerializeObject(new WappiDeleteChatRequest { recipient = recipient });

        using UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] raw = System.Text.Encoding.UTF8.GetBytes(body);
        www.uploadHandler = new UploadHandlerRaw(raw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;

        // TEMP diagnostic: log the exact outgoing request so we can compare it byte-for-byte
        // against Wappi's docs / a Postman call when chat/delete returns "deleteChat error".
        Debug.Log($"[Wappi] chat/delete → POST {url}  chatId='{chatId}'  body={body}");

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

        // Success: the chat is deleted on the server (comes back isDeleted=true); ParseChatsJson keeps it hidden.
    }

    private void RollbackDelete(string chatId, ChatViewModel vm, int index)
    {
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

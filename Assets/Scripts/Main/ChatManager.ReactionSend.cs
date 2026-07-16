using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    /// <summary>
    /// Sends (or toggles/removes) the owner's reaction to a message and reflects it
    /// instantly. Mirrors the text-send pattern: optimistic local apply first, then a
    /// background POST that reverts on failure. Runs on Manager.Instance so a mid-send
    /// bot switch (StopAllCoroutines on this object) can't strand the optimistic state.
    /// </summary>
    public void SendReaction(MessageViewModel target, string tappedEmoji)
    {
        if (target == null || string.IsNullOrEmpty(tappedEmoji)) return;
        if (string.IsNullOrEmpty(currentChatId)) return;

        // Can only react to a server-acknowledged message — a Pending/Failed optimistic
        // message still carries a temp id ("sending_…"), not a real Wappi stanza id.
        if (string.IsNullOrEmpty(target.messageId)
            || target.messageId.StartsWith("sending_")
            || target.deliveryStatus == DeliveryStatus.Pending
            || target.deliveryStatus == DeliveryStatus.Failed)
        {
            Debug.LogWarning("[ChatManager] SendReaction ignored: message not yet sent.");
            return;
        }

        string profileId = GetActiveProfileId();
        if (string.IsNullOrEmpty(profileId))
        {
            Debug.LogWarning("[ChatManager] SendReaction aborted: no valid profile for active bot.");
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string priorEmoji = OutgoingReaction.CurrentMyEmoji(target);          // snapshot for revert
        ReactionEvent ev = OutgoingReaction.Resolve(target, tappedEmoji, now);

        // --- INSTANT UI: apply + notify + persist before any network call ---
        string sendCacheRoot = GetCacheRoot();
        if (ReactionStore.ApplyToMessage(target, ev))
        {
            OnMessageReactionsChanged?.Invoke(target);
            PersistReaction(sendCacheRoot, target);
        }

        // Reflect the reaction in the chat-list row in place (no reorder). We hold the
        // target message, so we can show the full "You reacted ❤️ to “msg”".
        var sendChatVm = GetChat(target.chatId);
        if (sendChatVm != null)
            sendChatVm.SetReactionPreview(
                ev.emoji, true, target.text, ChatPreviewFormatter.TargetTypeKey(target.type));

        MonoBehaviour runner = Manager.Instance != null ? (MonoBehaviour)Manager.Instance : this;
        runner.StartCoroutine(PostReactionRoutine(target, ev.emoji, priorEmoji, profileId, sendCacheRoot, now));
    }

    private IEnumerator PostReactionRoutine(
        MessageViewModel target, string sentEmoji, string priorEmoji,
        string profileId, string sendCacheRoot, long appliedTime)
    {
        string url = WappiEndpoints.Sync(ActiveChannel, $"message/reaction?profile_id={profileId}");
        var requestData = new WappiSendReactionRequest
        {
            body       = sentEmoji,
            message_id = target.messageId,
            // tapi requires the recipient; WhatsApp omits it (kept byte-identical).
            recipient  = ActiveChannel == ChatChannel.Telegram
                       ? ChatIdFormat.Recipient(target.chatId)
                       : null,
        };
        string jsonPayload = JsonConvert.SerializeObject(requestData);

        using UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
        www.timeout = 30;

        yield return www.SendWebRequest();

        bool ok = false;
        if (www.result == UnityWebRequest.Result.Success)
        {
            try
            {
                var response = JsonConvert.DeserializeObject<WappiSendReactionResponse>(www.downloadHandler.text);
                if (response != null && response.status == "done")
                {
                    ok = true;
                    if (!string.IsNullOrEmpty(response.message_id))
                        seenMessageIds.Add(response.message_id);   // ignore our own echo if it ever syncs back
                }
                else
                {
                    Debug.LogWarning($"[Wappi] message/reaction non-done status: {www.downloadHandler.text}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Wappi] message/reaction response parse failed: {ex.Message}\n{www.downloadHandler.text}");
            }
        }
        else
        {
            Debug.LogError($"[Wappi] message/reaction failed: {www.error}\n{www.downloadHandler?.text}");
        }

        if (ok) yield break;

        // --- REVERT: restore the reaction state we optimistically changed ---
        var revert = new ReactionEvent
        {
            targetId   = target.messageId,
            emoji      = priorEmoji ?? "",                 // null prior => removal
            reactorKey = OutgoingReaction.MeReactorKey,
            senderName = "Me",
            fromMe     = true,
            time       = appliedTime
        };
        if (ReactionStore.ApplyToMessage(target, revert))
        {
            OnMessageReactionsChanged?.Invoke(target);
            PersistReaction(sendCacheRoot, target);
        }

        // Also revert the chat-list row preview (T-08-06-01). SendReaction optimistically set
        // "You reacted …" on the row; a failed send must not leave that stuck. Mirror the pill
        // revert — restore the prior reaction. An empty prior renders "Reaction removed", which
        // the next chat-list sync overwrites with the real last message (the failed reaction
        // never reached the server). No "You reacted …" row survives a 400.
        var revertChatVm = GetChat(target.chatId);
        if (revertChatVm != null)
            revertChatVm.SetReactionPreview(
                priorEmoji ?? "", true, target.text, ChatPreviewFormatter.TargetTypeKey(target.type));
    }

    /// <summary>
    /// Persists one message's updated reactions into the on-disk history. Load-edit-save
    /// the full cached list (like the text-send path) — never save the rendered-page
    /// list (_activeChatCache), which would truncate the cached history.
    /// </summary>
    private void PersistReaction(string cacheRoot, MessageViewModel target)
    {
        if (target == null || string.IsNullOrEmpty(target.chatId)) return;
        List<MessageViewModel> cached = ChatHistoryCache.LoadHistory(cacheRoot, target.chatId);
        for (int i = 0; i < cached.Count; i++)
        {
            if (cached[i].messageId == target.messageId)
            {
                cached[i].reactions = target.reactions;
                ChatHistoryCache.SaveHistory(cacheRoot, target.chatId, cached);
                return;
            }
        }
        // Not in cache (older than the retained window) — skip rather than truncate.
    }
}

[Serializable]
public class WappiSendReactionRequest
{
    public string body;        // the emoji; "" removes the reaction
    public string message_id;  // target message's Wappi stanza id

    // tapi requires a recipient; WhatsApp does not. Serialized only when set
    // (mirrors WappiSendTextRequest.quotedMessageId) so the WhatsApp body stays
    // byte-identical.
    [JsonProperty("recipient", NullValueHandling = NullValueHandling.Ignore)]
    public string recipient;
}

[Serializable]
public class WappiSendReactionResponse
{
    public string status;      // "done" on success
    public string message_id;  // the reaction stanza's own id
    public long timestamp;
    public string time;
    public string uuid;
}

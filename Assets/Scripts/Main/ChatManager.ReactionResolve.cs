using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    // Phase 2: lazily backfill the reacted-to text for chat-list reaction rows.
    private readonly Queue<string> _reactionResolveQueue = new Queue<string>();   // chatIds pending
    private readonly HashSet<string> _reactionResolveInFlight = new HashSet<string>(); // reactionIds
    private bool _reactionResolveDraining;

    /// <summary>
    /// Entry point called by ChatItemView when a reaction row missing its target text comes
    /// on screen. Cache hit → fills instantly; miss → enqueues one serial messages/get fetch.
    /// </summary>
    public void ResolveReactionTarget(ChatViewModel chatVm)
    {
        if (chatVm == null) return;
        if (chatVm.LastMessageType != "reaction") return;
        if (chatVm.ReactionTargetText != null) return;          // already resolved (incl. "")
        string reactionId = chatVm.LastMessageId;
        if (string.IsNullOrEmpty(reactionId)) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (ReactionTargetCache.TryGet(GetCacheRoot(), reactionId, now, out string cachedText, out string cachedType))
        {
            chatVm.UpdateReactionContext(cachedText, cachedType);
            return;
        }

        if (_reactionResolveInFlight.Contains(reactionId)) return;
        _reactionResolveInFlight.Add(reactionId);
        _reactionResolveQueue.Enqueue(chatVm.ChatId);
        if (!_reactionResolveDraining) StartCoroutine(DrainReactionResolveQueue());
    }

    private IEnumerator DrainReactionResolveQueue()
    {
        _reactionResolveDraining = true;
        string profileId = GetActiveProfileId();
        string cacheRoot = GetCacheRoot();

        while (_reactionResolveQueue.Count > 0)
        {
            // Abandon the queue if the active bot changed mid-drain.
            if (GetActiveProfileId() != profileId) break;

            string chatId = _reactionResolveQueue.Dequeue();
            ChatViewModel chatVm = GetChat(chatId);
            if (chatVm == null || chatVm.LastMessageType != "reaction"
                || chatVm.ReactionTargetText != null || string.IsNullOrEmpty(chatVm.LastMessageId))
                continue;

            string reactionId = chatVm.LastMessageId;
            if (string.IsNullOrEmpty(profileId)) { _reactionResolveInFlight.Remove(reactionId); continue; }

            string escapedId = UnityWebRequest.EscapeURL(chatId);
            string url = $"https://wappi.pro/api/sync/messages/get?profile_id={profileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset=0";

            bool definitive = false;
            ReactionTargetResolver.Result res = new ReactionTargetResolver.Result { text = "", type = "" };

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
                www.timeout = 30;
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var resp = JsonConvert.DeserializeObject<MessagesResponseRaw>(www.downloadHandler.text);
                        if (resp?.messages != null) res = ReactionTargetResolver.Resolve(resp.messages, reactionId);
                        definitive = true;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[ChatManager] reaction-target parse failed: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[Wappi] reaction-target messages/get failed [{www.responseCode}] {url}: {www.error}");
                }
            }

            _reactionResolveInFlight.Remove(reactionId);

            // Only cache/apply on a definitive answer; a network failure leaves the row
            // unresolved so it retries on a later on-screen bind.
            if (definitive)
            {
                ReactionTargetCache.Put(cacheRoot, reactionId, res.text, res.type, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (GetActiveProfileId() == profileId)
                {
                    ChatViewModel current = GetChat(chatId);
                    if (current != null && current.LastMessageType == "reaction"
                        && current.LastMessageId == reactionId)
                        current.UpdateReactionContext(res.text, res.type);
                }
            }
        }

        // Drained or aborted: drop stragglers so a new bot/list starts clean.
        _reactionResolveQueue.Clear();
        _reactionResolveInFlight.Clear();
        _reactionResolveDraining = false;
    }
}

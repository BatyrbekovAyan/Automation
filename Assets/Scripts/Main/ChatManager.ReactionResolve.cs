using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    // Lazily backfill chat-list row details for on-screen rows that are missing them:
    // a reaction's target text, and/or a group row's sender name (empty pushname for LID
    // participants). One serial messages/get per row resolves both; results are cached.
    private readonly Queue<string> _reactionResolveQueue = new Queue<string>();   // chatIds pending
    private readonly HashSet<string> _reactionResolveInFlight = new HashSet<string>(); // last-message ids
    private bool _reactionResolveDraining;

    /// <summary>
    /// Entry point called by ChatItemView when an on-screen row is missing detail. Cache hit →
    /// fills instantly; miss → enqueues one serial messages/get fetch for that chat.
    /// </summary>
    public void ResolveRowDetails(ChatViewModel chatVm)
    {
        if (chatVm == null) return;
        string id = chatVm.LastMessageId;
        if (string.IsNullOrEmpty(id) || !RowNeedsResolve(chatVm)) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (ReactionTargetCache.TryGet(GetCacheRoot(), id, now, out string cText, out string cType, out string cName))
        {
            ApplyRowDetails(chatVm, cText, cType, cName);
            return;
        }

        if (_reactionResolveInFlight.Contains(id)) return;
        _reactionResolveInFlight.Add(id);
        _reactionResolveQueue.Enqueue(chatVm.ChatId);
        if (!_reactionResolveDraining) StartCoroutine(DrainReactionResolveQueue());
    }

    // A row needs a fetch when it's a reaction whose target text is unresolved, or an incoming
    // group row whose sender name is still unknown (own rows render "You", 1:1 rows no prefix).
    private static bool RowNeedsResolve(ChatViewModel vm)
    {
        bool needsTarget = vm.LastMessageType == "reaction" && vm.ReactionTargetText == null;
        bool needsName = vm.IsGroup && !vm.IsLastMessageMine && string.IsNullOrEmpty(vm.LastMessageSenderName);
        return needsTarget || needsName;
    }

    private static void ApplyRowDetails(ChatViewModel vm, string text, string type, string senderName)
    {
        vm.ApplyResolvedRowDetails(text, type, senderName);
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
            if (chatVm == null || string.IsNullOrEmpty(chatVm.LastMessageId) || !RowNeedsResolve(chatVm))
                continue;

            string id = chatVm.LastMessageId;
            if (string.IsNullOrEmpty(profileId)) { _reactionResolveInFlight.Remove(id); continue; }

            string escapedId = UnityWebRequest.EscapeURL(chatId);
            string url = $"https://wappi.pro/api/sync/messages/get?profile_id={profileId}&chat_id={escapedId}&limit={MessagesPerPage}&offset=0";

            bool definitive = false;
            ReactionTargetResolver.Result res = new ReactionTargetResolver.Result { text = "", type = "", senderName = "" };

            // Never run this background messages/get while a chat-open/sync/pagination fetch is in
            // flight — Wappi crosses concurrent same-endpoint responses, which is what splices
            // another chat's messages into the open chat. Defer until the chat fetches drain.
            yield return WaitForChatFetchesToDrain();

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
                        if (resp?.messages != null) res = ReactionTargetResolver.Resolve(resp.messages, id);
                        definitive = true;
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[ChatManager] row-detail parse failed: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[Wappi] row-detail messages/get failed [{www.responseCode}] {url}: {www.error}");
                }
            }

            _reactionResolveInFlight.Remove(id);

            // Only cache/apply on a definitive answer; a network failure leaves the row
            // unresolved so it retries on a later on-screen bind.
            if (definitive)
            {
                ReactionTargetCache.Put(cacheRoot, id, res.text, res.type, res.senderName, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (GetActiveProfileId() == profileId)
                {
                    ChatViewModel current = GetChat(chatId);
                    if (current != null && current.LastMessageId == id)
                        ApplyRowDetails(current, res.text, res.type, res.senderName);
                }
            }
        }

        // Drained or aborted: drop stragglers so a new bot/list starts clean.
        _reactionResolveQueue.Clear();
        _reactionResolveInFlight.Clear();
        _reactionResolveDraining = false;
    }
}

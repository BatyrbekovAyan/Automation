using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public partial class ChatManager
{
    // Recovers the real text of a quoted (replied-to) message when its local preview is
    // unavailable — Wappi's reply_message snapshot was absent, or it echoed the reply's OWN body
    // (a confirmed server quirk) so ReplyParser blanked it. The reply_message id is reliable, so
    // we fetch the original by id via messages/id/get. One serial request per id; results (incl.
    // not-found) are cached in QuotedMessageCache so each id is fetched at most once per TTL.
    private readonly Queue<string> _quoteResolveQueue = new Queue<string>();      // quotedMessageIds pending
    private readonly HashSet<string> _quoteResolveInFlight = new HashSet<string>();
    private bool _quoteResolveDraining;

    // The exact VM instances waiting on each in-flight quotedMessageId. The fetch is front-loaded at
    // chat-open, so it can finish BEFORE the reply's bubble exists to receive the one-shot
    // OnMessageMediaRefreshed broadcast — and a bubble that binds mid-fetch hits the in-flight
    // early-return without re-arming. Registering the bound VM here guarantees the result reaches it
    // (and the right instance, even if _activeChatCache was reassigned/truncated since).
    private readonly Dictionary<string, List<MessageViewModel>> _quoteWaiters =
        new Dictionary<string, List<MessageViewModel>>();

    /// <summary>
    /// At chat open — BEFORE the bubbles render — start resolving every reply whose quoted preview
    /// is unavailable. Front-loading the fetches (rather than waiting for each bubble's first
    /// RenderQuotedCard) gives short fetches time to land in the cache so the first render hits it
    /// and shows the real quote immediately; longer ones still re-bind in place when they complete.
    /// A cache hit fills the VM here, so even a warm-cache reopen shows real text on first render.
    /// Safe to call repeatedly — deduped by cache + the in-flight set; non-replies are skipped.
    /// </summary>
    public void PrefetchUnavailableQuotes(List<MessageViewModel> messages)
    {
        if (messages == null) return;
        for (int i = 0; i < messages.Count; i++) ResolveQuotedMessage(messages[i]);
    }

    /// <summary>
    /// Entry point called by MessageItemView when a reply bubble has a quoted id but no preview
    /// text. A cache hit fills the VM in place immediately (so the bubble renders the real text in
    /// the same frame); a miss enqueues one serial fetch that re-binds the bubble on completion via
    /// <see cref="OnMessageMediaRefreshed"/>. Cheap to call on every bind — deduped by cache and
    /// the in-flight set.
    /// </summary>
    public void ResolveQuotedMessage(MessageViewModel vm)
    {
        if (vm == null || string.IsNullOrEmpty(vm.quotedMessageId) || !string.IsNullOrEmpty(vm.quotedText)) return;

        string qid = vm.quotedMessageId;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (QuotedMessageCache.TryGet(GetCacheRoot(), qid, now, out string cText, out string cSender, out MessageType cType))
        {
            // Cached hit. Non-empty text fills the VM now; an empty (not-found) entry means we
            // already tried and there's nothing to show — leave the placeholder, don't refetch.
            if (!string.IsNullOrEmpty(cText)) ApplyQuoteToVm(vm, cText, cSender, cType);
            return;
        }

        // Cache miss → this exact VM must receive the result when the (already in-flight or about-to-
        // start) fetch lands, regardless of whether its bubble is subscribed at broadcast time.
        AddQuoteWaiter(qid, vm);

        if (_quoteResolveInFlight.Contains(qid)) return; // already fetching — the waiter list gets it
        _quoteResolveInFlight.Add(qid);
        _quoteResolveQueue.Enqueue(qid);
        if (!_quoteResolveDraining) StartCoroutine(DrainQuoteResolveQueue());
    }

    private void AddQuoteWaiter(string qid, MessageViewModel vm)
    {
        if (!_quoteWaiters.TryGetValue(qid, out var list))
        {
            list = new List<MessageViewModel>();
            _quoteWaiters[qid] = list;
        }
        if (!list.Contains(vm)) list.Add(vm);
    }

    private IEnumerator DrainQuoteResolveQueue()
    {
        _quoteResolveDraining = true;
        string profileId = GetActiveProfileId();
        string cacheRoot = GetCacheRoot();

        while (_quoteResolveQueue.Count > 0)
        {
            // Abandon the queue if the active bot changed mid-drain.
            if (GetActiveProfileId() != profileId) break;

            string qid = _quoteResolveQueue.Dequeue();
            if (string.IsNullOrEmpty(profileId)) { _quoteResolveInFlight.Remove(qid); continue; }

            string url = $"https://wappi.pro/api/sync/messages/id/get?profile_id={profileId}&message_id={UnityWebRequest.EscapeURL(qid)}";

            bool definitive = false;
            string text = "", senderName = "";
            MessageType type = MessageType.Unknown;

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
                www.timeout = 30;
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var obj = JObject.Parse(www.downloadHandler.text);
                        if (obj["status"]?.ToString() == "done" && obj["message"] is JObject msg)
                        {
                            QuotedPreview q = ReplyParser.FromFetchedMessage(msg, ParseMessageType);
                            text = q.text ?? ""; senderName = q.senderName ?? ""; type = q.type;
                        }
                        definitive = true; // a valid response (even empty/not-found) — cache it to stop refetching
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[ChatManager] quote-by-id parse failed: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[Wappi] messages/id/get failed [{www.responseCode}] {url}: {www.error}");
                }
            }

            _quoteResolveInFlight.Remove(qid);

            // Only cache/apply on a definitive answer; a network failure leaves the id unresolved
            // so it retries on a later bind.
            if (definitive)
            {
                QuotedMessageCache.Put(cacheRoot, qid, text, senderName, type, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (GetActiveProfileId() == profileId && !string.IsNullOrEmpty(text))
                    ApplyResolvedQuote(qid, text, senderName, type);
                else
                    _quoteWaiters.Remove(qid); // not-found / stale bot — nothing to apply
            }
        }

        _quoteResolveQueue.Clear();
        _quoteResolveInFlight.Clear();
        _quoteWaiters.Clear();
        _quoteResolveDraining = false;
    }

    // Apply a freshly resolved quote to every VM registered as a waiter for this id — the exact
    // instances every bound bubble and the chat-open prefetch passed to ResolveQuotedMessage, so the
    // result reaches the right object even if _activeChatCache was reassigned/truncated since — then
    // re-bind each in place via the media-refresh event. Waiters are deduped on insert, so each VM
    // fires once. Bubbles that spawn AFTER resolution don't need this: they hit the now-warm cache
    // synchronously in ResolveQuotedMessage at bind time.
    private void ApplyResolvedQuote(string quotedId, string text, string senderName, MessageType type)
    {
        if (!_quoteWaiters.TryGetValue(quotedId, out var waiters)) return;

        foreach (var vm in waiters)
        {
            if (vm == null || !string.IsNullOrEmpty(vm.quotedText)) continue;
            ApplyQuoteToVm(vm, text, senderName, type);
            OnMessageMediaRefreshed?.Invoke(vm);
        }
        _quoteWaiters.Remove(quotedId);
    }

    private static void ApplyQuoteToVm(MessageViewModel vm, string text, string senderName, MessageType type)
    {
        vm.quotedText       = text;
        vm.quotedSenderName = senderName;
        vm.quotedType       = type;
    }
}

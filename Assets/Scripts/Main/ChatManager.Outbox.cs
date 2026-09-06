using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Outbox concerns split out of ChatManager — keeps the god-object trimmer
/// and groups related behavior. Mirrors the existing ChatManager.BotState.cs
/// partial split.
/// </summary>
public partial class ChatManager
{
    private OutboxStore _outbox;
    private readonly HashSet<string> _retriesInFlight = new();

    // ── Reply compose state ────────────────────────────────────────────────
    private MessageViewModel _replyTarget;

    /// <summary>Fires whenever the active reply target changes. Null payload == reply cancelled.</summary>
    public event System.Action<MessageViewModel> OnReplyTargetChanged;

    /// <summary>The message the next sent text will quote, or null. Read by SendTextMessageRoutine.</summary>
    public MessageViewModel ReplyTarget => _replyTarget;

    /// <summary>
    /// Begin replying to a message. Ignored for not-yet-Sent messages (decision D1):
    /// their id is still a temp id and cannot be quoted on the wire.
    /// </summary>
    public void BeginReply(MessageViewModel target)
    {
        if (target == null) return;
        if (target.deliveryStatus == DeliveryStatus.Pending || target.deliveryStatus == DeliveryStatus.Failed) return;
        _replyTarget = target;
        OnReplyTargetChanged?.Invoke(target);
    }

    public void CancelReply()
    {
        if (_replyTarget == null) return;
        _replyTarget = null;
        OnReplyTargetChanged?.Invoke(null);
    }
    // ── End reply compose state ────────────────────────────────────────────

    private OutboxStore Outbox => _outbox ??= new OutboxStore(GetCacheRoot);

    /// <summary>
    /// The message list an outgoing send must read-modify-write: <c>_activeChatCache</c> when the
    /// send belongs to the chat that is open on the bot it was sent from, otherwise a fresh disk
    /// load. Every send-path cache mutation goes through here.
    ///
    /// <para><b>This is the dedupe invariant.</b> <c>_activeChatCache</c> is the list the open-chat
    /// live poll hands to <c>SyncLatestMessages</c> as <c>cachedList</c>, so it is also the list
    /// <c>ReconcileGhostSend</c> searches for the optimistic bubble when the server echoes our own
    /// message back. Loading a detached list here instead (what the send path did until 2026-09-04)
    /// leaves the optimistic bubble invisible to that search: the reconcile reports "not
    /// recovered", the merge appends the echo as a SECOND bubble, and the send renders twice — ✓
    /// beside ✓✓ — until a reopen rebuilds the thread from disk. See <see cref="OutgoingSendCache"/>.</para>
    ///
    /// <para>Both halves of the guard are load-bearing. <paramref name="chatId"/> must be the open
    /// chat, and <paramref name="cacheRoot"/> must still be the ACTIVE bot's root — a send that
    /// completes after a bot switch carries the originating bot's snapshotted root, and
    /// <c>_activeChatCache</c> by then belongs to a different bot's chat. Falling back to the disk
    /// load in those cases is exactly today's behaviour, so nothing off the open chat changes.</para>
    /// </summary>
    private List<MessageViewModel> LiveCacheFor(string chatId, string cacheRoot)
    {
        // The guard itself is the pure OutgoingSendCache.UsesLiveList (pinned by tests);
        // this is only the delegate that feeds it live state.
        if (OutgoingSendCache.UsesLiveList(_activeChatCache != null, chatId, currentChatId, cacheRoot, GetCacheRoot()))
            return _activeChatCache;

        return ChatHistoryCache.LoadHistory(cacheRoot, chatId);
    }

    // Chats the owner deleted this session, keyed per cache root (a chat id can exist on
    // another bot / channel). See PersistSendCache.
    private readonly HashSet<string> _sendCacheDeleted = new HashSet<string>();

    private static string SendCacheKey(string cacheRoot, string chatId) => cacheRoot + "|" + chatId;

    /// <summary>
    /// The ONLY disk write the send paths make (text send + ack, media stage + ack + cancel;
    /// held to it by SendPathWiringTests). It refuses to write for a chat the owner has deleted
    /// (2026-09-05 review finding): a <c>message/send</c> ack routinely lands 1–3 s after the
    /// send, so «Удалить чат» can race it — and because LiveCacheFor still serves the deleted
    /// chat's live list (currentChatId is sticky), the ack's SaveHistory would have rewritten
    /// the chat's FULL history back onto disk after DeleteHistory removed it. The chat stays
    /// hidden (isDeleted), so the resurrected file is invisible in the UI and outlives the
    /// deletion the user explicitly asked for.
    /// </summary>
    private void PersistSendCache(string cacheRoot, string chatId, List<MessageViewModel> list)
    {
        if (_sendCacheDeleted.Contains(SendCacheKey(cacheRoot, chatId))) return;
        ChatHistoryCache.SaveHistory(cacheRoot, chatId, list);
    }

    private void MarkChatDeletedForSends(string cacheRoot, string chatId)
        => _sendCacheDeleted.Add(SendCacheKey(cacheRoot, chatId));

    private void UnmarkChatDeletedForSends(string cacheRoot, string chatId)
        => _sendCacheDeleted.Remove(SendCacheKey(cacheRoot, chatId));

    /// <summary>
    /// Re-fires the network half of a previously-failed send. No-op if the
    /// entry was never queued or a retry for the same tempId is already in
    /// flight — guards against rapid double-taps spawning duplicate POSTs.
    /// </summary>
    public void RetryOutboxMessage(string tempId)
    {
        if (string.IsNullOrEmpty(tempId)) return;
        if (!_retriesInFlight.Add(tempId)) return; // already retrying this id

        OutboxStore.OutboxEntry entry = Outbox.Find(tempId);
        if (entry == null)
        {
            _retriesInFlight.Remove(tempId);
            return;
        }

        entry.attemptCount++;
        Outbox.Update(entry);

        OnMessageStatusChanged?.Invoke(tempId, tempId, DeliveryStatus.Pending);

        MonoBehaviour runner = Manager.Instance != null ? (MonoBehaviour)Manager.Instance : this;
        runner.StartCoroutine(RetryRoutine(tempId, entry));
    }

    private IEnumerator RetryRoutine(string tempId, OutboxStore.OutboxEntry entry)
    {
        // Snapshot the cache root BEFORE any yield, mirroring SendTextMessageRoutine
        // so a same-frame bot switch can't redirect the retry's cache write to the
        // wrong bot's folder.
        string retryCacheRoot = GetCacheRoot();
        try
        {
            if (entry.kind == (int)OutboxKind.Media)
                yield return PostMediaMessageRoutine(entry, retryCacheRoot);
            else
                // Rebuild the send URL from the entry's snapshotted channel (legacy
                // entries default to WhatsApp) so a cross-session retry hits the right base.
                yield return PostTextMessageRoutine(entry.chatId, entry.text, tempId, entry.profileId, retryCacheRoot, entry.quotedMessageId, (ChatChannel)entry.channel);
        }
        finally
        {
            _retriesInFlight.Remove(tempId);
        }
    }
}

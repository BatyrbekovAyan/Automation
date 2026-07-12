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

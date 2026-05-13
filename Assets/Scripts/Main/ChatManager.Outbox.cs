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
        // (line 694) so a same-frame bot switch can't redirect the retry's
        // cache write to the wrong bot's folder.
        string retryCacheRoot = GetCacheRoot();
        try
        {
            yield return PostTextMessageRoutine(entry.chatId, entry.text, tempId, entry.profileId, retryCacheRoot);
        }
        finally
        {
            _retriesInFlight.Remove(tempId);
        }
    }
}

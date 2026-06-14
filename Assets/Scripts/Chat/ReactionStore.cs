using System.Collections.Generic;

/// <summary>
/// Reduces reaction events into per-message reaction state. Reactions arrive
/// before their (older) target during newest-first pagination, so events whose
/// target isn't loaded are buffered per (target, reactor) — latest wins — and
/// drained when the target message later enters the list. One ReactionStore per
/// open chat; Clear() on chat switch.
/// </summary>
public class ReactionStore
{
    // targetMessageId -> (reactorKey -> latest buffered event)
    private readonly Dictionary<string, Dictionary<string, ReactionEvent>> _pending
        = new Dictionary<string, Dictionary<string, ReactionEvent>>();

    /// <summary>
    /// Apply an event to its target in <paramref name="messages"/>. Returns the mutated
    /// target VM when state actually changed (caller fires OnMessageReactionsChanged +
    /// marks the cache dirty), or null when the target wasn't found (buffered) or the
    /// event was a no-op (idempotent re-delivery).
    /// </summary>
    public MessageViewModel Apply(ReactionEvent ev, IReadOnlyList<MessageViewModel> messages)
    {
        if (ev == null || string.IsNullOrEmpty(ev.targetId)) return null;

        var target = FindById(messages, ev.targetId);
        if (target != null)
            return ApplyToMessage(target, ev) ? target : null;

        Buffer(ev);
        return null;
    }

    /// <summary>
    /// Apply any buffered events targeting <paramref name="message"/>. Returns true if
    /// the message's reactions changed. Called right after a fresh VM is created from a
    /// server page, so a reaction seen earlier in the same (or a newer) page lands.
    /// </summary>
    public bool DrainInto(MessageViewModel message)
    {
        if (message == null || string.IsNullOrEmpty(message.messageId)) return false;
        if (!_pending.TryGetValue(message.messageId, out var byReactor)) return false;

        bool changed = false;
        foreach (var ev in byReactor.Values)
            changed |= ApplyToMessage(message, ev);

        _pending.Remove(message.messageId);
        return changed;
    }

    public void Clear() => _pending.Clear();

    /// <summary>
    /// Pure set/replace/remove of one reactor's reaction on a message. Returns true if
    /// the reactions list changed. Idempotent: re-applying the same emoji is a no-op.
    /// </summary>
    public static bool ApplyToMessage(MessageViewModel message, ReactionEvent ev)
    {
        if (message == null || ev == null) return false;
        message.reactions ??= new List<MessageReaction>();

        int idx = message.reactions.FindIndex(r => r.reactorKey == ev.reactorKey);

        if (ev.IsRemoval)
        {
            if (idx < 0) return false;
            message.reactions.RemoveAt(idx);
            return true;
        }

        if (idx >= 0)
        {
            var existing = message.reactions[idx];
            if (existing.emoji == ev.emoji) return false;   // idempotent re-delivery
            existing.emoji = ev.emoji;
            existing.time = ev.time;
            existing.senderName = ev.senderName;
            existing.fromMe = ev.fromMe;
            return true;
        }

        message.reactions.Add(new MessageReaction
        {
            emoji = ev.emoji,
            reactorKey = ev.reactorKey,
            senderName = ev.senderName,
            fromMe = ev.fromMe,
            time = ev.time
        });
        return true;
    }

    private void Buffer(ReactionEvent ev)
    {
        if (!_pending.TryGetValue(ev.targetId, out var byReactor))
        {
            byReactor = new Dictionary<string, ReactionEvent>();
            _pending[ev.targetId] = byReactor;
        }
        byReactor[ev.reactorKey] = ev;   // latest wins
    }

    private static MessageViewModel FindById(IReadOnlyList<MessageViewModel> messages, string id)
    {
        if (messages == null) return null;
        for (int i = 0; i < messages.Count; i++)
            if (messages[i] != null && messages[i].messageId == id) return messages[i];
        return null;
    }
}

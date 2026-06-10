using System;
using System.Collections.Generic;

/// <summary>
/// Canonical conversation ordering for messages. Wappi timestamps are unix
/// SECONDS, and rapid exchanges routinely put several messages in the same
/// second — a timestamp-only sort scrambles them (and List&lt;T&gt;.Sort is
/// unstable, so the scramble differs between reopens). Ordering here is a
/// composite key: (timestamp, within-second sequence, messageId), which is a
/// total order, so every sort is deterministic and matches the server's
/// conversation order for same-second ties.
/// </summary>
public static class MessageOrder
{
    /// <summary>Times of a server response (newest-first), in response order.</summary>
    public static long[] ResponseTimes(IReadOnlyList<RawMessage> messages)
    {
        var times = new long[messages.Count];
        for (int i = 0; i < messages.Count; i++) times[i] = messages[i].time;
        return times;
    }

    /// <summary>
    /// Within-second order of the message at <paramref name="index"/> in a
    /// newest-first response: how many same-second messages sit BELOW it
    /// (= are older). Counting from the oldest member of the tie group keeps
    /// the value stable when a later, overlapping fetch window sees the same
    /// group again with newer messages prepended above it.
    /// </summary>
    public static int WithinSecondSequence(IReadOnlyList<long> responseTimes, int index)
    {
        long time = responseTimes[index];
        int olderTies = 0;
        for (int i = index + 1; i < responseTimes.Count; i++)
        {
            if (responseTimes[i] == time) olderTies++;
        }
        return olderTies;
    }

    /// <summary>Ascending conversation order (oldest first).</summary>
    public static int Compare(MessageViewModel a, MessageViewModel b)
    {
        int byTime = a.timestamp.CompareTo(b.timestamp);
        if (byTime != 0) return byTime;

        int bySequence = a.sequence.CompareTo(b.sequence);
        if (bySequence != 0) return bySequence;

        return string.CompareOrdinal(a.messageId, b.messageId);
    }

    public static readonly Comparison<MessageViewModel> Ascending = Compare;

    public static readonly Comparison<MessageViewModel> Descending = (a, b) => Compare(b, a);

    public static readonly IComparer<MessageViewModel> AscendingComparer =
        Comparer<MessageViewModel>.Create(Ascending);

    /// <summary>
    /// Where a live arrival belongs among already-rendered bubbles (ascending
    /// order): index of the first existing message that sorts strictly after
    /// it, or -1 when it belongs at the end (the common append case).
    /// </summary>
    public static int InsertIndex(IReadOnlyList<MessageViewModel> existingInOrder, MessageViewModel incoming)
    {
        for (int i = 0; i < existingInOrder.Count; i++)
        {
            if (Compare(incoming, existingInOrder[i]) < 0) return i;
        }
        return -1;
    }
}

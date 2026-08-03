using System;
using System.Collections.Generic;

/// <summary>
/// Pure, unit-testable ordering seam for the chat list, used by ChatListView's resort
/// pass. Order derives from data — last-message time, newest first — never from event
/// firing order (the old per-row insert-at-top merge reversed every chat that changed
/// within one sync pass). Extracted per the ChatDialogTime/CrossChatResponseGuard
/// pure-seam precedent.
/// </summary>
public static class ChatListOrder
{
    /// <summary>
    /// Returns the rows reordered by last-message time descending. Ties — including
    /// unknown (zero) times, which therefore sink below every dated row — keep their
    /// current relative order, so a resort never churns rows the data can't rank.
    /// </summary>
    public static List<T> Apply<T>(IReadOnlyList<T> rowsInVisualOrder, Func<T, long> lastMessageTime)
    {
        // List<T>.Sort is unstable, so decorate with the current index and use it
        // as the tiebreaker — that's what keeps equal/zero times in visual order.
        var decorated = new List<(T row, long time, int index)>(rowsInVisualOrder.Count);
        for (int i = 0; i < rowsInVisualOrder.Count; i++)
            decorated.Add((rowsInVisualOrder[i], lastMessageTime(rowsInVisualOrder[i]), i));

        decorated.Sort((a, b) =>
        {
            int byTimeDesc = b.time.CompareTo(a.time);
            return byTimeDesc != 0 ? byTimeDesc : a.index.CompareTo(b.index);
        });

        var result = new List<T>(decorated.Count);
        foreach (var entry in decorated) result.Add(entry.row);
        return result;
    }
}

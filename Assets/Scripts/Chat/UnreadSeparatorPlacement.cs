using System.Collections.Generic;

/// <summary>
/// Pure placement math for the "N unread" separator. The message list is laid out
/// newest-at-bottom, so callers pass bubble incoming-flags newest-first (index 0 = newest).
/// Returns the number of bubbles that fall BELOW the separator (on the newer side): the
/// separator is positioned immediately above the Nth-newest incoming message, so all N
/// unread incoming messages — plus any newer outgoing ones — sit below it. When fewer than
/// N incoming messages are loaded, the separator goes above everything (returns the full
/// count). n &lt;= 0 or a null/empty list returns 0 (caller draws no separator).
/// </summary>
public static class UnreadSeparatorPlacement
{
    public static int IndexForUnreadCount(IReadOnlyList<bool> isIncomingNewestFirst, int n)
    {
        if (isIncomingNewestFirst == null || n <= 0) return 0;

        int incomingSeen = 0;
        for (int i = 0; i < isIncomingNewestFirst.Count; i++)
        {
            if (isIncomingNewestFirst[i]) incomingSeen++;
            if (incomingSeen == n) return i + 1;
        }

        return isIncomingNewestFirst.Count;
    }
}

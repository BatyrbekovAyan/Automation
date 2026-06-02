using System.Collections.Generic;

/// <summary>
/// Pure math for the scroll-to-bottom badge: how many tracked unread bubbles are still
/// below the fold (i.e. the user has not scrolled down to them). World Y increases upward,
/// so a bubble is below the fold when its top edge (world Y) is strictly below the
/// viewport's bottom edge. A bubble whose top edge sits exactly on the fold counts as
/// visible (not below). Order-independent; pass the top-edge world Y of each tracked bubble.
/// </summary>
public static class ScrollFabMath
{
    public static int CountBelowFold(IReadOnlyList<float> bubbleTopWorldY, float viewportBottomWorldY)
    {
        if (bubbleTopWorldY == null) return 0;

        int count = 0;
        for (int i = 0; i < bubbleTopWorldY.Count; i++)
        {
            if (bubbleTopWorldY[i] < viewportBottomWorldY) count++;
        }
        return count;
    }
}

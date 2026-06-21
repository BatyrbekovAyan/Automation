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

    /// <summary>
    /// Hysteresis decision for the scroll-to-bottom FAB's visibility. <paramref name="gapPx"/> is
    /// the absolute pixel distance the list is scrolled up from the newest message (the world gap
    /// between the content's bottom edge and the viewport bottom; >0 = scrolled up, &lt;=0 = at the
    /// bottom). Show once that gap reaches <paramref name="showGapPx"/>, hide once it drops back to
    /// <paramref name="hideGapPx"/>; inside the dead-band, hold the current state. This is fed an
    /// ABSOLUTE gap, never verticalNormalizedPosition — that ratio's denominator is the content
    /// height, so a newest-area image bubble growing on download shifted it across a fixed
    /// threshold and flickered the FAB. Expects hideGapPx &lt;= showGapPx; equal thresholds collapse
    /// to a plain >= threshold with no dead-band.
    /// </summary>
    public static bool ShouldShow(bool currentlyShown, float gapPx, float showGapPx, float hideGapPx)
    {
        if (gapPx >= showGapPx) return true;
        if (gapPx <= hideGapPx) return false;
        return currentlyShown; // dead-band: image relayout / sub-pixel jitter can't toggle
    }
}

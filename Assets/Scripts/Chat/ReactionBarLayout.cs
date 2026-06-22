using UnityEngine;

/// <summary>
/// Pure layout math for the long-press reaction overlay (quick-emoji bar + Reply/Copy/Forward
/// action menu). All values are in the overlay parent's local space, where Y increases upward
/// and x=0 is the parent's horizontal center. Kept side-effect free so it can be unit-tested
/// without a live RectTransform — mirrors <see cref="ScrollFabMath"/>.
/// </summary>
public static class ReactionBarLayout
{
    /// <summary>
    /// Horizontal anchored-x for a floating panel so it lines up with the message's side —
    /// the panel's LEFT edge meets the bubble's left edge for an incoming message, its RIGHT
    /// edge meets the bubble's right edge for an outgoing one — then clamped so the panel keeps
    /// <paramref name="edgePadding"/> from both screen edges. This replaces centering the panel
    /// on the bubble's center, which left the narrow action menu floating off to one side.
    /// </summary>
    /// <param name="bubbleLeftX">Bubble left-edge x (parent-local).</param>
    /// <param name="bubbleRightX">Bubble right-edge x (parent-local).</param>
    /// <param name="panelWidth">Width of the floating panel (bar or menu).</param>
    /// <param name="parentWidth">Width of the overlay parent.</param>
    /// <param name="edgePadding">Minimum inset the panel keeps from each screen edge.</param>
    /// <param name="isIncoming">True = left-aligned bubble; false = right-aligned bubble.</param>
    public static float SideAlignedCenterX(
        float bubbleLeftX, float bubbleRightX,
        float panelWidth, float parentWidth, float edgePadding, bool isIncoming)
    {
        float halfPanel = panelWidth * 0.5f;
        // How far the panel center may sit from screen-center before an edge breaks the padding.
        // Never negative: a panel wider than the safe span just centers.
        float maxCenter = Mathf.Max(0f, parentWidth * 0.5f - halfPanel - edgePadding);

        float center = isIncoming
            ? bubbleLeftX + halfPanel    // panel's left edge on the bubble's left edge
            : bubbleRightX - halfPanel;  // panel's right edge on the bubble's right edge

        return Mathf.Clamp(center, -maxCenter, maxCenter);
    }

    /// <summary>
    /// How far up (>= 0) the pressed bubble must float so the action menu fits fully below it,
    /// instead of the menu being clamped up over the bubble. Returns the lift needed to seat the
    /// menu at <paramref name="bottomLimitCenterY"/>, but never more than the room the bar has
    /// above the bubble before it would clip <paramref name="topLimitCenterY"/> — so raising the
    /// message to clear the bottom can't push the bar off the top. 0 when the menu already fits.
    /// </summary>
    /// <param name="bubbleTopCenterY">Bubble top-center y (parent-local).</param>
    /// <param name="bubbleBottomCenterY">Bubble bottom-center y (parent-local).</param>
    /// <param name="barHeight">Height of the quick-emoji bar (sits above the bubble).</param>
    /// <param name="menuHeight">Height of the action menu (sits below the bubble).</param>
    /// <param name="gap">Vertical gap between the bubble and each panel.</param>
    /// <param name="topLimitCenterY">Highest allowed bar center y (just below the screen top).</param>
    /// <param name="bottomLimitCenterY">Lowest allowed menu center y (just above the screen bottom).</param>
    public static float LiftToFitMenu(
        float bubbleTopCenterY, float bubbleBottomCenterY,
        float barHeight, float menuHeight, float gap,
        float topLimitCenterY, float bottomLimitCenterY)
    {
        float menuCenterY = bubbleBottomCenterY - gap - menuHeight * 0.5f;
        float needed = bottomLimitCenterY - menuCenterY;   // > 0 when the menu overflows below
        if (needed <= 0f) return 0f;

        float barCenterY = bubbleTopCenterY + gap + barHeight * 0.5f;
        float maxLift = Mathf.Max(0f, topLimitCenterY - barCenterY);

        return Mathf.Min(needed, maxLift);
    }
}

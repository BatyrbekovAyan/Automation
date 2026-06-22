using NUnit.Framework;

public class ReactionBarLayoutTests
{
    // Reference geometry: 1080-wide overlay, 40 edge padding, bubbles inset to the edges.
    private const float ParentW = 1080f;
    private const float Edge = 40f;

    // ---- SideAlignedCenterX -------------------------------------------------

    [Test]
    public void SideAligned_Incoming_PutsLeftEdgeOnBubbleLeft()
    {
        // Incoming bubble left edge at -500; a 520-wide menu should center at -500 + 260.
        float x = ReactionBarLayout.SideAlignedCenterX(-500f, 0f, 520f, ParentW, Edge, true);
        Assert.AreEqual(-240f, x, 0.01f);
    }

    [Test]
    public void SideAligned_Outgoing_PutsRightEdgeOnBubbleRight()
    {
        // Outgoing bubble right edge at +500; a 520-wide menu should center at +500 - 260.
        float x = ReactionBarLayout.SideAlignedCenterX(0f, 500f, 520f, ParentW, Edge, false);
        Assert.AreEqual(240f, x, 0.01f);
    }

    [Test]
    public void SideAligned_Incoming_ClampsToLeftPadding()
    {
        // Bubble pushed past the safe span — panel center clamps to -maxCenter (= -240 here).
        float x = ReactionBarLayout.SideAlignedCenterX(-540f, -20f, 520f, ParentW, Edge, true);
        float maxCenter = ParentW * 0.5f - 260f - Edge; // 240
        Assert.AreEqual(-maxCenter, x, 0.01f);
    }

    [Test]
    public void SideAligned_Outgoing_ClampsToRightPadding()
    {
        float x = ReactionBarLayout.SideAlignedCenterX(20f, 540f, 520f, ParentW, Edge, false);
        float maxCenter = ParentW * 0.5f - 260f - Edge; // 240
        Assert.AreEqual(maxCenter, x, 0.01f);
    }

    [Test]
    public void SideAligned_WideBar_StaysNearCenterBothSides()
    {
        // A near-full-width bar (980) lands ~symmetric regardless of side — no regression for the
        // emoji bar, which already read as "aligned" because it nearly spans the screen.
        float maxCenter = ParentW * 0.5f - 490f - Edge; // 10
        float incoming = ReactionBarLayout.SideAlignedCenterX(-500f, 20f, 980f, ParentW, Edge, true);
        float outgoing = ReactionBarLayout.SideAlignedCenterX(-20f, 500f, 980f, ParentW, Edge, false);
        Assert.AreEqual(-maxCenter, incoming, 0.01f);
        Assert.AreEqual(maxCenter, outgoing, 0.01f);
    }

    [Test]
    public void SideAligned_PanelWiderThanSafeSpan_Centers()
    {
        // Panel wider than (parent - 2*padding): maxCenter floors at 0, so it centers.
        float x = ReactionBarLayout.SideAlignedCenterX(-500f, 500f, 1100f, ParentW, Edge, true);
        Assert.AreEqual(0f, x, 0.01f);
    }

    // ---- LiftToFitMenu ------------------------------------------------------

    [Test]
    public void Lift_MenuAlreadyFits_IsZero()
    {
        // Bubble high on screen: menu center (~-196) sits well above the bottom limit (-900).
        float lift = ReactionBarLayout.LiftToFitMenu(
            bubbleTopCenterY: 100f, bubbleBottomCenterY: 0f,
            barHeight: 150f, menuHeight: 360f, gap: 16f,
            topLimitCenterY: 900f, bottomLimitCenterY: -900f);
        Assert.AreEqual(0f, lift, 0.01f);
    }

    [Test]
    public void Lift_MenuOverflowsBottom_RaisesByOverflow()
    {
        // Bubble low: menu center would be -996, must rise to -900 → lift 96.
        float lift = ReactionBarLayout.LiftToFitMenu(
            bubbleTopCenterY: -650f, bubbleBottomCenterY: -800f,
            barHeight: 150f, menuHeight: 360f, gap: 16f,
            topLimitCenterY: 900f, bottomLimitCenterY: -900f);
        Assert.AreEqual(96f, lift, 0.01f);
    }

    [Test]
    public void Lift_CappedSoBarNeverClipsTop()
    {
        // Bubble both very low AND very high (tiny viewport): bar has no top room → no lift,
        // rather than shoving the bar off the top of the screen.
        float lift = ReactionBarLayout.LiftToFitMenu(
            bubbleTopCenterY: 850f, bubbleBottomCenterY: -800f,
            barHeight: 150f, menuHeight: 360f, gap: 16f,
            topLimitCenterY: 900f, bottomLimitCenterY: -900f);
        Assert.AreEqual(0f, lift, 0.01f);
    }

    [Test]
    public void Lift_IsNeverNegative()
    {
        float lift = ReactionBarLayout.LiftToFitMenu(
            bubbleTopCenterY: 300f, bubbleBottomCenterY: 200f,
            barHeight: 150f, menuHeight: 360f, gap: 16f,
            topLimitCenterY: 900f, bottomLimitCenterY: -900f);
        Assert.GreaterOrEqual(lift, 0f);
    }
}

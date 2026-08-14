using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Guards the child order of MessagesPanel that makes the chat screen's back-swipe reachable.
///
/// uGUI awards a pointer to the LATER sibling. The strip shipped as a MovingArea child, BELOW the
/// composer (BottomPanel, a later sibling of it inside MovingArea) and BELOW the suggestions slot
/// (SuggestionsPanel, a later sibling of MovingArea itself) — so a back-swipe that began on either
/// of them never reached SwipeToBack at all. That is a pure ordering fact, which is the only part
/// of a raycast an EditMode test can assert: an unrendered canvas leaves Graphic.depth at -1 and
/// GraphicRaycaster returns nothing, so the hit walk itself cannot be exercised here.
///
/// The strip also has to stop below the chrome: TopBar and the modal overlays own gestures of
/// their own (header controls, SwipeToClose on the media viewers, the emoji and reaction sheets)
/// and a strip above them would swallow the left edge of all of it.
/// </summary>
public class SwipeBackLayeringTests
{
    // MessagesPanel's shipped layout: content first, then chrome, then the overlays.
    private const int MovingArea = 0;
    private const int SuggestionsPanel = 1;
    private const int TopBar = 2;

    [Test]
    public void StripInsideMovingArea_IsBelowBothSurfacesTheSwipeMustWorkOver()
    {
        // The bug as shipped: whatever its index INSIDE MovingArea, the strip is part of layer 0
        // and can never out-order layer 1.
        Assert.IsFalse(SwipeBackLayering.OutranksContent(MovingArea, MovingArea, SuggestionsPanel),
            "A strip parented to MovingArea cannot out-order the suggestions panel.");
    }

    [Test]
    public void TargetIndex_LandsAboveEveryContentLayer()
    {
        var target = SwipeBackLayering.TargetSiblingIndex(MovingArea, SuggestionsPanel);

        Assert.AreEqual(2, target);
        Assert.IsTrue(SwipeBackLayering.OutranksContent(target, MovingArea, SuggestionsPanel));
    }

    [Test]
    public void TargetIndex_StaysBelowTheChromeAndOverlays()
    {
        var target = SwipeBackLayering.TargetSiblingIndex(MovingArea, SuggestionsPanel);

        // Placing the strip at `target` pushes every later sibling — the chrome included — one
        // index down, so the rule is about where the top bar ENDS UP. Checking it against the
        // pre-move index reads this correct placement as a collision.
        const int topBarAfterInsertion = TopBar + 1;

        Assert.IsTrue(SwipeBackLayering.StaysBelowChrome(target, topBarAfterInsertion),
            "The strip must never render over the top bar or the modal overlays.");
    }

    // The other side of that rule: a strip that genuinely shares the chrome's index is a
    // collision, and the wirer has to refuse it rather than shadow a media viewer's gestures.
    [Test]
    public void StripAtTheChromesIndex_IsRefused()
    {
        Assert.IsFalse(SwipeBackLayering.StaysBelowChrome(stripIndex: 3, lowestChromeIndex: 3));
    }

    // A scene whose suggestions panel has not been built yet still has to come out correct — the
    // panel is created by its own builder, and this wirer must not depend on having run after it.
    [Test]
    public void NoSuggestionsPanelYet_StripStillLandsAboveTheComposer()
    {
        var target = SwipeBackLayering.TargetSiblingIndex(MovingArea, SwipeBackLayering.NotPresent);

        Assert.AreEqual(1, target);
        Assert.IsTrue(SwipeBackLayering.OutranksContent(target, MovingArea, SwipeBackLayering.NotPresent));
    }

    // SuggestionsPanelBuilder re-inserts the panel at MovingArea + 1 on every rebuild, pushing the
    // strip one index up. The ordering has to survive that without a re-run.
    [Test]
    public void PanelRebuildPushingTheStripUp_KeepsItAboveThePanel()
    {
        const int panelAfterRebuild = 1;
        const int stripAfterRebuild = 2;

        Assert.IsTrue(
            SwipeBackLayering.OutranksContent(stripAfterRebuild, MovingArea, panelAfterRebuild),
            "A panel rebuild must not be able to bury the strip again.");
    }

    /// <summary>
    /// The same contract on a real hierarchy, so a Transform-level mistake (reparenting into
    /// MovingArea rather than MessagesPanel) is caught and not just the arithmetic.
    /// </summary>
    [Test]
    public void OnARealHierarchy_TheStripOutOrdersComposerAndPanel()
    {
        var messagesPanel = new GameObject("MessagesPanel").transform;
        try
        {
            var movingArea = Child(messagesPanel, "MovingArea");
            Child(movingArea, "BottomPanel");                   // the composer, inside MovingArea
            var panel = Child(messagesPanel, "SuggestionsPanel");
            var topBar = Child(messagesPanel, "TopBar");
            var strip = Child(messagesPanel, "SwipeBack");       // appended last by a reparent

            strip.SetSiblingIndex(SwipeBackLayering.TargetSiblingIndex(
                movingArea.GetSiblingIndex(), panel.GetSiblingIndex()));

            Assert.IsTrue(SwipeBackLayering.OutranksContent(
                strip.GetSiblingIndex(), movingArea.GetSiblingIndex(), panel.GetSiblingIndex()));
            Assert.Less(strip.GetSiblingIndex(), topBar.GetSiblingIndex(),
                "The top bar must keep rendering above the strip.");
        }
        finally
        {
            Object.DestroyImmediate(messagesPanel.gameObject);
        }
    }

    // --- Band geometry -----------------------------------------------------

    // The band as it shipped: anchoredX 100, width 150, pivot .5 — seated 25u short of the screen
    // edge, which is exactly where an iOS-style edge pan starts. The strip is the only thing that
    // can begin a back-swipe, so a finger landing on the bezel fell into a dead band.
    private const float ShippedX = 100f, ShippedWidth = 150f, Pivot = 0.5f;

    [Test]
    public void ShippedBand_LeavesADeadStripAtTheScreenEdge()
    {
        Assert.AreEqual(25f, SwipeBackLayering.BandLeftEdge(ShippedX, ShippedWidth, Pivot), 0.001f);
    }

    [Test]
    public void EdgeAlignedBand_ReachesTheScreenEdge()
    {
        SwipeBackLayering.EdgeAlignedBand(ShippedX, ShippedWidth, Pivot, out var x, out var width);

        Assert.AreEqual(0f, SwipeBackLayering.BandLeftEdge(x, width, Pivot), 0.001f);
        Assert.AreEqual(87.5f, x, 0.001f);
        Assert.AreEqual(175f, width, 0.001f);
    }

    // Widening must only ever reach LEFT. Moving the right edge further in would newly shadow
    // composer controls (the «+» button already sits under the band at x 40–94).
    [Test]
    public void EdgeAlignedBand_KeepsTheRightEdgeExactlyWhereItWas()
    {
        var before = SwipeBackLayering.BandRightEdge(ShippedX, ShippedWidth, Pivot);
        SwipeBackLayering.EdgeAlignedBand(ShippedX, ShippedWidth, Pivot, out var x, out var width);

        Assert.AreEqual(before, SwipeBackLayering.BandRightEdge(x, width, Pivot), 0.001f);
    }

    // The wirer re-runs after every suggestions-panel rebuild, so a second pass must be a no-op.
    [Test]
    public void EdgeAlignedBand_IsIdempotent()
    {
        SwipeBackLayering.EdgeAlignedBand(ShippedX, ShippedWidth, Pivot, out var x1, out var w1);
        SwipeBackLayering.EdgeAlignedBand(x1, w1, Pivot, out var x2, out var w2);

        Assert.AreEqual(x1, x2, 0.001f);
        Assert.AreEqual(w1, w2, 0.001f);
    }

    // Pivot is read, never assumed: a band authored with a left pivot has to align just as well.
    [Test]
    public void EdgeAlignedBand_HonoursANonCentredPivot()
    {
        SwipeBackLayering.EdgeAlignedBand(anchoredX: 25f, width: 150f, pivotX: 0f,
                                          out var x, out var width);

        Assert.AreEqual(0f, SwipeBackLayering.BandLeftEdge(x, width, 0f), 0.001f);
        Assert.AreEqual(175f, SwipeBackLayering.BandRightEdge(x, width, 0f), 0.001f);
    }

    private static Transform Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        return go.transform;
    }
}

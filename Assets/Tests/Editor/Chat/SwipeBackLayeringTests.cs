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

    private static Transform Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        return go.transform;
    }
}

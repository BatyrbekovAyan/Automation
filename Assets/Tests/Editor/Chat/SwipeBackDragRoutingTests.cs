using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Guards where a NON-horizontal drag that starts on the chat screen's left-edge back-swipe strip
/// is sent.
///
/// The strip renders above the thread, the composer and the suggestions slot, so nothing under it
/// ever sees the gesture and SwipeToBack alone decides the owner. It used to forward every
/// non-horizontal drag straight to the message ScrollRect, which was invisible while the strip
/// covered the thread alone — and became wrong the moment the strip was raised over the slot, at
/// which point a vertical drag on the suggestion cards would have scrolled the message list
/// behind them.
///
/// The raycast walk itself is not testable in EditMode (an unrendered canvas leaves Graphic.depth
/// at -1, so GraphicRaycaster returns no hits at all — see the Bot Settings routing note), which
/// is precisely why the decision was extracted into <see cref="SwipeBackDragRouting"/>.
/// </summary>
public class SwipeBackDragRoutingTests
{
    [Test]
    public void NoHitsAtAll_FallsBackToTheThread()
    {
        // Unwired or degenerate scene — and the EditMode case. Silently owning the gesture and
        // dropping it would kill list scrolling in the whole band.
        Assert.AreEqual(SwipeBackDragRouting.VerticalTarget.ThreadFallback,
            SwipeBackDragRouting.Resolve(hasForeignHit: false, topHitOwnsADragGesture: false));
    }

    [Test]
    public void HitThatOwnsADrag_GetsTheGesture()
    {
        // The cards' ScrollRect, the slot's 42u grab handle, a sheet's drag zone, the thread.
        Assert.AreEqual(SwipeBackDragRouting.VerticalTarget.UnderFinger,
            SwipeBackDragRouting.Resolve(hasForeignHit: true, topHitOwnsADragGesture: true));
    }

    // The regression this whole seam exists for. uGUI's RaycastAll has NO occlusion: the thread
    // sits in the hit list underneath the composer's opaque background whether or not the user can
    // see it. Searching past a surface that owns no drag would therefore make a vertical drag on
    // the composer bar scroll a thread the finger is not touching.
    [Test]
    public void HitWithNoDragOfItsOwn_ScrollsNothing_NotTheThreadBehindIt()
    {
        Assert.AreEqual(SwipeBackDragRouting.VerticalTarget.None,
            SwipeBackDragRouting.Resolve(hasForeignHit: true, topHitOwnsADragGesture: false));
    }

    /// <summary>
    /// A back-swipe freezes vertical scrolling at drag-begin so nothing free-scrolls underneath
    /// it. A gesture interrupted by the chat closing never gets its OnEndDrag, so the freeze has
    /// to be released on disable too — otherwise the resolved target, which can be an object that
    /// OUTLIVES this screen (the suggestions panel), comes back unscrollable.
    ///
    /// Invoked by reflection: SwipeToBack is not [ExecuteAlways], so Unity does not run its
    /// lifecycle callbacks in EditMode (same idiom as InputFieldHideCaretTests).
    /// </summary>
    [Test]
    public void InterruptedBackSwipe_DoesNotLeaveTheThreadFrozen()
    {
        var root = new GameObject("SwipeBack");
        try
        {
            var scroll = new GameObject("Scroll").AddComponent<ScrollRect>();
            var swipe = root.AddComponent<SwipeToBack>();
            swipe.chatScrollRect = scroll;

            scroll.vertical = false;   // the state a committed back-swipe leaves behind

            var onDisable = typeof(SwipeToBack).GetMethod(
                "OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(onDisable, "SwipeToBack.OnDisable is gone — the freeze can now leak.");
            onDisable.Invoke(swipe, null);

            Assert.IsTrue(scroll.vertical,
                "OnDisable must release the vertical freeze, or a chat closed mid-swipe leaves it on.");

            Object.DestroyImmediate(scroll.gameObject);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}

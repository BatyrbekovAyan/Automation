using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
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
///
/// The last two tests guard the other half of the strip's contract: every piece of state a
/// back-swipe raises at drag-begin has to come back down on disable, because a gesture the chat
/// closing interrupts never reaches OnEndDrag.
/// </summary>
public class SwipeBackDragRoutingTests
{
    /// <summary>
    /// IsSliding is static and its setter is private, so a test that leaves it raised would
    /// silently change what every later test sees.
    /// </summary>
    [TearDown]
    public void ReleaseStaticState()
    {
        typeof(SwipeToBack)
            .GetProperty("IsSliding", BindingFlags.Static | BindingFlags.Public)
            ?.GetSetMethod(nonPublic: true)
            ?.Invoke(null, new object[] { false });
        SwipeToBack.Instance = null;   // Awake stamps it; ours is destroyed by then
    }

    /// <summary>
    /// SwipeToBack is not [ExecuteAlways], so Unity runs none of its lifecycle callbacks in
    /// EditMode — they have to be called by hand (same idiom as InputFieldHideCaretTests).
    /// </summary>
    private static void InvokeLifecycle(SwipeToBack target, string method)
    {
        var callback = typeof(SwipeToBack).GetMethod(
            method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(callback, $"SwipeToBack.{method} is gone — the state it released can now leak.");
        callback.Invoke(target, null);
    }

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

    /// <summary>
    /// The same interruption, the other piece of state it strands. <c>SwipeToBack.IsSliding</c> is
    /// raised the instant a back-swipe is recognised in OnBeginDrag and lowered only by the tail of
    /// SnapToPosition — so a gesture that never reaches OnEndDrag leaves it true with no animation
    /// running and nothing scheduled to clear it.
    ///
    /// It is not local state: it is the app-wide "a slide is animating, stay off the main thread"
    /// gate. Image decode (MessageItemView.AcquireDecodeSlot), the live poll
    /// (ChatManager.LivePoll), the message sync's park loop, long-press, swipe-to-reply and
    /// swipe-to-delete all pause on it. Stranded true, the app stops decoding images and stops
    /// polling until some later slide happens to run to completion.
    /// </summary>
    [Test]
    public void InterruptedBackSwipe_DoesNotLeaveTheSlideGateRaised()
    {
        var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        var panel = new GameObject("MessagesPanel", typeof(RectTransform));
        var scroll = new GameObject("Scroll").AddComponent<ScrollRect>();
        var strip = new GameObject("SwipeBack", typeof(RectTransform));
        try
        {
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1080f, 1920f);
            strip.transform.SetParent(canvas.transform, false);

            var swipe = strip.AddComponent<SwipeToBack>();
            swipe.chatPanelToSlide = (RectTransform)panel.transform;
            swipe.chatScrollRect = scroll;
            InvokeLifecycle(swipe, "Awake");   // resolves the canvas OnBeginDrag measures against

            // A recognised back-swipe: mostly horizontal, rightwards, out of the left edge.
            swipe.OnBeginDrag(new PointerEventData(EventSystem.current)
            {
                pressPosition = new Vector2(30f, 900f),
                position = new Vector2(150f, 910f),
            });
            Assert.IsTrue(SwipeToBack.IsSliding,
                "OnBeginDrag no longer raises the slide gate — this test would pass on nothing.");

            // ...and the chat closes under the finger. No OnEndDrag, so no SnapToPosition either.
            InvokeLifecycle(swipe, "OnDisable");

            Assert.IsFalse(SwipeToBack.IsSliding,
                "OnDisable must lower the slide gate, or an interrupted swipe pauses image decode "
                + "and the live poll app-wide until the next slide completes.");
        }
        finally
        {
            Object.DestroyImmediate(strip);
            Object.DestroyImmediate(scroll.gameObject);
            Object.DestroyImmediate(panel);
            Object.DestroyImmediate(canvas);
        }
    }
}

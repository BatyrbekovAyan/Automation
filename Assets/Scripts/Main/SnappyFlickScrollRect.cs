using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SnappyFlickScrollRect : ScrollRect
{
    [Header("Snappy Flick Settings")]
    [Tooltip("How short a touch must be to count as a 'quick flick' (in seconds).")]
    public float quickFlickTimeWindow = 0.2f; 
    public float quickFlickMultiplier = 1.8f;

    [Header("Momentum Acceleration")]
    [Tooltip("Maximum allowed speed so the list doesn't break space-time.")]
    public float maxVelocity = 15000f;

    private float dragStartTime;
    private Vector2 dragStartPosition;
    private float preDragVelocityY;

    /// <summary>
    /// The thread's drag stream, re-broadcast for gesture layers that must see EVERY drag this
    /// list receives — including the ones forwarded by a TYPED call rather than through
    /// ExecuteEvents: SwipeToReply on every bubble (`_scroll.OnDrag(e)`), DragShield, and
    /// SwipeToBack's left-band routing, which resolves to that same SwipeToReply. A component of
    /// its own on this GameObject would see only the drags that start in the gaps BETWEEN bubbles
    /// — dead over most of the thread. ScrollRect's own callbacks are the one point they converge.
    /// This class stays a plain scroll and knows nothing about its listeners.
    /// </summary>
    public event System.Action<PointerEventData> DragBegan;
    public event System.Action<PointerEventData> DragMoved;
    public event System.Action<PointerEventData> DragEnded;

    // THE FIX: Intercept the touch BEFORE Unity zeros out the velocity!
    public override void OnInitializePotentialDrag(PointerEventData eventData)
    {
        // Capture how fast it was spinning the exact millisecond you touched it
        preDragVelocityY = this.velocity.y;
        
        base.OnInitializePotentialDrag(eventData);
    }

    /// <summary>
    /// True between OnBeginDrag and OnEndDrag — the window in which this ScrollRect owns
    /// content.anchoredPosition, ELASTIC OVERSCROLL included. Anything else that writes that
    /// position must stand down while this is set, or it fights the rubber band under the finger
    /// (see ScrollTopInsetMath.ShouldClampContent for the device symptom that forced this).
    /// A MIRROR of the base class's own m_Dragging, not the truth: it is set unconditionally while
    /// base.OnBeginDrag early-returns on a non-left button, so it can read true for a gesture the
    /// scroll declined. Harmless because the clear is equally unconditional — the pair can never
    /// drift apart.
    /// </summary>
    public bool IsDragging { get; private set; }

    /// <summary>
    /// True while the ScrollRect is still moving the content BY ITSELF after the finger left —
    /// inertia, or the elastic ease back from an overscroll. Ownership of the content position does
    /// not end at pointer-up, and a guard that thinks it does re-opens exactly one frame before the
    /// ease starts, which is enough to turn the spring into a pop.
    /// </summary>
    public bool IsSettling { get; private set; }

    // Sampled right after base.LateUpdate, which has just applied this frame's inertia/elastic
    // step. Deliberately built from PUBLIC state only — ScrollRect's own out-of-bounds test
    // (CalculateOffset) is private and unreachable from a subclass.
    //
    // The latch is ARMED for the whole drag rather than at pointer-up, so it is already true on the
    // frame the finger leaves — the frame a release-time guard would otherwise miss. It clears only
    // once ScrollRect has stopped moving the content itself: the base class drives `velocity` with
    // SmoothDamp through both inertia and the elastic ease, and zeroes it when the motion falls
    // under 1 unit/s, so "velocity is exactly zero after base.LateUpdate" is precisely "the scroll
    // has come to rest". A release that lands in range with no momentum clears on the very next
    // frame, which is correct — there is no spring to protect.
    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (IsDragging) IsSettling = true;
        else if (velocity == Vector2.zero) IsSettling = false;
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        base.OnBeginDrag(eventData);

        IsDragging = true;
        dragStartTime = Time.unscaledTime;
        dragStartPosition = content.anchoredPosition;
        DragBegan?.Invoke(eventData);
    }

    // A drag can be lost without an OnEndDrag (the chat screen closing mid-gesture). Clearing the
    // flag here keeps a stranded `true` from suppressing the content clamp for the rest of the
    // session — it would fail silently, as an inset change that never corrects the scroll.
    protected override void OnDisable()
    {
        IsDragging = false;
        IsSettling = false;
        base.OnDisable();
    }

    public override void OnDrag(PointerEventData eventData)
    {
        base.OnDrag(eventData);
        DragMoved?.Invoke(eventData);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        base.OnEndDrag(eventData);

        IsDragging = false;

        float dragDuration = Time.unscaledTime - dragStartTime;

        if (dragDuration <= quickFlickTimeWindow && dragDuration > 0.01f)
        {
            Vector2 dragDistance = content.anchoredPosition - dragStartPosition;
            Vector2 rawVelocity = dragDistance / dragDuration;
            
            float newFlickY = rawVelocity.y * quickFlickMultiplier;
            float finalVelocityY = newFlickY;

            // --- TRUE ACCELERATION MATH ---
            if (Mathf.Sign(newFlickY) == Mathf.Sign(preDragVelocityY) && Mathf.Abs(preDragVelocityY) > 50f)
            {
                // Aggressively ADD the old speed and the new flick together!
                finalVelocityY = preDragVelocityY + newFlickY;
                
                finalVelocityY = Mathf.Clamp(finalVelocityY, -maxVelocity, maxVelocity);
            }

            this.velocity = new Vector2(0f, finalVelocityY);
        }

        DragEnded?.Invoke(eventData);
    }
}
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

    public override void OnBeginDrag(PointerEventData eventData)
    {
        base.OnBeginDrag(eventData);

        dragStartTime = Time.unscaledTime;
        dragStartPosition = content.anchoredPosition;
        DragBegan?.Invoke(eventData);
    }

    public override void OnDrag(PointerEventData eventData)
    {
        base.OnDrag(eventData);
        DragMoved?.Invoke(eventData);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        base.OnEndDrag(eventData);

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
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
    }
}
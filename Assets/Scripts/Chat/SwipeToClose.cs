using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

public class SwipeToClose : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    [Header("Settings")]
    [Tooltip("How many pixels the user must drag before it triggers a close.")]
    public float swipeThreshold = 150f;
    
    [Tooltip("The actual Image or Video RectTransform that should follow the finger.")]
    public RectTransform targetToMove; 

    [Header("Events")]
    public UnityEvent onSwipeClosed;

    private Vector2 initialTouchPos;
    private Vector3 initialTargetPos;

    public void OnBeginDrag(PointerEventData eventData)
    {
        initialTouchPos = eventData.position;

        // Remember exactly where the image started
        if (targetToMove != null)
        {
            initialTargetPos = targetToMove.localPosition;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Calculate how far the finger moved vertically
        float deltaY = eventData.position.y - initialTouchPos.y;

        // Make the image physically follow the finger!
        if (targetToMove != null)
        {
            targetToMove.localPosition = initialTargetPos + new Vector3(0, deltaY, 0);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        float deltaY = eventData.position.y - initialTouchPos.y;

        // Did they swipe far enough to close it? (Mathf.Abs allows swiping UP or DOWN)
        if (Mathf.Abs(deltaY) > swipeThreshold)
        {
            onSwipeClosed?.Invoke();
        }
        else
        {
            // They let go too early! Snap the image safely back to the center.
            if (targetToMove != null)
            {
                targetToMove.localPosition = initialTargetPos;
            }
        }
    }

    void OnDisable()
    {
        // Safety catch: Whenever this screen hides, instantly reset the position 
        // so it isn't broken the next time they open a photo!
        if (targetToMove != null)
        {
            targetToMove.localPosition = Vector3.zero;
        }
    }
}
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class ClickPassthrough : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Target Restriction")]
    [Tooltip("If assigned, the passthrough will ONLY click objects inside this specific panel.")]
    public Transform allowedPanel; // <-- 1. Add the safety net!

    [Header("Press passthrough")]
    [Tooltip("When true, pointer DOWN/UP are delivered to EVERY stacked target under the finger " +
             "instead of stopping at the first button. Lets a handler that sits behind a button — " +
             "e.g. a message bubble's long-press detector — still receive press events through this " +
             "passthrough. Clicks always still stop at the first button. Leave false for plain taps.")]
    [SerializeField] private bool deliverPressToAllBehind = false;

    private Vector2 pointerDownPosition;

    public void OnPointerDown(PointerEventData eventData)
    {
        // 1. Remember exactly where the finger touched the screen
        pointerDownPosition = eventData.position;
        PassEvent(eventData, ExecuteEvents.pointerDownHandler, breakAtInteractable: !deliverPressToAllBehind);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        PassEvent(eventData, ExecuteEvents.pointerUpHandler, breakAtInteractable: !deliverPressToAllBehind);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 2. Measure the distance the finger moved between touching down and lifting up
        float distanceMoved = Vector2.Distance(pointerDownPosition, eventData.position);

        // 3. THE FIX: If the distance is larger than Unity's official drag threshold, it was a swipe!
        // We instantly abort and do NOT pass the click event down to the buttons.
        if (distanceMoved > EventSystem.current.pixelDragThreshold)
        {
            return;
        }

        // If it was a clean tap (finger barely moved), pass the click! A click ALWAYS stops at the
        // first interactable so one tap can never fire two buttons.
        PassEvent(eventData, ExecuteEvents.pointerClickHandler, breakAtInteractable: true);
    }

    private void PassEvent<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> function, bool breakAtInteractable) where T : IEventSystemHandler
    {
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        foreach (RaycastResult result in results)
        {
            if (result.gameObject == gameObject) continue;

            // --- THE FIX: Gatekeep the click! ---
            // If we assigned an allowed panel, and the thing we hit is NOT inside it...
            if (allowedPanel != null && !result.gameObject.transform.IsChildOf(allowedPanel))
            {
                continue; // Skip it! Do not click!
            }

            ExecuteEvents.Execute(result.gameObject, eventData, function);

            if (breakAtInteractable &&
                (result.gameObject.GetComponent<UnityEngine.UI.Selectable>() != null ||
                 result.gameObject.GetComponent<IPointerClickHandler>() != null))
            {
                break;
            }
        }
    }
}

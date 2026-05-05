using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Stops UI pointer events from bubbling up to parent handlers. Attach to a
/// card/panel whose background should NOT forward taps to a surrounding
/// overlay (e.g., a modal overlay that dismisses on outside tap, where the
/// card itself should swallow taps in non-button areas).
///
/// Works because Unity's <see cref="ExecuteEvents.ExecuteHierarchy"/> walks
/// up the hierarchy until it finds a GameObject implementing the target
/// handler interface, then stops. Implementing the pointer interfaces here
/// with empty bodies halts the walk at this object without side effects.
/// </summary>
[DisallowMultipleComponent]
public class EventAbsorber : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public void OnPointerDown(PointerEventData eventData) { }
    public void OnPointerUp(PointerEventData eventData) { }
    public void OnPointerClick(PointerEventData eventData) { }
}

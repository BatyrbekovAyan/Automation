using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Left-edge gesture proxy for the suggestions sheet. The sheet renders ABOVE the global
/// SwipeBack strip (so its card scroll wins raycasts), which would otherwise swallow the
/// left-edge swipe-back gesture over the sheet. This strip sits on the sheet's left edge and
/// re-routes per gesture, mirroring SwipeToBack's own decision rule: a mostly-horizontal
/// rightward drag is forwarded to <see cref="SwipeToBack.Instance"/> (chat slides out under
/// the sheet), anything else goes to the sheet's own cards ScrollRect. Taps still reach the
/// cards via the companion ClickPassthrough on the same object.
/// </summary>
public class SuggestionsSheetSwipeProxy : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private ScrollRect verticalTarget;   // the sheet's cards viewport (builder-wired)

    private bool _horizontal;
    private bool _decided;

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        _decided = false;
        if (verticalTarget != null) verticalTarget.OnInitializePotentialDrag(eventData);
        if (SwipeToBack.Instance != null) SwipeToBack.Instance.OnInitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        Vector2 trajectory = eventData.position - eventData.pressPosition;
        _horizontal = Mathf.Abs(trajectory.x) > Mathf.Abs(trajectory.y) && trajectory.x > 0f;
        _decided = true;
        if (_horizontal) SwipeToBack.Instance?.OnBeginDrag(eventData);
        else verticalTarget?.OnBeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_decided) return;
        if (_horizontal) SwipeToBack.Instance?.OnDrag(eventData);
        else verticalTarget?.OnDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_decided) return;
        if (_horizontal) SwipeToBack.Instance?.OnEndDrag(eventData);
        else verticalTarget?.OnEndDrag(eventData);
        _decided = false;
    }
}

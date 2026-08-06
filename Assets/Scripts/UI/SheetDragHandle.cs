using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Drag adapter for the suggestions sheet's grab zone (grabber + header strip). Dragging down
/// moves the sheet with the finger; releasing past the close threshold dismisses it, otherwise
/// it springs back. The close routes through <see cref="SuggestionsController.SetSheetOpen"/>
/// so the message-list floor animates back down with the sheet — never through
/// <see cref="SuggestionsPanel.Hide"/> directly. Pure input adapter: the panel owns all tweens.
/// </summary>
public class SheetDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private SuggestionsPanel panel;             // wired by SuggestionsPanelBuilder
    [SerializeField] private SuggestionsController controller;   // wired by SuggestionsControllerWirer
    [SerializeField] private float closeThreshold = 0.25f;       // fraction of the footprint dragged down

    private float _draggedDown;
    private Canvas _rootCanvas;

    void Awake()
    {
        Canvas local = GetComponentInParent<Canvas>();
        if (local != null) _rootCanvas = local.rootCanvas;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _draggedDown = 0f;
        if (panel != null) panel.BeginHandleDrag();
    }

    public void OnDrag(PointerEventData eventData)
    {
        float scale = _rootCanvas != null ? _rootCanvas.scaleFactor : 1f;
        _draggedDown -= eventData.delta.y / scale;               // finger down = negative delta.y
        if (_draggedDown < 0f) _draggedDown = 0f;                // no over-drag above the rest position
        if (panel != null) panel.DragBy(_draggedDown);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        bool close = panel != null && panel.DragProgress > closeThreshold;
        if (close && controller != null) controller.SetSheetOpen(false);
        else if (panel != null) panel.SnapBack();
    }
}

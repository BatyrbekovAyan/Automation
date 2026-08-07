using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Drag adapter for the suggestions sheet's grab zone (grabber + header strip). One continuous
/// gesture space, split at the base detent: pulling UP grows the sheet toward the
/// "all cards visible" fit height (rubber-band past it), pulling DOWN first collapses back to
/// the base height and then translates the sheet toward dismissal. Release settles to the
/// nearest expansion detent, or — past the close threshold — dismisses via
/// <see cref="SuggestionsController.SetSheetOpen"/> so the message-list floor animates back
/// down with the sheet (never through <see cref="SuggestionsPanel.Hide"/> directly).
/// Pure input adapter: the panel owns all tweens.
/// </summary>
public class SheetDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private SuggestionsPanel panel;             // wired by SuggestionsPanelBuilder
    [SerializeField] private SuggestionsController controller;   // wired by SuggestionsControllerWirer
    [SerializeField] private float closeThreshold = 0.25f;       // fraction of the footprint dragged down
    [SerializeField] private float overdragResistance = 0.3f;    // finger-follow past the fit height

    private float _startHeight;      // sheet height when the finger landed
    private float _draggedUp;        // cumulative finger travel, up = positive
    private Canvas _rootCanvas;

    void Awake()
    {
        Canvas local = GetComponentInParent<Canvas>();
        if (local != null) _rootCanvas = local.rootCanvas;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _draggedUp = 0f;
        if (panel == null) return;
        panel.BeginHandleDrag();
        _startHeight = panel.CurrentHeight;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (panel == null) return;
        float scale = _rootCanvas != null ? _rootCanvas.scaleFactor : 1f;
        _draggedUp += eventData.delta.y / scale;
        float desired = _startHeight + _draggedUp;
        float baseH = panel.BaseHeight;

        if (desired > baseH)
        {
            // Expansion zone: grow toward the fit detent, rubber-band beyond it.
            float fit = panel.ExpandedFitHeight();
            float height = desired <= fit ? desired : fit + (desired - fit) * overdragResistance;
            panel.DragBy(0f);
            panel.SetSheetHeight(height);
        }
        else
        {
            // Translate zone: collapsed — the remaining pull moves the sheet toward dismissal.
            panel.SetSheetHeight(baseH);
            panel.DragBy(baseH - desired);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (panel == null) return;
        if (panel.DragProgress > 0f)
        {
            bool close = panel.DragProgress > closeThreshold;
            if (close && controller != null) controller.SetSheetOpen(false);
            else panel.SnapBack();
            return;
        }
        // Expansion zone: settle to the nearest detent (midpoint rule) — overdrag always
        // returns to the fit height, per the owner's spec.
        float baseHeight = panel.BaseHeight;
        float fitHeight = panel.ExpandedFitHeight();
        float settle = panel.CurrentHeight > (baseHeight + fitHeight) * 0.5f ? fitHeight : baseHeight;
        panel.SettleSheetHeight(settle);
    }
}

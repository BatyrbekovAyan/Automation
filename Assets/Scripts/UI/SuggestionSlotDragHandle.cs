using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The 42u grab strip on the suggestions panel's TOP edge, directly under the composer
/// (sketch-005 variant E). It is the ONLY gesture that may collapse the slot: dragging it up grows
/// the slot toward the Expanded detent, dragging it down shrinks it toward Collapsed.
/// <para>
/// View only, pointer math only. The handle NEVER touches KeyboardAwarePanel, never writes
/// VirtualBottomInset and never snaps to a detent — it reports a PROPOSED slot height in canvas
/// units through <see cref="Dragged"/> / <see cref="Released"/> and SuggestionsController owns the
/// inset, the snapping (SuggestionSlotDetents.Snap) and the smoothing bypass.
/// </para>
/// <para>
/// It also must not know that a native keyboard exists. The keyboard is just another tenant of the
/// same slot, and the merge of the two claims is a single rule in one place
/// (KeyboardAwarePanel: Mathf.Max(EffectiveAreaCanvasPx, VirtualBottomInset)). A handle that read
/// or drove the keyboard would become a second owner of that height and could dip the composer
/// during a tenant handoff — the NO-DIP invariant. So the handle only ever answers "how tall is the
/// slot right now?" by asking the controller through <see cref="HeightProvider"/>.
/// </para>
/// <para>
/// Scene requirement: the strip needs its own raycast-target Graphic (a transparent Image sized to
/// the 42u band). Pointer events are resolved by walking UP from the raycast target, so with no
/// graphic of its own this component is never reached and the handle is simply dead.
/// </para>
/// </summary>
public class SuggestionSlotDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>Slot height (canvas units) at the moment of the grab — the controller returns
    /// KeyboardAwarePanel.AppliedBottomInset, so grabbing mid-animation catches the panel exactly
    /// where it visually is. Null => grab at 0.</summary>
    public Func<float> HeightProvider;

    /// <summary>Drag ceiling (canvas units) — the controller returns the Expanded detent height.
    /// Null => no known ceiling (SuggestionSlotGestures clamps the floor only). Polled on EVERY
    /// drag frame, so it must return a value that is stable for the length of a gesture: a ceiling
    /// that shrinks mid-drag (cards refreshing under the finger) yanks the slot down with it.</summary>
    public Func<float> MaxHeightProvider;

    public event Action Grabbed;
    public event Action<float> Dragged;   // proposed slot height, canvas units
    /// <summary>Final proposed height + the finger's release velocity (canvas units per second, on
    /// Unity's POSITIVE-IS-UP axis, so a flick DOWN is negative). The CONTROLLER snaps —
    /// SuggestionSlotDetents.SnapWithFlick lets a genuine flick beat the half-way rule.</summary>
    public event Action<float, float> Released;

    public bool IsDragging { get; private set; }

    private Canvas _canvas;
    private int _activePointerId;
    private float _grabPointerScreenY;
    private float _grabHeightCanvasPx;
    private float _lastProposedCanvasPx;
    // Shared with the thread pull-down so the two entries into this gesture feel identical: a fast
    // release collapses (or expands) regardless of where the finger stopped.
    private readonly DragVelocitySampler _velocity = new DragVelocitySampler();

    void Awake() => CacheCanvas();

    // Cached: no GetComponent on a per-frame pointer path. rootCanvas carries the authoritative
    // scaleFactor (a nested Canvas mirrors it), same as SwipeToBack/SheetDragDismiss do.
    private void CacheCanvas()
    {
        var canvas = GetComponentInParent<Canvas>();
        _canvas = canvas != null ? canvas.rootCanvas : null;
    }

    void OnDisable()
    {
        // The pointer can be lost without an EndDrag (chat closed, panel hidden, object disabled
        // mid-gesture). Release at the last proposed height so the controller is never stranded
        // mid-drag with its smoothing bypass stuck on. IsDragging is cleared BEFORE the event so a
        // handler that disables us again cannot make Released fire twice.
        if (!IsDragging) return;
        IsDragging = false;
        Released?.Invoke(_lastProposedCanvasPx, 0f);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Left button only — the filter uGUI's own ScrollRect applies. Touch always reports Left,
        // so this only stops an Editor right/middle-drag from taking ownership of the gesture and
        // locking the real (left) pointer out through the pointerId filter below.
        if (eventData.button != PointerEventData.InputButton.Left) return;

        // A second finger landing on the strip must not re-grab and teleport the slot; the first
        // pointer owns the gesture until it releases.
        if (IsDragging) return;

        // Re-resolve a Canvas that was missing at Awake (GetComponentInParent skips a disabled
        // ancestor Canvas). Once per gesture, never on the per-frame drag path — silently keeping
        // the scale-1 fallback would drag the slot at a THIRD of the finger's speed on a 3x device.
        if (_canvas == null) CacheCanvas();

        _grabHeightCanvasPx = HeightProvider != null ? HeightProvider() : 0f;
        _lastProposedCanvasPx = _grabHeightCanvasPx;
        _grabPointerScreenY = eventData.position.y;
        _activePointerId = eventData.pointerId;
        _velocity.Reset();
        _velocity.Sample(eventData.position.y / CanvasScale, Time.unscaledTime);

        // State BEFORE the event. A Grabbed handler that deactivates the panel (or closes the
        // chat) runs our OnDisable synchronously: with the flag still false that release path
        // no-ops, and we would then latch IsDragging = true on a component Unity can no longer
        // deliver OnDrag/OnEndDrag to — a drag nothing can ever end, with the controller's
        // smoothing bypass stuck on. Setting it first lets OnDisable always close the gesture.
        IsDragging = true;
        Grabbed?.Invoke();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!IsDragging || eventData.pointerId != _activePointerId) return;

        _velocity.Sample(eventData.position.y / CanvasScale, Time.unscaledTime);
        _lastProposedCanvasPx = ProposedHeight(eventData);
        Dragged?.Invoke(_lastProposedCanvasPx);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!IsDragging || eventData.pointerId != _activePointerId) return;

        _velocity.Sample(eventData.position.y / CanvasScale, Time.unscaledTime);
        _lastProposedCanvasPx = ProposedHeight(eventData);
        IsDragging = false;                            // cleared first — Released must fire once
        Released?.Invoke(_lastProposedCanvasPx, _velocity.VelocityCanvasPxPerSec);
    }

    /// <summary>
    /// Pointer position → proposed slot height. Two sign/precision notes:
    /// (1) PointerEventData.position is SCREEN pixels with the origin at the BOTTOM-left (Unity's
    /// input events, unlike native touch APIs whose y grows downward), so a finger moving UP yields
    /// a LARGER y. The handle rides the slot's TOP edge, so up must GROW the slot — (now − grab) is
    /// therefore already POSITIVE-IS-UP as SuggestionSlotGestures.HeightFromDrag expects, with no
    /// negation. (2) The delta is measured from the GRAB position rather than summed from
    /// eventData.delta, so a dropped or coalesced pointer frame cannot drift the panel away from
    /// the finger.
    /// </summary>
    private float ProposedHeight(PointerEventData eventData)
    {
        float totalDeltaCanvasPx = (eventData.position.y - _grabPointerScreenY) / CanvasScale;

        // No provider = no known ceiling; HeightFromDrag reads a non-finite max as "floor only"
        // (reading a bad ceiling as 0 would slam the slot shut under the finger).
        float max = MaxHeightProvider != null ? MaxHeightProvider() : float.PositiveInfinity;

        return SuggestionSlotGestures.HeightFromDrag(_grabHeightCanvasPx, totalDeltaCanvasPx, max);
    }

    /// <summary>Screen px → canvas units. A missing or degenerate canvas falls back to 1 so a
    /// drag in an unwired scene moves the slot 1:1 instead of dividing by zero.</summary>
    private float CanvasScale =>
        _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
}

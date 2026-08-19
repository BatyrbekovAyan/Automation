using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Turns the message thread's own drag stream into the suggestions slot's PULL-DOWN gesture (owner
/// request 2026-08-19): drag the thread down, and the moment the finger reaches the composer's top
/// edge the slot follows it — iOS's interactive keyboard dismissal, applied to the slot the
/// suggestions panel and the native keyboard share.
/// <para>
/// Plain C#, deliberately NOT a MonoBehaviour: <see cref="SuggestionsController"/> owns one and
/// attaches it to the thread's <see cref="SnappyFlickScrollRect"/>, so the whole gesture ships with
/// ZERO scene edits — nothing to serialize, nothing to wire, no Main.unity save.
/// </para>
/// <para>
/// WHY THE SCROLLRECT AND NOT A COMPONENT OF ITS OWN: bubbles carry SwipeToReply, which forwards a
/// vertical drag with a TYPED call (`_scroll.OnDrag(e)`) instead of through ExecuteEvents; DragShield
/// does the same, and SwipeToBack's left-band routing resolves its target to that same SwipeToReply.
/// A sibling IDragHandler on the Scroll GameObject would therefore only ever see drags that start in
/// the gaps BETWEEN bubbles — dead over most of the thread. ScrollRect's own drag callbacks are the
/// one point every path lands in.
/// </para>
/// <para>
/// It emits the same three events the 42u handle does, so the controller drives both entries with a
/// single set of handlers. Over a LIVE keyboard it emits <see cref="KeyboardPullDown"/> instead and
/// goes inert for the rest of the touch: Unity cannot move the native keyboard with the finger, so
/// that branch is a one-shot dismissal rather than a track.
/// </para>
/// <para>
/// Pointer input arrives as plain floats (screen px + a clock), which is what makes the whole
/// gesture EditMode-testable; <see cref="Attach"/> is the only place PointerEventData is unpacked.
/// </para>
/// </summary>
public sealed class SlotPullDownRecognizer
{
    /// <summary>Live slot height in canvas units — the controller returns
    /// KeyboardAwarePanel.AppliedBottomInset, so engaging mid-animation catches the panel exactly
    /// where it visually is.</summary>
    public Func<float> HeightProvider;

    /// <summary>The composer's top edge in SCREEN pixels — the engage line. Polled every frame
    /// because it rides the slot inset: a value captured once would put the line in the wrong place
    /// the moment the slot is anything but the height it had at capture.</summary>
    public Func<float> ComposerTopScreenYProvider;

    /// <summary>Screen px per canvas unit. A missing or degenerate value falls back to 1 so an
    /// unwired scene drags 1:1 rather than dividing by zero.</summary>
    public Func<float> CanvasScaleProvider;

    /// <summary>The controller's whole veto set folded into one bit — see
    /// <see cref="SuggestionSlotPullDown.ShouldEngage"/>.</summary>
    public Func<bool> EligibleProvider;

    /// <summary>True while the native keyboard owns the slot; selects the one-shot branch.</summary>
    public Func<bool> KeyboardVisibleProvider;

    public event Action Grabbed;
    public event Action<float> Dragged;           // proposed slot height, canvas units
    public event Action<float, float> Released;   // final height + velocity (canvas units, canvas units/s)
    public event Action KeyboardPullDown;         // fires ONCE per gesture, over a live keyboard

    public bool IsEngaged { get; private set; }

    private readonly DragVelocitySampler _velocity = new DragVelocitySampler();
    private SnappyFlickScrollRect _scroll;
    private bool _tracking;
    private int _pointerId;
    private float _heightAtEngageCanvasPx;
    private float _engageFingerCanvasY;
    private float _lastHeightCanvasPx;

    /// <summary>Listen to a thread. Re-attaching is safe: the previous subscription is dropped
    /// first, so a controller that re-resolves its scroll can never end up double-firing.</summary>
    public void Attach(SnappyFlickScrollRect scroll)
    {
        Detach();
        _scroll = scroll;
        if (_scroll == null) return;
        _scroll.DragBegan += HandleScrollDragBegan;
        _scroll.DragMoved += HandleScrollDragMoved;
        _scroll.DragEnded += HandleScrollDragEnded;
    }

    public void Detach()
    {
        if (_scroll != null)
        {
            _scroll.DragBegan -= HandleScrollDragBegan;
            _scroll.DragMoved -= HandleScrollDragMoved;
            _scroll.DragEnded -= HandleScrollDragEnded;
            _scroll = null;
        }
        Reset();
    }

    /// <summary>
    /// Abandon the gesture WITHOUT emitting <see cref="Released"/>. Its callers — the «+» sheet
    /// evicting the slot, the chat screen closing — already own the slot's recovery, and a snap
    /// fired from here would land underneath whatever they are doing.
    /// </summary>
    public void Reset()
    {
        _tracking = false;
        IsEngaged = false;
        _velocity.Reset();
    }

    // --- PointerEventData adapters: the only Unity-typed code in this class ---

    private void HandleScrollDragBegan(PointerEventData e) => PointerDown(e.pointerId);

    private void HandleScrollDragMoved(PointerEventData e)
        => PointerMoved(e.pointerId, e.position.y, Time.unscaledTime);

    private void HandleScrollDragEnded(PointerEventData e)
        => PointerUp(e.pointerId, e.position.y, Time.unscaledTime);

    // --- Gesture core: plain floats, no Unity types --------------------------

    public void PointerDown(int pointerId)
    {
        _tracking = true;
        IsEngaged = false;
        _pointerId = pointerId;
        _velocity.Reset();
    }

    public void PointerMoved(int pointerId, float fingerScreenY, float timeSeconds)
    {
        if (!_tracking || pointerId != _pointerId) return;

        float fingerCanvasY = fingerScreenY / Scale;
        _velocity.Sample(fingerCanvasY, timeSeconds);

        if (!IsEngaged && !TryEngage(fingerCanvasY)) return;
        if (!IsEngaged) return;   // a Grabbed handler tore the screen down synchronously

        _lastHeightCanvasPx = SuggestionSlotPullDown.HeightFromPull(
            _heightAtEngageCanvasPx, fingerCanvasY, _engageFingerCanvasY);
        Dragged?.Invoke(_lastHeightCanvasPx);
    }

    public void PointerUp(int pointerId, float fingerScreenY, float timeSeconds)
    {
        if (!_tracking || pointerId != _pointerId) return;
        _tracking = false;
        if (!IsEngaged) return;

        float fingerCanvasY = fingerScreenY / Scale;
        _velocity.Sample(fingerCanvasY, timeSeconds);
        _lastHeightCanvasPx = SuggestionSlotPullDown.HeightFromPull(
            _heightAtEngageCanvasPx, fingerCanvasY, _engageFingerCanvasY);

        IsEngaged = false;   // cleared FIRST — Released must fire exactly once even if a handler
                             // re-enters, mirroring SuggestionSlotDragHandle.OnEndDrag
        Released?.Invoke(_lastHeightCanvasPx, _velocity.VelocityCanvasPxPerSec);
    }

    private bool TryEngage(float fingerCanvasY)
    {
        float composerTopScreenY = ComposerTopScreenYProvider != null
            ? ComposerTopScreenYProvider()
            : float.NaN;

        if (!SuggestionSlotPullDown.ShouldEngage(
                fingerCanvasY, composerTopScreenY / Scale, IsEngaged,
                EligibleProvider != null && EligibleProvider()))
            return false;

        // Over a LIVE keyboard there is nothing to track: Unity cannot drag the native keyboard, it
        // can only dismiss it. Fire once, stop tracking, and let it play its own animation with the
        // composer following IT rather than the finger.
        if (KeyboardVisibleProvider != null && KeyboardVisibleProvider())
        {
            _tracking = false;
            KeyboardPullDown?.Invoke();
            return false;
        }

        _heightAtEngageCanvasPx = HeightProvider != null ? HeightProvider() : 0f;
        _engageFingerCanvasY = fingerCanvasY;
        _lastHeightCanvasPx = _heightAtEngageCanvasPx;

        // Flag BEFORE the event, for the same reason SuggestionSlotDragHandle sets IsDragging
        // first: a Grabbed handler may close the chat synchronously, and Reset() must be able to
        // close a gesture that is already open rather than leaving one nothing can ever end.
        IsEngaged = true;
        Grabbed?.Invoke();
        return true;
    }

    private float Scale
    {
        get
        {
            float s = CanvasScaleProvider != null ? CanvasScaleProvider() : 1f;
            return float.IsFinite(s) && s > 0f ? s : 1f;
        }
    }
}

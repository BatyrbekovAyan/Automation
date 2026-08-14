using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pins the messages ScrollRect's TOP edge to its rest screen position while the MovingArea
/// rides KeyboardAwarePanel's bottom inset (native keyboard or the suggestions panel's slot
/// claim). The rise carries the whole Scroll up, but ScrollRect still clamps content-top to
/// viewport-top — which is then above the screen — so the first `inset` units of history are
/// unreachable; with a short thread nothing scrolls at all and the earliest messages sit
/// stuck behind the TopBar (found 2026-08-13 with the suggestions panel open; the native
/// keyboard shared the same gap). Trimming the top by the APPLIED rise (smoothing and all)
/// keeps the reachable range equal to the visible band, like native chat apps behave with
/// the keyboard up. Lives on the Scroll GameObject, under the KeyboardAwarePanel it tracks.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ScrollTopInsetCompensator : MonoBehaviour
{
    private RectTransform _rt;
    private ScrollRect _scroll;
    private KeyboardAwarePanel _mover;
    private float _restTopOffset;
    private float _lastApplied;

    void Awake()
    {
        _rt = (RectTransform)transform;
        _scroll = GetComponent<ScrollRect>();
        _mover = GetComponentInParent<KeyboardAwarePanel>();
        _restTopOffset = _rt.offsetMax.y;
    }

    /// <summary>
    /// The thread viewport's height with nothing raised: the current height plus whatever rise is
    /// trimming it right now (LateUpdate shortens the rect by exactly the applied inset, so adding
    /// it back reconstructs rest). The suggestions panel's Expanded detent is capped against this
    /// so a readable slice of thread always survives — computed from live geometry rather than a
    /// constant, because the canvas height varies with the device's aspect ratio.
    /// </summary>
    public float RestViewportHeightCanvasPx => _rt != null ? _rt.rect.height + _lastApplied : 0f;

    void LateUpdate()
    {
        if (_mover == null) return;
        float applied = Mathf.Max(0f, _mover.AppliedBottomInset);
        if (Mathf.Approximately(applied, _lastApplied)) return;
        _lastApplied = applied;
        _rt.offsetMax = new Vector2(_rt.offsetMax.x, ScrollTopInsetMath.TrimmedTopOffset(_restTopOffset, applied));
        ClampContentIntoRange();
    }

    private void ClampContentIntoRange()
    {
        if (_scroll == null || _scroll.content == null) return;
        RectTransform viewport = _scroll.viewport != null ? _scroll.viewport : _rt;
        Vector2 pos = _scroll.content.anchoredPosition;
        float clamped = ScrollTopInsetMath.ClampContentY(pos.y, _scroll.content.rect.height, viewport.rect.height);
        if (Mathf.Approximately(clamped, pos.y)) return;
        _scroll.content.anchoredPosition = new Vector2(pos.x, clamped);
    }
}

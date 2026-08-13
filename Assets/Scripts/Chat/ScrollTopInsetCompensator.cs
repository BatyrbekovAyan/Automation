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

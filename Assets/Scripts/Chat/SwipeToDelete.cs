using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// WhatsApp-style swipe-left-to-reveal-Delete on a chat list row. Lives on the row's
/// SwipeContent (the layer carrying avatar/text/bg/tap-button) and slides it on the X axis,
/// exposing the DeleteButton pinned behind it. Modeled on SwipeToReply: horizontal drags are
/// claimed here; vertical / blocked drags are forwarded to the list ScrollRect so scrolling is
/// unaffected. Only one row is open at a time.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SwipeToDelete : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private float revealWidth = 150f; // must match the DeleteButton width
    private const float SnapSeconds = 0.18f;

    private RectTransform _rt;
    private ScrollRect _scroll;
    private bool _routeToParent;
    private bool _dragging;
    private float _baseX;
    private Tween _snap;

    public bool IsOpen { get; private set; }

    private static SwipeToDelete _openInstance;

    private void Awake()
    {
        _rt = (RectTransform)transform;
        _scroll = GetComponentInParent<ScrollRect>();
    }

    private void OnDisable()
    {
        _snap?.Kill(); _snap = null;
        _routeToParent = false; _dragging = false;
        if (_openInstance == this) _openInstance = null;
    }

    /// <summary>Snap shut instantly (used by ChatItemView.Bind on prefab reuse).</summary>
    public void ResetClosed()
    {
        _snap?.Kill(); _snap = null;
        _rt.anchoredPosition = new Vector2(0f, _rt.anchoredPosition.y);
        IsOpen = false;
        if (_openInstance == this) _openInstance = null;
    }

    public void Close()
    {
        _snap?.Kill();
        _snap = _rt.DOAnchorPosX(0f, SnapSeconds).SetEase(Ease.OutCubic);
        IsOpen = false;
        if (_openInstance == this) _openInstance = null;
    }

    private void Open()
    {
        if (_openInstance != null && _openInstance != this) _openInstance.Close();
        _openInstance = this;
        _snap?.Kill();
        _snap = _rt.DOAnchorPosX(-revealWidth, SnapSeconds).SetEase(Ease.OutCubic);
        IsOpen = true;
    }

    public void OnInitializePotentialDrag(PointerEventData e) => _scroll?.OnInitializePotentialDrag(e);

    public void OnBeginDrag(PointerEventData e)
    {
        Vector2 traj = e.position - e.pressPosition;
        bool horizontal = Mathf.Abs(traj.x) > Mathf.Abs(traj.y);
        bool blocked = ScrollClickBlocker.IsBlocking || SwipeToBack.IsSliding;

        if (!horizontal || blocked)
        {
            _routeToParent = true;
            _scroll?.OnBeginDrag(e);
            return;
        }

        if (_openInstance != null && _openInstance != this) _openInstance.Close();

        _dragging = true;
        _snap?.Kill();
        _baseX = IsOpen ? -revealWidth : 0f;
    }

    public void OnDrag(PointerEventData e)
    {
        if (_routeToParent) { _scroll?.OnDrag(e); return; }
        if (!_dragging) return;
        float dx = e.position.x - e.pressPosition.x;
        float x = Mathf.Clamp(_baseX + dx, -revealWidth, 0f);
        _rt.anchoredPosition = new Vector2(x, _rt.anchoredPosition.y);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (_routeToParent) { _scroll?.OnEndDrag(e); _routeToParent = false; return; }
        if (!_dragging) return;
        _dragging = false;

        if (_rt.anchoredPosition.x <= -revealWidth * 0.5f) Open();
        else Close();
    }
}

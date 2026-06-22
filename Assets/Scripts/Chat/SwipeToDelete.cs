using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// WhatsApp-style swipe-left-to-reveal-Delete on a chat list row. Lives on the row's
/// SwipeContent (the layer carrying avatar/text/bg/tap-button) and slides it on the X axis;
/// the DeleteButton (a sibling pinned to the right, started off-screen) is driven in tandem so
/// it slides IN with the swipe rather than being statically revealed.
///
/// Horizontal drags are claimed here; vertical / blocked drags are forwarded to the list
/// ScrollRect. Only one row is open at a time, and any other interaction (tapping a row,
/// scrolling, opening a chat, hiding the page) closes the open row — see the static helpers
/// and the ChatItemView/ChatListView callers.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SwipeToDelete : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private float revealWidth = 200f;   // == DeleteButton width; set by the builder
    [SerializeField] private RectTransform deleteButton; // slides in alongside the content
    private const float SnapSeconds = 0.18f;

    private RectTransform _rt;
    private ScrollRect _scroll;
    private bool _routeToParent;
    private bool _dragging;
    private bool _gestureWasDrag;
    private float _baseX;
    private Tween _snap;
    private Tween _btnSnap;

    public bool IsOpen { get; private set; }

    private static SwipeToDelete _openInstance;

    /// <summary>True while some row's delete reveal is open.</summary>
    public static bool AnyOpen => _openInstance != null;

    /// <summary>Close whichever row is currently open (no-op if none).</summary>
    public static void CloseAnyOpen()
    {
        if (_openInstance != null) _openInstance.Close();
    }

    private void Awake()
    {
        _rt = (RectTransform)transform;
        _scroll = GetComponentInParent<ScrollRect>();
        PlaceButton(0f); // park the button off-screen at rest
    }

    private void OnDisable()
    {
        _snap?.Kill(); _snap = null;
        _btnSnap?.Kill(); _btnSnap = null;
        _routeToParent = false;
        _dragging = false;
        if (_openInstance == this) _openInstance = null;
        // Snap shut so a row hidden mid-open (e.g. page switch) is closed when shown again.
        SetContentX(0f);
        PlaceButton(0f);
        IsOpen = false;
    }

    /// <summary>True if the gesture that just ended was a drag/scroll (so a tap handler can ignore it).</summary>
    public bool ConsumeDragFlag()
    {
        bool was = _gestureWasDrag;
        _gestureWasDrag = false;
        return was;
    }

    /// <summary>Snap shut instantly (used by ChatItemView.Bind on prefab reuse).</summary>
    public void ResetClosed()
    {
        _snap?.Kill(); _snap = null;
        _btnSnap?.Kill(); _btnSnap = null;
        SetContentX(0f);
        PlaceButton(0f);
        IsOpen = false;
        if (_openInstance == this) _openInstance = null;
    }

    public void Close()
    {
        _snap?.Kill(); _btnSnap?.Kill();
        _snap = _rt.DOAnchorPosX(0f, SnapSeconds).SetEase(Ease.OutCubic);
        AnimateButtonTo(0f);
        IsOpen = false;
        if (_openInstance == this) _openInstance = null;
    }

    private void Open()
    {
        if (_openInstance != null && _openInstance != this) _openInstance.Close();
        _openInstance = this;
        _snap?.Kill(); _btnSnap?.Kill();
        _snap = _rt.DOAnchorPosX(-revealWidth, SnapSeconds).SetEase(Ease.OutCubic);
        AnimateButtonTo(-revealWidth);
        IsOpen = true;
    }

    public void OnInitializePotentialDrag(PointerEventData e)
    {
        _gestureWasDrag = false; // new gesture
        _scroll?.OnInitializePotentialDrag(e);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        _gestureWasDrag = true;

        // Starting any drag dismisses a different row that's open.
        if (_openInstance != null && _openInstance != this) _openInstance.Close();

        Vector2 traj = e.position - e.pressPosition;
        bool horizontal = Mathf.Abs(traj.x) > Mathf.Abs(traj.y);
        bool blocked = ScrollClickBlocker.IsBlocking || SwipeToBack.IsSliding;

        if (!horizontal || blocked)
        {
            // Vertical scroll: also close this row if it was the open one, then hand off the drag.
            if (_openInstance == this) Close();
            _routeToParent = true;
            _scroll?.OnBeginDrag(e);
            return;
        }

        _dragging = true;
        _snap?.Kill(); _btnSnap?.Kill();
        _baseX = IsOpen ? -revealWidth : 0f;
    }

    public void OnDrag(PointerEventData e)
    {
        if (_routeToParent) { _scroll?.OnDrag(e); return; }
        if (!_dragging) return;
        float dx = e.position.x - e.pressPosition.x;
        float x = Mathf.Clamp(_baseX + dx, -revealWidth, 0f);
        SetContentX(x);
        PlaceButton(x);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (_routeToParent) { _scroll?.OnEndDrag(e); _routeToParent = false; return; }
        if (!_dragging) return;
        _dragging = false;

        if (_rt.anchoredPosition.x <= -revealWidth * 0.5f) Open();
        else Close();
    }

    private void SetContentX(float x)
        => _rt.anchoredPosition = new Vector2(x, _rt.anchoredPosition.y);

    // The DeleteButton (pivot/anchor on the right edge) is parked off-screen to the right at rest
    // (contentX 0 → buttonX +revealWidth) and slides flush to the right edge as the content slides
    // left (contentX -revealWidth → buttonX 0), so its left edge tracks the content's right edge.
    private void PlaceButton(float contentX)
    {
        if (deleteButton == null) return;
        deleteButton.anchoredPosition = new Vector2(revealWidth + contentX, deleteButton.anchoredPosition.y);
    }

    private void AnimateButtonTo(float contentTargetX)
    {
        if (deleteButton == null) return;
        _btnSnap = deleteButton.DOAnchorPosX(revealWidth + contentTargetX, SnapSeconds).SetEase(Ease.OutCubic);
    }
}

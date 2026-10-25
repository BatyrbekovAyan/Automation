using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// WhatsApp-style swipe-left-to-reveal on a chat list row. Lives on the row's SwipeContent (the layer carrying
/// avatar/text/bg/tap-button) and slides it on the X axis. The action buttons behind it — «Удалить» rightmost and, on its
/// left, the move button («В личные» / «В рабочие», «Личные и рабочие чаты») — are siblings pinned to the row's right edge,
/// parked off-screen at rest and driven in tandem, so they slide IN with the swipe rather than being statically revealed.
/// Which of them the row offers is <see cref="SetActions"/>'s (ChatItemView, from ChatRowSwipePolicy); where they stand is
/// <see cref="SwipeActionsLayout"/>'s, and the row slides open by their widths together. A row with no action has this
/// component off, so its drags go straight to the list's scroll.
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
    [Tooltip("How far the row slides open: the visible buttons' widths together. SetActions derives it; the authored value is «Удалить» alone.")]
    [SerializeField] private float revealWidth = 200f;
    [Tooltip("«Удалить» — the rightmost button.")]
    [SerializeField] private RectTransform deleteButton;
    [Tooltip("«В личные» / «В рабочие» — on the left of «Удалить». Stamped by PersonalChatsBuilder.")]
    [SerializeField] private RectTransform moveButton;
    private const float SnapSeconds = 0.18f;

    private RectTransform _rt;
    private ScrollRect _scroll;
    private bool _routeToParent;
    private bool _dragging;
    private bool _gestureWasDrag;
    private float _baseX;
    private Tween _snap;
    private Tween _deleteSnap;
    private Tween _moveSnap;

    // SetActions' last answer (the same actions again change nothing) and each button's x once the row is fully open.
    private bool _actionsSet;
    private bool _showMove;
    private bool _showDelete;
    private float _moveOpenX;
    private float _deleteOpenX;

    public bool IsOpen { get; private set; }

    /// <summary>How far the row slides open: the visible buttons' widths together, 0 with no action.</summary>
    public float RevealWidth => revealWidth;

    private static SwipeToDelete _openInstance;

    /// <summary>True while some row's reveal is open.</summary>
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
        PlaceButtons(0f); // park the buttons off-screen at rest
    }

    private void OnDisable()
    {
        KillTweens();
        _routeToParent = false;
        _dragging = false;
        if (_openInstance == this) _openInstance = null;
        // Snap shut so a row hidden mid-open (e.g. page switch) is closed when shown again.
        SetContentX(0f);
        PlaceButtons(0f);
        IsOpen = false;
    }

    /// <summary>
    /// The actions the row offers — <paramref name="move"/>: the move button, <paramref name="delete"/>: «Удалить». The visible
    /// buttons stand right to left behind the row, «Удалить» rightmost, and the row slides open by their widths together; a
    /// button not offered is deactivated. No action turns the swipe off. The same actions again change nothing, so an open row
    /// stays open; different ones snap the row shut first and cut a drag in progress.
    /// </summary>
    public void SetActions(bool move, bool delete)
    {
        bool showMove = move && moveButton != null;
        bool showDelete = delete && deleteButton != null;
        if (_actionsSet && showMove == _showMove && showDelete == _showDelete) return;
        _actionsSet = true;
        _showMove = showMove;
        _showDelete = showDelete;

        (revealWidth, _moveOpenX, _deleteOpenX) =
            SwipeActionsLayout.Solve(showMove, showDelete, WidthOf(moveButton), WidthOf(deleteButton));
        SetShown(moveButton, showMove);
        SetShown(deleteButton, showDelete);
        ResetClosed();
        enabled = showMove || showDelete;
    }

    /// <summary>True if the gesture that just ended was a drag/scroll (so a tap handler can ignore it).</summary>
    public bool ConsumeDragFlag()
    {
        bool was = _gestureWasDrag;
        _gestureWasDrag = false;
        return was;
    }

    /// <summary>Snap shut instantly (ChatItemView.Bind on prefab reuse; SetActions on new actions). Cuts a drag in progress.</summary>
    public void ResetClosed()
    {
        KillTweens();
        _dragging = false;
        SetContentX(0f);
        PlaceButtons(0f);
        IsOpen = false;
        if (_openInstance == this) _openInstance = null;
    }

    public void Close()
    {
        KillTweens();
        _snap = Rt.DOAnchorPosX(0f, SnapSeconds).SetEase(Ease.OutCubic);
        AnimateButtonsTo(0f);
        IsOpen = false;
        if (_openInstance == this) _openInstance = null;
    }

    private void Open()
    {
        if (_openInstance != null && _openInstance != this) _openInstance.Close();
        _openInstance = this;
        KillTweens();
        _snap = Rt.DOAnchorPosX(-revealWidth, SnapSeconds).SetEase(Ease.OutCubic);
        AnimateButtonsTo(-revealWidth);
        IsOpen = true;
    }

    public void OnInitializePotentialDrag(PointerEventData e)
    {
        _scroll ??= GetComponentInParent<ScrollRect>(); // lazy — a pre-Awake drag can't NRE
        _gestureWasDrag = false; // new gesture
        _scroll?.OnInitializePotentialDrag(e);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        _scroll ??= GetComponentInParent<ScrollRect>(); // lazy — covers OnDrag/OnEndDrag this gesture
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
        KillTweens();
        _baseX = IsOpen ? -revealWidth : 0f;
    }

    public void OnDrag(PointerEventData e)
    {
        if (_routeToParent) { _scroll?.OnDrag(e); return; }
        if (!_dragging) return;
        float dx = e.position.x - e.pressPosition.x;
        float x = Mathf.Clamp(_baseX + dx, -revealWidth, 0f);
        SetContentX(x);
        PlaceButtons(x);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (_routeToParent) { _scroll?.OnEndDrag(e); _routeToParent = false; return; }
        if (!_dragging) return;
        _dragging = false;

        if (Rt.anchoredPosition.x <= -revealWidth * 0.5f) Open();
        else Close();
    }

    // Lazily resolved so any public entry point that runs BEFORE Awake never dereferences a
    // null _rt. ChatItemView.Bind → ResetClosed → SetContentX can fire on a row that was
    // Instantiate()'d while the chat-list panel was inactive (Awake is deferred until the
    // hierarchy activates) — that was the D6 post-bot-creation NullReferenceException. Awake
    // still assigns _rt on the normal path; this is the pre-Awake safety net (channel-agnostic:
    // the NRE fires on the WhatsApp path too, so the guard must protect both).
    private RectTransform Rt => _rt != null ? _rt : (_rt = (RectTransform)transform);

    private void SetContentX(float x)
        => Rt.anchoredPosition = new Vector2(x, Rt.anchoredPosition.y);

    // Every button (pivot/anchor on the row's right edge) rides with the content: its x is its open x + the reveal + the
    // content's x — parked just past the right edge at rest (content 0) and at its open x once the row is fully open (content
    // -revealWidth), so the buttons' left edge tracks the content's right edge.
    private void PlaceButtons(float contentX)
    {
        Place(deleteButton, _deleteOpenX + revealWidth + contentX);
        Place(moveButton, _moveOpenX + revealWidth + contentX);
    }

    private void AnimateButtonsTo(float contentTargetX)
    {
        _deleteSnap = SnapButton(deleteButton, _deleteOpenX + revealWidth + contentTargetX);
        _moveSnap = SnapButton(moveButton, _moveOpenX + revealWidth + contentTargetX);
    }

    private void KillTweens()
    {
        _snap?.Kill(); _snap = null;
        _deleteSnap?.Kill(); _deleteSnap = null;
        _moveSnap?.Kill(); _moveSnap = null;
    }

    private static void Place(RectTransform button, float x)
    {
        if (button == null) return;
        button.anchoredPosition = new Vector2(x, button.anchoredPosition.y);
    }

    // A hidden button is placed, never animated.
    private static Tween SnapButton(RectTransform button, float x)
    {
        if (button == null || !button.gameObject.activeSelf) return null;
        return button.DOAnchorPosX(x, SnapSeconds).SetEase(Ease.OutCubic);
    }

    private static float WidthOf(RectTransform button) => button != null ? button.rect.width : 0f;

    private static void SetShown(RectTransform button, bool shown)
    {
        if (button != null && button.gameObject.activeSelf != shown) button.gameObject.SetActive(shown);
    }
}

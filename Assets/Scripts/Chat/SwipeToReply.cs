using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Swipe-right-to-reply on a message bubble. Lives on the Bubble GameObject (next to
/// MessageBubbleLongPress). A right-drag slides the bubble; releasing past a threshold starts
/// a reply (<see cref="ChatManager.BeginReply"/> on the bound message). Vertical and left drags
/// are forwarded to the parent message ScrollRect (the AudioWaveform pattern) so list scrolling
/// is unaffected, and it never toggles ScrollRect.vertical (that is SwipeToBack's job).
///
/// Because this is the closest drag handler on the bubble, it claims swipes that land on a
/// bubble (including over media) before the screen-level SwipeToBack — so swipe-back still works
/// on the empty chat areas, but a swipe that starts on a message means "reply", WhatsApp-style.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SwipeToReply : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private const float CommitDistance = 90f;    // release past this (px) triggers the reply
    private const float MaxDrag        = 130f;   // visual slide clamp
    private const float SnapSeconds    = 0.18f;

    private RectTransform _rt;
    private MessageItemView _view;
    private ScrollRect _scroll;
    private bool _routeToParent;
    private bool _replying;
    private float _baseX;
    private Tween _snap;

    private void Awake()
    {
        _view = GetComponentInParent<MessageItemView>();
        // Slide the whole message row (the MessageItemView root), not just the Bubble — the
        // Outline/TailOutline are siblings of the Bubble, so moving only the Bubble leaves them
        // behind. The root carries them all.
        _rt = _view != null ? (RectTransform)_view.transform : (RectTransform)transform;
        _scroll = GetComponentInParent<ScrollRect>();
    }

    private void OnDisable()
    {
        _snap?.Kill();
        _snap = null;
        _routeToParent = false;
        _replying = false;
    }

    public void OnInitializePotentialDrag(PointerEventData e) => _scroll?.OnInitializePotentialDrag(e);

    public void OnBeginDrag(PointerEventData e)
    {
        Vector2 traj = e.position - e.pressPosition;
        bool rightSwipe = Mathf.Abs(traj.x) > Mathf.Abs(traj.y) && traj.x > 0f;
        bool blocked = ScrollClickBlocker.IsBlocking || SwipeToBack.IsSliding;

        // Vertical, left, mid-fling, or unbound → this is not a reply gesture; hand the whole
        // drag to the ScrollRect so the list scrolls normally.
        if (!rightSwipe || blocked || _view == null || _view.BoundVm == null)
        {
            _routeToParent = true;
            _scroll?.OnBeginDrag(e);
            return;
        }

        _replying = true;
        _snap?.Kill();
        _baseX = _rt.anchoredPosition.x;
    }

    public void OnDrag(PointerEventData e)
    {
        if (_routeToParent) { _scroll?.OnDrag(e); return; }
        if (!_replying) return;
        float dx = Mathf.Clamp(e.position.x - e.pressPosition.x, 0f, MaxDrag);
        _rt.anchoredPosition = new Vector2(_baseX + dx, _rt.anchoredPosition.y);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (_routeToParent) { _scroll?.OnEndDrag(e); _routeToParent = false; return; }
        if (!_replying) return;
        _replying = false;

        if (e.position.x - e.pressPosition.x >= CommitDistance && _view != null && _view.BoundVm != null)
            ChatManager.Instance?.BeginReply(_view.BoundVm);

        _snap?.Kill();
        _snap = _rt.DOAnchorPosX(_baseX, SnapSeconds).SetEase(Ease.OutCubic);
    }
}

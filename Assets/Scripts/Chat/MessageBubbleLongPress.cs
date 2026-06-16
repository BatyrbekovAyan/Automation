using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Opens the reaction bar when a message bubble is held. Implements ONLY the pointer
/// (down/up) interfaces — never drag — so vertical scroll bubbles to the parent
/// ScrollRect untouched (same coexistence trick as ClickPassthrough /
/// DelayedFingerUpAction). Cancels if the finger drifts past the drag slop, if a
/// fling/slide is in progress, or on release before the hold elapses.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class MessageBubbleLongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private float holdSeconds = 0.45f;
    [SerializeField] private float moveCancelPixels = 16f;

    private MessageItemView _view;
    private Coroutine _holdRoutine;
    private Vector2 _downPos;

    private void Awake() => _view = GetComponentInParent<MessageItemView>();

    public void OnPointerDown(PointerEventData eventData)
    {
        _downPos = eventData.position;
        if (_holdRoutine != null) StopCoroutine(_holdRoutine);
        _holdRoutine = StartCoroutine(HoldRoutine());
    }

    public void OnPointerUp(PointerEventData eventData) => CancelHold();
    private void OnDisable() => CancelHold();

    private void CancelHold()
    {
        if (_holdRoutine != null) { StopCoroutine(_holdRoutine); _holdRoutine = null; }
    }

    private IEnumerator HoldRoutine()
    {
        float elapsed = 0f;
        while (elapsed < holdSeconds)
        {
            if (SwipeToBack.IsSliding) yield break;                  // a swipe-back is animating
            if (Pointer.current != null)
            {
                Vector2 pos = Pointer.current.position.ReadValue();
                if (Vector2.Distance(pos, _downPos) > moveCancelPixels) yield break;  // turned into a scroll
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        _holdRoutine = null;
        if (ScrollClickBlocker.IsBlocking) yield break;             // landed on a flinging list
        if (_view == null || _view.BoundVm == null) yield break;
        ReactionBarController.Instance?.Show(_view);
    }
}

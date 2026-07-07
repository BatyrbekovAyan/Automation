using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Generic right-swipe-to-dismiss for a full-screen panel. Same drag mechanics
/// as SwipeToBackBotSettings but decoupled from BotsPage parallax and
/// BotSettings callbacks: the host wires <see cref="OnCommitted"/> instead.
/// Sits on a left-edge strip (built by ProfileSubPagesBuilder) together with
/// ClickPassthrough so taps still reach the content behind it.
/// </summary>
public class SwipeToBackPanel : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("UI References")]
    [SerializeField] private RectTransform panelToSlide;
    [SerializeField] private ScrollRect contentScrollRect; // optional: vertical drags forward here

    [Header("Swipe Physics")]
    [SerializeField] private float snapSpeed = 10f;
    [SerializeField] private float slowSwipeThreshold = 0.4f;
    [SerializeField] private float flickVelocityThreshold = 1000f;
    [SerializeField] private float minSnapSpeed = 1500f;

    /// <summary>Fires once the panel has fully slid off-screen after a committed swipe.</summary>
    public Action OnCommitted;

    private Canvas canvas;
    private Coroutine snapCoroutine;
    private bool dragDecided;
    private bool isHorizontalDrag;
    private float dragStartTime;
    private Vector2 dragStartPos;

    private void Awake()
    {
        var localCanvas = GetComponentInParent<Canvas>();
        if (localCanvas != null) canvas = localCanvas.rootCanvas;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        dragDecided = false;
        if (contentScrollRect != null) contentScrollRect.OnInitializePotentialDrag(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        var trajectory = eventData.position - eventData.pressPosition;
        var mostlyHorizontal = Mathf.Abs(trajectory.x) > Mathf.Abs(trajectory.y);
        var swipingRight = trajectory.x > 0f;

        if (mostlyHorizontal && swipingRight)
        {
            isHorizontalDrag = true;
            if (snapCoroutine != null) { StopCoroutine(snapCoroutine); snapCoroutine = null; }
            if (contentScrollRect != null) contentScrollRect.vertical = false;
        }
        else
        {
            isHorizontalDrag = false;
            if (contentScrollRect != null) contentScrollRect.OnBeginDrag(eventData);
        }

        dragDecided = true;
        dragStartTime = Time.unscaledTime;
        dragStartPos = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragDecided) return;

        if (isHorizontalDrag)
        {
            if (panelToSlide == null) return;
            var scaleFactor = canvas != null ? canvas.scaleFactor : 1f;
            var deltaX = eventData.delta.x / scaleFactor;
            var newX = Mathf.Max(0f, panelToSlide.anchoredPosition.x + deltaX);
            SetPanelX(newX);
        }
        else if (contentScrollRect != null)
        {
            contentScrollRect.OnDrag(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragDecided) return;

        if (isHorizontalDrag)
        {
            if (panelToSlide != null)
            {
                var screenWidth = GetScreenWidth();
                var dragDuration = Mathf.Max(0.0001f, Time.unscaledTime - dragStartTime);
                var dragDistanceX = eventData.position.x - dragStartPos.x;
                var velocityX = dragDistanceX / dragDuration;

                var fastFlick = velocityX > flickVelocityThreshold && dragDistanceX > 20f;
                var pastThreshold = panelToSlide.anchoredPosition.x > screenWidth * slowSwipeThreshold;

                snapCoroutine = fastFlick || pastThreshold
                    ? StartCoroutine(SnapToPosition(screenWidth, commit: true))
                    : StartCoroutine(SnapToPosition(0f, commit: false));
            }

            if (contentScrollRect != null) contentScrollRect.vertical = true;
        }
        else if (contentScrollRect != null)
        {
            contentScrollRect.OnEndDrag(eventData);
        }

        dragDecided = false;
        isHorizontalDrag = false;
    }

    private IEnumerator SnapToPosition(float targetX, bool commit)
    {
        while (Mathf.Abs(panelToSlide.anchoredPosition.x - targetX) > 2f)
        {
            var currentX = panelToSlide.anchoredPosition.x;
            var newX = Mathf.Lerp(currentX, targetX, Time.deltaTime * snapSpeed);

            var minStep = minSnapSpeed * Time.deltaTime;
            if (Mathf.Abs(newX - currentX) < minStep)
                newX = Mathf.MoveTowards(currentX, targetX, minStep);

            SetPanelX(newX);
            yield return null;
        }

        SetPanelX(targetX);
        snapCoroutine = null;

        if (commit) OnCommitted?.Invoke();
    }

    private void SetPanelX(float x)
    {
        if (panelToSlide == null) return;
        panelToSlide.anchoredPosition = new Vector2(x, panelToSlide.anchoredPosition.y);
    }

    private float GetScreenWidth() =>
        canvas != null ? canvas.GetComponent<RectTransform>().rect.width : Screen.width;
}

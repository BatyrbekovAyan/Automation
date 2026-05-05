using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SwipeToBackBotSettings : MonoBehaviour,
    IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public static SwipeToBackBotSettings Instance;

    [Header("UI References")]
    [SerializeField] private RectTransform botSettingsPanelToSlide;

    // BotsPage lives in Main.unity and cannot be serialized on a prefab
    // component (prefab assets cannot hold references to scene objects).
    // Resolve it lazily via the BotsPage singleton instead.
    private RectTransform botsPagePanelCached;
    private RectTransform BotsPagePanel
    {
        get
        {
            if (botsPagePanelCached != null) return botsPagePanelCached;
            if (BotsPage.Instance != null)
                botsPagePanelCached = BotsPage.Instance.GetComponent<RectTransform>();
            return botsPagePanelCached;
        }
    }

    [Header("Swipe Physics")]
    [Range(0.1f, 1f)] [SerializeField] private float parallaxStrength = 0.3f;
    [SerializeField] private float snapSpeed = 10f;
    [SerializeField] private float slowSwipeThreshold = 0.4f;
    [SerializeField] private float flickVelocityThreshold = 1000f;
    [SerializeField] private float minSnapSpeed = 1500f;

    private Canvas canvas;
    private Coroutine snapCoroutine;
    private bool dragDecided;
    private bool isHorizontalDrag;
    private float dragStartTime;
    private Vector2 dragStartPos;
    private ScrollRect dragScrollRect;

    public bool IsAnimating => snapCoroutine != null;

    private void Awake()
    {
        Instance = this;
        var localCanvas = GetComponentInParent<Canvas>();
        if (localCanvas != null) canvas = localCanvas.rootCanvas;
        if (EventSystem.current != null) EventSystem.current.pixelDragThreshold = 15;
    }

    // Called by Bot.OpenSettings() after activating the BotSettings wrapper.
    // BotsPage must still be active when this is invoked so the parallax is
    // visible; the onComplete callback deactivates BotsPage once the slide
    // finishes.
    public void SlideInFromRight(Action onComplete = null)
    {
        if (botSettingsPanelToSlide == null) { onComplete?.Invoke(); return; }
        var screenWidth = GetScreenWidth();

        SetPanelX(botSettingsPanelToSlide, screenWidth);
        SetPanelX(BotsPagePanel, 0f);

        if (snapCoroutine != null) StopCoroutine(snapCoroutine);
        snapCoroutine = StartCoroutine(SnapToPosition(0f, commitBack: false, onComplete: onComplete));
    }

    // Called by BotSettings.OnBackPressed() after the revert step and after
    // BotsPage has been re-activated. When the animation finishes, onComplete
    // runs — BotSettings uses that to deactivate its wrapper.
    public void SlideOutToBotsPage(Action onComplete = null)
    {
        if (botSettingsPanelToSlide == null) { onComplete?.Invoke(); return; }
        var screenWidth = GetScreenWidth();

        if (snapCoroutine != null) StopCoroutine(snapCoroutine);
        snapCoroutine = StartCoroutine(SnapToPosition(screenWidth, commitBack: false, onComplete: onComplete));
    }

    // One coroutine powers both directions. commitBack=true means "call
    // BotSettings.OnBackPressed() at the end" — used only by the gesture
    // path (see Task 4). Programmatic callers pass commitBack=false.
    private IEnumerator SnapToPosition(float targetX, bool commitBack, Action onComplete = null)
    {
        var screenWidth = GetScreenWidth();
        var maxOffset = screenWidth * parallaxStrength;

        while (Mathf.Abs(botSettingsPanelToSlide.anchoredPosition.x - targetX) > 2f)
        {
            var currentX = botSettingsPanelToSlide.anchoredPosition.x;
            var newX = Mathf.Lerp(currentX, targetX, Time.deltaTime * snapSpeed);

            var minStep = minSnapSpeed * Time.deltaTime;
            if (Mathf.Abs(newX - currentX) < minStep)
                newX = Mathf.MoveTowards(currentX, targetX, minStep);

            ApplyPositions(newX, screenWidth, maxOffset);
            yield return null;
        }

        ApplyPositions(targetX, screenWidth, maxOffset);
        snapCoroutine = null;

        if (commitBack && BotSettings.Instance != null)
            BotSettings.Instance.OnSwipeCommitted();

        onComplete?.Invoke();
    }

    private void ApplyPositions(float panelX, float screenWidth, float maxOffset)
    {
        SetPanelX(botSettingsPanelToSlide, panelX);
        var bgPanel = BotsPagePanel;
        if (bgPanel != null)
        {
            var progress = panelX / screenWidth;
            SetPanelX(bgPanel, -maxOffset + (maxOffset * progress));
        }
    }

    private static void SetPanelX(RectTransform rt, float x)
    {
        if (rt == null) return;
        rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
    }

    private float GetScreenWidth() =>
        canvas != null ? canvas.GetComponent<RectTransform>().rect.width : Screen.width;

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        dragDecided = false;
        dragScrollRect = BotSettings.Instance != null ? BotSettings.Instance.CurrentTabScrollRect : null;
        if (dragScrollRect != null) dragScrollRect.OnInitializePotentialDrag(eventData);
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
            if (dragScrollRect != null) dragScrollRect.vertical = false;
            var bgPanel = BotsPagePanel;
            if (bgPanel != null) bgPanel.gameObject.SetActive(true);
        }
        else
        {
            isHorizontalDrag = false;
            if (dragScrollRect != null) dragScrollRect.OnBeginDrag(eventData);
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
            var scaleFactor = canvas != null ? canvas.scaleFactor : 1f;
            var deltaX = eventData.delta.x / scaleFactor;
            var newX = Mathf.Max(0f, botSettingsPanelToSlide.anchoredPosition.x + deltaX);

            var screenWidth = GetScreenWidth();
            var maxOffset = screenWidth * parallaxStrength;
            ApplyPositions(newX, screenWidth, maxOffset);
        }
        else if (dragScrollRect != null)
        {
            dragScrollRect.OnDrag(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragDecided) return;

        if (isHorizontalDrag)
        {
            var screenWidth = GetScreenWidth();
            var dragDuration = Mathf.Max(0.0001f, Time.unscaledTime - dragStartTime);
            var dragDistanceX = eventData.position.x - dragStartPos.x;
            var velocityX = dragDistanceX / dragDuration;

            var fastFlick = velocityX > flickVelocityThreshold && dragDistanceX > 20f;
            var pastThreshold = botSettingsPanelToSlide.anchoredPosition.x > (screenWidth * slowSwipeThreshold);

            if (fastFlick || pastThreshold)
                snapCoroutine = StartCoroutine(SnapToPosition(screenWidth, commitBack: true));
            else
                snapCoroutine = StartCoroutine(SnapToPosition(0f, commitBack: false));

            if (dragScrollRect != null) dragScrollRect.vertical = true;
        }
        else if (dragScrollRect != null)
        {
            dragScrollRect.OnEndDrag(eventData);
        }

        dragDecided = false;
        isHorizontalDrag = false;
    }
}

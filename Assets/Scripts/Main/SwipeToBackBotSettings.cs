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
    [SerializeField] private RectTransform botsPagePanel;

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
        SetPanelX(botsPagePanel, 0f);

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
        if (botsPagePanel != null)
        {
            var progress = panelX / screenWidth;
            SetPanelX(botsPagePanel, -maxOffset + (maxOffset * progress));
        }
    }

    private static void SetPanelX(RectTransform rt, float x)
    {
        if (rt == null) return;
        rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
    }

    private float GetScreenWidth() =>
        canvas != null ? canvas.GetComponent<RectTransform>().rect.width : Screen.width;

    // Stubs — filled in Task 4.
    public void OnInitializePotentialDrag(PointerEventData eventData) { }
    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) { }
}

using System;
using System.Collections;
using System.Collections.Generic;
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

    // The tab's page scroll — frozen while a back-swipe is in flight.
    private ScrollRect pageScrollRect;
    // Where a VERTICAL drag goes. Not always the page: this strip is a
    // full-height band over the left edge that sits above the tab content, so
    // it wins the raycast over whatever the user actually aimed at.
    private ScrollRect dragScrollRect;
    private readonly List<RaycastResult> hitsUnderFinger = new List<RaycastResult>();

    public bool IsAnimating => snapCoroutine != null;

    private void Awake()
    {
        var localCanvas = GetComponentInParent<Canvas>();
        if (localCanvas != null) canvas = localCanvas.rootCanvas;
        if (EventSystem.current != null) EventSystem.current.pixelDragThreshold = 15;
    }

    // Each BotSettings prefab has its own SwipeBack child, so multiple
    // instances exist (one per bot). Claim the singleton on activation —
    // Bot.OpenSettings activates exactly one BotSettings at a time, so
    // OnEnable always runs on the SwipeBack the user is currently driving.
    private void OnEnable() => Instance = this;

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
        pageScrollRect = BotSettings.Instance != null ? BotSettings.Instance.CurrentTabScrollRect : null;
        dragScrollRect = ResolveVerticalTarget(eventData);

        // Stop momentum on both candidates — the real target is chosen at
        // drag-begin.
        if (dragScrollRect != null) dragScrollRect.OnInitializePotentialDrag(eventData);
        if (pageScrollRect != null && pageScrollRect != dragScrollRect)
            pageScrollRect.OnInitializePotentialDrag(eventData);
    }

    /// <summary>
    /// Which ScrollRect a vertical drag starting on this strip belongs to.
    ///
    /// The strip is a transparent full-height band over the left edge of the
    /// screen, and it is a later sibling than the tab content, so it wins the
    /// pointer raycast over every card beneath it — the card's own DragShield
    /// never sees the gesture. Forwarding blindly to the page (which is what
    /// this used to do) meant a vertical drag anywhere in that band scrolled
    /// the page even when the finger was on a text card with more text than it
    /// can show. Look past ourselves at what the user actually touched and
    /// apply the same ownership rule DragShield uses.
    /// </summary>
    private ScrollRect ResolveVerticalTarget(PointerEventData eventData)
    {
        if (EventSystem.current == null) return pageScrollRect;

        hitsUnderFinger.Clear();
        EventSystem.current.RaycastAll(eventData, hitsUnderFinger);

        foreach (var hit in hitsUnderFinger)
        {
            if (hit.gameObject == gameObject) continue;

            var scroll = hit.gameObject.GetComponentInParent<ScrollRect>();
            if (scroll == null) continue;

            // First scroller under the finger decides: either it is the page
            // itself, or it is the card the user is pointing at.
            if (scroll == pageScrollRect) return pageScrollRect;

            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            var hasHiddenText = scroll.content != null && DragScrollRouting.HasHiddenText(
                scroll.content.rect.height, viewport.rect.height);

            return DragScrollRouting.Resolve(
                hasInnerScroll: true, hasHiddenText, hasPageScroll: pageScrollRect != null)
                    == DragScrollRouting.Target.InnerText
                ? scroll
                : pageScrollRect;
        }

        return pageScrollRect;
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
            SetVerticalScrolling(false);
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

            SetVerticalScrolling(true);
        }
        else if (dragScrollRect != null)
        {
            dragScrollRect.OnEndDrag(eventData);
        }

        dragDecided = false;
        isHorizontalDrag = false;
    }

    // A back-swipe must not leave either candidate free-scrolling underneath
    // it — the page, and the card the gesture would otherwise have driven.
    private void SetVerticalScrolling(bool enabled)
    {
        if (pageScrollRect != null) pageScrollRect.vertical = enabled;
        if (dragScrollRect != null && dragScrollRect != pageScrollRect)
            dragScrollRect.vertical = enabled;
    }
}

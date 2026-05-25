using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using System.Collections;

public class SwipeToBack : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public static SwipeToBack Instance; // Added so ChatManager can call it easily!

    [Header("UI References")]
    public RectTransform chatPanelToSlide;
    public RectTransform chatListPanel; // <--- NEW: The background panel to move!
    public RectTransform bottomTabPanel; // Bottom navigation bar to slide with chat list
    public ScrollRect chatScrollRect;
    
    [Header("Swipe Physics Settings")]
    [Range(0.1f, 1f)]
    public float parallaxStrength = 0.3f; // <--- NEW: How far the background moves (30%)

    [Tooltip("Slide-IN duration in seconds (chat-list → chat panel). Ease-out cubic (decelerate into rest).")]
    [Range(0.15f, 0.6f)]
    public float slideInDuration = 0.290f;

    [Tooltip("Slide-OUT duration in seconds (chat panel → chat list). Ease-out cubic (peak velocity at t=0 so the animation picks up the finger's release momentum).")]
    [Range(0.15f, 0.6f)]
    public float slideOutDuration = 0.320f;

    [Tooltip("DEPRECATED: legacy exponential-lerp speed. Unused now that animation is fixed-duration. Kept for serialization compatibility; remove on next prefab cleanup.")]
    public float snapSpeed = 10f;

    public float slowSwipeThreshold = 0.4f;
    public float flickVelocityThreshold = 1000f;

    [Header("Action")]
    public UnityEvent onSwipeComplete;

    private Canvas canvas;
    private bool isHorizontalDrag = false;
    private bool dragDecided = false;
    private Coroutine snapCoroutine;

    private float dragStartTime;
    private Vector2 dragStartPos;

    /// <summary>
    /// True whenever a slide animation is running (in, out, or swipe-back
    /// snap). Read by MessageItemView.AcquireDecodeSlot and
    /// ChatManager.SyncLatestMessages to pause their heavy main-thread work
    /// during slides — image decode (~30ms each) and JSON parse (~100-300ms)
    /// would otherwise drop frames and make the slide look laggy.
    /// </summary>
    public static bool IsSliding { get; private set; }

    /// <summary>
    /// Fires the frame after a slide-out finishes and the chat panel is deactivated.
    /// Subscribers (currently MessageListView) free per-chat state — destroying
    /// bubbles here recovers their owned textures immediately instead of waiting
    /// for the next chat-open to clear them.
    /// </summary>
    public static event System.Action OnSlideOutComplete;

    /// <summary>
    /// Ease-out cubic: starts at full speed, decelerates into the target. Used by all
    /// slide directions — slide-in (settles into the on-screen rest position),
    /// drag-cancel snap-back-to-zero (same "settle to rest" motion), and drag-commit
    /// slide-out (peak velocity at t=0 picks up the finger's release momentum so
    /// there's no perceived pause before the animation takes over).
    /// </summary>
    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    void Awake()
    {
        Instance = this;
        Canvas localCanvas = GetComponentInParent<Canvas>();
        if (localCanvas != null)
        {
            canvas = localCanvas.rootCanvas;
        }
        if (EventSystem.current != null) EventSystem.current.pixelDragThreshold = 15;
    }

    // --- NEW: Call this from ChatManager.SelectChat() to animate IN ---
    public void SlideInToMessages(System.Action onComplete = null)
    {
        chatPanelToSlide.gameObject.SetActive(true);
        if (chatListPanel) chatListPanel.gameObject.SetActive(true);

        float screenWidth = canvas.GetComponent<RectTransform>().rect.width;

        // Snap to off-screen BEFORE restoring alpha so there's no single-
        // frame window where the panel is visible (alpha=1) at the wrong
        // pre-window position. If a parent LayoutGroup or OnEnable handler
        // moved the panel during pre-window, this resets it before the user
        // can see anything.
        chatPanelToSlide.anchoredPosition = new Vector2(screenWidth, chatPanelToSlide.anchoredPosition.y);

        // Now restore visibility. The pre-snap above guarantees the panel is at
        // screenWidth before alpha=1 takes effect, so SnapToPosition's first frame
        // animates from off-screen rather than from a stale layout position.
        var cg = chatPanelToSlide.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.blocksRaycasts = true;
        }

        if (snapCoroutine != null) StopCoroutine(snapCoroutine);

        // Pass the callback to the Coroutine!
        snapCoroutine = StartCoroutine(SnapToPosition(0f, false, onComplete));
    }

    public void SlideOutToChatList(bool instant = false)
    {
        if (chatListPanel) chatListPanel.gameObject.SetActive(true);
        float screenWidth = canvas.GetComponent<RectTransform>().rect.width;
        
        if (snapCoroutine != null) StopCoroutine(snapCoroutine);

        if (instant)
        {
            chatPanelToSlide.anchoredPosition = new Vector2(screenWidth, chatPanelToSlide.anchoredPosition.y);
            if (chatListPanel != null) chatListPanel.anchoredPosition = new Vector2(0, chatListPanel.anchoredPosition.y);
            if (bottomTabPanel != null) bottomTabPanel.anchoredPosition = new Vector2(0, bottomTabPanel.anchoredPosition.y);
            chatPanelToSlide.gameObject.SetActive(false);
        }
        else
        {
            // Pass null for the callback here since we use the UnityEvent for closing
            snapCoroutine = StartCoroutine(SnapToPosition(screenWidth, true, null)); 
        }
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (chatScrollRect != null) chatScrollRect.OnInitializePotentialDrag(eventData);
        dragDecided = false; 
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        Vector2 dragTrajectory = eventData.position - eventData.pressPosition;
        bool isMostlyHorizontal = Mathf.Abs(dragTrajectory.x) > Mathf.Abs(dragTrajectory.y);
        bool isSwipingRight = dragTrajectory.x > 0;

        // Optional: Only allow swipe from the left 20% of the screen (iOS style)
        // float screenWidth = canvas.GetComponent<RectTransform>().rect.width;
        // if (eventData.pressPosition.x > screenWidth * 0.2f) isMostlyHorizontal = false;

        if (isMostlyHorizontal && isSwipingRight)
        {
            // Lock out swipe-back during the slide-in animation. Mid-tween cancellation
            // looks janky and would add state to SnapToPosition. The slide is brief
            // (~300 ms) so the lockout window is short. Slide-OUT itself is fine to
            // interact with (the user IS the one driving slide-out via drag); we only
            // block when phase is Slide AND the panel is moving in (anchoredPosition.x
            // approaching 0 from screenWidth).
            float screenWidth = canvas.GetComponent<RectTransform>().rect.width;
            bool isSlidingIn = ChatManager.Instance != null
                && ChatManager.Instance.Phase == ChatManager.ChatOpenPhase.Slide
                && chatPanelToSlide.anchoredPosition.x < screenWidth - 1f;
            if (isSlidingIn)
            {
                isHorizontalDrag = false;
                dragDecided = true;
                dragStartTime = Time.unscaledTime;
                dragStartPos = eventData.position;
                return;
            }

            isHorizontalDrag = true;
            IsSliding = true;
            if (snapCoroutine != null) StopCoroutine(snapCoroutine);

            if (chatScrollRect != null) chatScrollRect.vertical = false;
            if (chatListPanel) chatListPanel.gameObject.SetActive(true); // Ensure background is visible
        }
        else
        {
            isHorizontalDrag = false;
            if (chatScrollRect != null) chatScrollRect.OnBeginDrag(eventData);
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
            float deltaX = eventData.delta.x / canvas.scaleFactor;
            float newX = chatPanelToSlide.anchoredPosition.x + deltaX;
            if (newX < 0) newX = 0; 
            
            chatPanelToSlide.anchoredPosition = new Vector2(newX, chatPanelToSlide.anchoredPosition.y);

            // --- THE PARALLAX MATH ---
            float screenWidth = canvas.GetComponent<RectTransform>().rect.width;
            float progress = newX / screenWidth;
            float maxOffset = screenWidth * parallaxStrength;

            if (chatListPanel != null)
            {
                chatListPanel.anchoredPosition = new Vector2(-maxOffset + (maxOffset * progress), chatListPanel.anchoredPosition.y);
            }
            if (bottomTabPanel != null)
            {
                bottomTabPanel.anchoredPosition = new Vector2(-maxOffset + (maxOffset * progress), bottomTabPanel.anchoredPosition.y);
            }
        }
        else
        {
            if (chatScrollRect != null) chatScrollRect.OnDrag(eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragDecided) return;

        if (isHorizontalDrag)
        {
            float screenWidth = canvas.GetComponent<RectTransform>().rect.width;
            
            float dragDuration = Time.unscaledTime - dragStartTime;
            float dragDistanceX = eventData.position.x - dragStartPos.x;
            float velocityX = dragDuration > 0 ? (dragDistanceX / dragDuration) : 0f;

            bool isFastFlick = (velocityX > flickVelocityThreshold) && (dragDistanceX > 20f);
            bool isPastThreshold = chatPanelToSlide.anchoredPosition.x > (screenWidth * slowSwipeThreshold);

            if (isFastFlick || isPastThreshold)
            {
                snapCoroutine = StartCoroutine(SnapToPosition(screenWidth, true));
            }
            else
            {
                snapCoroutine = StartCoroutine(SnapToPosition(0f, false));
            }
            
            if (chatScrollRect != null) chatScrollRect.vertical = true;
        }
        else
        {
            if (chatScrollRect != null) chatScrollRect.OnEndDrag(eventData);
        }

        dragDecided = false;
    }

    private IEnumerator SnapToPosition(float targetX, bool triggerBack, System.Action onComplete = null)
    {
        // Mark the slide active for the duration of the animation. Heavy
        // main-thread work elsewhere (image decode in MessageItemView, JSON
        // parse in SyncLatestMessages) polls IsSliding and pauses while it's
        // true so the animation can hit its frame budget.
        IsSliding = true;

        float screenWidth = canvas.GetComponent<RectTransform>().rect.width;
        float maxOffset = screenWidth * parallaxStrength;

        // Note: SlideInToMessages handles its own pre-snap to screenWidth before calling
        // SnapToPosition, so no internal re-snap is needed for slide-in. Drag flows
        // (OnEndDrag) intentionally preserve the panel's current position so the animation
        // continues from wherever the finger released — a generic pre-snap here would
        // make drag-cancel jump to the screen edge and slide back instead of smoothly
        // returning from the finger's position.

        // Fixed-duration animation. Frame-rate independent: the same physical duration
        // regardless of whether the device runs at 60 fps or 30 fps.
        float startX = chatPanelToSlide.anchoredPosition.x;
        float startTime = Time.realtimeSinceStartup;
        float duration = triggerBack ? slideOutDuration : slideInDuration;

        // Both directions use ease-out cubic. Ease-out has peak velocity at t=0, so a
        // drag-completed slide-out continues smoothly from the finger's release momentum
        // rather than starting from rest (which felt like a "pause" before the animation
        // took over). Slide-in and drag-cancel decelerate into rest as before.

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed = Time.realtimeSinceStartup - startTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = EaseOutCubic(t);
            float newX = Mathf.Lerp(startX, targetX, eased);

            chatPanelToSlide.anchoredPosition = new Vector2(newX, chatPanelToSlide.anchoredPosition.y);

            if (chatListPanel != null)
            {
                float progress = newX / screenWidth;
                chatListPanel.anchoredPosition = new Vector2(-maxOffset + (maxOffset * progress), chatListPanel.anchoredPosition.y);
            }
            if (bottomTabPanel != null)
            {
                float progress = newX / screenWidth;
                bottomTabPanel.anchoredPosition = new Vector2(-maxOffset + (maxOffset * progress), bottomTabPanel.anchoredPosition.y);
            }

            yield return null;
        }

        // Final snap to exact target (eliminates any sub-pixel residual from the curve).
        chatPanelToSlide.anchoredPosition = new Vector2(targetX, chatPanelToSlide.anchoredPosition.y);
        float finalProgress = targetX / screenWidth;
        if (chatListPanel != null)
        {
            chatListPanel.anchoredPosition = new Vector2(-maxOffset + (maxOffset * finalProgress), chatListPanel.anchoredPosition.y);
        }
        if (bottomTabPanel != null)
        {
            bottomTabPanel.anchoredPosition = new Vector2(-maxOffset + (maxOffset * finalProgress), bottomTabPanel.anchoredPosition.y);
        }

        if (triggerBack)
        {
            // Fire OnSlideOutComplete BEFORE SetActive(false). MessageListView lives
            // under chatPanelToSlide, so deactivating it would trigger OnDisable and
            // unsubscribe the handler before the event fires.
            OnSlideOutComplete?.Invoke();
            chatPanelToSlide.gameObject.SetActive(false);
            onSwipeComplete?.Invoke();
        }
        else
        {
            if (chatListPanel != null) chatListPanel.gameObject.SetActive(false);
        }

        // --- 3. FIRE the callback now that the animation is 100% finished! ---
        onComplete?.Invoke();

        // Slide done — release the gate so paused decodes / sync processing
        // can resume on the main thread.
        IsSliding = false;
    }
}
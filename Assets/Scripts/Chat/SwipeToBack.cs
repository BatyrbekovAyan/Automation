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

    [Tooltip("Slide-OUT duration in seconds (chat panel → chat list). Ease-in-out cubic (accelerate then decelerate, WhatsApp-like).")]
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
    /// Ease-out cubic: starts at full speed, decelerates into the target. Used by
    /// slide-in (the panel settles into its on-screen rest position) and by the
    /// drag-cancel snap-back-to-zero (same "settle to rest" motion).
    /// </summary>
    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    /// <summary>
    /// Ease-in-out cubic: accelerates from rest in the first half, decelerates into
    /// the target in the second half. Used by slide-out — feels weightier and more
    /// natural than ease-out alone, matching WhatsApp/iOS swipe-back behavior.
    /// </summary>
    private static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.5f) return 4f * t * t * t;
        float inv = -2f * t + 2f;
        return 1f - inv * inv * inv / 2f;
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

        // Isolate the chat panel and chat list panel into their own sub-canvases. Without
        // these, every per-frame anchoredPosition update during slide-in/out dirties the
        // ENTIRE root canvas and forces Unity to re-batch all UI elements — visible as
        // dropped frames on heavy-media chats. With a sub-Canvas, moving the panel only
        // re-batches that panel's own children, and the root canvas geometry stays cached.
        EnsureSubCanvas(chatPanelToSlide, sortingOrder: 2);
        EnsureSubCanvas(chatListPanel,    sortingOrder: 1);
    }

    /// <summary>
    /// Add a Canvas + GraphicRaycaster to the given panel if not already present. The
    /// sub-canvas isolates this panel's batching from the root, so animating its
    /// RectTransform doesn't trigger a root-canvas rebuild. GraphicRaycaster restores
    /// child click/drag detection (otherwise children stop receiving pointer events).
    /// Cheap one-shot setup at Awake; no per-frame cost.
    /// </summary>
    private static void EnsureSubCanvas(RectTransform panel, int sortingOrder)
    {
        if (panel == null) return;

        var existingCanvas = panel.GetComponent<Canvas>();
        if (existingCanvas == null)
        {
            var c = panel.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = sortingOrder;
            // Inherit additional shader channels from the root so TextMeshPro features
            // (underline, gradient, etc.) keep working. TMP uses TexCoord1 for some shaders.
            c.additionalShaderChannels =
                UnityEngine.AdditionalCanvasShaderChannels.TexCoord1 |
                UnityEngine.AdditionalCanvasShaderChannels.Normal |
                UnityEngine.AdditionalCanvasShaderChannels.Tangent;
        }

        if (panel.GetComponent<GraphicRaycaster>() == null)
        {
            panel.gameObject.AddComponent<GraphicRaycaster>();
        }
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

        // Now restore visibility. SnapToPosition (below) does a second
        // re-assert of the off-screen position before its while loop, so by
        // the time alpha=1 takes effect and SnapToPosition runs its first
        // frame, the panel is guaranteed to be off-screen.
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

        // For slide-IN (target = 0), guarantee the panel STARTS off-screen.
        // SlideInToMessages sets this, but a parent LayoutGroup or OnEnable handler
        // can override anchoredPosition between activation and the first SnapToPosition
        // frame. Without this re-assert, the animation below can start from a stale
        // position and look like a snap instead of a slide.
        if (Mathf.Approximately(targetX, 0f))
        {
            chatPanelToSlide.anchoredPosition = new Vector2(screenWidth, chatPanelToSlide.anchoredPosition.y);
        }

        // Fixed-duration animation. Frame-rate independent: the same physical duration
        // regardless of whether the device runs at 60 fps or 30 fps. triggerBack=true
        // is the slide-OUT (panel exiting to screenWidth) — use ease-in-out cubic for
        // a weightier WhatsApp-like feel. Everything else (slide-IN from OpenChatRoutine,
        // drag-cancel snap-back-to-zero from OnEndDrag) settles into rest — use
        // ease-out cubic so the panel decelerates into its final position.
        float startX = chatPanelToSlide.anchoredPosition.x;
        float startTime = Time.realtimeSinceStartup;
        float duration = triggerBack ? slideOutDuration : slideInDuration;
        bool useEaseInOut = triggerBack;

        ChatManager.ChatOpenLog($"Slide-{(triggerBack ? "OUT" : "IN")} start (duration={duration*1000f:F0}ms, from x={startX:F0} to x={targetX:F0})");

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed = Time.realtimeSinceStartup - startTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = useEaseInOut ? EaseInOutCubic(t) : EaseOutCubic(t);
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

        float actualMs = (Time.realtimeSinceStartup - startTime) * 1000f;
        ChatManager.ChatOpenLog($"Slide-{(triggerBack ? "OUT" : "IN")} done (actual={actualMs:F0}ms)");

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
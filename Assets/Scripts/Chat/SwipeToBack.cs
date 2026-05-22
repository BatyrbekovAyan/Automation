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
        // SlideInToMessages sets this, but a parent LayoutGroup or OnEnable
        // handler can override anchoredPosition between activation and the
        // first SnapToPosition frame. Without this re-assert, the while loop
        // below can start with the panel already near target=0, exit in 2-3
        // frames, and make the slide look like a snap instead of an animation.
        if (Mathf.Approximately(targetX, 0f))
        {
            chatPanelToSlide.anchoredPosition = new Vector2(screenWidth, chatPanelToSlide.anchoredPosition.y);
        }

        while (Mathf.Abs(chatPanelToSlide.anchoredPosition.x - targetX) > 2f)
        {
            float currentX = chatPanelToSlide.anchoredPosition.x;
            float newX = Mathf.Lerp(currentX, targetX, Time.deltaTime * snapSpeed);

            float minSpeed = 1500f * Time.deltaTime;
            if (Mathf.Abs(newX - currentX) < minSpeed)
            {
                newX = Mathf.MoveTowards(currentX, targetX, minSpeed);
            }

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
            chatPanelToSlide.gameObject.SetActive(false);
            onSwipeComplete?.Invoke();
            OnSlideOutComplete?.Invoke();
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
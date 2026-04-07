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
        
        chatPanelToSlide.anchoredPosition = new Vector2(screenWidth, chatPanelToSlide.anchoredPosition.y);

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
            isHorizontalDrag = true;
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
        float screenWidth = canvas.GetComponent<RectTransform>().rect.width;
        float maxOffset = screenWidth * parallaxStrength;

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
        }
        else
        {
            if (chatListPanel != null) chatListPanel.gameObject.SetActive(false);
        }

        // --- 3. FIRE the callback now that the animation is 100% finished! ---
        onComplete?.Invoke(); 
    }
}
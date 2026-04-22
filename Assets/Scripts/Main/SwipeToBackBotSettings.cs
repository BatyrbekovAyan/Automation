using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Swipe-right-to-go-back + slide-in-from-right animation for the BotSettings
// page. Mirrors Assets/Scripts/Chat/SwipeToBack.cs but is scoped to the
// bot-settings flow (BotSettings wrapper + BotsPage parallax, no chat
// ScrollRect or bottom-tab panel).
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
    private ScrollRect dragScrollRect; // ScrollRect captured at drag-begin for restore

    public bool IsAnimating => snapCoroutine != null;

    private void Awake()
    {
        Instance = this;
        var localCanvas = GetComponentInParent<Canvas>();
        if (localCanvas != null) canvas = localCanvas.rootCanvas;
        if (EventSystem.current != null) EventSystem.current.pixelDragThreshold = 15;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData) { }
    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) { }
}

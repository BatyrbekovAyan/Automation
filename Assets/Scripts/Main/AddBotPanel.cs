using DG.Tweening;
using UnityEngine;

/// <summary>
/// Presents Screen_New (the Add-Bot form) as a slide-in overlay instead of a bottom
/// tab. Open()/Close() mirror ProfileSubPages: SetActive(true) then slide from the
/// right on open; slide out then SetActive(false) on close. Closed by
/// BottomTabManager on any tab switch and by Manager on creation-success.
/// </summary>
public class AddBotPanel : MonoBehaviour
{
    private const float SlideInDuration = 0.3f;
    private const float SlideOutDuration = 0.25f;

    private static AddBotPanel _instance;

    /// <summary>Resolves even while Screen_New is inactive (Awake hasn't run yet).</summary>
    public static AddBotPanel Instance =>
        _instance != null ? _instance
            : _instance = Object.FindFirstObjectByType<AddBotPanel>(FindObjectsInactive.Include);

    private RectTransform _rt;
    private Canvas _rootCanvas;
    private Tween _activeSlide;

    public bool IsOpen => gameObject.activeSelf;

    private void Awake()
    {
        _instance = this;
        EnsureInit();
        // Screen_New serializes inactive in the scene; hiding it is not this
        // component's job at startup (Unity never runs Awake on an inactive
        // GameObject anyway — this method only fires once it's already shown).
    }

    private void EnsureInit()
    {
        if (_rt == null) _rt = GetComponent<RectTransform>();
        if (_rootCanvas == null) _rootCanvas = GetComponentInParent<Canvas>(true);
    }

    public void Open()
    {
        EnsureInit();
        if (IsOpen) return;                      // idempotent — see EmptyStateView + zero-bot auto-open
        gameObject.SetActive(true);
        transform.SetAsLastSibling();            // draw above the Bots page
        _activeSlide?.Kill();
        _rt.anchoredPosition = new Vector2(CanvasWidth(), _rt.anchoredPosition.y);
        _activeSlide = _rt.DOAnchorPosX(0f, SlideInDuration).SetEase(Ease.OutCubic);
    }

    public void Close()
    {
        EnsureInit();
        if (!IsOpen) return;
        _activeSlide?.Kill();
        _activeSlide = _rt.DOAnchorPosX(CanvasWidth(), SlideOutDuration)
            .SetEase(Ease.InCubic)
            .OnComplete(() => gameObject.SetActive(false));
    }

    /// <summary>Instant hide with no tween — used when a tab switch must close us now.</summary>
    public void CloseImmediate()
    {
        EnsureInit();
        if (!IsOpen) return;
        _activeSlide?.Kill();
        _rt.anchoredPosition = new Vector2(CanvasWidth(), _rt.anchoredPosition.y);
        gameObject.SetActive(false);
    }

    private float CanvasWidth() =>
        _rootCanvas != null ? _rootCanvas.GetComponent<RectTransform>().rect.width : 1080f;
}

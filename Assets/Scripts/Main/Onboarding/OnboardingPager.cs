using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Horizontal snap pager for the onboarding carousel. On end-drag it settles on the
/// nearest page (OnboardingPageMath) with a 0.3s OutCubic tween and raises
/// OnPageChanged so the dot pills update. Deliberately NOT the existing vertical
/// flick-momentum ScrollRect subclass, which has no paging (RESEARCH Pitfall 1).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class OnboardingPager : ScrollRect
{
    [SerializeField] private int pageCount = 3;

    public System.Action<int> OnPageChanged;
    public int CurrentPage { get; private set; }

    private Tween _snap;

    protected override void Awake()
    {
        base.Awake();
        // Carousel config: horizontal-only, clamped, no inertia — snap owns the motion.
        horizontal = true;
        vertical = false;
        movementType = MovementType.Clamped;
        inertia = false;
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        base.OnEndDrag(eventData);
        SnapToNearest();
    }

    /// <summary>Snap to a specific page programmatically (e.g. after a dot tap — optional).</summary>
    public void GoToPage(int index) => AnimateTo(Mathf.Clamp(index, 0, pageCount - 1));

    private void SnapToNearest() =>
        AnimateTo(OnboardingPageMath.NearestPage(horizontalNormalizedPosition, pageCount));

    private void AnimateTo(int page)
    {
        float targetX = OnboardingPageMath.PageToNormalizedX(page, pageCount);
        _snap?.Kill();
        _snap = DOTween.To(() => horizontalNormalizedPosition,
                           x => horizontalNormalizedPosition = x,
                           targetX, 0.3f).SetEase(Ease.OutCubic);
        if (page != CurrentPage)
        {
            CurrentPage = page;
            OnPageChanged?.Invoke(page);
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        _snap?.Kill();
    }
}

using System.Reflection;
using DG.Tweening;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TMP_Dropdown variant that:
///   1) Drives an <see cref="ImageWithIndependentRoundedCorners"/> on the trigger so
///      the bottom corners flatten while the list is open and round again once it's
///      fully closed.
///   2) Replaces TMP's alpha fade with a Content slide animation. The list panel
///      shows instantly at its natural size; items inside slide down from above the
///      Viewport into view, clipped by the Viewport's existing Mask. We don't touch
///      the list root's size or the Viewport's size, so the Mask's stencil writes
///      and the items' stencil-test materials stay valid throughout — earlier
///      attempts that resized the list (sizeDelta.y or localScale.y) corrupted the
///      stencil pipeline and made items invisible until SetActive was toggled.
/// </summary>
public class RoundedCornerDropdown : TMP_Dropdown
{
    private static readonly int RadiusProp = Shader.PropertyToID("_r");
    private static readonly int HalfSizeProp = Shader.PropertyToID("_halfSize");
    private static readonly int Rect2PropsProp = Shader.PropertyToID("_rect2props");

    private static readonly FieldInfo CoroutineField =
        typeof(TMP_Dropdown).GetField("m_Coroutine", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo DropdownGoField =
        typeof(TMP_Dropdown).GetField("m_Dropdown", BindingFlags.NonPublic | BindingFlags.Instance);

    [Header("Rounded Corners")]
    [SerializeField] private ImageWithIndependentRoundedCorners corners;
    [SerializeField] private Vector4 closedRadii = new Vector4(40f, 40f, 40f, 40f);
    [SerializeField] private Vector4 openRadii = new Vector4(40f, 40f, 0f, 0f);

    [Header("Slide Animation")]
    [SerializeField] private float animDuration = 0.2f;
    [SerializeField] private Ease openEase = Ease.OutCubic;
    [SerializeField] private Ease closeEase = Ease.InCubic;

    private enum AnimState { Closed, Open, Closing }

    private Graphic cornersGraphic;
    private AnimState lastState = AnimState.Closed;
    private float slideDistance;

    // Per-list cache; refreshed when the spawned list GameObject changes.
    private GameObject cachedListGo;
    private CanvasGroup cachedListCanvasGroup;
    private RectTransform cachedListRect;
    private ScrollRect cachedListScrollRect;
    private RectTransform cachedListContent;

    protected override void Awake()
    {
        base.Awake();
        if (corners != null)
            cornersGraphic = corners.GetComponent<Graphic>();

        // Sync TMP's post-Hide destroy timer with our slide-out animation length so
        // the list isn't destroyed before items finish sliding away. Alpha fade
        // itself is defeated below by forcing CanvasGroup.alpha = 1 each frame.
        alphaFadeSpeed = animDuration;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        lastState = AnimState.Closed;
        ApplyRadii(closedRadii);
    }

    protected override void OnDisable()
    {
        if (cachedListContent != null) DOTween.Kill(cachedListContent);
        base.OnDisable();
        ClearListCache();
        lastState = AnimState.Closed;
        ApplyRadii(closedRadii);
    }

    private void LateUpdate()
    {
        RefreshListCache();

        AnimState now;
        if (!IsExpanded) now = AnimState.Closed;
        else if (IsFadingOut()) now = AnimState.Closing;
        else now = AnimState.Open;

        if (now != lastState)
        {
            OnStateChanged(lastState, now);
            lastState = now;
        }

        // Defeat TMP's CanvasGroup.alpha fade so the list panel is fully opaque
        // throughout — our slide animation owns the visible motion.
        if (cachedListCanvasGroup != null && cachedListCanvasGroup.alpha < 1f)
            cachedListCanvasGroup.alpha = 1f;
    }

    private void OnStateChanged(AnimState from, AnimState to)
    {
        switch (to)
        {
            case AnimState.Open:
                ApplyRadii(openRadii);
                if (from == AnimState.Closed) StartSlideIn();
                break;

            case AnimState.Closing:
                StartSlideOut();
                break;

            case AnimState.Closed:
                ApplyRadii(closedRadii);
                break;
        }
    }

    private void StartSlideIn()
    {
        if (cachedListContent == null || cachedListRect == null) return;

        // Distance = list height; that's enough to push every item entirely above
        // the Viewport top so nothing peeks at the start of the slide.
        slideDistance = cachedListRect.sizeDelta.y;

        // Take ownership of Content's position from ScrollRect for the duration of
        // the slide; clamp logic would otherwise yank items back into the viewport.
        SetScrollRectEnabled(false);
        SetContentY(slideDistance);
        TweenContentY(0f, openEase, OnSlideInComplete);
    }

    private void StartSlideOut()
    {
        if (cachedListContent == null) return;
        SetScrollRectEnabled(false);
        TweenContentY(slideDistance, closeEase);
    }

    private void OnSlideInComplete()
    {
        // Hand scrolling back to the user; pin to top so the first item is in view
        // even when there's enough content to scroll further.
        if (cachedListScrollRect == null) return;
        cachedListScrollRect.enabled = true;
        cachedListScrollRect.verticalNormalizedPosition = 1f;
    }

    private void SetScrollRectEnabled(bool value)
    {
        if (cachedListScrollRect != null)
            cachedListScrollRect.enabled = value;
    }

    private void SetContentY(float y)
    {
        if (cachedListContent == null) return;
        var pos = cachedListContent.anchoredPosition;
        cachedListContent.anchoredPosition = new Vector2(pos.x, y);
    }

    private void TweenContentY(float target, Ease ease, TweenCallback onComplete = null)
    {
        var content = cachedListContent;
        if (content == null) return;
        DOTween.Kill(content);
        var tween = DOTween.To(
                () => content.anchoredPosition.y,
                value =>
                {
                    var pos = content.anchoredPosition;
                    content.anchoredPosition = new Vector2(pos.x, value);
                },
                target,
                animDuration)
            .SetEase(ease)
            .SetUpdate(true)
            .SetTarget(content);
        if (onComplete != null) tween.OnComplete(onComplete);
    }

    private void RefreshListCache()
    {
        var listGo = DropdownGoField?.GetValue(this) as GameObject;
        if (listGo == cachedListGo) return;

        cachedListGo = listGo;
        if (listGo == null)
        {
            cachedListCanvasGroup = null;
            cachedListRect = null;
            cachedListScrollRect = null;
            cachedListContent = null;
        }
        else
        {
            cachedListCanvasGroup = listGo.GetComponent<CanvasGroup>();
            cachedListRect = listGo.transform as RectTransform;
            cachedListScrollRect = listGo.GetComponent<ScrollRect>();
            cachedListContent = cachedListScrollRect != null ? cachedListScrollRect.content : null;
        }
    }

    private void ClearListCache()
    {
        cachedListGo = null;
        cachedListCanvasGroup = null;
        cachedListRect = null;
        cachedListScrollRect = null;
        cachedListContent = null;
    }

    private bool IsFadingOut() =>
        CoroutineField != null && CoroutineField.GetValue(this) != null;

    private void ApplyRadii(Vector4 radii)
    {
        if (corners == null) return;
        if (corners.r == radii) return;

        corners.r = radii;
        // Refresh writes _r, _halfSize, _rect2props to the base material. _rect2props
        // is recomputed from _r and the rect size — these three must stay in sync or
        // the shader produces malformed corners (looks like 45° cuts instead of arcs).
        corners.Validate();
        corners.Refresh();

        if (cornersGraphic == null) return;

        // Mask/RectMask2D in the parent hierarchy causes MaskableGraphic to render a
        // stencil-wrapped COPY of the base material. That copy snapshotted props at
        // creation and won't pick up our SetVector calls, so mirror all three coupled
        // uniforms from the freshly-refreshed base material onto the render material.
        var baseMat = cornersGraphic.material;
        var renderMat = cornersGraphic.materialForRendering;
        if (baseMat != null && renderMat != null && renderMat != baseMat)
        {
            renderMat.SetVector(RadiusProp, baseMat.GetVector(RadiusProp));
            renderMat.SetVector(HalfSizeProp, baseMat.GetVector(HalfSizeProp));
            renderMat.SetVector(Rect2PropsProp, baseMat.GetVector(Rect2PropsProp));
        }

        cornersGraphic.SetMaterialDirty();
    }
}

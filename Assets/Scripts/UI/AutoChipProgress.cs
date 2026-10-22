using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The learning fill of one «Авто» capsule (spec 2026-10-22-learning-progress-percent §4.3), laid over the capsule's inner Fill
/// (<see cref="baseFill"/>) and under its lamp and label:
/// <list type="bullet">
///   <item><see cref="clip"/> — a RectMask2D pinned to the inner Fill's left edge; its width is the fraction of the inner width.</item>
///   <item><see cref="fill"/> — the rounded fill, always the inner Fill's full size. Only the clip moves, so the fill's left end
///         keeps the capsule's rounding and its right end is the clip's straight cut. And no rounded image ever resizes with
///         the percent: under the bot-switcher sheet's stencil Mask Nobi's corners lag behind a resizing image
///         (RoundedCornerMaskSync), which is also why the cut is a RectMask2D and never a stencil Mask.</item>
/// </list>
/// Painted only by ReplyModeToggleBinder.PaintChip, which hands it AutoChipLook's fraction and fill colour. A growth on a
/// capsule that is on screen tweens; a lower fill, a hidden capsule and a plain repaint land at once. It sits on the capsule's
/// Pill, so it also hears the capsule resize (the bot card's «Подключить» ↔ «Авто») and refits itself to the new inner width.
/// </summary>
public class AutoChipProgress : MonoBehaviour
{
    /// <summary>The growth tween's length (spec §4.3: about 0.4 s, OutCubic).</summary>
    public const float GrowSeconds = 0.4f;

    // The fill changes colour together with the rest of the capsule: the painter's cross-fade (PaintChip's default duration).
    private const float ColorSeconds = 0.22f;

    [SerializeField] private RectTransform clip;      // RectMask2D, pinned left; width = fraction × baseFill's width
    [SerializeField] private Image fill;              // rounded, always baseFill's size
    [SerializeField] private RectTransform baseFill;  // the capsule's existing inner Fill

    private Vector2 fittedInner;   // the inner Fill size the layer was last fitted to

    /// <summary>The fraction (0..1) the last <see cref="Apply"/> asked for — already the target while a growth still runs.</summary>
    public float AppliedFraction { get; private set; }

    /// <summary>
    /// Shows <paramref name="fraction"/> (0..1) of the capsule in <paramref name="color"/>; 0 is a zero-width clip that shows
    /// nothing. On an active capsule <paramref name="animate"/> cross-fades the colour and tweens a growth; a lower fill is set
    /// at once, its colour too.
    /// </summary>
    public void Apply(float fraction, Color color, bool animate)
    {
        float target = Mathf.Clamp01(fraction);
        bool tween = animate && target >= AppliedFraction && gameObject.activeInHierarchy;
        bool grow = tween && target > AppliedFraction;
        StopTweens(complete: false);
        AppliedFraction = target;

        if (fill != null)
        {
            if (tween) fill.DOColor(color, ColorSeconds).SetEase(Ease.OutCubic);
            else fill.color = color;
        }
        if (clip == null || baseFill == null) return;

        var width = new Vector2(target * FitFillToCapsule(), clip.sizeDelta.y);
        if (grow) clip.DOSizeDelta(width, GrowSeconds).SetEase(Ease.OutCubic);
        else clip.sizeDelta = width;
    }

    /// <summary>
    /// Whether <paramref name="target"/>'s centre lies under the fill at <paramref name="fraction"/>. The painter asks it for a
    /// hollow lamp, whose hole must show the colour that is actually under it.
    /// </summary>
    public bool Covers(RectTransform target, float fraction)
    {
        if (target == null || baseFill == null) return false;
        float filled = Mathf.Clamp01(fraction) * baseFill.rect.width;
        if (filled <= 0f) return false;
        Vector3 centre = baseFill.InverseTransformPoint(target.TransformPoint(target.rect.center));
        float fromLeft = centre.x - baseFill.rect.xMin;
        return fromLeft >= 0f && fromLeft <= filled;
    }

    // The capsule changed size — the card's «Подключить» ↔ «Авто», whose layout lands after the paint that asked for it: follow
    // the inner Fill at the fraction already shown. A call that leaves the inner size as it was lets a running growth be.
    private void OnRectTransformDimensionsChange()
    {
        if (clip == null || baseFill == null || baseFill.rect.size == fittedInner) return;
        clip.DOKill();
        clip.sizeDelta = new Vector2(AppliedFraction * FitFillToCapsule(), clip.sizeDelta.y);
    }

    // A capsule that leaves the screen mid-tween lands on its target: it never comes back half-grown.
    private void OnDisable() => StopTweens(complete: true);

    private void OnDestroy() => StopTweens(complete: false);

    // The fill always spans the capsule's inner Fill; returns that inner width.
    private float FitFillToCapsule()
    {
        fittedInner = baseFill.rect.size;
        if (fill != null && fill.rectTransform.sizeDelta != fittedInner) fill.rectTransform.sizeDelta = fittedInner;
        return fittedInner.x;
    }

    private void StopTweens(bool complete)
    {
        if (clip != null) clip.DOKill(complete);
        if (fill != null) fill.DOKill(complete);
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// One reply-suggestion card (sketch-002 winner P). The WHOLE card is a single tap target
/// (D-01 — no dual-action arrow). Shows the FULL reply text (no truncation, no inner scroll)
/// in a bordered card whose intent title sits ON the top border (legend). The top
/// (recommended) card is tint-only — PositiveBg fill, PositiveInk border/legend and a ✦
/// sparkle — never a badge or a numeric % (D-07 revised per owner). Colors resolve from
/// <see cref="Theme"/> at bind time so both palettes work without a rebuild. Pure view: it
/// raises <see cref="OnTapped"/> with the reply text; the controller does the composer
/// hand-off + re-cluster. Binds only Plan-01 seam types — no networking.
/// </summary>
public class SuggestionCard : MonoBehaviour
{
    [SerializeField] private Button cardButton;            // whole card is the tap target
    [SerializeField] private TextMeshProUGUI replyText;    // full reply, drives the card height (builder)
    [SerializeField] private TextMeshProUGUI intentLabel;  // legend label on the top border
    [SerializeField] private Image cardBackground;         // inner fill (Surface / PositiveBg)
    [SerializeField] private Image borderImage;            // outer ring (Border / PositiveInk-tinted)
    [SerializeField] private Image legendPillBorder;       // legend pill ring — matches the card border
    [SerializeField] private Image legendPillFill;         // legend pill interior — matches the card fill
    [SerializeField] private GameObject sparkIcon;         // ✦ in the legend, recommended card only

    public event Action<string> OnTapped;

    // Recommended border = PositiveInk washed toward Surface (the sketch's 45% color-mix).
    private const float RecommendedBorderMix = 0.45f;

    private bool _isTop;   // remembered so a theme flip can re-resolve the bind-time colors (audit F17a)

    void OnEnable()
    {
        Theme.Changed += RepaintForTheme;
    }

    void OnDisable()
    {
        Theme.Changed -= RepaintForTheme;
        // The card is Destroy()'d on every re-cluster (panel Clear); kill the tap-punch tween
        // so it doesn't tick on a destroyed RectTransform (the DOTWEEN "target destroyed" errors).
        transform.DOKill();
    }

    // Cards bind colors at Setup; without this, a Profile-tab theme flip left already-rendered
    // cards in the old palette until the next request re-instantiated them.
    private void RepaintForTheme() => ApplyColors(_isTop);

    public void Setup(SuggestionItem item, bool isTop)
    {
        if (item == null) return;
        _isTop = isTop;
        replyText.text = item.text;
        intentLabel.text = item.intentLabel;   // rendered uppercase via the label's FontStyles
        ApplyColors(isTop);
        if (sparkIcon != null) sparkIcon.SetActive(isTop);
        cardButton.onClick.RemoveAllListeners();
        cardButton.onClick.AddListener(() =>
        {
            transform.DOPunchScale(Vector3.one * -0.03f, 0.15f, 0, 0).SetEase(Ease.OutQuad); // 0.97 punch
            OnTapped?.Invoke(item.text);
        });
    }

    private void ApplyColors(bool isTop)
    {
        Color surface = Theme.Color(ThemeRole.Surface);
        Color fill = isTop ? Theme.Color(ThemeRole.PositiveBg) : surface;
        Color border = isTop
            ? Color.Lerp(surface, Theme.Color(ThemeRole.PositiveInk), RecommendedBorderMix)
            : Theme.Color(ThemeRole.Border);
        if (cardBackground != null) cardBackground.color = fill;
        if (borderImage != null) borderImage.color = border;
        if (legendPillBorder != null) legendPillBorder.color = border;   // pill reads as part of the border system
        if (legendPillFill != null) legendPillFill.color = fill;
        if (intentLabel != null)
            intentLabel.color = isTop ? Theme.Color(ThemeRole.PositiveInk) : Theme.Color(ThemeRole.InkSecondary);
    }
}

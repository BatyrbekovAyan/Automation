using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// One reply-suggestion card (PANEL-02/03/06). The WHOLE card is a single tap target
/// (D-01 — no dual-action arrow). Shows the full reply text and a muted intent caption.
/// The top (recommended) card is tinted green instead of carrying a separate badge, so the
/// reply text gets the whole card (D-07 revised per owner). Pure view: it raises
/// <see cref="OnTapped"/> with the reply text; Plan 04's controller does the composer
/// hand-off + re-cluster. Binds only Plan-01 seam types — no networking.
/// </summary>
public class SuggestionCard : MonoBehaviour
{
    [SerializeField] private Button cardButton;            // whole card is the tap target
    [SerializeField] private TextMeshProUGUI replyText;    // full reply, fills the card (builder)
    [SerializeField] private TextMeshProUGUI intentLabel;  // muted one-word intent caption
    [SerializeField] private Image cardBackground;         // recolored on the top (recommended) card
    [SerializeField] private Color normalColor = Color.white;                                // #FFFFFF (builder-driven)
    [SerializeField] private Color recommendedColor = new Color(0.788f, 0.937f, 0.851f, 1f); // #C9EFD9 mint (builder-driven)

    public event Action<string> OnTapped;

    void OnDisable()
    {
        // The card is Destroy()'d on every re-cluster (panel Clear); kill the tap-punch tween
        // so it doesn't tick on a destroyed RectTransform (the DOTWEEN "target destroyed" errors).
        transform.DOKill();
    }

    public void Setup(SuggestionItem item, bool isTop)
    {
        if (item == null) return;
        replyText.text = item.text;
        intentLabel.text = item.intentLabel;
        if (cardBackground != null) cardBackground.color = isTop ? recommendedColor : normalColor;  // mint tint = recommended
        cardButton.onClick.RemoveAllListeners();
        cardButton.onClick.AddListener(() =>
        {
            transform.DOPunchScale(Vector3.one * -0.03f, 0.15f, 0, 0).SetEase(Ease.OutQuad); // 0.97 punch
            OnTapped?.Invoke(item.text);
        });
    }
}

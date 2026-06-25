using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// One reply-suggestion card (PANEL-02/03/06). The WHOLE card is a single tap target
/// (D-01 — no dual-action arrow). Shows reply text (≤2 lines, ellipsis — clamped by the
/// builder), a single-accent intent chip, and a «Рекомендуем» badge on the top card only.
/// Pure view: it raises <see cref="OnTapped"/> with the reply text; Plan 04's controller
/// does the composer hand-off + re-cluster. Binds only Plan-01 seam types — no networking.
/// </summary>
public class SuggestionCard : MonoBehaviour
{
    [SerializeField] private Button cardButton;            // whole card is the tap target
    [SerializeField] private TextMeshProUGUI replyText;    // 42u, #111B21, Ellipsis, 2-line cap (builder)
    [SerializeField] private TextMeshProUGUI intentLabel;  // 28u semibold, inside the chip
    [SerializeField] private GameObject recommendedBadge;  // top card only (PANEL-03/D-07)

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
        if (recommendedBadge != null) recommendedBadge.SetActive(isTop);   // index 0 ONLY
        cardButton.onClick.RemoveAllListeners();
        cardButton.onClick.AddListener(() =>
        {
            transform.DOPunchScale(Vector3.one * -0.03f, 0.15f, 0, 0).SetEase(Ease.OutQuad); // 0.97 punch
            OnTapped?.Invoke(item.text);
        });
    }
}

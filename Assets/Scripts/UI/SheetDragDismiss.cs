using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Drag-to-dismiss for bottom sheets, attached to a transparent DragZone over
/// the sheet's grabber/header strip (wired by SheetDragDismissWirer). The
/// panel follows the finger downward only; releasing past dismissFraction of
/// the panel height fires onDismiss (the sheet's own Close(), which tweens
/// from the current position), otherwise the panel snaps back.
/// </summary>
public class SheetDragDismiss : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Refs")]
    [SerializeField] private RectTransform panel;
    [SerializeField] private CanvasGroup backdropGroup;

    [Header("Tuning")]
    [SerializeField, Range(0.1f, 0.9f)] private float dismissFraction = 0.25f;
    [SerializeField] private float snapBackSeconds = 0.2f;

    public UnityEvent onDismiss = new UnityEvent();

    private bool dragging;
    private float shownY;
    private float startPointerY;
    private float canvasScale = 1f;
    private float backdropBaseAlpha;

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Grabbing mid open/close would fight the slide tween (and killing
        // BotSwitcherSheet's tween would strand its isAnimating flag), so
        // drags only start while the panel is at rest.
        dragging = panel != null && !DOTween.IsTweening(panel);
        if (!dragging) return;

        shownY = panel.anchoredPosition.y;
        startPointerY = eventData.position.y;
        backdropBaseAlpha = backdropGroup != null ? backdropGroup.alpha : 0f;

        Canvas canvas = GetComponentInParent<Canvas>();
        canvasScale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        if (canvasScale <= 0f) canvasScale = 1f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!dragging) return;

        float delta = (eventData.position.y - startPointerY) / canvasScale;
        float offset = Mathf.Min(0f, delta);
        panel.anchoredPosition = new Vector2(panel.anchoredPosition.x, shownY + offset);

        if (backdropGroup != null && panel.rect.height > 0f)
        {
            float progress = Mathf.Clamp01(-offset / panel.rect.height);
            backdropGroup.alpha = backdropBaseAlpha * (1f - progress);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        dragging = false;

        float dragged = shownY - panel.anchoredPosition.y;
        if (dragged > panel.rect.height * dismissFraction)
        {
            onDismiss?.Invoke();
            return;
        }

        panel.DOAnchorPosY(shownY, snapBackSeconds)
            .SetEase(Ease.OutCubic)
            .SetLink(panel.gameObject);
        if (backdropGroup != null)
        {
            backdropGroup.DOFade(backdropBaseAlpha, snapBackSeconds)
                .SetLink(backdropGroup.gameObject);
        }
    }
}

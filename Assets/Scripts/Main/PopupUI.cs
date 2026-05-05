using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared popup animation + input wiring. Handles the three recurring pieces
/// of the project's modal popups:
///
///   1. Show/Hide — fades the backdrop (overlay Image) while popping the card
///      with an OutBack/InBack scale tween. Matches the look of the
///      ProfilePage edit popup.
///   2. WireFingerUp — routes a button's action through DelayedFingerUpAction
///      so it commits on true finger release, filtering out the spurious
///      PointerUp/PointerDown cycle InputSystemUIInputModule dispatches on
///      iOS when a soft keyboard dismisses mid-gesture.
///   3. AbsorbEvents — attaches EventAbsorber to a card so taps on the card
///      background don't bubble up to an overlay dismiss handler.
///
/// Structure expected:
///   Panel (has Image = backdrop, optional overlay Button)
///     └─ Card (has its own Image/content; named "Content" or "Card", else
///                the first child)
/// </summary>
public static class PopupUI
{
    public const float DefaultBackdropAlpha = 0.5f;
    public const float OpenDuration  = 0.22f;
    public const float CloseDuration = 0.16f;
    public const float StartScale    = 0.88f;

    /// <summary>
    /// Show the popup. Fades the backdrop Image from 0 → <paramref name="backdropAlpha"/>
    /// and scales the card from <see cref="StartScale"/> → 1 with OutBack.
    /// <paramref name="onCardSettled"/> fires when the card's scale tween
    /// completes — the safe moment to call ActivateInputField(), since the
    /// native keyboard's slide-up would otherwise starve the tween of frames.
    /// </summary>
    public static void Show(GameObject panel, Action onCardSettled = null,
                            float backdropAlpha = DefaultBackdropAlpha)
    {
        if (panel == null) return;
        var panelT = panel.transform;
        var backdrop = panel.GetComponent<Image>();
        var card = FindCard(panelT);

        panelT.DOKill();
        if (backdrop != null) backdrop.DOKill();
        if (card != null) card.DOKill();

        // Older code scaled the whole overlay transform — undo that so the
        // backdrop stays full-screen while only the card pops.
        panelT.localScale = Vector3.one;
        panel.SetActive(true);

        if (backdrop != null)
        {
            var c = backdrop.color;
            backdrop.color = new Color(c.r, c.g, c.b, 0f);
            backdrop.DOFade(backdropAlpha, OpenDuration).SetEase(Ease.OutQuad);
        }

        if (card != null)
        {
            card.localScale = Vector3.one * StartScale;
            var tween = card.DOScale(Vector3.one, OpenDuration).SetEase(Ease.OutBack);
            if (onCardSettled != null) tween.OnComplete(() => onCardSettled());
        }
        else if (onCardSettled != null)
        {
            onCardSettled();
        }
    }

    /// <summary>
    /// Hide the popup. Fades the backdrop out and shrinks the card back to
    /// <see cref="StartScale"/>. Deactivates the panel in OnComplete so
    /// SetActive(false) runs well past any pointer-event dispatch — this is
    /// what prevents taps leaking to buttons beneath the popup.
    /// </summary>
    public static void Hide(GameObject panel)
    {
        if (panel == null) return;
        var panelT = panel.transform;
        var backdrop = panel.GetComponent<Image>();
        var card = FindCard(panelT);

        panelT.DOKill();
        if (backdrop != null) backdrop.DOKill();
        if (card != null) card.DOKill();

        if (backdrop != null)
            backdrop.DOFade(0f, CloseDuration).SetEase(Ease.InQuad);

        if (card != null)
        {
            card.DOScale(Vector3.one * StartScale, CloseDuration)
                .SetEase(Ease.InBack)
                .OnComplete(() => { if (panel != null) panel.SetActive(false); });
        }
        else
        {
            // No card to animate — deactivate after the backdrop fade if one
            // is running, else immediately.
            if (backdrop != null)
                backdrop.DOFade(0f, CloseDuration).OnComplete(() =>
                {
                    if (panel != null) panel.SetActive(false);
                });
            else
                panel.SetActive(false);
        }
    }

    /// <summary>
    /// Make <paramref name="button"/> invoke <paramref name="action"/> on true
    /// finger release via <see cref="DelayedFingerUpAction"/>. Replaces (not
    /// supplements) any existing Button.onClick wiring for this action, so
    /// don't also call Button.onClick.AddListener for the same handler.
    /// </summary>
    public static void WireFingerUp(Button button, Action action)
    {
        if (button == null) return;
        WireFingerUp(button.gameObject, action);
    }

    /// <summary>
    /// GameObject overload: wire a non-Button raycast target (e.g. an overlay
    /// panel that has only an Image, no Button) to invoke <paramref name="action"/>
    /// on true finger release. The GameObject must have a Graphic with
    /// raycastTarget = true for pointer events to reach it.
    /// </summary>
    public static void WireFingerUp(GameObject target, Action action)
    {
        if (target == null || action == null) return;
        var handler = target.GetComponent<DelayedFingerUpAction>();
        if (handler == null) handler = target.AddComponent<DelayedFingerUpAction>();
        handler.OnRealRelease += () => action();
    }

    /// <summary>
    /// Attach <see cref="EventAbsorber"/> so taps on the card background
    /// don't bubble up to an overlay dismiss handler. Safe to call multiple
    /// times; the component is added at most once.
    /// </summary>
    public static void AbsorbEvents(Component target)
    {
        if (target == null) return;
        var go = target.gameObject;
        if (go.GetComponent<EventAbsorber>() == null)
            go.AddComponent<EventAbsorber>();
    }

    /// <summary>
    /// Default card lookup: "Content" → "Card" → first child. Returns null
    /// if the panel has no children.
    /// </summary>
    private static Transform FindCard(Transform panelT)
    {
        var card = panelT.Find("Content");
        if (card == null) card = panelT.Find("Card");
        if (card == null && panelT.childCount > 0) card = panelT.GetChild(0);
        return card;
    }
}

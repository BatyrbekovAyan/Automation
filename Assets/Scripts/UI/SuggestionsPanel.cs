using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// The suggestions panel view — since sketch-003 (variant A) a KEYBOARD-SLOT tenant: it sits at
/// the very bottom of the screen, exactly where (and exactly as tall as) the native keyboard,
/// below the composer instead of above it. It renders a best-first vertical stack of cards and
/// a state machine (skeleton / cards / empty / error) inside that fixed slot. It does NOT move
/// itself: the controller drives the MovingArea inset (KeyboardAwarePanel.VirtualBottomInset)
/// and glues this panel's top edge to the composer's bottom via <see cref="FollowInset"/> every
/// LateUpdate. Pure view: it raises <see cref="OnCardTapped"/> / <see cref="OnRefreshRequested"/>
/// / <see cref="OnBackRequested"/>; the controller owns all slot/keyboard choreography.
/// Binds only Plan-01 seam types — no live-backend / messaging-API / web-request reference.
/// </summary>
public class SuggestionsPanel : MonoBehaviour
{
    [SerializeField] private Transform cardsContainer;     // VerticalLayoutGroup root (single column of 4)
    [SerializeField] private SuggestionCard cardPrefab;    // inactive in-scene template; instantiated per item
    [SerializeField] private GameObject[] skeletonCards;   // 4 shimmer placeholders (D-12)
    [SerializeField] private GameObject emptyState;        // «Нет предложений»
    [SerializeField] private GameObject errorState;        // «Не удалось загрузить» + «Обновить»
    [SerializeField] private Button refreshButton;         // manual refresh (INT-03)
    [SerializeField] private Button errorRetryButton;      // «Обновить» retry in the error state
    [SerializeField] private Button emptyRetryButton;      // «Обновить» in the empty state (audit F17b; wired by the builder)
    [SerializeField] private Button backButton;            // ‹ previous round, header left (rounds flow 2026-08-11; hidden at round 1)
    [SerializeField] private RectTransform rt;             // slot root (bottom-anchored, height = slot)
    [SerializeField] private CanvasGroup canvasGroup;      // kept at 1 — the slot swap is positional, not a fade
    [SerializeField] private RectTransform cardsViewport;  // fixed scroll region (chrome = -offsetMax.y)
    [SerializeField] private GameObject bottomFade;        // "more below" wash — hidden when nothing overflows

    public event Action<string> OnCardTapped;
    public event Action OnRefreshRequested;
    public event Action OnBackRequested;   // ‹ tap — the controller restores the previous round locally

    private readonly List<SuggestionCard> _cards = new();
    private float _slotCanvasPx;       // panel height — the keyboard slot's effective height
    private float _safeInsetCanvasPx;  // home-bar clearance applied inside the slot

    /// <summary>View-level visibility. The controller owns INTENT (its own sheet-open state) —
    /// the panel can be active-but-covered while the native keyboard slides over it.</summary>
    public bool IsShown => gameObject.activeSelf;

    /// <summary>The slot height the panel currently occupies (canvas px).</summary>
    public float SlotCanvasPx => _slotCanvasPx;

    void Awake()
    {
        if (refreshButton != null) refreshButton.onClick.AddListener(() => OnRefreshRequested?.Invoke());
        if (errorRetryButton != null) errorRetryButton.onClick.AddListener(() => OnRefreshRequested?.Invoke());
        if (emptyRetryButton != null) emptyRetryButton.onClick.AddListener(() => OnRefreshRequested?.Invoke());
        if (backButton != null) backButton.onClick.AddListener(() => OnBackRequested?.Invoke());
    }

    void OnDisable() => StopShimmer();

    // --- Slot chassis (sketch-003 variant A) --------------------------------

    /// <summary>
    /// Size the panel for the slot it is about to occupy: <paramref name="slotCanvasPx"/> is the
    /// keyboard-equivalent height (safe-adjusted, same space as KeyboardAwarePanel's inset) and
    /// <paramref name="safeInsetCanvasPx"/> the home-bar clearance kept content-free at the
    /// panel's bottom — the slot background still fills to the true screen bottom, like the
    /// keyboard's own tray does.
    /// </summary>
    public void SetSlotMetrics(float slotCanvasPx, float safeInsetCanvasPx)
    {
        _slotCanvasPx = slotCanvasPx;
        _safeInsetCanvasPx = safeInsetCanvasPx;
        if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, slotCanvasPx);
        ApplySafeInset(safeInsetCanvasPx);
        if (gameObject.activeInHierarchy) StartCoroutine(UpdateFadeNextFrame());
        else UpdateFadeVisibility();
    }

    // The safe pad lives on the CONTENT region, not the panel rect: viewport, the fade glued to
    // its bottom edge, and the empty/error overlays (built with the viewport's own offsets).
    private void ApplySafeInset(float safePx)
    {
        if (cardsViewport != null)
            cardsViewport.offsetMin = new Vector2(cardsViewport.offsetMin.x, safePx);
        if (bottomFade != null)
        {
            var fadeRt = (RectTransform)bottomFade.transform;
            fadeRt.anchoredPosition = new Vector2(fadeRt.anchoredPosition.x, safePx);
        }
        ApplyStateInset(emptyState, safePx);
        ApplyStateInset(errorState, safePx);
    }

    private static void ApplyStateInset(GameObject state, float safePx)
    {
        if (state == null) return;
        var stateRt = (RectTransform)state.transform;
        stateRt.offsetMin = new Vector2(stateRt.offsetMin.x, safePx);
    }

    /// <summary>Occupy the slot. Position comes from <see cref="FollowInset"/> — activate first,
    /// then the controller's LateUpdate glue takes over the same frame.</summary>
    public void ShowInSlot()
    {
        gameObject.SetActive(true);
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        FollowInset(0f);   // safe default until the first glue tick (fully below the composer's rest position)
    }

    /// <summary>Leave the slot (fully closed / the keyboard finished taking over).</summary>
    public void Deactivate()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    /// <summary>
    /// Glue the panel's top edge to the composer's bottom edge: the composer sits
    /// <paramref name="appliedInsetCanvasPx"/> above its rest position (KeyboardAwarePanel's
    /// APPLIED rise, smoothing and all), so the panel's top must sit exactly there.
    /// With bottom-anchored pivot: y = applied − slot.
    /// </summary>
    public void FollowInset(float appliedInsetCanvasPx)
    {
        if (rt == null) return;
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, appliedInsetCanvasPx - _slotCanvasPx);
    }

    // --- 5-state machine ----------------------------------------------------

    public void ShowSkeleton()   // D-12: shown on first load and each re-cluster, in place
    {
        Clear();
        SetActiveSafe(emptyState, false);
        SetActiveSafe(errorState, false);
        SetSkeletons(true);
        StartShimmer();
        UpdateFadeVisibility();
    }

    public void Render(SuggestionResult result)
    {
        StopShimmer();
        SetSkeletons(false);
        if (result == null) { RenderError(); return; }
        switch (result.status)
        {
            case SuggestionStatus.Ok:    RenderCards(result.items); break;
            case SuggestionStatus.Empty: RenderEmpty();             break;
            default:                     RenderError();             break;   // SuggestionStatus.Error
        }
    }

    private void RenderCards(List<SuggestionItem> items)
    {
        SetActiveSafe(emptyState, false);
        SetActiveSafe(errorState, false);
        Clear();
        if (items == null || cardPrefab == null) return;
        for (int i = 0; i < items.Count; i++)
        {
            SuggestionCard card = Instantiate(cardPrefab, cardsContainer);
            card.gameObject.SetActive(true);
            card.Setup(items[i], i == 0);                 // badge on top card only
            card.OnTapped += HandleCardTapped;
            _cards.Add(card);
        }
        if (gameObject.activeInHierarchy) StartCoroutine(UpdateFadeNextFrame());
    }

    private void RenderEmpty()
    {
        Clear();
        SetActiveSafe(errorState, false);
        SetActiveSafe(emptyState, true);
    }

    private void RenderError()
    {
        Clear();
        SetActiveSafe(emptyState, false);
        SetActiveSafe(errorState, true);
    }

    private void HandleCardTapped(string text) => OnCardTapped?.Invoke(text);

    /// <summary>Show the ‹ previous-round button only when there is a round to return to.</summary>
    public void SetBackVisible(bool visible)
    {
        if (backButton != null && backButton.gameObject.activeSelf != visible)
            backButton.gameObject.SetActive(visible);
    }

    public void Clear()
    {
        foreach (SuggestionCard card in _cards)
        {
            if (card == null) continue;
            card.OnTapped -= HandleCardTapped;
            Destroy(card.gameObject);
        }
        _cards.Clear();
    }

    // The fade is a lie once everything is visible — show it only while content overflows.
    private void UpdateFadeVisibility()
    {
        if (bottomFade == null || cardsViewport == null || cardsContainer == null) return;
        float contentH = UnityEngine.UI.LayoutUtility.GetPreferredHeight((RectTransform)cardsContainer);
        bool overflows = contentH > cardsViewport.rect.height + 1f;
        if (bottomFade.activeSelf != overflows) bottomFade.SetActive(overflows);
    }

    private IEnumerator UpdateFadeNextFrame()
    {
        yield return null;   // let the freshly-instantiated cards' TMP layout settle
        UpdateFadeVisibility();
    }

    // --- Skeleton shimmer (neutral, no spinner) -----------------------------

    private void StartShimmer()
    {
        if (skeletonCards == null) return;
        // Each skeleton self-animates its "thinking" dots (ThinkingDotsSkeleton) once active —
        // just ensure the card is at full opacity (no whole-card pulse on top of the dot bounce).
        foreach (GameObject sk in skeletonCards)
        {
            if (sk == null) continue;
            CanvasGroup cg = sk.GetComponent<CanvasGroup>();
            if (cg != null) { cg.DOKill(); cg.alpha = 1f; }
        }
    }

    private void StopShimmer()
    {
        if (skeletonCards == null) return;
        foreach (GameObject sk in skeletonCards)
        {
            if (sk == null) continue;
            CanvasGroup cg = sk.GetComponent<CanvasGroup>();
            if (cg != null) { cg.DOKill(); cg.alpha = 1f; }
        }
    }

    private void SetSkeletons(bool on)
    {
        if (skeletonCards == null) return;
        foreach (GameObject sk in skeletonCards) SetActiveSafe(sk, on);
    }

    private static void SetActiveSafe(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

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
    [SerializeField] private TextMeshProUGUI headerTitle;  // «ПРЕДЛОЖЕНИЯ» overline; drill rounds retitle it (wired by Tools/Suggestions/Wire Header Title)

    public event Action<string> OnCardTapped;
    public event Action OnRefreshRequested;
    public event Action OnBackRequested;   // ‹ tap — the controller restores the previous round locally

    private readonly List<SuggestionCard> _cards = new();
    private float _slotCanvasPx;       // panel height — the keyboard slot's effective height
    private float _safeInsetCanvasPx;  // home-bar clearance applied inside the slot
    private bool _fadeSuppressed;      // sticky "never show the fade" (expanded detent — nothing is cut off)

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

    void OnDisable()
    {
        StopShimmer();
        // Defensive, mirroring KeyboardAwarePanel.OnDisable: a drag that suppressed the fade at the
        // Expanded detent never gets its release when the chat screen closes underneath it. The
        // latch is sticky BY DESIGN against re-renders and the controller cannot read it back, so a
        // stranded `true` would silently kill the "more below" cue for every later round — on a
        // whole new chat, at Standard, with cards genuinely cut off. Clearing it here can only
        // over-show the fade (an honest cue at a capped Expanded), never strand it hidden. The
        // visibility itself is re-derived by the next UpdateFadeVisibility — every show runs
        // SetSlotMetrics, and every state change (skeleton / cards) recomputes too.
        _fadeSuppressed = false;
    }

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
        _safeInsetCanvasPx = safeInsetCanvasPx;
        ApplySlotHeight(slotCanvasPx);
        PositionStates();
        if (gameObject.activeInHierarchy) StartCoroutine(UpdateFadeNextFrame());
        else UpdateFadeVisibility();
    }

    /// <summary>
    /// The live-drag path: same geometry as <see cref="SetSlotMetrics"/> (the stored slot height,
    /// the rect and the content insets) but WITHOUT the fade recompute — that measures the whole
    /// card stack via <see cref="UnityEngine.UI.LayoutUtility"/> and would run a full layout pass
    /// on every drag frame. The fade cannot change while only the slot height moves anyway; the
    /// caller MUST settle it when the drag releases — <see cref="SetSlotMetrics"/> at the snapped
    /// detent (it re-measures next frame), or <see cref="SetFadeSuppressed"/> at Expanded.
    /// The safe inset is unchanged during a drag, so it is reused from the last SetSlotMetrics.
    /// The empty/error block is deliberately NOT re-placed here either. Its offset below the chrome
    /// is a function of the area's height, so recomputing it per frame would slide the block toward
    /// the header as the slot shrank — the panel would not descend as one piece. Holding the last
    /// settled offset makes the chrome and the block travel rigidly, which is what a collapse should
    /// look like; the caller settles it on release through <see cref="SetSlotMetrics"/>.
    /// LOCKSTEP: the caller must drive the APPLIED inset with this height, not behind it —
    /// <see cref="FollowInset"/> assumes applied ≤ the stored slot height, and a SMOOTHED inset
    /// lagging a shrinking slot (every downward drag) makes applied &gt; slot for the smoothing
    /// tail, which lifts the panel's bottom edge off the screen bottom. Hold
    /// KeyboardAwarePanel.TrackInsetImmediately for the whole gesture.
    /// </summary>
    public void SetSlotHeightLive(float slotCanvasPx) => ApplySlotHeight(slotCanvasPx);

    /// <summary>
    /// Height of the panel's own chrome — the drag handle strip plus the header — authored once at
    /// build time as the card viewport's top inset. The Expanded detent is chrome + content, so the
    /// controller must read the chrome from the SAME rect the content is measured inside
    /// (<see cref="MeasuredContentHeight"/>), or the detent and the fade disagree about what "all
    /// the cards fit" means.
    /// </summary>
    public float ChromeHeightCanvasPx => cardsViewport != null ? -cardsViewport.offsetMax.y : 0f;

    // Shared core of both height paths. _slotCanvasPx MUST move with the rect: FollowInset derives
    // both the panel's y and its open ratio from it, so a rect resized behind its back would let
    // the panel float off the screen bottom mid-drag.
    private void ApplySlotHeight(float slotCanvasPx)
    {
        _slotCanvasPx = slotCanvasPx;
        // The panel is TALLER than the slot by the safe inset. The native keyboard covers the
        // composer's baked bottom pad (KeyboardAwarePanel subtracts the safe area from the
        // rise, so the pad "slides under the keyboard"); a tenant of the same slot must cover
        // that same strip, or it shows as a dead gap between the composer pill and the panel
        // chrome (device finding 2026-08-12). The pad is 108u, the safe inset ≈94u — the pill
        // itself starts above the pad, so the overlap can never touch it.
        if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, slotCanvasPx + _safeInsetCanvasPx);
        ApplySafeInset(_safeInsetCanvasPx);
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
    }

    // --- Empty / error block chassis (device fix 2026-08-19) ----------------
    // The builder stretches these overlays across the card area, which made the block SHRINK with
    // the slot rather than slide out of it: the VerticalLayoutGroup has childControlHeight, so as
    // its rect fell below the content it lerped the children from preferred toward MIN — and the two
    // labels are plain TMP with a min of 0, so their rects collapsed while the glyphs kept drawing
    // and «Нет предложений» printed on top of «Напишите ответ вручную». The «Обновить» pill held its
    // size because it is the only child with a LayoutElement.minHeight.
    //
    // The runtime owns the chassis instead. The block keeps its NATURAL height — a ContentSizeFitter
    // reads it off the same VerticalLayoutGroup that lays it out, so nothing has to be measured by
    // hand or kept in sync with the text — and only its POSITION follows the slot
    // (SuggestionStateLayout.TopOffset). Conversion is idempotent, so a builder re-run cannot undo
    // it for longer than the next slot change.

    private void EnsureStateChassis(GameObject state)
    {
        if (state == null) return;
        var stateRt = (RectTransform)state.transform;

        // Already converted — bail before touching the transform. PositionStates runs on every drag
        // frame, and re-writing anchors there would dirty the layout 60 times a second for nothing.
        if (stateRt.anchorMin.y == 1f && stateRt.anchorMax.y == 1f && stateRt.pivot.y == 1f
            && state.GetComponent<ContentSizeFitter>() != null) return;

        // Top-anchored, top-pivoted: anchoredPosition.y then places the block's TOP edge below the
        // panel's top, which is the one edge that must never be crossed. Horizontal anchors are left
        // alone, so the block keeps stretching to the panel's width.
        stateRt.anchorMin = new Vector2(stateRt.anchorMin.x, 1f);
        stateRt.anchorMax = new Vector2(stateRt.anchorMax.x, 1f);
        stateRt.pivot = new Vector2(stateRt.pivot.x, 1f);

        var fitter = state.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = state.AddComponent<ContentSizeFitter>();
        // Vertical only: the width is anchored, and a horizontal fit would fight it.
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private void PositionStates()
    {
        // The card area is the panel minus its chrome; the safe pad sits BELOW the area, so it is
        // already out of this height (panel = slot + safe, area = panel - chrome - safe).
        float areaHeight = Mathf.Max(0f, _slotCanvasPx - ChromeHeightCanvasPx);
        PositionState(emptyState, areaHeight);
        PositionState(errorState, areaHeight);
    }

    private void PositionState(GameObject state, float areaHeight)
    {
        if (state == null) return;
        EnsureStateChassis(state);
        var stateRt = (RectTransform)state.transform;
        float top = SuggestionStateLayout.TopOffset(areaHeight, stateRt.rect.height);
        stateRt.anchoredPosition = new Vector2(
            stateRt.anchoredPosition.x, -(ChromeHeightCanvasPx + top));
    }

    // The activation path only. A state switched on this frame has not had its ContentSizeFitter
    // run, so its rect still holds the builder's stretched height — placing it against that reads as
    // a one-frame jump exactly when the block appears. Settling the layout first costs a rebuild on
    // a state change (never on a drag frame, where PositionStates alone is called and the height is
    // already correct and unchanging).
    private void RebuildAndPositionState(GameObject state)
    {
        if (state != null && state.activeInHierarchy)
        {
            EnsureStateChassis(state);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)state.transform);
        }
        PositionStates();
        // ...and a belt for the first show of all, where the PANEL's own width may not be settled
        // yet, so even a forced rebuild measures the text against nothing. Same trick, same reason
        // as UpdateFadeNextFrame.
        if (gameObject.activeInHierarchy) StartCoroutine(PositionStatesNextFrame());
    }

    private IEnumerator PositionStatesNextFrame()
    {
        yield return null;
        PositionStates();
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
    /// Glue the panel to the composer as it rides KeyboardAwarePanel's APPLIED rise (smoothing
    /// and all). Fully open, the panel's top edge sits at applied + safe — overlapping the
    /// composer's baked bottom pad by exactly the safe inset, the same strip the native
    /// keyboard covers. The overlap blends out on the way down (the panel travels ~12% faster
    /// than the composer — imperceptible), so fully closed it is fully off-screen instead of
    /// leaving a safe-height sliver poking over the pad.
    /// </summary>
    public void FollowInset(float appliedInsetCanvasPx)
    {
        if (rt == null) return;
        float openRatio = _slotCanvasPx > 0f ? Mathf.Clamp01(appliedInsetCanvasPx / _slotCanvasPx) : 0f;
        float y = appliedInsetCanvasPx - _slotCanvasPx - _safeInsetCanvasPx * (1f - openRatio);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
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
        RebuildAndPositionState(emptyState);
    }

    private void RenderError()
    {
        Clear();
        SetActiveSafe(emptyState, false);
        SetActiveSafe(errorState, true);
        RebuildAndPositionState(errorState);
    }

    private void HandleCardTapped(string text) => OnCardTapped?.Invoke(text);

    /// <summary>Show the ‹ previous-round button only when there is a round to return to.</summary>
    public void SetBackVisible(bool visible)
    {
        if (backButton != null && backButton.gameObject.activeSelf != visible)
            backButton.gameObject.SetActive(visible);
    }

    /// <summary>The header overline's rest text — round 1 and every fresh round.</summary>
    public const string DefaultHeaderTitle = "ПРЕДЛОЖЕНИЯ";

    // Validate clamps titles to 24 server-side; the slice only guards a rogue payload.
    private const int HeaderTitleMaxChars = 26;

    /// <summary>Round header (drill flow 2026-08-18): null/empty restores the default
    /// overline; a drill round shows the picked card's title. Uppercased HERE because the
    /// scene TMP carries no uppercase FontStyle — the composed string IS the display string.</summary>
    public void SetHeaderTitle(string title)
    {
        if (headerTitle != null) headerTitle.text = ComposeHeaderTitle(title);
    }

    /// <summary>Pure composition seam for <see cref="SetHeaderTitle"/> — EditMode-tested.</summary>
    public static string ComposeHeaderTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return DefaultHeaderTitle;
        string value = title.Trim().ToUpperInvariant();
        return value.Length <= HeaderTitleMaxChars
            ? value
            : value.Substring(0, HeaderTitleMaxChars - 1) + "…";
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

    /// <summary>
    /// The measured height of the card stack — the SAME number the fade's overflow rule uses, so a
    /// caller sizing a detent around the content can never disagree with the fade about whether
    /// anything is cut off. Only valid AFTER a layout pass has run over freshly built cards (this
    /// class works around that with <see cref="UpdateFadeNextFrame"/>); read it a frame late too.
    /// </summary>
    public float MeasuredContentHeight
    {
        get
        {
            // `as` + Unity's null operator, not a hard cast: this is public and read from a drag
            // hot path, so a mis-wired (plain Transform) or destroyed container must read as 0
            // rather than throw InvalidCastException / MissingReferenceException mid-gesture.
            RectTransform content = cardsContainer as RectTransform;
            return content == null ? 0f : UnityEngine.UI.LayoutUtility.GetPreferredHeight(content);
        }
    }

    /// <summary>
    /// Force the "more below" fade off regardless of overflow, and remember it — the Expanded
    /// detent shows the whole stack, so a fade there would be a lie, and a later
    /// <see cref="UpdateFadeVisibility"/> (a re-render, a re-measure) must not pop it back.
    /// Suppression only vetoes the VISIBILITY decision: <see cref="ApplySafeInset"/> keeps moving
    /// the (hidden) fade with the safe pad, so it reappears at the right Y back at Standard.
    /// </summary>
    public void SetFadeSuppressed(bool suppressed)
    {
        if (_fadeSuppressed == suppressed) return;   // repeatable per drag frame — never re-measures
        _fadeSuppressed = suppressed;
        UpdateFadeVisibility();
    }

    // The fade is a lie once everything is visible — show it only while content overflows (and
    // never while suppressed). SOLE owner of the fade's active state.
    private void UpdateFadeVisibility()
    {
        if (bottomFade == null) return;
        if (_fadeSuppressed) { SetActiveSafe(bottomFade, false); return; }
        if (cardsViewport == null || cardsContainer == null) return;   // cannot measure — leave as authored
        // A slot dragged to (or near) Collapsed leaves the viewport zero/negative — the chrome alone
        // is taller than the slot — and every content height then "overflows". Nothing is on screen
        // to be cut off, and the fade is the one child anchored ABOVE the panel's own bottom edge
        // (by the safe pad), so at slot 0 it would still paint a Surface wash over the bottom ~100u
        // of the screen while the panel itself sits fully below it.
        float viewportH = cardsViewport.rect.height;
        bool overflows = viewportH > 0f && MeasuredContentHeight > viewportH + 1f;
        SetActiveSafe(bottomFade, overflows);
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

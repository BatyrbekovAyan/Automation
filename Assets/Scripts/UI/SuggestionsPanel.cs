using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// The suggestions panel view (PANEL-01..05). A white sheet above the composer that renders a
/// best-first vertical stack of 4 cards and a 5-state machine (skeleton / cards / empty / error)
/// at a FIXED footprint — no layout pop (D-12). Slides in/out via DOTween. Pure view: it raises
/// <see cref="OnCardTapped"/> / <see cref="OnRefreshRequested"/>; Plan 04's controller drives it.
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
    [SerializeField] private RectTransform rt;             // slide root
    [SerializeField] private CanvasGroup canvasGroup;      // fade
    [SerializeField] private RectTransform cardsViewport;  // fixed scroll region (chrome = -offsetMax.y)
    [SerializeField] private GameObject bottomFade;        // "more below" wash — hidden when nothing overflows

    public event Action<string> OnCardTapped;
    public event Action OnRefreshRequested;

    private const float TopSafeClearance = 180f;   // expansion never grows closer than this to the parent top

    private readonly List<SuggestionCard> _cards = new();
    private float _restY;            // panel bottom sits on the composer's top edge (set by the controller)
    private float _baseHeight;       // authored sheet height — the collapsed detent (captured in Awake)
    private bool _visible, _sliding;
    private Tweener _slideTween;     // Tweener (not Tween) so ChangeEndValue is available for live retargeting
    private Tweener _heightTween;    // expand/collapse settle

    /// <summary>Full sheet height — the clearance the message list must leave above the composer.</summary>
    public float Footprint => rt != null ? rt.rect.height : 0f;
    private float HiddenY => _restY - Footprint;

    /// <summary>True while the sheet is (or is animating) open. Hide() flips it immediately.</summary>
    public bool IsShown => _visible;

    /// <summary>0 at rest → 1 fully dragged down. Read by the grab handle's close decision.</summary>
    public float DragProgress => rt == null || Footprint <= 0f
        ? 0f
        : Mathf.Clamp01((_restY - rt.anchoredPosition.y) / Footprint);

    void Awake()
    {
        if (rt != null) _baseHeight = rt.sizeDelta.y;
        if (refreshButton != null) refreshButton.onClick.AddListener(() => OnRefreshRequested?.Invoke());
        if (errorRetryButton != null) errorRetryButton.onClick.AddListener(() => OnRefreshRequested?.Invoke());
        if (emptyRetryButton != null) emptyRetryButton.onClick.AddListener(() => OnRefreshRequested?.Invoke());
    }

    void OnDisable()
    {
        _slideTween?.Kill();
        _slideTween = null;
        _heightTween?.Kill();
        _heightTween = null;
        _sliding = false;
        if (canvasGroup != null) canvasGroup.DOKill();
        StopShimmer();
    }

    /// <summary>
    /// Controller-fed: the panel's bottom edge must sit at the composer's TOP edge, i.e. at
    /// `composerHeight` units above the MovingArea bottom. Repositions live when shown, retargets
    /// the slide if mid-animation, or stores it for the next Show when hidden.
    /// </summary>
    public void SetComposerHeight(float composerHeight)
    {
        _restY = composerHeight;
        if (rt == null) return;
        if (_visible && !_sliding)
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, _restY);
        else if (_sliding && _slideTween != null && _slideTween.IsActive())
            _slideTween.ChangeEndValue(new Vector2(rt.anchoredPosition.x, _visible ? _restY : HiddenY), true);
    }

    // --- 5-state machine ----------------------------------------------------

    public void ShowSkeleton()   // D-12: shown on first load and each re-cluster, in place
    {
        Clear();
        SetActiveSafe(emptyState, false);
        SetActiveSafe(errorState, false);
        SetSkeletons(true);
        StartShimmer();
        // A re-cluster resets an expanded sheet — the skeletons fit the base detent.
        if (_visible && CurrentHeight > BaseHeight + 1f) SettleSheetHeight(BaseHeight);
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

    // --- Show / hide (DOTween slide + fade) ---------------------------------

    public void Show()
    {
        gameObject.SetActive(true);
        _visible = true;
        _slideTween?.Kill();
        _heightTween?.Kill();
        if (rt != null && BaseHeight > 0f)
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, BaseHeight);   // fresh open = collapsed detent
        UpdateFadeVisibility();
        if (canvasGroup != null) canvasGroup.alpha = 1f;     // no fade — pure slide up from behind the composer
        if (rt != null)
        {
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, HiddenY);   // start behind composer, slide up to rest-Y
            _sliding = true;
            _slideTween = rt.DOAnchorPosY(_restY, 0.25f).SetEase(Ease.OutCubic).OnComplete(() => _sliding = false);
        }
    }

    public void Hide()
    {
        if (!gameObject.activeSelf) return;
        _visible = false;
        _slideTween?.Kill();
        if (rt != null)
        {
            _sliding = true;
            _slideTween = rt.DOAnchorPosY(HiddenY, 0.20f).SetEase(Ease.InCubic)   // slide back down behind the composer
                            .OnComplete(() => { _sliding = false; gameObject.SetActive(false); });
        }
        else gameObject.SetActive(false);
    }

    // --- Grab-handle drag + expansion (SheetDragHandle drives; the panel owns its tween state) ---

    /// <summary>The collapsed detent — the sheet's authored height.</summary>
    public float BaseHeight => _baseHeight > 0f ? _baseHeight : (rt != null ? rt.sizeDelta.y : 0f);

    /// <summary>Current sheet height (the drag works in height space above the base detent).</summary>
    public float CurrentHeight => rt != null ? rt.sizeDelta.y : 0f;

    /// <summary>
    /// The expanded detent: just tall enough that ALL cards are visible (chrome + content),
    /// never below the base height and never closer than <see cref="TopSafeClearance"/> to the
    /// parent's top edge. Equals the base height when the cards already fit — no expansion then.
    /// </summary>
    public float ExpandedFitHeight()
    {
        float baseH = BaseHeight;
        if (rt == null || cardsViewport == null || cardsContainer == null) return baseH;
        float chrome = -cardsViewport.offsetMax.y;
        float contentH = UnityEngine.UI.LayoutUtility.GetPreferredHeight((RectTransform)cardsContainer);
        float fit = chrome + contentH;
        var parentRt = rt.parent as RectTransform;
        float cap = parentRt != null ? parentRt.rect.height - _restY - TopSafeClearance : fit;
        return Mathf.Max(baseH, Mathf.Min(fit, cap));
    }

    /// <summary>Finger down on the grab zone — stop any running slide so the drag owns the position.</summary>
    public void BeginHandleDrag()
    {
        _slideTween?.Kill();
        _slideTween = null;
        _heightTween?.Kill();
        _heightTween = null;
        _sliding = false;
    }

    /// <summary>Live height while dragging upward (no tween). The bottom edge stays on the composer.</summary>
    public void SetSheetHeight(float height)
    {
        if (rt == null) return;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        UpdateFadeVisibility();
    }

    /// <summary>Released in the expansion zone — settle to a detent (base or expanded-fit).</summary>
    public void SettleSheetHeight(float height)
    {
        if (rt == null) return;
        _heightTween?.Kill();
        _heightTween = rt.DOSizeDelta(new Vector2(rt.sizeDelta.x, height), 0.25f)
                         .SetEase(Ease.OutCubic)
                         .OnUpdate(UpdateFadeVisibility)
                         .OnComplete(UpdateFadeVisibility);
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

    /// <summary>Follow the finger: <paramref name="draggedDown"/> ≥ 0 units below the rest position.</summary>
    public void DragBy(float draggedDown)
    {
        if (rt == null) return;
        float y = Mathf.Clamp(_restY - Mathf.Max(0f, draggedDown), HiddenY, _restY);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
    }

    /// <summary>Released without committing a close — spring back to the rest position.</summary>
    public void SnapBack()
    {
        if (rt == null) return;
        _slideTween?.Kill();
        _sliding = true;
        _slideTween = rt.DOAnchorPosY(_restY, 0.2f).SetEase(Ease.OutCubic).OnComplete(() => _sliding = false);
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

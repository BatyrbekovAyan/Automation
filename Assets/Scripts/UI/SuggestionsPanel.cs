using System;
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
    [SerializeField] private RectTransform rt;             // slide root
    [SerializeField] private CanvasGroup canvasGroup;      // fade

    public event Action<string> OnCardTapped;
    public event Action OnRefreshRequested;

    private readonly List<SuggestionCard> _cards = new();
    private float _restY;            // panel bottom sits on the composer's top edge (set by the controller)
    private bool _visible, _sliding;
    private Tweener _slideTween;     // Tweener (not Tween) so ChangeEndValue is available for live retargeting

    /// <summary>Full sheet height — the clearance the message list must leave above the composer.</summary>
    public float Footprint => rt != null ? rt.rect.height : 0f;
    private float HiddenY => _restY - Footprint;

    void Awake()
    {
        if (refreshButton != null) refreshButton.onClick.AddListener(() => OnRefreshRequested?.Invoke());
        if (errorRetryButton != null) errorRetryButton.onClick.AddListener(() => OnRefreshRequested?.Invoke());
    }

    void OnDisable()
    {
        _slideTween?.Kill();
        _slideTween = null;
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

    // --- Skeleton shimmer (neutral, no spinner) -----------------------------

    private void StartShimmer()
    {
        if (skeletonCards == null) return;
        foreach (GameObject sk in skeletonCards)
        {
            if (sk == null) continue;
            CanvasGroup cg = sk.GetComponent<CanvasGroup>() ?? sk.AddComponent<CanvasGroup>();
            cg.alpha = 0.45f;
            cg.DOFade(1f, 1.0f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
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

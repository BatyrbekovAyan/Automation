using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen_Paywall — the full-screen slide-in overlay that sells the three tiers
/// (spec §6 «Пейволл»). Built by <c>Tools/Billing/Build Paywall</c>
/// (<c>PaywallBuilder</c>); every visible string comes from <see cref="PaywallRows"/>,
/// which is the pure, test-pinned seam — nothing user-facing is typed into the scene.
///
/// Lifecycle mirrors <see cref="AddBotPanel"/>: the screen serializes INACTIVE inside
/// ScreenContainer, <see cref="Open"/> activates it and slides it in from the right,
/// <see cref="Close"/> slides it out and deactivates. Sibling order (after Screen_New,
/// before the auth pages) is owned by <c>NavRestructureBuilder.ReorderScreens</c> — do
/// not reorder at runtime.
///
/// The gate hookup is deliberately STATIC (<see cref="Bootstrap"/>): every
/// <see cref="EntitlementGate.RequestPaywall"/> fires while this screen is inactive,
/// so an instance-level OnEnable subscription could never hear the very event that is
/// supposed to open it.
/// </summary>
[DisallowMultipleComponent]
public class PaywallController : MonoBehaviour
{
    private const float SlideInDuration = 0.3f;
    private const float SlideOutDuration = 0.25f;

    /// <summary>Wire-up block for one tier card; stamped by PaywallBuilder.</summary>
    [Serializable]
    public class TierCardRefs
    {
        public GameObject root;
        public Button button;
        public GameObject ring;           // accent selection border (6u, drawn behind the fill)
        public GameObject popularBadge;   // «Популярный» ribbon — Бизнес only
        public GameObject crossBotRow;    // «Сводка по всем ботам» — Бизнес/Сеть only
        public TextMeshProUGUI title;
        public TextMeshProUGUI price;
        public TextMeshProUGUI counts;
    }

    /// <summary>One value-receipt stat tile (день-5 «чек ценности»).</summary>
    [Serializable]
    public class StatTileRefs
    {
        public TextMeshProUGUI value;
        public TextMeshProUGUI label;
    }

    [Header("Chrome")]
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private TextMeshProUGUI headerTitle;
    [SerializeField] private TextMeshProUGUI headerSubline;
    [SerializeField] private Button closeButton;
    [SerializeField] private SwipeToBackPanel swipeBack;

    [Header("Период")]
    [SerializeField] private Button monthButton;
    [SerializeField] private Button yearButton;
    [SerializeField] private GameObject monthFill;
    [SerializeField] private GameObject yearFill;
    [SerializeField] private TextMeshProUGUI monthLabel;
    [SerializeField] private TextMeshProUGUI yearLabel;

    [Header("Тарифы")]
    [SerializeField] private TierCardRefs[] tierCards = new TierCardRefs[3];

    [Header("CTA")]
    [SerializeField] private Button ctaButton;
    [SerializeField] private TextMeshProUGUI ctaLabel;
    [SerializeField] private TextMeshProUGUI finePrint;
    [SerializeField] private Button restoreButton;
    [SerializeField] private TextMeshProUGUI restoreLabel;

    [Header("Чек ценности (PaywallTrigger.TrialExpired)")]
    [SerializeField] private GameObject receiptBlock;
    [SerializeField] private StatTileRefs[] receiptTiles = new StatTileRefs[4];

    private static PaywallController _instance;

    /// <summary>Resolves even while Screen_Paywall is inactive (Awake hasn't run yet).</summary>
    public static PaywallController Instance =>
        _instance != null ? _instance
            : _instance = UnityEngine.Object.FindFirstObjectByType<PaywallController>(FindObjectsInactive.Include);

    private RectTransform _rt;
    private Canvas _rootCanvas;
    private Tween _activeSlide;
    private bool _wired;
    // True while the exit tween is in flight. The GameObject is still active during it
    // (SetActive(false) happens in OnComplete), so without this flag a paywall request
    // arriving mid-close would read as "already open", kill the exit tween, and leave the
    // screen parked wherever the tween had got to.
    private bool _closing;

    private PaywallPeriod _period = PaywallPeriod.Month;
    private PlanTier _selected = PaywallRows.Recommended;
    private PlanTier _purchased = PlanTier.None;
    private bool _trialStarted;
    private bool _receiptVariant;
    private string _notice = "";

    public bool IsOpen => gameObject.activeSelf;
    public PaywallPeriod Period => _period;
    public PlanTier Selected => _selected;

    // ── Gate hookup ──────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        _instance = null;   // survives a domain-reload-free play-mode enter
        EntitlementGate.OnPaywallRequested -= HandlePaywallRequested;
        EntitlementGate.OnPaywallRequested += HandlePaywallRequested;
    }

    /// <summary>
    /// THE single subscriber to <see cref="EntitlementGate.OnPaywallRequested"/>, and
    /// therefore the one place the sheet-vs-paywall decision is made (Task 14d). A second
    /// subscriber owning the interception would make BOTH open on a limit trigger, which is
    /// exactly the outcome the sheet exists to avoid.
    ///
    /// Limit triggers (<see cref="BillingGateRows.ShouldInterceptWithSheet"/>) get the
    /// lightweight bottom sheet first; its «Посмотреть тарифы» re-enters
    /// <see cref="Open"/> with the SAME trigger, so nothing downstream (the receipt variant,
    /// the CTA form) can tell the difference between a direct request and a sheet-forwarded
    /// one. Browse/TrialExpired open the paywall directly, unchanged.
    /// </summary>
    private static void HandlePaywallRequested(PaywallTrigger trigger)
    {
        var instance = Instance;
        if (instance == null)
        {
            Debug.LogWarning($"[PaywallController] Paywall requested ({trigger}) but Screen_Paywall is not in the scene — run Tools/Billing/Build Paywall.");
            return;
        }

        // Resolved BEFORE the sheet so a missing paywall screen never leaves the owner
        // holding a sheet whose only action leads nowhere.
        if (BillingGateRows.ShouldInterceptWithSheet(trigger))
        {
            BillingGateSheet.Show(trigger, instance.SemiboldFont, instance.RegularFont,
                                  () => Instance?.Open(trigger));
            return;
        }

        instance.Open(trigger);
    }

    /// <summary>
    /// Font assets for the runtime-built <see cref="BillingGateSheet"/>. PaywallBuilder
    /// stamped the project's real weights onto these labels, and there is no runtime path to
    /// AssetDatabase — TMP's own default asset ships an empty weight table, so a synthesized
    /// bold would not match anything else on screen.
    /// </summary>
    internal TMP_FontAsset SemiboldFont => ctaLabel != null ? ctaLabel.font : null;

    internal TMP_FontAsset RegularFont => headerSubline != null ? headerSubline.font : null;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        _instance = this;
        EnsureInit();
    }

    private void OnEnable()
    {
        Theme.Changed += PaintPeriodLabels;
        UsageStore.OnUsageChanged += HandleUsageChanged;
        Render();
    }

    private void OnDisable()
    {
        Theme.Changed -= PaintPeriodLabels;
        UsageStore.OnUsageChanged -= HandleUsageChanged;
    }

    /// <summary>
    /// The receipt's «Диалогов» tile reads <see cref="UsageStore.Current"/>, which is null until
    /// the first GetUsage response lands — and at LAUNCH that fetch and the TrialExpired paywall
    /// are started in the same tick (Manager.PreloadSecretsThenInitBilling), so the tile would
    /// otherwise sit on «—» exactly when it is supposed to persuade. Repainting on the store's own
    /// event is the whole fix; subscription is tied to enable/disable, so a closed paywall holds
    /// nothing. Non-receipt variants ignore it: nothing else on this screen reads usage.
    /// </summary>
    private void HandleUsageChanged()
    {
        if (!_receiptVariant || !IsOpen) return;
        RenderReceipt();
    }

    private void EnsureInit()
    {
        if (_rt == null) _rt = GetComponent<RectTransform>();
        if (_rootCanvas == null) _rootCanvas = GetComponentInParent<Canvas>(true);
        if (_wired) return;
        _wired = true;

        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (ctaButton != null) ctaButton.onClick.AddListener(OnCtaClicked);
        if (restoreButton != null) restoreButton.onClick.AddListener(OnRestoreClicked);
        if (monthButton != null) monthButton.onClick.AddListener(() => SetPeriod(PaywallPeriod.Month));
        if (yearButton != null) yearButton.onClick.AddListener(() => SetPeriod(PaywallPeriod.Year));
        if (swipeBack != null) swipeBack.OnCommitted = HandleSwipeDismissed;

        for (int i = 0; i < tierCards.Length; i++)
        {
            var card = tierCards[i];
            if (card?.button == null) continue;
            PlanTier tier = i < PaywallRows.Order.Length ? PaywallRows.Order[i] : PlanTier.None;
            card.button.onClick.AddListener(() => SelectTier(tier));
        }
    }

    // ── Open / close ─────────────────────────────────────────────────────────

    public void Open(PaywallTrigger trigger = PaywallTrigger.Browse)
    {
        EnsureInit();

        bool wasActive = gameObject.activeSelf;
        // A slide of EITHER direction still running means the screen is not where it looks like
        // it is. The exit case is covered by _closing; the ENTRY case is not — a request landing
        // inside the 0.3s entry slide would read as "settled open", skip the (re)slide, and leave
        // the screen parked at whatever x the killed tween had reached.
        // IsActive() alone: a tween that exists but is paused/queued still means the screen is
        // parked mid-travel, and IsPlaying() would call that "settled".
        bool slideInFlight = _activeSlide != null && _activeSlide.IsActive();
        bool wasSettledOpen = wasActive && !_closing && !slideInFlight;

        // Kill() defaults to complete:false, so a mid-flight exit tween's OnComplete — which
        // would SetActive(false) — can never land on the screen we are re-opening.
        _activeSlide?.Kill();
        _closing = false;

        _receiptVariant = trigger == PaywallTrigger.TrialExpired;
        _notice = "";
        _purchased = BillingService.PurchasedTier;
        _trialStarted = TrialLedger.HasStarted;
        _selected = _purchased != PlanTier.None ? _purchased : PaywallRows.Recommended;
        _period = PaywallPeriod.Month;

        gameObject.SetActive(true);   // a fresh activation renders through OnEnable…
        // …but OnEnable never fires when the object was ALREADY active — including the 0.25s
        // window in which the exit tween has not yet deactivated us. Every state field above
        // was just reassigned, so without this a TrialExpired request landing on a
        // just-dismissed Browse paywall would slide back in wearing the Browse header and no
        // receipt.
        if (wasActive) Render();

        if (scroll != null) scroll.verticalNormalizedPosition = 1f;

        // A re-request while genuinely settled on screen refreshes in place; a re-request
        // mid-close falls through and slides back in from wherever the exit tween left us.
        if (wasSettledOpen) return;
        _rt.anchoredPosition = new Vector2(CanvasWidth(), _rt.anchoredPosition.y);
        _activeSlide = _rt.DOAnchorPosX(0f, SlideInDuration).SetEase(Ease.OutCubic);
    }

    public void Close()
    {
        EnsureInit();
        if (!IsOpen || _closing) return;
        _closing = true;
        _activeSlide?.Kill();
        _activeSlide = _rt.DOAnchorPosX(CanvasWidth(), SlideOutDuration)
            .SetEase(Ease.InCubic)
            .OnComplete(() =>
            {
                _closing = false;
                gameObject.SetActive(false);
            });
    }

    /// <summary>The left-edge strip already slid us off screen — just stop being active.</summary>
    private void HandleSwipeDismissed()
    {
        _activeSlide?.Kill();
        _closing = false;
        gameObject.SetActive(false);
    }

    private float CanvasWidth() =>
        _rootCanvas != null ? _rootCanvas.GetComponent<RectTransform>().rect.width : 1080f;

    // ── Interaction ──────────────────────────────────────────────────────────

    private void SetPeriod(PaywallPeriod period)
    {
        if (_period == period) return;
        _period = period;
        _notice = "";
        Render();
    }

    private void SelectTier(PlanTier tier)
    {
        if (tier == PlanTier.None || _selected == tier) return;
        _selected = tier;
        _notice = "";
        Render();
    }

    private void OnCtaClicked()
    {
        if (IsTrialOffer)
        {
            // «Попробовать N дней бесплатно» buys nothing: the trial takes no card, and its
            // clock is started by the first channel authorization (spec §3), never by this
            // button. So the honest action here is to get out of the user's way.
            Close();
            return;
        }

        string sku = PaywallRows.Sku(_selected, _period);
        if (string.IsNullOrEmpty(sku))
        {
            Debug.LogWarning($"[PaywallController] No SKU for {_selected}/{_period}.");
            return;
        }

        SetBusy(true);
        BillingService.Purchase(sku, (ok, reason) =>
        {
            if (this == null) return;
            SetBusy(false);
            if (ok)
            {
                _purchased = BillingService.PurchasedTier;
                Close();   // the gate re-evaluates off BillingService.OnEntitlementChanged
                return;
            }
            Debug.LogWarning($"[PaywallController] Purchase of {sku} failed: {reason}");
            _notice = reason == "user_cancelled" ? "" : PaywallRows.PurchaseFailedNotice;
            Render();
        });
    }

    private void OnRestoreClicked()
    {
        SetBusy(true);
        BillingService.RestorePurchases(ok =>
        {
            if (this == null) return;
            SetBusy(false);
            _purchased = BillingService.PurchasedTier;
            if (_purchased != PlanTier.None)
            {
                _selected = _purchased;
                Close();
                return;
            }
            _notice = ok ? PaywallRows.RestoreNothingFoundNotice : PaywallRows.RestoreFailedNotice;
            Render();
        });
    }

    private bool IsTrialOffer => !_trialStarted && _purchased == PlanTier.None;

    private void SetBusy(bool busy)
    {
        if (ctaButton != null) ctaButton.interactable = !busy;
        if (restoreButton != null) restoreButton.interactable = !busy;
    }

    // ── Render ───────────────────────────────────────────────────────────────

    private void Render()
    {
        if (headerTitle != null)
            headerTitle.text = _receiptVariant ? PaywallCopy.ReceiptTitle() : PaywallRows.HeaderTitle;
        if (headerSubline != null)
            headerSubline.text = _receiptVariant ? PaywallRows.ReceiptSubline : PaywallRows.HeaderSubline;

        if (receiptBlock != null)
        {
            receiptBlock.SetActive(_receiptVariant);
            if (_receiptVariant) RenderReceipt();
        }

        if (monthLabel != null) monthLabel.text = PaywallRows.PeriodMonth;
        if (yearLabel != null) yearLabel.text = PaywallRows.PeriodYear;
        if (monthFill != null) monthFill.SetActive(_period == PaywallPeriod.Month);
        if (yearFill != null) yearFill.SetActive(_period == PaywallPeriod.Year);
        PaintPeriodLabels();

        var rows = PaywallRows.Build(_period);
        for (int i = 0; i < tierCards.Length; i++)
        {
            var card = tierCards[i];
            if (card?.root == null) continue;
            if (i >= rows.Length) { card.root.SetActive(false); continue; }

            PaywallTierRow row = rows[i];
            card.root.SetActive(true);
            if (card.title != null) card.title.text = row.Title;
            if (card.price != null) card.price.text = row.PriceText;
            if (card.counts != null) card.counts.text = row.CountsLine;
            if (card.crossBotRow != null) card.crossBotRow.SetActive(row.ShowCrossBotLine);
            if (card.popularBadge != null) card.popularBadge.SetActive(row.IsHighlighted);
            if (card.ring != null) card.ring.SetActive(row.Tier == _selected);
        }

        if (ctaLabel != null)
            ctaLabel.text = PaywallRows.CtaText(_trialStarted, _purchased, _selected, _period);
        if (finePrint != null)
            finePrint.text = string.IsNullOrEmpty(_notice) ? PaywallRows.FinePrint : _notice;
        if (restoreLabel != null)
            restoreLabel.text = PaywallRows.RestoreLabel;
    }

    /// <summary>
    /// The segment labels are the ONLY state-dependent colours on this screen, so they
    /// deliberately carry NO ThemedColor binding (two owners would repaint the active
    /// segment back to the inactive ink) — same rule as PromptSuggestionChip/NavTabPalette.
    /// </summary>
    private void PaintPeriodLabels()
    {
        if (monthLabel != null)
            monthLabel.color = Theme.Color(_period == PaywallPeriod.Month ? ThemeRole.AccentOnFill : ThemeRole.InkSecondary);
        if (yearLabel != null)
            yearLabel.color = Theme.Color(_period == PaywallPeriod.Year ? ThemeRole.AccentOnFill : ThemeRole.InkSecondary);
    }

    // ── Value receipt ────────────────────────────────────────────────────────

    private void RenderReceipt()
    {
        string[] values =
        {
            PaywallRows.StatValue(TrialDialogsUsed()),
            PaywallRows.StatValue(TrialOrdersCollected()),
            PaywallRows.StatUnknown,   // not measured anywhere yet — see task-14a report
            PaywallRows.StatUnknown,   // not measured anywhere yet — see task-14a report
        };

        for (int i = 0; i < receiptTiles.Length && i < values.Length; i++)
        {
            var tile = receiptTiles[i];
            if (tile == null) continue;
            if (tile.value != null) tile.value.text = values[i];
            if (tile.label != null && i < PaywallRows.ReceiptLabels.Length) tile.label.text = PaywallRows.ReceiptLabels[i];
        }
    }

    /// <summary>Dialogs metered server-side this period; null while the first GetUsage read hasn't landed.</summary>
    private static int? TrialDialogsUsed() => UsageStore.Current?.used;

    /// <summary>
    /// Orders collected over the trial window, from the «Сводка» disk cache.
    /// Null when that cache has never been filled — a 0 there would claim
    /// «ноль заказов» when the truth is «мы ещё ни разу не считали».
    /// </summary>
    private static int? TrialOrdersCollected()
    {
        List<DashboardOutcome> outcomes = DashboardStore.Load();
        if (DashboardStore.LastFetchMs <= 0) return null;

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var window = new Window
        {
            CurStart = nowMs - PlanCatalog.TrialDays * 86_400_000L,
            CurEnd = nowMs,
        };
        return DashboardMetrics.CountOrders(outcomes, window);
    }
}

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
    // The direct-purchase button under the CTA — shown only while the CTA offers the trial
    // (PaywallRows.SecondaryPurchase). Its own GameObject is the visibility switch, so no
    // separate root ref is needed.
    [SerializeField] private Button purchaseButton;
    [SerializeField] private TextMeshProUGUI purchaseLabel;
    [SerializeField] private Button restoreButton;
    [SerializeField] private TextMeshProUGUI restoreLabel;
    // Legal links row (store submission pack): hidden entirely until LegalLinks carries
    // real URLs, so a build made before the domain exists never shows a dead link.
    [SerializeField] private GameObject legalRow;
    [SerializeField] private Button termsButton;
    [SerializeField] private Button privacyButton;
    [SerializeField] private TextMeshProUGUI termsLabel;
    [SerializeField] private TextMeshProUGUI privacyLabel;

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
    private bool _serverSaysExpired;
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

    private bool _navBarWasActive;

    private void OnEnable()
    {
        Theme.Changed += PaintPeriodLabels;
        UsageStore.OnUsageChanged += HandleUsageChanged;
        BillingService.OnPricesChanged += HandlePricesChanged;
        BillingService.FetchLocalizedPrices();

        // The nav bar draws ABOVE ScreenContainer, so it stayed visible over this
        // full-screen overlay while its taps only switched the screens BENEATH it —
        // dead-looking buttons (owner check 2026-09-01). Hide it for the paywall's
        // lifetime; restore only what we hid, so another surface's own hide survives.
        var navBar = BottomTabManager.Instance;
        _navBarWasActive = navBar != null && navBar.gameObject.activeSelf;
        if (_navBarWasActive) navBar.gameObject.SetActive(false);

        Render();
    }

    private void OnDisable()
    {
        Theme.Changed -= PaintPeriodLabels;
        UsageStore.OnUsageChanged -= HandleUsageChanged;
        BillingService.OnPricesChanged -= HandlePricesChanged;

        if (_navBarWasActive && BottomTabManager.Instance != null)
            BottomTabManager.Instance.gameObject.SetActive(true);
        _navBarWasActive = false;
    }

    /// <summary>
    /// Store-localized prices land async (a GetProducts round-trip started in OnEnable) —
    /// repaint so the tier cards/CTA flip from the KZT fallback to the store's own
    /// strings while the screen is up. Subscription is enable-scoped, so a closed
    /// paywall holds nothing.
    /// </summary>
    private void HandlePricesChanged() => Render();

    /// <summary>
    /// The receipt's «Диалогов» tile reads <see cref="UsageStore.Current"/>, which is null until
    /// the first GetUsage response lands — and at LAUNCH that fetch and the TrialExpired paywall
    /// are started in the same tick (Manager.PreloadSecretsThenInitBilling), so the tile would
    /// otherwise sit on «—» exactly when it is supposed to persuade. Repainting on the store's own
    /// event is the whole fix; subscription is tied to enable/disable, so a closed paywall holds
    /// nothing.
    ///
    /// Since Task 19 the SAME event can also flip the CTA: a snapshot that lands while the paywall
    /// is open is exactly when «сервер говорит expired» becomes known, and a screen left offering
    /// a trial the account cannot have is the bug this task exists to close. A full
    /// <see cref="Render"/> (which repaints the receipt itself when that variant is up) runs only
    /// when the fact actually changed — every other usage read still costs a tile repaint at most.
    /// </summary>
    private void HandleUsageChanged()
    {
        if (!IsOpen) return;

        bool expired = ServerAccountStatus.Expired;
        if (expired != _serverSaysExpired)
        {
            _serverSaysExpired = expired;
            Render();
            return;
        }

        if (!_receiptVariant) return;
        RenderReceipt();
    }

    private RectTransform _bottomBarRt;
    private VerticalLayoutGroup _contentGroup;
    private float _appliedBarClearance = -1f;

    private void LateUpdate() => SyncContentBottomPadding();

    /// <summary>
    /// The scroll content's bottom padding used to be a CONSTANT sized for the bar's
    /// tallest state («scroll slack nobody sees», PaywallBuilder.BuildScrollColumn) —
    /// but the bar height now varies twice at runtime (secondary purchase button, legal
    /// row), and after the 2026-09-01 feature-list trim the slack surfaced as a visible
    /// dead zone under the «Во всех тарифах» card (owner check). Follow the bar's
    /// ACTUAL fitted height instead; +48 keeps the authored breathing room. LateUpdate,
    /// because the bar's own ContentSizeFitter settles a frame after Render toggles its
    /// rows — the cached-clearance guard makes the steady-state cost one float compare.
    /// </summary>
    private void SyncContentBottomPadding()
    {
        if (_contentGroup == null || _bottomBarRt == null) return;
        float clearance = Mathf.Ceil(_bottomBarRt.rect.height) + 48f;
        if (Mathf.Approximately(clearance, _appliedBarClearance)) return;
        _appliedBarClearance = clearance;
        _contentGroup.padding.bottom = (int)clearance;
        LayoutRebuilder.MarkLayoutForRebuild(scroll.content);
    }

    private void EnsureInit()
    {
        if (_rt == null) _rt = GetComponent<RectTransform>();
        if (_rootCanvas == null) _rootCanvas = GetComponentInParent<Canvas>(true);
        if (_contentGroup == null && scroll != null && scroll.content != null)
            _contentGroup = scroll.content.GetComponent<VerticalLayoutGroup>();
        if (_bottomBarRt == null && ctaButton != null)
            for (Transform bar = ctaButton.transform.parent; bar != null; bar = bar.parent)
                if (bar.GetComponent<ContentSizeFitter>() != null)
                {
                    _bottomBarRt = (RectTransform)bar;
                    break;
                }
        if (_wired) return;
        _wired = true;

        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (ctaButton != null) ctaButton.onClick.AddListener(OnCtaClicked);
        if (purchaseButton != null) purchaseButton.onClick.AddListener(StartPurchase);
        if (restoreButton != null) restoreButton.onClick.AddListener(OnRestoreClicked);
        if (termsButton != null) termsButton.onClick.AddListener(() => OpenLegal(LegalLinks.TermsUrl));
        if (privacyButton != null) privacyButton.onClick.AddListener(() => OpenLegal(LegalLinks.PrivacyUrl));
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
        _serverSaysExpired = ServerAccountStatus.Expired;
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
            // button. So the honest action here is to get out of the user's way. Paying right
            // now is reachable through the secondary button below it (Task 18) — which shares
            // StartPurchase with this method's other branch, so there is exactly one buy path.
            Close();
            return;
        }

        StartPurchase();
    }

    /// <summary>
    /// THE purchase path, entered by the CTA once it is the subscribe form and by the
    /// secondary button while the CTA still offers the trial. Both read the same
    /// selection fields, so the button's label and what it buys cannot drift apart.
    /// </summary>
    private void StartPurchase()
    {
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
                RefetchUsage();   // the new tier's quota lives server-side (M-6)
                Close();   // the gate re-evaluates off BillingService.OnEntitlementChanged
                return;
            }
            Debug.LogWarning($"[PaywallController] Purchase of {sku} failed: {reason}");
            _notice = reason == "user_cancelled" ? "" : PaywallRows.PurchaseFailedNotice;
            Render();
        });
    }

    private static void OpenLegal(string url)
    {
        if (string.IsNullOrEmpty(url)) return;   // row is hidden in this state; belt-and-braces
        Application.OpenURL(url);
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
                RefetchUsage();   // a restored plan changes the quota, not just the tier
                Close();
                return;
            }
            _notice = ok ? PaywallRows.RestoreNothingFoundNotice : PaywallRows.RestoreFailedNotice;
            Render();
        });
    }

    /// <summary>
    /// Re-reads the usage snapshot after the plan changed, so the «Боты» strip and «Подписка»
    /// show the new quota without the user having to leave and re-enter the tab (final-review
    /// M-6; the «Подписка» page's own top-up and restore have always done this).
    ///
    /// Hosted on <see cref="Manager.Instance"/>, NEVER on this component: every caller here is
    /// one line away from <see cref="Close"/>, which deactivates this GameObject — and Unity
    /// kills a coroutine whose host goes inactive, so the fetch would be cut off mid-request.
    /// Manager is the always-active host that already runs the boot fetch.
    /// </summary>
    private static void RefetchUsage()
    {
        if (Manager.Instance == null) return;
        Manager.Instance.StartCoroutine(UsageClient.FetchRoutine());
    }

    private bool IsTrialOffer => PaywallRows.IsTrialOffer(_trialStarted, _purchased, _serverSaysExpired);

    private void SetBusy(bool busy)
    {
        if (ctaButton != null) ctaButton.interactable = !busy;
        if (purchaseButton != null) purchaseButton.interactable = !busy;
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

        var localizedPrices = BillingService.LocalizedPrices;
        var rows = PaywallRows.Build(_period, localizedPrices);
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
            ctaLabel.text = PaywallRows.CtaText(_trialStarted, _purchased, _serverSaysExpired, _selected, _period, localizedPrices);

        // The secondary button follows the same selection as the CTA, so a tier/period tap
        // repaints both here — and the seam keeps it OFF in every state where the CTA is
        // already the subscribe form.
        PaywallSecondaryRow secondary =
            PaywallRows.SecondaryPurchase(_trialStarted, _purchased, _serverSaysExpired, _selected, _period, localizedPrices);
        if (purchaseButton != null) purchaseButton.gameObject.SetActive(secondary.Visible);
        if (purchaseLabel != null && secondary.Visible) purchaseLabel.text = secondary.Text;

        if (finePrint != null)
            finePrint.text = !string.IsNullOrEmpty(_notice) ? _notice
                : PaywallRows.FinePrintText(IsTrialOffer,
                    Application.platform == RuntimePlatform.IPhonePlayer);
        if (restoreLabel != null)
            restoreLabel.text = PaywallRows.RestoreLabel;

        if (legalRow != null) legalRow.SetActive(LegalLinks.HasUrls);
        if (termsLabel != null) termsLabel.text = LegalLinks.TermsLabel;
        if (privacyLabel != null) privacyLabel.text = LegalLinks.PrivacyLabel;
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

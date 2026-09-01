using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Profile → «Подписка» (Task 14b, spec §6): the plan card with its status pill and
// renewal date, the dialog/bot/channel meters, and the three store actions
// («Изменить тариф» / «Купить N диалогов» / «Восстановить покупки») plus the
// deep-link out to the store's own subscription management.
//
// Every user-facing string and every state decision comes from the pure, test-pinned
// SubscriptionPageRows — nothing here composes copy. The panel is built additively by
// SubscriptionPageBuilder (Tools/Billing/Build Subscription Page).
public partial class ProfileSubPages
{
    // Store deep-links for «Отменить подписку». Neither store lets an app cancel a
    // subscription itself — the only correct action is to hand the user to the place
    // that can, which is also what Apple's and Google's review guidelines expect.
    private const string AppleSubscriptionsUrl = "https://apps.apple.com/account/subscriptions";
    // package= derived at runtime so a bundle-id rename can never desync this link
    // (it did once: the id was hardcoded before the Choose Reply rename).
    private static string GoogleSubscriptionsUrl =>
        "https://play.google.com/store/account/subscriptions?package=" + Application.identifier;

    // A fill narrower than the bar's own corner radius reads as a dot rather than a
    // sliver, so any non-zero usage draws at least this much of the track.
    private const float MinVisibleFill = 0.02f;

    [Header("Subscription page — plan card")]
    [SerializeField] private TextMeshProUGUI subPlanTitle;
    [SerializeField] private TextMeshProUGUI subPlanSubline;
    [SerializeField] private TextMeshProUGUI subPillLabel;
    // The pill's two colours are state-dependent, so ThemedColor stays their SINGLE
    // owner and we re-point its role instead of writing Graphic.color directly — a
    // runtime colour write plus a binding is the documented two-owner repaint trap.
    [SerializeField] private ThemedColor subPillBgTheme;
    [SerializeField] private ThemedColor subPillInkTheme;

    [Header("Subscription page — meters")]
    // Hidden wholesale at PlanTier.None (see SubscriptionPageRows.MetersVisible): the divider
    // goes with the block, or the card ends on a hairline with nothing under it.
    [SerializeField] private GameObject subMetersBlock;
    [SerializeField] private GameObject subMetersDivider;
    [SerializeField] private TextMeshProUGUI subDialogsValue;
    [SerializeField] private RectTransform subQuotaFill;
    [SerializeField] private ThemedColor subQuotaFillTheme;
    [SerializeField] private TextMeshProUGUI subBotsValue;
    [SerializeField] private TextMeshProUGUI subChannelsValue;

    [Header("Subscription page — actions")]
    [SerializeField] private Button subChangePlanButton;
    [SerializeField] private Button subTopUpButton;
    [SerializeField] private TextMeshProUGUI subTopUpLabel;
    [SerializeField] private Button subRestoreButton;
    [SerializeField] private TextMeshProUGUI subNotice;
    [SerializeField] private GameObject subCancelCard;
    [SerializeField] private GameObject subCancelCaption;
    [SerializeField] private Button subCancelButton;

    private string _subNoticeText = "";
    private bool _subBusy;

    // ── Wiring ─────────────────────────────────────────────────────────────

    private void WireSubscription()
    {
        if (subChangePlanButton != null)
            subChangePlanButton.onClick.AddListener(() => EntitlementGate.RequestPaywall(PaywallTrigger.Browse));
        if (subTopUpButton != null) subTopUpButton.onClick.AddListener(OnTopUpClicked);
        if (subRestoreButton != null) subRestoreButton.onClick.AddListener(OnRestoreClicked);
        if (subCancelButton != null) subCancelButton.onClick.AddListener(OpenStoreSubscriptions);
    }

    // The SubPages root is always active, so these fire exactly once and stay
    // symmetric. Both events can land while the page is closed — repainting a
    // hidden panel is free, and it means an entitlement that changed on the
    // paywall is already correct when this page slides in.
    private void OnEnable()
    {
        UsageStore.OnUsageChanged += RefreshSubscriptionPage;
        BillingService.OnEntitlementChanged += HandleEntitlementChanged;
        // Self-heal: while the Profile tab was away, both events fired into nothing. Repaint
        // on the way back rather than trusting whatever was last rendered.
        RefreshSubscriptionPage();
    }

    private void OnDisable()
    {
        UsageStore.OnUsageChanged -= RefreshSubscriptionPage;
        BillingService.OnEntitlementChanged -= HandleEntitlementChanged;
    }

    private void HandleEntitlementChanged(PlanTier _) => RefreshSubscriptionPage();

    // ── Open ───────────────────────────────────────────────────────────────

    private void OpenSubscriptionPage()
    {
        _subNoticeText = "";
        // A store callback that never came back (app backgrounded mid-purchase) would otherwise
        // leave every action row disabled for the rest of the session.
        _subBusy = false;
        RefreshSubscriptionPage();
        FetchUsage();   // paint the cache instantly, then quietly refresh (house pattern)
    }

    /// <summary>
    /// Re-reads the usage snapshot. Safe on this component: unlike BotSettings, the
    /// SubPages root never deactivates, so the coroutine cannot be killed mid-flight.
    /// </summary>
    private void FetchUsage()
    {
        if (!isActiveAndEnabled) return;
        StartCoroutine(UsageClient.FetchRoutine());
    }

    // ── Render ─────────────────────────────────────────────────────────────

    private void RefreshSubscriptionPage()
    {
        PlanTier purchased = BillingService.PurchasedTier;
        UsageSnapshot usage = UsageStore.Current;

        SubscriptionStatusLine status = SubscriptionPageRows.StatusLine(
            purchased, TrialLedger.DaysLeft(), usage?.periodEnd, usage?.interval);

        if (subPlanTitle != null) subPlanTitle.text = status.Title;
        if (subPlanSubline != null) subPlanSubline.text = status.Subline;
        if (subPillLabel != null) subPillLabel.text = status.PillText;
        if (subPillBgTheme != null) subPillBgTheme.Configure(status.PillBg);
        if (subPillInkTheme != null) subPillInkTheme.Configure(status.PillInk);

        RenderMeters(usage);

        if (subTopUpLabel != null)
        {
            // Prefer the store's localized top-up price (Apple 3.1.2, same rule as the
            // paywall); kick the fetch so a page opened before any paywall still gets
            // them — the next refresh then renders the store string.
            BillingService.FetchLocalizedPrices();
            string localizedTopUp = null;
            var storePrices = BillingService.LocalizedPrices;
            if (storePrices != null) storePrices.TryGetValue(PlanCatalog.SkuTopUp, out localizedTopUp);
            subTopUpLabel.text = SubscriptionPageRows.TopUpRowText(localizedTopUp);
        }
        if (subNotice != null)
        {
            subNotice.text = _subNoticeText;
            subNotice.gameObject.SetActive(!string.IsNullOrEmpty(_subNoticeText));
        }

        bool cancellable = SubscriptionPageRows.CancelVisible(purchased);
        if (subCancelCard != null) subCancelCard.SetActive(cancellable);
        if (subCancelCaption != null)
        {
            // 2.3.10: the scene seed is store-neutral; the per-store wording is
            // stamped here so the iOS binary never displays «Google Play» (mirrors
            // PaywallController's FinePrintText platform branch).
            var captionTmp = subCancelCaption.GetComponentInChildren<TextMeshProUGUI>(true);
            if (captionTmp != null)
                captionTmp.text = SubscriptionPageRows.CancelCaptionText(
                    Application.platform == RuntimePlatform.IPhonePlayer);
            subCancelCaption.SetActive(cancellable);
        }

        SetSubscriptionBusy(_subBusy);
    }

    private void RenderMeters(UsageSnapshot usage)
    {
        // Limits follow the EFFECTIVE tier (a live trial has its own caps), and the
        // quota falls back to the catalog whenever the server has not spoken yet.
        PlanTier tier = EntitlementGate.CurrentTier;
        PlanSpec spec = PlanCatalog.Get(tier);

        bool metersVisible = SubscriptionPageRows.MetersVisible(tier);
        if (subMetersBlock != null) subMetersBlock.SetActive(metersVisible);
        if (subMetersDivider != null) subMetersDivider.SetActive(metersVisible);
        if (!metersVisible) return;   // None has no allowances to measure against

        int quota = usage != null && usage.quota > 0 ? usage.quota : spec.DialogQuota;

        SubscriptionUsageLine dialogs = usage == null
            ? SubscriptionPageRows.UnknownUsageLine(quota)
            : SubscriptionPageRows.UsageLine(usage.used, quota, usage.topupBalance);

        if (subDialogsValue != null) subDialogsValue.text = dialogs.Text;
        if (subQuotaFillTheme != null) subQuotaFillTheme.Configure(SubscriptionPageRows.FillRole(dialogs.State));

        float fill = usage == null
            ? 0f
            : SubscriptionPageRows.FillFraction(usage.used, quota);
        ApplyQuotaFill(fill);

        // Bots and channels are read LOCALLY, not from the snapshot: these are exactly
        // the numbers EntitlementGate enforces against, so reading them anywhere else
        // could show a limit the user does not actually hit (or hide one they do).
        Transform botsRoot = Manager.Instance != null ? Manager.Instance.BotsRoot : null;
        int bots = botsRoot != null ? botsRoot.childCount : 0;
        if (subBotsValue != null) subBotsValue.text = SubscriptionPageRows.CountLine(bots, spec.MaxBots);
        if (subChannelsValue != null)
            subChannelsValue.text = SubscriptionPageRows.CountLine(
                EntitlementGate.ConnectedChannelCount(), spec.MaxChannels);
    }

    /// <summary>
    /// The bar is driven by the fill's right ANCHOR, not by a width — so it stays correct
    /// at any canvas size and needs no layout pass.
    /// </summary>
    private void ApplyQuotaFill(float fraction)
    {
        if (subQuotaFill == null) return;

        bool visible = fraction > 0f;
        subQuotaFill.gameObject.SetActive(visible);
        if (!visible) return;

        float shown = Mathf.Clamp(fraction, MinVisibleFill, 1f);
        subQuotaFill.anchorMin = new Vector2(0f, 0f);
        subQuotaFill.anchorMax = new Vector2(shown, 1f);
        subQuotaFill.offsetMin = Vector2.zero;
        subQuotaFill.offsetMax = Vector2.zero;
    }

    // ── Actions ────────────────────────────────────────────────────────────

    private void OnTopUpClicked()
    {
        SetSubscriptionBusy(true);
        BillingService.Purchase(PlanCatalog.SkuTopUp, (ok, reason) =>
        {
            if (this == null) return;
            SetSubscriptionBusy(false);

            if (ok)
            {
                _subNoticeText = SubscriptionPageRows.TopUpDoneNotice;
                FetchUsage();   // the new balance lives server-side; re-read it
            }
            else
            {
                // A cancelled purchase needs no message — the user just did it.
                Debug.LogWarning($"[Subscription] Top-up failed: {reason}");
                _subNoticeText = reason == "user_cancelled" ? "" : SubscriptionPageRows.TopUpFailedNotice;
            }
            RefreshSubscriptionPage();
        });
    }

    private void OnRestoreClicked()
    {
        SetSubscriptionBusy(true);
        BillingService.RestorePurchases(ok =>
        {
            if (this == null) return;
            SetSubscriptionBusy(false);

            bool found = BillingService.PurchasedTier != PlanTier.None;
            _subNoticeText = found
                ? ""
                : ok ? PaywallRows.RestoreNothingFoundNotice : PaywallRows.RestoreFailedNotice;

            if (found) FetchUsage();   // a restored plan changes the quota, not just the tier
            RefreshSubscriptionPage();
        });
    }

    private static void OpenStoreSubscriptions()
    {
#if UNITY_IOS
        Application.OpenURL(AppleSubscriptionsUrl);
#elif UNITY_ANDROID
        Application.OpenURL(GoogleSubscriptionsUrl);
#else
        // Editor/desktop: there is no store session to open, so say so rather than
        // launching a page that cannot act on this device.
        Debug.Log("[Subscription] Управление подпиской доступно только на устройстве (App Store / Google Play).");
#endif
    }

    private void SetSubscriptionBusy(bool busy)
    {
        _subBusy = busy;
        if (subTopUpButton != null) subTopUpButton.interactable = !busy;
        if (subRestoreButton != null) subRestoreButton.interactable = !busy;
        if (subChangePlanButton != null) subChangePlanButton.interactable = !busy;
    }
}

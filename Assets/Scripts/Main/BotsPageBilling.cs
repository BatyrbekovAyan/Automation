using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The «Боты» page's billing surface (Task 14c, spec §6): the header trial pill, the
/// account-level dialog meter strip between the header and the list, and the «+ бот»
/// ghost card that closes the list with the plan's remaining bot slots.
///
/// A SEPARATE component on the BotsPage GameObject rather than fields on
/// <see cref="BotsPage"/> itself: it shares that object's activation lifecycle (so
/// OnEnable fires exactly when the Bots tab becomes visible) without touching BotsPage's
/// serialized layout, which several builders already stamp. Every string and state
/// decision comes from the pure, test-pinned <see cref="BotsPageRows"/> /
/// <see cref="SubscriptionPageRows"/> — nothing here composes copy or re-derives a limit.
/// The scene surface is built additively by <c>BotsPageBillingWirer</c>
/// (Tools/Billing/Wire Bots Page Billing).
///
/// THREE pieces of page geometry are owned here, and this component is their SOLE writer
/// (the two-owner repaint/relayout trap):
///  • <c>ScrollContent.offsetMax.y</c> and <c>FirstStepsCard.anchoredPosition.y</c> both
///    move down by the strip's block while it is visible. They move by the SAME amount, so
///    the «Первые шаги» banner keeps exactly the clearance its own top-padding reservation
///    buys it — that reservation (<c>padding.top</c>) stays FirstStepsCard's alone.
///  • <c>BotsParent.padding.bottom</c> is grown by the «+ бот» card's block so the list's
///    own content height contains it; the card then rides the content's scroll offset and
///    behaves like a final row without ever being a CHILD of BotsParent, whose
///    <c>childCount</c> is the authoritative «has bots» fact in six places.
/// Each has a SERIALIZED base (stamped by the wirer) and is always recomputed from it, so
/// a toggle can never accumulate — see the «Layout bases» block for why the base is not
/// captured live at Awake.
/// </summary>
// Ordered AFTER ScrollRect (execution order 0), which moves the list's content in its own
// LateUpdate: the «+ бот» card is a sibling of that content and carries its scroll offset by
// hand, so reading the offset before ScrollRect writes it would leave the card one frame
// behind the list through every fling.
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class BotsPageBilling : MonoBehaviour
{
    // ── Layout constants (1080×1920 canvas reference units) ──────────────────
    // PUBLIC because BotsPageBillingWirer authors the scene's edit-time preview from the
    // very same numbers — two copies of the strip's height would drift on the first tweak.

    /// <summary>Gap between the header's bottom edge and the strip.</summary>
    public const float StripGap = 24f;

    /// <summary>28 top padding + 44 title row + 18 gap + 18 bar + 28 bottom.</summary>
    public const float StripHeightCompact = 136f;

    /// <summary>…+ 16 gap + 40 hint line. Re-derive both together if the strip's children move.</summary>
    public const float StripHeightWithHint = 192f;

    /// <summary>«+ бот» card height. Shorter than a bot card's 232 — it is a ghost row.</summary>
    public const float AddCardHeight = 200f;

    /// <summary>Header pill: 120 tap target around a 68 chip (house touch floor is 120–132).</summary>
    public const float PillHeight = 120f;

    /// <summary>A fill narrower than the bar's own radius reads as a dot (ProfileSubPages precedent).</summary>
    private const float MinVisibleFill = 0.02f;

    // ── Serialized surface (stamped by BotsPageBillingWirer) ─────────────────

    [Header("Header trial pill")]
    [SerializeField] private GameObject pillRoot;
    [SerializeField] private RectTransform pillRect;
    [SerializeField] private Button pillButton;
    [SerializeField] private TextMeshProUGUI pillLabel;
    // The pill's two colours are state-dependent, so ThemedColor stays their SINGLE owner
    // and we re-point its role instead of writing Graphic.color — a runtime colour write
    // alongside a binding is the documented two-owner repaint trap.
    [SerializeField] private ThemedColor pillBgTheme;
    [SerializeField] private ThemedColor pillInkTheme;
    [Tooltip("Horizontal padding inside the chip; the pill's width is measured from the label.")]
    [SerializeField] private float pillPaddingX = 28f;

    [Header("Usage strip")]
    [SerializeField] private GameObject stripRoot;
    [SerializeField] private RectTransform stripRect;
    [SerializeField] private Button stripButton;
    [SerializeField] private TextMeshProUGUI stripTitle;
    [SerializeField] private TextMeshProUGUI stripValue;
    [SerializeField] private RectTransform stripBarFill;
    [SerializeField] private ThemedColor stripBarFillTheme;
    [SerializeField] private GameObject stripHintRoot;
    [SerializeField] private TextMeshProUGUI stripHint;
    [SerializeField] private ThemedColor stripHintTheme;

    [Header("«+ бот» card")]
    [SerializeField] private GameObject addCardRoot;
    [SerializeField] private RectTransform addCardRect;
    [SerializeField] private Button addCardButton;
    [SerializeField] private TextMeshProUGUI addCardTitle;
    [SerializeField] private TextMeshProUGUI addCardSubtext;

    [Header("Page geometry this component drives")]
    [SerializeField] private BotsPage botsPage;
    [Tooltip("BotsParent — the ScrollRect content the bot cards live in.")]
    [SerializeField] private RectTransform botsList;
    [Tooltip("ScrollContent — its top edge moves down to make room for the strip.")]
    [SerializeField] private RectTransform scrollContent;
    [Tooltip("«Первые шаги» banner — moves down with the strip so the two never overlap.")]
    [SerializeField] private RectTransform firstStepsCard;

    [Header("Layout bases — the UN-INSET authored values (stamped by BotsPageBillingWirer)")]
    // SERIALIZED, not captured in Awake: the wirer authors the scene WITH the strip's
    // preview inset applied (so the Scene view is not lying about the page), and a live read
    // at Awake would then take the already-inset value as the base and double it. The wirer
    // captures each base once, on the first run, and preserves it on every re-run.
    [Tooltip("ScrollContent.offsetMax.y with no strip showing.")]
    [SerializeField] private float scrollTopBase = -300f;
    [Tooltip("FirstStepsCard.anchoredPosition.y with no strip showing.")]
    [SerializeField] private float firstStepsBaseY = -328f;
    [Tooltip("BotsParent's VerticalLayoutGroup padding.bottom with no «+ бот» card.")]
    [SerializeField] private int listPadBottomBase = 44;
    [Tooltip("Set by the wirer once the three bases above hold real captured values. Read only " +
             "by the wirer, to know that a re-run must PRESERVE them instead of re-reading the " +
             "(by then already inset) live rects.")]
    [HideInInspector, SerializeField] private bool layoutBasesStamped;

    /// <summary>Set in Awake so the fact-changing moments (bot created, bot deleted) can
    /// refresh this surface without a serialized reference — the FirstStepsCard idiom.</summary>
    public static BotsPageBilling Instance;

    private VerticalLayoutGroup _listLayout;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
        if (botsList != null) _listLayout = botsList.GetComponent<VerticalLayoutGroup>();

        if (pillButton != null) pillButton.onClick.AddListener(OpenPaywall);
        if (stripButton != null) stripButton.onClick.AddListener(OpenPaywall);
        if (addCardButton != null) addCardButton.onClick.AddListener(OpenAddBot);
    }

    private void OnEnable()
    {
        UsageStore.OnUsageChanged += Refresh;
        BillingService.OnEntitlementChanged += HandleEntitlementChanged;
        // Both events fire into nothing while the Bots tab is away, so repaint on the way
        // back rather than trusting whatever was last rendered. The usage FETCH is not
        // started here: BotsPage.OnEnable already owns that trigger on this same object.
        Refresh();
    }

    private void OnDisable()
    {
        UsageStore.OnUsageChanged -= Refresh;
        BillingService.OnEntitlementChanged -= HandleEntitlementChanged;
    }

    private void HandleEntitlementChanged(PlanTier _) => Refresh();

    /// <summary>
    /// Keeps the «+ бот» card seated under the last bot card as the list grows, shrinks
    /// or scrolls. A per-frame float compare on one screen — the list's height changes
    /// asynchronously (card binds, the banner's padding reservation, a layout pass landing
    /// after LateUpdate), so an event-only position would be a frame behind on some of them
    /// and permanently wrong on others.
    /// </summary>
    private void LateUpdate()
    {
        if (addCardRoot == null || !addCardRoot.activeSelf) return;
        PositionAddCard();
    }

    // ── Render ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Repaint from live facts. Public so the fact-changing moments already wired for the
    /// «Первые шаги» card (bot created via BotsPage.RefreshEmptyState, bot deleted via
    /// Bot.DeleteBot) refresh this surface too. Never opens anything, so it is safe on the
    /// «Удалить все данные» wipe path.
    /// </summary>
    public void Refresh()
    {
        PlanTier tier = EntitlementGate.CurrentTier;
        int bots = botsList != null ? botsList.childCount : 0;

        RenderPill(tier);
        RenderStrip(tier, bots);
        RenderAddCard(tier, bots);
    }

    private void RenderPill(PlanTier tier)
    {
        TrialPillRow pill = BotsPageRows.TrialPill(tier, TrialLedger.HasStarted, TrialLedger.DaysLeft());
        if (pillRoot != null) pillRoot.SetActive(pill.Visible);
        if (!pill.Visible) return;

        if (pillLabel != null)
        {
            pillLabel.text = pill.Text;
            ResizePill();
        }
        if (pillBgTheme != null) pillBgTheme.Configure(pill.Bg);
        if (pillInkTheme != null) pillInkTheme.Configure(pill.Ink);
    }

    /// <summary>
    /// HeaderIcons has childControlWidth OFF, so its HorizontalLayoutGroup allocates from
    /// each child's own sizeDelta and ignores LayoutElement entirely — the width has to be
    /// written onto the rect. Measuring the label (rather than pinning a constant) is what
    /// keeps a reworded pill from clipping; the group is right-aligned, so the extra width
    /// grows leftwards, away from the «+» button.
    /// </summary>
    private void ResizePill()
    {
        if (pillRect == null || pillLabel == null) return;

        float width = pillLabel.GetPreferredValues(pillLabel.text).x + pillPaddingX * 2f;
        pillRect.sizeDelta = new Vector2(Mathf.Ceil(width), PillHeight);
        if (pillRect.parent is RectTransform parent) LayoutRebuilder.MarkLayoutForRebuild(parent);
    }

    private void RenderStrip(PlanTier tier, int bots)
    {
        bool visible = BotsPageRows.MeterVisible(tier, bots);
        if (stripRoot != null) stripRoot.SetActive(visible);
        if (!visible)
        {
            ApplyStripInset(0f);
            return;
        }

        UsageSnapshot usage = UsageStore.Current;
        PlanSpec spec = PlanCatalog.Get(tier);
        // The catalog carries the quota until the server has spoken (same fallback the
        // «Подписка» meters use), so the denominator is never a placeholder zero.
        int quota = usage != null && usage.quota > 0 ? usage.quota : spec.DialogQuota;

        // DateTime.Now (device-local) is deliberate: this is a calendar LABEL for the owner,
        // not a join key. RuDateFormat — never ToString("MMMM"), which follows the locale.
        if (stripTitle != null) stripTitle.text = BotsPageRows.MeterTitle(DateTime.Now);

        SubscriptionUsageLine line = usage == null
            ? SubscriptionPageRows.UnknownUsageLine(quota)
            : SubscriptionPageRows.UsageLine(usage.used, quota, usage.topupBalance);
        if (stripValue != null) stripValue.text = line.Text;
        if (stripBarFillTheme != null) stripBarFillTheme.Configure(SubscriptionPageRows.FillRole(line.State));

        ApplyFill(usage == null
            ? 0f
            : SubscriptionPageRows.FillFraction(usage.used, quota, usage.topupBalance));

        // No snapshot means no honest hint — «— из 300» already says the number is unknown.
        string hint = usage == null ? null : BotsPageRows.MeterHint(usage.used, quota, usage.topupBalance);
        bool hasHint = !string.IsNullOrEmpty(hint);
        if (stripHintRoot != null) stripHintRoot.SetActive(hasHint);
        if (hasHint)
        {
            if (stripHint != null) stripHint.text = hint;
            if (stripHintTheme != null) stripHintTheme.Configure(BotsPageRows.HintRole(line.State));
        }

        float height = hasHint ? StripHeightWithHint : StripHeightCompact;
        if (stripRect != null) stripRect.sizeDelta = new Vector2(stripRect.sizeDelta.x, height);
        ApplyStripInset(StripGap + height);
    }

    /// <summary>
    /// The bar is driven by the fill's right ANCHOR, not a width, so it stays correct at any
    /// canvas size and needs no layout pass (ProfileSubPages.ApplyQuotaFill precedent).
    /// </summary>
    private void ApplyFill(float fraction)
    {
        if (stripBarFill == null) return;

        bool visible = fraction > 0f;
        stripBarFill.gameObject.SetActive(visible);
        if (!visible) return;

        float shown = Mathf.Clamp(fraction, MinVisibleFill, 1f);
        stripBarFill.anchorMin = new Vector2(0f, 0f);
        stripBarFill.anchorMax = new Vector2(shown, 1f);
        stripBarFill.offsetMin = Vector2.zero;
        stripBarFill.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Pushes the list region and the «Первые шаги» banner down by the strip's block —
    /// both by the SAME amount, so the banner keeps exactly the clearance its own
    /// <c>padding.top</c> reservation buys it. Always recomputed from the captured base,
    /// so repeated calls cannot accumulate.
    /// </summary>
    private void ApplyStripInset(float block)
    {
        if (scrollContent != null)
        {
            Vector2 max = scrollContent.offsetMax;
            float target = scrollTopBase - block;
            if (!Mathf.Approximately(max.y, target))
            {
                max.y = target;
                scrollContent.offsetMax = max;
            }
        }

        if (firstStepsCard != null)
        {
            Vector2 pos = firstStepsCard.anchoredPosition;
            float target = firstStepsBaseY - block;
            if (!Mathf.Approximately(pos.y, target))
            {
                pos.y = target;
                firstStepsCard.anchoredPosition = pos;
            }
        }
    }

    private void RenderAddCard(PlanTier tier, int bots)
    {
        // Zero bots is the EmptyState's screen, and its own CTA is the add-bot entry there.
        bool visible = bots > 0;
        if (addCardRoot != null) addCardRoot.SetActive(visible);
        ReserveListBottom(visible);
        if (!visible) return;

        if (addCardTitle != null) addCardTitle.text = BotsPageRows.AddBotTitle;
        if (addCardSubtext != null) addCardSubtext.text = BotsPageRows.AddBotSubtext(tier, bots);

        // Refresh() runs on exactly the frames the list CHANGED (a bot created or deleted),
        // and PositionAddCard seats the card off botsList.rect.height — which uGUI has not
        // recomputed yet: ReserveListBottom's MarkLayoutForRebuild is deferred to the end of
        // the frame, and the new/removed card's own row is queued with it. Reading the stale
        // height parks the card at the PREVIOUS list length for one frame, which is the pop
        // on create/delete. Forcing the rebuild here makes the position right in the same
        // frame the fact changed; LateUpdate keeps it right afterwards.
        if (botsList != null) LayoutRebuilder.ForceRebuildLayoutImmediate(botsList);
        PositionAddCard();
    }

    /// <summary>
    /// Grows the list's own bottom padding by the card's block so the scrollable content
    /// CONTAINS the card's slot. <c>padding.bottom</c> is untouched by anything else on this
    /// page (FirstStepsCard owns <c>padding.top</c> and only that), which is what keeps this
    /// a single-owner write.
    /// </summary>
    private void ReserveListBottom(bool reserve)
    {
        if (_listLayout == null) return;

        int target = reserve
            ? listPadBottomBase + Mathf.RoundToInt(_listLayout.spacing + AddCardHeight)
            : listPadBottomBase;
        if (_listLayout.padding.bottom == target) return;

        _listLayout.padding.bottom = target;
        if (botsList != null) LayoutRebuilder.MarkLayoutForRebuild(botsList);
    }

    /// <summary>
    /// Seats the card one list-spacing below the last bot card. The card is a SIBLING of the
    /// content (not a child — BotsParent.childCount is the app's «has bots» fact), so it has
    /// to carry the content's own scroll offset itself; both rects share the Viewport's top
    /// edge as their anchor, which makes that a plain addition.
    /// </summary>
    private void PositionAddCard()
    {
        if (addCardRect == null || botsList == null || _listLayout == null) return;

        float contentTop = botsList.anchoredPosition.y;
        float slotTop = contentTop - (botsList.rect.height - _listLayout.padding.bottom + _listLayout.spacing);
        if (Mathf.Approximately(addCardRect.anchoredPosition.y, slotTop)) return;

        addCardRect.anchoredPosition = new Vector2(addCardRect.anchoredPosition.x, slotTop);
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private static void OpenPaywall() => EntitlementGate.RequestPaywall(PaywallTrigger.Browse);

    /// <summary>
    /// Routes through <see cref="BotsPage.StartNewBot"/>, which already refuses over the plan's
    /// bot limit and raises the paywall itself — so the card needs no gate of its own and the
    /// «Лимит ботов тарифа» state stays tappable rather than dead.
    /// </summary>
    private void OpenAddBot()
    {
        if (botsPage != null) botsPage.StartNewBot();
        else BotsPage.Instance?.StartNewBot();
    }
}

using UnityEngine;
using UnityEngine.UI;

public class BotsPage : MonoBehaviour
{
    [Tooltip("Plus button in the Bots page header (top-right).")]
    [SerializeField] private Button NewBotButton;

    [Tooltip("Empty-state root shown when no bots exist (hero + CTA).")]
    [SerializeField] private GameObject emptyState;

    [Tooltip("Parent holding the Bot cards (Manager.BotsParent).")]
    [SerializeField] private Transform botsParent;

    [Tooltip("Screen_Onboarding root (first-run welcome carousel). Stamped by OnboardingScreenBuilder.")]
    [SerializeField] private GameObject onboardingScreen;

    public static BotsPage Instance;

    void Start()
    {
        Instance = this;
        if (NewBotButton != null)
            NewBotButton.onClick.AddListener(StartNewBot);
    }

    void OnEnable()
    {
        // Deferred one frame so a tab switch settles and freshly-created/deleted
        // cards are counted. RefreshEmptyState both toggles the empty UI and, when
        // there are zero bots, auto-opens the Add-Bot overlay.
        // IN-06: no isActiveAndEnabled guard — it is always true inside OnEnable.
        Invoke(nameof(RefreshEmptyState), 0f);

        // Task 11 usage-snapshot fetch trigger #2 (boot is #1, in
        // Manager.PreloadSecretsThenInitBilling): the Bots tab becoming visible. Best-effort —
        // UsageClient.FetchRoutine() already swallows its own errors (non-200/garbage keeps the
        // cache, logs a warning), so no try/catch needed here.
        StartCoroutine(UsageClient.FetchRoutine());
    }

    void OnDisable()
    {
        CancelInvoke(nameof(RefreshEmptyState));
    }

    public void RefreshEmptyState()
    {
        int liveBots = botsParent != null ? botsParent.childCount : 0;
        bool hasBots = liveBots > 0;
        if (emptyState != null) emptyState.SetActive(!hasBots);

        // D1 authority + return-to-Bots refresh: the checklist mirrors the live bot count.
        // With zero bots the ShouldShow gate hides it so only the EmptyState renders (no
        // overlap on the wizard back-out repro). Fire-and-forget; null-guarded no-op if the
        // card is not yet awake.
        FirstStepsCard.Instance?.RefreshFromFacts();

        // Same fact, same moment: the trial pill, the dialog meter and the «+ бот» card's
        // remaining-slot subtext all read the live bot count (Task 14c). Fire-and-forget,
        // null-guarded no-op if the billing surface is not yet awake.
        BotsPageBilling.Instance?.Refresh();

        if (!hasBots)
        {
            // Single zero-bot chokepoint (the Chats empty-state CTA also routes here
            // via SwitchTab(Bots)→RefreshEmptyState): show the first-run carousel on a
            // true first launch, otherwise fall back to the existing auto-open. The
            // null-guard keeps a not-yet-built scene on the existing behaviour so a
            // brand-new user is never trapped on a dead end.
            bool seen = PlayerPrefs.GetInt(OnboardingKeys.Seen, 0) == 1;
            if (onboardingScreen != null && OnboardingGate.ShouldShowCarousel(hasBots, seen))
            {
                onboardingScreen.SetActive(true);   // carousel instead of the auto-open
            }
            else if (BotsPageRows.ShouldAutoOpenWizard(EntitlementGate.CurrentTier, liveBots))
            {
                StartNewBot();                       // existing auto-open (idempotent, unchanged)
            }
            // Nothing else: at PlanTier.None the auto-open would refuse, and a refusal raises the
            // limit sheet — so merely ARRIVING here (tab switch, wizard back-out) would throw a
            // modal every time. The empty state above is already showing; its CTA is a real tap,
            // and THAT still refuses into the sheet. Owner default: no modal on auto-open.
        }
    }

    /// <summary>
    /// Opens the Add-Bot overlay. Ensures the Bots tab is active first so closing the
    /// form always lands on the Bots page. Idempotent (AddBotPanel.Open no-ops when
    /// already open). Public so the header + and the chats empty-state CTA share it.
    /// </summary>
    public void StartNewBot() => TryStartNewBot();

    /// <summary>
    /// <see cref="StartNewBot"/> with its verdict: <c>false</c> means the plan's bot limit
    /// refused, and the gate sheet / paywall is what the owner sees instead.
    ///
    /// Exists because a caller that follows up on the wizard (EmptyStateView preselects the
    /// platform, and used to force the panel open defensively) must be able to tell a
    /// REFUSAL from a failure — the void overload stays for UnityEvent wiring, which cannot
    /// bind a bool-returning method.
    /// </summary>
    public bool TryStartNewBot()
    {
        int existingBots = botsParent != null ? botsParent.childCount : 0;
        if (!EntitlementGate.CanCreateBot(existingBots))
        {
            // Task 19: тариф упёрся в потолок — лёгкий лист; сервер сказал «expired» — полный
            // пейволл с «чеком ценности», потому что потолка тут нет, кончилась подписка.
            EntitlementGate.RequestPaywall(EntitlementGate.BotRefusalTrigger(ServerAccountStatus.Expired));
            return false;
        }

        var tabs = BottomTabManager.Instance;   // IN-08
        if (tabs != null && tabs.ActiveTabIndex != BottomTabManager.BotsTabIndex)
            tabs.SwitchTab(BottomTabManager.BotsTabIndex);
        AddBotPanel.Instance?.Open();
        return true;
    }
}

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
        if (isActiveAndEnabled) Invoke(nameof(RefreshEmptyState), 0f);
    }

    void OnDisable()
    {
        CancelInvoke(nameof(RefreshEmptyState));
    }

    public void RefreshEmptyState()
    {
        bool hasBots = botsParent != null && botsParent.childCount > 0;
        if (emptyState != null) emptyState.SetActive(!hasBots);

        // D1 authority + return-to-Bots refresh: the checklist mirrors the live bot count.
        // With zero bots the ShouldShow gate hides it so only the EmptyState renders (no
        // overlap on the wizard back-out repro). Fire-and-forget; null-guarded no-op if the
        // card is not yet awake.
        FirstStepsCard.Instance?.RefreshFromFacts();

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
            else
            {
                StartNewBot();                       // existing auto-open (idempotent, unchanged)
            }
        }
    }

    /// <summary>
    /// Opens the Add-Bot overlay. Ensures the Bots tab is active first so closing the
    /// form always lands on the Bots page. Idempotent (AddBotPanel.Open no-ops when
    /// already open). Public so the header + and the chats empty-state CTA share it.
    /// </summary>
    public void StartNewBot()
    {
        var tabs = FindFirstObjectByType<BottomTabManager>();
        if (tabs != null && tabs.ActiveTabIndex != BottomTabManager.BotsTabIndex)
            tabs.SwitchTab(BottomTabManager.BotsTabIndex);
        AddBotPanel.Instance?.Open();
    }
}

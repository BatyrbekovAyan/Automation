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
        if (!hasBots) StartNewBot();   // idempotent — no double-open if already open
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

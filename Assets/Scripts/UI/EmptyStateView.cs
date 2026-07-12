using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CanvasGroup))]
public class EmptyStateView : MonoBehaviour
{
    [Header("UI references")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI bodyLabel;
    [SerializeField] private Button primaryButton;
    [SerializeField] private TextMeshProUGUI primaryButtonLabel;

    // [Header("Icons (drag in inspector)")]
    // [SerializeField] private Sprite iconNoBots;
    // [SerializeField] private Sprite iconNoWhatsApp;

    private CanvasGroup canvasGroup;
    private EmptyStateReason? _lastReason;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        Hide();
    }

    private void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnEmptyState += HandleEmptyState;
            ChatManager.Instance.OnActiveBotChanged += HandleActiveBotChanged;
            ChatManager.Instance.OnChatAdded += HandleChatAdded;

            // Catch up to the current state. The initial OnEmptyState event may have
            // fired before this view's GameObject was activated (Screen_Whatsapp is
            // inactive at scene load), in which case our subscription missed it.
            EmptyStateReason? reason = ChatManager.Instance.ComputeCurrentEmptyState();
            if (reason.HasValue)
            {
                HandleEmptyState(reason.Value);
            }
            else if (ChatManager.Instance.Chats != null && ChatManager.Instance.Chats.Count > 0)
            {
                Hide();
            }
        }
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnEmptyState -= HandleEmptyState;
            ChatManager.Instance.OnActiveBotChanged -= HandleActiveBotChanged;
            ChatManager.Instance.OnChatAdded -= HandleChatAdded;
        }
        if (primaryButton != null) primaryButton.onClick.RemoveAllListeners();
        // Reset so the next OnEnable re-runs ConfigureForReason and RE-WIRES the button.
        // Without this, the _lastReason guard skips re-config on re-entry (same reason),
        // leaving the just-cleared button with no click listener — so it works once then
        // goes dead on the second visit.
        _lastReason = null;
    }

    private void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    private void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        _lastReason = null;
    }

    private void HandleEmptyState(EmptyStateReason reason)
    {
        // Guard against double-fire during the OnEnable catch-up race: the
        // catch-up call and a real OnEmptyState event can both deliver the
        // same reason in the same frame. Reprocessing is safe but wasteful
        // (re-applies sprites, re-binds button listeners).
        if (_lastReason == reason) return;
        _lastReason = reason;

        ConfigureForReason(reason);
        Show();
    }

    private void HandleActiveBotChanged(string _) => Hide();

    private void HandleChatAdded(ChatViewModel _) => Hide();

    private void ConfigureForReason(EmptyStateReason reason)
    {
        switch (reason)
        {
            case EmptyStateReason.NoBotsExist:
                // if (iconImage != null) iconImage.sprite = iconNoBots;
                if (titleLabel != null) titleLabel.text = "Create your first bot";
                if (bodyLabel != null) bodyLabel.text = "An AI assistant that answers your customers on WhatsApp, day and night.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Create a bot";
                if (primaryButton != null)
                {
                    primaryButton.onClick.RemoveAllListeners();
                    primaryButton.onClick.AddListener(OpenCreateBotFlow);
                }
                break;

            case EmptyStateReason.BotHasNoWhatsApp:
                // if (iconImage != null) iconImage.sprite = iconNoWhatsApp;
                if (titleLabel != null) titleLabel.text = "WhatsApp not connected";
                if (bodyLabel != null) bodyLabel.text = "Connect WhatsApp to this bot to see its chats.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Connect WhatsApp";
                if (primaryButton != null)
                {
                    primaryButton.onClick.RemoveAllListeners();
                    primaryButton.onClick.AddListener(OpenCurrentBotAuth);
                }
                break;

            case EmptyStateReason.BotHasNoTelegram:
                // if (iconImage != null) iconImage.sprite = iconNoWhatsApp;
                if (titleLabel != null) titleLabel.text = "Telegram not connected";
                if (bodyLabel != null) bodyLabel.text = "Connect Telegram to this bot to see its chats.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Connect Telegram";
                if (primaryButton != null)
                {
                    // Same CTA as WhatsApp — OpenCurrentBotAuth opens BotSettings; no
                    // channel-specific routing is needed this phase (Phase 6 owns that).
                    primaryButton.onClick.RemoveAllListeners();
                    primaryButton.onClick.AddListener(OpenCurrentBotAuth);
                }
                break;
        }
    }

    private void OpenCreateBotFlow()
    {
        // Reuse the exact add-bot entry the header "+" uses: StartNewBot ensures the
        // Bots tab is active, then opens the Add-Bot overlay directly. Find it
        // include-inactive so it works even if the Bots tab was never opened this
        // session (BotsPage.Instance is set only after Screen_Bots first activates).
        BotsPage botsPage = FindFirstObjectByType<BotsPage>(FindObjectsInactive.Include);
        if (botsPage != null) botsPage.StartNewBot();

        // We launched from the WhatsApp page — preselect WhatsApp as the platform so the
        // user lands on the form with it already chosen. StartNewBot switched to the form
        // synchronously, so the page is active and SelectPlatform's UI updates apply.
        if (Manager.Instance != null) Manager.Instance.SelectPlatform(1);
    }

    private void OpenCurrentBotAuth()
    {
        if (ChatManager.Instance == null) return;
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(ChatManager.Instance.CurrentBotId) : null;
        if (bot == null) return;

        // Switch to Screen_Bots first. SwitchTab toggles SetActive on the screen panels
        // synchronously, so by the next line Screen_Bots is the active screen container.
        BottomTabManager tabManager = FindFirstObjectByType<BottomTabManager>(FindObjectsInactive.Include);
        if (tabManager != null)
        {
            tabManager.SwitchTab(BottomTabManager.BotsTabIndex);
        }

        // Bot.EditButton is wired to the existing OpenSettings flow (SettingsPage
        // activation + slide-in animation). Invoking it avoids exposing OpenSettings
        // publicly or calling SendMessage by string name.
        if (bot.EditButton != null) bot.EditButton.onClick.Invoke();
    }
}

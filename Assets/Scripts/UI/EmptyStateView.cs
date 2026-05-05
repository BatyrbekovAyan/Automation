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

    [Header("Icons (drag in inspector)")]
    [SerializeField] private Sprite iconNoBots;
    [SerializeField] private Sprite iconNoWhatsApp;

    private CanvasGroup canvasGroup;

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
    }

    private void HandleEmptyState(EmptyStateReason reason)
    {
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
                if (iconImage != null) iconImage.sprite = iconNoBots;
                if (titleLabel != null) titleLabel.text = "No bots yet";
                if (bodyLabel != null) bodyLabel.text = "Create your first bot to start managing chats.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Create your first bot";
                if (primaryButton != null)
                {
                    primaryButton.onClick.RemoveAllListeners();
                    primaryButton.onClick.AddListener(OpenCreateBotFlow);
                }
                break;

            case EmptyStateReason.BotHasNoWhatsApp:
                if (iconImage != null) iconImage.sprite = iconNoWhatsApp;
                if (titleLabel != null) titleLabel.text = "WhatsApp not connected";
                if (bodyLabel != null) bodyLabel.text = "Connect WhatsApp to this bot to see its chats.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Connect WhatsApp";
                if (primaryButton != null)
                {
                    primaryButton.onClick.RemoveAllListeners();
                    primaryButton.onClick.AddListener(OpenCurrentBotAuth);
                }
                break;
        }
    }

    private void OpenCreateBotFlow()
    {
        if (BotsPage.Instance != null)
        {
            BotsPage.Instance.gameObject.SetActive(true);
        }
    }

    // Index of the bots tab in BottomTabManager — matches BottomTabManager.defaultTabIndex.
    // Bot settings live under Screen_Bots, so we must switch to it before the
    // SettingsPage activation in Bot.OpenSettings has anything visible to render against.
    private const int BotsTabIndex = 3;

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
            tabManager.SwitchTab(BotsTabIndex);
        }

        // Bot.EditButton is wired to the existing OpenSettings flow (SettingsPage
        // activation + slide-in animation). Invoking it avoids exposing OpenSettings
        // publicly or calling SendMessage by string name.
        if (bot.EditButton != null) bot.EditButton.onClick.Invoke();
    }
}

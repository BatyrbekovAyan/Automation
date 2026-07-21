using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CanvasGroup))]
public class EmptyStateView : MonoBehaviour
{
    [Header("UI references")]
    [SerializeField] private Image iconImage;
    // The Telegram logo shown (UNTINTED) in place of the placeholder icon on the Telegram
    // channel. Stamped into the scene by EmptyStateTelegramIconBuilder; a runtime script
    // can't resolve an asset sprite (no Resources.Load), so it must be a serialized ref.
    [SerializeField] private Sprite telegramIcon;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI bodyLabel;
    [SerializeField] private Button primaryButton;
    [SerializeField] private TextMeshProUGUI primaryButtonLabel;

    // [Header("Icons (drag in inspector)")]
    // [SerializeField] private Sprite iconNoBots;
    // [SerializeField] private Sprite iconNoWhatsApp;

    private CanvasGroup canvasGroup;
    private EmptyStateReason? _lastReason;

    // Authored (WhatsApp) values captured once at Awake so the Telegram theming maps FROM the
    // real authored state (never a hardcoded scene green) and reverts EXACTLY on the WhatsApp
    // channel — the empty state is a persistent widget reused across channel switches. Covered:
    // the connect/create CTA fill; the placeholder icon's sprite + color; and the disc BEHIND
    // the icon (IconCircle, the pale-mint parent the owner sees as "green"). All null-guarded.
    private Image primaryButtonImage;
    private Color primaryButtonAuthoredColor;
    private Color iconAuthoredColor;
    private Sprite iconAuthoredSprite;
    private Image iconCircleImage;
    private Color iconCircleAuthoredColor;
    private bool accentColorsCached;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        CacheAccentColors();
        Hide();
    }

    private void CacheAccentColors()
    {
        if (accentColorsCached) return;
        accentColorsCached = true;
        if (primaryButton != null) primaryButtonImage = primaryButton.GetComponent<Image>();
        if (primaryButtonImage != null) primaryButtonAuthoredColor = primaryButtonImage.color;
        if (iconImage != null)
        {
            iconAuthoredColor = iconImage.color;
            iconAuthoredSprite = iconImage.sprite;
            iconCircleImage = ResolveIconCircle(iconImage);
            if (iconCircleImage != null) iconCircleAuthoredColor = iconCircleImage.color;
        }
    }

    // The disc behind the icon is the icon's nearest ancestor Image (IconCircle in the
    // EmptyState hierarchy). Walk up so a future non-Image wrapper wouldn't break resolution,
    // but STOP before this view's own root so we never recolor the opaque white background.
    private Image ResolveIconCircle(Image icon)
    {
        for (Transform t = icon.transform.parent; t != null && t != transform; t = t.parent)
        {
            Image img = t.GetComponent<Image>();
            if (img != null) return img;
        }
        return null;
    }

    // Theme the empty state for the active channel. Runs at the tail of ConfigureForReason,
    // which fires on every OnEnable/OnEmptyState — including after a channel switch — so the
    // empty state matches the channel that surfaced it (BotHasNoTelegram only on TG, etc.).
    //   • CTA fill  — recolors to Telegram blue on TG, authored green otherwise (05-10).
    //   • Icon      — on TG shows the Telegram logo UNTINTED (its own colors); elsewhere the
    //                 authored placeholder sprite + green tint, byte-identical.
    //   • Icon disc — on TG the pale-mint parent circle turns Telegram blue; else authored.
    private void ApplyChannelAccent()
    {
        CacheAccentColors();
        ChatChannel channel = ChatManager.Instance != null
            ? ChatManager.Instance.ActiveChannel
            : ChatChannel.WhatsApp;

        if (primaryButtonImage != null)
            primaryButtonImage.color = ChannelAccent.Resolve(channel, primaryButtonAuthoredColor);

        if (channel == ChatChannel.Telegram)
        {
            if (iconImage != null)
            {
                if (telegramIcon != null) iconImage.sprite = telegramIcon;
                iconImage.color = Color.white; // untinted → the logo's natural colors
            }
            if (iconCircleImage != null)
                iconCircleImage.color = ChannelAccent.Resolve(ChatChannel.Telegram, iconCircleAuthoredColor);
        }
        else
        {
            if (iconImage != null)
            {
                iconImage.sprite = iconAuthoredSprite;
                iconImage.color = iconAuthoredColor;
            }
            if (iconCircleImage != null)
                iconCircleImage.color = iconCircleAuthoredColor;
        }
    }

    private void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnEmptyState += HandleEmptyState;
            ChatManager.Instance.OnActiveBotChanged += HandleActiveBotChanged;
            ChatManager.Instance.OnChatAdded += HandleChatAdded;
            ChatManager.Instance.OnActiveChannelChanged += HandleActiveChannelChanged;

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
            ChatManager.Instance.OnActiveChannelChanged -= HandleActiveChannelChanged;
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
        // D12-ext (08-REVIEW CR-01): BeginLoadForActiveBot fires BotHasNo{Channel} even with ZERO bots,
        // which would re-wire the CTA to OpenCurrentBotAuth (a silent no-op). Coerce back to NoBots when
        // the authoritative resolver agrees, so «Создать бота» stays wired across channel switches.
        if (reason != EmptyStateReason.NoBotsExist && ChatManager.Instance != null)
            reason = EmptyStateReasonPolicy.Effective(reason, ChatManager.Instance.ComputeCurrentEmptyState());

        // Guard against double-fire during the OnEnable catch-up race: the catch-up call and a real
        // OnEmptyState event can both deliver the same reason in the same frame. Reprocessing is safe
        // but wasteful (re-applies sprites, re-binds button listeners).
        if (_lastReason == reason) return;
        _lastReason = reason;

        ConfigureForReason(reason);
        Show();
    }

    private void HandleActiveBotChanged(string _) => Hide();

    private void HandleChatAdded(ChatViewModel _) => Hide();

    // D12-ext + WR-02: on a channel switch, re-DERIVE the reason from the authoritative resolver rather
    // than replaying a stale _lastReason. The empty state is a persistent widget SHARED by both channels
    // (WhatsApp and Telegram render inside the SAME Screen_Whatsapp) so a switch never fires OnEnable and
    // its catch-up never re-runs ConfigureForReason. Replaying _lastReason kept the previous channel's
    // reason — surfacing on device as a stale wrong-channel connect card (with its raycast block) sitting
    // over the Telegram syncing cover for minutes, and, on zero bots, as an inert «Создать бота».
    //
    // ComputeCurrentEmptyState is channel- & sync-window-aware and NoBots-wins: if the new channel has no
    // empty card (syncing / has chats), HIDE; otherwise re-theme, RE-WIRE, and Show for the reason now in
    // view. WhatsApp is unaffected — a WhatsApp reason re-derives to the identical green card + identical
    // handler (byte-identical outcome).
    private void HandleActiveChannelChanged(ChatChannel _)
    {
        if (!_lastReason.HasValue) return;              // nothing was showing → nothing to re-derive
        EmptyStateReason? reason = ChatManager.Instance != null
            ? ChatManager.Instance.ComputeCurrentEmptyState()
            : _lastReason;                              // manager gone: fall back to the prior reason
        if (!reason.HasValue) { Hide(); return; }       // new channel is syncing / has chats — no card
        _lastReason = reason;
        ConfigureForReason(reason.Value);
        Show();
    }

    private void ConfigureForReason(EmptyStateReason reason)
    {
        switch (reason)
        {
            case EmptyStateReason.NoBotsExist:
                // if (iconImage != null) iconImage.sprite = iconNoBots;
                if (titleLabel != null) titleLabel.text = "Создайте первого бота";
                if (bodyLabel != null) bodyLabel.text = "ИИ-ассистент, который отвечает вашим клиентам в WhatsApp круглосуточно.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Создать бота";
                if (primaryButton != null)
                {
                    primaryButton.onClick.RemoveAllListeners();
                    primaryButton.onClick.AddListener(OpenCreateBotFlow);
                }
                break;

            case EmptyStateReason.BotHasNoWhatsApp:
                // if (iconImage != null) iconImage.sprite = iconNoWhatsApp;
                if (titleLabel != null) titleLabel.text = "WhatsApp не подключён";
                if (bodyLabel != null) bodyLabel.text = "Подключите WhatsApp к этому боту, чтобы видеть его чаты.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Подключить WhatsApp";
                if (primaryButton != null)
                {
                    primaryButton.onClick.RemoveAllListeners();
                    primaryButton.onClick.AddListener(OpenCurrentBotAuth);
                }
                break;

            case EmptyStateReason.BotHasNoTelegram:
                // if (iconImage != null) iconImage.sprite = iconNoWhatsApp;
                if (titleLabel != null) titleLabel.text = "Telegram не подключён";
                if (bodyLabel != null) bodyLabel.text = "Подключите Telegram к этому боту, чтобы видеть его чаты.";
                if (primaryButtonLabel != null) primaryButtonLabel.text = "Подключить Telegram";
                if (primaryButton != null)
                {
                    // Same CTA as WhatsApp — OpenCurrentBotAuth opens BotSettings; no
                    // channel-specific routing is needed this phase (Phase 6 owns that).
                    primaryButton.onClick.RemoveAllListeners();
                    primaryButton.onClick.AddListener(OpenCurrentBotAuth);
                }
                break;
        }

        // All three reasons share the same green CTA/icon accents; recolor them per the
        // active channel after the reason-specific text/wiring is set.
        ApplyChannelAccent();
    }

    private void OpenCreateBotFlow()
    {
        // Reuse the exact add-bot entry the header "+" uses: StartNewBot ensures the
        // Bots tab is active, then opens the Add-Bot overlay directly. Find it
        // include-inactive so it works even if the Bots tab was never opened this
        // session (BotsPage.Instance is set only after Screen_Bots first activates).
        BotsPage botsPage = FindFirstObjectByType<BotsPage>(FindObjectsInactive.Include);
        if (botsPage != null) botsPage.StartNewBot();

        // Defensive guarantee (D12 device re-fail — the CTA read as INERT, "nothing happens"):
        // ensure the Add-Bot overlay actually opens even if the BotsPage path above was compromised
        // at runtime (missing BotsPage, a SwitchTab no-op, or a swallowed exception before Open()).
        // Open() is idempotent, so on the normal path — including every WhatsApp tap, where
        // StartNewBot already opened it — this is a no-op (WhatsApp byte-identical).
        if (AddBotPanel.Instance != null && !AddBotPanel.Instance.IsOpen)
            AddBotPanel.Instance.Open();

        // Preselect the platform for the channel the empty state surfaced on: Telegram (2)
        // from the Telegram channel, WhatsApp (1) otherwise. The empty state is themed for
        // ActiveChannel (ApplyChannelAccent), so the form must open on the SAME platform the
        // owner was viewing — otherwise the Telegram CTA looks dead (opens WhatsApp). WhatsApp
        // still resolves to 1 (byte-identical). StartNewBot switched to the form synchronously,
        // so the page is active and SelectPlatform's UI updates apply.
        int platform = (ChatManager.Instance != null
                        && ChatManager.Instance.ActiveChannel == ChatChannel.Telegram) ? 2 : 1;
        if (Manager.Instance != null) Manager.Instance.SelectPlatform(platform);
    }

    private void OpenCurrentBotAuth()
    {
        if (ChatManager.Instance == null)
        {
            Debug.LogWarning("[D12] OpenCurrentBotAuth aborted: ChatManager.Instance is null.");
            return;
        }
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(ChatManager.Instance.CurrentBotId) : null;
        if (bot == null)
        {
            Debug.LogWarning($"[D12] OpenCurrentBotAuth aborted: no bot for id '{ChatManager.Instance.CurrentBotId}' (connect CTA fired with no bot — see CR-01).");
            return;
        }

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

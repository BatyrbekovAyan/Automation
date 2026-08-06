using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

/// <summary>
/// One bot row inside Sheet_BotSwitcher — the compact А2 layout of the 2026-08
/// restyle (docs/design/ui-restyle/chats-topbar-spec.md §4). Shows the
/// business-tint avatar, the bot name, a subline of channel brand dots +
/// «N чатов[ · M новых]», and a trailing per-bot «Авто» mini-chip that mirrors
/// the header button (hidden for unconnected bots). The selected bot reads as
/// an AccentFill inset ring + left rail — no corner badge.
///
/// The chip reuses the header's flow: enabling routes through
/// <see cref="ReplyModeToggleBinder.RequestEnableAuto"/> (confirm popup —
/// rendered above the sheet), disabling is instant. The row listens to
/// <see cref="ReplyModeToggleBinder.OnReplyModeChanged"/> so a confirm that
/// lands while the sheet is open repaints the right chip.
///
/// All references are wired by ChatsTopBarRestyleBuilder into the saved prefab.
/// </summary>
public class BotSwitcherRowView : MonoBehaviour
{
    [Header("Row")]
    [SerializeField] private Image ringImage;      // root image — AccentFill when selected, clear otherwise
    [SerializeField] private GameObject railObject; // 10u left rail, selected only
    [SerializeField] private Image railImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Button rowButton;

    [Header("Identity")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private Image avatarIcon;
    [SerializeField] private TextMeshProUGUI nameLabel;

    [Header("Subline")]
    [SerializeField] private Image waDot;
    [SerializeField] private Image tgDot;
    [SerializeField] private TextMeshProUGUI subLabel;

    [Header("Auto chip")]
    [SerializeField] private GameObject chipRoot;
    [SerializeField] private Button chipButton;
    [SerializeField] private Image chipRing;
    [SerializeField] private Image chipFill;
    [SerializeField] private Image chipDotRing;
    [SerializeField] private Image chipDotCore;
    [SerializeField] private TextMeshProUGUI chipLabel;

    public CanvasGroup CanvasGroup => canvasGroup;

    private string botId;
    private bool isSelected;
    private bool waConnected;
    private bool tgConnected;
    private bool anyConnected;
    private System.Action<string> onTap;

    public void Bind(Bot bot, bool isSelected, System.Action<string> tapHandler)
    {
        if (bot == null) return;

        botId = bot.transform.name;
        this.isSelected = isSelected;
        onTap = tapHandler;

        if (nameLabel != null)
            nameLabel.text = PlayerPrefs.GetString(botId + "Name", botId);

        if (avatarImage != null) avatarImage.color = bot.GetBusinessIconTint();
        if (avatarIcon != null)
        {
            Sprite sprite = bot.GetBusinessIconSprite();
            avatarIcon.sprite = sprite;
            avatarIcon.enabled = sprite != null;
        }

        waConnected = IsConnected(bot.whatsappProfileId);
        tgConnected = IsConnected(bot.telegramProfileId);
        anyConnected = waConnected || tgConnected;

        ApplySubline();
        ApplySelection();
        ApplyChip();

        if (rowButton != null)
        {
            rowButton.onClick.RemoveAllListeners();
            rowButton.onClick.AddListener(HandleTap);
        }
        if (chipButton != null)
        {
            chipButton.onClick.RemoveAllListeners();
            chipButton.onClick.AddListener(HandleChipTap);
        }
    }

    private void OnEnable()
    {
        ReplyModeToggleBinder.OnReplyModeChanged += HandleModeChanged;
        Theme.Changed += HandleThemeChanged;
    }

    private void OnDisable()
    {
        ReplyModeToggleBinder.OnReplyModeChanged -= HandleModeChanged;
        Theme.Changed -= HandleThemeChanged;
    }

    private static bool IsConnected(string profileId) =>
        !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    // Brand dots: one per CONNECTED channel at full brand color; a bot with no
    // channels shows a single Border-colored dot beside «Не подключён». Counts
    // come from the on-disk caches (both channels) — see BotChatStats.
    private void ApplySubline()
    {
        if (waDot != null)
        {
            bool visible = waConnected || !anyConnected;
            waDot.gameObject.SetActive(visible);
            waDot.color = waConnected ? Theme.Fixed.WhatsAppGreen : Theme.Color(ThemeRole.Border);
        }
        if (tgDot != null)
        {
            tgDot.gameObject.SetActive(tgConnected);
            tgDot.color = Theme.Fixed.TelegramBlue;
        }

        if (subLabel != null)
        {
            BotChatStats.Stats stats = anyConnected ? BotChatStats.Read(botId) : default;
            subLabel.text = BotSwitcherRowModel.Subline(anyConnected, stats.ChatCount, stats.UnreadCount);
        }
    }

    private void ApplySelection()
    {
        if (ringImage != null)
            ringImage.color = isSelected ? Theme.Color(ThemeRole.AccentFill) : Color.clear;
        if (railImage != null)
            railImage.color = Theme.Color(ThemeRole.AccentFill);
        if (railObject != null)
            railObject.SetActive(isSelected);
    }

    // Mirrors ReplyModeToggleBinder.ApplyVisuals — the chip IS the header button,
    // replicated per bot, so the two must speak one visual language.
    private void ApplyChip()
    {
        if (chipRoot == null) return;

        bool visible = BotSwitcherRowModel.AutoChipVisible(waConnected, tgConnected);
        chipRoot.SetActive(visible);
        if (!visible) return;

        bool on = AutoButtonModel.IsAutoOn(ReplyModeToggleBinder.GetMode(botId));

        Color fill = on ? Theme.Color(ThemeRole.PositiveBg) : Theme.Color(ThemeRole.Surface);
        Color ring = on ? Theme.Color(ThemeRole.PositiveBg) : Theme.Color(ThemeRole.Border);
        Color ink = on ? Theme.Color(ThemeRole.PositiveInk) : Theme.Color(ThemeRole.InkSecondary);
        Color lamp = on ? Theme.Color(ThemeRole.PositiveInk) : Theme.Color(ThemeRole.InkTertiary);

        if (chipFill != null) chipFill.color = fill;
        if (chipRing != null) chipRing.color = ring;
        if (chipLabel != null) chipLabel.color = ink;
        if (chipDotRing != null) chipDotRing.color = lamp;
        if (chipDotCore != null) chipDotCore.color = on ? lamp : fill;
    }

    private void HandleModeChanged(string changedBotId, ReplyModeToggleBinder.ReplyMode _)
    {
        if (!string.IsNullOrEmpty(botId) && changedBotId == botId) ApplyChip();
    }

    private void HandleThemeChanged()
    {
        if (string.IsNullOrEmpty(botId)) return;
        ApplySelection();
        ApplyChip();
    }

    private void HandleTap()
    {
        if (string.IsNullOrEmpty(botId)) return;

        transform.DOPunchScale(Vector3.one * 0.04f, 0.18f, 1, 0.5f);
        onTap?.Invoke(botId);
    }

    // Enabling confirms (popup sits above the sheet), disabling is instant —
    // the header button's exact asymmetry, scoped to this row's bot.
    private void HandleChipTap()
    {
        if (string.IsNullOrEmpty(botId) || chipRoot == null) return;

        chipRoot.transform.DOKill();
        chipRoot.transform.localScale = Vector3.one;
        chipRoot.transform.DOPunchScale(Vector3.one * 0.06f, 0.18f, 1, 0.5f);

        var mode = ReplyModeToggleBinder.GetMode(botId);
        if (AutoButtonModel.IsAutoOn(mode))
            ReplyModeToggleBinder.DisableAuto(botId);   // repaint arrives via OnReplyModeChanged
        else
            ReplyModeToggleBinder.RequestEnableAuto(botId);
    }

    private void OnDestroy()
    {
        if (rowButton != null) rowButton.onClick.RemoveAllListeners();
        if (chipButton != null) chipButton.onClick.RemoveAllListeners();
    }
}

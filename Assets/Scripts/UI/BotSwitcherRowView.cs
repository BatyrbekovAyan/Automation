using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

/// <summary>
/// One bot card inside Sheet_BotSwitcher. Shows the business-tint avatar, the
/// bot name, and one connection chip per platform (WhatsApp / Telegram). The
/// active bot gets an accent ring (the row's root image) and a corner badge.
/// All references are wired by BotSwitcherSheetBuilder into the saved prefab.
/// </summary>
public class BotSwitcherRowView : MonoBehaviour
{
    [Header("UI references")]
    [SerializeField] private Image ringImage;
    [SerializeField] private GameObject selectedBadge;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image avatarImage;
    [SerializeField] private Image avatarIcon;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private Image waChipBg;
    [SerializeField] private Image waChipIcon;
    [SerializeField] private TextMeshProUGUI waChipLabel;
    [SerializeField] private Image tgChipBg;
    [SerializeField] private Image tgChipIcon;
    [SerializeField] private TextMeshProUGUI tgChipLabel;
    [SerializeField] private Button rowButton;

    [Header("Style")]
    [SerializeField] private Color accentColor = new Color(0.106f, 0.486f, 0.922f);
    [SerializeField] private Color waConnectedBg = new Color(0.914f, 0.969f, 0.937f);
    [SerializeField] private Color waConnectedLabel = new Color(0.059f, 0.431f, 0.337f);
    [SerializeField] private Color tgConnectedBg = new Color(0.902f, 0.945f, 0.984f);
    [SerializeField] private Color tgConnectedLabel = new Color(0.094f, 0.373f, 0.647f);
    [SerializeField] private Color disconnectedBg = new Color(0.925f, 0.925f, 0.933f);
    [SerializeField] private Color disconnectedLabel = new Color(0.557f, 0.557f, 0.576f);
    [SerializeField, Range(0f, 1f)] private float disconnectedIconAlpha = 0.35f;

    public CanvasGroup CanvasGroup => canvasGroup;

    private string botId;
    private System.Action<string> onTap;

    public void Bind(Bot bot, bool isSelected, System.Action<string> tapHandler)
    {
        if (bot == null) return;

        botId = bot.transform.name;
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

        ApplyChip(waChipBg, waChipIcon, waChipLabel,
            IsConnected(bot.whatsappProfileId), waConnectedBg, waConnectedLabel);
        ApplyChip(tgChipBg, tgChipIcon, tgChipLabel,
            IsConnected(bot.telegramProfileId), tgConnectedBg, tgConnectedLabel);

        if (ringImage != null) ringImage.color = isSelected ? accentColor : Color.clear;
        if (selectedBadge != null) selectedBadge.SetActive(isSelected);

        if (rowButton != null)
        {
            rowButton.onClick.RemoveAllListeners();
            rowButton.onClick.AddListener(HandleTap);
        }
    }

    private static bool IsConnected(string profileId) =>
        !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    /// <summary>
    /// Brand logos are full-color sprites, so the disconnected state fades
    /// alpha instead of tinting — multiplying a colored logo by gray goes muddy.
    /// </summary>
    private void ApplyChip(Image bg, Image icon, TextMeshProUGUI label,
        bool connected, Color onBg, Color onLabel)
    {
        if (bg != null) bg.color = connected ? onBg : disconnectedBg;
        if (label != null) label.color = connected ? onLabel : disconnectedLabel;
        if (icon != null) icon.color = new Color(1f, 1f, 1f, connected ? 1f : disconnectedIconAlpha);
    }

    private void HandleTap()
    {
        if (string.IsNullOrEmpty(botId)) return;

        transform.DOPunchScale(Vector3.one * 0.04f, 0.18f, 1, 0.5f);
        onTap?.Invoke(botId);
    }

    private void OnDestroy()
    {
        if (rowButton != null) rowButton.onClick.RemoveAllListeners();
    }
}

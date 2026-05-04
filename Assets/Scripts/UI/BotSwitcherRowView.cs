using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

public class BotSwitcherRowView : MonoBehaviour
{
    [Header("UI references")]
    [SerializeField] private Image avatarImage;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI subLineLabel;
    [SerializeField] private Image statusDot;
    [SerializeField] private Image selectedBackground;
    [SerializeField] private Image selectedAccentBar;
    [SerializeField] private Button rowButton;

    [Header("Style")]
    [SerializeField] private Color statusConnectedColor = new Color(0.13f, 0.78f, 0.42f);
    [SerializeField] private Color statusDisconnectedColor = new Color(0.6f, 0.6f, 0.6f);
    [SerializeField] private Sprite avatarFallback;

    private string botId;
    private System.Action<string> onTap;

    public void Bind(Bot bot, bool isSelected, System.Action<string> tapHandler)
    {
        if (bot == null) return;

        botId = bot.transform.name;
        onTap = tapHandler;

        string botDisplayName = PlayerPrefs.GetString(botId + "Name", botId);
        if (nameLabel != null)
        {
            nameLabel.text = botDisplayName;
            nameLabel.fontStyle = isSelected ? FontStyles.Bold : FontStyles.Normal;
        }

        bool waConnected = !string.IsNullOrEmpty(bot.whatsappProfileId)
                           && bot.whatsappProfileId != Bot.UnauthedProfileSentinel;
        if (subLineLabel != null)
        {
            subLineLabel.text = waConnected ? "WhatsApp connected" : "WhatsApp not connected";
        }
        if (statusDot != null)
        {
            statusDot.color = waConnected ? statusConnectedColor : statusDisconnectedColor;
        }

        // Reset to fallback unconditionally so a rebind doesn't keep the prior bot's avatar.
        // A future avatar fetcher will overwrite this once a real sprite resolves.
        if (avatarImage != null)
        {
            avatarImage.sprite = avatarFallback;
        }

        if (selectedBackground != null) selectedBackground.gameObject.SetActive(isSelected);
        if (selectedAccentBar != null) selectedAccentBar.gameObject.SetActive(isSelected);

        if (rowButton != null)
        {
            rowButton.onClick.RemoveAllListeners();
            rowButton.onClick.AddListener(HandleTap);
        }
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

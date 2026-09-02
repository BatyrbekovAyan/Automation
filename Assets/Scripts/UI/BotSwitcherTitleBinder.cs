using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class BotSwitcherTitleBinder : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private Image avatarImage;
    [SerializeField] private Image avatarIcon;

    private Button rowButton;

    private void Awake()
    {
        if (nameLabel == null)
        {
            Transform t = transform.Find("BotName");
            if (t != null) nameLabel = t.GetComponent<TextMeshProUGUI>();
        }

        rowButton = GetComponent<Button>();
        if (rowButton != null)
        {
            BotSwitcherSheet sheet = FindFirstObjectByType<BotSwitcherSheet>(FindObjectsInactive.Include);
            if (sheet != null)
            {
                rowButton.onClick.RemoveAllListeners();
                rowButton.onClick.AddListener(sheet.Open);
            }
        }
    }

    private void OnEnable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnActiveBotChanged += UpdateTitle;
            UpdateTitle(ChatManager.Instance.CurrentBotId);
        }
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnActiveBotChanged -= UpdateTitle;
        }
    }

    private void UpdateTitle(string botId)
    {
        Bot bot = !string.IsNullOrEmpty(botId) && Manager.Instance != null
            ? Manager.Instance.FindBotByName(botId)
            : null;

        if (nameLabel != null)
        {
            // The scene shipped at Truncate although every builder that authors this label
            // (BotSwitcherTitleNameClamper, Screen_WhatsappHeaderRebuilder) writes Ellipsis, so
            // «Авто-Деталь KZ» was cut to «Авто-Дет» and read as a typo (store screenshot
            // review, 2026-09-02). Asserted on every bind so scene/builder drift cannot recur.
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;
            nameLabel.text = bot != null ? PlayerPrefs.GetString(botId + "Name", botId) : "Bot";
        }

        ApplyAvatar(bot);
    }

    private void ApplyAvatar(Bot bot)
    {
        if (avatarImage != null)
            avatarImage.color = bot != null ? bot.GetBusinessIconTint() : Bot.NeutralTile;

        if (avatarIcon != null)
        {
            Sprite sprite = bot != null ? bot.GetBusinessIconSprite() : null;
            avatarIcon.sprite = sprite;
            avatarIcon.enabled = sprite != null;
        }
    }
}

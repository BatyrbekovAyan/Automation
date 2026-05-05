using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class BotSwitcherTitleBinder : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameLabel;

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
        if (nameLabel == null) return;
        if (string.IsNullOrEmpty(botId)) { nameLabel.text = "Bot"; return; }
        nameLabel.text = PlayerPrefs.GetString(botId + "Name", botId);
    }
}

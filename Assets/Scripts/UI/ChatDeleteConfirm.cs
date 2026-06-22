using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Modal confirm for deleting a chat. Reuses PopupUI's show/hide animation. Cancel hides;
/// Delete calls ChatManager.DeleteChat for the pending chat. Wired from ChatListView.RequestDelete.
/// </summary>
public class ChatDeleteConfirm : MonoBehaviour
{
    [SerializeField] private GameObject panel;       // backdrop Image + "Content" card child
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button deleteButton;

    private string _pendingChatId;

    private void Awake()
    {
        if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        if (deleteButton != null) deleteButton.onClick.AddListener(Confirm);
        if (panel != null) panel.SetActive(false);
    }

    public void Ask(string chatId, string chatTitle)
    {
        if (string.IsNullOrEmpty(chatId)) return;
        _pendingChatId = chatId;
        if (bodyText != null)
            bodyText.text = string.IsNullOrEmpty(chatTitle)
                ? "This chat will be permanently deleted."
                : $"\"{chatTitle}\" will be permanently deleted.";
        PopupUI.Show(panel);
    }

    private void Cancel()
    {
        _pendingChatId = null;
        PopupUI.Hide(panel);
    }

    private void Confirm()
    {
        string id = _pendingChatId;
        _pendingChatId = null;
        PopupUI.Hide(panel);
        if (!string.IsNullOrEmpty(id)) ChatManager.Instance?.DeleteChat(id);
    }
}

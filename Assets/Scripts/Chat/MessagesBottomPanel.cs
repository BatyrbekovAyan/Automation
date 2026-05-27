using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using UnityEngine.EventSystems;

public class MessagesBottomPanel : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField inputField;
    public Button sendButton;
    public Button micButton;
    public Button attachButton;
    [SerializeField] private AttachSheet attachSheet;

    [Header("Quick Replies")]
    public QuickReplyPanel quickReplyPanel;

    public static event Action<string> OnMessageSendRequested;

    void OnEnable()
    {
        inputField.text = "";
        UpdateButtonState("");

        inputField.onValueChanged.AddListener(UpdateButtonState);
        attachButton.onClick.AddListener(OnAttachClicked);

        // Send button uses raw PointerDown for responsiveness.
        sendButton.onClick.RemoveAllListeners();
        EventTrigger trigger = sendButton.gameObject.GetComponent<EventTrigger>();
        if (trigger == null) trigger = sendButton.gameObject.AddComponent<EventTrigger>();
        trigger.triggers.Clear();
        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = EventTriggerType.PointerDown;
        entry.callback.AddListener((data) => { OnSendClicked(); });
        trigger.triggers.Add(entry);
    }

    void OnDisable()
    {
        inputField.onValueChanged.RemoveListener(UpdateButtonState);
        attachButton.onClick.RemoveListener(OnAttachClicked);

        EventTrigger trigger = sendButton.gameObject.GetComponent<EventTrigger>();
        if (trigger != null) trigger.triggers.Clear();
    }

    private void UpdateButtonState(string currentText)
    {
        bool hasText = !string.IsNullOrWhiteSpace(currentText);
        sendButton.gameObject.SetActive(hasText);
        micButton.gameObject.SetActive(!hasText);
    }

    private void OnSendClicked()
    {
        string messageToDelivery = inputField.text.Trim();
        if (string.IsNullOrWhiteSpace(messageToDelivery)) return;

        if (ChatManager.Instance != null)
            ChatManager.Instance.SendTextMessage(messageToDelivery);

        OnMessageSendRequested?.Invoke(messageToDelivery);

        inputField.text = "";

        // Force re-focus after send so the keyboard doesn't dismiss between messages.
        StopAllCoroutines();
        StartCoroutine(KeepKeyboardOpenRoutine());
    }

    private System.Collections.IEnumerator KeepKeyboardOpenRoutine()
    {
        // Wait for Unity to finish processing the touch + any internal Deselect
        // events it might be trying to fire, then re-establish focus.
        yield return new WaitForEndOfFrame();
        EventSystem.current.SetSelectedGameObject(inputField.gameObject);
        inputField.ActivateInputField();
    }

    private void OnAttachClicked()
    {
        if (attachSheet != null)
            attachSheet.Toggle();
        else
            Debug.LogWarning("[MessagesBottomPanel] attachSheet ref is null — open Tools menu and run Build Attach Sheet");
    }
}

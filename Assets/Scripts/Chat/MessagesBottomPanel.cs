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

        // Prevent buttons from stealing EventSystem selection on PointerDown.
        // Pairs with DeferredDismissInputField: without Navigation.Mode.None,
        // tapping a button would deselect the focused input field on Down,
        // which then defers; with Mode.None the input field never receives
        // OnDeselect in the first place and the EventSystem state stays clean.
        SetNavigationNone(attachButton);
        SetNavigationNone(micButton);
        SetNavigationNone(sendButton);

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

    private static void SetNavigationNone(Button button)
    {
        if (button == null) return;
        var nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
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

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
    [SerializeField] private Image attachButtonIcon;
    [SerializeField] private Sprite plusIconSprite;
    [SerializeField] private Sprite keyboardIconSprite;
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

        // Prevent the attach button from stealing EventSystem selection on tap.
        // Without this, tapping + would deselect the focused input field, which
        // on iOS triggers the OS keyboard's resignFirstResponder slide-down
        // animation — exactly what we're trying to avoid.
        var attachNav = attachButton.navigation;
        attachNav.mode = UnityEngine.UI.Navigation.Mode.None;
        attachButton.navigation = attachNav;

        // Remove standard onClick
        sendButton.onClick.RemoveAllListeners();
        
        // Catch the raw PointerDown event
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
        {
            ChatManager.Instance.SendTextMessage(messageToDelivery);
        }

        OnMessageSendRequested?.Invoke(messageToDelivery);

        inputField.text = "";
        
        // Stop any previous focus routines and start a fresh one
        StopAllCoroutines();
        StartCoroutine(KeepKeyboardOpenRoutine());
    }

    // --- THE BULLETPROOF KEYBOARD COROUTINE ---
    private System.Collections.IEnumerator KeepKeyboardOpenRoutine()
    {
        // 1. Wait for Unity to completely finish processing the physical touch 
        // and any internal "Deselect" events it might be trying to fire.
        yield return new WaitForEndOfFrame();
        
        // 2. Now that the frame is over, force the EventSystem to lock onto the InputField!
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

    public void ShowKeyboardIcon()
    {
        if (attachButtonIcon != null && keyboardIconSprite != null)
            attachButtonIcon.sprite = keyboardIconSprite;
    }

    public void ShowPlusIcon()
    {
        if (attachButtonIcon != null && plusIconSprite != null)
            attachButtonIcon.sprite = plusIconSprite;
    }
}
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;
using UnityEngine.EventSystems;
using DG.Tweening;

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

    [Header("Reply Preview")]
    [SerializeField] private GameObject replyPreviewBar;
    [SerializeField] private TextMeshProUGUI replyPreviewSender;
    [SerializeField] private TextMeshProUGUI replyPreviewSnippet;
    [SerializeField] private Button replyPreviewCancel;

    private RectTransform _previewRt;
    private float _previewRestY;
    private float _previewHiddenY;
    private bool _previewMetricsReady;
    private Tween _previewTween;

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

        if (ChatManager.Instance != null)
            ChatManager.Instance.OnReplyTargetChanged += HandleReplyTargetChanged;
        if (replyPreviewCancel != null)
        {
            replyPreviewCancel.onClick.RemoveAllListeners();
            replyPreviewCancel.onClick.AddListener(() => ChatManager.Instance?.CancelReply());
        }
        if (replyPreviewBar != null)
        {
            // Capture the bar's built resting Y once (before any slide moves it). Hidden = one
            // bar-height lower, where the opaque bottom panel (rendered in front) occludes it.
            if (!_previewMetricsReady)
            {
                _previewRt = (RectTransform)replyPreviewBar.transform;
                _previewRestY = _previewRt.anchoredPosition.y;
                _previewHiddenY = _previewRestY - _previewRt.rect.height;
                _previewMetricsReady = true;
            }
            replyPreviewBar.SetActive(false);
        }
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

        if (ChatManager.Instance != null)
            ChatManager.Instance.OnReplyTargetChanged -= HandleReplyTargetChanged;

        _previewTween?.Kill();
        _previewTween = null;
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

    private void HandleReplyTargetChanged(MessageViewModel target)
    {
        if (replyPreviewBar == null) return;
        if (target == null) { HidePreview(); return; }

        if (replyPreviewSender != null)
            replyPreviewSender.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(
                ReplyParser.SenderLabel(target.isIncoming, target.senderName), MissingEmojiMode.Hide);
        if (replyPreviewSnippet != null)
            replyPreviewSnippet.text = ReplyParser.CleanSnippet(
                UnicodeEmojiConverter.ConvertRealEmojisToSprites(
                    ReplyParser.SnippetFor(target.type, target.text), MissingEmojiMode.Hide));

        ShowPreview();
        if (inputField != null) inputField.ActivateInputField();   // open keyboard on reply
    }

    // The bar is a sibling rendered BEHIND the opaque bottom panel, so animating it up from one
    // bar-height below its resting Y makes it emerge from under the panel; reverse on close.
    private void ShowPreview()
    {
        _previewTween?.Kill();
        replyPreviewBar.SetActive(true);
        if (_previewRt == null) return;
        _previewRt.anchoredPosition = new Vector2(_previewRt.anchoredPosition.x, _previewHiddenY);
        _previewTween = _previewRt.DOAnchorPosY(_previewRestY, 0.25f).SetEase(Ease.OutCubic);
    }

    private void HidePreview()
    {
        if (!replyPreviewBar.activeSelf) return;
        _previewTween?.Kill();
        if (_previewRt == null) { replyPreviewBar.SetActive(false); return; }
        _previewTween = _previewRt.DOAnchorPosY(_previewHiddenY, 0.2f).SetEase(Ease.InCubic)
            .OnComplete(() => replyPreviewBar.SetActive(false));
    }
}

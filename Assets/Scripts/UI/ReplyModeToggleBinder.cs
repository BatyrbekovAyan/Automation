using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the chats-list header's reply-mode toggle — a sliding-knob switch
/// between Automation ("Авто") and Semi-auto ("Полу"). The mode is a per-bot
/// default persisted in PlayerPrefs under "&lt;botName&gt;ReplyMode"
/// (0 = Авто, 1 = Полу; default Авто). Авто sits on the RIGHT, Полу on the
/// LEFT: the white thumb covers the active word and slides across on switch
/// while the track recolours green↔amber.
///
/// Switching EITHER direction asks for confirmation via the shared
/// <see cref="PopupUI"/>; on confirm it commits optimistically, persists, and
/// raises <see cref="OnReplyModeChanged"/> so the autonomous-vs-suggestions
/// backend (semi-auto phase) can react when it lands.
///
/// Built and wired by Assets/Editor/ReplyModeToggleBuilder.cs.
/// </summary>
[RequireComponent(typeof(Button))]
public class ReplyModeToggleBinder : MonoBehaviour
{
    public enum ReplyMode { Auto = 0, Semi = 1 }

    [Header("Toggle")]
    [SerializeField] private Image trackImage;
    [SerializeField] private Button toggleButton;
    [SerializeField] private RectTransform thumb;
    [SerializeField] private TextMeshProUGUI thumbLabel;
    [SerializeField] private TextMeshProUGUI faintAvto;
    [SerializeField] private TextMeshProUGUI faintPolu;

    [Header("Confirm popup")]
    [SerializeField] private GameObject confirmPopup;
    [SerializeField] private TextMeshProUGUI confirmTitle;
    [SerializeField] private TextMeshProUGUI confirmBody;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    /// <summary>Fires after a bot's reply mode is committed: (botName, mode).</summary>
    public static event Action<string, ReplyMode> OnReplyModeChanged;

    private const string KeySuffix = "ReplyMode";
    private const float AnimDuration = 0.22f;
    private const float ThumbXAuto = 70f;   // right half — Авто lives on the right
    private const float ThumbXSemi = -70f;  // left half  — Вместе lives on the left

    private static readonly Color TrackAuto = Hex("#2FB344");
    private static readonly Color TrackSemi = Hex("#007AFF");
    private static readonly Color InkAuto = Hex("#206A2C");
    private static readonly Color InkSemi = Hex("#004C99");
    private static readonly Color FaintOnAuto = Hex("#C3EFCB");
    private static readonly Color FaintOnSemi = Hex("#A8CFFF");

    private string currentBotId;
    private ReplyMode currentMode = ReplyMode.Auto;
    private ReplyMode pendingMode = ReplyMode.Auto;
    private bool popupWired;

    /// <summary>Reads a bot's persisted reply mode (defaults to Авто).</summary>
    public static ReplyMode GetMode(string botName) =>
        (ReplyMode)PlayerPrefs.GetInt(botName + KeySuffix, (int)ReplyMode.Auto);

    private void Awake()
    {
        if (toggleButton == null) toggleButton = GetComponent<Button>();
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveAllListeners();
            toggleButton.onClick.AddListener(OnTogglePressed);
        }
        WirePopupButtons();
    }

    private void OnEnable()
    {
        if (ChatManager.Instance == null) return;
        ChatManager.Instance.OnActiveBotChanged += Refresh;
        Refresh(ChatManager.Instance.CurrentBotId);
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
            ChatManager.Instance.OnActiveBotChanged -= Refresh;

        if (thumb != null) thumb.DOKill();
        if (trackImage != null) trackImage.DOKill();
        transform.DOKill();
        transform.localScale = Vector3.one;
    }

    // Wired once: the popup buttons live on an inactive GameObject, so this is
    // safe to do in Awake before the popup is ever shown.
    private void WirePopupButtons()
    {
        if (popupWired) return;
        if (confirmButton != null) PopupUI.WireFingerUp(confirmButton, OnConfirm);
        if (cancelButton != null) PopupUI.WireFingerUp(cancelButton, OnCancel);
        popupWired = true;
    }

    private void Refresh(string botId)
    {
        currentBotId = botId;
        SetMode(GetMode(botId), animate: false, persist: false);
    }

    private void OnTogglePressed()
    {
        pendingMode = currentMode == ReplyMode.Auto ? ReplyMode.Semi : ReplyMode.Auto;

        transform.DOKill();
        transform.localScale = Vector3.one;
        transform.DOPunchScale(Vector3.one * -0.04f, 0.18f, 1, 0.5f);

        // No confirm dialog wired — commit straight away.
        if (confirmPopup == null)
        {
            SetMode(pendingMode, animate: true, persist: true);
            return;
        }

        bool toSemi = pendingMode == ReplyMode.Semi;
        if (confirmTitle != null) confirmTitle.text = "Сменить режим?";
        if (confirmBody != null)
            confirmBody.text = toSemi
                ? "Бот перестанет отвечать сам — он будет предлагать варианты ответа, а вы выберете."
                : "Бот снова будет отвечать клиентам автоматически.";

        PopupUI.Show(confirmPopup);
    }

    private void OnConfirm()
    {
        if (confirmPopup != null) PopupUI.Hide(confirmPopup);
        SetMode(pendingMode, animate: true, persist: true);
    }

    private void OnCancel()
    {
        if (confirmPopup != null) PopupUI.Hide(confirmPopup);
    }

    private void SetMode(ReplyMode mode, bool animate, bool persist)
    {
        currentMode = mode;
        ApplyVisuals(mode, animate);

        if (!persist || string.IsNullOrEmpty(currentBotId)) return;
        PlayerPrefs.SetInt(currentBotId + KeySuffix, (int)mode);
        PlayerPrefs.Save();
        OnReplyModeChanged?.Invoke(currentBotId, mode);
    }

    private void ApplyVisuals(ReplyMode mode, bool animate)
    {
        bool isAuto = mode == ReplyMode.Auto;
        Color track = isAuto ? TrackAuto : TrackSemi;
        float thumbX = isAuto ? ThumbXAuto : ThumbXSemi;
        Color faint = isAuto ? FaintOnAuto : FaintOnSemi;

        if (thumbLabel != null)
        {
            thumbLabel.text = isAuto ? "Авто" : "Вместе";
            thumbLabel.color = isAuto ? InkAuto : InkSemi;
        }
        if (faintAvto != null) faintAvto.color = faint;
        if (faintPolu != null) faintPolu.color = faint;

        if (thumb != null) thumb.DOKill();
        if (trackImage != null) trackImage.DOKill();

        if (animate)
        {
            if (thumb != null)
                thumb.DOAnchorPosX(thumbX, AnimDuration).SetEase(Ease.OutCubic);
            if (trackImage != null)
                trackImage.DOColor(track, AnimDuration).SetEase(Ease.OutCubic);
            return;
        }

        if (thumb != null)
        {
            Vector2 p = thumb.anchoredPosition;
            p.x = thumbX;
            thumb.anchoredPosition = p;
        }
        if (trackImage != null) trackImage.color = track;
    }

    private static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}

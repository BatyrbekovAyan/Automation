using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the chats-header «Авто» button — the autopilot switch that replaced
/// the two-word Авто/Вместе sliding toggle in the 2026-08 top-bar restyle
/// (docs/design/ui-restyle/chats-topbar-spec.md). Semi-auto is the silent
/// default state and never appears as a word on this screen; the pill reads
/// «Авто» with a state lamp — filled dot on PositiveBg when the bot answers
/// clients itself, hollow dot in an outlined pill when it only proposes.
///
/// The mode stays a per-bot default persisted under "&lt;botName&gt;ReplyMode"
/// (0 = auto ON, 1 = semi; unset reads SEMI — <see cref="AutoButtonModel.DefaultMode"/>,
/// the owner-approved default flip). ENABLING routes through the confirm popup
/// (the bot starts messaging real clients); DISABLING commits instantly — the
/// deliberate asymmetry pinned by AutoButtonModelTests. The popup targets an
/// explicit bot, so Sheet_BotSwitcher's per-row chips reuse it via
/// <see cref="RequestEnableAuto"/> / <see cref="DisableAuto"/>.
///
/// CLASS NAME UNCHANGED on purpose: the scene serialises the component by
/// class, and SemiAutoStore / Manager.ReplyModeSync consume <see cref="GetMode"/>.
/// Built and wired by Assets/Editor/ChatsTopBarRestyleBuilder.cs.
/// </summary>
[RequireComponent(typeof(Button))]
public class ReplyModeToggleBinder : MonoBehaviour
{
    public enum ReplyMode { Auto = 0, Semi = 1 }

    [Header("Auto button")]
    [SerializeField] private Button toggleButton;
    [SerializeField] private Image ringImage;   // outer rounded rect: Border OFF / PositiveBg ON
    [SerializeField] private Image fillImage;   // inner rounded rect: Surface OFF / PositiveBg ON
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Image dotRing;     // 18u state lamp: InkTertiary hollow OFF / PositiveInk ON
    [SerializeField] private Image dotCore;     // the hollow's hole: matches fill OFF / PositiveInk ON

    [Header("Confirm popup (enable only)")]
    [SerializeField] private GameObject confirmPopup;
    [SerializeField] private TextMeshProUGUI confirmTitle;
    [SerializeField] private TextMeshProUGUI confirmBody;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    /// <summary>Fires after a bot's reply mode is committed: (botName, mode).</summary>
    public static event Action<string, ReplyMode> OnReplyModeChanged;

    private const string KeySuffix = "ReplyMode";
    private const float AnimDuration = 0.22f;
    private const string EnableTitle = "Включить авто-режим?";
    private const string EnableBody =
        "Бот будет отвечать клиентам сам. Выключить можно в любой момент — этой же кнопкой.";

    private static ReplyModeToggleBinder instance;

    private string currentBotId;
    private ReplyMode currentMode = AutoButtonModel.DefaultMode;
    private string pendingBotId;   // popup target — may be a non-active bot (sheet chip)
    private bool popupWired;

    /// <summary>Reads a bot's persisted reply mode (unset ⇒ semi-auto, the silent default).</summary>
    public static ReplyMode GetMode(string botName) =>
        (ReplyMode)PlayerPrefs.GetInt(botName + KeySuffix, (int)AutoButtonModel.DefaultMode);

    /// <summary>
    /// Persist + notify, no questions asked. The header button repaints itself
    /// when the committed bot is the one it currently shows.
    /// </summary>
    public static void CommitMode(string botName, ReplyMode mode)
    {
        if (string.IsNullOrEmpty(botName)) return;

        PlayerPrefs.SetInt(botName + KeySuffix, (int)mode);
        PlayerPrefs.Save();
        OnReplyModeChanged?.Invoke(botName, mode);

        if (instance != null && botName == instance.currentBotId)
            instance.SetVisualMode(mode, animate: true);
    }

    /// <summary>Instant OFF — the safe direction never confirms (spec asymmetry).</summary>
    public static void DisableAuto(string botName) => CommitMode(botName, ReplyMode.Semi);

    /// <summary>
    /// Route an enable request through the confirm popup (falls back to a direct
    /// commit when no popup is wired, e.g. before the builder ran).
    /// </summary>
    public static void RequestEnableAuto(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return;

        if (instance != null && instance.confirmPopup != null)
            instance.ShowEnableConfirm(botName);
        else
            CommitMode(botName, ReplyMode.Auto);
    }

    private void Awake()
    {
        instance = this;

        if (toggleButton == null) toggleButton = GetComponent<Button>();
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveAllListeners();
            toggleButton.onClick.AddListener(OnButtonPressed);
        }
        WirePopupButtons();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnEnable()
    {
        Theme.Changed += RepaintForTheme;

        if (ChatManager.Instance == null) return;
        ChatManager.Instance.OnActiveBotChanged += Refresh;
        Refresh(ChatManager.Instance.CurrentBotId);
    }

    private void OnDisable()
    {
        Theme.Changed -= RepaintForTheme;

        if (ChatManager.Instance != null)
            ChatManager.Instance.OnActiveBotChanged -= Refresh;

        KillColorTweens();
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
        SetVisualMode(GetMode(botId), animate: false);
    }

    private void RepaintForTheme() => ApplyVisuals(currentMode, animate: false);

    private void OnButtonPressed()
    {
        transform.DOKill();
        transform.localScale = Vector3.one;
        transform.DOPunchScale(Vector3.one * -0.04f, 0.18f, 1, 0.5f);

        if (string.IsNullOrEmpty(currentBotId)) return;

        if (AutoButtonModel.ConfirmRequired(currentMode))
            RequestEnableAuto(currentBotId);
        else
            DisableAuto(currentBotId);   // instant — CommitMode repaints us
    }

    private void ShowEnableConfirm(string botName)
    {
        pendingBotId = botName;
        if (confirmTitle != null) confirmTitle.text = EnableTitle;
        if (confirmBody != null) confirmBody.text = EnableBody;
        PopupUI.Show(confirmPopup);
    }

    private void OnConfirm()
    {
        if (confirmPopup != null) PopupUI.Hide(confirmPopup);
        if (string.IsNullOrEmpty(pendingBotId)) return;

        CommitMode(pendingBotId, ReplyMode.Auto);
        pendingBotId = null;
    }

    private void OnCancel()
    {
        if (confirmPopup != null) PopupUI.Hide(confirmPopup);
        pendingBotId = null;
    }

    private void SetVisualMode(ReplyMode mode, bool animate)
    {
        currentMode = mode;
        ApplyVisuals(mode, animate);
    }

    private void ApplyVisuals(ReplyMode mode, bool animate)
    {
        bool on = AutoButtonModel.IsAutoOn(mode);

        Color fill = on ? Theme.Color(ThemeRole.PositiveBg) : Theme.Color(ThemeRole.Surface);
        Color ring = on ? Theme.Color(ThemeRole.PositiveBg) : Theme.Color(ThemeRole.Border);
        Color ink = on ? Theme.Color(ThemeRole.PositiveInk) : Theme.Color(ThemeRole.InkSecondary);
        Color lamp = on ? Theme.Color(ThemeRole.PositiveInk) : Theme.Color(ThemeRole.InkTertiary);
        Color lampCore = on ? Theme.Color(ThemeRole.PositiveInk) : fill;

        KillColorTweens();

        if (!animate)
        {
            if (fillImage != null) fillImage.color = fill;
            if (ringImage != null) ringImage.color = ring;
            if (label != null) label.color = ink;
            if (dotRing != null) dotRing.color = lamp;
            if (dotCore != null) dotCore.color = lampCore;
            return;
        }

        if (fillImage != null) fillImage.DOColor(fill, AnimDuration).SetEase(Ease.OutCubic);
        if (ringImage != null) ringImage.DOColor(ring, AnimDuration).SetEase(Ease.OutCubic);
        if (label != null) label.DOColor(ink, AnimDuration).SetEase(Ease.OutCubic);
        if (dotRing != null) dotRing.DOColor(lamp, AnimDuration).SetEase(Ease.OutCubic);
        if (dotCore != null) dotCore.DOColor(lampCore, AnimDuration).SetEase(Ease.OutCubic);
    }

    private void KillColorTweens()
    {
        if (fillImage != null) fillImage.DOKill();
        if (ringImage != null) ringImage.DOKill();
        if (label != null) label.DOKill();
        if (dotRing != null) dotRing.DOKill();
        if (dotCore != null) dotCore.DOKill();
    }
}

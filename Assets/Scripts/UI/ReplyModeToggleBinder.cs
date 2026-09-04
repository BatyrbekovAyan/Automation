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
    private Action pendingConfirm;   // armed by ShowConfirm, fired by the popup's confirm button
    private bool popupWired;
    private ConfirmCardFitter.Baseline confirmBaseline;   // authored card geometry, captured once

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

        if (!ShowConfirm(EnableTitle, EnableBody, () => CommitMode(botName, ReplyMode.Auto)))
            CommitMode(botName, ReplyMode.Auto);
    }

    /// <summary>
    /// Show the shared confirm popup with caller-supplied copy; <paramref name="onConfirm"/>
    /// runs only when the user confirms. Returns false when no popup is wired —
    /// callers then commit directly. Also serves SemiAutoToggle's per-chat gate.
    ///
    /// The card is fitted to the copy AFTER PopupUI.Show, never before: the
    /// popup is inactive between shows, and TMP cannot measure text on a
    /// GameObject that has never been active (see ConfirmCardFitter). Show
    /// activates it, so the fit lands in the same frame and long before the
    /// first render — which is what lets the per-chat title wrap to two lines
    /// and push the body down instead of drawing over it.
    /// </summary>
    public static bool ShowConfirm(string title, string body, Action onConfirm)
    {
        if (instance == null || instance.confirmPopup == null) return false;

        instance.pendingConfirm = onConfirm;
        if (instance.confirmTitle != null) instance.confirmTitle.text = title;
        if (instance.confirmBody != null) instance.confirmBody.text = body;
        PopupUI.Show(instance.confirmPopup);
        instance.FitConfirmCard();
        return true;
    }

    /// <summary>
    /// Resize the confirm card to whatever copy it is currently showing. The
    /// baseline was snapshotted in WirePopupButtons, so every solve starts from
    /// the geometry the scene authored rather than from the previous result.
    /// </summary>
    private void FitConfirmCard()
    {
        if (confirmPopup == null || confirmTitle == null || confirmBody == null) return;

        var card = PopupUI.FindCard(confirmPopup.transform) as RectTransform;
        ConfirmCardFitter.Fit(card, confirmTitle, confirmBody, ref confirmBaseline);
    }

    /// <summary>
    /// The single source of the «Авто» chip's state colors — used by the header
    /// button, the sheet's per-bot chips, and the per-chat SemiAutoToggle so the
    /// three controls can never drift apart. ON = PositiveBg pill + filled lamp;
    /// OFF = Border ring on Surface + hollow lamp. Every ref is null-guarded.
    /// </summary>
    public static void PaintChip(bool on, Image ring, Image fill, TextMeshProUGUI chipLabel,
        Image lampRing, Image lampCore, bool animate, float duration = AnimDuration)
    {
        Color fillColor = on ? Theme.Color(ThemeRole.PositiveBg) : Theme.Color(ThemeRole.Surface);
        Color ringColor = on ? Theme.Color(ThemeRole.PositiveBg) : Theme.Color(ThemeRole.Border);
        Color inkColor = on ? Theme.Color(ThemeRole.PositiveInk) : Theme.Color(ThemeRole.InkSecondary);
        Color lampColor = on ? Theme.Color(ThemeRole.PositiveInk) : Theme.Color(ThemeRole.InkTertiary);
        Color lampCoreColor = on ? lampColor : fillColor;

        if (!animate)
        {
            if (fill != null) fill.color = fillColor;
            if (ring != null) ring.color = ringColor;
            if (chipLabel != null) chipLabel.color = inkColor;
            if (lampRing != null) lampRing.color = lampColor;
            if (lampCore != null) lampCore.color = lampCoreColor;
            return;
        }

        if (fill != null) fill.DOColor(fillColor, duration).SetEase(Ease.OutCubic);
        if (ring != null) ring.DOColor(ringColor, duration).SetEase(Ease.OutCubic);
        if (chipLabel != null) chipLabel.DOColor(inkColor, duration).SetEase(Ease.OutCubic);
        if (lampRing != null) lampRing.DOColor(lampColor, duration).SetEase(Ease.OutCubic);
        if (lampCore != null) lampCore.DOColor(lampCoreColor, duration).SetEase(Ease.OutCubic);
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

        // Snapshot the card as the scene authored it, while it is guaranteed
        // untouched — every later fit solves from these values.
        if (confirmPopup != null && confirmTitle != null && confirmBody != null)
            ConfirmCardFitter.Capture(PopupUI.FindCard(confirmPopup.transform) as RectTransform,
                confirmTitle, confirmBody, ref confirmBaseline);

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

    private void OnConfirm()
    {
        if (confirmPopup != null) PopupUI.Hide(confirmPopup);
        Action confirmed = pendingConfirm;
        pendingConfirm = null;
        confirmed?.Invoke();
    }

    private void OnCancel()
    {
        if (confirmPopup != null) PopupUI.Hide(confirmPopup);
        pendingConfirm = null;
    }

    private void SetVisualMode(ReplyMode mode, bool animate)
    {
        currentMode = mode;
        ApplyVisuals(mode, animate);
    }

    private void ApplyVisuals(ReplyMode mode, bool animate)
    {
        KillColorTweens();
        PaintChip(AutoButtonModel.IsAutoOn(mode), ringImage, fillImage, label, dotRing, dotCore, animate);
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

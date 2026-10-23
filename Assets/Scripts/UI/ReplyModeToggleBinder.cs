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
/// clients itself, hollow dot in an outlined pill when it only proposes. The
/// selected bot's learning percent fills the pill and captions it «Учится · N %»
/// (spec 2026-10-22-learning-progress-percent §4.3), through the same
/// <see cref="PaintChip"/> every «Авто» capsule is painted by.
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
    [SerializeField] private Image ringImage;   // outer rounded rect: Border OFF / PositiveBg ON (AutoChipLook's Ring)
    [SerializeField] private Image fillImage;   // inner rounded rect, the face: Surface OFF / PositiveBg ON (AutoChipLook's Base)
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Image dotRing;     // 18u state lamp: InkTertiary hollow OFF / PositiveInk ON
    [SerializeField] private Image dotCore;     // the hollow's hole: what is under it OFF / PositiveInk ON
    [SerializeField] private AutoChipProgress progress;      // the selected bot's learning fill over fillImage
    [SerializeField] private TextMeshProUGUI learnCaption;   // «Учится · N %» under the pill (PaintCaption)

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
        "ИИ-сотрудник будет сам отвечать клиентам. Выключить можно в любой момент — этой же кнопкой.";

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

    /// <summary>Opacity of a pill whose tapped state the server has not confirmed yet (A2).</summary>
    public const float PendingAlpha = 0.55f;

    /// <summary>
    /// True while <see cref="ReconcileFromServer"/> raises <see cref="OnReplyModeChanged"/>: the
    /// Manager's sync hook must NOT turn a value the server already holds into a new desire.
    /// </summary>
    public static bool ReconcilingFromServer { get; private set; }

    /// <summary>
    /// The server holds a NEWER bot-default than the local one (another device, or a reordered
    /// write): persist + repaint every surface WITHOUT writing it back (A2).
    /// </summary>
    public static void ReconcileFromServer(string botName, ReplyMode mode)
    {
        if (string.IsNullOrEmpty(botName)) return;
        ReconcilingFromServer = true;
        try { CommitMode(botName, mode); }
        finally { ReconcilingFromServer = false; }
    }

    private void HandleSyncStateChanged(string botId, string chatId)
    {
        if (botId != currentBotId || chatId != ReplyModeSyncQueue.BotDefaultChat) return;
        ApplyPending();
    }

    private void HandleServerNewer(string botId, string profileId, string chatId, bool suppressed)
    {
        if (chatId != ReplyModeSyncQueue.BotDefaultChat) return;
        ReconcileFromServer(botId, suppressed ? ReplyMode.Semi : ReplyMode.Auto);
    }

    /// <summary>Dim the header pill while this bot's '*' desire is unconfirmed; restore once confirmed.</summary>
    private void ApplyPending()
    {
        if (toggleButton == null) return;
        bool pending = ReplyModeSyncService.Existing != null && !string.IsNullOrEmpty(currentBotId)
                       && ReplyModeSyncService.Existing.IsPending(currentBotId, ReplyModeSyncQueue.BotDefaultChat);
        var group = toggleButton.GetComponent<CanvasGroup>();
        if (group == null) group = toggleButton.gameObject.AddComponent<CanvasGroup>();
        group.alpha = pending ? PendingAlpha : 1f;
    }

    /// <summary>
    /// Route an enable request through the confirm popup (falls back to a direct
    /// commit when no popup is wired, e.g. before the builder ran).
    /// </summary>
    public static void RequestEnableAuto(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return;

        // learning B: the soft lock first (the server's verdict), then the shared confirm; «Включить всё равно» re-enters here.
        TMP_FontAsset font = instance != null && instance.confirmTitle != null ? instance.confirmTitle.font : null;
        AutoLockGate.TryEnable(botName, font, perChat: false, () =>
        {
            if (!ShowConfirm(EnableTitle, EnableBody, () => CommitMode(botName, ReplyMode.Auto)))
                CommitMode(botName, ReplyMode.Auto);
        });
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
    /// The single painter of every «Авто» capsule — the bot card, this header button, the bot-switcher sheet's per-bot chips
    /// and the per-chat SemiAutoToggle — so the four can never drift apart. What each part wears is
    /// <see cref="AutoChipLook.For"/>'s answer (is «Авто» ON in this place; the bot's learning percent, null = no data): the
    /// ring, the face (<paramref name="fill"/>), the label, the lamp, then the learning fill through <paramref name="progress"/>.
    /// A hollow lamp's core is the colour actually under it — the fill's where the fill covers the lamp's centre, else the
    /// face's. A repaint that LOWERS the fill (ON at 99 % → trained, where the fill goes and the face takes its colour) sets
    /// every colour at once: a cross-fade would start the face from the lighter track under a fill that is already gone, a
    /// flash. So does the capsule's FIRST paint with a percent — the first snapshot over «данных нет», the «Авто» face back from
    /// «Подключить» (<see cref="AutoChipProgress.IsFirstPercent"/>): spec §4.3 «при первой отрисовке — сразу», only a percent
    /// grown from one this capsule already showed animates. A growth and an ordinary mode change cross-fade as before. Every ref
    /// is null-guarded, the layer too (through Unity's ==: an unassigned or destroyed component is not C# null) — a capsule
    /// without it paints as it always did.
    /// </summary>
    public static void PaintChip(bool on, int? percent, Image ring, Image fill, AutoChipProgress progress,
        TextMeshProUGUI chipLabel, Image lampRing, Image lampCore, bool animate, float duration = AnimDuration)
    {
        AutoChipPaint paint = AutoChipLook.For(on, percent);
        bool hasLayer = progress != null;
        Color faceColor = Theme.Color(paint.Base);
        Color fillColor = Theme.Color(paint.Fill);
        Color lampColor = Theme.Color(paint.Lamp);
        Color lampCoreColor = paint.LampSolid ? lampColor
            : hasLayer && lampCore != null && progress.Covers(lampCore.rectTransform, paint.FillFraction) ? fillColor
            : faceColor;
        bool atOnce = hasLayer && (progress.IsFirstPercent(percent.HasValue) || paint.FillFraction < progress.AppliedFraction);
        bool tween = animate && !atOnce;

        PaintPart(fill, faceColor, tween, duration);
        PaintPart(ring, Theme.Color(paint.Ring), tween, duration);
        PaintPart(chipLabel, Theme.Color(paint.Ink), tween, duration);
        PaintPart(lampRing, lampColor, tween, duration);
        PaintPart(lampCore, lampCoreColor, tween, duration);
        if (hasLayer) progress.Apply(paint.FillFraction, fillColor, tween, percent.HasValue);
    }

    /// <summary>
    /// The learning caption under a capsule — only the bot card's and this header's (the sheet row and the chat chip are too
    /// small for it): shown with <paramref name="text"/>, hidden on null. Its colour is the caption's own ThemedColor binding.
    /// </summary>
    public static void PaintCaption(TextMeshProUGUI caption, string text)
    {
        if (caption == null) return;
        if (text != null) caption.text = text;
        caption.gameObject.SetActive(text != null);
    }

    private static void PaintPart(Graphic part, Color color, bool tween, float duration)
    {
        if (part == null) return;
        if (tween) part.DOColor(color, duration).SetEase(Ease.OutCubic);
        else part.color = color;
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
        ReplyModeSyncService.OnStateChanged += HandleSyncStateChanged;
        ReplyModeSyncService.OnServerNewer += HandleServerNewer;
        UsageStore.OnUsageChanged += HandleUsageChanged;
        ApplyPending();

        if (ChatManager.Instance == null) return;
        ChatManager.Instance.OnActiveBotChanged += Refresh;
        Refresh(ChatManager.Instance.CurrentBotId);
    }

    private void OnDisable()
    {
        Theme.Changed -= RepaintForTheme;
        ReplyModeSyncService.OnStateChanged -= HandleSyncStateChanged;
        ReplyModeSyncService.OnServerNewer -= HandleServerNewer;
        UsageStore.OnUsageChanged -= HandleUsageChanged;

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
        ApplyPending();   // A2: a bot with an unconfirmed '*' desire opens dimmed
        SetVisualMode(GetMode(botId), animate: false);
    }

    private void RepaintForTheme() => ApplyVisuals(currentMode, animate: false);

    // A fresh Get Usage can move the selected bot's percent: the fill grows on screen, the caption follows.
    private void HandleUsageChanged() => ApplyVisuals(currentMode, animate: true);

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
        int? percent = AutoReadinessPolicy.StateFor(currentBotId)?.Percent;
        PaintChip(AutoButtonModel.IsAutoOn(mode), percent, ringImage, fillImage, progress, label, dotRing, dotCore, animate);
        PaintCaption(learnCaption, AutoChipLook.Caption(percent));
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

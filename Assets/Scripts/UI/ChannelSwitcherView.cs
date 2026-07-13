using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the chats-screen TopBar channel switcher — a two-segment pill
/// («WhatsApp» | «Telegram») that flips the active bot's chat channel. The
/// selected segment is filled with its brand accent; a segment whose channel
/// has no connected profile ("-1"/empty) renders MUTED (~40% alpha) but stays
/// TAPPABLE, so tapping it selects that channel and the 05-02 empty state
/// (BotHasNoWhatsApp / BotHasNoTelegram) surfaces the connect CTA (SWITCH-02).
///
/// Source of truth is <see cref="ChatManager"/>: selection follows
/// <see cref="ChatManager.ActiveChannel"/> (05-02 auto-resolves it per bot, so
/// per-bot persistence — SWITCH-03 — flows through here read-only, no local
/// state), and connectivity is read from the current <see cref="Bot"/>'s profile
/// fields. Event-driven (OnEnable subscribe / OnDisable unsubscribe, no Update
/// polling) with a late-activation catch-up Refresh in OnEnable.
///
/// All serialized refs are stamped by the 06-02 ChannelSwitcherBuilder via
/// SerializedObject; the field names below are that builder's contract. Every
/// ref is null-guarded — a bot deleted mid-screen degrades to computed default
/// state, never an NRE (T-06-02).
/// </summary>
public class ChannelSwitcherView : MonoBehaviour
{
    [Header("Chip buttons")]
    [SerializeField] private Button waChipButton;
    [SerializeField] private Button tgChipButton;

    [Header("Chip fills (selected-state background)")]
    [SerializeField] private Image waChipFill;
    [SerializeField] private Image tgChipFill;

    [Header("Chip labels")]
    [SerializeField] private TextMeshProUGUI waLabel;
    [SerializeField] private TextMeshProUGUI tgLabel;

    [Header("Chip icons (optional brand logos)")]
    [SerializeField] private Image waChipIcon;
    [SerializeField] private Image tgChipIcon;

    // Brand accents shown only on the SELECTED (filled) chip. WhatsApp keeps its
    // #25D366-family green; Telegram matches Manager.TelegramBrandColor (#2AABEE,
    // private there, so mirrored locally).
    private static readonly Color WaSelectedFill = Hex("#25D366");
    private static readonly Color TgSelectedFill = Hex("#2AABEE");
    private static readonly Color SelectedLabel = Color.white;   // legible on the saturated fill
    private static readonly Color UnselectedLabel = Hex("#3A3A3C"); // neutral ink on the transparent segment
    private const float MutedAlpha = 0.40f;                       // BotSwitcherRowView-style fade (never tint)

    private void Awake()
    {
        WireChip(waChipButton, ChatChannel.WhatsApp);
        WireChip(tgChipButton, ChatChannel.Telegram);
    }

    private void OnEnable()
    {
        // Mirror ReplyModeToggleBinder: no ChatManager yet ⇒ nothing to bind.
        if (ChatManager.Instance == null) return;

        ChatManager.Instance.OnActiveBotChanged += HandleBotChanged;
        ChatManager.Instance.OnActiveChannelChanged += HandleChannelChanged;

        // Late-activation catch-up: the bot/channel may have changed while this
        // screen was inactive — pull current state immediately.
        Refresh();
    }

    private void OnDisable()
    {
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnActiveBotChanged -= HandleBotChanged;
            ChatManager.Instance.OnActiveChannelChanged -= HandleChannelChanged;
        }

        KillChipTween(waChipButton);
        KillChipTween(tgChipButton);
    }

    private void WireChip(Button button, ChatChannel channel)
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => OnChipTapped(channel));
    }

    // A tap always routes through SetActiveChannel — which no-ops when the channel
    // is unchanged (per 05-02) — so no pre-check here. Muted chips are NEVER made
    // non-interactable: tapping an unconnected channel is how the owner reaches its
    // connect empty state (SWITCH-02).
    private void OnChipTapped(ChatChannel channel)
    {
        Button chip = channel == ChatChannel.Telegram ? tgChipButton : waChipButton;
        if (chip != null)
        {
            chip.transform.DOKill();
            chip.transform.localScale = Vector3.one;
            chip.transform.DOPunchScale(Vector3.one * 0.06f, 0.18f, 1, 0.5f);
        }

        ChatManager.Instance?.SetActiveChannel(channel);
    }

    private void HandleBotChanged(string _) => Refresh();
    private void HandleChannelChanged(ChatChannel _) => Refresh();

    /// <summary>
    /// Recompute both chips from the live source of truth: the current bot's
    /// per-channel connectivity and ChatManager's ActiveChannel. Fully null-safe —
    /// a missing Manager/ChatManager/bot degrades to WhatsApp-selected defaults.
    /// </summary>
    private void Refresh()
    {
        string botId = ChatManager.Instance != null ? ChatManager.Instance.CurrentBotId : null;
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(botId) : null;

        bool waConnected = IsConnected(bot != null ? bot.whatsappProfileId : null);
        bool tgConnected = IsConnected(bot != null ? bot.telegramProfileId : null);
        ChatChannel active = ChatManager.Instance != null ? ChatManager.Instance.ActiveChannel : ChatChannel.WhatsApp;

        ApplyChip(waChipFill, waLabel, waChipIcon,
            ChannelSwitcherModel.StateFor(ChatChannel.WhatsApp, active, waConnected, tgConnected), WaSelectedFill);
        ApplyChip(tgChipFill, tgLabel, tgChipIcon,
            ChannelSwitcherModel.StateFor(ChatChannel.Telegram, active, waConnected, tgConnected), TgSelectedFill);
    }

    // Copied verbatim from BotSwitcherRowView: a profile id is connected only when
    // it is a real, non-sentinel value.
    private static bool IsConnected(string profileId) =>
        !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    /// <summary>
    /// Paint one chip. Selected ⇒ brand fill visible (alpha 1) + white label;
    /// unselected ⇒ transparent fill + neutral label. Muted ⇒ label and icon alpha
    /// dropped to <see cref="MutedAlpha"/> (brand logos fade, never tint), applied on
    /// top of the selection colors so a selected-but-unconnected chip still reads muted.
    /// Every ref is null-guarded (the 06-02 builder may leave the optional icon unset).
    /// </summary>
    private static void ApplyChip(Image fill, TextMeshProUGUI label, Image icon,
        ChannelChipState state, Color selectedFill)
    {
        if (fill != null)
        {
            Color f = selectedFill;
            f.a = state.Selected ? 1f : 0f;
            fill.color = f;
        }

        if (label != null)
        {
            Color c = state.Selected ? SelectedLabel : UnselectedLabel;
            if (state.Muted) c.a *= MutedAlpha;
            label.color = c;
        }

        if (icon != null)
            icon.color = new Color(1f, 1f, 1f, state.Muted ? MutedAlpha : 1f);
    }

    private static void KillChipTween(Button button)
    {
        if (button == null) return;
        button.transform.DOKill();
        button.transform.localScale = Vector3.one;
    }

    private static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}

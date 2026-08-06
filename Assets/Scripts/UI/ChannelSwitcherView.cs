using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the chats-screen TopBar channel switcher — since the 2026-08 restyle a
/// full-width RECESSED segment (a Background-token well) where the selected cell
/// is a Surface card and the channel brand survives only as a 20u dot
/// (palette-native treatment, docs/design/ui-restyle/chats-topbar-spec.md §2).
/// The selected cell also carries the ACTIVE channel's unread total.
///
/// Behaviour is unchanged from the pill era: a segment whose channel has no
/// connected profile ("-1"/empty) renders MUTED (~40% alpha) but stays TAPPABLE,
/// so tapping an unconnected channel still surfaces its connect empty state
/// (SWITCH-02). Source of truth is <see cref="ChatManager"/>: selection follows
/// <see cref="ChatManager.ActiveChannel"/>, connectivity is read from the
/// current <see cref="Bot"/>'s profile fields, decisions stay in the pure
/// <see cref="ChannelSwitcherModel"/> seam. Event-driven with a late-activation
/// catch-up Refresh in OnEnable; colors re-pull on <see cref="Theme.Changed"/>.
///
/// All serialized refs are stamped by ChatsTopBarRestyleBuilder via
/// SerializedObject; every ref is null-guarded so a bot deleted mid-screen
/// degrades to computed default state, never an NRE.
/// </summary>
public class ChannelSwitcherView : MonoBehaviour
{
    [Header("Cell buttons")]
    [SerializeField] private Button waChipButton;
    [SerializeField] private Button tgChipButton;

    [Header("Cell cards (Surface fill, visible when selected)")]
    [SerializeField] private Image waChipFill;
    [SerializeField] private Image tgChipFill;

    [Header("Cell labels")]
    [SerializeField] private TextMeshProUGUI waLabel;
    [SerializeField] private TextMeshProUGUI tgLabel;

    [Header("Brand dots (20u)")]
    [SerializeField] private Image waDot;
    [SerializeField] private Image tgDot;

    [Header("Unread counts (selected cell only)")]
    [SerializeField] private TextMeshProUGUI waCount;
    [SerializeField] private TextMeshProUGUI tgCount;

    private const float MutedAlpha = 0.40f;         // unconnected fade (never tint)
    private const float UnselectedDotAlpha = 0.40f; // brand dot on an unselected cell

    private Action<ChatViewModel> chatAddedHandler;

    private void Awake()
    {
        chatAddedHandler = _ => Refresh();
        WireChip(waChipButton, ChatChannel.WhatsApp);
        WireChip(tgChipButton, ChatChannel.Telegram);
    }

    private void OnEnable()
    {
        Theme.Changed += Refresh;

        // Mirror ReplyModeToggleBinder: no ChatManager yet ⇒ nothing to bind.
        if (ChatManager.Instance == null) return;

        ChatManager.Instance.OnActiveBotChanged += HandleBotChanged;
        ChatManager.Instance.OnActiveChannelChanged += HandleChannelChanged;
        ChatManager.Instance.OnChatAdded += chatAddedHandler;    // unread total follows the list
        ChatManager.Instance.OnChatListCleared += Refresh;

        // Late-activation catch-up: the bot/channel may have changed while this
        // screen was inactive — pull current state immediately.
        Refresh();
    }

    private void OnDisable()
    {
        Theme.Changed -= Refresh;

        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.OnActiveBotChanged -= HandleBotChanged;
            ChatManager.Instance.OnActiveChannelChanged -= HandleChannelChanged;
            ChatManager.Instance.OnChatAdded -= chatAddedHandler;
            ChatManager.Instance.OnChatListCleared -= Refresh;
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
    // is unchanged (per 05-02) — so no pre-check here. Muted cells are NEVER made
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
    /// Recompute both cells from the live source of truth. Fully null-safe —
    /// a missing Manager/ChatManager/bot degrades to WhatsApp-selected defaults.
    /// </summary>
    private void Refresh()
    {
        string botId = ChatManager.Instance != null ? ChatManager.Instance.CurrentBotId : null;
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(botId) : null;

        bool waConnected = IsConnected(bot != null ? bot.whatsappProfileId : null);
        bool tgConnected = IsConnected(bot != null ? bot.telegramProfileId : null);
        ChatChannel active = ChatManager.Instance != null ? ChatManager.Instance.ActiveChannel : ChatChannel.WhatsApp;
        int unread = ActiveUnreadTotal();

        ApplyCell(waChipFill, waLabel, waDot, waCount,
            ChannelSwitcherModel.StateFor(ChatChannel.WhatsApp, active, waConnected, tgConnected),
            Theme.Fixed.WhatsAppGreen, unread);
        ApplyCell(tgChipFill, tgLabel, tgDot, tgCount,
            ChannelSwitcherModel.StateFor(ChatChannel.Telegram, active, waConnected, tgConnected),
            Theme.Fixed.TelegramBlue, unread);
    }

    // Copied verbatim from BotSwitcherRowView: a profile id is connected only when
    // it is a real, non-sentinel value.
    private static bool IsConnected(string profileId) =>
        !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    // Phase 1 of the spec: the count is the ACTIVE channel's — exactly what the
    // live chat list holds. The inactive channel's total needs its cache read and
    // is deliberately deferred (spec §2, phase 2).
    private int ActiveUnreadTotal()
    {
        if (ChatManager.Instance == null || ChatManager.Instance.Chats == null) return 0;

        int total = 0;
        var chats = ChatManager.Instance.Chats;
        for (int i = 0; i < chats.Count; i++)
        {
            if (chats[i] != null) total += Mathf.Max(0, chats[i].UnreadCount);
        }
        return total;
    }

    /// <summary>
    /// Paint one cell, palette-native. Selected ⇒ Surface card + InkPrimary label
    /// + full-alpha brand dot (+ unread count when > 0); unselected ⇒ transparent
    /// card + InkTertiary label + 40% dot. Muted multiplies every alpha by
    /// <see cref="MutedAlpha"/> on top — a selected-but-unconnected cell must read
    /// muted as a whole. Every ref is null-guarded.
    /// </summary>
    private static void ApplyCell(Image fill, TextMeshProUGUI label, Image dot,
        TextMeshProUGUI count, ChannelChipState state, Color brand, int unreadTotal)
    {
        float mutedFactor = state.Muted ? MutedAlpha : 1f;

        if (fill != null)
        {
            Color card = Theme.Color(ThemeRole.Surface);
            card.a = state.Selected ? mutedFactor : 0f;
            fill.color = card;
        }

        if (label != null)
        {
            Color ink = Theme.Color(state.Selected ? ThemeRole.InkPrimary : ThemeRole.InkTertiary);
            ink.a *= mutedFactor;
            label.color = ink;
        }

        if (dot != null)
        {
            Color d = brand;
            d.a = (state.Selected ? 1f : UnselectedDotAlpha) * mutedFactor;
            dot.color = d;
        }

        if (count != null)
        {
            bool show = state.Selected && unreadTotal > 0;
            count.gameObject.SetActive(show);
            if (show)
            {
                count.text = unreadTotal > 99 ? "99+" : unreadTotal.ToString();
                Color ink = Theme.Color(ThemeRole.InkTertiary);
                ink.a *= mutedFactor;
                count.color = ink;
            }
        }
    }

    private static void KillChipTween(Button button)
    {
        if (button == null) return;
        button.transform.DOKill();
        button.transform.localScale = Vector3.one;
    }
}

using System;
using UnityEngine;

// Partial class extension housing ChatManager's channel identity: which channel
// (WhatsApp / Telegram) the active bot is currently showing, the per-bot persisted
// choice, the SetActiveChannel reset choreography (mirrors SetActiveBot), and the
// pure channel-resolution helpers used on bot switch / startup.
//
// Defaults to WhatsApp and — until SetActiveChannel is ever called — leaves every
// WhatsApp behaviour byte-identical (legacy cache root, same events, same sync window).
public partial class ChatManager
{
    /// <summary>
    /// The channel the active bot's chat pipeline is currently bound to. Read-only
    /// to the outside; mutated only via SetActiveChannel / channel resolution.
    /// Defaults to WhatsApp so existing single-channel bots behave exactly as before.
    /// </summary>
    public ChatChannel ActiveChannel { get; private set; } = ChatChannel.WhatsApp;

    /// <summary>PlayerPrefs key suffix holding a bot's persisted channel (int ordinal).</summary>
    private const string ActiveChannelKeySuffix = "ActiveChatChannel";

    /// <summary>
    /// Fires after the active channel changes (Phase 6's switcher UI consumes it).
    /// Announced mid-reset, mirroring OnActiveBotChanged's place in SetActiveBot.
    /// </summary>
    public event Action<ChatChannel> OnActiveChannelChanged;

    /// <summary>
    /// Defensive read of a bot's persisted channel. PlayerPrefs are user-editable on
    /// disk, so any value that is not a defined ChatChannel ordinal (0/1) is clamped
    /// to WhatsApp — no out-of-range enum cast ever reaches a switch (T-0502-01).
    /// </summary>
    private static int ReadPersistedChannel(string botId)
    {
        int raw = PlayerPrefs.GetInt(botId + ActiveChannelKeySuffix, (int)ChatChannel.WhatsApp);
        return raw == (int)ChatChannel.Telegram ? (int)ChatChannel.Telegram : (int)ChatChannel.WhatsApp;
    }

    /// <summary>The profile id a bot holds for the given channel (unvalidated).</summary>
    private static string ProfileIdForChannel(Bot bot, ChatChannel channel) =>
        channel == ChatChannel.Telegram ? bot.telegramProfileId : bot.whatsappProfileId;

    /// <summary>
    /// Switch the active channel for the current bot. Reuses SetActiveBot's full reset
    /// choreography so the guards, in-memory lists, queues and outbox never carry a
    /// stale channel's data across the switch. Persists the choice and fires
    /// OnActiveChannelChanged. No-ops when the channel is unchanged.
    /// </summary>
    public void SetActiveChannel(ChatChannel channel)
    {
        if (channel == ActiveChannel) return;

        // If a chat is open, return to the list first so the message view doesn't
        // strand on a channel that no longer owns the open conversation. Then drop the
        // open-chat identity/cache: nothing SelectChat-owned may survive the switch, or
        // public accessors (CurrentChatId, TryGetRecentMessages) would serve the other
        // channel's chat until the next SelectChat (IN-01). ShowChatList never reads them.
        if (!string.IsNullOrEmpty(currentChatId)) ShowChatList();
        currentChatId = null;
        _activeChatCache = null;

        if (_syncWaitRoutine != null) { StopCoroutine(_syncWaitRoutine); _syncWaitRoutine = null; }

        ActiveChannel = channel;
        PlayerPrefs.SetInt(CurrentBotId + ActiveChannelKeySuffix, (int)channel);
        PlayerPrefs.Save();

        // Drop the per-bot outbox cache so the next access re-loads from disk instead
        // of returning entries snapshotted against the previous channel.
        _outbox = null;

        Chats.Clear();
        chatLookup.Clear();
        OnChatListCleared?.Invoke();
        OnActiveChannelChanged?.Invoke(channel);

        StopAllCoroutines();        // also cancels in-flight thumbnail extraction coroutines
        _chatFetchesInFlight = 0;   // counter never decremented for the killed-mid-flight fetches
        _chatListSyncing = false;   // a SyncAllChats killed mid-flight never runs its finally
        ClearVideoThumbQueue();     // reset queue bookkeeping the cancelled coroutines never freed
        ClearMediaDownloadQueue();  // same for the serial media-download worker
        ClearResolveQueues();       // quote/reaction drain workers were just killed; reset their bookkeeping

        // Owner identity is per-profile — LOAD this bot's persisted Telegram owner-id rather than
        // stranding it null (D2 root cause B; see SetActiveBot). CurrentBotId is unchanged by a
        // channel switch; the shared helper reads the Telegram id explicitly so the load key
        // always matches the learn key (never GetActiveProfileId()).
        ReloadTgOwnUserIdFor(CurrentBotId);

        // D5: StopAllCoroutines() above killed the open-chat live poll — re-kick it so a channel
        // switch never strands it (D5 is cross-channel; the poll must keep running on Telegram
        // too). Guarded against duplicates.
        if (_livePollRoutine != null) StopCoroutine(_livePollRoutine);
        _livePollRoutine = StartCoroutine(OpenChatLivePollRoutine());

        BeginLoadForActiveBot();
    }

    /// <summary>
    /// Resolve the effective channel for a bot at switch/startup: restore the persisted
    /// choice, but if that channel is unconnected while the OTHER channel IS connected,
    /// auto-select the connected one and persist the correction. Missing key ⇒ WhatsApp.
    /// Pure decision delegated to <see cref="ChannelResolver"/>.
    /// </summary>
    public ChatChannel ResolveChannelForBot(string botId)
    {
        int persisted = ReadPersistedChannel(botId);
        Bot bot = Manager.Instance != null ? Manager.Instance.FindBotByName(botId) : null;
        bool waConnected = bot != null && IsValidProfileId(bot.whatsappProfileId);
        bool tgConnected = bot != null && IsValidProfileId(bot.telegramProfileId);

        ChatChannel resolved = ChannelResolver.Resolve(persisted, waConnected, tgConnected);
        if ((int)resolved != persisted)
        {
            PlayerPrefs.SetInt(botId + ActiveChannelKeySuffix, (int)resolved);
            PlayerPrefs.Save();
        }
        return resolved;
    }
}

/// <summary>
/// Pure, side-effect-free channel-selection decision so the switch/startup logic stays
/// unit-testable in EditMode (WhatsAppSyncGate / ChannelTabStateResolver precedent).
/// Rule: keep the persisted channel if it is connected; otherwise fall back to the OTHER
/// channel when IT is connected; otherwise keep the persisted/default channel.
/// </summary>
public static class ChannelResolver
{
    public static ChatChannel Resolve(int persistedChannel, bool waConnected, bool tgConnected)
    {
        // Clamp anything that is not the Telegram ordinal to WhatsApp (defensive; the
        // caller already clamps, but the pure core must never trust its input either).
        ChatChannel persisted = persistedChannel == (int)ChatChannel.Telegram
            ? ChatChannel.Telegram
            : ChatChannel.WhatsApp;

        bool persistedConnected = persisted == ChatChannel.Telegram ? tgConnected : waConnected;
        if (persistedConnected) return persisted;

        // Persisted channel unconnected — auto-select the other one only if it is connected.
        if (persisted == ChatChannel.WhatsApp && tgConnected) return ChatChannel.Telegram;
        if (persisted == ChatChannel.Telegram && waConnected) return ChatChannel.WhatsApp;

        // Neither connected — keep the persisted/default choice.
        return persisted;
    }
}

/// <summary>
/// Pure mapping of a channel to its cache sub-directory under BotCache/{botId}/.
/// WhatsApp keeps the legacy root (empty sub-dir, byte-identical); Telegram nests in a
/// hardcoded constant sub-dir so a dual-channel bot no longer collides chats.json.
/// The sub-dir is a fixed constant — never a user-controlled path component (T-0502-03).
/// </summary>
public static class ChannelCachePath
{
    public const string TelegramSubDir = "telegram";

    public static string SubDir(ChatChannel channel) =>
        channel == ChatChannel.Telegram ? TelegramSubDir : "";
}

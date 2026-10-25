using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.4): «Кто из них клиенты?» — when it is owed and when it shows.
///
/// Owed for one channel of a bot (<c>{bot}AudienceOnboarding{Whatsapp|Telegram}</c>, <see cref="Request"/>):
///   • at the channel's first connection — a channel never seen on this bot (<c>{bot}AudienceChannelSeen{Whatsapp|Telegram}</c>,
///     <see cref="MarkChannelSeen"/>: set at every confirmed pairing, the wizard's and the settings', and forgotten only when the
///     bot is deleted — a bot created anew connects its channels for the first time again);
///   • again when the channel now carries another number — another number is a new connection: the server says so in its
///     answer, the moment moved later for the same profile (<see cref="RequestForProfile"/>), or the settings saw two different
///     numbers (<see cref="ChatAudienceConnectPolicy.OwesOnboarding"/>).
/// Channels paired before this version are marked seen ONCE, right after the bots load (<see cref="PresetChannelsSeen"/>), so
/// those bots never get the screen. Cleared when the owner is done with the screen («Готово» / «Отмечу позже») and when the
/// bot is deleted.
///
/// Shown (<see cref="TryShow"/>) while the chats screen shows that bot on that channel; the chats screen asks when it comes on
/// screen, on a bot or channel switch and when a flag is set (<see cref="Requested"/>). The screen itself is the «Личные»
/// folder in its selection mode (<see cref="PersonalChatsFolderView"/>). Per-bot PlayerPrefs keys, saved at once
/// (bot-persistence); the preset's marker is global.
/// </summary>
public static class PersonalChatsOnboarding
{
    public const string WhatsappSuffix = "AudienceOnboardingWhatsapp";
    public const string TelegramSuffix = "AudienceOnboardingTelegram";
    public const string WhatsappSeenSuffix = "AudienceChannelSeenWhatsapp";
    public const string TelegramSeenSuffix = "AudienceChannelSeenTelegram";

    /// <summary>Global: the channels paired before this version have been marked seen — once, after the bots loaded.</summary>
    public const string SeenPresetKey = "AudienceChannelSeenPreset";

    /// <summary>(botName, telegram) — the screen became owed for this channel of the bot; the chats screen shows it at once.</summary>
    public static event Action<string, bool> Requested;

    /// <summary>One bot's channels as the device holds them: the current profile ids and the ones the server retired.</summary>
    public readonly struct BotChannels
    {
        public BotChannels(string botName, string whatsappProfileId, string telegramProfileId, string whatsappRetiredId, string telegramRetiredId)
        {
            BotName = botName;
            WhatsappProfileId = whatsappProfileId;
            TelegramProfileId = telegramProfileId;
            WhatsappRetiredId = whatsappRetiredId;
            TelegramRetiredId = telegramRetiredId;
        }

        public string BotName { get; }
        public string WhatsappProfileId { get; }
        public string TelegramProfileId { get; }
        public string WhatsappRetiredId { get; }
        public string TelegramRetiredId { get; }
    }

    // ---- the screen owed ----

    public static string FlagKey(string botName, bool telegram) => $"{botName}{(telegram ? TelegramSuffix : WhatsappSuffix)}";

    /// <summary>The screen is owed for this channel of the bot — saved at once (a pairing is a moment worth keeping) and told.</summary>
    public static void Request(string botName, bool telegram)
    {
        if (string.IsNullOrEmpty(botName)) return;
        PlayerPrefs.SetInt(FlagKey(botName, telegram), 1);
        PlayerPrefs.Save();
        Requested?.Invoke(botName, telegram);
    }

    public static bool IsRequested(string botName, bool telegram) =>
        !string.IsNullOrEmpty(botName) && PlayerPrefs.GetInt(FlagKey(botName, telegram), 0) == 1;

    /// <summary>Nothing is owed any more for this channel of the bot (the channel stays seen).</summary>
    public static void Clear(string botName, bool telegram)
    {
        if (string.IsNullOrEmpty(botName)) return;
        PlayerPrefs.DeleteKey(FlagKey(botName, telegram));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// The server restarted the moment of <paramref name="profileId"/>'s connection — another number: the screen is owed to the
    /// bot whose stored <c>{bot}WhatsappProfileId</c> / <c>{bot}TelegramProfileId</c> it is, on that channel. False (nothing
    /// owed) when no bot of <paramref name="botNames"/> holds it.
    /// </summary>
    public static bool RequestForProfile(string profileId, IEnumerable<string> botNames)
    {
        if (!ChatAudienceRequest.IsRealProfileId(profileId) || botNames == null) return false;
        foreach (string botName in botNames)
        {
            if (string.IsNullOrEmpty(botName)) continue;
            foreach (bool telegram in new[] { false, true })
            {
                if (StoredProfileId(botName, telegram) != profileId) continue;
                Request(botName, telegram);
                return true;
            }
        }
        return false;
    }

    // ---- the channel seen ----

    public static string ChannelSeenKey(string botName, bool telegram) => $"{botName}{(telegram ? TelegramSeenSuffix : WhatsappSeenSuffix)}";

    /// <summary>A pairing of this channel was confirmed on the bot: from now on it is no first connection.</summary>
    public static void MarkChannelSeen(string botName, bool telegram)
    {
        if (string.IsNullOrEmpty(botName)) return;
        PlayerPrefs.SetInt(ChannelSeenKey(botName, telegram), 1);
        PlayerPrefs.Save();
    }

    public static bool IsChannelSeen(string botName, bool telegram) =>
        !string.IsNullOrEmpty(botName) && PlayerPrefs.GetInt(ChannelSeenKey(botName, telegram), 0) == 1;

    /// <summary>The bot is deleted (Bot.DeleteBotLocally — the only caller): both its channels are forgotten.</summary>
    public static void ClearChannelsSeen(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return;
        PlayerPrefs.DeleteKey(ChannelSeenKey(botName, telegram: false));
        PlayerPrefs.DeleteKey(ChannelSeenKey(botName, telegram: true));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// The first launch of this version: every channel of <paramref name="bots"/> that holds a real current or retired profile
    /// was connected before — it is marked seen and never gets the screen. Runs ONCE (the global <see cref="SeenPresetKey"/>),
    /// and must be given the LOADED bots: a run over none would spend the marker and leave every existing channel owing the
    /// screen. True when it ran.
    /// </summary>
    public static bool PresetChannelsSeen(IReadOnlyList<BotChannels> bots)
    {
        if (PlayerPrefs.GetInt(SeenPresetKey, 0) == 1) return false;
        if (bots != null)
            foreach (BotChannels bot in bots)
            {
                if (!ChatAudienceConnectPolicy.IsFirstConnection(bot.WhatsappProfileId, bot.WhatsappRetiredId)) MarkChannelSeen(bot.BotName, telegram: false);
                if (!ChatAudienceConnectPolicy.IsFirstConnection(bot.TelegramProfileId, bot.TelegramRetiredId)) MarkChannelSeen(bot.BotName, telegram: true);
            }
        PlayerPrefs.SetInt(SeenPresetKey, 1);
        PlayerPrefs.Save();
        return true;
    }

    // ---- showing it ----

    /// <summary>Shown when it is owed for the chats on screen: the chats screen is up and shows that bot on that connected channel.</summary>
    public static bool ShouldShow(bool flagSet, bool chatsScreenVisible, bool botActive, bool channelActive) =>
        flagSet && chatsScreenVisible && botActive && channelActive;

    /// <summary>
    /// The chats screen came on screen, switched bot or channel, or a flag was set: the folder opens in its onboarding mode when
    /// <see cref="ShouldShow"/> says so for the bot and the channel the chats show now.
    /// </summary>
    public static void TryShow()
    {
        ChatManager chats = ChatManager.Instance;
        PersonalChatsFolderView folder = PersonalChatsFolderView.Instance;
        if (chats == null || folder == null) return;
        string botName = chats.CurrentBotId;
        bool botActive = Manager.Instance != null && Manager.Instance.FindBotByName(botName) != null;
        bool channelActive = ChatAudienceRequest.IsRealProfileId(chats.ActiveChannelProfileId());
        bool flagSet = IsRequested(botName, chats.ActiveChannel == ChatChannel.Telegram);
        if (ShouldShow(flagSet, folder.ChatsScreenVisible, botActive, channelActive)) folder.ShowOnboarding();
    }

    private static string StoredProfileId(string botName, bool telegram) =>
        PlayerPrefs.GetString($"{botName}{(telegram ? "Telegram" : "Whatsapp")}ProfileId", "");
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.4): «Кто из них клиенты?» — when it is owed and when it shows.
///
/// Owed for one channel of a bot (<c>{bot}AudienceOnboarding{Whatsapp|Telegram}</c>, <see cref="Request"/>), decided at every
/// confirmed pairing — the wizard's and the settings' (<see cref="ReportPairing"/>):
///   • at the channel's first connection — a channel never seen on this bot (<c>{bot}AudienceChannelSeen{Whatsapp|Telegram}</c>:
///     set at every confirmed pairing, forgotten only when the bot is deleted — a bot created anew connects its channels for the
///     first time again);
///   • again when the channel now carries another number than the last one confirmed on it
///     (<c>{bot}AudienceNumber{Whatsapp|Telegram}</c>, <see cref="PairedNumber"/>). Not the stored <c>{bot}{Ch}Number</c>: «Сменить
///     номер», its Back and an outside logout clear that one long before the next pairing, a fresh profile included.
///   • again when the server says another number restarted the connection (<see cref="AnswerConnected"/>) — unless the app
///     itself asked for the screen at that pairing: the first answer for that profile is consumed, whatever it says
///     (<c>{bot}AudienceOnboardingFor{Whatsapp|Telegram}</c> = the profile), so the screen comes once.
/// Withdrawn when the server says the pairing RESTORED the earlier connection (<see cref="AnswerRestored"/>: the previous number
/// back soon after a mistaken one, its decisions holding again) — the app's own request for that very pairing only.
/// Channels paired before this version are marked seen ONCE, right after the bots load, and their stored numbers become the
/// last paired ones (<see cref="PresetChannelsSeen"/>): those bots get the screen only for a later number change. The flag is
/// cleared when the owner is done with the screen («Готово» / «Отмечу позже»); everything here goes when the bot is deleted
/// (<see cref="ForgetBot"/>).
///
/// Shown (<see cref="TryShow"/>) while the chats screen shows that bot on that channel; the chats screen asks when it comes on
/// screen, on a bot or channel switch, when a flag is set (<see cref="Requested"/>) and when the «Личные» folder leaves
/// «Изменить» or closes. The screen itself is the folder in its selection mode (<see cref="PersonalChatsFolderView"/>).
/// Per-bot PlayerPrefs keys, saved at once (bot-persistence); the preset's marker is global.
/// </summary>
public static class PersonalChatsOnboarding
{
    public const string WhatsappSuffix = "AudienceOnboardingWhatsapp";
    public const string TelegramSuffix = "AudienceOnboardingTelegram";
    public const string WhatsappSeenSuffix = "AudienceChannelSeenWhatsapp";
    public const string TelegramSeenSuffix = "AudienceChannelSeenTelegram";
    public const string WhatsappNumberSuffix = "AudienceNumberWhatsapp";
    public const string TelegramNumberSuffix = "AudienceNumberTelegram";
    public const string WhatsappRequestedForSuffix = "AudienceOnboardingForWhatsapp";
    public const string TelegramRequestedForSuffix = "AudienceOnboardingForTelegram";

    /// <summary>Global: the channels paired before this version have been marked seen — once, after the bots loaded.</summary>
    public const string SeenPresetKey = "AudienceChannelSeenPreset";

    private static readonly bool[] Channels = { false, true };   // WhatsApp, Telegram

    /// <summary>(botName, telegram) — the screen became owed for this channel of the bot; the chats screen shows it at once.</summary>
    public static event Action<string, bool> Requested;

    /// <summary>
    /// One bot's channels as the device holds them: the current profile ids, the ones the server retired and the stored numbers
    /// (<c>{bot}{Ch}Number</c>).
    /// </summary>
    public readonly struct BotChannels
    {
        public BotChannels(string botName, string whatsappProfileId, string telegramProfileId, string whatsappRetiredId, string telegramRetiredId,
                           string whatsappNumber = "", string telegramNumber = "")
        {
            BotName = botName;
            WhatsappProfileId = whatsappProfileId;
            TelegramProfileId = telegramProfileId;
            WhatsappRetiredId = whatsappRetiredId;
            TelegramRetiredId = telegramRetiredId;
            WhatsappNumber = whatsappNumber;
            TelegramNumber = telegramNumber;
        }

        public string BotName { get; }
        public string WhatsappProfileId { get; }
        public string TelegramProfileId { get; }
        public string WhatsappRetiredId { get; }
        public string TelegramRetiredId { get; }
        public string WhatsappNumber { get; }
        public string TelegramNumber { get; }
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

    /// <summary>
    /// Nothing is owed any more for this channel of the bot. The channel stays seen, its last number stays, and so does the app's
    /// own request: the server's answer to that pairing, if it is still on its way, must not bring the screen back.
    /// </summary>
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
            foreach (bool telegram in Channels)
            {
                if (StoredProfileId(botName, telegram) != profileId) continue;
                Request(botName, telegram);
                return true;
            }
        }
        return false;
    }

    // ---- a pairing and the server's answer to it ----

    /// <summary>
    /// A pairing of the bot's channel was confirmed — the wizard's (<paramref name="newBot"/>: always a first connection) or the
    /// settings'. The screen is owed for a channel never seen on this bot, or one that now carries another number than the last
    /// one confirmed on it (<see cref="PairedNumber"/>). Decided first; then the channel is seen and <paramref name="authedNumber"/>
    /// becomes the last paired number (when it is known). An owed screen is the app's own request for
    /// <paramref name="profileId"/>, so the server's answer to this pairing asks nothing more (<see cref="AnswerConnected"/>).
    /// True when the screen became owed.
    /// </summary>
    public static bool ReportPairing(string botName, bool telegram, string profileId, string authedNumber, bool newBot)
    {
        if (string.IsNullOrEmpty(botName)) return false;
        bool owed = newBot
                    || ChatAudienceConnectPolicy.OwesOnboarding(IsChannelSeen(botName, telegram), PairedNumber(botName, telegram), authedNumber);
        MarkChannelSeen(botName, telegram);
        if (!string.IsNullOrWhiteSpace(authedNumber)) PlayerPrefs.SetString(PairedNumberKey(botName, telegram), authedNumber.Trim());
        if (owed && ChatAudienceRequest.IsRealProfileId(profileId)) PlayerPrefs.SetString(RequestedForKey(botName, telegram), profileId);
        PlayerPrefs.Save();
        if (owed) Request(botName, telegram);
        return owed;
    }

    /// <summary>
    /// The server answered a connection report of <paramref name="profileId"/> (ChatAudienceSyncService). The first answer for a
    /// profile the app itself asked the screen for (<see cref="ReportPairing"/>) is consumed — whatever it says — and asks
    /// nothing more: the app asked already, and the owner may be done with the screen by now. A marker naming another profile is
    /// left alone. Otherwise another number (<paramref name="numberRestarted"/>) owes the screen to the bot holding the profile.
    /// True when the screen became owed.
    /// </summary>
    public static bool AnswerConnected(string profileId, bool numberRestarted, IEnumerable<string> botNames)
    {
        if (!ChatAudienceRequest.IsRealProfileId(profileId) || botNames == null) return false;
        var bots = new List<string>(botNames);
        if (ConsumeOwnRequest(profileId, bots, out _, out _)) return false;
        return numberRestarted && RequestForProfile(profileId, bots);
    }

    /// <summary>
    /// The server answered a connection report of <paramref name="profileId"/> by RESTORING the earlier connection: the previous
    /// number was paired again soon after a mistaken one, and its decisions hold again — nothing is owed for it. The app asked for
    /// the screen itself at this pairing (<see cref="ReportPairing"/>: the number differs from the last paired one, the mistaken
    /// number); when that request names this profile it is consumed, as every answer consumes it, and withdrawn — the owed flag of
    /// that bot's channel is cleared. Nothing more: a request naming another profile stays, a restore never owes the screen, and a
    /// screen already on display stays until the owner closes it (clearing the flag closes nothing). True when a request was
    /// withdrawn.
    /// </summary>
    public static bool AnswerRestored(string profileId, IEnumerable<string> botNames)
    {
        if (!ChatAudienceRequest.IsRealProfileId(profileId) || botNames == null) return false;
        if (!ConsumeOwnRequest(profileId, botNames, out string botName, out bool telegram)) return false;
        Clear(botName, telegram);
        return true;
    }

    public static string RequestedForKey(string botName, bool telegram) =>
        $"{botName}{(telegram ? TelegramRequestedForSuffix : WhatsappRequestedForSuffix)}";

    // The app asked the screen for this profile at its pairing: that request answers the server's word, once. Names the bot and
    // the channel whose request it was.
    private static bool ConsumeOwnRequest(string profileId, IEnumerable<string> botNames, out string requestedBot, out bool requestedTelegram)
    {
        requestedBot = null;
        requestedTelegram = false;
        foreach (string botName in botNames)
        {
            if (string.IsNullOrEmpty(botName)) continue;
            foreach (bool telegram in Channels)
            {
                string key = RequestedForKey(botName, telegram);
                if (PlayerPrefs.GetString(key, "") != profileId) continue;
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                requestedBot = botName;
                requestedTelegram = telegram;
                return true;
            }
        }
        return false;
    }

    // ---- the channel seen, its last paired number ----

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

    public static string PairedNumberKey(string botName, bool telegram) => $"{botName}{(telegram ? TelegramNumberSuffix : WhatsappNumberSuffix)}";

    /// <summary>The last number confirmed on this channel of the bot — "" when none is known.</summary>
    public static string PairedNumber(string botName, bool telegram) =>
        string.IsNullOrEmpty(botName) ? "" : PlayerPrefs.GetString(PairedNumberKey(botName, telegram), "");

    /// <summary>
    /// The bot is deleted (Bot.DeleteBotLocally — the only caller): everything this keeps for it goes — the owed screens, the
    /// channels seen, the last paired numbers and the app's own requests. A bot created anew starts afresh.
    /// </summary>
    public static void ForgetBot(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return;
        foreach (bool telegram in Channels)
        {
            PlayerPrefs.DeleteKey(FlagKey(botName, telegram));
            PlayerPrefs.DeleteKey(ChannelSeenKey(botName, telegram));
            PlayerPrefs.DeleteKey(PairedNumberKey(botName, telegram));
            PlayerPrefs.DeleteKey(RequestedForKey(botName, telegram));
        }
        PlayerPrefs.Save();
    }

    /// <summary>
    /// The first launch of this version: every channel of <paramref name="bots"/> that holds a real current or retired profile
    /// was connected before — it is marked seen and never gets the screen for that connection — and a stored number becomes the
    /// channel's last paired number, so a later number change still owes the screen. Runs ONCE (the global
    /// <see cref="SeenPresetKey"/>), and must be given the LOADED bots: a run over none would spend the marker and leave every
    /// existing channel owing the screen. True when it ran.
    /// </summary>
    public static bool PresetChannelsSeen(IReadOnlyList<BotChannels> bots)
    {
        if (PlayerPrefs.GetInt(SeenPresetKey, 0) == 1) return false;
        if (bots != null)
            foreach (BotChannels bot in bots)
            {
                PresetChannel(bot.BotName, telegram: false, bot.WhatsappProfileId, bot.WhatsappRetiredId, bot.WhatsappNumber);
                PresetChannel(bot.BotName, telegram: true, bot.TelegramProfileId, bot.TelegramRetiredId, bot.TelegramNumber);
            }
        PlayerPrefs.SetInt(SeenPresetKey, 1);
        PlayerPrefs.Save();
        return true;
    }

    private static void PresetChannel(string botName, bool telegram, string profileId, string retiredId, string storedNumber)
    {
        if (string.IsNullOrEmpty(botName)) return;
        if (!ChatAudienceConnectPolicy.IsFirstConnection(profileId, retiredId)) MarkChannelSeen(botName, telegram);
        if (!string.IsNullOrWhiteSpace(storedNumber) && PairedNumber(botName, telegram).Length == 0)
            PlayerPrefs.SetString(PairedNumberKey(botName, telegram), storedNumber.Trim());
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

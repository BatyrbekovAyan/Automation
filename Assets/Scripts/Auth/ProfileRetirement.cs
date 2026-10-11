using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// M4 (follow-ups 2026-10-09): the device's record that the server retired one channel of a bot — the Profile
/// Lifecycle Sweep removed it (trial over, subscription lapsed, gone from Wappi), an app delete stopped half way, or the
/// channel was refused. The server says so once, on claim.start (410 <c>profile_retired</c>); the boot claim loop then
/// records it here, never asks that profile again, and the bot card says the channel is off.
///
/// F2 (plan 2026-10-11, task T8) replaces M4's «the ids stay»: the channel's ids are MOVED ASIDE — its workflow id to
/// <c>{bot}{Ch}RetiredWorkflowId</c>, the current profile / workflow ids to the sentinel, the channel switched off — so
/// every reader of a channel's ids sees an ordinary disconnected channel and a reconnect takes the fresh path naming the
/// retired profile as Create's <c>PreviousProfileId</c>. The deletion (V12) reads the retired set through
/// <see cref="ServerIdsOf"/>; the decisions are the pure <see cref="RetiredChannelPolicy"/>'s, an unfinished app delete is
/// not moved (its ids stay, the M4 marker applies to them).
///
/// Keys follow the per-bot convention (bot-persistence): <c>{bot}{Whatsapp|Telegram}Retired{ProfileId|Reason|WorkflowId}</c>,
/// prefixed with the bot GameObject's name; <see cref="Clear"/> runs in <c>Bot.DeleteBotLocally</c>, the full wipe's
/// <c>PlayerPrefs.DeleteAll</c> covers the rest. The Bot's fields and its PlayerPrefs are written in ONE place
/// (<see cref="Store"/>) with one Save.
/// </summary>
public static class ProfileRetirement
{
    public const string WhatsappProfileIdSuffix = "WhatsappRetiredProfileId";
    public const string WhatsappReasonSuffix = "WhatsappRetiredReason";
    public const string WhatsappWorkflowIdSuffix = "WhatsappRetiredWorkflowId";
    public const string TelegramProfileIdSuffix = "TelegramRetiredProfileId";
    public const string TelegramReasonSuffix = "TelegramRetiredReason";
    public const string TelegramWorkflowIdSuffix = "TelegramRetiredWorkflowId";

    /// <summary>Every retired-set key suffix — what <see cref="Clear"/> deletes.</summary>
    public static readonly string[] KeySuffixes =
    {
        WhatsappProfileIdSuffix, WhatsappReasonSuffix, WhatsappWorkflowIdSuffix,
        TelegramProfileIdSuffix, TelegramReasonSuffix, TelegramWorkflowIdSuffix,
    };

    // The server's deleted_reason values (bot_profiles.deleted_reason). Only the row's owner is told one.
    public const string ReasonTrialExpiry = "trial_expiry";
    public const string ReasonChurnGrace = "churn_grace";
    public const string ReasonLiveness = "liveness";
    public const string ReasonAppDelete = "app_delete";
    public const string ReasonChannelLimit = "channel_limit";

    /// <summary>A bot's channel was just retired or its retired set cleared (argument: the bot key) — its card repaints.</summary>
    public static event System.Action<string> Changed;

    public static string ProfileIdSuffix(ChatChannel channel) =>
        channel == ChatChannel.Telegram ? TelegramProfileIdSuffix : WhatsappProfileIdSuffix;

    public static string ReasonSuffix(ChatChannel channel) =>
        channel == ChatChannel.Telegram ? TelegramReasonSuffix : WhatsappReasonSuffix;

    public static string WorkflowIdSuffix(ChatChannel channel) =>
        channel == ChatChannel.Telegram ? TelegramWorkflowIdSuffix : WhatsappWorkflowIdSuffix;

    private static string ChannelWord(ChatChannel channel) => channel == ChatChannel.Telegram ? "Telegram" : "Whatsapp";

    /// <summary>
    /// The server retired <paramref name="profileId"/> on this bot's channel (reason may be null): the channel's ids are
    /// moved aside — unless it was an unfinished app delete, or the answer is about a profile the channel no longer holds.
    /// </summary>
    public static void Mark(Bot bot, ChatChannel channel, string profileId, string reason)
    {
        if (bot == null || !ProfileClaimPolicy.IsAuthedId(profileId)) return;
        RetiredChannelPolicy.Channel before = Load(bot, channel);
        Apply(bot, channel, before, RetiredChannelPolicy.Retire(before, profileId, reason));
    }

    /// <summary>
    /// A Create of this channel succeeded: the retired set goes only when the server named its profile among the adopted
    /// ones (their data now lives under the new connection).
    /// </summary>
    public static void AfterCreate(Bot bot, ChatChannel channel, IEnumerable<string> adoptedProfiles)
    {
        if (bot == null) return;
        RetiredChannelPolicy.Channel before = Load(bot, channel);
        Apply(bot, channel, before, RetiredChannelPolicy.AfterCreate(before, adoptedProfiles));
    }

    /// <summary>
    /// F2: markers the M4 build wrote kept the channel's ids — move them aside once the session is known (the token still
    /// decides whether a marker applies). The marker is the server's 410 for that very profile; asking again is not needed.
    /// </summary>
    public static void TransferLegacyMarkers(Transform botsRoot, InstallSession.TokenClaims claims)
    {
        if (botsRoot == null || claims == null) return;
        for (int i = 0; i < botsRoot.childCount; i++)
        {
            var bot = botsRoot.GetChild(i).GetComponent<Bot>();
            if (bot == null) continue;
            foreach (ChatChannel channel in new[] { ChatChannel.WhatsApp, ChatChannel.Telegram })
            {
                RetiredChannelPolicy.Channel state = Load(bot, channel);
                if (RetiredChannelPolicy.NeedsMoveAside(state, claims))
                    Mark(bot, channel, state.ProfileId, state.RetiredReason);
            }
        }
    }

    /// <summary>The profile id the channel was marked retired for ("" = never).</summary>
    public static string MarkerId(string botKey, ChatChannel channel) =>
        string.IsNullOrEmpty(botKey) ? "" : PlayerPrefs.GetString($"{botKey}{ProfileIdSuffix(channel)}", "");

    public static string Reason(string botKey, ChatChannel channel) =>
        string.IsNullOrEmpty(botKey) ? "" : PlayerPrefs.GetString($"{botKey}{ReasonSuffix(channel)}", "");

    /// <summary>The clone the channel had when it was retired (the sentinel = none).</summary>
    public static string RetiredWorkflowId(string botKey, ChatChannel channel) =>
        string.IsNullOrEmpty(botKey) ? Bot.UnauthedProfileSentinel
            : PlayerPrefs.GetString($"{botKey}{WorkflowIdSuffix(channel)}", Bot.UnauthedProfileSentinel);

    /// <summary>
    /// The reason to show for this channel ("" when the server named none), or null when the channel is not retired —
    /// <see cref="RetiredChannelPolicy.ActiveReason"/> over the channel's CURRENT profile id <paramref name="profileId"/>.
    /// </summary>
    public static string ActiveReason(string botKey, ChatChannel channel, string profileId, InstallSession.TokenClaims claims) =>
        RetiredChannelPolicy.ActiveReason(new RetiredChannelPolicy.Channel
        {
            ProfileId = profileId,
            RetiredProfileId = MarkerId(botKey, channel),
            RetiredReason = Reason(botKey, channel),
        }, claims);

    /// <summary>Create's <c>PreviousProfileId</c> for the channel's new profile ("" = none).</summary>
    public static string PreviousProfileId(Bot bot, ChatChannel channel, string newProfileId) =>
        bot == null ? "" : RetiredChannelPolicy.PreviousProfileId(Load(bot, channel), newProfileId);

    /// <summary>Every id a server deletion of this bot works from: the current ones and the retired set of each channel.</summary>
    public static BotServerDeletion.BotServerIds ServerIdsOf(Bot bot)
    {
        if (bot == null) return default;
        return new BotServerDeletion.BotServerIds(
            new BotServerDeletion.ChannelServerIds(bot.whatsappProfileId, bot.whatsappWorkflowId,
                MarkerId(bot.name, ChatChannel.WhatsApp), RetiredWorkflowId(bot.name, ChatChannel.WhatsApp)),
            new BotServerDeletion.ChannelServerIds(bot.telegramProfileId, bot.telegramWorkflowId,
                MarkerId(bot.name, ChatChannel.Telegram), RetiredWorkflowId(bot.name, ChatChannel.Telegram)));
    }

    /// <summary>Deletes every retired-set key of the bot (the bot is being deleted).</summary>
    public static void Clear(string botKey)
    {
        if (string.IsNullOrEmpty(botKey)) return;
        foreach (var suffix in KeySuffixes)
            PlayerPrefs.DeleteKey($"{botKey}{suffix}");
    }

    /// <summary>The channel as the device holds it: the Bot's live fields for the current ids, PlayerPrefs for the rest.</summary>
    private static RetiredChannelPolicy.Channel Load(Bot bot, ChatChannel channel)
    {
        bool telegram = channel == ChatChannel.Telegram;
        return new RetiredChannelPolicy.Channel
        {
            ProfileId = telegram ? bot.telegramProfileId : bot.whatsappProfileId,
            WorkflowId = telegram ? bot.telegramWorkflowId : bot.whatsappWorkflowId,
            IsOn = PlayerPrefs.GetInt($"{bot.name}isOn{ChannelWord(channel)}", 1) == 1,
            RetiredProfileId = MarkerId(bot.name, channel),
            RetiredWorkflowId = PlayerPrefs.GetString($"{bot.name}{WorkflowIdSuffix(channel)}", ""),
            RetiredReason = Reason(bot.name, channel),
        };
    }

    private static void Apply(Bot bot, ChatChannel channel, RetiredChannelPolicy.Channel before, RetiredChannelPolicy.Channel after)
    {
        if (before.Equals(after)) return;                     // a stale or repeated answer: nothing to write
        Store(bot, channel, before, after);
        if (before.IsOn && !after.IsOn) SwitchSettingsRowOff(bot, channel);
        Changed?.Invoke(bot.name);
    }

    /// <summary>
    /// The ONE place a channel's ids are written for a retirement: the Bot's fields and their PlayerPrefs copies, the
    /// channel switch, the retired set — only what changed (an absent key keeps its caller-specific default), one Save.
    /// </summary>
    private static void Store(Bot bot, ChatChannel channel, RetiredChannelPolicy.Channel before, RetiredChannelPolicy.Channel after)
    {
        string key = bot.name;
        string word = ChannelWord(channel);
        if (after.ProfileId != before.ProfileId || after.WorkflowId != before.WorkflowId)
        {
            if (channel == ChatChannel.Telegram)
            {
                bot.telegramProfileId = after.ProfileId;
                bot.telegramWorkflowId = after.WorkflowId;
            }
            else
            {
                bot.whatsappProfileId = after.ProfileId;
                bot.whatsappWorkflowId = after.WorkflowId;
            }
            PlayerPrefs.SetString($"{key}{word}ProfileId", after.ProfileId);
            PlayerPrefs.SetString($"{key}{word}WorkflowId", after.WorkflowId);
        }
        if (after.IsOn != before.IsOn) PlayerPrefs.SetInt($"{key}isOn{word}", after.IsOn ? 1 : 0);
        if (string.IsNullOrEmpty(after.RetiredProfileId))
        {
            PlayerPrefs.DeleteKey($"{key}{ProfileIdSuffix(channel)}");
            PlayerPrefs.DeleteKey($"{key}{ReasonSuffix(channel)}");
            PlayerPrefs.DeleteKey($"{key}{WorkflowIdSuffix(channel)}");
        }
        else
        {
            PlayerPrefs.SetString($"{key}{ProfileIdSuffix(channel)}", after.RetiredProfileId);
            PlayerPrefs.SetString($"{key}{ReasonSuffix(channel)}", after.RetiredReason ?? "");
            if (string.IsNullOrEmpty(after.RetiredWorkflowId)) PlayerPrefs.DeleteKey($"{key}{WorkflowIdSuffix(channel)}");
            else PlayerPrefs.SetString($"{key}{WorkflowIdSuffix(channel)}", after.RetiredWorkflowId);
        }
        PlayerPrefs.Save();
    }

    /// <summary>The paired settings screen (same sibling index) shows the switched-off channel without re-entering auth.</summary>
    private static void SwitchSettingsRowOff(Bot bot, ChatChannel channel)
    {
        GameObject parent = Manager.BotSettingsParentStatic;
        int index = bot.transform.GetSiblingIndex();
        if (parent == null || bot.transform.parent == null || index >= parent.transform.childCount) return;
        var settings = parent.transform.GetChild(index).GetComponent<BotSettings>();
        if (settings == null) return;
        var row = channel == ChatChannel.Telegram ? settings.telegramRow : settings.whatsappRow;
        if (row != null) row.SetIsOnQuiet(false);
    }
}

/// <summary>
/// RU copy for a retired channel on the bot card's subline (M4). Pure seam — registered in FontGlyphCoverageTests, so
/// every glyph exists in the static SF Pro atlases («·» yes; «№», «−», «✕», «→» never).
/// </summary>
public static class ProfileRetirementCopy
{
    public const string TrialEnded = "Отключён · пробный период закончился";
    public const string SubscriptionEnded = "Отключён · подписка закончилась";
    public const string ChannelOff = "Канал отключён";
    public const string DeleteUnfinished = "Удаление не завершено · удалите ещё раз";

    /// <summary>The subline for a server reason; liveness, channel_limit, an unknown or no reason → «Канал отключён».</summary>
    public static string SublineFor(string reason) => reason switch
    {
        ProfileRetirement.ReasonTrialExpiry => TrialEnded,
        ProfileRetirement.ReasonChurnGrace => SubscriptionEnded,
        ProfileRetirement.ReasonAppDelete => DeleteUnfinished,
        _ => ChannelOff,
    };
}

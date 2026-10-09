using UnityEngine;

/// <summary>
/// M4 (follow-ups 2026-10-09): the device's record that the server retired one channel of a bot — the Profile
/// Lifecycle Sweep removed it (trial over, subscription lapsed, gone from Wappi), an app delete stopped half way, or the
/// channel was refused. The server says so once, on claim.start (410 <c>profile_retired</c>); the boot claim loop then
/// marks the channel here, never asks that profile again, and the bot card says the channel is off.
///
/// Keys follow the per-bot convention (bot-persistence): <c>{bot}{Whatsapp|Telegram}Retired{ProfileId|Reason}</c>,
/// prefixed with the bot GameObject's name; <see cref="Clear"/> runs in <c>Bot.DeleteBotLocally</c>, the full wipe's
/// <c>PlayerPrefs.DeleteAll</c> covers the rest. The bot's own profile / workflow ids are NEVER touched here: V12 deletes
/// the server traces (memory, history, clone) by them. A marker holds for the id it was written for only, and it yields
/// to the token — a token that lists the profile again (a future F2 revival) wins without any cleanup.
/// </summary>
public static class ProfileRetirement
{
    public const string WhatsappProfileIdSuffix = "WhatsappRetiredProfileId";
    public const string WhatsappReasonSuffix = "WhatsappRetiredReason";
    public const string TelegramProfileIdSuffix = "TelegramRetiredProfileId";
    public const string TelegramReasonSuffix = "TelegramRetiredReason";

    /// <summary>Every marker key suffix — what <see cref="Clear"/> deletes.</summary>
    public static readonly string[] KeySuffixes =
    {
        WhatsappProfileIdSuffix, WhatsappReasonSuffix, TelegramProfileIdSuffix, TelegramReasonSuffix,
    };

    // The server's deleted_reason values (bot_profiles.deleted_reason). Only the row's owner is told one.
    public const string ReasonTrialExpiry = "trial_expiry";
    public const string ReasonChurnGrace = "churn_grace";
    public const string ReasonLiveness = "liveness";
    public const string ReasonAppDelete = "app_delete";
    public const string ReasonChannelLimit = "channel_limit";

    /// <summary>A bot's channel was just marked (argument: the bot key) — its card repaints.</summary>
    public static event System.Action<string> Changed;

    public static string ProfileIdSuffix(ChatChannel channel) =>
        channel == ChatChannel.Telegram ? TelegramProfileIdSuffix : WhatsappProfileIdSuffix;

    public static string ReasonSuffix(ChatChannel channel) =>
        channel == ChatChannel.Telegram ? TelegramReasonSuffix : WhatsappReasonSuffix;

    /// <summary>Remembers that the server retired <paramref name="profileId"/> on this bot's channel (reason may be null).</summary>
    public static void Mark(string botKey, ChatChannel channel, string profileId, string reason)
    {
        if (string.IsNullOrEmpty(botKey) || !ProfileClaimPolicy.IsAuthedId(profileId)) return;
        PlayerPrefs.SetString($"{botKey}{ProfileIdSuffix(channel)}", profileId);
        PlayerPrefs.SetString($"{botKey}{ReasonSuffix(channel)}", reason ?? "");
        PlayerPrefs.Save();
        Changed?.Invoke(botKey);
    }

    /// <summary>The profile id the channel was marked retired for ("" = never).</summary>
    public static string MarkerId(string botKey, ChatChannel channel) =>
        string.IsNullOrEmpty(botKey) ? "" : PlayerPrefs.GetString($"{botKey}{ProfileIdSuffix(channel)}", "");

    public static string Reason(string botKey, ChatChannel channel) =>
        string.IsNullOrEmpty(botKey) ? "" : PlayerPrefs.GetString($"{botKey}{ReasonSuffix(channel)}", "");

    /// <summary>
    /// The reason to show for this channel ("" when the server named none), or null when the channel is not retired:
    /// no marker for THIS profile id, or the token lists the profile again.
    /// </summary>
    public static string ActiveReason(string botKey, ChatChannel channel, string profileId, InstallSession.TokenClaims claims) =>
        ProfileClaimPolicy.RetiredMarkerApplies(MarkerId(botKey, channel), profileId, claims) ? Reason(botKey, channel) : null;

    /// <summary>Deletes every marker key of the bot (the bot is being deleted).</summary>
    public static void Clear(string botKey)
    {
        if (string.IsNullOrEmpty(botKey)) return;
        foreach (var suffix in KeySuffixes)
            PlayerPrefs.DeleteKey($"{botKey}{suffix}");
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

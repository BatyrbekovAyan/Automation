/// <summary>
/// Single source of truth for whether a bot's per-channel n8n workflow should be
/// ACTIVE on the server.
///
/// A channel's automation runs only when BOTH activation gates are on:
///   1. the bot's master switch — the toggle on the bot card
///      («Бот работает» / «Бот на паузе», persisted under the bare bot name key), and
///   2. that channel's own enable toggle — the WhatsApp / Telegram switch in Bot
///      Settings (persisted under "isOnWhatsapp" / "isOnTelegram").
///
/// Before this rule was centralised, the master toggle and each channel toggle
/// drove n8n activate/deactivate independently, so the two gates never AND-ed:
/// turning a channel on while the bot was paused (master off) left the workflow
/// active, and turning the master on activated channels that were toggled off.
/// Every activation site now routes through <see cref="ChannelWorkflowActive"/>.
/// </summary>
public static class BotActivationPolicy
{
    /// <summary>
    /// True when the channel's workflow should be active on n8n: the bot master
    /// switch is on AND that channel's own toggle is on.
    /// </summary>
    public static bool ChannelWorkflowActive(bool masterOn, bool channelOn) => masterOn && channelOn;
}

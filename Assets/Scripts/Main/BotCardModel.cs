/// <summary>
/// Visibility states of a channel brand icon in the C2 bot-card subline
/// (sketch 006, locked 2026-08-13).
/// </summary>
public enum BotChannelIconState
{
    /// <summary>Bot has no profile on this channel — no icon at all.</summary>
    Hidden,

    /// <summary>Channel connected AND enabled — full brand color.</summary>
    Colored,

    /// <summary>Channel connected but toggled off in Bot Settings — InkTertiary gray.</summary>
    Muted,
}

/// <summary>
/// Pure decision seam for the C2 bot card (sketch 006): the channel brand
/// icons and the blinking «Подключение…» subline. No MonoBehaviour so the
/// matrix is EditMode-testable (AutoButtonModel precedent).
///
/// The «Авто» capsule itself needs no seam of its own since the 2026-08-13
/// unification: it drives the bot's ReplyMode — the exact store and confirm
/// asymmetry of the chats-header button, both pinned by
/// <see cref="AutoButtonModel"/> / AutoButtonModelTests. The old master
/// activation key is dead (see BotActivationMigration); workflows stay active
/// per the channel toggles alone.
/// </summary>
public static class BotCardModel
{
    public const string ConnectingText = "Подключение…";

    /// <summary>«Bot is connected to this channel» — a real Wappi profile id
    /// (non-empty, not the unauthed sentinel). Mirrors BotSwitcherRowView.</summary>
    public static bool IsConnected(string profileId) =>
        !string.IsNullOrEmpty(profileId) && profileId != Bot.UnauthedProfileSentinel;

    /// <summary>
    /// The owner-approved three-state rule: no profile → no icon at all;
    /// connected + channel toggle on → brand color; connected + toggle off →
    /// gray («как будто деактивирован»).
    /// </summary>
    public static BotChannelIconState IconState(string profileId, bool channelEnabled)
    {
        if (!IsConnected(profileId)) return BotChannelIconState.Hidden;
        return channelEnabled ? BotChannelIconState.Colored : BotChannelIconState.Muted;
    }

    /// <summary>
    /// Subline text: while a channel is connecting the blinking word replaces
    /// the business type entirely (icons hide too — the sketch's rule).
    /// </summary>
    public static string SublineText(bool connecting, string businessDisplayName) =>
        connecting ? ConnectingText : businessDisplayName ?? "";
}

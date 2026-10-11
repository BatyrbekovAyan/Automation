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

    /// <summary>Channel connected but toggled off in Bot Settings — InkTertiary gray.
    /// Also (M4) a channel the server retired: the profile is gone, the bot cannot answer there.</summary>
    Muted,
}

/// <summary>Which state the C2 subline shows — one at a time, in <see cref="BotCardModel.SublineKind"/>'s priority order.</summary>
public enum BotSublineKind
{
    /// <summary>The business type (the ordinary card).</summary>
    Business,
    /// <summary>A channel is being authorised — the blinking «Подключение…».</summary>
    Connecting,
    /// <summary>M4: the server retired a channel — «Отключён · …» / «Канал отключён» / «Удаление не завершено …».</summary>
    Retired,
    /// <summary>A2: a reply-mode change waits for the server's confirmation.</summary>
    SyncPending,
    /// <summary>A2: the reply-mode change was not saved this launch.</summary>
    SyncFailed,
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
/// activation key is dead; workflows stay active per the channel toggles alone.
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
    public static BotChannelIconState IconState(string profileId, bool channelEnabled) =>
        IconState(profileId, channelEnabled, retired: false);

    /// <summary>
    /// M4: a channel the server retired keeps its icon (the bot still HAS that channel — its ids are what V12 deletes
    /// the server traces by) but never in brand colour: gray, whatever its toggle says. F2 (T8): once its ids are moved
    /// aside the current profile id is the sentinel — the icon stays gray all the same (the plan's card, §5.3); whether
    /// the channel IS retired is <see cref="RetiredChannelPolicy"/>'s call, never this method's.
    /// </summary>
    public static BotChannelIconState IconState(string profileId, bool channelEnabled, bool retired)
    {
        if (retired) return BotChannelIconState.Muted;
        if (!IsConnected(profileId)) return BotChannelIconState.Hidden;
        return channelEnabled ? BotChannelIconState.Colored : BotChannelIconState.Muted;
    }

    /// <summary>
    /// M4: the subline's state, highest first — connecting (an auth in progress owns the card), a retired channel (the
    /// persistent fact that also explains a mode that cannot be saved), then the A2 sync states, then the business type.
    /// </summary>
    public static BotSublineKind SublineKind(bool connecting, bool retired, bool syncPending, bool syncFailed)
    {
        if (connecting) return BotSublineKind.Connecting;
        if (retired) return BotSublineKind.Retired;
        if (syncPending) return BotSublineKind.SyncPending;
        return syncFailed ? BotSublineKind.SyncFailed : BotSublineKind.Business;
    }

    /// <summary>
    /// M4: the subline for the bot's retired channel(s) — null when neither is retired. A reason of "" (the server named
    /// none) still counts as retired. Card order: WhatsApp's reason first, then Telegram's.
    /// </summary>
    public static string RetiredSubline(string whatsappRetiredReason, string telegramRetiredReason)
    {
        string reason = whatsappRetiredReason ?? telegramRetiredReason;
        return reason == null ? null : ProfileRetirementCopy.SublineFor(reason);
    }

    /// <summary>
    /// Subline text: while a channel is connecting the blinking word replaces
    /// the business type entirely (icons hide too — the sketch's rule).
    /// </summary>
    public static string SublineText(bool connecting, string businessDisplayName) =>
        connecting ? ConnectingText : businessDisplayName ?? "";
}

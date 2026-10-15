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
    /// <summary>A4: a connected channel's Wappi status said <c>authorized:false</c> — «Канал отключён · переподключите в настройках».</summary>
    Unauthorized,
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
/// <summary>The capsule's face on the bot card: the authored «Авто», «Подключить» (F2) or «Учится · N/20» (learning B).</summary>
public enum BotPillKind { Auto, Connect, Learning }

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
    public static BotChannelIconState IconState(string profileId, bool channelEnabled, bool retired) =>
        IconState(profileId, channelEnabled, retired, unauthorized: false);

    /// <summary>
    /// A4 (2026-10-14): a connected channel the device marked unauthorised is gray too — the bot cannot answer there, and
    /// on a two-channel card the gray icon says WHICH channel «Канал отключён» means. A mark on a channel without a profile
    /// changes nothing (no icon).
    /// </summary>
    public static BotChannelIconState IconState(string profileId, bool channelEnabled, bool retired, bool unauthorized)
    {
        if (retired) return BotChannelIconState.Muted;
        if (!IsConnected(profileId)) return BotChannelIconState.Hidden;
        return channelEnabled && !unauthorized ? BotChannelIconState.Colored : BotChannelIconState.Muted;
    }

    /// <summary>
    /// M4: the subline's state, highest first — connecting (an auth in progress owns the card), a retired channel (the
    /// persistent fact that also explains a mode that cannot be saved), then the A2 sync states, then the business type.
    /// </summary>
    public static BotSublineKind SublineKind(bool connecting, bool retired, bool syncPending, bool syncFailed) =>
        SublineKind(connecting, retired, unauthorized: false, syncPending, syncFailed);

    /// <summary>
    /// A4 (2026-10-14): a connected channel the device marked unauthorised (<see cref="ChannelHealth.CardShowsUnauthorized"/>)
    /// ranks right below a retired one — the server's verdict about a channel outranks the device's own reading — and above
    /// the A2 sync states (a mode cannot be confirmed through a channel that is off either).
    /// </summary>
    public static BotSublineKind SublineKind(bool connecting, bool retired, bool unauthorized, bool syncPending, bool syncFailed)
    {
        if (connecting) return BotSublineKind.Connecting;
        if (retired) return BotSublineKind.Retired;
        if (unauthorized) return BotSublineKind.Unauthorized;
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

    // ---------------- T9: «Подключить» in place of «Авто» ----------------

    /// <summary>The capsule's authored width (BotCardAutoPillBuilder) — «Авто» with its lamp.</summary>
    public const float AutoPillWidth = 190f;

    /// <summary>Side padding of «Подключить» inside the capsule — the pill's own 76u height keeps it a full pill.</summary>
    public const float ConnectPillPadding = 28f;

    /// <summary>
    /// F2 (plan §5.3): the capsule turns into «Подключить» only when the bot has NO live channel and at least one the
    /// server retired — the reply mode it would switch has nothing to act on. A retired channel's reason is non-null
    /// (<see cref="RetiredChannelPolicy.ActiveReason"/>, "" when the server named none); a channel with a real profile and no
    /// reason is live. An unfinished app delete anywhere on the bot keeps «Авто»: its screens only offer «Удалить ещё раз».
    /// </summary>
    public static bool ShowConnectPill(string whatsappProfileId, string whatsappRetiredReason,
                                       string telegramProfileId, string telegramRetiredReason)
    {
        if (whatsappRetiredReason == null && telegramRetiredReason == null) return false;
        if (whatsappRetiredReason == ProfileRetirement.ReasonAppDelete
            || telegramRetiredReason == ProfileRetirement.ReasonAppDelete) return false;
        bool whatsappLive = whatsappRetiredReason == null && IsConnected(whatsappProfileId);
        bool telegramLive = telegramRetiredReason == null && IsConnected(telegramProfileId);
        return !whatsappLive && !telegramLive;
    }

    /// <summary>Learning B: which face the capsule wears. A retired channel outranks everything but «Подключение…»; the lock
    /// paints only a bot in «Вместе» the server says is not ready — an enabled «Авто» is never relabelled.</summary>
    public static BotPillKind PillKind(bool connect, bool autoOn, bool locked)
    {
        if (connect) return BotPillKind.Connect;
        if (!autoOn && locked) return BotPillKind.Learning;
        return BotPillKind.Auto;
    }

    /// <summary>Measured width for every face but the authored «Авто» (the lamp is hidden, the label is alone in the pill).</summary>
    public static float PillWidth(BotPillKind kind, float labelPreferredWidth) =>
        PillWidth(kind != BotPillKind.Auto, labelPreferredWidth);

    /// <summary>
    /// The capsule's width: the authored 190 for «Авто»; for «Подключить» its measured label plus the padding on both
    /// sides, never narrower than the authored capsule (the lamp is hidden, so the label is alone in the pill).
    /// </summary>
    public static float PillWidth(bool connect, float labelPreferredWidth) =>
        connect ? System.Math.Max(AutoPillWidth, (float)System.Math.Ceiling(labelPreferredWidth) + 2f * ConnectPillPadding)
                : AutoPillWidth;
}

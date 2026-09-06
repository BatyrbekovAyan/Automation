/// <summary>
/// The four mutually-exclusive states of a channel's tab content area.
/// Channel-neutral core (WhatsApp/Telegram share the same precedence).
/// </summary>
public enum ChannelTabState
{
    NoBots,       // No bots exist at all
    NoConnection, // Active bot exists but has no profile for this channel
    Syncing,      // Active bot connected, still inside the fixed sync window
    Ready,        // Show the chat list
}

/// <summary>
/// Channel-neutral precedence resolver. Order matters:
/// NoBots → NoConnection → Syncing → Ready. Pure function.
/// </summary>
public static class ChannelTabStateResolver
{
    public static ChannelTabState Resolve(int botCount, bool activeBotHasChannel, bool isSyncing)
    {
        if (botCount <= 0) return ChannelTabState.NoBots;
        if (!activeBotHasChannel) return ChannelTabState.NoConnection;
        if (isSyncing) return ChannelTabState.Syncing;
        return ChannelTabState.Ready;
    }
}

/// <summary>
/// Pure NoBots-coercion for the empty-state card (D12-ext / 08-REVIEW CR-01). BeginLoadForActiveBot
/// fires BotHasNo{Channel} even when ZERO bots exist; that wrong reason re-wires the create-bot CTA
/// to OpenCurrentBotAuth (a silent no-op with no bot). This promotes such a raw reason back to
/// NoBotsExist ONLY when the authoritative resolver (ComputeCurrentEmptyState) also says NoBots —
/// so a genuine connect card for a real bot is preserved byte-identically (WhatsApp invariant).
/// </summary>
public static class EmptyStateReasonPolicy
{
    public static EmptyStateReason Effective(EmptyStateReason raw, EmptyStateReason? resolved) =>
        (raw != EmptyStateReason.NoBotsExist && resolved == EmptyStateReason.NoBotsExist)
            ? EmptyStateReason.NoBotsExist
            : raw;
}

/// <summary>What the empty-state card must do after a re-derive from the authoritative resolver.</summary>
public enum EmptyStateCardAction
{
    None, // nothing on screen and nothing to raise — leave the card alone
    Show, // (re)configure for the resolved reason and raise the card
    Hide, // the resolver says there is no empty-card state — take whatever is showing off screen
}

/// <summary>
/// Pure re-derive rule for the empty-state card (2026-09-04 stale-card fix). The card is a
/// CanvasGroup whose alpha SURVIVES its GameObject being deactivated, while every event that
/// would correct it (bot created, channel authorised, chats loaded) fires while the chats
/// screen is inactive and the view is unsubscribed. So on every re-derive the ONE authority is
/// <see cref="ChatManager.ComputeCurrentEmptyState"/>: a reason means show that card, and null
/// (Syncing / Ready — "not an empty-card state") means hide whatever is on screen, no matter
/// how many chats are loaded. The chat count is deliberately NOT an input: keying the hide on
/// it left a just-created bot (empty list inside its 300s sync window, or an account with
/// genuinely zero chats) showing the stale «Создайте первого бота» card — opaque, full-stretch
/// and swallowing taps over the chat list — until a WhatsApp→Telegram→WhatsApp round trip
/// happened to re-derive it. Same invariant SyncingView.OnEnable already carries for the
/// sibling cover under the same parent (pinned by SyncingViewLifecycleTests).
///
/// Show is returned even when that reason is already the one showing: the re-derive sites are
/// also the channel-switch sites, where the card keeps its reason but must re-theme (the
/// Telegram accent) and re-wire its CTA. Suppressing duplicate work is the EVENT path's job
/// (EmptyStateView.HandleEmptyState's _lastReason guard), never this one's.
/// </summary>
public static class EmptyStateCardPolicy
{
    public static EmptyStateCardAction Decide(EmptyStateReason? resolved, bool cardVisible)
    {
        if (resolved.HasValue) return EmptyStateCardAction.Show;
        return cardVisible ? EmptyStateCardAction.Hide : EmptyStateCardAction.None;
    }
}

/// <summary>The four mutually-exclusive states of the WhatsApp tab content area.</summary>
public enum WhatsAppTabState
{
    NoBots,     // No bots exist at all
    NoWhatsApp, // Active bot exists but has no WhatsApp profile
    Syncing,    // Active bot connected, still inside the fixed sync window
    Ready,      // Show the chat list
}

/// <summary>
/// Pure precedence resolver for the WhatsApp tab. Order matters.
/// Delegates to the channel-neutral <see cref="ChannelTabStateResolver"/> and maps
/// its result onto the WhatsApp enum (NoConnection => NoWhatsApp). Kept as a wrapper
/// so existing call sites and WhatsAppTabStateResolverTests don't churn.
/// </summary>
public static class WhatsAppTabStateResolver
{
    public static WhatsAppTabState Resolve(int botCount, bool activeBotHasWhatsApp, bool isSyncing) =>
        ChannelTabStateResolver.Resolve(botCount, activeBotHasWhatsApp, isSyncing) switch
        {
            ChannelTabState.NoBots       => WhatsAppTabState.NoBots,
            ChannelTabState.NoConnection => WhatsAppTabState.NoWhatsApp,
            ChannelTabState.Syncing      => WhatsAppTabState.Syncing,
            _                            => WhatsAppTabState.Ready,
        };
}

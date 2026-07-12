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

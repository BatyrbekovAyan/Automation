/// <summary>The four mutually-exclusive states of the WhatsApp tab content area.</summary>
public enum WhatsAppTabState
{
    NoBots,     // No bots exist at all
    NoWhatsApp, // Active bot exists but has no WhatsApp profile
    Syncing,    // Active bot connected, still inside the fixed sync window
    Ready,      // Show the chat list
}

/// <summary>Pure precedence resolver for the WhatsApp tab. Order matters.</summary>
public static class WhatsAppTabStateResolver
{
    public static WhatsAppTabState Resolve(int botCount, bool activeBotHasWhatsApp, bool isSyncing)
    {
        if (botCount <= 0) return WhatsAppTabState.NoBots;
        if (!activeBotHasWhatsApp) return WhatsAppTabState.NoWhatsApp;
        if (isSyncing) return WhatsAppTabState.Syncing;
        return WhatsAppTabState.Ready;
    }
}

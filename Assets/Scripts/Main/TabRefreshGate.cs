/// <summary>
/// Pure decision helper for the bottom-nav chat-list refresh trigger. Kept
/// separate from BottomTabManager so the rule is unit-testable without a scene.
/// </summary>
public static class TabRefreshGate
{
    /// <summary>
    /// True when switching to <paramref name="newIndex"/> should quietly refresh
    /// the chat list: it must be the WhatsApp tab, and must not be the initial
    /// startup selection (ChatManager runs its own first load on launch).
    /// </summary>
    public static bool ShouldRefreshChats(int newIndex, bool isInitialSelection, int whatsAppTabIndex)
        => newIndex == whatsAppTabIndex && !isInitialSelection;
}

/// <summary>
/// Pure, UnityEngine-free minimum-visible-duration decision for the Telegram chat-list "syncing"
/// pill (D9). The pill (<see cref="ChatListSyncIndicator"/>) is driven by ChatManager's
/// OnChatListSyncStart/OnChatListSyncEnd, but a normal (fast) Telegram list load flashes it for
/// only the chats/filter network round-trip — a sub-legible blink the owner never registers, so
/// "no cue shows". This gate holds the pill up for a legible floor even when the sync settles
/// faster than that: once shown it stays for at least <see cref="MinVisibleSeconds"/>, then hides
/// only when the sync is genuinely settled. Side-effect-free and UnityEngine-free so it is fully
/// EditMode-testable without a scene (mirrors <see cref="OpenChatLivePollGate"/>).
/// </summary>
public static class ChatListSyncIndicatorGate
{
    /// <summary>
    /// The legible floor, in seconds, the pill stays visible once shown — even if the sync ends
    /// almost immediately. 0.6s reads as a deliberate cue (long enough to notice a spinner) while
    /// staying out of the way; raise it for a more insistent cue, lower it to blink sooner.
    /// </summary>
    public const float MinVisibleSeconds = 0.6f;

    // RED stub — intentionally wrong; the real logic lands in the GREEN commit.
    public static float RemainingVisibleSeconds(float shownAtRealtime, float nowRealtime, float minVisible)
        => minVisible;

    public static bool ShouldHideNow(float shownAtRealtime, float nowRealtime, float minVisible, bool syncStillInFlight)
        => false;
}

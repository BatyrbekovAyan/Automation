/// <summary>
/// Pure, UnityEngine-free decision for WHEN the open-chat live poll may fire (D5 gap-closure).
/// Before this seam the open chat's messages were synced exactly once (at chat-open, inside
/// OpenChatRoutine → SyncLatestMessages); nothing re-fetched them on a cadence, so a message
/// arriving while the chat stayed open never rendered until the owner re-entered it — and the
/// «Вместе» suggestions payload (built from the same _activeChatCache) stayed stale too.
/// This gate lets a single repeating coroutine decide, cheaply, when to re-issue that one-shot
/// sync. Kept side-effect-free and UnityEngine-free so it is fully EditMode-testable (mirrors
/// ChannelTabStateResolver / WhatsAppSyncGate).
/// </summary>
public static class OpenChatLivePollGate
{
    /// <summary>
    /// The open-chat refresh cadence, in seconds — the tunable "one refresh cycle" for D5. A
    /// brand-new incoming message renders within this window without re-entering the chat.
    /// 3s balances perceived liveness against battery drain and Wappi/tapi request pressure;
    /// raise it to poll less aggressively, lower it for snappier liveness.
    /// </summary>
    public const float IntervalSeconds = 3f;

    /// <summary>
    /// True when an open-chat live poll is allowed to fire. Every condition must hold:
    /// <list type="bullet">
    /// <item><paramref name="chatIsOpen"/> — a chat is open and on-screen.</item>
    /// <item><paramref name="appFocused"/> — the app is foregrounded (a backgrounded app must
    /// not poll: battery, and the pairing-code flow deliberately leaves the app).</item>
    /// <item>not <paramref name="fetchInFlight"/> — no messages/get is already in flight; the
    /// serial-fetch invariant (Wappi/tapi cross concurrent same-endpoint responses).</item>
    /// <item><paramref name="chatOpenSettled"/> — the chat-open state machine has settled
    /// (Idle, not sliding), so a poll never lands mid-open or mid-slide.</item>
    /// <item><paramref name="secondsSinceLastPoll"/> ≥ <see cref="IntervalSeconds"/> — the
    /// min-interval throttle.</item>
    /// </list>
    /// Channel-agnostic on purpose: D5 reproduces on BOTH WhatsApp and Telegram, so there is no
    /// ChatChannel parameter and no per-channel branch.
    /// </summary>
    public static bool ShouldIssue(
        bool chatIsOpen,
        bool appFocused,
        bool fetchInFlight,
        bool chatOpenSettled,
        float secondsSinceLastPoll)
        => chatIsOpen
           && appFocused
           && !fetchInFlight
           && chatOpenSettled
           && secondsSinceLastPoll >= IntervalSeconds;
}

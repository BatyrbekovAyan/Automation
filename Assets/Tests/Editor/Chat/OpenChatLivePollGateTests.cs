using NUnit.Framework;

// D5 gap-closure: the pure decision for WHEN an open-chat live poll may fire. The poll must
// only run when a chat is open, the app is foregrounded, no messages/get is already in flight
// (serial-fetch invariant), the chat-open state machine has settled, and at least
// IntervalSeconds have elapsed since the previous poll. Channel-agnostic — D5 is cross-channel,
// so the gate deliberately takes no ChatChannel. Mirrors the pure-seam NUnit style of
// ChannelTabStateResolverTests (no scene, no MonoBehaviour).
public class OpenChatLivePollGateTests
{
    private const float Interval = OpenChatLivePollGate.IntervalSeconds;

    [Test] public void Fires_WhenAllConditionsHold()
        => Assert.IsTrue(OpenChatLivePollGate.ShouldIssue(
            chatIsOpen: true, appFocused: true, fetchInFlight: false,
            chatOpenSettled: true, secondsSinceLastPoll: Interval + 1f));

    [Test] public void Blocked_WhenFetchInFlight()
        => Assert.IsFalse(OpenChatLivePollGate.ShouldIssue(
            chatIsOpen: true, appFocused: true, fetchInFlight: true,
            chatOpenSettled: true, secondsSinceLastPoll: Interval + 1f));

    [Test] public void Blocked_WhenNotSettled()
        => Assert.IsFalse(OpenChatLivePollGate.ShouldIssue(
            chatIsOpen: true, appFocused: true, fetchInFlight: false,
            chatOpenSettled: false, secondsSinceLastPoll: Interval + 1f));

    [Test] public void Blocked_WhenNotFocused()
        => Assert.IsFalse(OpenChatLivePollGate.ShouldIssue(
            chatIsOpen: true, appFocused: false, fetchInFlight: false,
            chatOpenSettled: true, secondsSinceLastPoll: Interval + 1f));

    [Test] public void Blocked_WhenNoChatOpen()
        => Assert.IsFalse(OpenChatLivePollGate.ShouldIssue(
            chatIsOpen: false, appFocused: true, fetchInFlight: false,
            chatOpenSettled: true, secondsSinceLastPoll: Interval + 1f));

    [Test] public void Blocked_WhenBelowInterval()
        => Assert.IsFalse(OpenChatLivePollGate.ShouldIssue(
            chatIsOpen: true, appFocused: true, fetchInFlight: false,
            chatOpenSettled: true, secondsSinceLastPoll: Interval - 0.5f));

    // "One refresh cycle" is inclusive of the boundary — >= IntervalSeconds fires.
    [Test] public void Fires_ExactlyAtIntervalBoundary()
        => Assert.IsTrue(OpenChatLivePollGate.ShouldIssue(
            chatIsOpen: true, appFocused: true, fetchInFlight: false,
            chatOpenSettled: true, secondsSinceLastPoll: Interval));
}

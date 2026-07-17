using NUnit.Framework;

// D9 gap-closure: the pure minimum-visible-duration decision for the Telegram chat-list sync pill.
// A fast Telegram list load fires OnChatListSyncStart then OnChatListSyncEnd only a network
// round-trip apart, flashing the pill sub-legibly ("no cue shows" on device). This gate holds the
// pill for a legible floor (MinVisibleSeconds) once shown, then permits a hide only when the floor
// has elapsed AND the sync is genuinely settled. Pure, UnityEngine-free — no scene, no
// MonoBehaviour — mirroring OpenChatLivePollGateTests.
public class ChatListSyncIndicatorGateTests
{
    private const float Min = ChatListSyncIndicatorGate.MinVisibleSeconds;

    // A hide is never permitted while the sync is still in flight, even long after the floor.
    [Test]
    public void Hide_Blocked_WhileSyncStillInFlight()
        => Assert.IsFalse(ChatListSyncIndicatorGate.ShouldHideNow(
            shownAtRealtime: 0f, nowRealtime: 10f, minVisible: Min, syncStillInFlight: true));

    // Even after the sync settled, a hide is blocked until the legible floor has elapsed.
    [Test]
    public void Hide_Blocked_BeforeMinVisibleElapsed_EvenAfterSyncEnded()
        => Assert.IsFalse(ChatListSyncIndicatorGate.ShouldHideNow(
            shownAtRealtime: 0f, nowRealtime: Min * 0.5f, minVisible: Min, syncStillInFlight: false));

    // Once the floor has elapsed and the sync is settled, the hide is allowed.
    [Test]
    public void Hide_Allowed_OnceElapsedAndSettled()
        => Assert.IsTrue(ChatListSyncIndicatorGate.ShouldHideNow(
            shownAtRealtime: 0f, nowRealtime: Min + 1f, minVisible: Min, syncStillInFlight: false));

    // The floor is inclusive: at exactly MinVisibleSeconds the remaining time is 0 → hide allowed.
    [Test]
    public void Hide_Allowed_ExactlyAtBoundary()
        => Assert.IsTrue(ChatListSyncIndicatorGate.ShouldHideNow(
            shownAtRealtime: 0f, nowRealtime: Min, minVisible: Min, syncStillInFlight: false));

    // Remaining time clamps at 0 once the floor is exceeded (never negative).
    [Test]
    public void RemainingVisibleSeconds_ClampsAtZero()
        => Assert.AreEqual(0f, ChatListSyncIndicatorGate.RemainingVisibleSeconds(
            shownAtRealtime: 0f, nowRealtime: Min + 1f, minVisible: Min), 1e-4f);

    // Before the floor elapses, the remaining time is the un-elapsed portion.
    [Test]
    public void RemainingVisibleSeconds_PositiveBeforeElapsed()
        => Assert.AreEqual(Min * 0.75f, ChatListSyncIndicatorGate.RemainingVisibleSeconds(
            shownAtRealtime: 0f, nowRealtime: Min * 0.25f, minVisible: Min), 1e-4f);
}

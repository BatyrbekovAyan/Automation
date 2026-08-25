using NUnit.Framework;

/// <summary>
/// Truth table for the launch-time trial backfill (Task 15b). Two inputs, one output —
/// pinned here rather than re-derived inside Manager's boot coroutine, which EditMode
/// cannot instantiate.
/// </summary>
public class LaunchTrialBackfillTests
{
    [Test]
    public void A_pre_ledger_install_with_a_connected_channel_starts_its_clock()
    {
        // The whole reason this exists: bots authed before Task 15a shipped carry no
        // TrialStartedUtc, so IsExpired (HasStarted && DaysLeft() <= 0) is false forever.
        Assert.IsTrue(LaunchTrialBackfill.ShouldBackfill(hasStarted: false, connectedChannels: 1));
    }

    [Test]
    public void An_install_with_no_connected_channel_is_left_alone()
    {
        // Not a backfill case — a fresh install must reach its first auth with the full
        // trial ahead of it, which is what Task 15a's auth-time StartIfNeeded gives it.
        Assert.IsFalse(LaunchTrialBackfill.ShouldBackfill(hasStarted: false, connectedChannels: 0));
    }

    [Test]
    public void A_ledger_that_already_exists_is_never_restarted()
    {
        // hasStarted is checked first and outranks the channel count, so disconnecting and
        // reconnecting a channel can never hand out a second, fresh trial.
        Assert.IsFalse(LaunchTrialBackfill.ShouldBackfill(hasStarted: true, connectedChannels: 2));
        Assert.IsFalse(LaunchTrialBackfill.ShouldBackfill(hasStarted: true, connectedChannels: 0));
    }
}

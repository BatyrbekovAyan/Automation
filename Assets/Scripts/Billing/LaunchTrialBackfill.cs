/// <summary>
/// The launch-time trial backfill (Task 15b): should THIS launch start the trial clock
/// for an install that already has a connected channel but no ledger entry?
///
/// <para>The trial clock is normally started at the first channel auth
/// (<see cref="TrialLedger.StartIfNeeded"/> from the auth success path, Task 15a). Every
/// install that authed a channel BEFORE that shipped therefore carries bots and Wappi
/// profiles with no <c>TrialStartedUtc</c> at all — a trial that never starts and, because
/// <see cref="TrialLedger.IsExpired"/> is <c>HasStarted &amp;&amp; DaysLeft() &lt;= 0</c>,
/// never expires either. Left alone, those installs sit on an unlimited CLIENT-side free
/// tier forever: nothing ever moves them off Trial, so no launch ever shows the expiry
/// paywall and no client gate ever refuses them. Owner-approved default: start the clock
/// at the next launch instead.</para>
///
/// <para>Client-side only, deliberately. The server-side Profile Lifecycle Sweep has its
/// own independent clock — <c>bot_profiles.created_at</c> — and nothing server-side ever
/// reads <c>TrialStartedUtc</c>, so this ledger being absent never affected the sweep and
/// writing it now does not change what the sweep does.</para>
///
/// <para>A pure seam for the same reason as <see cref="LaunchPaywallPolicy"/>: the caller
/// lives inside a Manager coroutine, which EditMode cannot instantiate, so the rule is
/// pinned by a table rather than trusted by inspection.</para>
/// </summary>
public static class LaunchTrialBackfill
{
    /// <summary>
    /// True only when the ledger has never been written AND the install already has at
    /// least one connected channel.
    ///
    /// <para>The channel count is what keeps this from being a blanket
    /// «start everyone's trial at launch»: a fresh install with no channel yet must reach
    /// its first auth with the full trial ahead of it, exactly as Task 15a intended — that
    /// install is not a backfill case, it is the normal case, and starting its clock here
    /// would silently burn days before the user has a working bot.</para>
    ///
    /// <para><paramref name="hasStarted"/> is checked first and is the durable half: once
    /// the ledger exists this can never fire again, so a user who later disconnects every
    /// channel does not get a second, fresh trial.</para>
    /// </summary>
    public static bool ShouldBackfill(bool hasStarted, int connectedChannels)
    {
        if (hasStarted) return false;
        return connectedChannels > 0;
    }
}

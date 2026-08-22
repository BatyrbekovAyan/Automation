using System;
using NUnit.Framework;

/// <summary>
/// Pins <see cref="UsageClient.ShouldStart"/> — the whole decision the usage fetch makes
/// before it touches the network (Task 14c carry-over).
///
/// The in-flight arm collapses a burst of the five trigger sites to one request. The
/// staleness arm is the fallback for the case the routine's own try/finally cannot reach:
/// an iterator that is never disposed strands the flag, and without an age check that would
/// freeze the usage strip and the «Подписка» meters for the rest of the session.
/// </summary>
public class UsageClientTests
{
    // The flag lives on a static, so a run that abandoned a fetch would otherwise leak
    // «in flight» into every test after it.
    [TearDown]
    public void Reset() => UsageClient.ResetSeamsForTests();

    private static readonly DateTime Started = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void An_idle_client_always_starts()
    {
        // The stamp is meaningless while nothing is running — including its default(DateTime).
        Assert.IsTrue(UsageClient.ShouldStart(inFlight: false, Started, Started));
        Assert.IsTrue(UsageClient.ShouldStart(inFlight: false, default, Started));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(29)]
    public void A_live_request_refuses_a_second_one(int secondsElapsed)
        => Assert.IsFalse(UsageClient.ShouldStart(true, Started, Started.AddSeconds(secondsElapsed)));

    [Test]
    public void At_exactly_the_request_timeout_the_marker_is_debris()
    {
        // The request cannot outlive its own timeout, so the flag is no longer describing
        // anything real — the boundary belongs to the stale side.
        Assert.IsTrue(UsageClient.ShouldStart(true, Started,
            Started.AddSeconds(UsageClient.RequestTimeoutSeconds)));
    }

    [Test]
    public void A_long_stranded_marker_never_freezes_usage_for_the_session()
        => Assert.IsTrue(UsageClient.ShouldStart(true, Started, Started.AddHours(3)));

    [Test]
    public void A_rolled_back_clock_still_refuses()
    {
        // Negative age. Refusing costs one skipped refresh; starting would duplicate the
        // request and let the older response land last, which is the failure the guard exists
        // for in the first place.
        Assert.IsFalse(UsageClient.ShouldStart(true, Started, Started.AddDays(-1)));
    }

    [Test]
    public void The_staleness_window_is_the_request_timeout_itself()
    {
        // Single-sourced on purpose: FetchRoutine stamps req.timeout from this same constant,
        // so the two can never drift into a window where a live request looks like debris.
        Assert.AreEqual(30, UsageClient.RequestTimeoutSeconds);
    }
}

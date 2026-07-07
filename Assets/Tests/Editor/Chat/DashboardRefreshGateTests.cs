using NUnit.Framework;

public class DashboardRefreshGateTests
{
    [Test] public void FetchesWhenNeverFetched()
        => Assert.IsTrue(DashboardRefreshGate.ShouldFetch(0, 1_000_000));

    [Test] public void SkipsWithinInterval()
        => Assert.IsFalse(DashboardRefreshGate.ShouldFetch(1_000_000, 1_030_000)); // 30s < 60s

    [Test] public void FetchesAfterInterval()
        => Assert.IsTrue(DashboardRefreshGate.ShouldFetch(1_000_000, 1_061_000));  // 61s > 60s
}

/// <summary>Pure time throttle for dashboard fetches (in the mold of TabRefreshGate,
/// which has no time component — this is a separate helper).</summary>
public static class DashboardRefreshGate
{
    public static bool ShouldFetch(long lastFetchMs, long nowMs, long minIntervalMs = 60_000)
        => nowMs - lastFetchMs >= minIntervalMs;
}

using System.Collections.Generic;
using System.Linq;

public enum DashboardPeriod { Today, Week, Month }

public struct Window
{
    public long CurStart, CurEnd, PrevStart, PrevEnd;
}

public static class DashboardMetrics
{
    private const long Day = 86_400_000L;

    public static Window ComputeWindow(DashboardPeriod period, long nowMs, long todayStartMs)
    {
        switch (period)
        {
            case DashboardPeriod.Today:
                return new Window {
                    CurStart = todayStartMs, CurEnd = nowMs,
                    PrevStart = todayStartMs - Day, PrevEnd = nowMs - Day };
            case DashboardPeriod.Week:
                return new Window {
                    CurStart = nowMs - 7 * Day, CurEnd = nowMs,
                    PrevStart = nowMs - 14 * Day, PrevEnd = nowMs - 7 * Day };
            default: // Month
                return new Window {
                    CurStart = nowMs - 30 * Day, CurEnd = nowMs,
                    PrevStart = nowMs - 60 * Day, PrevEnd = nowMs - 30 * Day };
        }
    }

    /// <summary>
    /// Set-based bot filter (DASH-02): a null/empty set means "all bots"; otherwise keep
    /// rows whose profileId is in the set. A dual-channel bot passes BOTH its profile ids
    /// so its WhatsApp and Telegram rows show under one chip.
    /// </summary>
    public static IEnumerable<DashboardOutcome> FilterByProfiles(
        IEnumerable<DashboardOutcome> rows, ISet<string> ids)
        => (ids == null || ids.Count == 0)
            ? rows
            : rows.Where(r => ids.Contains(r.profileId));

    /// <summary>Single-id convenience overload — delegates to <see cref="FilterByProfiles"/>.</summary>
    public static IEnumerable<DashboardOutcome> FilterByProfile(
        IEnumerable<DashboardOutcome> rows, string profileIdOrNull)
        => string.IsNullOrEmpty(profileIdOrNull)
            ? rows
            : FilterByProfiles(rows, new HashSet<string> { profileIdOrNull });

    public static int CountOrders(IEnumerable<DashboardOutcome> rows, Window w)
        => rows.Count(r => r.Status == OutcomeStatus.OrderCollected
                        && r.outcomeAt >= w.CurStart && r.outcomeAt <= w.CurEnd);

    // Previous window is half-open at the top [PrevStart, PrevEnd): for Week/Month
    // PrevEnd == CurStart exactly, so an order on that seam must belong to the
    // CURRENT window only, never both. Otherwise the delta double-counts it.
    public static int CountOrdersPrev(IEnumerable<DashboardOutcome> rows, Window w)
        => rows.Count(r => r.Status == OutcomeStatus.OrderCollected
                        && r.outcomeAt >= w.PrevStart && r.outcomeAt < w.PrevEnd);

    /// <summary>Counts current outcome of conversations active in the window,
    /// indexed by DashboardStatusInfo.Ordered.</summary>
    public static int[] StatusCounts(IEnumerable<DashboardOutcome> rows, Window w)
    {
        var counts = new int[DashboardStatusInfo.Ordered.Length];
        foreach (var r in rows)
        {
            if (r.lastMessageAt < w.CurStart || r.lastMessageAt > w.CurEnd) continue;
            int idx = System.Array.IndexOf(DashboardStatusInfo.Ordered, r.Status);
            if (idx >= 0) counts[idx]++;
        }
        return counts;
    }

    public static List<DashboardOutcome> Recent(IEnumerable<DashboardOutcome> rows, Window w, int n)
        => rows.Where(r => r.Status == OutcomeStatus.OrderCollected
                        && r.lastMessageAt >= w.CurStart && r.lastMessageAt <= w.CurEnd)
               .OrderByDescending(r => r.lastMessageAt)
               .Take(n).ToList();
}

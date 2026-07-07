using System.Collections.Generic;
using NUnit.Framework;

public class DashboardMetricsTests
{
    private static DashboardOutcome O(string p, string status, long outAt, long lastAt) =>
        new DashboardOutcome { profileId = p, chatId = p + ":c", outcome = status,
                               outcomeAt = outAt, lastMessageAt = lastAt };

    private const long Day = 86_400_000L;

    [Test] public void TodayWindowComparesAgainstSameTimeYesterday()
    {
        long todayStart = 1_000_000_000_000L;
        long now = todayStart + 10 * 3_600_000L;         // 10:00 today
        var w = DashboardMetrics.ComputeWindow(DashboardPeriod.Today, now, todayStart);
        Assert.AreEqual(todayStart, w.CurStart);
        Assert.AreEqual(now, w.CurEnd);
        Assert.AreEqual(todayStart - Day, w.PrevStart);  // yesterday midnight
        Assert.AreEqual(now - Day, w.PrevEnd);           // 10:00 yesterday (partial vs partial)
    }

    [Test] public void CountsOrdersInCurrentWindowOnly()
    {
        long todayStart = 1_000_000_000_000L;
        long now = todayStart + 12 * 3_600_000L;
        var w = DashboardMetrics.ComputeWindow(DashboardPeriod.Today, now, todayStart);
        var rows = new List<DashboardOutcome>
        {
            O("p", "order_collected", todayStart + 3_600_000L, now),  // today
            O("p", "order_collected", todayStart - 2 * Day, now),     // old — excluded
            O("p", "in_dialog",       todayStart + 3_600_000L, now),  // not an order
        };
        Assert.AreEqual(1, DashboardMetrics.CountOrders(rows, w));
    }

    [Test] public void StatusCountsBucketByCurrentOutcomeInWindow()
    {
        var w = new Window { CurStart = 0, CurEnd = 100, PrevStart = -100, PrevEnd = 0 };
        var rows = new List<DashboardOutcome>
        {
            O("p", "order_collected", 10, 50),
            O("p", "owner_needed",    10, 60),
            O("p", "order_collected", 10, 999),   // lastMessageAt outside window — excluded
        };
        var counts = DashboardMetrics.StatusCounts(rows, w);
        Assert.AreEqual(1, counts[0]);  // OrderCollected (Ordered[0])
        Assert.AreEqual(1, counts[1]);  // OwnerNeeded (Ordered[1])
    }

    [Test] public void FilterByProfileNullReturnsAll()
    {
        var rows = new List<DashboardOutcome> { O("a","in_dialog",1,1), O("b","in_dialog",1,1) };
        Assert.AreEqual(2, new List<DashboardOutcome>(DashboardMetrics.FilterByProfile(rows, null)).Count);
        Assert.AreEqual(1, new List<DashboardOutcome>(DashboardMetrics.FilterByProfile(rows, "a")).Count);
    }
}

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

    [Test] public void FilterByProfilesSetReturnsRowsWhoseProfileIsInSet()
    {
        // Set {"a","b"} models a dual-channel bot's two profile ids: both its rows
        // must show under the one chip; the unrelated "c" row is excluded.
        var rows = new List<DashboardOutcome>
            { O("a","in_dialog",1,1), O("b","in_dialog",1,1), O("c","in_dialog",1,1) };
        var set = new HashSet<string> { "a", "b" };
        Assert.AreEqual(2, new List<DashboardOutcome>(DashboardMetrics.FilterByProfiles(rows, set)).Count);
    }

    [Test] public void FilterByProfilesNullOrEmptyReturnsAll()
    {
        var rows = new List<DashboardOutcome> { O("a","in_dialog",1,1), O("b","in_dialog",1,1) };
        Assert.AreEqual(2, new List<DashboardOutcome>(DashboardMetrics.FilterByProfiles(rows, null)).Count);
        Assert.AreEqual(2, new List<DashboardOutcome>(
            DashboardMetrics.FilterByProfiles(rows, new HashSet<string>())).Count);
    }

    [Test] public void CountsOrdersInPreviousWindow()
    {
        long now = 100_000_000_000L;
        var w = DashboardMetrics.ComputeWindow(DashboardPeriod.Week, now, 0);
        var rows = new List<DashboardOutcome>
        {
            O("p", "order_collected", now - 10 * Day, now),  // inside previous window
            O("p", "order_collected", now - 3 * Day, now),   // inside current, not previous
        };
        Assert.AreEqual(1, DashboardMetrics.CountOrdersPrev(rows, w));
        Assert.AreEqual(1, DashboardMetrics.CountOrders(rows, w));
    }

    [Test] public void WeekSeamOrderCountedInCurrentOnly()
    {
        long now = 100_000_000_000L;
        var w = DashboardMetrics.ComputeWindow(DashboardPeriod.Week, now, 0);
        Assert.AreEqual(w.CurStart, w.PrevEnd, "precondition: week seam values coincide");
        var rows = new List<DashboardOutcome>
        {
            O("p", "order_collected", w.CurStart, w.CurStart),
        };
        Assert.AreEqual(1, DashboardMetrics.CountOrders(rows, w));      // current
        Assert.AreEqual(0, DashboardMetrics.CountOrdersPrev(rows, w));  // NOT also previous
    }

    [Test] public void RecentReturnsOrdersNewestFirstLimited()
    {
        var w = new Window { CurStart = 0, CurEnd = 1000, PrevStart = -1000, PrevEnd = 0 };
        var rows = new List<DashboardOutcome>
        {
            O("p", "order_collected", 10, 100),
            O("p", "order_collected", 10, 300),
            O("p", "order_collected", 10, 200),
            O("p", "in_dialog",       10, 400),   // not an order — excluded
            O("p", "order_collected", 10, 5000),  // lastMessageAt outside window — excluded
        };
        var recent = DashboardMetrics.Recent(rows, w, 2);
        Assert.AreEqual(2, recent.Count);
        Assert.AreEqual(300, recent[0].lastMessageAt);  // newest first
        Assert.AreEqual(200, recent[1].lastMessageAt);
    }
}

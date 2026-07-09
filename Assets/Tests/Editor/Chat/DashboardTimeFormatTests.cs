using NUnit.Framework;

public class DashboardTimeFormatTests
{
    [Test] public void PickPrefersNewerLocal()
        => Assert.AreEqual(1_000_000_100L,
            DashboardTimeFormat.PickDisplaySec(1_000_000_000_000L, 1_000_000_100L));

    [Test] public void PickFallsBackToServerWhenLocalAbsent()
        => Assert.AreEqual(1_000_000_000L,
            DashboardTimeFormat.PickDisplaySec(1_000_000_000_000L, 0));

    [Test] public void PickKeepsServerWhenLocalOlder()
        => Assert.AreEqual(1_000_000_000L,
            DashboardTimeFormat.PickDisplaySec(1_000_000_000_000L, 999_999_000L));

    [Test] public void SameInstantIsClock()
    {
        long now = 1_700_000_000L;
        StringAssert.Contains(":", DashboardTimeFormat.Relative(now, now)); // HH:mm
    }

    [Test] public void MidWeekIsShortWeekday()
    {
        // 3.5 days ago is days==3 or 4 in any timezone (offset ≤ 14h) → a 2-char RU weekday.
        long now = 1_700_000_000L;
        string s = DashboardTimeFormat.Relative(now - (3 * 86400L + 43200L), now);
        Assert.AreEqual(2, s.Length);
        Assert.IsFalse(s.Contains(":"));
        Assert.IsFalse(s.Contains("."));
    }

    [Test] public void OldUsesDate()
    {
        long now = 1_700_000_000L;
        StringAssert.Contains(".", DashboardTimeFormat.Relative(now - 40 * 86400L, now)); // dd.MM.yy
    }

    [Test] public void NonPositiveIsEmpty()
        => Assert.AreEqual("", DashboardTimeFormat.Relative(0, 1_700_000_000L));
}

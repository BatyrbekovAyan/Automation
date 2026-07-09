using System;

/// <summary>
/// Row timestamp for the dashboard. <see cref="PickDisplaySec"/> implements the
/// spec's "local time wins": prefer the newer of the server outcome time and the
/// app's local chat last-activity — the local chat list sees owner-typed manual
/// replies that the bot's transcript store (and therefore the server outcome) misses,
/// so its time can be fresher. <see cref="Relative"/> renders a short RU label.
/// </summary>
public static class DashboardTimeFormat
{
    /// <summary>
    /// Newer of the server outcome time (unix ms) and the local chat last-activity
    /// (unix sec), returned in unix sec. A local value of 0 means "no local chat" →
    /// the server time is used.
    /// </summary>
    public static long PickDisplaySec(long serverMs, long localSec)
    {
        long serverSec = serverMs / 1000L;
        return localSec > serverSec ? localSec : serverSec;
    }

    /// <summary>Short RU relative label for a unix-sec instant against a unix-sec "now".</summary>
    public static string Relative(long unixSec, long nowSec)
    {
        if (unixSec <= 0) return "";
        DateTime dt = DateTimeOffset.FromUnixTimeSeconds(unixSec).ToLocalTime().DateTime;
        DateTime now = DateTimeOffset.FromUnixTimeSeconds(nowSec).ToLocalTime().DateTime;
        int days = (now.Date - dt.Date).Days;
        if (days <= 0) return dt.ToString("HH:mm");   // today (or a slightly-ahead server clock)
        if (days == 1) return "вчера";
        if (days < 7) return RuWeekday(dt.DayOfWeek);
        return dt.ToString("dd.MM.yy");
    }

    private static string RuWeekday(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "пн",
        DayOfWeek.Tuesday => "вт",
        DayOfWeek.Wednesday => "ср",
        DayOfWeek.Thursday => "чт",
        DayOfWeek.Friday => "пт",
        DayOfWeek.Saturday => "сб",
        _ => "вс",
    };
}

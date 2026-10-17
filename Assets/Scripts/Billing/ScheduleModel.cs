using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Part B «расписание бота» (spec 2026-10-14 §4): the schedule's rules in ONE pure place — the day mask, validation,
/// HH:MM parsing and formatting, the bot's zone and THE window predicate, the C# twin of the server's
/// (Tools/n8n/bot_schedule.py WINDOW_SQL). Both sides are pinned by one case table, Tools/n8n/fixtures/schedule-cases.json,
/// which ScheduleModelTests reads through the project root (never a copy): a case the server passes and this fails is a
/// bug here.
///
/// The rule: daysMask bit 0 = Monday … bit 6 = Sunday; start == end → the whole day; start &lt; end → [start, end);
/// start &gt; end → across midnight, [start, 1440) ∪ [0, end), and the day that decides is the day the window STARTED in
/// the bot's zone — the PREVIOUS calendar day for the after-midnight half; start inclusive, end exclusive, seconds do not
/// count; daysMask 0 → never open. A disabled schedule (or none) lets the bot answer whatever its window says.
///
/// The zone: the bot's stored IANA id when it has that shape, else Asia/Almaty (the server's default); a zone this
/// platform cannot resolve is read as a FIXED UTC+5 — never the device's own zone.
/// </summary>
public static class ScheduleModel
{
    public const int DaysInWeek = 7;
    public const int AllDays = (1 << DaysInWeek) - 1;
    public const int MinutesPerDay = 24 * 60;
    public const int DefaultStartMin = 9 * 60;
    public const int DefaultEndMin = 21 * 60;
    /// <summary>What an unparsable «С» / «До» reads as: never valid, always a change against any stored minute.</summary>
    public const int InvalidMinute = -1;
    public const string DefaultTimeZone = "Asia/Almaty";

    /// <summary>The server's default zone as a fixed offset — the reading of a zone this platform cannot resolve.</summary>
    public static readonly TimeSpan DefaultZoneOffset = TimeSpan.FromHours(5);

    private const int MaxZoneLength = 64;

    // The last instant a zone conversion cannot push past DateTimeOffset.MaxValue (offsets stay within ±14 h).
    private static readonly long MaxConvertibleUnixMs = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() - 86_400_000L;

    // The server's shape check (build-set-schedule.py TZ_PATTERN): an IANA name or UTC. Anything else would be refused
    // there as bad_request — and judged here in a zone the server never sees.
    private static readonly Regex IanaShape =
        new Regex(@"^[A-Za-z_]+(?:/[A-Za-z0-9_+\-]+)+$|^UTC$", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, TimeZoneInfo> Zones = new Dictionary<string, TimeZoneInfo>();

    // ── days ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Monday → 0 … Sunday → 6 (the mask's bit).</summary>
    public static int DayIndex(DayOfWeek day) => ((int)day + DaysInWeek - 1) % DaysInWeek;

    public static bool HasDay(int mask, int day) => day >= 0 && day < DaysInWeek && ((mask >> day) & 1) == 1;

    /// <summary>Flips one day; an unknown day changes nothing.</summary>
    public static int ToggleDay(int mask, int day) => day >= 0 && day < DaysInWeek ? mask ^ (1 << day) : mask;

    // ── validation ────────────────────────────────────────────────────────────────────────────────────────

    public static bool IsMinuteOfDay(int minutes) => minutes >= 0 && minutes < MinutesPerDay;

    /// <summary>
    /// What Save may persist: both times are minutes of the day and the mask is a mask — also for a disabled schedule,
    /// which is stored too (re-enabling restores the window) — and an enabled schedule names at least one day (an
    /// enabled schedule with none would keep the bot silent for good).
    /// </summary>
    public static bool IsValid(bool enabled, int mask, int startMin, int endMin)
        => IsMinuteOfDay(startMin) && IsMinuteOfDay(endMin)
        && mask >= 0 && mask <= AllDays
        && (!enabled || mask != 0);

    // ── HH:MM ─────────────────────────────────────────────────────────────────────────────────────────────

    // What may stand between the hours and the minutes: the colon, and what a phone keyboard offers first instead.
    private static readonly char[] TimeSeparators = { ':', '.', '-', ' ' };

    /// <summary>
    /// The time the owner typed, 00:00–23:59, surrounding blanks ignored (fix round 3, M3 — lenient, not loose):
    /// one or two hour digits, ONE of «:», «.», «-», a space, and exactly two minute digits («9:00», «21.30», «9-30»,
    /// «9 30»); three or four digits with no separator, the last two being the minutes («900», «2130»); or a bare hour
    /// («9», «21» → 21:00). Refused: one minute digit («9.5» — 9:05 or 9:50?), two separators, any other character.
    /// ASCII digits only — char.IsDigit would let an Arabic-Indic «١٢:٠٠» through, which int.Parse then reads differently
    /// on every device. The blur write-back shows a parsed value canonical («HH:MM»).
    /// </summary>
    public static bool TryParseHhMm(string s, out int minutes)
    {
        minutes = 0;
        if (s == null) return false;
        string t = s.Trim();
        int hours, mins;
        int separator = t.IndexOfAny(TimeSeparators);
        if (separator >= 0)
        {
            if (separator < 1 || separator > 2 || t.Length != separator + 3) return false;
            if (!AsciiDigits(t, 0, separator) || !AsciiDigits(t, separator + 1, 2)) return false;
            hours = DigitsValue(t, 0, separator);
            mins = DigitsValue(t, separator + 1, 2);
        }
        else
        {
            if (t.Length < 1 || t.Length > 4 || !AsciiDigits(t, 0, t.Length)) return false;
            bool bareHour = t.Length <= 2;
            hours = DigitsValue(t, 0, bareHour ? t.Length : t.Length - 2);
            mins = bareHour ? 0 : DigitsValue(t, t.Length - 2, 2);
        }
        if (hours > 23 || mins > 59) return false;
        minutes = hours * 60 + mins;
        return true;
    }

    /// <summary>«09:00» — zero-padded, culture-free.</summary>
    public static string FormatHhMm(int minutes)
    {
        int m = ((minutes % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
        return (m / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (m % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>«09:00–21:00» — the en dash (U+2013) the SF Pro atlases draw.</summary>
    public static string FormatWindow(int startMin, int endMin) => $"{FormatHhMm(startMin)}–{FormatHhMm(endMin)}";

    private static bool AsciiDigits(string s, int from, int count)
    {
        for (int i = from; i < from + count; i++)
            if (s[i] < '0' || s[i] > '9') return false;
        return true;
    }

    // The value of ASCII digits already checked by AsciiDigits — culture-free by construction.
    private static int DigitsValue(string s, int from, int count)
    {
        int value = 0;
        for (int i = from; i < from + count; i++)
            value = value * 10 + (s[i] - '0');
        return value;
    }

    // ── the window ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether the window holds <paramref name="nowLocal"/> — a wall time ALREADY in the bot's zone (its own offset is
    /// what counts). The server's WINDOW_SQL, term by term.
    /// </summary>
    public static bool IsInsideWindow(DateTimeOffset nowLocal, int mask, int startMin, int endMin)
    {
        int minute = nowLocal.Hour * 60 + nowLocal.Minute;
        int today = DayIndex(nowLocal.DayOfWeek);
        bool afterMidnightHalf = startMin > endMin && minute < endMin;
        int startDay = afterMidnightHalf ? (today + DaysInWeek - 1) % DaysInWeek : today;
        if (!HasDay(mask, startDay)) return false;
        if (startMin == endMin) return true;
        if (startMin < endMin) return minute >= startMin && minute < endMin;
        return minute >= startMin || minute < endMin;
    }

    /// <summary>The window at a UTC instant, read in the bot's zone (<see cref="ToBotLocal"/>).</summary>
    public static bool IsInsideWindowAt(DateTimeOffset utc, string iana, int mask, int startMin, int endMin)
        => IsInsideWindow(ToBotLocal(utc, iana), mask, startMin, endMin);

    /// <summary>«The schedule lets the bot answer» at a UTC instant — the case table's expectOpen.</summary>
    public static bool IsOpenAt(DateTimeOffset utc, string iana, bool enabled, int mask, int startMin, int endMin)
        => !enabled || IsInsideWindowAt(utc, iana, mask, startMin, endMin);

    /// <summary>
    /// The «Сводка» marker's question: did the row's last message fall outside the bot's ENABLED schedule? An unknown
    /// or garbage time marks nothing. Judged by the bot's CURRENT schedule — an approximation by design (the schedule
    /// in force when the message came is not kept anywhere).
    /// </summary>
    public static bool IsOffHoursRow(long lastMessageAtMs, bool enabled, int mask, int startMin, int endMin, string iana)
    {
        if (!enabled || lastMessageAtMs <= 0 || lastMessageAtMs > MaxConvertibleUnixMs) return false;
        return !IsInsideWindowAt(DateTimeOffset.FromUnixTimeMilliseconds(lastMessageAtMs), iana, mask, startMin, endMin);
    }

    // ── the bot's zone ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The zone the bot is judged in — and the one the server is sent: its IANA id, else Asia/Almaty.</summary>
    public static string ResolveZone(string stored)
    {
        string z = stored?.Trim();
        return !string.IsNullOrEmpty(z) && z.Length <= MaxZoneLength && IanaShape.IsMatch(z) ? z : DefaultTimeZone;
    }

    /// <summary>The instant on the bot's wall clock. An unresolvable zone reads as a FIXED UTC+5, never the device's.</summary>
    public static DateTimeOffset ToBotLocal(DateTimeOffset utc, string iana)
    {
        var zone = FindZone(ResolveZone(iana));
        return zone != null ? TimeZoneInfo.ConvertTime(utc, zone) : utc.ToOffset(DefaultZoneOffset);
    }

    // Memoised (the «Сводка» asks once per row); an unknown id is remembered as null so it throws only once.
    private static TimeZoneInfo FindZone(string id)
    {
        if (Zones.TryGetValue(id, out var zone)) return zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception)
        {
            zone = null;   // TimeZoneNotFound / InvalidTimeZone / a platform without tz data
        }
        Zones[id] = zone;
        return zone;
    }
}

/// <summary>
/// One bot's schedule as the app stores it (<see cref="BotScheduleStore"/>) and sends it (SetSchedule): disabled by
/// default, all days, 09:00–21:00 — the values the section shows before the owner touches it.
/// </summary>
public readonly struct BotSchedule : IEquatable<BotSchedule>
{
    public readonly bool Enabled;
    public readonly int DaysMask;
    public readonly int StartMin;
    public readonly int EndMin;

    public BotSchedule(bool enabled, int daysMask, int startMin, int endMin)
    {
        Enabled = enabled;
        DaysMask = daysMask;
        StartMin = startMin;
        EndMin = endMin;
    }

    public static BotSchedule Default => new BotSchedule(false, ScheduleModel.AllDays, ScheduleModel.DefaultStartMin, ScheduleModel.DefaultEndMin);

    public bool IsValid => ScheduleModel.IsValid(Enabled, DaysMask, StartMin, EndMin);

    /// <summary>
    /// The four stored ints, each out-of-range one read as its default — a corrupted key must never put the settings in a
    /// state Save can never leave.
    /// </summary>
    public static BotSchedule FromStored(int enabled, int daysMask, int startMin, int endMin)
    {
        var d = Default;
        return new BotSchedule(
            enabled == 1,
            daysMask >= 0 && daysMask <= ScheduleModel.AllDays ? daysMask : d.DaysMask,
            ScheduleModel.IsMinuteOfDay(startMin) ? startMin : d.StartMin,
            ScheduleModel.IsMinuteOfDay(endMin) ? endMin : d.EndMin);
    }

    public bool Equals(BotSchedule other)
        => Enabled == other.Enabled && DaysMask == other.DaysMask && StartMin == other.StartMin && EndMin == other.EndMin;

    public override bool Equals(object obj) => obj is BotSchedule other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Enabled, DaysMask, StartMin, EndMin);

    public static bool operator ==(BotSchedule a, BotSchedule b) => a.Equals(b);

    public static bool operator !=(BotSchedule a, BotSchedule b) => !a.Equals(b);

    public override string ToString() => $"{(Enabled ? "on" : "off")} mask={DaysMask} {ScheduleModel.FormatWindow(StartMin, EndMin)}";
}

using System;

/// <summary>
/// Russian date labels for the chat UI. The app ships RU-only, so day and month
/// names are spelled out here instead of going through <c>ToString("dddd")</c>:
/// that path reads <c>CultureInfo.CurrentCulture</c>, which follows the DEVICE
/// locale, so an English phone rendered English separators inside a Russian UI.
/// Same reasoning (and the same hardcoded-name shape) as <see cref="DashboardTimeFormat"/>.
/// Pure + static so EditMode tests pin every bucket.
/// </summary>
public static class RuDateFormat
{
    /// <summary>Full nominative weekday, e.g. "вторник". Used for dates 2..6 days old.</summary>
    public static string Weekday(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday    => "понедельник",
        DayOfWeek.Tuesday   => "вторник",
        DayOfWeek.Wednesday => "среда",
        DayOfWeek.Thursday  => "четверг",
        DayOfWeek.Friday    => "пятница",
        DayOfWeek.Saturday  => "суббота",
        _                   => "воскресенье",
    };

    /// <summary>Short month name in the genitive used after a day number, e.g. "фев".</summary>
    public static string ShortMonth(int month) => month switch
    {
        1  => "янв",
        2  => "фев",
        3  => "мар",
        4  => "апр",
        5  => "мая",
        6  => "июн",
        7  => "июл",
        8  => "авг",
        9  => "сен",
        10 => "окт",
        11 => "ноя",
        _  => "дек",
    };

    /// <summary>
    /// Full month name in the genitive used after a day number, e.g. "августа".
    /// The long sibling of <see cref="ShortMonth"/>, for places with room to spell it out
    /// (the «Подписка» renewal line). Same reason it is hardcoded: ToString("MMMM") reads
    /// CultureInfo.CurrentCulture, which follows the DEVICE locale.
    /// </summary>
    public static string MonthGenitive(int month) => month switch
    {
        1  => "января",
        2  => "февраля",
        3  => "марта",
        4  => "апреля",
        5  => "мая",
        6  => "июня",
        7  => "июля",
        8  => "августа",
        9  => "сентября",
        10 => "октября",
        11 => "ноября",
        _  => "декабря",
    };

    /// <summary>Day + spelled-out genitive month, no year, e.g. "26 августа".</summary>
    public static string DayMonth(DateTime date) =>
        $"{date.Day} {MonthGenitive(date.Month)}";

    /// <summary>Day-month-year label for dates older than a week, e.g. "24 фев 2026".</summary>
    public static string DayMonthYear(DateTime date) =>
        $"{date.Day} {ShortMonth(date.Month)} {date.Year}";
}

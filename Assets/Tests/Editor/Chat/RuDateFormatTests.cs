using System;
using NUnit.Framework;

/// <summary>
/// Pins the Russian day/month names. The app ships RU-only, so these must NOT come
/// from ToString("dddd")/("MMM") — that reads CultureInfo.CurrentCulture, i.e. the
/// DEVICE locale, and printed English separators inside the Russian UI.
/// </summary>
public class RuDateFormatTests
{
    [TestCase(DayOfWeek.Monday,    "понедельник")]
    [TestCase(DayOfWeek.Tuesday,   "вторник")]
    [TestCase(DayOfWeek.Wednesday, "среда")]
    [TestCase(DayOfWeek.Thursday,  "четверг")]
    [TestCase(DayOfWeek.Friday,    "пятница")]
    [TestCase(DayOfWeek.Saturday,  "суббота")]
    [TestCase(DayOfWeek.Sunday,    "воскресенье")]
    public void Weekday_IsRussian(DayOfWeek day, string expected)
        => Assert.AreEqual(expected, RuDateFormat.Weekday(day));

    [TestCase(1,  "янв")] [TestCase(2,  "фев")] [TestCase(3,  "мар")]
    [TestCase(4,  "апр")] [TestCase(5,  "мая")] [TestCase(6,  "июн")]
    [TestCase(7,  "июл")] [TestCase(8,  "авг")] [TestCase(9,  "сен")]
    [TestCase(10, "окт")] [TestCase(11, "ноя")] [TestCase(12, "дек")]
    public void ShortMonth_IsRussian(int month, string expected)
        => Assert.AreEqual(expected, RuDateFormat.ShortMonth(month));

    [Test] public void DayMonthYear_Composes()
        => Assert.AreEqual("24 фев 2026", RuDateFormat.DayMonthYear(new DateTime(2026, 2, 24)));

    [Test] public void DayMonthYear_NoLeadingZeroOnDay()
        => Assert.AreEqual("3 мая 2026", RuDateFormat.DayMonthYear(new DateTime(2026, 5, 3)));
}

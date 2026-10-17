using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Part B «расписание бота» (spec 2026-10-14 §4): the client twin of the server's window predicate. Both sides are pinned
/// by ONE case table — Tools/n8n/fixtures/schedule-cases.json, read here through the project root (never a copy), the
/// same file tests/test_reply_schedule.py feeds the server's SQL. A case that fails here while the server passes it is a
/// bug on this side. expectOpen reads «the schedule lets the bot answer»: a disabled case is open whatever its window.
/// </summary>
public class ScheduleModelTests
{
    private static readonly string FixturePath =
        Path.Combine(Application.dataPath, "..", "Tools", "n8n", "fixtures", "schedule-cases.json");

    private static readonly string[] FixtureFields =
        { "name", "tz", "nowIso", "daysMask", "startMin", "endMin", "enabled", "expectOpen" };

    private const string TestBot = "ScheduleModelTestBot";

    private sealed class Case
    {
        public string name;
        public string tz;
        public string nowIso;
        public int daysMask;
        public int startMin;
        public int endMin;
        public bool enabled;
        public bool expectOpen;

        public DateTimeOffset NowUtc => DateTimeOffset.Parse(nowIso, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        public override string ToString() => name;
    }

    // DateParseHandling.None: nowIso must stay the exact string, never a culture-formatted DateTime.
    private static string FixtureText() => File.ReadAllText(FixturePath);

    private static List<Case> LoadCases() => JsonConvert.DeserializeObject<List<Case>>(FixtureText(),
        new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });

    private static IEnumerable<TestCaseData> FixtureCases() =>
        LoadCases().Select(c => new TestCaseData(c).SetName($"Fixture_{c.name}"));

    [SetUp]
    [TearDown]
    public void WipeTestBot() => BotScheduleStore.Clear(TestBot);

    // ── the shared case table ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Fixture_IsTheCanonicalTable_WithExactlyTheAgreedFields()
    {
        Assert.IsTrue(File.Exists(FixturePath), $"the shared case table is missing: {FixturePath}");
        var rows = JArray.Parse(FixtureText());
        Assert.GreaterOrEqual(rows.Count, 30, "the case table collapsed");
        foreach (JObject row in rows)
            CollectionAssert.AreEquivalent(FixtureFields, row.Properties().Select(p => p.Name).ToArray(),
                $"{row["name"]}: the table's fields are a contract shared with the server tests");
        Assert.AreEqual(rows.Count, LoadCases().Select(c => c.name).Distinct().Count(), "case names must be unique");
    }

    [TestCaseSource(nameof(FixtureCases))]
    public void Fixture(object boxed)
    {
        var c = (Case)boxed;
        Assert.AreEqual(c.expectOpen, ScheduleModel.IsOpenAt(c.NowUtc, c.tz, c.enabled, c.daysMask, c.startMin, c.endMin),
            $"{c.name}: IsOpenAt disagrees with the server's table");
        if (!c.enabled) return;
        // The pure rule on the already-converted wall time — the predicate itself, without the zone lookup.
        var local = ScheduleModel.ToBotLocal(c.NowUtc, c.tz);
        Assert.AreEqual(c.expectOpen, ScheduleModel.IsInsideWindow(local, c.daysMask, c.startMin, c.endMin),
            $"{c.name}: IsInsideWindow disagrees on local {local:yyyy-MM-dd HH:mm ddd}");
    }

    // ── the predicate's edges beyond the table ────────────────────────────────────────────────────────────

    [Test]
    public void IsInsideWindow_SecondsDoNotCount()
    {
        var monday = new DateTimeOffset(2026, 10, 12, 8, 59, 59, TimeSpan.FromHours(5));
        Assert.IsFalse(ScheduleModel.IsInsideWindow(monday, 1, 540, 1080), "08:59:59 is minute 539");
        Assert.IsTrue(ScheduleModel.IsInsideWindow(monday.AddSeconds(1), 1, 540, 1080), "09:00:00 opens");
        var almostSix = new DateTimeOffset(2026, 10, 12, 17, 59, 59, TimeSpan.FromHours(5));
        Assert.IsTrue(ScheduleModel.IsInsideWindow(almostSix, 1, 540, 1080), "17:59:59 is still minute 1079");
    }

    [Test]
    public void IsInsideWindow_UsesTheOffsetOfTheValueItIsGiven()
    {
        // 2026-10-12 is a Monday; the same instant read in UTC is still Sunday evening.
        var mondayLocal = new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.FromHours(5));
        Assert.IsTrue(ScheduleModel.IsInsideWindow(mondayLocal, 1, 0, 360));
        Assert.IsFalse(ScheduleModel.IsInsideWindow(mondayLocal.ToUniversalTime(), 1, 0, 360), "the UTC reading is Sunday 20:00");
    }

    [Test]
    public void DayIndex_IsMondayFirst()
    {
        Assert.AreEqual(0, ScheduleModel.DayIndex(DayOfWeek.Monday));
        Assert.AreEqual(5, ScheduleModel.DayIndex(DayOfWeek.Saturday));
        Assert.AreEqual(6, ScheduleModel.DayIndex(DayOfWeek.Sunday));
    }

    [Test]
    public void ToggleDay_FlipsOneBit_AndIgnoresAnUnknownDay()
    {
        Assert.AreEqual(1, ScheduleModel.ToggleDay(0, 0));
        Assert.AreEqual(0, ScheduleModel.ToggleDay(1, 0));
        Assert.AreEqual(63, ScheduleModel.ToggleDay(127, 6), "Sunday is bit 6");
        Assert.AreEqual(127, ScheduleModel.ToggleDay(63, 6));
        Assert.AreEqual(31, ScheduleModel.ToggleDay(31, 7));
        Assert.AreEqual(31, ScheduleModel.ToggleDay(31, -1));
        Assert.IsTrue(ScheduleModel.HasDay(64, 6));
        Assert.IsFalse(ScheduleModel.HasDay(63, 6));
        Assert.IsFalse(ScheduleModel.HasDay(127, 7));
    }

    // ── validation, parsing, formatting ───────────────────────────────────────────────────────────────────

    [TestCase(false, 0, 0, 0, true, TestName = "Valid_DisabledWithNoDays")]
    [TestCase(true, 31, 540, 1260, true, TestName = "Valid_EnabledWeekdays")]
    [TestCase(true, 127, 600, 600, true, TestName = "Valid_StartEqualsEnd_Is24h")]
    [TestCase(true, 1, 1320, 360, true, TestName = "Valid_AcrossMidnight")]
    [TestCase(true, 0, 540, 1260, false, TestName = "Invalid_EnabledWithNoDay")]
    [TestCase(false, 31, -1, 1260, false, TestName = "Invalid_UnparsedStart_EvenWhenDisabled")]
    [TestCase(true, 31, 540, 1440, false, TestName = "Invalid_EndPastTheDay")]
    [TestCase(true, 128, 540, 1260, false, TestName = "Invalid_MaskOutOfRange")]
    [TestCase(true, -1, 540, 1260, false, TestName = "Invalid_NegativeMask")]
    public void IsValid(bool enabled, int mask, int start, int end, bool expected)
        => Assert.AreEqual(expected, ScheduleModel.IsValid(enabled, mask, start, end));

    [TestCase("09:00", 540)]
    [TestCase("9:00", 540)]
    [TestCase("00:00", 0)]
    [TestCase("0:05", 5)]
    [TestCase("23:59", 1439)]
    [TestCase(" 21:30 ", 1290)]
    // Fix round 3 (M3): the separators a phone keyboard offers first, and the forms people type without one.
    [TestCase("9.00", 540)]
    [TestCase("21.30", 1290)]
    [TestCase("09-00", 540)]
    [TestCase("9-30", 570)]
    [TestCase("9 00", 540)]
    [TestCase("21 30", 1290)]
    [TestCase("900", 540)]
    [TestCase("930", 570)]
    [TestCase("0900", 540)]
    [TestCase("2130", 1290)]
    [TestCase("0000", 0)]
    [TestCase("2359", 1439)]
    [TestCase("9", 540)]
    [TestCase("21", 1260)]
    [TestCase("0", 0)]
    [TestCase("00", 0)]
    [TestCase("23", 1380)]
    [TestCase(" 9.30 ", 570)]
    public void TryParseHhMm_Accepts(string text, int minutes)
    {
        Assert.IsTrue(ScheduleModel.TryParseHhMm(text, out int parsed), text);
        Assert.AreEqual(minutes, parsed);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("24:00")]
    [TestCase("12:60")]
    [TestCase("09:0")]
    [TestCase("09:000")]
    [TestCase("009:00")]
    [TestCase(":30")]
    [TestCase("+9:00")]
    [TestCase("ab:cd")]
    [TestCase("١٢:٠٠")]   // Arabic-Indic digits: «a digit» to char.IsDigit, never to the server
    [TestCase("09:00 утра")]
    // Fix round 3 (M3): lenient, not loose — one separator, two minute digits, 00:00–23:59, ASCII digits only.
    [TestCase("24")]
    [TestCase("25")]
    [TestCase("2400")]
    [TestCase("960")]
    [TestCase("12345")]
    [TestCase("9.5")]
    [TestCase("9:")]
    [TestCase("9.")]
    [TestCase("-9")]
    [TestCase("9 :00")]
    [TestCase("9  00")]
    [TestCase("9:00:00")]
    [TestCase("9.00.")]
    [TestCase("9,00")]
    [TestCase("9h00")]
    [TestCase("9:00am")]
    [TestCase("１２:００")]   // full-width digits
    [TestCase("٩")]
    public void TryParseHhMm_Refuses(string text)
    {
        Assert.IsFalse(ScheduleModel.TryParseHhMm(text, out _), $"'{text}' must not parse");
    }

    [Test]
    public void FormatHhMm_And_FormatWindow_AreZeroPadded_WithAnEnDash()
    {
        Assert.AreEqual("09:00", ScheduleModel.FormatHhMm(540));
        Assert.AreEqual("00:05", ScheduleModel.FormatHhMm(5));
        Assert.AreEqual("23:59", ScheduleModel.FormatHhMm(1439));
        Assert.AreEqual("09:00–21:00", ScheduleModel.FormatWindow(540, 1260), "U+2013 — the SF Pro atlases draw it");
        Assert.AreEqual("22:00–06:00", ScheduleModel.FormatWindow(1320, 360));
        Assert.IsTrue(ScheduleModel.TryParseHhMm(ScheduleModel.FormatHhMm(1290), out int back) && back == 1290, "round trip");
    }

    [Test]
    public void Formatting_IgnoresTheDeviceCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.AreEqual("09:00–21:00", ScheduleModel.FormatWindow(540, 1260));
            Assert.IsTrue(ScheduleModel.TryParseHhMm("09:30", out int m) && m == 570);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ── the bot's zone ────────────────────────────────────────────────────────────────────────────────────

    [TestCase("Asia/Almaty", "Asia/Almaty")]
    [TestCase("Europe/Moscow", "Europe/Moscow")]
    [TestCase("America/Argentina/Buenos_Aires", "America/Argentina/Buenos_Aires")]
    [TestCase("Etc/GMT-5", "Etc/GMT-5")]
    [TestCase("UTC", "UTC")]
    [TestCase(null, "Asia/Almaty")]
    [TestCase("", "Asia/Almaty")]
    [TestCase("  ", "Asia/Almaty")]
    [TestCase("GMT+05:00", "Asia/Almaty")]
    [TestCase("Central Asia Standard Time", "Asia/Almaty")]
    public void ResolveZone_KeepsAnIanaName_ElseTheServersDefault(string stored, string expected)
        => Assert.AreEqual(expected, ScheduleModel.ResolveZone(stored));

    [Test]
    public void ToBotLocal_AKnownZone_IsConverted()
    {
        var utc = new DateTimeOffset(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);
        var moscow = ScheduleModel.ToBotLocal(utc, "Europe/Moscow");
        Assert.AreEqual(TimeSpan.FromHours(3), moscow.Offset);
        Assert.AreEqual(9, moscow.Hour);
        Assert.AreEqual(utc, moscow, "the same instant");
    }

    [Test]
    public void ToBotLocal_AnUnresolvableZone_FallsBackToAFixedUtcPlus5_NeverTheDeviceZone()
    {
        var utc = new DateTimeOffset(2026, 7, 6, 13, 30, 0, TimeSpan.Zero);
        foreach (var zone in new[] { "Europe/Nowhere", "Mars/Olympus_Mons", null, "", "not a zone" })
        {
            var local = ScheduleModel.ToBotLocal(utc, zone);
            Assert.AreEqual(TimeSpan.FromHours(5), local.Offset, $"'{zone}' must read as the server's default zone (UTC+5)");
            Assert.AreEqual(18, local.Hour);
            Assert.AreEqual(utc, local);
        }
    }

    [Test]
    public void IsInsideWindowAt_ABogusZone_IsJudgedInUtcPlus5()
    {
        // 04:00Z = 09:00 at UTC+5 (open from 09:00) but 04:00 in UTC (closed): the fallback is the server's zone.
        var utc = new DateTimeOffset(2026, 10, 12, 4, 0, 0, TimeSpan.Zero);
        Assert.IsTrue(ScheduleModel.IsInsideWindowAt(utc, "Europe/Nowhere", 31, 540, 1080));
        Assert.IsFalse(ScheduleModel.IsInsideWindowAt(utc, "UTC", 31, 540, 1080));
    }

    // ── the «Сводка» marker's seam ────────────────────────────────────────────────────────────────────────

    [Test]
    public void IsOffHoursRow_OnlyAnEnabledScheduleWithAKnownTimeOutsideItsWindow()
    {
        // 2026-10-12T14:00Z = Monday 19:00 in Almaty; the window is Mon–Fri 09:00–18:00.
        long evening = new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long midday = new DateTimeOffset(2026, 10, 12, 7, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.IsTrue(ScheduleModel.IsOffHoursRow(evening, true, 31, 540, 1080, "Asia/Almaty"));
        Assert.IsFalse(ScheduleModel.IsOffHoursRow(midday, true, 31, 540, 1080, "Asia/Almaty"), "inside the window");
        Assert.IsFalse(ScheduleModel.IsOffHoursRow(evening, false, 31, 540, 1080, "Asia/Almaty"), "a disabled schedule marks nothing");
        Assert.IsFalse(ScheduleModel.IsOffHoursRow(0, true, 31, 540, 1080, "Asia/Almaty"), "an unknown time marks nothing");
        Assert.IsFalse(ScheduleModel.IsOffHoursRow(-5, true, 31, 540, 1080, "Asia/Almaty"));
        Assert.IsFalse(ScheduleModel.IsOffHoursRow(long.MaxValue, true, 31, 540, 1080, "Asia/Almaty"), "a garbage time never throws");
        Assert.IsTrue(ScheduleModel.IsOffHoursRow(evening, true, 31, 540, 1080, null), "a bot without a zone is judged in Almaty");
    }

    // ── the stored value ──────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Default_IsDisabled_AllDays_0900To2100()
    {
        var d = BotSchedule.Default;
        Assert.IsFalse(d.Enabled);
        Assert.AreEqual(ScheduleModel.AllDays, d.DaysMask);
        Assert.AreEqual(540, d.StartMin);
        Assert.AreEqual(1260, d.EndMin);
        Assert.IsTrue(d.IsValid);
        Assert.AreEqual(127, ScheduleModel.AllDays);
    }

    [Test]
    public void FromStored_ReadsAnOutOfRangeValueAsItsDefault()
    {
        Assert.AreEqual(new BotSchedule(true, 31, 600, 1200), BotSchedule.FromStored(1, 31, 600, 1200));
        var corrupt = BotSchedule.FromStored(7, 999, -40, 5000);
        Assert.IsFalse(corrupt.Enabled, "only 1 is «enabled»");
        Assert.AreEqual(BotSchedule.Default.DaysMask, corrupt.DaysMask);
        Assert.AreEqual(BotSchedule.Default.StartMin, corrupt.StartMin);
        Assert.AreEqual(BotSchedule.Default.EndMin, corrupt.EndMin);
        Assert.IsTrue(corrupt.IsValid, "a corrupted key must never pin Save disabled");
    }

    [Test]
    public void Equality_IsByValue()
    {
        Assert.IsTrue(new BotSchedule(true, 3, 60, 120) == new BotSchedule(true, 3, 60, 120));
        Assert.IsTrue(new BotSchedule(true, 3, 60, 120) != new BotSchedule(false, 3, 60, 120));
        Assert.IsTrue(new BotSchedule(true, 3, 60, 120) != new BotSchedule(true, 3, 60, 121));
    }

    [Test]
    public void Store_RoundTripsUnderTheAgreedKeys_AndClearRemovesThem()
    {
        Assert.IsFalse(BotScheduleStore.HasSaved(TestBot));
        Assert.AreEqual(BotSchedule.Default, BotScheduleStore.Read(TestBot), "absent keys read as the default");

        var schedule = new BotSchedule(true, 31, 600, 1320);
        BotScheduleStore.Write(TestBot, schedule);
        Assert.IsTrue(BotScheduleStore.HasSaved(TestBot));
        Assert.AreEqual(schedule, BotScheduleStore.Read(TestBot));
        Assert.AreEqual(1, PlayerPrefs.GetInt(TestBot + "ScheduleEnabled", -1));
        Assert.AreEqual(31, PlayerPrefs.GetInt(TestBot + "ScheduleDaysMask", -1));
        Assert.AreEqual(600, PlayerPrefs.GetInt(TestBot + "ScheduleStartMin", -1));
        Assert.AreEqual(1320, PlayerPrefs.GetInt(TestBot + "ScheduleEndMin", -1));

        BotScheduleStore.Clear(TestBot);
        Assert.IsFalse(BotScheduleStore.HasSaved(TestBot));
        foreach (var suffix in new[] { "ScheduleEnabled", "ScheduleDaysMask", "ScheduleStartMin", "ScheduleEndMin" })
            Assert.IsFalse(PlayerPrefs.HasKey(TestBot + suffix), suffix);
    }

    [Test]
    public void Store_ToleratesAMissingBot()
    {
        Assert.AreEqual(BotSchedule.Default, BotScheduleStore.Read(null));
        Assert.AreEqual(BotSchedule.Default, BotScheduleStore.Read(""));
        Assert.IsFalse(BotScheduleStore.HasSaved(null));
    }
}

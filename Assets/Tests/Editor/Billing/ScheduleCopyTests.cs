using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Part B «расписание бота»: every user-facing string of the schedule lives in <see cref="ScheduleCopy"/> (RU only; the
/// glyphs are pinned by FontGlyphCoverageTests). These pin the decisions the copy encodes — which error wins, and that
/// the «Сводка» marker never outranks a real request.
/// </summary>
public class ScheduleCopyTests
{
    private static DashboardOutcome Row(string source, string summary = "Клиент спросил про доставку")
        => new DashboardOutcome { profileId = "p1", chatId = "c1", summary = summary, source = source };

    [Test]
    public void DayNames_AreMondayFirst_SevenShortRussianNames()
    {
        CollectionAssert.AreEqual(new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" }, ScheduleCopy.DayNames.ToArray());
        Assert.AreEqual("Пн", ScheduleCopy.DayName(0));
        Assert.AreEqual("Вс", ScheduleCopy.DayName(6));
        Assert.AreEqual("", ScheduleCopy.DayName(7));
        Assert.AreEqual("", ScheduleCopy.DayName(-1));
    }

    [Test]
    public void TheSectionsStrings_AreTheAgreedOnes()
    {
        Assert.AreEqual("РАСПИСАНИЕ РАБОТЫ", ScheduleCopy.SectionTitle);
        Assert.AreEqual("С", ScheduleCopy.FromLabel);
        Assert.AreEqual("До", ScheduleCopy.ToLabel);
        Assert.AreEqual("Вне часов ИИ-сотрудник не отвечает сам — работает режим «Выбрать»", ScheduleCopy.Hint);
        Assert.AreEqual("Вне графика · ", ScheduleCopy.OffHoursRowPrefix);
        Assert.IsTrue(ScheduleModel.TryParseHhMm(ScheduleCopy.StartPlaceholder, out int start) && start == BotSchedule.Default.StartMin,
            "the placeholder teaches the format AND the default");
        Assert.IsTrue(ScheduleModel.TryParseHhMm(ScheduleCopy.EndPlaceholder, out int end) && end == BotSchedule.Default.EndMin);
    }

    [Test]
    public void EveryString_IsRussianText_WithNoGlyphTheAtlasesLack()
    {
        var strings = new[]
        {
            ScheduleCopy.SectionTitle, ScheduleCopy.ToggleLabel, ScheduleCopy.FromLabel, ScheduleCopy.ToLabel,
            ScheduleCopy.Hint, ScheduleCopy.Invalid, ScheduleCopy.NoDays, ScheduleCopy.OffHoursRowPrefix,
            ScheduleCopy.SyncPending, ScheduleCopy.SyncFailed, ScheduleCopy.SyncRejected,
        }.Concat(ScheduleCopy.DayNames);
        foreach (var s in strings)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(s));
            Assert.IsFalse(s.Any(ch => ch >= 'A' && ch <= 'z' && char.IsLetter(ch)), $"'{s}' carries Latin letters (RU-only UI)");
            foreach (char forbidden in new[] { '−', '№', '✕', '→', '⇒' })
                Assert.IsFalse(s.Contains(forbidden), $"'{s}' carries U+{(int)forbidden:X4}, which the SF Pro atlases cannot draw");
        }
    }

    [Test]
    public void TheDeliveryLine_SaysPendingOrFailed_AndHidesOnceTheServerHoldsIt()
    {
        Assert.AreEqual("Расписание ещё не дошло до сервера", ScheduleCopy.SyncStatus(ScheduleDelivery.Pending));
        Assert.AreEqual("Не удалось сохранить расписание — проверьте интернет", ScheduleCopy.SyncStatus(ScheduleDelivery.Failed));
        Assert.AreEqual("Сервер не принял расписание — повторим при следующем открытии настроек", ScheduleCopy.SyncStatus(ScheduleDelivery.Rejected),
            "a refusal (400/401/403) is not the network — never «проверьте интернет»");
        Assert.IsFalse(ScheduleCopy.SyncStatus(ScheduleDelivery.Rejected).Contains("интернет"));
        Assert.IsNull(ScheduleCopy.SyncStatus(ScheduleDelivery.Confirmed), "a confirmed schedule needs no line");
    }

    [Test]
    public void ErrorFor_ATypedTimeError_OutranksTheMissingDay()
    {
        Assert.AreEqual(ScheduleCopy.Invalid, ScheduleCopy.ErrorFor(timeInvalid: true, enabled: true, daysMask: 0));
        Assert.AreEqual(ScheduleCopy.Invalid, ScheduleCopy.ErrorFor(timeInvalid: true, enabled: false, daysMask: 31));
        Assert.AreEqual(ScheduleCopy.NoDays, ScheduleCopy.ErrorFor(timeInvalid: false, enabled: true, daysMask: 0));
        Assert.IsNull(ScheduleCopy.ErrorFor(timeInvalid: false, enabled: false, daysMask: 0), "no day on a disabled schedule is fine");
        Assert.IsNull(ScheduleCopy.ErrorFor(timeInvalid: false, enabled: true, daysMask: 1));
    }

    // What DashboardPage.BindRow composes for a row.
    private static string Summary(DashboardOutcome row, bool offHours)
        => ScheduleCopy.WithOffHoursMark(HandoffCopy.RowSummary(row), row.IsEvent, offHours);

    [Test]
    public void TheRowMarker_MarksAnOffHoursVerdict_ButARealRequestKeepsItsOwnMarker()
    {
        Assert.AreEqual("Вне графика · Клиент спросил про доставку", Summary(Row("classifier"), offHours: true));
        Assert.AreEqual("Клиент спросил про доставку", Summary(Row("classifier"), offHours: false));
        Assert.AreEqual(HandoffCopy.RequestMarker + "Клиент спросил про доставку", Summary(Row("event"), offHours: true),
            "«Заявка ·» outranks «Вне графика ·» — a real request is the stronger fact, one marker per row");
        Assert.AreEqual(HandoffCopy.RowSummary(Row("event")), Summary(Row("event"), offHours: false));
        Assert.AreEqual("Вне графика · ", Summary(Row(null, summary: null), offHours: true));
        Assert.AreEqual("", ScheduleCopy.WithOffHoursMark(null, isRequest: false, offHours: false));
    }

    [Test]
    public void TheDashboard_ComposesTheRowThroughBothSeams()
    {
        string page = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/Dashboard/DashboardPage.cs"));
        StringAssert.Contains(
            "summary.text = ScheduleCopy.WithOffHoursMark(HandoffCopy.RowSummary(r), r.IsEvent, IsOffHoursRow(r, map))", page,
            "the «Сводка» row: the request marker first (A5), the off-hours marker only on a non-request (B2)");
        StringAssert.Contains("ScheduleModel.IsOffHoursRow(r.lastMessageAt, schedule.Enabled,", page,
            "judged by the row's last message against its bot's stored schedule");
    }
}

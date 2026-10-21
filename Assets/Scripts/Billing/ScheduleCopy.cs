using System.Collections.Generic;

/// <summary>
/// Part B «расписание бота»: every user-facing string of the schedule — RU only, glyphs the static SF Pro atlases draw
/// (pinned by FontGlyphCoverageTests: no «−», «№», «✕», «→»). Tools/BotSettings/Add Schedule Section authors the
/// section's static labels from here; at runtime only the error line, the delivery line and the «Сводка» row marker are
/// written.
/// </summary>
public static class ScheduleCopy
{
    public const string SectionTitle = "РАСПИСАНИЕ РАБОТЫ";
    public const string ToggleLabel = "Отвечать по расписанию";
    public const string FromLabel = "С";
    public const string ToLabel = "До";
    /// <summary>Teaches the format AND the default window (09:00–21:00).</summary>
    public const string StartPlaceholder = "09:00";
    public const string EndPlaceholder = "21:00";
    public const string Hint = "Вне часов сотрудник не отвечает сам — работает режим «Выбрать»";
    public const string Invalid = "Укажите время в формате ЧЧ:ММ, например 09:00";
    public const string NoDays = "Выберите хотя бы один день";
    /// <summary>The «Сводка» row marker — the <see cref="HandoffCopy.RequestMarker"/> shape.</summary>
    public const string OffHoursRowPrefix = "Вне графика · ";
    /// <summary>Fix round 3 (I-1): the delivery line — the saved schedule has not reached the server yet.</summary>
    public const string SyncPending = "Расписание ещё не дошло до сервера";
    /// <summary>...or every attempt of this launch failed — the network (retried on return to the app).</summary>
    public const string SyncFailed = "Не удалось сохранить расписание — проверьте интернет";
    /// <summary>
    /// Fix rounds 4–5: ...or the server refused it (400/401/403) — not the network, so never «проверьте интернет»; and the
    /// retry is automatic (a return to the app, opening the settings), so the line says what happens next instead of asking
    /// for a Save the owner cannot make (it stays disabled until an edit).
    /// </summary>
    public const string SyncRejected = "Сервер не принял расписание — повторим при следующем открытии настроек";

    // Monday first: index = the ScheduleModel day bit.
    private static readonly string[] Days = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };

    public static IReadOnlyList<string> DayNames => Days;

    public static string DayName(int day) => day >= 0 && day < Days.Length ? Days[day] : "";

    /// <summary>
    /// The section's one error line: a time typed wrong (shown once the field lost focus) outranks an enabled schedule
    /// with no day left; null hides the line.
    /// </summary>
    public static string ErrorFor(bool timeInvalid, bool enabled, int daysMask)
    {
        if (timeInvalid) return Invalid;
        return enabled && daysMask == 0 ? NoDays : null;
    }

    /// <summary>The section's delivery line for a state; null hides it (the server holds the schedule, or none is owed).</summary>
    public static string SyncStatus(ScheduleDelivery state) => state switch
    {
        ScheduleDelivery.Pending => SyncPending,
        ScheduleDelivery.Failed => SyncFailed,
        ScheduleDelivery.Rejected => SyncRejected,
        _ => null,
    };

    /// <summary>
    /// The «Сводка» row marker over the already composed summary (<see cref="HandoffCopy.RowSummary"/>): «Вне графика ·»
    /// when the row's last message fell outside its bot's schedule — unless the row is a REAL request, whose «Заявка ·»
    /// outranks it (one marker per row).
    /// </summary>
    public static string WithOffHoursMark(string summary, bool isRequest, bool offHours)
        => offHours && !isRequest ? OffHoursRowPrefix + (summary ?? "") : summary ?? "";
}

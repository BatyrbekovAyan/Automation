using System;

/// <summary>
/// Part B «расписание бота» (spec 2026-10-14 §4 B2): outside the bot's schedule the server answers EXACTLY as in
/// «Вместе» — no auto-reply (the Read Reply Mode term, Tools/n8n/bot_schedule.py) — so the suggestions panel must be
/// there for an «Авто» bot too, or the chat sits in silence with no panel. Like the server, the schedule outranks a
/// chat's explicit «Авто».
///
/// An EFFECTIVE-mode override, the <see cref="QuotaFallbackPolicy"/> pattern: nothing here writes a mode store or a
/// server row — the owner's chosen mode comes back untouched the moment the window opens. The same predicate and the
/// same zone resolution as the server (<see cref="ScheduleModel"/>). Pure: the caller reads the schedule and the zone.
/// </summary>
public static class ScheduleFallbackPolicy
{
    /// <param name="iana">The bot's stored zone (BusinessTimeZone.ForBot) — null or unusable reads as Asia/Almaty.</param>
    public static bool ShouldFallBackToSemi(BotSchedule schedule, DateTimeOffset nowUtc, string iana)
        => schedule.Enabled
        && !ScheduleModel.IsInsideWindowAt(nowUtc, iana, schedule.DaysMask, schedule.StartMin, schedule.EndMin);
}

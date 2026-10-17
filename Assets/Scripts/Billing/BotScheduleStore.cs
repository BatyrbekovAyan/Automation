using UnityEngine;

/// <summary>
/// Part B «расписание бота»: the bot's schedule in PlayerPrefs — four ints under the bot's slot name,
/// <c>{bot}ScheduleEnabled | ScheduleDaysMask | ScheduleStartMin | ScheduleEndMin</c>. The ONE reader and writer: the
/// settings (load, revert, snapshot, save), the «Сводка» marker, the panel's off-hours fallback and the sync queue all go
/// through it. Absent keys read as <see cref="BotSchedule.Default"/> (disabled, all days, 09:00–21:00); an out-of-range
/// value reads as its default. Writes are not flushed here — the caller saves its batch (Manager.SaveSettings).
/// </summary>
public static class BotScheduleStore
{
    public const string EnabledSuffix = "ScheduleEnabled";
    public const string DaysMaskSuffix = "ScheduleDaysMask";
    public const string StartMinSuffix = "ScheduleStartMin";
    public const string EndMinSuffix = "ScheduleEndMin";

    public static BotSchedule Read(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return BotSchedule.Default;
        var d = BotSchedule.Default;
        return BotSchedule.FromStored(
            PlayerPrefs.GetInt(botName + EnabledSuffix, 0),
            PlayerPrefs.GetInt(botName + DaysMaskSuffix, d.DaysMask),
            PlayerPrefs.GetInt(botName + StartMinSuffix, d.StartMin),
            PlayerPrefs.GetInt(botName + EndMinSuffix, d.EndMin));
    }

    public static void Write(string botName, BotSchedule schedule)
    {
        if (string.IsNullOrEmpty(botName)) return;
        PlayerPrefs.SetInt(botName + EnabledSuffix, schedule.Enabled ? 1 : 0);
        PlayerPrefs.SetInt(botName + DaysMaskSuffix, schedule.DaysMask);
        PlayerPrefs.SetInt(botName + StartMinSuffix, schedule.StartMin);
        PlayerPrefs.SetInt(botName + EndMinSuffix, schedule.EndMin);
    }

    /// <summary>Whether the owner ever saved a schedule for this bot (a Save writes the keys only when it changed).</summary>
    public static bool HasSaved(string botName) => !string.IsNullOrEmpty(botName) && PlayerPrefs.HasKey(botName + EnabledSuffix);

    public static void Clear(string botName)
    {
        if (string.IsNullOrEmpty(botName)) return;
        PlayerPrefs.DeleteKey(botName + EnabledSuffix);
        PlayerPrefs.DeleteKey(botName + DaysMaskSuffix);
        PlayerPrefs.DeleteKey(botName + StartMinSuffix);
        PlayerPrefs.DeleteKey(botName + EndMinSuffix);
    }
}

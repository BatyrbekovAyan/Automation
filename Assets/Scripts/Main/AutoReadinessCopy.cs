/// <summary>
/// Learning B + the percent (spec 2026-10-22 §4.6): every string of the soft «Авто» lock — the «ИИ-сотрудник ещё учится» sheet
/// (big percent, the mark's label, the body) and the caption «Учится · N %». RU only (house rule); the numbers come from the
/// server's row through AutoReadinessPolicy.StateFor, so the copy and the verdict cannot drift. Registered in
/// FontGlyphCoverageTests (const strings + the composed samples). Counted nouns (chats, days) agree through RuPlural.
/// A percent is always «N», a NO-BREAK space (U+00A0) and «%» — the number never parts from the sign across a line break.
/// <see cref="PillText"/> is the capsule's old «N/20» face; it goes with the capsule rework.
/// </summary>
public static class AutoReadinessCopy
{
    public const string SheetTitle = "ИИ-сотрудник ещё учится";
    public const string Understood = "Понятно";
    public const string EnableAnyway = "Включить всё равно";

    /// <summary>The mark on the sheet's bar names what opens at the threshold.</summary>
    public const string MarkerLabel = "Авто";

    private const string CappedLead = "Почти готово: ИИ-сотрудник уже угадывает ваши ответы, но нужны ответы ещё ";
    private const string PerChatTail = " Включить авто-режим в этом чате можно и сейчас.";

    /// <summary>The percent as drawn: «45 %».</summary>
    public static string Percent(int percent) => $"{percent}\u00A0%";

    /// <summary>The caption under the «Авто» capsule: «Учится · 45 %».</summary>
    public static string Caption(int percent) => $"Учится · {Percent(percent)}";

    /// <summary>
    /// The sheet's body. Ordinary: where «Авто» opens and how the percent grows. <paramref name="capped"/> (good answers, but
    /// the server holds the percent just below the threshold until the history spans enough chats and days): what is still
    /// missing, only the non-zero parts. A `capped` with nothing missing falls back to the ordinary text rather than print a
    /// broken sentence. <paramref name="perChat"/> adds the old tail for the per-chat toggle.
    /// </summary>
    public static string SheetBody(int unlockAt, bool capped, int needChats, int needDays, bool perChat)
    {
        // «откроется» — the LOCK lifts; «включится» read as an automatic switch-on, which never happens (review 2026-10-14).
        string core = capped && (needChats > 0 || needDays > 0)
            ? CappedLead + Missing(needChats, needDays) + "."
            : $"«Авто» откроется на {Percent(unlockAt)}. Процент растёт, когда панель угадывает ваш ответ: "
              + "отвечайте в режиме «Выбрать» — каждая отправка учит ИИ-сотрудника.";
        return perChat ? core + PerChatTail : core;
    }

    // «в 2 других чатах», «в 1 другой день», «в 2 других чатах и в 1 другой день» — chats first; a zero part is left out.
    private static string Missing(int needChats, int needDays)
    {
        string chats = needChats > 0
            ? $"в {needChats} {RuPlural.Pick(needChats, "другом чате", "других чатах", "других чатах")}"
            : null;
        string days = needDays > 0
            ? $"в {needDays} {RuPlural.Pick(needDays, "другой день", "других дня", "других дней")}"
            : null;
        return chats != null && days != null ? $"{chats} и {days}" : chats ?? days;
    }

    /// <summary>The capsule's old learning face: «Учится · 7/20». Removed with the capsule rework.</summary>
    public static string PillText(int matches) => $"Учится · {matches}/{AutoReadinessPolicy.Window}";
}

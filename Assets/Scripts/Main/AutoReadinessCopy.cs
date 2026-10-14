/// <summary>
/// Learning B (2026-10-14): every string of the soft «Авто» lock — the capsule's learning face and the «Бот ещё учится»
/// sheet. RU only (house rule); the numbers come from AutoReadinessPolicy so the copy and the verdict cannot drift.
/// Registered in FontGlyphCoverageTests (const strings + the three composed samples). No counted nouns here — «раз» and
/// «из» do not decline with the number — so RuPlural is not needed.
/// </summary>
public static class AutoReadinessCopy
{
    public const string SheetTitle = "Бот ещё учится";
    public const string Understood = "Понятно";
    public const string EnableAnyway = "Включить всё равно";

    public static string SheetBody(int matches, bool perChat)
    {
        string core = $"Авто включится, когда панель угадает ваш ответ {AutoReadinessPolicy.MinMatches} раз из последних "
                    + $"{AutoReadinessPolicy.Window} — сейчас {matches} из {AutoReadinessPolicy.Window}. "
                    + "Отвечайте через «Вместе»: каждая отправка учит бота.";
        return perChat ? core + " Включить авто-режим в этом чате можно и сейчас." : core;
    }

    /// <summary>The capsule's learning face: «Учится · 7/20».</summary>
    public static string PillText(int matches) => $"Учится · {matches}/{AutoReadinessPolicy.Window}";
}

/// <summary>
/// Pure decisions for one bot row inside Sheet_BotSwitcher (compact А2 layout,
/// docs/design/ui-restyle/chats-topbar-spec.md §4): whether the per-bot «Авто»
/// chip renders, and the subline text under the bot name. Flat pure-seam style
/// (ChannelSwitcherModel precedent) — EditMode-testable without a scene.
/// </summary>
public static class BotSwitcherRowModel
{
    /// <summary>
    /// The auto chip only renders for bots with at least one connected channel —
    /// an unconnected bot cannot reply at all, so offering the autopilot switch
    /// would be a lie.
    /// </summary>
    public static bool AutoChipVisible(bool waConnected, bool tgConnected) =>
        waConnected || tgConnected;

    /// <summary>
    /// Subline under the bot name: «Не подключён» when no channel is connected,
    /// otherwise «N чатов[ · M новых]» with proper Russian plurals; a connected
    /// bot with an empty cache reads «Нет чатов».
    /// </summary>
    public static string Subline(bool anyConnected, int chatCount, int unreadCount)
    {
        if (!anyConnected) return "Не подключён";
        if (chatCount <= 0) return "Нет чатов";

        string chats = $"{chatCount} {RuPlural(chatCount, "чат", "чата", "чатов")}";
        if (unreadCount <= 0) return chats;
        return $"{chats} · {unreadCount} {RuPlural(unreadCount, "новый", "новых", "новых")}";
    }

    /// <summary>
    /// Standard Russian numeral agreement: 1/21/31… → one, 2–4/22–24… → few,
    /// everything else (incl. 11–14) → many.
    /// </summary>
    public static string RuPlural(int n, string one, string few, string many)
    {
        int abs = n < 0 ? -n : n;
        int m10 = abs % 10, m100 = abs % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
        return many;
    }
}

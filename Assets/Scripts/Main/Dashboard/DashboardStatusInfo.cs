using UnityEngine;

/// <summary>
/// RU labels + pill colors for the 5 conversation-outcome statuses. Palette matches
/// BotStatusPill (order_collected reuses the active pill green) and the spec table.
/// </summary>
public static class DashboardStatusInfo
{
    public static readonly OutcomeStatus[] Ordered =
    {
        OutcomeStatus.OrderCollected,
        OutcomeStatus.OwnerNeeded,
        OutcomeStatus.InDialog,
        OutcomeStatus.ClientSilent,
        OutcomeStatus.QuestionClosed,
    };

    public static string Label(OutcomeStatus s) => s switch
    {
        OutcomeStatus.OrderCollected => "Заявка",
        OutcomeStatus.OwnerNeeded    => "Нужен владелец",
        OutcomeStatus.InDialog       => "В диалоге",
        OutcomeStatus.ClientSilent   => "Клиент замолчал",
        OutcomeStatus.QuestionClosed => "Вопрос закрыт",
        _                            => "—",
    };

    public static Color BgColor(OutcomeStatus s) => Hex(s switch
    {
        OutcomeStatus.OrderCollected => "#E8F8EE",
        OutcomeStatus.OwnerNeeded    => "#FFF3E0",
        OutcomeStatus.InDialog       => "#E3F2FF",
        OutcomeStatus.ClientSilent   => "#F2F2F7",
        OutcomeStatus.QuestionClosed => "#E4E6EB",
        _                            => "#E4E6EB",
    });

    public static Color FgColor(OutcomeStatus s) => Hex(s switch
    {
        OutcomeStatus.OrderCollected => "#34C759",
        OutcomeStatus.OwnerNeeded    => "#F57C00",
        OutcomeStatus.InDialog       => "#007AFF",
        OutcomeStatus.ClientSilent   => "#8E8E93",
        OutcomeStatus.QuestionClosed => "#65676B",
        _                            => "#65676B",
    });

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}

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

    // Theme-routed: the dashboard's status elements were bound to the Status*
    // roles by the theme pass, but this stamp used to write the pre-theme iOS
    // constants back over them on every refresh — quietly undoing the palette in
    // light and breaking contrast in dark. FgColor now resolves the same roles
    // the bindings use, so a refresh and a binding can never disagree.
    // BgColor stays static for now: the 4 non-positive soft tints have no roles
    // yet, and a light pill with a dark-sibling label stays readable in dark.
    public static Color FgColor(OutcomeStatus s) => Theme.Color(s switch
    {
        OutcomeStatus.OrderCollected => ThemeRole.StatusOrderCollected,
        OutcomeStatus.OwnerNeeded    => ThemeRole.StatusOwnerNeeded,
        OutcomeStatus.InDialog       => ThemeRole.StatusInDialog,
        OutcomeStatus.ClientSilent   => ThemeRole.StatusClientSilent,
        OutcomeStatus.QuestionClosed => ThemeRole.StatusQuestionClosed,
        _                            => ThemeRole.StatusQuestionClosed,
    });

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}

using System.Collections.Generic;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.8): «Сводка» shows nothing from a personal chat, and at once — not two days later, when the
/// server wipes what it kept about the chat. A row goes when its chat is personal for the assistant
/// (<see cref="ChatAudienceStore.KindOf"/> under the row's profile: the owner's move, else the server's decision). A chat nobody
/// has decided stays: the server sends business rows only, and a row it recorded before any decision existed is not a personal
/// chat's by anything the app knows. DashboardPage filters before its metrics, so the numbers and the rows agree; the rows
/// cached in DashboardStore and a chat made personal after its outcome was recorded are covered here.
/// </summary>
public static class DashboardAudienceFilter
{
    /// <summary>The rows «Сводка» may show, in their order.</summary>
    public static List<DashboardOutcome> WithoutPersonal(IEnumerable<DashboardOutcome> rows)
    {
        var shown = new List<DashboardOutcome>();
        if (rows == null) return shown;
        foreach (DashboardOutcome row in rows)
            if (row != null && ChatAudienceStore.KindOf(row.profileId, row.chatId) != AudienceKind.Personal) shown.Add(row);
        return shown;
    }
}

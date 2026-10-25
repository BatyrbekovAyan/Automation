using System.Collections.Generic;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.2, §5.8): what the working chat list shows, as a pure function of how each chat is drawn
/// (<see cref="ChatAudienceStore.ViewOf"/>) and whether a search is on.
///   • The list shows working chats; a search finds the personal ones too, each wearing a lock.
///   • Above the list, one «Личные» row stands for the personal chats — while there are any and no search is on — with a grey
///     counter of the personal chats that have something unread (chats, not messages).
///   • The tab counters, the bot switcher and the notifications count working chats only.
/// A missing view reads as personal: what is not known is personal (the safe side), as everywhere in the feature.
/// </summary>
public static class PersonalChatsListModel
{
    /// <summary>The chat is personal (or nothing is known about it).</summary>
    public static bool IsPersonal(AudienceView v) => v == null || v.Kind != AudienceKind.Business;

    /// <summary>The chat has a row in the working list: it is working, or a search is on.</summary>
    public static bool Visible(AudienceView v, bool searchActive) => searchActive || !IsPersonal(v);

    /// <summary>The chat's row wears the lock: a personal chat among the search results.</summary>
    public static bool ShowLock(AudienceView v, bool searchActive) => searchActive && IsPersonal(v);

    /// <summary>The «Личные» row is shown: there are personal chats and no search is on (a search shows them themselves).</summary>
    public static bool RowVisible(int personalCount, bool searchActive) => personalCount > 0 && !searchActive;

    /// <summary>How many chats are personal — the «Личные» row exists while this is above zero.</summary>
    public static int PersonalCount(IEnumerable<(AudienceView v, int unread)> chats)
    {
        int count = 0;
        if (chats == null) return count;
        foreach (var (view, _) in chats)
            if (IsPersonal(view)) count++;
        return count;
    }

    /// <summary>The «Личные» row's grey counter: the personal chats with something unread — chats, not messages.</summary>
    public static int PersonalUnreadChats(IEnumerable<(AudienceView v, int unread)> chats)
    {
        int count = 0;
        if (chats == null) return count;
        foreach (var (view, unread) in chats)
            if (IsPersonal(view) && unread > 0) count++;
        return count;
    }

    /// <summary>The unread messages of the working chats — what a tab counter and the bot switcher show.</summary>
    public static int WorkingUnreadTotal(IEnumerable<(AudienceView v, int unread)> chats)
    {
        int total = 0;
        if (chats == null) return total;
        foreach (var (view, unread) in chats)
            if (!IsPersonal(view) && unread > 0) total += unread;
        return total;
    }
}

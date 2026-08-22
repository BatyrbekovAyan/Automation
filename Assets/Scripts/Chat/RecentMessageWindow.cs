using System.Collections.Generic;

/// <summary>
/// The conversation window handed to the «Вместе» suggestions payload: the NEWEST n messages of a
/// chat, returned OLDEST-&gt;NEWEST.
///
/// <para>This seam exists because the two ends of that sentence disagreed for two months.
/// <c>ChatManager._activeChatCache</c> is NEWEST-FIRST — every assignment site sorts it that way
/// (ChatManager.cs:1019 / :1077) or concatenates brand-new server rows, which arrive newest-first,
/// on top of a list that already is (ChatManager.cs:836-852; see <see cref="MessageOrder"/>'s
/// "Times of a server response (newest-first)"). It has to be: the first-screen slice
/// <c>GetRange(0, initialCount)</c>, the pagination queue and the 100-message cap all read the
/// newest end at index 0. But <c>TryGetRecentMessages</c> took <c>GetRange(Count - n, n)</c> —
/// the LAST n of that list — and labelled the result "oldest-&gt;newest". On a newest-first list
/// that is the OLDEST n, handed back reversed.</para>
///
/// <para>Both consumers then read the wrong element. <c>N8nSuggestionsProvider.ToWireMessages</c>
/// preserves the order verbatim, and the server's Prep node walks BACKWARD from the array's end to
/// find the trailing client run — so it anchored on the chat's oldest message, found an owner reply
/// there, and produced an empty <c>queryText</c>, which the panel prompt's ВОЗДЕРЖАНИЕ rule turns
/// into an abstain. In-chat this stayed invisible: the live path also sends
/// <c>lastIncomingText</c>, and Prep's per-line merge rebuilds <c>queryText</c> from it. The
/// chat-open path sends null, so nothing repaired it and the panel showed «Нет предложений».
/// <c>SuggestionsController.CurrentTailKey</c> read the same wrong end, keying the F9 cache on the
/// chat's oldest message — an identity that never moves.</para>
///
/// <para>Pure and allocation-only: it NEVER sorts or reverses the source. <c>_activeChatCache</c>
/// must stay newest-first for the callers above, so the window is built into a new list.</para>
/// </summary>
public static class RecentMessageWindow
{
    /// <summary>
    /// The newest <paramref name="n"/> of a NEWEST-FIRST list, returned OLDEST-&gt;NEWEST, in a new
    /// list. Null/empty source or n &lt;= 0 yields an empty list (never null), so callers keep
    /// deciding emptiness by Count.
    /// </summary>
    public static List<MessageViewModel> TakeNewest(IReadOnlyList<MessageViewModel> newestFirst, int n)
    {
        var window = new List<MessageViewModel>();
        if (newestFirst == null || n <= 0) return window;

        // The newest n live at the FRONT of a newest-first list; emit them back to front so the
        // result reads oldest->newest, the order every consumer of this window documents.
        int take = n < newestFirst.Count ? n : newestFirst.Count;
        for (int i = take - 1; i >= 0; i--) window.Add(newestFirst[i]);
        return window;
    }
}

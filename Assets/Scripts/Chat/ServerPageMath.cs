using System;

/// <summary>
/// Server-page arithmetic for chat history pagination (wappi messages/get is
/// offset-paged: page N covers 0-based offsets [(N-1)*pageSize, N*pageSize)).
///
/// Exists because cache-queue drains and server fetches share one history
/// stream: the first screen and scroll-up drains are served from the local
/// store, and only after the queue empties does LoadNextPage hit the server.
/// The server cursor must resume at the page containing the first message the
/// UI hasn't been handed yet — advancing it once per drain (the old behavior)
/// skipped whole server pages, leaving permanent gaps in the history.
///
/// Rounding policy: always round DOWN to a page we may already partially hold.
/// Overlap is cheap — seenMessageIds dedups repeats and the ghost-page chain
/// in GetMessagesRoutine auto-advances past all-duplicate pages — but a
/// skipped page is never recovered.
///
/// Pure logic, unit-tested in ServerPageMathTests.
/// </summary>
public static class ServerPageMath
{
    /// <summary>1-based page whose offset range contains the given 0-based message offset.</summary>
    public static int PageContaining(int messageOffset, int pageSize)
    {
        if (pageSize <= 0) return 1;
        return Math.Max(0, messageOffset) / pageSize + 1;
    }

    /// <summary>
    /// The next history page to fetch: the page containing the first unserved
    /// offset, but never one at or below a page already fetched in full.
    /// </summary>
    /// <param name="servedFromStore">Messages handed to the UI from the local store (first batch + drains).</param>
    /// <param name="lastFetchedPage">Last server page actually fetched (0 = none yet).</param>
    /// <param name="pageSize">Messages per server page.</param>
    public static int NextServerPage(int servedFromStore, int lastFetchedPage, int pageSize)
    {
        return Math.Max(lastFetchedPage + 1, PageContaining(servedFromStore, pageSize));
    }
}

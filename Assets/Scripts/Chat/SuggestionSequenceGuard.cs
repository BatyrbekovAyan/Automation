using System;

/// <summary>
/// Discard predicate for suggestion results (DATA-03). Mirrors the conservative,
/// single-pure-bool-predicate shape of <see cref="CrossChatResponseGuard"/>: a
/// suggestion result is kept only when it is the newest request AND the active
/// chat hasn't changed under it. Reused concurrency discipline from the
/// QuoteResolve / CrossChatResponseGuard pull path — no Unity types, no network.
/// </summary>
public static class SuggestionSequenceGuard
{
    /// <summary>
    /// True only when this result is the newest issued request (<paramref name="resultSeq"/>
    /// == <paramref name="currentSeq"/>) AND the chat captured at request time still
    /// matches the active chat. Conservative on missing data: <c>null == null</c> is
    /// treated as the same chat (Ordinal compare), so a result is never discarded just
    /// because both chat ids are absent.
    /// </summary>
    public static bool IsCurrent(long resultSeq, long currentSeq, string capturedChatId, string currentChatId)
    {
        if (resultSeq != currentSeq) return false;                                   // superseded / out-of-order
        if (!string.Equals(capturedChatId, currentChatId, StringComparison.Ordinal)) // chat switched under us
            return false;
        return true;
    }
}

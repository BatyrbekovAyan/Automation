/// <summary>What the parked chat-open suggestions request should do when it finally lands.</summary>
public enum OpenChatRequestAction
{
    /// <summary>The open's own sync already armed the debounce window — its coalesced fire IS the
    /// open request (and a better one: it carries lastIncomingText). Issue nothing here.</summary>
    WaitForDebounce,

    /// <summary>The history tail hasn't moved since the last rendered set — render the F9 cached
    /// set, no paid call.</summary>
    RenderCached,

    /// <summary>First visit or a drifted tail with no fresh incoming staged — issue the request.</summary>
    Issue
}

/// <summary>
/// The decision the chat-open request park takes at settle time (2026-08-20). The request used to
/// be issued synchronously inside OnChatSelected — before OpenChatRoutine had loaded ANY history —
/// so it was assembled from a null or stale snapshot and answered the previous conversational turn
/// (abstain when that turn ended on the owner, wrong-message cards when it ended on the client).
/// Parking it to the chat-open settle (the same <see cref="SuggestionSlotOpenTiming"/> gate the
/// slot show uses) lets it read the loaded history — but by then the open's sync may ALSO have
/// staged a brand-new incoming into the debounce window, and the priority below is the rule.
///
/// PRIORITY IS LOAD-BEARING, do not reorder: an armed window beats a cache hit. During a slow open
/// the disk tail is still the OLD tail, so the cache would HIT and render the previous run's cards
/// over a chat that has a new unanswered message — while the armed fire, seconds away, already
/// carries the right context. Armed also beats Issue, or every missed-message open would pay for
/// two LLM calls (the settle request plus the fire that supersedes it).
/// </summary>
public static class SuggestionOpenRequestPolicy
{
    public static OpenChatRequestAction Resolve(bool debounceArmed, bool cacheHit)
    {
        if (debounceArmed) return OpenChatRequestAction.WaitForDebounce;
        return cacheHit ? OpenChatRequestAction.RenderCached : OpenChatRequestAction.Issue;
    }
}

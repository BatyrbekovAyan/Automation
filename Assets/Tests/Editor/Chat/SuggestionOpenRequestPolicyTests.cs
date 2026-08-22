using NUnit.Framework;

// The chat-open request park's decision table (2026-08-20). Tiny on purpose — what these pin is
// the PRIORITY, because each mis-ordering has a concrete cost the fix already paid for once:
// cache-before-armed renders the previous run's cards over a chat that has a fresh unanswered
// message (slow-open case: the disk tail is still the old tail, so the cache HITS); issue-despite-
// armed pays for two LLM calls on every missed-message open (the settle request plus the coalesced
// fire that supersedes it) and flashes the stale-tail answer first.
public class SuggestionOpenRequestPolicyTests
{
    [Test]
    public void Armed_WaitsForTheDebounceFire_EvenOverACacheHit()
    {
        Assert.AreEqual(OpenChatRequestAction.WaitForDebounce,
            SuggestionOpenRequestPolicy.Resolve(debounceArmed: true, cacheHit: true),
            "the armed fire carries lastIncomingText — a cache hit here is the OLD run's set");
        Assert.AreEqual(OpenChatRequestAction.WaitForDebounce,
            SuggestionOpenRequestPolicy.Resolve(debounceArmed: true, cacheHit: false));
    }

    [Test]
    public void NotArmed_CacheHit_RendersCached()
    {
        Assert.AreEqual(OpenChatRequestAction.RenderCached,
            SuggestionOpenRequestPolicy.Resolve(debounceArmed: false, cacheHit: true),
            "an unmoved tail renders instantly with no paid call (F9)");
    }

    [Test]
    public void NotArmed_CacheMiss_Issues()
    {
        Assert.AreEqual(OpenChatRequestAction.Issue,
            SuggestionOpenRequestPolicy.Resolve(debounceArmed: false, cacheHit: false));
    }
}

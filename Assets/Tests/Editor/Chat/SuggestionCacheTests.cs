using System.Collections.Generic;
using NUnit.Framework;

// EditMode coverage for SuggestionCache — the audit-F9 session memo that lets a chat re-open
// render its last suggestion set instantly (and skip the paid LLM call) when the history tail
// hasn't moved. Pure C#: no Unity objects, no PlayerPrefs, no network. The controller's
// capture-at-issue / verify-at-store discipline is documented at its call sites; THIS seam
// pins the cache semantics: tail-key identity, Ok-only storage, per-chat scoping, session Clear.
public class SuggestionCacheTests
{
    private static SuggestionResult Ok(string text = "ответ")
        => new SuggestionResult
        {
            status = SuggestionStatus.Ok,
            requestSeq = 1,
            items = new List<SuggestionItem> { new SuggestionItem { text = text, intentLabel = "Ответ" } }
        };

    private static MessageViewModel Msg(string id, long ts = 100, int seq = 0,
        bool incoming = true, string text = "привет")
        => new MessageViewModel { messageId = id, timestamp = ts, sequence = seq, isIncoming = incoming, text = text };

    // --- TailKey identity -----------------------------------------------------

    [Test]
    public void TailKey_NullMessage_IsNull()
    {
        Assert.IsNull(SuggestionCache.TailKey(null));
    }

    [Test]
    public void TailKey_UsesMessageId_WhenPresent()
    {
        Assert.AreEqual(SuggestionCache.TailKey(Msg("ABC")), SuggestionCache.TailKey(Msg("ABC", ts: 999)),
            "messageId alone identifies the tail — timestamps may be re-normalized between polls");
        Assert.AreNotEqual(SuggestionCache.TailKey(Msg("ABC")), SuggestionCache.TailKey(Msg("XYZ")));
    }

    [Test]
    public void TailKey_FallsBackToCompositeIdentity_WhenNoMessageId()
    {
        var a = SuggestionCache.TailKey(Msg(null, ts: 100, seq: 1, incoming: true, text: "вопрос"));
        var same = SuggestionCache.TailKey(Msg(null, ts: 100, seq: 1, incoming: true, text: "вопрос"));
        var laterTs = SuggestionCache.TailKey(Msg(null, ts: 101, seq: 1, incoming: true, text: "вопрос"));
        var otherSeq = SuggestionCache.TailKey(Msg(null, ts: 100, seq: 2, incoming: true, text: "вопрос"));
        var otherDir = SuggestionCache.TailKey(Msg(null, ts: 100, seq: 1, incoming: false, text: "вопрос"));
        Assert.AreEqual(a, same);
        Assert.AreNotEqual(a, laterTs);
        Assert.AreNotEqual(a, otherSeq);
        Assert.AreNotEqual(a, otherDir, "an owner echo with the same second must read as a NEW tail");
    }

    // --- Store / TryGet -------------------------------------------------------

    [Test]
    public void TryGet_EmptyCache_Misses()
    {
        var cache = new SuggestionCache();
        Assert.IsFalse(cache.TryGet("c1", "t1", out _));
    }

    [Test]
    public void Store_ThenTryGet_SameChatAndTail_Hits()
    {
        var cache = new SuggestionCache();
        var result = Ok();
        cache.Store("c1", "t1", result);
        Assert.IsTrue(cache.TryGet("c1", "t1", out var got));
        Assert.AreSame(result, got);
    }

    [Test]
    public void TryGet_TailMoved_Misses()
    {
        var cache = new SuggestionCache();
        cache.Store("c1", "t1", Ok());
        Assert.IsFalse(cache.TryGet("c1", "t2", out _),
            "a new message in the chat must force a fresh generation");
    }

    [Test]
    public void TryGet_OtherChat_Misses()
    {
        var cache = new SuggestionCache();
        cache.Store("c1", "t1", Ok());
        Assert.IsFalse(cache.TryGet("c2", "t1", out _));
    }

    [Test]
    public void Store_Overwrites_PreviousEntryForChat()
    {
        var cache = new SuggestionCache();
        cache.Store("c1", "t1", Ok("старый"));
        var newer = Ok("новый");
        cache.Store("c1", "t2", newer);
        Assert.IsFalse(cache.TryGet("c1", "t1", out _), "one entry per chat — the latest set wins");
        Assert.IsTrue(cache.TryGet("c1", "t2", out var got));
        Assert.AreSame(newer, got);
    }

    // --- Ok-only policy + defensive no-ops ------------------------------------

    [Test]
    public void Store_NonOkResults_AreNotCached()
    {
        var cache = new SuggestionCache();
        cache.Store("c1", "t1", new SuggestionResult { status = SuggestionStatus.Error });
        cache.Store("c1", "t1", new SuggestionResult { status = SuggestionStatus.Empty });
        Assert.IsFalse(cache.TryGet("c1", "t1", out _),
            "a cached error would freeze the error state across re-opens");
    }

    [Test]
    public void Store_NullArguments_AreNoOps()
    {
        var cache = new SuggestionCache();
        cache.Store(null, "t1", Ok());
        cache.Store("c1", null, Ok());
        cache.Store("c1", "t1", null);
        Assert.IsFalse(cache.TryGet("c1", "t1", out _));
        Assert.IsFalse(cache.TryGet(null, null, out _));
    }

    [Test]
    public void Clear_DropsEverything()
    {
        var cache = new SuggestionCache();
        cache.Store("c1", "t1", Ok());
        cache.Store("c2", "t2", Ok());
        cache.Clear();
        Assert.IsFalse(cache.TryGet("c1", "t1", out _));
        Assert.IsFalse(cache.TryGet("c2", "t2", out _));
    }
}

/// <summary>S6 (audit stage 5): the cache knows about scope, knowledge version and time.</summary>
public class SuggestionCacheScopeTests
{
    private static SuggestionResult Ok() => new SuggestionResult { status = SuggestionStatus.Ok, requestSeq = 1,
        items = new System.Collections.Generic.List<SuggestionItem> { new SuggestionItem { text = "ответ", intentLabel = "Ответ" } } };

    [Test]
    public void KnowledgeVersionChange_Misses_SameVersionHits()
    {
        var cache = new SuggestionCache();
        cache.SetKnowledgeVersion("v1");
        cache.Store("c1", "t1", Ok());
        Assert.IsTrue(cache.TryGet("c1", "t1", out _));
        cache.SetKnowledgeVersion("v2");
        Assert.IsFalse(cache.TryGet("c1", "t1", out _), "a price/contact/prompt edit must not serve the old set");
        cache.SetKnowledgeVersion("v1");
        Assert.IsTrue(cache.TryGet("c1", "t1", out _), "the version is a key, not an invalidation counter");
    }

    [Test]
    public void ChannelOrProfileSwitch_Misses_SwitchingBackHits()
    {
        var cache = new SuggestionCache();
        cache.SetScope("Bot0", "whatsapp", "wa1");
        cache.Store("77@c.us", "t1", Ok());
        cache.SetScope("Bot0", "telegram", "tg1");
        Assert.IsFalse(cache.TryGet("77@c.us", "t1", out _), "a chat id is not unique across channels/profiles");
        cache.SetScope("Bot0", "whatsapp", "wa1");
        Assert.IsTrue(cache.TryGet("77@c.us", "t1", out _), "the useful re-open hit survives a round trip");
    }

    [Test]
    public void Ttl_Expires_TimeSensitiveAnswers()
    {
        long now = 1_000_000;
        var cache = new SuggestionCache { NowMs = () => now, TtlMs = 10 * 60 * 1000 };
        cache.Store("c1", "t1", Ok());
        now += 9 * 60 * 1000;
        Assert.IsTrue(cache.TryGet("c1", "t1", out _));
        now += 2 * 60 * 1000;
        Assert.IsFalse(cache.TryGet("c1", "t1", out _), "«сейчас открыты» must not outlive the hour");
    }
}

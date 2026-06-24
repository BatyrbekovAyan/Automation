using NUnit.Framework;

public class SuggestionSequenceGuardTests
{
    private const string ChatA = "chatA@c.us";
    private const string ChatB = "chatB@c.us";

    // --- The keep case: newest seq, same chat ----------------------------------

    [Test]
    public void NewestSeq_SameChat_IsCurrent()
        => Assert.IsTrue(SuggestionSequenceGuard.IsCurrent(5, 5, ChatA, ChatA));

    // --- Superseded / out-of-order seq is discarded ----------------------------

    [Test]
    public void OlderSeq_IsDiscarded()
        => Assert.IsFalse(SuggestionSequenceGuard.IsCurrent(4, 5, ChatA, ChatA));

    // --- Chat switched under the in-flight request is discarded ----------------

    [Test]
    public void ChatSwitched_IsDiscarded()
        => Assert.IsFalse(SuggestionSequenceGuard.IsCurrent(5, 5, ChatA, ChatB));

    // --- Both stale AND chat switched is discarded -----------------------------

    [Test]
    public void StaleSeqAndChatSwitched_IsDiscarded()
        => Assert.IsFalse(SuggestionSequenceGuard.IsCurrent(4, 5, ChatA, ChatB));

    // --- Conservative: equal seq with both chat ids null is kept ---------------

    [Test]
    public void EqualSeq_BothChatIdsNull_IsCurrent()
        => Assert.IsTrue(SuggestionSequenceGuard.IsCurrent(5, 5, null, null));
}

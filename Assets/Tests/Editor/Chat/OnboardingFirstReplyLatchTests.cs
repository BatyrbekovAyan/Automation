using System.Collections.Generic;
using NUnit.Framework;

// Covers OnboardingFirstReplyLatch.ShouldLatch — the pure decision behind the
// «Первые шаги» row-4 milestone. The PlayerPrefs write + card refresh side effects
// live in TryLatch (MonoBehaviour-free but I/O-bound, exercised on device); the
// decision itself is fully unit-tested here.
public class OnboardingFirstReplyLatchTests
{
    private static MessageViewModel Msg(bool incoming) => new MessageViewModel { isIncoming = incoming };

    [Test]
    public void ShouldLatch_OutgoingPresent_True()
        => Assert.IsTrue(OnboardingFirstReplyLatch.ShouldLatch(false,
            new List<MessageViewModel> { Msg(true), Msg(false) }),
            "Any outgoing (isIncoming==false) message is the bot-replied proxy.");

    [Test]
    public void ShouldLatch_OnlyIncoming_False()
        => Assert.IsFalse(OnboardingFirstReplyLatch.ShouldLatch(false,
            new List<MessageViewModel> { Msg(true), Msg(true) }));

    [Test]
    public void ShouldLatch_AlreadyLatched_False_EvenWithOutgoing()
        => Assert.IsFalse(OnboardingFirstReplyLatch.ShouldLatch(true,
            new List<MessageViewModel> { Msg(false) }),
            "Latched is terminal — no repeated PlayerPrefs writes.");

    [Test]
    public void ShouldLatch_NullOrEmpty_False()
    {
        Assert.IsFalse(OnboardingFirstReplyLatch.ShouldLatch(false, null));
        Assert.IsFalse(OnboardingFirstReplyLatch.ShouldLatch(false, new List<MessageViewModel>()));
    }

    [Test]
    public void ShouldLatch_NullEntriesTolerated()
        => Assert.IsTrue(OnboardingFirstReplyLatch.ShouldLatch(false,
            new List<MessageViewModel> { null, Msg(false) }));
}

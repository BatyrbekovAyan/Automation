using NUnit.Framework;

// Covers OnboardingGate — the pure first-run gate + existing-user auto-flag
// predicates. Show the carousel only when there are NO bots AND the flag is unset;
// auto-flag existing users so a later delete-all within the session never
// resurfaces the carousel.
public class OnboardingGateTests
{
    [Test]
    public void ShowCarousel_FirstRun_NoBots_FlagUnset_True()
        => Assert.IsTrue(OnboardingGate.ShouldShowCarousel(hasBots: false, seen: false),
            "First run (no bots, flag unset) must show the carousel.");

    [Test]
    public void ShowCarousel_NoBots_AlreadySeen_False()
        => Assert.IsFalse(OnboardingGate.ShouldShowCarousel(hasBots: false, seen: true),
            "Already-seen users never see the carousel again, even with no bots.");

    [Test]
    public void ShowCarousel_ExistingUser_BotsPresent_FlagUnset_False()
        => Assert.IsFalse(OnboardingGate.ShouldShowCarousel(hasBots: true, seen: false),
            "Existing users (bots present) never see the first-run carousel.");

    [Test]
    public void ShowCarousel_BotsPresent_AlreadySeen_False()
        => Assert.IsFalse(OnboardingGate.ShouldShowCarousel(hasBots: true, seen: true),
            "Bots present and already seen ⇒ never show.");

    [Test]
    public void AutoFlagSeen_ExistingUser_FlagUnset_True()
        => Assert.IsTrue(OnboardingGate.ShouldAutoFlagSeen(hasBots: true, seen: false),
            "Existing user with the flag unset must be auto-flagged (later delete-all must not resurface the carousel).");

    [Test]
    public void AutoFlagSeen_NoBots_FlagUnset_False()
        => Assert.IsFalse(OnboardingGate.ShouldAutoFlagSeen(hasBots: false, seen: false),
            "No bots ⇒ do not auto-flag (a genuine first-run user must still see the carousel).");

    [Test]
    public void AutoFlagSeen_BotsPresent_AlreadyFlagged_False()
        => Assert.IsFalse(OnboardingGate.ShouldAutoFlagSeen(hasBots: true, seen: true),
            "Already-flagged ⇒ nothing to do.");
}

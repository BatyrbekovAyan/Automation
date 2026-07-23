using NUnit.Framework;

// Covers FirstStepsCardVisibility — the pure D1 visibility rule for the «Первые
// шаги» card. Zero bots ⇒ the EmptyState owns the screen (card hidden); a bot present
// mid-onboarding ⇒ card shows; the permanent 4/4 completion latch ⇒ hidden forever.
// Facts (bot count, ChecklistDone latch) are supplied by the MonoBehaviour; this class
// stays pure.
public class FirstStepsCardVisibilityTests
{
    [Test]
    public void ShouldShow_ZeroBots_Hidden()
        => Assert.IsFalse(FirstStepsCardVisibility.ShouldShow(hasBots: false, checklistDone: false),
            "Zero bots ⇒ the EmptyState is the step-1 guidance, so the card stays hidden (D1).");

    [Test]
    public void ShouldShow_BotPresentMidOnboarding_Shown()
        => Assert.IsTrue(FirstStepsCardVisibility.ShouldShow(hasBots: true, checklistDone: false),
            "≥1 bot and the checklist not yet complete ⇒ the card shows.");

    [Test]
    public void ShouldShow_BotPresentChecklistDone_Hidden()
        => Assert.IsFalse(FirstStepsCardVisibility.ShouldShow(hasBots: true, checklistDone: true),
            "Permanent 4/4 completion ⇒ the card is hidden forever, even with bots present.");

    [Test]
    public void ShouldShow_ZeroBotsChecklistDone_Hidden()
        => Assert.IsFalse(FirstStepsCardVisibility.ShouldShow(hasBots: false, checklistDone: true),
            "Both hide reasons at once ⇒ hidden.");
}

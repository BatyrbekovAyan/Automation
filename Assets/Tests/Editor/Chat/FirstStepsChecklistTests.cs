using NUnit.Framework;

// Covers FirstStepsChecklist — pure step-state + milestone + completion
// derivation for the «Первые шаги» card. Facts (bot count, channel auth, uploaded
// files, first-reply latch) are supplied by the MonoBehaviour; this class stays pure.
public class FirstStepsChecklistTests
{
    [Test]
    public void Milestone_LatchedStaysDone_EvenWhenLiveFactRegresses()
        => Assert.IsTrue(FirstStepsChecklist.Milestone(latched: true, liveFact: false),
            "A previously achieved step never regresses (messenger toggled off, files deleted).");

    [Test]
    public void Milestone_LiveFactAchieves()
        => Assert.IsTrue(FirstStepsChecklist.Milestone(latched: false, liveFact: true));

    [Test]
    public void Milestone_NeitherIsNotDone()
        => Assert.IsFalse(FirstStepsChecklist.Milestone(latched: false, liveFact: false));

    [Test]
    public void Milestone_BothIsDone()
        => Assert.IsTrue(FirstStepsChecklist.Milestone(latched: true, liveFact: true));

    [Test]
    public void StepStates_ReturnsFourBoolsInOrder()
    {
        var steps = FirstStepsChecklist.StepStates(botExists: true, channelAuthed: false, hasFiles: true, firstReplySeen: false);
        Assert.AreEqual(new[] { true, false, true, false }, steps,
            "Step order: create bot · connect channel · upload price list · first reply.");
    }

    [Test]
    public void AllDone_AllTrue_True()
        => Assert.IsTrue(FirstStepsChecklist.AllDone(new[] { true, true, true, true }));

    [Test]
    public void AllDone_AnyFalse_False()
        => Assert.IsFalse(FirstStepsChecklist.AllDone(new[] { true, true, false, true }));

    [Test]
    public void AllDone_Empty_True()
        => Assert.IsTrue(FirstStepsChecklist.AllDone(new bool[0]),
            "Array.TrueForAll on an empty array is vacuously true (documented semantics).");
}

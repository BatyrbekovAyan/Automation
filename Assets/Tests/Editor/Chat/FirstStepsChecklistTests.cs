using NUnit.Framework;

// Covers FirstStepsChecklist — pure channel-label + step-state + completion
// derivation for the «Первые шаги» card. Facts (bot count, channel auth, uploaded
// files, first-reply latch) are supplied by the MonoBehaviour; this class stays pure.
public class FirstStepsChecklistTests
{
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

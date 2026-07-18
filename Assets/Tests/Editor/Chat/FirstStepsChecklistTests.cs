using NUnit.Framework;

// Covers FirstStepsChecklist — pure channel-label + step-state + completion
// derivation for the «Первые шаги» card. Facts (bot count, channel auth, uploaded
// files, first-reply latch) are supplied by the MonoBehaviour; this class stays pure.
public class FirstStepsChecklistTests
{
    [Test]
    public void ChannelLabel_WhatsAppOnly_WhatsApp()
        => Assert.AreEqual("WhatsApp", FirstStepsChecklist.ChannelLabel(isOnWhatsapp: true, isOnTelegram: false));

    [Test]
    public void ChannelLabel_TelegramOnly_Telegram()
        => Assert.AreEqual("Telegram", FirstStepsChecklist.ChannelLabel(isOnWhatsapp: false, isOnTelegram: true));

    [Test]
    public void ChannelLabel_Both_WhatsAppWins()
        => Assert.AreEqual("WhatsApp", FirstStepsChecklist.ChannelLabel(isOnWhatsapp: true, isOnTelegram: true),
            "WhatsApp wins the dual-channel case.");

    [Test]
    public void ChannelLabel_Neither_FallsBackToTelegram()
        => Assert.AreEqual("Telegram", FirstStepsChecklist.ChannelLabel(isOnWhatsapp: false, isOnTelegram: false),
            "Neither flag set ⇒ Telegram fallback (only reachable if both PlayerPrefs defaults were cleared).");

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

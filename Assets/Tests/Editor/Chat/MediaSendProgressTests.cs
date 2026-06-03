using NUnit.Framework;

public class MediaSendProgressTests
{
    // Phase floors/ceilings on the whole-pipeline axis (30 / 10 / 50 split; the
    // final 10% is the server-ack window, completed in the view — not by this map).
    [TestCase(ChatManager.SendPhase.Convert, 0f, 0.00f)]
    [TestCase(ChatManager.SendPhase.Convert, 1f, 0.30f)]
    [TestCase(ChatManager.SendPhase.Encode,  0f, 0.30f)]
    [TestCase(ChatManager.SendPhase.Encode,  1f, 0.40f)]
    [TestCase(ChatManager.SendPhase.Upload,  0f, 0.40f)]
    [TestCase(ChatManager.SendPhase.Upload,  1f, 0.90f)]
    public void SendProgress_PhaseBoundaries_MapToPipelineFractions(
        ChatManager.SendPhase phase, float sub, float expected)
    {
        Assert.AreEqual(expected, ChatManager.SendProgress(phase, sub), 1e-4f);
    }

    [TestCase(ChatManager.SendPhase.Convert, -0.5f, 0.00f)] // clamps low
    [TestCase(ChatManager.SendPhase.Convert,  2.0f, 0.30f)] // clamps high
    [TestCase(ChatManager.SendPhase.Upload,  -1.0f, 0.40f)]
    [TestCase(ChatManager.SendPhase.Upload,   5.0f, 0.90f)]
    public void SendProgress_ClampsSubProgress(
        ChatManager.SendPhase phase, float sub, float expected)
    {
        Assert.AreEqual(expected, ChatManager.SendProgress(phase, sub), 1e-4f);
    }

    [Test]
    public void SendProgress_IsMonotonicAcrossPhases()
    {
        float convertMid = ChatManager.SendProgress(ChatManager.SendPhase.Convert, 0.5f);
        float convertEnd = ChatManager.SendProgress(ChatManager.SendPhase.Convert, 1f);
        float encodeEnd  = ChatManager.SendProgress(ChatManager.SendPhase.Encode, 1f);
        float uploadMid  = ChatManager.SendProgress(ChatManager.SendPhase.Upload, 0.5f);
        float uploadEnd  = ChatManager.SendProgress(ChatManager.SendPhase.Upload, 1f);

        Assert.Less(convertMid, convertEnd);
        Assert.LessOrEqual(convertEnd, encodeEnd);
        Assert.Less(encodeEnd, uploadMid);
        Assert.Less(uploadMid, uploadEnd);
        Assert.AreEqual(0.90f, uploadEnd, 1e-4f);   // byte-upload ceiling; ack adds the last 0.10 in the view
    }

    [Test]
    public void SendProgress_UnknownPhase_ReturnsZero()
    {
        Assert.AreEqual(0f, ChatManager.SendProgress((ChatManager.SendPhase)999, 0.5f), 1e-4f);
    }
}

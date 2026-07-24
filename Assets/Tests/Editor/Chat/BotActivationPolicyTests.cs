using NUnit.Framework;

public class BotActivationPolicyTests
{
    // A channel's workflow is active ONLY when both gates are on:
    // the bot master switch AND that channel's own toggle.
    //
    // master  channel  => active
    [TestCase(true,  true,  true)]   // both on          → running
    [TestCase(true,  false, false)]  // master on, ch off → off (only enabled channels run)
    [TestCase(false, true,  false)]  // master off, ch on → off (the reported bug: must NOT run)
    [TestCase(false, false, false)]  // both off         → off
    public void ChannelWorkflowActive_IsLogicalAnd(bool masterOn, bool channelOn, bool expected)
    {
        Assert.AreEqual(expected, BotActivationPolicy.ChannelWorkflowActive(masterOn, channelOn));
    }
}

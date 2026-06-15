using NUnit.Framework;

public class WhatsAppSyncGateTests
{
    [Test] public void IsSyncing_FutureEpoch_True()  => Assert.IsTrue(WhatsAppSyncGate.IsSyncing(2000L, 1000L));
    [Test] public void IsSyncing_PastEpoch_False()   => Assert.IsFalse(WhatsAppSyncGate.IsSyncing(1000L, 2000L));
    [Test] public void IsSyncing_EqualEpoch_False()  => Assert.IsFalse(WhatsAppSyncGate.IsSyncing(1000L, 1000L));

    [Test] public void RemainingMs_Clamped()
    {
        Assert.AreEqual(3000L, WhatsAppSyncGate.RemainingMs(5000L, 2000L));
        Assert.AreEqual(0L,    WhatsAppSyncGate.RemainingMs(1000L, 5000L));
    }

    [Test] public void ProgressFraction_StartHalfEnd()
    {
        Assert.AreEqual(0f,   WhatsAppSyncGate.ProgressFraction(1_000_000L + 300_000L, 1_000_000L, 300), 0.001f);
        Assert.AreEqual(0.5f, WhatsAppSyncGate.ProgressFraction(1_000_000L + 150_000L, 1_000_000L, 300), 0.001f);
        Assert.AreEqual(1f,   WhatsAppSyncGate.ProgressFraction(1_000_000L,            2_000_000L, 300), 0.001f);
    }

    [Test] public void ProgressFraction_ZeroWindow_Full()
        => Assert.AreEqual(1f, WhatsAppSyncGate.ProgressFraction(0L, 0L, 0), 0.001f);

    [Test] public void FormatCountdown_Buckets()
    {
        Assert.AreEqual("Finishing up…",          WhatsAppSyncGate.FormatCountdown(0L));
        Assert.AreEqual("Less than a minute left", WhatsAppSyncGate.FormatCountdown(30_000L));
        Assert.AreEqual("Less than a minute left", WhatsAppSyncGate.FormatCountdown(60_000L));
        Assert.AreEqual("About 2 min left",        WhatsAppSyncGate.FormatCountdown(90_000L));
        Assert.AreEqual("About 5 min left",        WhatsAppSyncGate.FormatCountdown(300_000L));
    }
}

public class WhatsAppTabStateResolverTests
{
    [Test] public void NoBots_WinsOverEverything()
        => Assert.AreEqual(WhatsAppTabState.NoBots, WhatsAppTabStateResolver.Resolve(0, true, true));

    [Test] public void NoWhatsApp_WhenBotLacksProfile()
        => Assert.AreEqual(WhatsAppTabState.NoWhatsApp, WhatsAppTabStateResolver.Resolve(1, false, false));

    [Test] public void Syncing_WhenConnectedAndInWindow()
        => Assert.AreEqual(WhatsAppTabState.Syncing, WhatsAppTabStateResolver.Resolve(1, true, true));

    [Test] public void Ready_WhenConnectedAndWindowClosed()
        => Assert.AreEqual(WhatsAppTabState.Ready, WhatsAppTabStateResolver.Resolve(1, true, false));
}

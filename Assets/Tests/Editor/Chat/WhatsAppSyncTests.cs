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

// 08-19 D13a: the post-creation sync window is per-channel — Telegram gets a sibling
// PlayerPrefs key and the same gate math; WhatsApp stays byte-identical (legacy suffix,
// same parse + WhatsAppSyncGate semantics).
public class ChannelSyncGateTests
{
    [Test] public void SuffixFor_WhatsApp_KeepsLegacyKey()
        => Assert.AreEqual("WhatsappSyncUntil", ChatManager.SyncUntilSuffixFor(ChatChannel.WhatsApp));

    [Test] public void SuffixFor_Telegram_SiblingKey()
        => Assert.AreEqual("TelegramSyncUntil", ChatManager.SyncUntilSuffixFor(ChatChannel.Telegram));

    [Test] public void IsSyncingRawValue_FutureEpoch_TrueWithParsedUntil()
    {
        Assert.IsTrue(ChatManager.IsSyncingRawValue("2000", 1000L, out long until));
        Assert.AreEqual(2000L, until);
    }

    [Test] public void IsSyncingRawValue_PastEpoch_FalseButUntilParsed()
    {
        Assert.IsFalse(ChatManager.IsSyncingRawValue("1000", 2000L, out long until));
        Assert.AreEqual(1000L, until);
    }

    [Test] public void IsSyncingRawValue_MissingKeyDefault_False()
        => Assert.IsFalse(ChatManager.IsSyncingRawValue("0", 1000L, out _));

    [Test] public void IsSyncingRawValue_Unparseable_FailSafeFalse()
    {
        Assert.IsFalse(ChatManager.IsSyncingRawValue("", 1000L, out long until));
        Assert.AreEqual(0L, until);
        Assert.IsFalse(ChatManager.IsSyncingRawValue("garbage", 1000L, out until));
        Assert.AreEqual(0L, until);
    }
}

// 08-19 D13a: the cover's countdown label — WhatsApp keeps WhatsAppSyncGate's English
// buckets byte-identically; Telegram mirrors the same rounding buckets in Russian
// (the app's Telegram-facing copy is Russian, matching the RU title/body/footnote).
public class SyncingCountdownCopyTests
{
    [Test] public void WhatsApp_DelegatesToGate_ByteIdentical()
    {
        Assert.AreEqual(WhatsAppSyncGate.FormatCountdown(0L),       SyncingView.FormatCountdownFor(ChatChannel.WhatsApp, 0L));
        Assert.AreEqual(WhatsAppSyncGate.FormatCountdown(30_000L),  SyncingView.FormatCountdownFor(ChatChannel.WhatsApp, 30_000L));
        Assert.AreEqual(WhatsAppSyncGate.FormatCountdown(90_000L),  SyncingView.FormatCountdownFor(ChatChannel.WhatsApp, 90_000L));
        Assert.AreEqual(WhatsAppSyncGate.FormatCountdown(300_000L), SyncingView.FormatCountdownFor(ChatChannel.WhatsApp, 300_000L));
    }

    [Test] public void Telegram_RussianBuckets()
    {
        Assert.AreEqual("Завершаем…",             SyncingView.FormatCountdownFor(ChatChannel.Telegram, 0L));
        Assert.AreEqual("Осталось меньше минуты", SyncingView.FormatCountdownFor(ChatChannel.Telegram, 30_000L));
        Assert.AreEqual("Осталось меньше минуты", SyncingView.FormatCountdownFor(ChatChannel.Telegram, 60_000L));
        Assert.AreEqual("Осталось около 2 мин",   SyncingView.FormatCountdownFor(ChatChannel.Telegram, 90_000L));
        Assert.AreEqual("Осталось около 5 мин",   SyncingView.FormatCountdownFor(ChatChannel.Telegram, 300_000L));
    }
}

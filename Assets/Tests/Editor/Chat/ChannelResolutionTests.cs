using NUnit.Framework;

// Covers ChannelResolver.Resolve — the pure decision behind
// ChatManager.ResolveChannelForBot (bot switch / startup channel restore).
// Rule: keep the persisted channel if connected; else fall back to the OTHER
// channel when IT is connected; else keep the persisted/default channel.
public class ChannelResolutionTests
{
    private const int Wa = (int)ChatChannel.WhatsApp; // 0
    private const int Tg = (int)ChatChannel.Telegram; // 1

    [Test]
    public void BothConnected_KeepsPersisted_WhatsApp()
        => Assert.AreEqual(ChatChannel.WhatsApp, ChannelResolver.Resolve(Wa, waConnected: true, tgConnected: true));

    [Test]
    public void BothConnected_KeepsPersisted_Telegram()
        => Assert.AreEqual(ChatChannel.Telegram, ChannelResolver.Resolve(Tg, waConnected: true, tgConnected: true));

    [Test]
    public void PersistedTelegram_ButOnlyWhatsAppConnected_FallsBackToWhatsApp()
        => Assert.AreEqual(ChatChannel.WhatsApp, ChannelResolver.Resolve(Tg, waConnected: true, tgConnected: false));

    [Test]
    public void PersistedWhatsApp_ButOnlyTelegramConnected_FallsBackToTelegram()
        => Assert.AreEqual(ChatChannel.Telegram, ChannelResolver.Resolve(Wa, waConnected: false, tgConnected: true));

    [Test]
    public void NeitherConnected_KeepsPersisted_WhatsApp()
        => Assert.AreEqual(ChatChannel.WhatsApp, ChannelResolver.Resolve(Wa, waConnected: false, tgConnected: false));

    [Test]
    public void NeitherConnected_KeepsPersisted_Telegram()
        => Assert.AreEqual(ChatChannel.Telegram, ChannelResolver.Resolve(Tg, waConnected: false, tgConnected: false));

    [Test]
    public void PersistedChannelConnected_NeverFallsBack()
    {
        // Persisted WhatsApp connected while Telegram also connected → stays WhatsApp.
        Assert.AreEqual(ChatChannel.WhatsApp, ChannelResolver.Resolve(Wa, waConnected: true, tgConnected: true));
        // Persisted Telegram connected while WhatsApp also connected → stays Telegram.
        Assert.AreEqual(ChatChannel.Telegram, ChannelResolver.Resolve(Tg, waConnected: true, tgConnected: true));
    }

    // Defensive: a tampered/out-of-range persisted ordinal (PlayerPrefs are user-editable)
    // is clamped to WhatsApp before the decision, never reaching an undefined enum value.
    [Test]
    public void OutOfRangePersisted_ClampsToWhatsApp_WhenWhatsAppConnected()
        => Assert.AreEqual(ChatChannel.WhatsApp, ChannelResolver.Resolve(99, waConnected: true, tgConnected: false));

    [Test]
    public void OutOfRangePersisted_ClampedToWhatsApp_FallsBackToTelegram_WhenOnlyTelegram()
        => Assert.AreEqual(ChatChannel.Telegram, ChannelResolver.Resolve(99, waConnected: false, tgConnected: true));

    [Test]
    public void NegativePersisted_ClampsToWhatsApp()
        => Assert.AreEqual(ChatChannel.WhatsApp, ChannelResolver.Resolve(-1, waConnected: false, tgConnected: false));
}

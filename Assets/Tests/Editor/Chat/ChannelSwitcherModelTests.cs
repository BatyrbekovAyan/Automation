using NUnit.Framework;

// Covers ChannelSwitcherModel.StateFor — the pure per-chip decision behind the TopBar
// channel switcher. Selected = this chip is the active channel (equality only); Muted =
// this chip's OWN channel is unconnected (connectivity only). A chip can be both Selected
// and Muted (active channel on a disconnected profile) — muted is never suppressed by
// selection. Matrix: both chips × active ∈ {WhatsApp, Telegram} × connectivity (Tests A–E).
public class ChannelSwitcherModelTests
{
    private static ChannelChipState Wa(ChatChannel active, bool wa, bool tg)
        => ChannelSwitcherModel.StateFor(ChatChannel.WhatsApp, active, wa, tg);

    private static ChannelChipState Tg(ChatChannel active, bool wa, bool tg)
        => ChannelSwitcherModel.StateFor(ChatChannel.Telegram, active, wa, tg);

    // Test A: active=WhatsApp, wa=true, tg=false
    // → WA chip {selected=true, muted=false}; TG chip {selected=false, muted=true}
    [Test]
    public void A_ActiveWhatsApp_WaConnected_TgDisconnected()
    {
        var wa = Wa(ChatChannel.WhatsApp, wa: true, tg: false);
        Assert.IsTrue(wa.Selected, "WA chip selected when WhatsApp is active");
        Assert.IsFalse(wa.Muted, "WA chip not muted when WhatsApp is connected");

        var tg = Tg(ChatChannel.WhatsApp, wa: true, tg: false);
        Assert.IsFalse(tg.Selected, "TG chip not selected when WhatsApp is active");
        Assert.IsTrue(tg.Muted, "TG chip muted when Telegram is disconnected");
    }

    // Test B: active=Telegram, wa=true, tg=true
    // → WA chip {selected=false, muted=false}; TG chip {selected=true, muted=false}
    [Test]
    public void B_ActiveTelegram_BothConnected()
    {
        var wa = Wa(ChatChannel.Telegram, wa: true, tg: true);
        Assert.IsFalse(wa.Selected);
        Assert.IsFalse(wa.Muted);

        var tg = Tg(ChatChannel.Telegram, wa: true, tg: true);
        Assert.IsTrue(tg.Selected);
        Assert.IsFalse(tg.Muted);
    }

    // Test C: active=Telegram, wa=false, tg=true
    // → WA chip {selected=false, muted=true}; TG chip {selected=true, muted=false}
    [Test]
    public void C_ActiveTelegram_WaDisconnected()
    {
        var wa = Wa(ChatChannel.Telegram, wa: false, tg: true);
        Assert.IsFalse(wa.Selected);
        Assert.IsTrue(wa.Muted, "WA chip muted when WhatsApp is disconnected");

        var tg = Tg(ChatChannel.Telegram, wa: false, tg: true);
        Assert.IsTrue(tg.Selected);
        Assert.IsFalse(tg.Muted);
    }

    // Test D: active=WhatsApp, wa=false, tg=true — selected-but-unconnected edge
    // → WA chip {selected=true, muted=true}; TG chip {selected=false, muted=false}
    [Test]
    public void D_ActiveWhatsApp_ButWhatsAppDisconnected_SelectedAndMuted()
    {
        var wa = Wa(ChatChannel.WhatsApp, wa: false, tg: true);
        Assert.IsTrue(wa.Selected, "WA chip selected (active) even though disconnected");
        Assert.IsTrue(wa.Muted, "WA chip muted because WhatsApp is disconnected");

        var tg = Tg(ChatChannel.WhatsApp, wa: false, tg: true);
        Assert.IsFalse(tg.Selected);
        Assert.IsFalse(tg.Muted);
    }

    // Test E: active=WhatsApp, wa=false, tg=false — both muted, WA selected
    // → WA chip {selected=true, muted=true}; TG chip {selected=false, muted=true}
    [Test]
    public void E_ActiveWhatsApp_NeitherConnected()
    {
        var wa = Wa(ChatChannel.WhatsApp, wa: false, tg: false);
        Assert.IsTrue(wa.Selected);
        Assert.IsTrue(wa.Muted);

        var tg = Tg(ChatChannel.WhatsApp, wa: false, tg: false);
        Assert.IsFalse(tg.Selected);
        Assert.IsTrue(tg.Muted);
    }
}

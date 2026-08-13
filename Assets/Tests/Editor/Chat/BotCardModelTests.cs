using NUnit.Framework;

/// <summary>
/// Pins the C2 bot-card decision matrix (sketch 006, locked 2026-08-13):
/// channel icon three-state rule, the confirm asymmetry the capsule shares
/// with the chats-header «Авто» button, and the connecting-subline override.
/// Replaces BotSwitchFooterTests — the footer (label + iOS switch) is gone.
/// </summary>
public class BotCardModelTests
{
    // ---- IsConnected -----------------------------------------------------

    [Test]
    public void Connected_RequiresRealProfileId()
    {
        Assert.IsTrue(BotCardModel.IsConnected("abc123"));
        Assert.IsFalse(BotCardModel.IsConnected(""), "empty id is not a connection");
        Assert.IsFalse(BotCardModel.IsConnected(null), "null id is not a connection");
        Assert.IsFalse(BotCardModel.IsConnected(Bot.UnauthedProfileSentinel),
            "the \"-1\" unauthed sentinel must never read as connected");
    }

    // ---- Icon three-state rule ------------------------------------------

    [Test]
    public void Icon_Hidden_WhenChannelNotConnected()
    {
        // No icon at all — regardless of the channel toggle.
        Assert.AreEqual(BotChannelIconState.Hidden, BotCardModel.IconState("", true));
        Assert.AreEqual(BotChannelIconState.Hidden, BotCardModel.IconState("", false));
        Assert.AreEqual(BotChannelIconState.Hidden,
            BotCardModel.IconState(Bot.UnauthedProfileSentinel, true));
    }

    [Test]
    public void Icon_Colored_WhenConnectedAndEnabled()
    {
        Assert.AreEqual(BotChannelIconState.Colored, BotCardModel.IconState("profile1", true));
    }

    [Test]
    public void Icon_Muted_WhenConnectedButToggledOff()
    {
        Assert.AreEqual(BotChannelIconState.Muted, BotCardModel.IconState("profile1", false));
    }

    // ---- Confirm asymmetry ----------------------------------------------

    [Test]
    public void Confirm_OnlyWhenEnabling()
    {
        Assert.IsTrue(BotCardModel.ConfirmRequired(masterOn: false),
            "OFF → ON starts messaging real clients — must confirm");
        Assert.IsFalse(BotCardModel.ConfirmRequired(masterOn: true),
            "ON → OFF is the safe direction — instant");
    }

    // ---- Subline ---------------------------------------------------------

    [Test]
    public void Subline_ConnectingWordReplacesBusinessType()
    {
        Assert.AreEqual("Подключение…", BotCardModel.SublineText(true, "Цветочный магазин"));
    }

    [Test]
    public void Subline_ShowsBusinessTypeWhenNotConnecting()
    {
        Assert.AreEqual("Цветочный магазин", BotCardModel.SublineText(false, "Цветочный магазин"));
        Assert.AreEqual("", BotCardModel.SublineText(false, null),
            "legacy/unset business type renders as empty, never null");
    }
}

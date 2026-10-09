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

    // Confirm asymmetry is AutoButtonModel's contract since the unification —
    // the card feeds ReplyModeToggleBinder.GetMode into AutoButtonModel
    // directly, and AutoButtonModelTests pin that matrix.

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

    // ---- M4: a channel the server retired (2026-10-09) ---------------------

    [Test]
    public void Icon_Muted_WhenTheServerRetiredTheChannel_WhateverItsToggle()
    {
        Assert.AreEqual(BotChannelIconState.Muted, BotCardModel.IconState("profile1", true, retired: true),
            "a retired channel never shows in brand colour — the bot cannot answer there");
        Assert.AreEqual(BotChannelIconState.Muted, BotCardModel.IconState("profile1", false, retired: true));
        Assert.AreEqual(BotChannelIconState.Colored, BotCardModel.IconState("profile1", true, retired: false));
        Assert.AreEqual(BotChannelIconState.Hidden, BotCardModel.IconState(Bot.UnauthedProfileSentinel, true, retired: true),
            "no profile, no icon — a marker cannot conjure one");
    }

    [Test]
    public void RetiredSubline_NamesTheReason_InTheOwnersWords()
    {
        Assert.AreEqual("Отключён · пробный период закончился", BotCardModel.RetiredSubline("trial_expiry", null));
        Assert.AreEqual("Отключён · подписка закончилась", BotCardModel.RetiredSubline("churn_grace", null));
        Assert.AreEqual("Удаление не завершено · удалите ещё раз", BotCardModel.RetiredSubline(null, "app_delete"));
        Assert.AreEqual("Канал отключён", BotCardModel.RetiredSubline("liveness", null));
        Assert.AreEqual("Канал отключён", BotCardModel.RetiredSubline("channel_limit", null));
        Assert.AreEqual("Канал отключён", BotCardModel.RetiredSubline("", null), "retired without a reason (not the row's owner)");
        Assert.AreEqual("Канал отключён", BotCardModel.RetiredSubline("something_new", null), "an unknown reason never shows server text");
        Assert.IsNull(BotCardModel.RetiredSubline(null, null), "no retired channel — the ordinary subline");
        Assert.AreEqual("Отключён · подписка закончилась", BotCardModel.RetiredSubline("churn_grace", "trial_expiry"),
            "both retired: WhatsApp's reason first (card icon order)");
    }

    [Test]
    public void SublineKind_Connecting_ThenRetired_ThenTheSyncStates_ThenTheBusinessType()
    {
        Assert.AreEqual(BotSublineKind.Connecting, BotCardModel.SublineKind(true, true, true, true), "an auth in progress owns the card");
        Assert.AreEqual(BotSublineKind.Retired, BotCardModel.SublineKind(false, true, true, true),
            "a retired channel outranks the sync states — it is also why a mode cannot be saved there");
        Assert.AreEqual(BotSublineKind.SyncPending, BotCardModel.SublineKind(false, false, true, true));
        Assert.AreEqual(BotSublineKind.SyncFailed, BotCardModel.SublineKind(false, false, false, true));
        Assert.AreEqual(BotSublineKind.Business, BotCardModel.SublineKind(false, false, false, false));
    }
}

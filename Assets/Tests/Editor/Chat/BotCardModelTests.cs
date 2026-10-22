using NUnit.Framework;
using UnityEngine;

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
        // F2 (T8): a retired channel's ids are moved aside — its current profile id is the sentinel and the icon stays gray
        // (plan §5.3). Whether it IS retired is RetiredChannelPolicy's call (a marker for another id is not — tested there).
        Assert.AreEqual(BotChannelIconState.Muted, BotCardModel.IconState(Bot.UnauthedProfileSentinel, true, retired: true),
            "a channel whose ids were moved aside still shows, gray");
        Assert.AreEqual(BotChannelIconState.Hidden, BotCardModel.IconState(Bot.UnauthedProfileSentinel, true, retired: false),
            "no profile and not retired — no icon");
        Assert.AreEqual(BotChannelIconState.Hidden, BotCardModel.IconState(Bot.UnauthedProfileSentinel, false, retired: false));
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

    // ---- A4: a connected channel whose Wappi status said authorized:false (2026-10-14) ----------------

    [Test]
    public void SublineKind_Unauthorized_RanksBelowConnectingAndRetired_AboveTheSyncStates()
    {
        Assert.AreEqual(BotSublineKind.Connecting, BotCardModel.SublineKind(true, true, true, true, true), "an auth in progress owns the card");
        Assert.AreEqual(BotSublineKind.Retired, BotCardModel.SublineKind(false, true, true, true, true),
            "the server's verdict about a channel outranks the device's own reading");
        Assert.AreEqual(BotSublineKind.Unauthorized, BotCardModel.SublineKind(false, false, true, true, true),
            "a channel that is off outranks a mode that waits for (or failed) its confirmation");
        Assert.AreEqual(BotSublineKind.SyncPending, BotCardModel.SublineKind(false, false, false, true, true));
        Assert.AreEqual(BotSublineKind.SyncFailed, BotCardModel.SublineKind(false, false, false, false, true));
        Assert.AreEqual(BotSublineKind.Business, BotCardModel.SublineKind(false, false, false, false, false));
    }

    [Test]
    public void SublineKind_TheFourArgumentForm_IsTheFiveArgumentFormWithoutAMark()
    {
        foreach (bool connecting in new[] { false, true })
        foreach (bool retired in new[] { false, true })
        foreach (bool pending in new[] { false, true })
        foreach (bool failed in new[] { false, true })
            Assert.AreEqual(BotCardModel.SublineKind(connecting, retired, false, pending, failed),
                BotCardModel.SublineKind(connecting, retired, pending, failed));
    }

    [Test]
    public void Icon_Muted_WhenTheChannelIsMarkedUnauthorized_WhateverItsToggle()
    {
        Assert.AreEqual(BotChannelIconState.Muted, BotCardModel.IconState("profile1", true, retired: false, unauthorized: true),
            "the gray icon says WHICH channel «Канал отключён» means");
        Assert.AreEqual(BotChannelIconState.Muted, BotCardModel.IconState("profile1", false, retired: false, unauthorized: true));
        Assert.AreEqual(BotChannelIconState.Colored, BotCardModel.IconState("profile1", true, retired: false, unauthorized: false));
        Assert.AreEqual(BotChannelIconState.Hidden, BotCardModel.IconState(Bot.UnauthedProfileSentinel, true, retired: false, unauthorized: true),
            "a mark on a channel without a profile shows no icon");
        Assert.AreEqual(BotChannelIconState.Muted, BotCardModel.IconState(Bot.UnauthedProfileSentinel, true, retired: true, unauthorized: true));
        Assert.AreEqual(BotCardModel.IconState("profile1", true, retired: true), BotCardModel.IconState("profile1", true, retired: true, unauthorized: false),
            "the three-argument form is the four-argument form without a mark");
    }

    // ---- T9: «Подключить» in place of «Авто» ------------------------------------

    private const string Sentinel = "-1";

    [TestCase("trial_expiry")]
    [TestCase("churn_grace")]
    [TestCase("liveness")]
    [TestCase("channel_limit")]
    [TestCase("")]
    public void ConnectPill_WhenEveryChannelOfTheBotIsRetired(string reason)
    {
        Assert.IsTrue(BotCardModel.ShowConnectPill(Sentinel, reason, Sentinel, null),
            "WhatsApp retired (ids moved aside), Telegram never connected — nothing for «Авто» to act on");
        Assert.IsTrue(BotCardModel.ShowConnectPill(Sentinel, reason, Sentinel, reason), "both channels retired");
        Assert.IsTrue(BotCardModel.ShowConnectPill(Sentinel, null, Sentinel, reason), "Telegram-only bot, retired");
    }

    [Test]
    public void AutoStays_WhileAnyChannelIsLive()
    {
        Assert.IsFalse(BotCardModel.ShowConnectPill("wa-live", null, Sentinel, "trial_expiry"),
            "one live channel: «Авто» keeps driving its reply mode, the retired one is the subline + gray icon (§5.3)");
        Assert.IsFalse(BotCardModel.ShowConnectPill(Sentinel, "churn_grace", "tg-live", null));
        Assert.IsFalse(BotCardModel.ShowConnectPill("wa-new", null, Sentinel, null),
            "a reconnect in progress (a new profile, the marker names the old one → no reason) is live again");
    }

    [Test]
    public void AutoStays_OnABotWithNothingRetired()
    {
        Assert.IsFalse(BotCardModel.ShowConnectPill(Sentinel, null, Sentinel, null), "a bot that was never connected is not «отключён»");
        Assert.IsFalse(BotCardModel.ShowConnectPill("wa-1", null, "tg-1", null));
    }

    [Test]
    public void AnUnfinishedAppDelete_NeverOffersConnect()
    {
        Assert.IsFalse(BotCardModel.ShowConnectPill("wa-1", ProfileRetirement.ReasonAppDelete, Sentinel, null),
            "app_delete: only «Удалить ещё раз» — the server would not take that row as a predecessor");
        Assert.IsFalse(BotCardModel.ShowConnectPill(Sentinel, "trial_expiry", "tg-1", ProfileRetirement.ReasonAppDelete),
            "a half-deleted bot gets no reconnect button even when its other channel is merely expired");
    }

    [Test]
    public void PillWidth_AutoKeepsItsAuthoredWidth_ConnectFitsItsLabel()
    {
        Assert.AreEqual(BotCardModel.AutoPillWidth, BotCardModel.PillWidth(false, 400f), "«Авто» never resizes");
        Assert.AreEqual(BotCardModel.AutoPillWidth, BotCardModel.PillWidth(true, 50f), "never narrower than the authored capsule");
        Assert.AreEqual(Mathf.Ceil(165.2f) + 2f * BotCardModel.ConnectPillPadding, BotCardModel.PillWidth(true, 165.2f),
            "the measured label (rounded up) plus the padding on both sides");
    }

    /// <summary>
    /// The real capsule: «Подключить» measured with the label's own font and size from Bot.prefab (BotCardAutoPillBuilder's
    /// label is 96 wide for «Авто»). The runtime grows the capsule to the label plus its padding — this pins that the word
    /// does need that growth and that the grown capsule still leaves the card's name column room on a 1080 card.
    /// </summary>
    [Test]
    public void TheRealCapsuleLabel_FitsConnect_OnceTheCapsuleGrows()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Bot.prefab");
        Assert.IsNotNull(prefab, "Bot.prefab");
        var card = Object.Instantiate(prefab);
        try
        {
            Transform pill = FindDeep(card.transform, "AutoPill");
            Assert.IsNotNull(pill, "the C2 capsule (BotCardAutoPillBuilder)");
            var label = FindDeep(pill, "Label").GetComponent<TMPro.TextMeshProUGUI>();
            float connect = label.GetPreferredValues(RetiredChannelCopy.ConnectPill).x;
            float auto = label.GetPreferredValues(label.text).x;
            Assert.Greater(connect, 0f, "the label measures (a font is assigned)");
            Assert.Greater(connect, label.rectTransform.sizeDelta.x,
                "«Подключить» does not fit the authored «Авто» label box — the runtime must grow it (else it overflows the pill)");
            float width = BotCardModel.PillWidth(true, connect);
            TestContext.Out.WriteLine($"label «{label.text}» {auto:F1}u in a {label.rectTransform.sizeDelta.x:F0}u box; " +
                                      $"«{RetiredChannelCopy.ConnectPill}» {connect:F1}u → capsule {width:F0}u (authored {BotCardModel.AutoPillWidth:F0}u)");
            Assert.GreaterOrEqual(width, connect + 2f * BotCardModel.ConnectPillPadding - 0.01f, "the label sits inside the pill");
            Assert.LessOrEqual(width, 320f, $"the grown capsule ({width:F0}u) must leave the name column room");
            Assert.LessOrEqual(auto, label.rectTransform.sizeDelta.x + 0.5f, "«Авто» still fits its authored box");
        }
        finally
        {
            Object.DestroyImmediate(card);
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    // ---- the learning percent is a fill, never a face ----

    [Test]
    public void PillKind_ConnectOutranksEverything_ElseAuto()
    {
        Assert.AreEqual(BotPillKind.Connect, BotCardModel.PillKind(connect: true));
        Assert.AreEqual(BotPillKind.Auto, BotCardModel.PillKind(connect: false),
            "the capsule always reads «Авто» — the learning percent is the fill behind it and the caption under it");
        CollectionAssert.AreEquivalent(new[] { BotPillKind.Auto, BotPillKind.Connect }, System.Enum.GetValues(typeof(BotPillKind)),
            "the «Учится · N/20» face is gone");
        Assert.AreEqual(BotCardModel.AutoPillWidth, BotCardModel.PillWidth(BotPillKind.Auto, 400f), "«Авто» keeps its authored width");
        Assert.AreEqual(BotCardModel.PillWidth(true, 200f), BotCardModel.PillWidth(BotPillKind.Connect, 200f), "«Подключить» fits its label");
    }
}

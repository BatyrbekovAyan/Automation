using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// F2 (plan 2026-10-11 §1.2 #15, task T8): the pure rules of a retired channel — the move aside of its ids on
/// profile_retired (idempotent, never a live channel, never an unfinished app delete), what «retired» means afterwards,
/// what Create names as the predecessor and when the retired set goes again (only for an adopted profile).
/// </summary>
public class RetiredChannelPolicyTests
{
    private const string Sentinel = "-1";

    private static InstallSession.TokenClaims Token(params string[] profiles) =>
        new InstallSession.TokenClaims { InstallId = "i", Profiles = profiles, Workflows = new string[0], Exp = 1 };

    private static RetiredChannelPolicy.Channel Live(string profileId = "wa-1", string workflowId = "wfClone00001") =>
        new RetiredChannelPolicy.Channel { ProfileId = profileId, WorkflowId = workflowId, IsOn = true, RetiredProfileId = "", RetiredWorkflowId = "", RetiredReason = "" };

    [TestCase("trial_expiry")]
    [TestCase("churn_grace")]
    [TestCase("liveness")]
    [TestCase("channel_limit")]
    [TestCase("")]
    [TestCase(null)]
    public void Retire_MovesTheIdsAside_ForEveryReasonButAnAppDelete(string reason)
    {
        var after = RetiredChannelPolicy.Retire(Live(), "wa-1", reason);
        Assert.AreEqual(Sentinel, after.ProfileId, "the current profile id goes — every reader sees a disconnected channel");
        Assert.AreEqual(Sentinel, after.WorkflowId, "the current workflow id goes too");
        Assert.IsFalse(after.IsOn, "the channel is switched off");
        Assert.AreEqual("wa-1", after.RetiredProfileId, "the retired profile is kept — Create names it, V12 deletes by it");
        Assert.AreEqual("wfClone00001", after.RetiredWorkflowId, "the former clone is kept — V12 deletes it");
        Assert.AreEqual(reason ?? "", after.RetiredReason, "the server's reason (\"\" = not told)");
    }

    [Test]
    public void Retire_AChannelWithoutAClone_KeepsTheSentinelAsItsFormerClone()
    {
        var after = RetiredChannelPolicy.Retire(Live(workflowId: Sentinel), "wa-1", "trial_expiry");
        Assert.AreEqual(Sentinel, after.RetiredWorkflowId);
        Assert.AreEqual("wa-1", after.RetiredProfileId);
    }

    [Test]
    public void Retire_AnUnfinishedAppDelete_KeepsTheIds_AndOnlyMarks()
    {
        var after = RetiredChannelPolicy.Retire(Live(), "wa-1", ProfileRetirement.ReasonAppDelete);
        Assert.AreEqual("wa-1", after.ProfileId, "app_delete: the screens offer only «Удалить ещё раз» — the ids stay");
        Assert.AreEqual("wfClone00001", after.WorkflowId);
        Assert.IsTrue(after.IsOn, "the switch is not touched");
        Assert.AreEqual("wa-1", after.RetiredProfileId, "the M4 marker is written");
        Assert.AreEqual(ProfileRetirement.ReasonAppDelete, after.RetiredReason);
        Assert.AreEqual("", after.RetiredWorkflowId, "nothing is moved");
        Assert.IsFalse(RetiredChannelPolicy.MovesIdsAside(ProfileRetirement.ReasonAppDelete));
        Assert.IsTrue(RetiredChannelPolicy.MovesIdsAside(ProfileRetirement.ReasonTrialExpiry));
    }

    [Test]
    public void Retire_IsIdempotent()
    {
        var once = RetiredChannelPolicy.Retire(Live(), "wa-1", "trial_expiry");
        var twice = RetiredChannelPolicy.Retire(once, "wa-1", "trial_expiry");
        Assert.AreEqual(once, twice, "a repeated 410 for the moved profile changes nothing (the former clone is not lost to the sentinel)");
        var appDelete = RetiredChannelPolicy.Retire(Live(), "wa-1", "app_delete");
        Assert.AreEqual(appDelete, RetiredChannelPolicy.Retire(appDelete, "wa-1", "app_delete"));
    }

    [Test]
    public void Retire_NeverTouchesALiveChannel_ThatMovedOn()
    {
        // The answer concerns a profile the channel no longer holds (a new pairing happened while claim.start was in flight).
        var live = Live("wa-2", "wfClone00002");
        var after = RetiredChannelPolicy.Retire(live, "wa-1", "trial_expiry");
        Assert.AreEqual(live, after, "a live channel with another profile is not touched — nothing is moved, nothing marked");
        Assert.AreEqual(live, RetiredChannelPolicy.Retire(live, Sentinel, "trial_expiry"), "the sentinel is never retired");
        Assert.AreEqual(live, RetiredChannelPolicy.Retire(live, "", "trial_expiry"));
        var moved = RetiredChannelPolicy.Retire(Live(), "wa-1", "trial_expiry");
        Assert.AreEqual(moved, RetiredChannelPolicy.Retire(moved, "wa-9", "churn_grace"), "after the move no other id can overwrite the set");
    }

    [Test]
    public void ActiveReason_MovedChannel_IsRetired_WhateverTheToken()
    {
        var moved = RetiredChannelPolicy.Retire(Live(), "wa-1", "trial_expiry");
        Assert.AreEqual("trial_expiry", RetiredChannelPolicy.ActiveReason(moved, Token()));
        Assert.AreEqual("trial_expiry", RetiredChannelPolicy.ActiveReason(moved, null), "no session yet — still retired");
        Assert.IsTrue(RetiredChannelPolicy.IsRetired(moved, Token("wa-1")), "the current id is the sentinel — nothing to yield to");
        var noReason = RetiredChannelPolicy.Retire(Live(), "wa-1", null);
        Assert.AreEqual("", RetiredChannelPolicy.ActiveReason(noReason, Token()), "\"\" = retired, reason unknown («Канал отключён»)");
    }

    [Test]
    public void ActiveReason_AppDeleteMarker_AppliesToItsOwnIdOnly_AndYieldsToTheToken()
    {
        var marked = RetiredChannelPolicy.Retire(Live(), "wa-1", "app_delete");
        Assert.AreEqual("app_delete", RetiredChannelPolicy.ActiveReason(marked, Token()));
        Assert.IsNull(RetiredChannelPolicy.ActiveReason(marked, Token("wa-1")), "the token lists the profile again — it wins");
        var reconnected = marked;
        reconnected.ProfileId = "wa-2";
        Assert.IsNull(RetiredChannelPolicy.ActiveReason(reconnected, Token()), "a new connection on the channel: not retired");
    }

    [Test]
    public void ActiveReason_ALiveChannelWithAnUnadoptedRetiredSet_IsNotRetired()
    {
        // A reconnect whose Create did not adopt the retired profile: both sets live side by side, the channel works.
        var both = new RetiredChannelPolicy.Channel
        {
            ProfileId = "wa-2", WorkflowId = "wfClone00002", IsOn = true,
            RetiredProfileId = "wa-1", RetiredWorkflowId = "wfClone00001", RetiredReason = "trial_expiry",
        };
        Assert.IsNull(RetiredChannelPolicy.ActiveReason(both, Token("wa-2")));
        Assert.IsNull(RetiredChannelPolicy.ActiveReason(Live(), Token()), "never retired");
        Assert.IsNull(RetiredChannelPolicy.ActiveReason(Live(Sentinel, Sentinel), Token()), "never connected: not retired either");
    }

    [Test]
    public void NeedsMoveAside_OnlyAnM4MarkerOfTheCurrentId_ThatIsNotAnAppDelete()
    {
        var m4 = Live();
        m4.RetiredProfileId = "wa-1";          // what the M4 build wrote: the marker, the ids kept
        m4.RetiredReason = "trial_expiry";
        Assert.IsTrue(RetiredChannelPolicy.NeedsMoveAside(m4, Token()));
        Assert.IsFalse(RetiredChannelPolicy.NeedsMoveAside(m4, Token("wa-1")), "the token lists it — the marker does not apply");
        Assert.IsFalse(RetiredChannelPolicy.NeedsMoveAside(m4, null), "no session: wait for one");
        var appDelete = m4;
        appDelete.RetiredReason = "app_delete";
        Assert.IsFalse(RetiredChannelPolicy.NeedsMoveAside(appDelete, Token()), "an unfinished app delete keeps its ids");
        Assert.IsFalse(RetiredChannelPolicy.NeedsMoveAside(RetiredChannelPolicy.Retire(m4, "wa-1", "trial_expiry"), Token()),
            "already moved");
        var other = m4;
        other.RetiredProfileId = "wa-0";
        Assert.IsFalse(RetiredChannelPolicy.NeedsMoveAside(other, Token()), "a marker for another id never moves a live channel");
    }

    [Test]
    public void PreviousProfileId_NamesTheRetiredProfile_NeverTheNewOne()
    {
        var moved = RetiredChannelPolicy.Retire(Live(), "wa-1", "trial_expiry");
        Assert.AreEqual("wa-1", RetiredChannelPolicy.PreviousProfileId(moved, "wa-2"));
        Assert.AreEqual("", RetiredChannelPolicy.PreviousProfileId(moved, "wa-1"), "never the profile being created");
        Assert.AreEqual("", RetiredChannelPolicy.PreviousProfileId(Live(), "wa-2"), "no retired set — nothing to name");
    }

    [Test]
    public void AfterCreate_ClearsTheRetiredSet_OnlyForAnAdoptedProfile()
    {
        var reconnected = RetiredChannelPolicy.Retire(Live(), "wa-1", "trial_expiry");
        reconnected.ProfileId = "wa-2";
        reconnected.WorkflowId = "wfClone00002";
        var adopted = RetiredChannelPolicy.AfterCreate(reconnected, new[] { "wa-1" });
        Assert.AreEqual("", adopted.RetiredProfileId, "the server moved the data — the retired set goes");
        Assert.AreEqual("", adopted.RetiredWorkflowId);
        Assert.AreEqual("", adopted.RetiredReason);
        Assert.AreEqual("wa-2", adopted.ProfileId, "the new connection is untouched");
        Assert.AreEqual("wfClone00002", adopted.WorkflowId);
        Assert.AreEqual(reconnected, RetiredChannelPolicy.AfterCreate(reconnected, new string[0]),
            "not adopted — the set stays (the deletion covers both)");
        Assert.AreEqual(reconnected, RetiredChannelPolicy.AfterCreate(reconnected, new[] { "wa-0", "wa-2" }), "another profile adopted");
        Assert.AreEqual(reconnected, RetiredChannelPolicy.AfterCreate(reconnected, null), "an older server that does not say");
    }

    [Test]
    public void AMovedChannel_IsNoLongerCountedAsAConnectedChannel()
    {
        // The client gate counts channels by their CURRENT profile ids (EntitlementGate.ConnectedChannelCount): after the
        // move a retired channel is free again, so its reconnect is not refused on a plan with room for it.
        var moved = RetiredChannelPolicy.Retire(Live(), "wa-1", "trial_expiry");
        bool occupied = ProfileClaimPolicy.IsAuthedId(moved.ProfileId);
        Assert.IsFalse(occupied);
        Assert.AreEqual(0, EntitlementGate.CountChannels(new List<(bool wa, bool tg)> { (occupied, false) }));
        Assert.AreEqual(1, EntitlementGate.CountChannels(new List<(bool wa, bool tg)> { (ProfileClaimPolicy.IsAuthedId(Live().ProfileId), false) }));
    }

    // ---------------- T9: the screens' decisions ----------------

    private static RetiredChannelPolicy.Channel Never() =>
        new RetiredChannelPolicy.Channel { ProfileId = Sentinel, WorkflowId = Sentinel, IsOn = false, RetiredProfileId = "", RetiredWorkflowId = "", RetiredReason = "" };

    [Test]
    public void SettingsRow_ALiveOrNeverConnectedChannel_IsNormal()
    {
        Assert.AreEqual(RetiredChannelPolicy.SettingsRowState.Normal, RetiredChannelPolicy.SettingsRow(Live(), Token("wa-1")));
        Assert.AreEqual(RetiredChannelPolicy.SettingsRowState.Normal, RetiredChannelPolicy.SettingsRow(Never(), Token()));
        Assert.AreEqual(RetiredChannelPolicy.SettingsRowState.Normal, RetiredChannelPolicy.SettingsRow(default, null),
            "no bot at all reads as an ordinary row");
    }

    [TestCase("trial_expiry")]
    [TestCase("churn_grace")]
    [TestCase("liveness")]
    [TestCase("channel_limit")]
    [TestCase("")]
    public void SettingsRow_AMovedChannel_OffersTheReconnect(string reason)
    {
        var moved = RetiredChannelPolicy.Retire(Live(), "wa-1", reason);
        Assert.AreEqual(RetiredChannelPolicy.SettingsRowState.Reconnect, RetiredChannelPolicy.SettingsRow(moved, Token()));
        Assert.AreEqual(RetiredChannelPolicy.SettingsRowState.Reconnect, RetiredChannelPolicy.SettingsRow(moved, null),
            "before the session is known the moved ids already say it");
    }

    [Test]
    public void SettingsRow_AnUnfinishedAppDelete_OffersOnlyTheDelete()
    {
        var marked = RetiredChannelPolicy.Retire(Live(), "wa-1", ProfileRetirement.ReasonAppDelete);
        Assert.AreEqual(RetiredChannelPolicy.SettingsRowState.DeleteUnfinished, RetiredChannelPolicy.SettingsRow(marked, Token()));
        Assert.AreEqual(RetiredChannelPolicy.SettingsRowState.Normal, RetiredChannelPolicy.SettingsRow(marked, Token("wa-1")),
            "the token lists the profile again — the marker yields (M4)");
    }

    [Test]
    public void SettingsRow_AnM4MarkerNotMovedYet_IsAReconnect_ThatMovesItFirst()
    {
        var legacy = Live();
        legacy.RetiredProfileId = "wa-1";               // the M4 build marked the current id and kept the ids
        legacy.RetiredReason = "trial_expiry";
        Assert.AreEqual(RetiredChannelPolicy.SettingsRowState.Reconnect, RetiredChannelPolicy.SettingsRow(legacy, Token()));
        Assert.IsTrue(RetiredChannelPolicy.NeedsMoveAside(legacy, Token()), "the reconnect moves it aside first (ProfileRetirement.MoveAsideIfMarked)");
    }

    [Test]
    public void SettingsRow_AReconnectInProgress_IsNormalAgain()
    {
        var reconnecting = RetiredChannelPolicy.Retire(Live(), "wa-1", "trial_expiry");
        reconnecting.ProfileId = "wa-2";                // profile.add answered, the pairing is on screen
        Assert.AreEqual(RetiredChannelPolicy.SettingsRowState.Normal, RetiredChannelPolicy.SettingsRow(reconnecting, Token("wa-2")));
        Assert.AreEqual("wa-1", RetiredChannelPolicy.PreviousProfileId(reconnecting, "wa-2"), "…while Create still names the predecessor");
    }

    [Test]
    public void Banner_OnlyWhileAChannelWaitsAndNoPlanIsActive()
    {
        var moved = RetiredChannelPolicy.Retire(Live(), "wa-1", "trial_expiry");
        Assert.IsTrue(RetiredChannelPolicy.ShowBanner(moved, Never(), Token(), PlanTier.None), "trial over, nothing bought: «Бот отключён»");
        Assert.IsTrue(RetiredChannelPolicy.ShowBanner(Never(), moved, Token(), PlanTier.None), "Telegram's channel the same");
        foreach (PlanTier active in new[] { PlanTier.Trial, PlanTier.Start, PlanTier.Business, PlanTier.Network })
            Assert.IsFalse(RetiredChannelPolicy.ShowBanner(moved, Never(), Token(), active),
                $"{active}: a plan is active — the row hint carries the reconnect, no «после оплаты»");
        Assert.IsFalse(RetiredChannelPolicy.ShowBanner(Live(), Never(), Token("wa-1"), PlanTier.None),
            "nothing retired — an expired plan alone is the paywall's business, not this banner's");
        var appDelete = RetiredChannelPolicy.Retire(Live(), "wa-1", ProfileRetirement.ReasonAppDelete);
        Assert.IsFalse(RetiredChannelPolicy.ShowBanner(appDelete, Never(), Token(), PlanTier.None),
            "an unfinished delete is never told to pay and reconnect");
    }

    [Test]
    public void BannerReason_WhatsappFirst_ThenTelegram()
    {
        var waTrial = RetiredChannelPolicy.Retire(Live(), "wa-1", "trial_expiry");
        var tgChurn = RetiredChannelPolicy.Retire(Live("tg-1", "wfClone00002"), "tg-1", "churn_grace");
        Assert.AreEqual("trial_expiry", RetiredChannelPolicy.BannerReason(waTrial, tgChurn, Token()));
        Assert.AreEqual("churn_grace", RetiredChannelPolicy.BannerReason(Never(), tgChurn, Token()));
        Assert.AreEqual("", RetiredChannelPolicy.BannerReason(Never(), Never(), Token()));
    }

    [Test]
    public void Candidates_EveryWaitingChannel_InCardOrder_NeverALiveOrHalfDeletedOne()
    {
        var bots = new[]
        {
            new RetiredChannelPolicy.BotChannels
            {
                BotKey = "Bot0", BotName = "Цветы",
                Whatsapp = RetiredChannelPolicy.Retire(Live(), "wa-1", "trial_expiry"),
                Telegram = RetiredChannelPolicy.Retire(Live("tg-1", "wfClone00002"), "tg-1", "trial_expiry"),
            },
            new RetiredChannelPolicy.BotChannels
            {
                BotKey = "Bot1", BotName = "Запчасти",
                Whatsapp = Live("wa-9"),
                Telegram = RetiredChannelPolicy.Retire(Live("tg-9", "wfClone00009"), "tg-9", ProfileRetirement.ReasonAppDelete),
            },
            new RetiredChannelPolicy.BotChannels
            {
                BotKey = "Bot2", BotName = "Ремонт",
                Whatsapp = Never(),
                Telegram = RetiredChannelPolicy.Retire(Live("tg-5", "wfClone00005"), "tg-5", "churn_grace"),
            },
        };
        List<RetiredChannelPolicy.Candidate> rows = RetiredChannelPolicy.Candidates(bots, Token("wa-9"));
        Assert.AreEqual(3, rows.Count);
        Assert.AreEqual(("Bot0", ChatChannel.WhatsApp), (rows[0].BotKey, rows[0].Channel));
        Assert.AreEqual(("Bot0", ChatChannel.Telegram), (rows[1].BotKey, rows[1].Channel));
        Assert.AreEqual(("Bot2", ChatChannel.Telegram), (rows[2].BotKey, rows[2].Channel));
        Assert.AreEqual("Ремонт", rows[2].BotName);
        Assert.IsEmpty(RetiredChannelPolicy.Candidates(null, Token()));
    }
}

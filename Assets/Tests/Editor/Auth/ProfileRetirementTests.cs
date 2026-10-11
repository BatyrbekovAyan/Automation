using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// M4 (follow-ups 2026-10-09) + F2 (plan 2026-10-11, task T8): the device's record that the server retired one channel
/// of a bot. M4 only remembered the retired profile next to the channel's ids («ids stay»); F2 replaces that rule — the
/// channel's ids are MOVED ASIDE to its retired set ({bot}{Ch}Retired{ProfileId|Reason|WorkflowId}), the current ones
/// become the sentinel and the channel is off, written to the Bot's fields and PlayerPrefs in one place. An unfinished app
/// delete keeps its ids (M4's rule survives for it). Runs against the real PlayerPrefs under a test-only bot key, removed
/// afterwards.
/// </summary>
public class ProfileRetirementTests
{
    private const string BotKey = "M4RetirementTestBot";
    private const string Sentinel = "-1";

    private static readonly string[] ChannelKeys =
    {
        "WhatsappProfileId", "WhatsappWorkflowId", "TelegramProfileId", "TelegramWorkflowId", "isOnWhatsapp", "isOnTelegram",
    };

    private GameObject root;

    private static InstallSession.TokenClaims Token(params string[] profiles) =>
        new InstallSession.TokenClaims { InstallId = "i", Profiles = profiles, Workflows = new string[0], Exp = 1 };

    [TearDown]
    public void TearDown()
    {
        foreach (var suffix in ProfileRetirement.KeySuffixes.Concat(ChannelKeys))
            PlayerPrefs.DeleteKey(BotKey + suffix);
        if (root != null) Object.DestroyImmediate(root);
    }

    /// <summary>A bot card the way LoadBots leaves it: fields and PlayerPrefs agree, both channels on.</summary>
    private Bot MakeBot(string wa = "wa-1", string waClone = "wfCloneWa0001", string tg = "tg-1", string tgClone = "wfCloneTg0001")
    {
        root = new GameObject("BotsRootForRetirementTests");
        var go = new GameObject(BotKey);
        go.transform.SetParent(root.transform);
        var bot = go.AddComponent<Bot>();
        bot.whatsappProfileId = wa;
        bot.whatsappWorkflowId = waClone;
        bot.telegramProfileId = tg;
        bot.telegramWorkflowId = tgClone;
        PlayerPrefs.SetString(BotKey + "WhatsappProfileId", wa);
        PlayerPrefs.SetString(BotKey + "WhatsappWorkflowId", waClone);
        PlayerPrefs.SetString(BotKey + "TelegramProfileId", tg);
        PlayerPrefs.SetString(BotKey + "TelegramWorkflowId", tgClone);
        PlayerPrefs.SetInt(BotKey + "isOnWhatsapp", 1);
        PlayerPrefs.SetInt(BotKey + "isOnTelegram", 1);
        return bot;
    }

    [Test]
    public void KeySuffixes_FollowThePerBotConvention()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "WhatsappRetiredProfileId", "WhatsappRetiredReason", "WhatsappRetiredWorkflowId",
                "TelegramRetiredProfileId", "TelegramRetiredReason", "TelegramRetiredWorkflowId",
            },
            ProfileRetirement.KeySuffixes);
        Assert.AreEqual("TelegramRetiredProfileId", ProfileRetirement.ProfileIdSuffix(ChatChannel.Telegram));
        Assert.AreEqual("WhatsappRetiredReason", ProfileRetirement.ReasonSuffix(ChatChannel.WhatsApp));
        Assert.AreEqual("WhatsappRetiredWorkflowId", ProfileRetirement.WorkflowIdSuffix(ChatChannel.WhatsApp));
    }

    [TestCase(ChatChannel.WhatsApp, "Whatsapp", "wa-1", "wfCloneWa0001")]
    [TestCase(ChatChannel.Telegram, "Telegram", "tg-1", "wfCloneTg0001")]
    public void Mark_MovesTheChannelsIdsAside_FieldsAndPrefsTogether(ChatChannel channel, string word, string profile, string clone)
    {
        var bot = MakeBot();
        string notified = null;
        System.Action<string> listener = key => notified = key;
        ProfileRetirement.Changed += listener;
        try
        {
            ProfileRetirement.Mark(bot, channel, profile, "churn_grace");
        }
        finally
        {
            ProfileRetirement.Changed -= listener;
        }
        Assert.AreEqual(BotKey, notified, "the bot's card repaints");
        bool telegram = channel == ChatChannel.Telegram;
        Assert.AreEqual(Sentinel, telegram ? bot.telegramProfileId : bot.whatsappProfileId, "the Bot's field — every reader sees «not connected»");
        Assert.AreEqual(Sentinel, telegram ? bot.telegramWorkflowId : bot.whatsappWorkflowId);
        Assert.AreEqual(Sentinel, PlayerPrefs.GetString(BotKey + word + "ProfileId"), "its PlayerPrefs copy, written in the same place");
        Assert.AreEqual(Sentinel, PlayerPrefs.GetString(BotKey + word + "WorkflowId"));
        Assert.AreEqual(0, PlayerPrefs.GetInt(BotKey + "isOn" + word, 1), "the channel is off");
        Assert.AreEqual(profile, ProfileRetirement.MarkerId(BotKey, channel));
        Assert.AreEqual("churn_grace", ProfileRetirement.Reason(BotKey, channel));
        Assert.AreEqual(clone, ProfileRetirement.RetiredWorkflowId(BotKey, channel), "the former clone is kept for the deletion");
        Assert.AreEqual("churn_grace", ProfileRetirement.ActiveReason(BotKey, channel, Sentinel, Token()),
            "the card reads the moved channel as retired (current id = the sentinel)");

        // The other channel is untouched.
        var other = telegram ? ChatChannel.WhatsApp : ChatChannel.Telegram;
        Assert.AreEqual(telegram ? "wa-1" : "tg-1", telegram ? bot.whatsappProfileId : bot.telegramProfileId);
        Assert.AreEqual("", ProfileRetirement.MarkerId(BotKey, other));
        Assert.AreEqual(1, PlayerPrefs.GetInt(BotKey + "isOn" + (telegram ? "Whatsapp" : "Telegram"), 1));
    }

    [Test]
    public void Mark_AnUnfinishedAppDelete_KeepsTheIds()
    {
        var bot = MakeBot();
        ProfileRetirement.Mark(bot, ChatChannel.Telegram, "tg-1", ProfileRetirement.ReasonAppDelete);
        Assert.AreEqual("tg-1", bot.telegramProfileId, "app_delete: «Удалить ещё раз» only — the ids stay (M4's rule)");
        Assert.AreEqual("wfCloneTg0001", bot.telegramWorkflowId);
        Assert.AreEqual("tg-1", PlayerPrefs.GetString(BotKey + "TelegramProfileId"));
        Assert.AreEqual(1, PlayerPrefs.GetInt(BotKey + "isOnTelegram", 1));
        Assert.AreEqual("tg-1", ProfileRetirement.MarkerId(BotKey, ChatChannel.Telegram), "the M4 marker is written");
        Assert.AreEqual("app_delete", ProfileRetirement.ActiveReason(BotKey, ChatChannel.Telegram, "tg-1", Token()));
        Assert.IsNull(ProfileRetirement.ActiveReason(BotKey, ChatChannel.Telegram, "tg-1", Token("tg-1")), "the token wins");
        Assert.IsFalse(PlayerPrefs.HasKey(BotKey + ProfileRetirement.TelegramWorkflowIdSuffix), "nothing was moved aside");
    }

    [Test]
    public void Mark_WithoutAReason_StillCountsAsRetired()
    {
        var bot = MakeBot();
        ProfileRetirement.Mark(bot, ChatChannel.WhatsApp, "wa-1", null);   // a row this install does not own: 410, no reason
        Assert.AreEqual("", ProfileRetirement.ActiveReason(BotKey, ChatChannel.WhatsApp, bot.whatsappProfileId, null),
            "\"\" = retired, reason unknown («Канал отключён»); null would mean «not retired»");
        Assert.AreEqual(Sentinel, bot.whatsappProfileId, "a reasonless retirement moves the ids aside too");
    }

    [Test]
    public void Mark_IsIdempotent_AndNeverTouchesALiveChannelThatMovedOn()
    {
        var bot = MakeBot(wa: "wa-2", waClone: "wfCloneWa0002");
        int notified = 0;
        System.Action<string> listener = _ => notified++;
        ProfileRetirement.Changed += listener;
        try
        {
            ProfileRetirement.Mark(bot, ChatChannel.WhatsApp, "wa-1", "trial_expiry");   // a stale answer: the channel holds wa-2
            Assert.AreEqual(0, notified, "nothing changed — no repaint");
            Assert.AreEqual("wa-2", bot.whatsappProfileId, "the live channel is untouched");
            Assert.AreEqual("wfCloneWa0002", PlayerPrefs.GetString(BotKey + "WhatsappWorkflowId"));
            Assert.IsFalse(PlayerPrefs.HasKey(BotKey + ProfileRetirement.WhatsappProfileIdSuffix), "no marker for an id the channel no longer holds");

            ProfileRetirement.Mark(bot, ChatChannel.WhatsApp, "wa-2", "trial_expiry");
            ProfileRetirement.Mark(bot, ChatChannel.WhatsApp, "wa-2", "trial_expiry");     // the same 410 again
            Assert.AreEqual(1, notified, "the repeat changes nothing");
            Assert.AreEqual("wfCloneWa0002", ProfileRetirement.RetiredWorkflowId(BotKey, ChatChannel.WhatsApp),
                "the repeat does not lose the former clone to the sentinel");
        }
        finally
        {
            ProfileRetirement.Changed -= listener;
        }
    }

    [Test]
    public void Mark_IgnoresTheSentinelAndAMissingBot()
    {
        var bot = MakeBot();
        ProfileRetirement.Mark(bot, ChatChannel.WhatsApp, Bot.UnauthedProfileSentinel, "trial_expiry");
        ProfileRetirement.Mark(null, ChatChannel.WhatsApp, "wa-1", "trial_expiry");
        Assert.IsFalse(PlayerPrefs.HasKey(BotKey + ProfileRetirement.WhatsappProfileIdSuffix));
        Assert.IsFalse(PlayerPrefs.HasKey(ProfileRetirement.WhatsappProfileIdSuffix), "no bot → nothing written under a bare suffix");
        Assert.AreEqual("wa-1", bot.whatsappProfileId);
    }

    [Test]
    public void AfterCreate_ClearsTheRetiredSet_OnlyWhenTheServerAdoptedItsProfile()
    {
        var bot = MakeBot();
        ProfileRetirement.Mark(bot, ChatChannel.WhatsApp, "wa-1", "trial_expiry");
        bot.whatsappProfileId = "wa-2";              // the reconnect: a fresh profile, then a new clone
        bot.whatsappWorkflowId = "wfCloneWa0002";
        Assert.AreEqual("wa-1", ProfileRetirement.PreviousProfileId(bot, ChatChannel.WhatsApp, "wa-2"), "Create names the retired profile");

        ProfileRetirement.AfterCreate(bot, ChatChannel.WhatsApp, new[] { "wa-0" });
        Assert.AreEqual("wa-1", ProfileRetirement.MarkerId(BotKey, ChatChannel.WhatsApp), "another profile adopted — the set stays");
        ProfileRetirement.AfterCreate(bot, ChatChannel.WhatsApp, new string[0]);
        Assert.AreEqual("wfCloneWa0001", ProfileRetirement.RetiredWorkflowId(BotKey, ChatChannel.WhatsApp), "not adopted — the set stays");

        ProfileRetirement.AfterCreate(bot, ChatChannel.WhatsApp, new[] { "wa-1" });
        Assert.AreEqual("", ProfileRetirement.MarkerId(BotKey, ChatChannel.WhatsApp), "adopted — the set goes");
        Assert.IsFalse(PlayerPrefs.HasKey(BotKey + ProfileRetirement.WhatsappReasonSuffix));
        Assert.IsFalse(PlayerPrefs.HasKey(BotKey + ProfileRetirement.WhatsappWorkflowIdSuffix));
        Assert.AreEqual("wa-2", bot.whatsappProfileId, "the new connection is untouched");
        Assert.IsNull(ProfileRetirement.ActiveReason(BotKey, ChatChannel.WhatsApp, "wa-2", Token("wa-2")), "the card is ordinary again");
    }

    [Test]
    public void TransferLegacyMarkers_MovesAnM4MarkerOnce_NeverAnAppDelete()
    {
        // What the M4 build left behind: the marker for the channel's own current id, the ids kept.
        var bot = MakeBot();
        PlayerPrefs.SetString(BotKey + ProfileRetirement.WhatsappProfileIdSuffix, "wa-1");
        PlayerPrefs.SetString(BotKey + ProfileRetirement.WhatsappReasonSuffix, "trial_expiry");
        PlayerPrefs.SetString(BotKey + ProfileRetirement.TelegramProfileIdSuffix, "tg-1");
        PlayerPrefs.SetString(BotKey + ProfileRetirement.TelegramReasonSuffix, "app_delete");

        ProfileRetirement.TransferLegacyMarkers(root.transform, null);
        Assert.AreEqual("wa-1", bot.whatsappProfileId, "no session yet — the token still decides whether a marker applies");

        ProfileRetirement.TransferLegacyMarkers(root.transform, Token("tg-9"));
        Assert.AreEqual(Sentinel, bot.whatsappProfileId, "the M4 marker is moved aside");
        Assert.AreEqual("wfCloneWa0001", ProfileRetirement.RetiredWorkflowId(BotKey, ChatChannel.WhatsApp));
        Assert.AreEqual("trial_expiry", ProfileRetirement.Reason(BotKey, ChatChannel.WhatsApp));
        Assert.AreEqual(0, PlayerPrefs.GetInt(BotKey + "isOnWhatsapp", 1));
        Assert.AreEqual("tg-1", bot.telegramProfileId, "an unfinished app delete keeps its ids");

        ProfileRetirement.TransferLegacyMarkers(root.transform, Token());
        Assert.AreEqual("wfCloneWa0001", ProfileRetirement.RetiredWorkflowId(BotKey, ChatChannel.WhatsApp), "a second boot changes nothing");
    }

    [Test]
    public void TransferLegacyMarkers_LeavesAProfileTheTokenListsAgain()
    {
        var bot = MakeBot();
        PlayerPrefs.SetString(BotKey + ProfileRetirement.WhatsappProfileIdSuffix, "wa-1");
        PlayerPrefs.SetString(BotKey + ProfileRetirement.WhatsappReasonSuffix, "liveness");
        ProfileRetirement.TransferLegacyMarkers(root.transform, Token("wa-1"));
        Assert.AreEqual("wa-1", bot.whatsappProfileId, "the token lists the profile — the marker does not apply");
    }

    [Test]
    public void ServerIdsOf_CarriesTheCurrentIdsAndTheRetiredSet()
    {
        var bot = MakeBot();
        ProfileRetirement.Mark(bot, ChatChannel.Telegram, "tg-1", "trial_expiry");
        var ids = ProfileRetirement.ServerIdsOf(bot);
        Assert.AreEqual("wa-1", ids.Whatsapp.ProfileId);
        Assert.AreEqual("wfCloneWa0001", ids.Whatsapp.WorkflowId);
        Assert.IsFalse(ids.Whatsapp.HasSeparateRetired);
        Assert.AreEqual(Sentinel, ids.Telegram.ProfileId);
        Assert.AreEqual("tg-1", ids.Telegram.RetiredProfileId);
        Assert.AreEqual("wfCloneTg0001", ids.Telegram.RetiredWorkflowId);
        Assert.IsTrue(BotServerDeletion.HasServerTrace(ids));
    }

    [Test]
    public void Clear_RemovesEveryRetiredKey()
    {
        var bot = MakeBot();
        ProfileRetirement.Mark(bot, ChatChannel.WhatsApp, "wa-1", "trial_expiry");
        ProfileRetirement.Mark(bot, ChatChannel.Telegram, "tg-1", "app_delete");
        ProfileRetirement.Clear(BotKey);
        foreach (var suffix in ProfileRetirement.KeySuffixes)
            Assert.IsFalse(PlayerPrefs.HasKey(BotKey + suffix), $"{suffix} survived the bot's deletion");
    }
}

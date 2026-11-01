using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using static SourceGuard;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.4): when «Кто из них клиенты?» is owed and when it shows.
///   • Owed once per channel of a bot: at its first connection — a channel never seen on this bot
///     (<c>{bot}AudienceChannelSeen{Whatsapp|Telegram}</c>, set at every confirmed pairing, cleared only when the bot is
///     deleted) — and again when the channel now carries another number: another number than the last one confirmed on it
///     (<c>{bot}AudienceNumber{Whatsapp|Telegram}</c> — kept across logouts and fresh profiles), or the server says so
///     (<c>restarted</c>, a later moment for the same profile) for a pairing the app did not ask for itself — the screen comes
///     once. Channels paired before this version are marked seen once, after the bots load, their stored numbers remembered.
///   • Shown while the chats screen shows that bot and that channel; «Готово» and «Отмечу позже» clear it for good.
/// The flags and the policy are pure; the screen is the real folder over a bare app (PersonalChatsFolderFixture); the
/// wiring EditMode cannot run — a pairing, a boot, a server answer — is pinned at the source.
/// </summary>
public class PersonalChatsOnboardingTests
{
    private const string TestBot = "PersonalChatsOnboardingTestBot";
    private const string OtherTestBot = "PersonalChatsOnboardingOtherBot";
    private const string Profile = "onboarding-wa-profile";
    private static readonly long MomentMs = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static readonly long BeforeSec = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private readonly List<GameObject> hosts = new List<GameObject>();
    private readonly List<(string bot, bool telegram)> requested = new List<(string, bool)>();
    private string dir;
    private string savedPreset;
    private Manager previousManager;
    private ChatManager previousChatManager;

    [SetUp]
    public void SetUp()
    {
        AudienceToast.ResetForTests();
        PersonalChatsFolderView.ResetForTests();
        dir = Path.Combine(Path.GetTempPath(), "PersonalChatsOnboardingTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ChatAudienceStore.UseCacheFileForTests(Path.Combine(dir, ChatAudienceStore.CacheFileName));
        ChatAudienceStore.ApplyConnected(Profile, MomentMs);
        previousManager = Manager.Instance;
        previousChatManager = ChatManager.Instance;
        savedPreset = PlayerPrefs.HasKey(PersonalChatsOnboarding.SeenPresetKey)
            ? PlayerPrefs.GetInt(PersonalChatsOnboarding.SeenPresetKey).ToString() : null;
        requested.Clear();
        PersonalChatsOnboarding.Requested += Record;
        ForgetTheTestBots();
    }

    [TearDown]
    public void TearDown()
    {
        PersonalChatsOnboarding.Requested -= Record;
        ForgetTheTestBots();
        if (savedPreset == null) PlayerPrefs.DeleteKey(PersonalChatsOnboarding.SeenPresetKey);
        else PlayerPrefs.SetInt(PersonalChatsOnboarding.SeenPresetKey, int.Parse(savedPreset));
        Manager.Instance = previousManager;
        ChatManager.Instance = previousChatManager;
        foreach (GameObject host in hosts)
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
        hosts.Clear();
        PersonalChatsFolderView.ResetForTests();
        AudienceToast.ResetForTests();
        ChatAudienceStore.ResetForTests();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private void Record(string bot, bool telegram) => requested.Add((bot, telegram));

    private static void ForgetTheTestBots()
    {
        foreach (string bot in new[] { TestBot, OtherTestBot })
        {
            PersonalChatsOnboarding.ForgetBot(bot);
            foreach (string key in new[] { "WhatsappProfileId", "TelegramProfileId", "WhatsappNumber", "TelegramNumber" })
                PlayerPrefs.DeleteKey(bot + key);
        }
    }

    // ---- the flags ----

    [Test]
    public void Request_RaisesTheEvent_TheChatsScreenListensTo()
    {
        PersonalChatsOnboarding.Request(TestBot, telegram: true);
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(TestBot, telegram: true));
        CollectionAssert.AreEqual(new[] { (TestBot, true) }, requested, "a flag set while the list is on screen shows at once");

        PersonalChatsOnboarding.Request("", telegram: false);
        Assert.AreEqual(1, requested.Count, "no bot — nothing owed, nothing told");
    }

    [Test]
    public void ChannelSeen_IsPerBotAndChannel_AndOnlyTheBotsDeletionForgetsIt()
    {
        Assert.AreEqual(TestBot + "AudienceChannelSeenWhatsapp", PersonalChatsOnboarding.ChannelSeenKey(TestBot, telegram: false));
        Assert.AreEqual(TestBot + "AudienceChannelSeenTelegram", PersonalChatsOnboarding.ChannelSeenKey(TestBot, telegram: true));
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: false));

        PersonalChatsOnboarding.MarkChannelSeen(TestBot, telegram: false);
        Assert.IsTrue(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: false));
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: true), "each channel of its own");
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(OtherTestBot, telegram: false), "each bot of its own");

        PersonalChatsOnboarding.Request(TestBot, telegram: false);
        PersonalChatsOnboarding.Clear(TestBot, telegram: false);   // «Готово» / «Отмечу позже»
        Assert.IsTrue(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: false), "done with the screen is not a new channel");

        PersonalChatsOnboarding.MarkChannelSeen(TestBot, telegram: true);
        PersonalChatsOnboarding.ForgetBot(TestBot);                 // Bot.DeleteBotLocally
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: false), "a deleted bot's channels are forgotten…");
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: true), "…both of them: a bot made anew owes the screen again");
    }

    // ---- when it is owed ----

    [Test]
    public void FirstConnection_IsAChannelNeverSeen_AnotherNumberOwesItAgain()
    {
        Assert.IsTrue(ChatAudienceConnectPolicy.OwesOnboarding(false, "", "77011112233"), "a channel never seen on this bot");
        Assert.IsTrue(ChatAudienceConnectPolicy.OwesOnboarding(false, "77011112233", "77011112233"),
            "never seen — whatever number the phone kept from before");
        Assert.IsFalse(ChatAudienceConnectPolicy.OwesOnboarding(true, "77011112233", "77011112233"), "a re-pairing of the same number");
        Assert.IsFalse(ChatAudienceConnectPolicy.OwesOnboarding(true, "", "77011112233"), "the last paired number is not known: nothing proves a change");
        Assert.IsTrue(ChatAudienceConnectPolicy.OwesOnboarding(true, "77011112233", "77019998877"), "another number is a new connection");
    }

    [Test]
    public void NumberRestarted_TheServersWordOrALaterMomentForTheSameProfile()
    {
        Assert.IsTrue(ChatAudienceConnectPolicy.NumberRestarted(restarted: true, mirroredConnectedAtMs: null, answeredConnectedAtMs: MomentMs),
            "the server moved the moment: another number");
        Assert.IsTrue(ChatAudienceConnectPolicy.NumberRestarted(false, MomentMs, MomentMs + 1),
            "a later moment than the one mirrored — a retried call after its first answer was lost");
        Assert.IsFalse(ChatAudienceConnectPolicy.NumberRestarted(false, MomentMs, MomentMs), "the same moment: the same connection");
        Assert.IsFalse(ChatAudienceConnectPolicy.NumberRestarted(false, MomentMs, MomentMs - 1), "an earlier one (a reconnect's line) is no restart");
        Assert.IsFalse(ChatAudienceConnectPolicy.NumberRestarted(false, null, MomentMs), "nothing mirrored — a first answer proves nothing");
        Assert.IsFalse(ChatAudienceConnectPolicy.NumberRestarted(false, 0, MomentMs));
    }

    [Test]
    public void RequestForProfile_OwesTheScreenToTheBotHoldingIt()
    {
        PlayerPrefs.SetString(OtherTestBot + "WhatsappProfileId", "other-wa");
        PlayerPrefs.SetString(TestBot + "TelegramProfileId", Profile);

        Assert.IsTrue(PersonalChatsOnboarding.RequestForProfile(Profile, new[] { OtherTestBot, TestBot }));
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(TestBot, telegram: true), "the bot whose stored profile it is, on that channel");
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false));
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(OtherTestBot, telegram: false));

        Assert.IsFalse(PersonalChatsOnboarding.RequestForProfile("nobodys-profile", new[] { OtherTestBot, TestBot }), "no bot holds it: nothing owed");
        PlayerPrefs.SetString(TestBot + "WhatsappProfileId", global::Bot.UnauthedProfileSentinel);
        Assert.IsFalse(PersonalChatsOnboarding.RequestForProfile(global::Bot.UnauthedProfileSentinel, new[] { TestBot }), "never the never-paired sentinel");
        Assert.IsFalse(PersonalChatsOnboarding.RequestForProfile(Profile, null));
        Assert.AreEqual(1, requested.Count);
    }

    // ---- the channels paired before this version ----

    [Test]
    public void Preset_MarksEveryChannelPairedBefore_Once()
    {
        PlayerPrefs.DeleteKey(PersonalChatsOnboarding.SeenPresetKey);
        var bots = new[]
        {
            new PersonalChatsOnboarding.BotChannels(TestBot, "wa-now", "-1", "", ""),
            new PersonalChatsOnboarding.BotChannels(OtherTestBot, "-1", "", "", "tg-retired"),
        };

        Assert.IsTrue(PersonalChatsOnboarding.PresetChannelsSeen(bots), "the first launch of this version");
        Assert.IsTrue(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: false), "a channel holding a profile now");
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: true), "a channel never paired stays owed");
        Assert.IsTrue(PersonalChatsOnboarding.IsChannelSeen(OtherTestBot, telegram: true), "a channel the server retired was connected before");
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(OtherTestBot, telegram: false));
        Assert.AreEqual(1, PlayerPrefs.GetInt(PersonalChatsOnboarding.SeenPresetKey, 0), "done once, remembered");

        PersonalChatsOnboarding.ForgetBot(TestBot);
        Assert.IsFalse(PersonalChatsOnboarding.PresetChannelsSeen(bots), "never again: a pairing cut short later keeps its screen");
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: false));
        Assert.IsEmpty(requested, "the preset owes nothing");
    }

    [Test]
    public void Preset_OverNoBots_SpendsTheMarker_SoItRunsAfterTheBotsLoad()
    {
        PlayerPrefs.DeleteKey(PersonalChatsOnboarding.SeenPresetKey);
        Assert.IsTrue(PersonalChatsOnboarding.PresetChannelsSeen(new PersonalChatsOnboarding.BotChannels[0]));
        Assert.IsFalse(PersonalChatsOnboarding.PresetChannelsSeen(new[] { new PersonalChatsOnboarding.BotChannels(TestBot, "wa-now", "", "", "") }),
            "a run before the bots load would spend the marker and leave every existing channel unprotected");
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: false));

        string manager = Read("Scripts/Main/Manager.cs");
        string loadBots = MethodBody(manager, "public IEnumerator LoadBots()");
        int preset = loadBots.IndexOf("PersonalChatsOnboarding.PresetChannelsSeen(AudienceChannelsOfLoadedBots())", StringComparison.Ordinal);
        Assert.GreaterOrEqual(preset, 0, "LoadBots marks the channels paired before this version");
        foreach (string loaded in new[] { "GameObject recreatedBot = Instantiate(BotPrefab", "recreatedBot.GetComponent<Bot>().telegramProfileId =",
                     "recreatedService.Description = PlayerPrefs.GetString(" })
        {
            int at = loadBots.IndexOf(loaded, StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, $"«{loaded}» moved — update the test");
            Assert.Greater(preset, at, $"…after the bots are loaded ({loaded})");
        }
        string channels = MethodBody(manager, "private List<PersonalChatsOnboarding.BotChannels> AudienceChannelsOfLoadedBots()");
        StringAssert.Contains("BotsRoot", channels, "the loaded bots");
        StringAssert.Contains("ProfileRetirement.MarkerId(bot.name, ChatChannel.WhatsApp)", channels,
            "…the ids they hold now and the ones the server retired");
        StringAssert.Contains("ProfileRetirement.MarkerId(bot.name, ChatChannel.Telegram)", channels);
    }

    [Test]
    public void Preset_RemembersTheStoredNumbers_SoALaterNumberChangeStillOwesTheScreen()
    {
        PlayerPrefs.DeleteKey(PersonalChatsOnboarding.SeenPresetKey);
        PlayerPrefs.SetString(PersonalChatsOnboarding.PairedNumberKey(OtherTestBot, telegram: false), "77015554433");
        var bots = new[]
        {
            new PersonalChatsOnboarding.BotChannels(TestBot, "wa-now", "-1", "", "", whatsappNumber: "77011112233", telegramNumber: ""),
            new PersonalChatsOnboarding.BotChannels(OtherTestBot, "wa-other", "", "", "", whatsappNumber: "77010000000"),
        };

        Assert.IsTrue(PersonalChatsOnboarding.PresetChannelsSeen(bots));
        Assert.AreEqual("77011112233", PersonalChatsOnboarding.PairedNumber(TestBot, telegram: false),
            "a channel paired before this version: its stored number is its last paired one");
        Assert.AreEqual("", PersonalChatsOnboarding.PairedNumber(TestBot, telegram: true), "no number stored — none remembered");
        Assert.AreEqual("77015554433", PersonalChatsOnboarding.PairedNumber(OtherTestBot, telegram: false), "a number already remembered stays");

        Assert.IsFalse(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-fresh", "+7 701 111 22 33", newBot: false),
            "the same number paired again later: nothing new");
        Assert.IsTrue(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-fresh2", "77019998877", newBot: false),
            "another number later: that pre-release channel still gets the screen");
    }

    // ---- a confirmed pairing ----

    [Test]
    public void FirstPairing_OwesTheScreen_RemembersTheNumber_AndRecordsTheAppsOwnRequest()
    {
        Assert.AreEqual(TestBot + "AudienceNumberWhatsapp", PersonalChatsOnboarding.PairedNumberKey(TestBot, telegram: false));
        Assert.AreEqual(TestBot + "AudienceNumberTelegram", PersonalChatsOnboarding.PairedNumberKey(TestBot, telegram: true));
        Assert.AreEqual(TestBot + "AudienceOnboardingForWhatsapp", PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: false));
        Assert.AreEqual(TestBot + "AudienceOnboardingForTelegram", PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: true));

        Assert.IsTrue(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-first", "+7 701 111 22 33", newBot: true),
            "the wizard: a new bot is always a first connection");
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false));
        Assert.IsTrue(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: false), "seen from now on");
        Assert.AreEqual("+7 701 111 22 33", PersonalChatsOnboarding.PairedNumber(TestBot, telegram: false), "the first pairing writes the number");
        Assert.AreEqual("wa-first", PlayerPrefs.GetString(PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: false), ""),
            "the app asked for this profile's screen itself");

        Assert.IsTrue(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: true, "tg-first", "", newBot: false),
            "the settings: a channel never seen on this bot is a first connection too");
        Assert.AreEqual("", PersonalChatsOnboarding.PairedNumber(TestBot, telegram: true), "a number not known is not written");
        CollectionAssert.AreEqual(new[] { (TestBot, false), (TestBot, true) }, requested);
    }

    [Test]
    public void FreshProfile_WithAnotherNumber_OwesTheScreen()
    {
        PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-old", "77011112233", newBot: true);
        PersonalChatsOnboarding.Clear(TestBot, telegram: false);   // the owner was done with the screen
        // A new SIM logs the device out (or «Сменить номер» and its Back): the stored number is cleared and the profile deleted —
        // the owner pairs ANOTHER number on a fresh profile.
        PlayerPrefs.DeleteKey(TestBot + "WhatsappNumber");
        requested.Clear();

        Assert.IsTrue(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-fresh", "77019998877", newBot: false),
            "decided against the last paired number, not the stored one a logout cleared");
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false));
        Assert.AreEqual("77019998877", PersonalChatsOnboarding.PairedNumber(TestBot, telegram: false), "written after the decision: the new number");
        Assert.AreEqual("wa-fresh", PlayerPrefs.GetString(PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: false), ""));
        Assert.AreEqual(1, requested.Count);
    }

    [Test]
    public void FreshProfile_WithTheSameNumber_OwesNothing()
    {
        PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-old", "77011112233", newBot: true);
        PersonalChatsOnboarding.Clear(TestBot, telegram: false);
        PlayerPrefs.DeleteKey(PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: false));   // its answer came
        PlayerPrefs.DeleteKey(TestBot + "WhatsappNumber");
        requested.Clear();

        Assert.IsFalse(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-fresh", "+7 (701) 111-22-33", newBot: false),
            "the same number on a fresh profile is the same connection");
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false));
        Assert.IsFalse(PlayerPrefs.HasKey(PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: false)), "nothing asked, nothing recorded");

        Assert.IsFalse(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-fresh", "", newBot: false),
            "a pairing whose number is not known proves nothing…");
        Assert.AreEqual("+7 (701) 111-22-33", PersonalChatsOnboarding.PairedNumber(TestBot, telegram: false), "…and keeps the last number");
        Assert.IsEmpty(requested);
    }

    [Test]
    public void ForgetBot_ForgetsEverythingKeptForTheBot_OnlyTheBotsDeletionCallsIt()
    {
        PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-p", "77011112233", newBot: true);
        PersonalChatsOnboarding.ReportPairing(TestBot, telegram: true, "tg-p", "77019998877", newBot: true);
        PersonalChatsOnboarding.ReportPairing(OtherTestBot, telegram: false, "wa-o", "77015554433", newBot: true);

        PersonalChatsOnboarding.ForgetBot(TestBot);
        foreach (bool telegram in new[] { false, true })
            foreach (string key in new[]
                     {
                         PersonalChatsOnboarding.FlagKey(TestBot, telegram), PersonalChatsOnboarding.ChannelSeenKey(TestBot, telegram),
                         PersonalChatsOnboarding.PairedNumberKey(TestBot, telegram), PersonalChatsOnboarding.RequestedForKey(TestBot, telegram),
                     })
                Assert.IsFalse(PlayerPrefs.HasKey(key), $"{key}: a deleted bot leaves nothing behind");
        Assert.IsTrue(PersonalChatsOnboarding.IsChannelSeen(OtherTestBot, telegram: false), "another bot keeps its own");
        Assert.AreEqual("77015554433", PersonalChatsOnboarding.PairedNumber(OtherTestBot, telegram: false));

        StringAssert.Contains("PersonalChatsOnboarding.ForgetBot(transform.name);", MethodBody(Read("Scripts/Main/Bot.cs"), "public void DeleteBotLocally"),
            "the bot's deletion forgets it all");
        string scripts = Path.Combine(Application.dataPath, "Scripts");
        foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "Bot.cs") continue;
            StringAssert.DoesNotContain("PersonalChatsOnboarding.ForgetBot(", File.ReadAllText(file),
                $"{Path.GetFileName(file)}: only the bot's deletion forgets the channels seen and their numbers");
        }
    }

    // ---- the server's answer to a pairing ----

    [Test]
    public void TheAppsOwnRequest_TakesTheServersAnswerToTheSamePairing_TheScreenComesOnce()
    {
        PlayerPrefs.SetString(TestBot + "WhatsappProfileId", "wa-p");
        string[] bots = { OtherTestBot, TestBot };
        Assert.IsTrue(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-p", "77011112233", newBot: true));
        PersonalChatsOnboarding.Clear(TestBot, telegram: false);   // the owner is done with the screen before the answer lands

        Assert.IsFalse(PersonalChatsOnboarding.AnswerConnected("wa-p", numberRestarted: true, bots), "the app asked at this pairing already");
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false), "…so the screen the owner finished does not come back");
        Assert.AreEqual(1, requested.Count, "requested once");
        Assert.IsFalse(PlayerPrefs.HasKey(PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: false)), "the answer consumed the request");

        Assert.IsTrue(PersonalChatsOnboarding.AnswerConnected("wa-p", numberRestarted: true, bots),
            "that once only: a later restart of the profile is the server's own news");
        Assert.AreEqual(2, requested.Count);
    }

    [Test]
    public void WithoutTheAppsRequest_ARestartOwesTheScreen()
    {
        PlayerPrefs.SetString(TestBot + "WhatsappProfileId", "wa-p");
        Assert.IsFalse(PersonalChatsOnboarding.AnswerConnected("wa-p", numberRestarted: false, new[] { TestBot }), "no restart — nothing owed");
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false));

        Assert.IsTrue(PersonalChatsOnboarding.AnswerConnected("wa-p", numberRestarted: true, new[] { TestBot }));
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false), "owed to the bot holding the profile");
        Assert.IsFalse(PersonalChatsOnboarding.AnswerConnected(global::Bot.UnauthedProfileSentinel, true, new[] { TestBot }));
        Assert.IsFalse(PersonalChatsOnboarding.AnswerConnected("wa-p", true, null));
        Assert.AreEqual(1, requested.Count);
    }

    [Test]
    public void TheAppsOwnRequest_IsConsumedByAnAnswerSayingNoRestart_Too()
    {
        PlayerPrefs.SetString(TestBot + "WhatsappProfileId", "wa-p");
        PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-p", "77011112233", newBot: true);
        PersonalChatsOnboarding.Clear(TestBot, telegram: false);

        Assert.IsFalse(PersonalChatsOnboarding.AnswerConnected("wa-p", numberRestarted: false, new[] { TestBot }));
        Assert.IsFalse(PlayerPrefs.HasKey(PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: false)),
            "the first answer for the profile consumes the request, whatever it says");
        Assert.IsTrue(PersonalChatsOnboarding.AnswerConnected("wa-p", numberRestarted: true, new[] { TestBot }),
            "so the next restart is owed again");
    }

    [Test]
    public void TheAppsOwnRequest_ForAnotherProfile_IsLeftAlone()
    {
        PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-old", "77011112233", newBot: true);   // its answer never came
        PersonalChatsOnboarding.Clear(TestBot, telegram: false);
        PlayerPrefs.SetString(TestBot + "WhatsappProfileId", "wa-new");

        Assert.IsTrue(PersonalChatsOnboarding.AnswerConnected("wa-new", numberRestarted: true, new[] { TestBot }),
            "another profile's answer is judged on its own");
        Assert.AreEqual("wa-old", PlayerPrefs.GetString(PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: false), ""),
            "…and the request it does not name stays");
    }

    // ---- a return to the previous number ----

    [Test]
    public void AReturnToThePreviousNumber_TheServersRestore_WithdrawsTheAppsOwnRequest_TheScreenDoesNotComeBack()
    {
        PlayerPrefs.SetString(TestBot + "WhatsappProfileId", "wa-p");
        string[] bots = { OtherTestBot, TestBot };
        PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-p", "77011112233", newBot: true);
        PersonalChatsOnboarding.AnswerConnected("wa-p", numberRestarted: false, bots);
        PersonalChatsOnboarding.Clear(TestBot, telegram: false);   // the owner was done with the first screen
        // The mistake: another account paired on the same profile — the app owes the screen, the server restarts the moment.
        Assert.IsTrue(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-p", "77019998877", newBot: false), "premise");
        Assert.IsFalse(PersonalChatsOnboarding.AnswerConnected("wa-p", numberRestarted: true, bots), "premise: the app asked already");
        // The return the same day: the number differs from the last paired one — the mistaken one —, so the app owes it again…
        Assert.IsTrue(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-p", "+7 701 111 22 33", newBot: false));
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false), "premise: owed by the app's own comparison");
        requested.Clear();

        // …until the server says it restored the earlier connection: its decisions hold again, nothing is owed for it.
        Assert.IsTrue(PersonalChatsOnboarding.AnswerRestored("wa-p", bots), "the app's own request for this pairing is withdrawn");
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false), "the screen does not come back after a return");
        Assert.IsFalse(PlayerPrefs.HasKey(PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: false)), "the request is consumed");
        Assert.IsEmpty(requested, "a restore never owes the screen");
        Assert.IsTrue(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: false), "the channel stays seen");
        Assert.AreEqual("+7 701 111 22 33", PersonalChatsOnboarding.PairedNumber(TestBot, telegram: false),
            "the previous number is the last paired one again — a later number change still owes the screen");
        Assert.IsFalse(PersonalChatsOnboarding.AnswerRestored("wa-p", bots), "once: nothing of the app's is left to withdraw");
    }

    [Test]
    public void TheServersRestore_WithdrawsOnlyTheAppsOwnRequest_ForThatVeryProfile()
    {
        PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, "wa-old", "77011112233", newBot: true);   // its answer never came
        PersonalChatsOnboarding.Request(TestBot, telegram: true);                                                 // owed on the other channel
        requested.Clear();

        Assert.IsFalse(PersonalChatsOnboarding.AnswerRestored("wa-new", new[] { TestBot }), "another profile's restore");
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false), "…withdraws nothing the app asked for another profile");
        Assert.AreEqual("wa-old", PlayerPrefs.GetString(PersonalChatsOnboarding.RequestedForKey(TestBot, telegram: false), ""));
        Assert.IsFalse(PersonalChatsOnboarding.AnswerRestored(global::Bot.UnauthedProfileSentinel, new[] { TestBot }));
        Assert.IsFalse(PersonalChatsOnboarding.AnswerRestored("wa-old", null));

        Assert.IsTrue(PersonalChatsOnboarding.AnswerRestored("wa-old", new[] { TestBot }));
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false), "its own channel's screen is withdrawn");
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(TestBot, telegram: true), "the other channel's stays owed");
        Assert.IsEmpty(requested, "a restore never owes the screen");
    }

    [Test]
    public void TheServersRestore_NeverClosesTheScreenOnDisplay()
    {
        FolderHarness h = PersonalChatsFolderFixture.Build(TestBot, Profile, hosts);
        PlayerPrefs.SetString(TestBot + "WhatsappProfileId", Profile);
        Assert.IsTrue(PersonalChatsOnboarding.ReportPairing(TestBot, telegram: false, Profile, "77011112233", newBot: false), "premise");
        PersonalChatsOnboarding.TryShow();
        Assert.IsTrue(h.Folder.IsOpen && h.Folder.Mode == FolderMode.Onboarding, "premise: the owner looks at the screen already");

        Assert.IsTrue(PersonalChatsOnboarding.AnswerRestored(Profile, new[] { TestBot }));
        Assert.IsTrue(h.Folder.IsOpen, "the screen on display stays until the owner closes it");
        Assert.AreEqual(FolderMode.Onboarding, h.Folder.Mode);
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false), "…but nothing is owed any more");

        h.Secondary.onClick.Invoke();   // «Отмечу позже»
        Assert.IsFalse(h.Folder.IsOpen);
        PersonalChatsOnboarding.TryShow();
        Assert.IsFalse(h.Folder.IsOpen, "and it does not come back");
    }

    // ---- where it is owed (sources: a pairing, a server answer) ----

    [Test]
    public void EveryConfirmedPairing_GoesThroughOneDecision()
    {
        string wizard = MethodBody(Read("Scripts/Main/Manager.cs"),
            "private static void ReportNewBotPairing(string botName, string profileId, bool telegram, string authedNumber)");
        StringAssert.Contains("PersonalChatsOnboarding.ReportPairing(botName, telegram, profileId, authedNumber, newBot: true);", wizard,
            "the wizard: a new bot's channel, always a first connection, its number remembered");

        string settings = MethodBody(Read("Scripts/Main/BotSettings.Auth.cs"), "private static void ReportSettingsPairing(");
        StringAssert.Contains(
            "PersonalChatsOnboarding.ReportPairing(Manager.openBot.name, channel == ChatChannel.Telegram, profileId, authedNumber, newBot: false);",
            settings, "the settings: never seen, or another number than the last paired one");
        StringAssert.DoesNotContain("PlayerPrefs", settings, "never the stored number: a logout clears it before the next pairing");
        Assert.IsFalse(settings.Contains("IsFirstConnection("), "the seen flag replaced the stored-id guess");
    }

    [Test]
    public void TheConnectedAnswer_TellsANumberChange_UnlessTheAppAskedAtThatPairing()
    {
        string send = MethodBody(Read("Scripts/Chat/ChatAudienceSyncService.cs"), "private IEnumerator SendConnected(");
        StringAssert.Contains("ChatAudienceResponse.ClassifyConnected(www.responseCode, www.downloadHandler?.text, out long connectedAtMs,\n"
                              + "            out bool restarted, out bool restored)",
            send, "the answer's restarted and restored are read");
        int mirrored = send.IndexOf("ChatAudienceStore.ConnectedAtMs(profileId)", StringComparison.Ordinal);
        int applied = send.IndexOf("ChatAudienceStore.ApplyConnected(profileId, connectedAtMs);", StringComparison.Ordinal);
        Assert.IsTrue(mirrored >= 0 && applied > mirrored, "the mirrored moment is read before the answer replaces it");
        int restore = send.IndexOf("if (restored) PersonalChatsOnboarding.AnswerRestored(profileId, BotNames());", StringComparison.Ordinal);
        int answer = send.IndexOf("else PersonalChatsOnboarding.AnswerConnected(profileId, ChatAudienceConnectPolicy.NumberRestarted(restarted, mirrored, connectedAtMs),",
            StringComparison.Ordinal);
        Assert.IsTrue(restore > applied && answer > restore,
            "every answered report is settled against the app's own request: a restore withdraws it, any other answer consumes it");
        StringAssert.DoesNotContain("PersonalChatsOnboarding.RequestForProfile(", send, "never past the app's own request");
    }

    // ---- when it shows ----

    [TestCase(true, true, true, true, true)]
    [TestCase(false, true, true, true, false)]
    [TestCase(true, false, true, true, false)]
    [TestCase(true, true, false, true, false)]
    [TestCase(true, true, true, false, false)]
    public void ShouldShow_NeedsTheFlagTheChatsScreenTheBotAndTheChannel(bool flag, bool screen, bool bot, bool channel, bool shows)
    {
        Assert.AreEqual(shows, PersonalChatsOnboarding.ShouldShow(flag, screen, bot, channel));
    }

    [Test]
    public void Onboarding_ShowsOnce_ClearsOnDoneAndLater()
    {
        FolderHarness h = PersonalChatsFolderFixture.Build(TestBot, Profile, hosts);
        PersonalChatsOnboarding.TryShow();
        Assert.IsFalse(h.Folder.IsOpen, "nothing owed — nothing shown");

        PersonalChatsOnboarding.Request(TestBot, telegram: true);
        PersonalChatsOnboarding.TryShow();
        Assert.IsFalse(h.Folder.IsOpen, "owed for Telegram: the chats show WhatsApp");

        PersonalChatsOnboarding.Request(TestBot, telegram: false);
        PersonalChatsOnboarding.TryShow();
        Assert.IsTrue(h.Folder.IsOpen, "owed for the bot and the channel on screen");
        Assert.AreEqual(FolderMode.Onboarding, h.Folder.Mode);

        h.Primary.onClick.Invoke();   // «Готово · клиентов: 0»
        Assert.IsFalse(h.Folder.IsOpen, "«Готово» closes it");
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false), "…and clears the flag");
        Assert.IsNull(AudienceToast.LastText, "nothing marked: nothing moved, nothing said");
        PersonalChatsOnboarding.TryShow();
        Assert.IsFalse(h.Folder.IsOpen, "shown once — it never comes back by itself");

        PersonalChatsOnboarding.Request(TestBot, telegram: false);
        PersonalChatsOnboarding.TryShow();
        Assert.IsTrue(h.Folder.IsOpen);
        h.Secondary.onClick.Invoke();  // «Отмечу позже»
        Assert.IsFalse(h.Folder.IsOpen, "«Отмечу позже» closes it");
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false), "…and clears the flag too");
        PersonalChatsOnboarding.TryShow();
        Assert.IsFalse(h.Folder.IsOpen);
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(TestBot, telegram: true), "the other channel's screen is still owed");
    }

    [Test]
    public void Onboarding_Done_MovesTheMarkedChats_AndCountsThem()
    {
        FolderHarness h = PersonalChatsFolderFixture.Build(TestBot, Profile, hosts);
        h.AddChat("77010000011@c.us", "Асем", BeforeSec + 30, contact: true);
        h.AddChat("77010000012@c.us", "Мама", BeforeSec + 20, contact: true);
        h.AddChat("77010000013@c.us", "Ерлан", BeforeSec + 10, contact: true);
        PersonalChatsOnboarding.Request(TestBot, telegram: false);
        PersonalChatsOnboarding.TryShow();
        Assert.AreEqual(FolderMode.Onboarding, h.Folder.Mode);

        h.Folder.RowForTests("77010000011@c.us").button.onClick.Invoke();
        h.Folder.RowForTests("77010000013@c.us").button.onClick.Invoke();
        Assert.AreEqual("Готово · клиентов: 2", h.PrimaryLabel.text, "the button counts the marks");

        h.Primary.onClick.Invoke();
        Assert.AreEqual("2 чата стали рабочими", AudienceToast.LastText, "several chats moved at once — the toast counts them");
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(TestBot, telegram: false));
        Assert.IsFalse(h.Folder.IsOpen);
    }

    [Test]
    public void TheChatsScreen_AsksForTheScreen_WhenItShows_OnASwitch_AndOnARequest()
    {
        string list = Read("Scripts/UI/ChatListView.cs");
        StringAssert.Contains("onboardingCheckPending = true", MethodBody(list, "void OnEnable("), "the chat list on screen again");
        StringAssert.Contains("onboardingCheckPending = true", MethodBody(list, "private void HandleActiveBotChanged("), "a bot switch");
        StringAssert.Contains("onboardingCheckPending = true", MethodBody(list, "private void HandleActiveChannelChanged("), "a channel switch");
        StringAssert.Contains("onboardingCheckPending = true", MethodBody(list, "private void HandleOnboardingRequested("), "a flag set now");
        string start = MethodBody(list, "void Start(");
        StringAssert.Contains("manager.OnActiveChannelChanged += HandleActiveChannelChanged;", start);
        StringAssert.Contains("PersonalChatsOnboarding.Requested += HandleOnboardingRequested;", start);
        StringAssert.Contains("PersonalChatsOnboarding.TryShow();", MethodBody(list, "void LateUpdate("),
            "decided at the end of the frame — a bot switch resolves its channel after it announces the bot");
        StringAssert.Contains("PersonalChatsOnboarding.Requested -= HandleOnboardingRequested;", MethodBody(list, "void OnDestroy("));
    }
}

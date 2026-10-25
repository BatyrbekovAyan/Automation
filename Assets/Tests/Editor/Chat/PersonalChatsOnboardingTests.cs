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
///     deleted) — and again when the channel now carries another number: the server says so (<c>restarted</c>), its moment
///     moved later for the same profile, or the settings saw two different numbers. Channels paired before this version are
///     marked seen once, after the bots load, so they never get the screen.
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
            PersonalChatsOnboarding.Clear(bot, telegram: false);
            PersonalChatsOnboarding.Clear(bot, telegram: true);
            PersonalChatsOnboarding.ClearChannelsSeen(bot);
            PlayerPrefs.DeleteKey(bot + "WhatsappProfileId");
            PlayerPrefs.DeleteKey(bot + "TelegramProfileId");
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
        PersonalChatsOnboarding.ClearChannelsSeen(TestBot);        // Bot.DeleteBotLocally
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: false), "a deleted bot's channels are forgotten…");
        Assert.IsFalse(PersonalChatsOnboarding.IsChannelSeen(TestBot, telegram: true), "…both of them: a bot made anew owes the screen again");
    }

    // ---- when it is owed ----

    [Test]
    public void FirstConnection_IsAChannelNeverSeen_AnotherNumberOwesItAgain()
    {
        Assert.IsTrue(ChatAudienceConnectPolicy.OwesOnboarding(false, "", "77011112233"), "a channel never seen on this bot");
        Assert.IsTrue(ChatAudienceConnectPolicy.OwesOnboarding(false, "77011112233", "77011112233"),
            "never seen — even with a number stored (an outside logout, Back on «Сменить номер»: the channel itself was seen)");
        Assert.IsFalse(ChatAudienceConnectPolicy.OwesOnboarding(true, "77011112233", "77011112233"), "a re-pairing of the same number");
        Assert.IsFalse(ChatAudienceConnectPolicy.OwesOnboarding(true, "", "77011112233"), "the stored number is not known: nothing proves a change");
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

        PersonalChatsOnboarding.ClearChannelsSeen(TestBot);
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

    // ---- where it is owed (sources: a pairing, a server answer) ----

    [Test]
    public void EveryConfirmedPairing_MarksTheChannelSeen()
    {
        string wizard = MethodBody(Read("Scripts/Main/Manager.cs"), "private static void ReportNewBotPairing(string botName, string profileId, bool telegram)");
        StringAssert.Contains("PersonalChatsOnboarding.MarkChannelSeen(botName, telegram);", wizard, "the wizard: a new bot's channel");
        StringAssert.Contains("PersonalChatsOnboarding.Request(botName, telegram);", wizard, "…always a first connection");

        string settings = MethodBody(Read("Scripts/Main/BotSettings.Auth.cs"), "private static void ReportSettingsPairing(");
        int seen = settings.IndexOf("PersonalChatsOnboarding.IsChannelSeen(botName, telegram)", StringComparison.Ordinal);
        int mark = settings.IndexOf("PersonalChatsOnboarding.MarkChannelSeen(botName, telegram);", StringComparison.Ordinal);
        int owes = settings.IndexOf("ChatAudienceConnectPolicy.OwesOnboarding(", StringComparison.Ordinal);
        Assert.IsTrue(seen >= 0 && mark > seen && owes > mark, "the settings: read whether the channel was seen, mark it, then decide");
        Assert.IsFalse(settings.Contains("IsFirstConnection("), "the seen flag replaced the stored-id guess");

        string local = MethodBody(Read("Scripts/Main/Bot.cs"), "public void DeleteBotLocally");
        StringAssert.Contains("PersonalChatsOnboarding.ClearChannelsSeen(transform.name);", local, "only the bot's deletion forgets its channels");
    }

    [Test]
    public void TheConnectedAnswer_TellsANumberChange_ToTheBotHoldingTheProfile()
    {
        string send = MethodBody(Read("Scripts/Chat/ChatAudienceSyncService.cs"), "private IEnumerator SendConnected(");
        StringAssert.Contains("ChatAudienceResponse.ClassifyConnected(www.responseCode, www.downloadHandler?.text, out long connectedAtMs, out bool restarted)",
            send, "the answer's restarted is read");
        int mirrored = send.IndexOf("ChatAudienceStore.ConnectedAtMs(profileId)", StringComparison.Ordinal);
        int applied = send.IndexOf("ChatAudienceStore.ApplyConnected(profileId, connectedAtMs);", StringComparison.Ordinal);
        Assert.IsTrue(mirrored >= 0 && applied > mirrored, "the mirrored moment is read before the answer replaces it");
        int policy = send.IndexOf("ChatAudienceConnectPolicy.NumberRestarted(restarted, mirrored, connectedAtMs)", StringComparison.Ordinal);
        int request = send.IndexOf("PersonalChatsOnboarding.RequestForProfile(profileId,", StringComparison.Ordinal);
        Assert.IsTrue(policy > 0 && request > policy, "a restart owes the screen to the bot that holds the profile");
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

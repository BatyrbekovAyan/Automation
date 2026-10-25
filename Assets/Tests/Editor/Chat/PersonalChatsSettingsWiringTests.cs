using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static SourceGuard;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.7, sketch 009 screen 7): the bot settings' «Личные чаты» row.
///   • Its number is <see cref="PersonalChatsCount"/> for THIS screen's bot, read whenever the screen shows (OnEnable for a
///     screen left open, Bot.OpenSettings once the pairing names its bot) — never live while it is shown.
///   • A tap with unsaved edits says «Сначала сохраните изменения» and does nothing else: the way out is the back chevron's
///     own (RequestBack), which discards them. The verdict is the Save button's own dirty check over the same snapshots.
///   • Otherwise: RequestBack, and once the screen has settled closed (its slide-out is its own coroutine; a tab switch would
///     stop it half-way) — the chats switch to the bot, the «Чаты» tab shows, the channel is the one the chats screen shows
///     when the bot has it connected, else the other one, and the folder opens in Browse. No channel connected: the chats
///     screen alone.
/// The tap's order runs across a coroutine and a scene EditMode cannot drive, so it is pinned on the source; the dirty branch
/// and the number are driven for real on the loaded BotSettings.prefab.
/// </summary>
public class PersonalChatsSettingsWiringTests
{
    private const string PersonalChatsPath = "Scripts/Main/BotSettings.PersonalChats.cs";
    private const string SettingsPath = "Scripts/Main/BotSettings.cs";
    private const string ManagerPath = "Scripts/Main/Manager.cs";
    private const string BotPath = "Scripts/Main/Bot.cs";
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string BotName = "PersonalChatsSettingsTestBot";
    private const string Wa = "settings-wa-profile";
    private static readonly long MomentMs = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private readonly List<GameObject> hosts = new List<GameObject>();
    private string dir;
    private Manager previousManager;
    private ChatManager previousChatManager;
    private SwipeToBackBotSettings previousSwipe;
    private GameObject previousOpenBot;
    private BotSettings previousOpenSettings;

    [SetUp]
    public void SetUp()
    {
        AudienceToast.ResetForTests();
        dir = Path.Combine(Path.GetTempPath(), "PersonalChatsSettingsWiringTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ChatAudienceStore.UseCacheFileForTests(Path.Combine(dir, ChatAudienceStore.CacheFileName));
        BotChatStats.UseCacheRootForTests(Path.Combine(dir, "BotCache"));
        previousManager = Manager.Instance;
        previousChatManager = ChatManager.Instance;
        previousSwipe = SwipeToBackBotSettings.Instance;
        previousOpenBot = Manager.openBot;
        previousOpenSettings = Manager.openBotSettings;
        SwipeToBackBotSettings.Instance = null;
        ChatManager.Instance = null;
    }

    [TearDown]
    public void TearDown()
    {
        Manager.openBot = previousOpenBot;
        Manager.openBotSettings = previousOpenSettings;
        SwipeToBackBotSettings.Instance = previousSwipe;
        Manager.Instance = previousManager;
        ChatManager.Instance = previousChatManager;
        foreach (GameObject host in hosts)
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
        hosts.Clear();
        BotChatStats.ResetForTests();
        ChatAudienceStore.ResetForTests();
        AudienceToast.ResetForTests();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // ---- which channel the folder opens on ----

    [TestCase(ChatChannel.WhatsApp, true, true, ChatChannel.WhatsApp)]
    [TestCase(ChatChannel.Telegram, true, true, ChatChannel.Telegram)]
    [TestCase(ChatChannel.WhatsApp, false, true, ChatChannel.Telegram)]
    [TestCase(ChatChannel.Telegram, true, false, ChatChannel.WhatsApp)]
    public void TheFolder_OpensOnTheChannelTheChatsShow_ElseOnTheConnectedOne(ChatChannel shown, bool whatsapp, bool telegram, ChatChannel expected)
    {
        Assert.AreEqual(expected, PersonalChatsFolderModel.ChannelFromSettings(shown, whatsapp, telegram));
    }

    [TestCase(ChatChannel.WhatsApp)]
    [TestCase(ChatChannel.Telegram)]
    public void NoChannelConnected_NoFolder(ChatChannel shown)
    {
        Assert.IsNull(PersonalChatsFolderModel.ChannelFromSettings(shown, false, false), "the chats screen says why there are no chats");
    }

    // ---- the tap ----

    [Test]
    public void ATap_WithUnsavedEdits_SaysSaveFirst_AndLeavesTheScreenAsItIs()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            BotSettings settings = OpenOnTheTestBot(root);
            settings.BotNameField.Value = "Другое имя";   // the stored name is empty: an unsaved edit
            Assert.IsTrue(Manager.Instance.HasUnsavedBotSettings(), "the premise: the screen holds an unsaved edit");

            settings.OnPersonalChatsTapped();

            Assert.AreEqual(PersonalChatsCopy.SaveFirst, AudienceToast.LastText);
            Assert.AreSame(settings, Manager.openBotSettings, "the screen stays open — the chevron's way back would discard the edit");
            Assert.AreEqual("Другое имя", settings.BotNameField.Value, "…and the edit stays");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void TheTap_SaveFirstComesFirst_ThenTheChevronsWayBack_ThenTheWait()
    {
        string tap = MethodBody(Read(PersonalChatsPath), "internal void OnPersonalChatsTapped()");
        int dirty = tap.IndexOf("Manager.Instance.HasUnsavedBotSettings()", StringComparison.Ordinal);
        int toast = tap.IndexOf("AudienceToast.Show(PersonalChatsCopy.SaveFirst)", StringComparison.Ordinal);
        int stop = toast >= 0 ? tap.IndexOf("return;", toast, StringComparison.Ordinal) : -1;
        int back = tap.IndexOf("RequestBack()", StringComparison.Ordinal);
        int wait = tap.IndexOf("Manager.Instance.StartCoroutine(OpenPersonalChatsWhenClosed(", StringComparison.Ordinal);
        Assert.IsTrue(dirty >= 0 && toast > dirty && stop > toast && back > stop && wait > back,
            "unsaved edits → «Сначала сохраните изменения» and nothing else; otherwise the chevron's own way back, then the wait — " +
            "on the Manager, which outlives this screen");
        Assert.AreEqual(1, Calls(tap, "RequestBack(").Count, "one way back");
        Assert.IsFalse(tap.Contains("SetActiveBot(") || tap.Contains("SwitchTab(") || tap.Contains(".Open("),
            "nothing navigates before the screen settled closed — a tab switch would stop its slide-out half-way");
        Assert.IsTrue(tap.Contains("SwipeToBackBotSettings.Instance.IsAnimating"),
            "a tap while the screen slides is refused like the chevron's — a refused back must not leave a wait behind");
    }

    [Test]
    public void OnceTheScreenClosed_TheBot_ThenTheChatsTab_ThenTheChannel_ThenTheFolder()
    {
        string source = Read(PersonalChatsPath);
        string wait = MethodBody(source, "private static IEnumerator OpenPersonalChatsWhenClosed(");
        int loop = wait.IndexOf("while (Manager.openBotSettings == screen", StringComparison.Ordinal);
        int guard = wait.IndexOf("if (Manager.openBotSettings != null) yield break;", StringComparison.Ordinal);
        int go = wait.IndexOf("OpenPersonalChatsFolder(", StringComparison.Ordinal);
        Assert.IsTrue(loop >= 0 && guard > loop && go > guard,
            "wait for the screen to settle closed; a screen that never closed, or another bot's opened meanwhile, ends it");

        string open = MethodBody(source, "private static void OpenPersonalChatsFolder(");
        int bot = open.IndexOf(".SetActiveBot(bot)", StringComparison.Ordinal);
        int tab = open.IndexOf(".SwitchTab(BottomTabManager.WhatsAppTabIndex)", StringComparison.Ordinal);
        int pick = open.IndexOf("PersonalChatsFolderModel.ChannelFromSettings(chats.ActiveChannel", StringComparison.Ordinal);
        int channel = open.IndexOf(".SetActiveChannel(", StringComparison.Ordinal);
        int thread = open.IndexOf(".ShowChatList()", StringComparison.Ordinal);
        int folder = open.IndexOf(".Open(FolderMode.Browse)", StringComparison.Ordinal);
        Assert.IsTrue(bot >= 0 && tab > bot && pick > tab && channel > pick && thread > channel && folder > thread,
            "the bot first (it closes a folder of another bot while the chats are hidden), the «Чаты» tab, the channel read AFTER " +
            "the bot switch (SetActiveBot resolves it after announcing the bot), a chat left open goes the way Back closes it " +
            "(the folder slides in under an open chat), and the folder last — it takes the bot and channel on screen");
    }

    [Test]
    public void TheUnsavedCheck_IsTheSaveButtonsOwnDirtyCheck()
    {
        string manager = Read(ManagerPath);
        string unsaved = Squash(MethodBody(manager, "public bool HasUnsavedBotSettings()"));
        StringAssert.Contains("BotSettingsDirtyPolicy.IsDirty(BuildEditedSettingsSnapshot(), ReadSavedSettings(openBot.name))", unsaved,
            "the very snapshots the Save button compares — never a second, hand-rolled check");
        string canSave = Squash(MethodBody(manager, "public bool CanSaveBotSettings()"));
        StringAssert.Contains("BotSettingsDirtyPolicy.CanSave(BuildEditedSettingsSnapshot(), ReadSavedSettings(openBot.name))", canSave);
    }

    // ---- the number ----

    [Test]
    public void TheNumber_IsThisBotsPersonalChats_ForTheOpenScreenOnly()
    {
        WriteWhatsappCache(
            WaRow("77000000001@c.us", fullName: "Мама", time: "2026-10-21T10:00:00Z"),   // a contact — personal
            WaRow("120363000000000003@g.us", fullName: "", time: "2026-10-21T10:00:00Z"),   // a group — personal
            WaRow("77000000002@c.us", fullName: "", time: "2026-10-21T10:00:00Z"));      // a new person — working
        ChatAudienceStore.ApplyConnected(Wa, MomentMs);

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var settings = root.GetComponent<BotSettings>();
            Assert.IsNotNull(settings.PersonalChatsCountLabel, "BotSettings.personalChatsCount is wired — run Tools/Personal Chats/Build");
            Manager.Instance = OpenChatFixture.ManagerWithBot(BotName, Wa, hosts);
            settings.PersonalChatsCountLabel.text = "—";

            settings.RefreshPersonalChatsCount();
            Assert.AreEqual("—", settings.PersonalChatsCountLabel.text, "not the open screen yet — nothing is read for it");

            Manager.openBot = Manager.Instance.FindBotByName(BotName).gameObject;
            Manager.openBotSettings = settings;
            settings.RefreshPersonalChatsCount();
            Assert.AreEqual("2", settings.PersonalChatsCountLabel.text, "the contact and the group, never the new person");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void TheNumber_IsReadWhenTheScreenShows_NeverLive()
    {
        string settings = Read(SettingsPath);
        Assert.IsTrue(MethodBody(settings, "public void OnEnable()").Contains("RefreshPersonalChatsCount()"),
            "a screen left open shows again — after the folder closed back to the chat list, say");
        Assert.IsTrue(MethodBody(settings, "void Start()").Contains("WirePersonalChats()"));

        string open = MethodBody(Read(BotPath), "private void OpenSettings()");
        int pair = open.IndexOf("Manager.openBotSettings = ", StringComparison.Ordinal);
        int read = open.IndexOf("Manager.openBotSettings.RefreshPersonalChatsCount()", StringComparison.Ordinal);
        Assert.IsTrue(pair >= 0 && read > pair, "a fresh open reads it after the pairing — OnEnable ran before it named the bot");

        string personal = Read(PersonalChatsPath);
        Assert.IsTrue(MethodBody(personal, "private void WirePersonalChats()").Contains("personalChatsRow.onClick.AddListener(OnPersonalChatsTapped)"));
        Assert.IsFalse(personal.Contains("ChatAudienceStore.OnChanged"), "the number is not live while the screen is shown");
        Assert.IsTrue(MethodBody(personal, "public void RefreshPersonalChatsCount()").Contains("PersonalChatsCount.Read("));
    }

    // ---- helpers ----

    // The loaded prefab as the open screen of the test bot (Manager.openBot / openBotSettings — Bot.OpenSettings' pairing).
    private BotSettings OpenOnTheTestBot(GameObject root)
    {
        var settings = root.GetComponent<BotSettings>();
        Assert.IsNotNull(settings, "BotSettings.prefab carries BotSettings on its root");
        Manager.Instance = OpenChatFixture.ManagerWithBot(BotName, Wa, hosts);
        Manager.openBot = Manager.Instance.FindBotByName(BotName).gameObject;
        Manager.openBotSettings = settings;
        return settings;
    }

    private void WriteWhatsappCache(params object[] dialogs)
    {
        string folder = Path.Combine(dir, "BotCache", BotName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "chats.json"), JsonConvert.SerializeObject(new { status = "done", dialogs },
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
    }

    private static object WaRow(string id, string fullName, string time) => new
    {
        id,
        name = "Chat " + id,
        contact = new { Found = true, FullName = fullName },
        isGroup = id.EndsWith("@g.us", StringComparison.Ordinal),
        unread_count = 0,
        last_message_id = "M-" + id,
        last_timestamp = time,
    };

    private static string Squash(string text) => Regex.Replace(text, @"\s+", " ");
}

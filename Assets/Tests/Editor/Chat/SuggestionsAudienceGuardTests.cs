using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using static SourceGuard;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.5, §5.8): the suggestions panel, its requests and the ✦ key exist only in a chat that is a
/// DECIDED working chat — <see cref="ChatAudienceStore.AllowsAssistant"/>: the server's business decision or the owner's move
/// to business on its way. Not in a personal chat, not in a chat nobody has decided yet, whatever the chat's stored mode,
/// the bot's schedule or the quota say (the two fallbacks included).
///   • SuggestionsController: the open chat's mode is «no panel» outside a working chat, ShowPanel leaves at once, the
///     capsule's tap does nothing, and a change of the audience resolves the open chat again (made personal: the panel and
///     the key go; made working: they come back as the chat's mode says). <c>WouldSuggestIgnoringAudience</c> is the same
///     mode without the audience — what the «Сделать чат рабочим?» sheet tells the owner.
///   • N8nSuggestionsProvider: no request at all for such a chat (checked before the coroutine and again after the drain,
///     right before the payload); the server's own refusal, <c>personal_chat</c>, is an empty panel, never an error text.
/// The controller's wiring is a source guard (EditMode cannot raise its panel); the provider is driven for real.
/// </summary>
public class SuggestionsAudienceGuardTests
{
    private const string ControllerPath = "Scripts/Chat/SuggestionsController.cs";
    private const string ProviderPath = "Scripts/Chat/N8nSuggestionsProvider.cs";
    private const string TestBot = "SuggestionsAudienceGuardBot";
    private const string Profile = "guard-wa-profile";
    private const string Chat = "77000000101@c.us";

    // Every way the client records a reply mode — none of them may run off the audience question.
    private static readonly string[] Writes =
    {
        "SemiAutoStore.Set(", "PushReplyModeForActiveChat(", "SyncReplyMode(", "ReassertReplyMode", "ReplyModeToggleBinder.CommitMode(",
    };

    private readonly List<GameObject> hosts = new List<GameObject>();
    private string dir;
    private Manager previousManager;
    private ChatManager previousChatManager;

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "SuggestionsAudienceGuardTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ChatAudienceStore.UseCacheFileForTests(Path.Combine(dir, ChatAudienceStore.CacheFileName));
        previousManager = Manager.Instance;
        previousChatManager = ChatManager.Instance;
    }

    [TearDown]
    public void TearDown()
    {
        Manager.Instance = previousManager;
        ChatManager.Instance = previousChatManager;
        foreach (GameObject host in hosts)
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
        hosts.Clear();
        ChatAudienceStore.ResetForTests();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // ---- SuggestionsController ----

    [Test]
    public void TheOpenChatsMode_IsNoPanel_OutsideAWorkingChat()
    {
        string source = Read(ControllerPath);
        string resolve = MethodBody(source, "private bool ResolveSemiAutoForOpenChat(");
        int gate = resolve.IndexOf("AllowsAssistant(", StringComparison.Ordinal);
        Assert.GreaterOrEqual(gate, 0, "ResolveSemiAutoForOpenChat must ask whether the open chat is a working chat");
        Assert.Less(gate, resolve.IndexOf("SemiAutoStore.IsOn(", StringComparison.Ordinal),
            "…FIRST: a personal chat has no panel whatever its stored mode says");
        Assert.Less(gate, resolve.IndexOf("FallbackSemiAutoForOpenBot()", StringComparison.Ordinal),
            "…and whatever the schedule or the quota say — the fallbacks never reach a personal chat");
        StringAssert.Contains("return false", resolve.Substring(gate, resolve.IndexOf("SemiAutoStore.IsOn(", StringComparison.Ordinal) - gate),
            "outside a working chat the mode is «no panel»");

        string helper = MethodBody(source, "private static bool OpenChatAllowsAssistant()");
        StringAssert.Contains("ChatAudienceStore.AllowsAssistant(", helper, "the store's verdict — the server's decision or the owner's move, never the rule");
        StringAssert.Contains("ActiveChannelProfileId()", helper, "under the active channel's profile, the key the decisions are kept under");
        StringAssert.Contains("CurrentChatId", helper, "for the open chat");
    }

    [Test]
    public void ShowPanel_LeavesAtOnce_OutsideAWorkingChat()
    {
        string show = MethodBody(Read(ControllerPath), "private void ShowPanel(");
        int gate = show.IndexOf("AllowsAssistant(", StringComparison.Ordinal);
        Assert.GreaterOrEqual(gate, 0, "ShowPanel must refuse a chat that is not a working chat");
        Assert.Less(gate, show.IndexOf("SlotOpenAllowed", StringComparison.Ordinal),
            "…before anything — not even a parked show may wait for such a chat");
        Assert.Less(gate, show.IndexOf("_pendingShow = true", StringComparison.Ordinal));
        Assert.Less(gate, show.IndexOf("_panel.ShowInSlot()", StringComparison.Ordinal));
    }

    [Test]
    public void TheCapsuleTap_DoesNothing_OutsideAWorkingChat()
    {
        string source = Read(ControllerPath);
        string toggle = MethodBody(source, "private void HandleToggle(");
        int gate = toggle.IndexOf("AllowsAssistant(", StringComparison.Ordinal);
        Assert.GreaterOrEqual(gate, 0, "the chip's tap must ask whether the chat is a working chat");
        Assert.Less(gate, toggle.IndexOf("SuggestionToggleTapPolicy.Decide(", StringComparison.Ordinal), "…before the tap is read at all");
        Assert.Less(gate, toggle.IndexOf("SemiAutoStore.Set(", StringComparison.Ordinal), "…and before anything is written");

        string dismiss = MethodBody(source, "private void DismissFallbackForThisOpen()");
        int dismissGate = dismiss.IndexOf("AllowsAssistant(", StringComparison.Ordinal);
        Assert.GreaterOrEqual(dismissGate, 0, "the chip's quiet dismiss is the capsule's tap too");
        Assert.Less(dismissGate, dismiss.IndexOf("_fallbackDismissed = true", StringComparison.Ordinal));
    }

    [Test]
    public void TheKey_FollowsThePanelsMode()
    {
        string key = MethodBody(Read(ControllerPath), "private void ApplyKeyStyle()");
        StringAssert.Contains("ComposerSlotKeyModel.For(_slotState, _semiAutoOn)", key,
            "the ✦ key exists only where the panel's mode is on — never in a chat that is not a working chat");
    }

    [Test]
    public void AnAudienceChange_ResolvesTheOpenChatAgain()
    {
        string source = Read(ControllerPath);
        StringAssert.Contains("ChatAudienceStore.OnChanged += HandleAudienceChanged", MethodBody(source, "void Awake()"),
            "subscribed in Awake — the change can land while the chat screen is closed");
        StringAssert.Contains("ChatAudienceStore.OnChanged -= HandleAudienceChanged", MethodBody(source, "void OnDestroy()"));

        string changed = MethodBody(source, "private void HandleAudienceChanged()");
        int resolved = changed.IndexOf("_chatResolved", StringComparison.Ordinal);
        int clock = changed.IndexOf("_fallbackCheckedAt = -1f", StringComparison.Ordinal);
        int recheck = changed.IndexOf("RecheckFallback()", StringComparison.Ordinal);
        Assert.GreaterOrEqual(resolved, 0, "never against a chat that was not resolved");
        Assert.IsTrue(clock > resolved && recheck > clock,
            "the minute clock is reset first, so a recheck a drag or a keyboard handoff refuses runs on the next frame");
        foreach (string write in Writes)
            Assert.IsFalse(changed.Contains(write), $"an audience change writes no reply mode ({write})");
    }

    [Test]
    public void ASetTheServerRefused_IsAskedForAgain_OnceTheMoveLands()
    {
        string source = Read(ControllerPath);
        string result = MethodBody(source, "private void OnResult(");
        StringAssert.Contains("SuggestionErrorKind.PersonalChat", result,
            "the panel remembers the server refused the set for a chat it did not hold working yet");
        StringAssert.Contains("_audienceRefused = false", MethodBody(source, "private void IssueRequest("),
            "any new request supersedes the refusal");

        string changed = MethodBody(source, "private void HandleAudienceChanged()");
        int recheck = changed.IndexOf("RecheckFallback()", StringComparison.Ordinal);
        int retry = changed.IndexOf("IssueRequest(", StringComparison.Ordinal);
        Assert.Greater(retry, recheck, "after the open chat is resolved again, the refused set is asked for again");
        StringAssert.Contains("_audienceRefused", changed.Substring(recheck));
        StringAssert.Contains("_semiAutoOn", changed.Substring(recheck), "…only while the panel is still on");
    }

    [Test]
    public void WouldSuggestIgnoringAudience_IsTheModeWithoutTheAudience()
    {
        MethodInfo method = typeof(SuggestionsController).GetMethod("WouldSuggestIgnoringAudience", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNotNull(method, "public bool WouldSuggestIgnoringAudience()");
        Assert.AreEqual(typeof(bool), method.ReturnType);
        Assert.IsEmpty(method.GetParameters());

        string body = MethodBody(Read(ControllerPath), "public bool WouldSuggestIgnoringAudience()");
        StringAssert.Contains("SemiAutoStore.IsOn(", body, "the chat's stored mode");
        StringAssert.Contains("FallbackSemiAutoForOpenBot()", body, "the schedule and the quota");
        Assert.IsFalse(body.Contains("AllowsAssistant"), "without the audience — the chat is personal when the sheet asks");
        foreach (string write in Writes)
            Assert.IsFalse(body.Contains(write), $"asking writes nothing ({write})");
    }

    [Test]
    public void WouldSuggestIgnoringAudience_ReadsTheChatsStoredMode()
    {
        SetUpOpenChat();
        var controller = NewHost("Suggestions").AddComponent<SuggestionsController>();
        Func<string, int> savedGet = SemiAutoStore.GetInt;
        Action<string, int> savedSet = SemiAutoStore.SetIntAndSave;
        Func<string, bool> savedDefault = SemiAutoStore.BotDefaultSemi;
        var memory = new Dictionary<string, int>();
        SemiAutoStore.GetInt = key => memory.TryGetValue(key, out int value) ? value : 0;
        SemiAutoStore.SetIntAndSave = (key, value) => memory[key] = value;
        SemiAutoStore.BotDefaultSemi = _ => false;
        try
        {
            Assert.IsFalse(controller.WouldSuggestIgnoringAudience(), "the bot's «Авто» default: it would answer itself");
            SemiAutoStore.Set(TestBot, Chat, true);
            Assert.IsTrue(controller.WouldSuggestIgnoringAudience(), "«Выбрать» in this chat: the assistant would suggest");
            SemiAutoStore.Set(TestBot, Chat, false);
            Assert.IsFalse(controller.WouldSuggestIgnoringAudience(), "«Авто» in this chat: it would answer itself");
            Assert.IsFalse(ChatAudienceStore.AllowsAssistant(Profile, Chat), "…asked of a chat that is not working at all");
        }
        finally
        {
            SemiAutoStore.GetInt = savedGet;
            SemiAutoStore.SetIntAndSave = savedSet;
            SemiAutoStore.BotDefaultSemi = savedDefault;
        }
    }

    // ---- N8nSuggestionsProvider ----

    [Test]
    public void TheProvider_AsksTheAudience_BeforeAnyRequest_AndAgainBeforeThePayload()
    {
        string source = Read(ProviderPath);
        string request = MethodBody(source, "public void Request(");
        int gate = request.IndexOf("ChatAudienceStore.AllowsAssistant(", StringComparison.Ordinal);
        Assert.GreaterOrEqual(gate, 0, "Request must refuse a chat that is not a working chat");
        Assert.Less(gate, request.IndexOf("StartCoroutine(Run(", StringComparison.Ordinal), "…before the coroutine — no network at all");

        string run = MethodBody(source, "private IEnumerator Run(");
        int drain = run.IndexOf("WaitForChatFetchesDrain()", StringComparison.Ordinal);
        int again = run.IndexOf("ChatAudienceStore.AllowsAssistant(", StringComparison.Ordinal);
        int send = run.IndexOf("AuthorizedRequest.Send(", StringComparison.Ordinal);
        Assert.IsTrue(drain >= 0 && again > drain && send > again,
            "the chat may be made personal during the drain — asked again right before the payload is built and sent");
    }

    [TestCase(null)]
    [TestCase(AudienceKind.Personal)]
    public void ANonWorkingChat_GetsAnEmptyAnswer_WithoutTheNetwork(AudienceKind? decided)
    {
        SetUpOpenChat();
        if (decided.HasValue) ChatAudienceStore.ApplyDesire(Profile, Chat, decided.Value);

        var answers = new List<SuggestionResult>();
        new N8nSuggestionsProvider().Request(new SuggestionRequest { chatId = Chat, requestSeq = 41 }, answers.Add);

        Assert.AreEqual(1, answers.Count, "answered at once — nothing was started");
        Assert.AreEqual(SuggestionStatus.Empty, answers[0].status, "an empty panel, never an error");
        Assert.AreEqual(SuggestionErrorKind.PersonalChat, answers[0].errorKind, "…marked as the audience's refusal");
        Assert.AreEqual(41, answers[0].requestSeq);
        Assert.IsNull(answers[0].items);
    }

    [Test]
    public void TheServersRefusalForAPersonalChat_IsAnEmptyPanel_NotAnErrorText()
    {
        SuggestionResult result = N8nSuggestionsProvider.MapResponse("{\"v\":1,\"requestSeq\":7,\"suggestions\":[],\"error\":\"personal_chat\"}", 7);
        Assert.AreEqual(SuggestionStatus.Empty, result.status, "the quiet «no suggestions» state — the chat is simply not a working one");
        Assert.AreEqual(SuggestionErrorKind.PersonalChat, result.errorKind);
        Assert.IsNull(result.items);
        Assert.AreEqual(7, result.requestSeq);
        Assert.AreEqual("personal_chat", SuggestionErrorKind.PersonalChat, "the server's wire word");

        SuggestionResult other = N8nSuggestionsProvider.MapResponse("{\"v\":1,\"requestSeq\":8,\"suggestions\":[],\"error\":\"subscription\"}", 8);
        Assert.AreEqual(SuggestionStatus.Error, other.status, "every other refusal still reads as the error it is");
        Assert.AreEqual(SuggestionErrorKind.Subscription, other.errorKind);
    }

    // ---- the harness ----

    // A Manager with the test bot (a WhatsApp profile) and a bare ChatManager on that bot's WhatsApp channel with the chat open.
    private void SetUpOpenChat()
    {
        var managerGo = NewHost("ManagerHost");
        var manager = managerGo.AddComponent<Manager>();
        var botsParent = NewHost("BotsParent");
        var botGo = new GameObject(TestBot);
        botGo.transform.SetParent(botsParent.transform);
        botGo.AddComponent<Bot>().whatsappProfileId = Profile;
        FieldInfo bots = typeof(Manager).GetField("BotsParent", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(bots, "Manager.BotsParent moved — update SuggestionsAudienceGuardTests");
        bots.SetValue(manager, botsParent);
        Manager.Instance = manager;

        var chatManager = NewHost("ChatManagerHost").AddComponent<ChatManager>();
        typeof(ChatManager).GetProperty(nameof(ChatManager.ActiveChannel))?.SetValue(chatManager, ChatChannel.WhatsApp);
        typeof(ChatManager).GetProperty(nameof(ChatManager.CurrentBotId))?.SetValue(chatManager, TestBot);
        FieldInfo open = typeof(ChatManager).GetField("currentChatId", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(open, "ChatManager.currentChatId moved — update SuggestionsAudienceGuardTests");
        open.SetValue(chatManager, Chat);
        ChatManager.Instance = chatManager;
        Assert.AreEqual(Profile, chatManager.ActiveChannelProfileId(), "the harness resolves the bot's WhatsApp profile");
    }

    private GameObject NewHost(string name)
    {
        var go = new GameObject(name);
        hosts.Add(go);
        return go;
    }
}

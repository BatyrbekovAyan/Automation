using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// «Личные и рабочие чаты»: where the decisions mirror and the moves queue meet the rest of the app. The pairing policy and
/// the onboarding flags are pure and tested on behaviour; everything else is pinned at the source, because EditMode cannot
/// pair a channel, delete a bot or send a request:
///   • every confirmed pairing (wizard and settings) reports the connection, and asks for «Кто из них клиенты?» when the
///     channel is new to the bot or carries another number — decided BEFORE the stored number is overwritten;
///   • a server deletion holds the queue (nothing may recreate the rows Delete Bot Files removes) and releases it when the
///     bot stays; the local delete and the full wipe forget it;
///   • the service boots itself, waits at the reply-mode send gate, sends only through AuthorizedRequest, reports
///     connections first, reads the list on boot / bot change / return / after each delivery, and never lets a stale list
///     overwrite a fresher write.
/// </summary>
public class ChatAudienceWiringTests
{
    private const string FlagBot = "ChatAudienceWiringFlagBot";

    private static string Src(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, rel));

    private static string MethodBody(string text, string signature)
    {
        int i = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.GreaterOrEqual(i, 0, $"{signature} not found");
        int open = text.IndexOf('{', i);
        int depth = 0;
        for (int k = open; k < text.Length; k++)
        {
            if (text[k] == '{') depth++;
            else if (text[k] == '}' && --depth == 0) return text.Substring(open, k - open + 1);
        }
        Assert.Fail($"{signature}: unbalanced braces");
        return null;
    }

    // An expression-bodied member («=> …;»): from its arrow to the semicolon that ends it. MethodBody would skip to the next
    // member's braces.
    private static string ExpressionBody(string text, string signature)
    {
        int i = At(text, signature);
        int arrow = text.IndexOf("=>", i, StringComparison.Ordinal);
        int brace = text.IndexOf('{', i);
        Assert.IsTrue(arrow > i && (brace < 0 || arrow < brace), $"{signature}: not an expression-bodied member");
        return text.Substring(arrow, text.IndexOf(';', arrow) - arrow + 1);
    }

    private static int At(string text, string needle)
    {
        int i = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.GreaterOrEqual(i, 0, $"«{needle}» not found");
        return i;
    }

    private static string Service => Src("Scripts/Chat/ChatAudienceSyncService.cs");

    [TearDown]
    public void ClearTheFlags()
    {
        PersonalChatsOnboarding.Clear(FlagBot, telegram: false);
        PersonalChatsOnboarding.Clear(FlagBot, telegram: true);
    }

    // ── the pairing: pure ────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void ConnectPolicy_FirstConnectionAndNumberChanged()
    {
        Assert.IsTrue(ChatAudienceConnectPolicy.IsFirstConnection(Bot.UnauthedProfileSentinel, ""), "a channel never paired on this bot");
        Assert.IsTrue(ChatAudienceConnectPolicy.IsFirstConnection("", Bot.UnauthedProfileSentinel));
        Assert.IsTrue(ChatAudienceConnectPolicy.IsFirstConnection(null, null));
        Assert.IsFalse(ChatAudienceConnectPolicy.IsFirstConnection("wa-old", ""), "the channel had a profile: a reconnection");
        Assert.IsFalse(ChatAudienceConnectPolicy.IsFirstConnection(Bot.UnauthedProfileSentinel, "wa-retired"),
            "the server retired its profile: a reconnection of the same bot");

        Assert.IsTrue(ChatAudienceConnectPolicy.NumberChanged("77011112233", "77019998877"), "another number");
        Assert.IsFalse(ChatAudienceConnectPolicy.NumberChanged("+7 701 111 22 33", "77011112233"), "the same digits, written differently");
        Assert.IsFalse(ChatAudienceConnectPolicy.NumberChanged("", "77011112233"), "the stored number is not known");
        Assert.IsFalse(ChatAudienceConnectPolicy.NumberChanged("77011112233", ""), "the new number is not known");
        Assert.IsFalse(ChatAudienceConnectPolicy.NumberChanged(null, null));
        Assert.IsFalse(ChatAudienceConnectPolicy.NumberChanged("{\"status\":\"done\",\"phone\":\"77011112233\"}", "77019998877"),
            "a stale status blob stored by an old build is no number");
    }

    [Test]
    public void OnboardingFlags_RequestAndClear()
    {
        Assert.AreEqual("Bot7AudienceOnboardingWhatsapp", PersonalChatsOnboarding.FlagKey("Bot7", telegram: false));
        Assert.AreEqual("Bot7AudienceOnboardingTelegram", PersonalChatsOnboarding.FlagKey("Bot7", telegram: true));

        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(FlagBot, telegram: false));
        PersonalChatsOnboarding.Request(FlagBot, telegram: false);
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(FlagBot, telegram: false));
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(FlagBot, telegram: true), "each channel has its own screen");
        Assert.AreEqual(1, PlayerPrefs.GetInt(PersonalChatsOnboarding.FlagKey(FlagBot, telegram: false), 0), "a per-bot PlayerPrefs flag");

        PersonalChatsOnboarding.Request(FlagBot, telegram: true);
        PersonalChatsOnboarding.Clear(FlagBot, telegram: false);
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested(FlagBot, telegram: false));
        Assert.IsTrue(PersonalChatsOnboarding.IsRequested(FlagBot, telegram: true));
        Assert.IsFalse(PlayerPrefs.HasKey(PersonalChatsOnboarding.FlagKey(FlagBot, telegram: false)), "cleared means deleted");

        PersonalChatsOnboarding.Request("", telegram: false);
        Assert.IsFalse(PersonalChatsOnboarding.IsRequested("", telegram: false), "no bot, no flag");
        Assert.IsFalse(PlayerPrefs.HasKey(PersonalChatsOnboarding.FlagKey("", telegram: false)));
    }

    // ── the pairing: where it is reported ────────────────────────────────────────────────────────────────────

    [Test]
    public void Wizard_ReportsEachPairedChannel_BeforeItsCreate()
    {
        string manager = Src("Scripts/Main/Manager.cs");
        string wizard = MethodBody(manager, "private IEnumerator CreateBotFromForm()");
        foreach (var (auth, report, create) in new[]
                 {
                     ("while (!whatsappAuthCompleted)", "ReportNewBotPairing(newBot.name, whatsappProfileId, telegram: false);",
                      "StartCoroutine(CreateWhatsappWorkflowFromStart(newBot));"),
                     ("while (!telegramAuthCompleted)", "ReportNewBotPairing(newBot.name, telegramProfileId, telegram: true);",
                      "StartCoroutine(CreateTelegramWorkflowFromStart(newBot));"),
                 })
        {
            int confirmed = At(wizard, auth), reported = At(wizard, report), created = At(wizard, create);
            Assert.IsTrue(confirmed < reported && reported < created, $"{report}: after the pairing is confirmed, before the Create");
            Assert.IsFalse(wizard.Substring(reported, created - reported).Contains("}"), $"{report}: in the same branch as the Create");
        }
        string helper = MethodBody(manager, "private static void ReportNewBotPairing(string botName, string profileId, bool telegram)");
        Assert.IsTrue(helper.Contains("ChatAudienceSyncService.ReportConnected(profileId);"), "the server learns the connection");
        Assert.IsTrue(helper.Contains("PersonalChatsOnboarding.MarkChannelSeen(botName, telegram);"), "the channel is seen on this bot from now on");
        Assert.IsTrue(helper.Contains("PersonalChatsOnboarding.Request(botName, telegram);"),
            "a new bot is a first connection: «Кто из них клиенты?» is owed");
    }

    [Test]
    public void Settings_ReportThePairing_BeforeTheStoredNumberIsOverwritten()
    {
        string auth = Src("Scripts/Main/BotSettings.Auth.cs");
        foreach (var (done, channel, field, word, show, open) in new[]
                 {
                     ("private void OnWhatsappAuthFromSettingsDone()", "WhatsApp", "_whatsappBeforePairing", "Whatsapp",
                      "private void ShowWhatsappAuthFromSettings(string profileId)", "Manager.Instance.OpenWhatsappAuthFromSettings("),
                     ("private void OnTelegramAuthFromSettingsDone()", "Telegram", "_telegramBeforePairing", "Telegram",
                      "private void ShowTelegramAuthFromSettings(string profileId)", "Manager.Instance.OpenTelegramAuthFromSettings("),
                 })
        {
            string body = MethodBody(auth, done);
            int reported = At(body, $"ReportSettingsPairing(ChatChannel.{channel}, Manager.openBot.GetComponent<Bot>().{word.ToLowerInvariant()}ProfileId,");
            int overwritten = At(body, $"PlayerPrefs.SetString(Manager.openBot.name + \"{word}Number\"");
            Assert.Less(reported, overwritten, $"{done}: decided while the stored number is still the old one");
            Assert.IsTrue(body.Contains($"{field} ?? StoredNumber(ChatChannel.{channel})"),
                $"{done}: the number as it was when the pairing began");
            Assert.IsTrue(body.Contains($"Manager.Instance.LastAuthed{word}Number"), $"{done}: against the number just paired");

            // «Сменить номер» logs out first — the logout clears the stored number long before the new one pairs: the number is
            // read when the pairing BEGINS.
            string begin = MethodBody(auth, show);
            Assert.Less(At(begin, $"{field} = StoredNumber(ChatChannel.{channel});"), At(begin, open), $"{show}: captured before the auth page opens");
        }

        string helper = MethodBody(auth, "private static void ReportSettingsPairing(");
        int report = At(helper, "ChatAudienceSyncService.ReportConnected(profileId);");
        int seen = At(helper, "PersonalChatsOnboarding.IsChannelSeen(botName, telegram)");
        int mark = At(helper, "PersonalChatsOnboarding.MarkChannelSeen(botName, telegram);");
        int ask = At(helper, "PersonalChatsOnboarding.Request(botName, telegram);");
        Assert.IsTrue(helper.Contains("ChatAudienceConnectPolicy.OwesOnboarding(seen, numberBefore, authedNumber)"),
            "a channel never seen on this bot, or another number");
        Assert.IsTrue(report < seen && seen < mark && mark < ask,
            "every pairing is reported and marks its channel seen; the screen only for a first connection or another number");
        Assert.IsFalse(helper.Contains("IsFirstConnection("), "the seen flag decides a first connection, not the stored ids");

        string stored = ExpressionBody(auth, "private static string StoredNumber(ChatChannel channel)");
        Assert.IsTrue(stored.Contains("\"Number\""), "the stored number of the channel");
    }

    // ── deletion ─────────────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void ServerDeletion_HoldsTheQueue_DrainsIt_AndReleasesItWhenTheBotStays()
    {
        string server = MethodBody(Src("Scripts/Main/Manager.cs"), "public IEnumerator DeleteBotOnServer");
        int hold = At(server, "ChatAudienceSyncService.Hold(botId);");
        int firstCall = At(server, "WappiProxy.Send(");
        int drain = At(server, "ChatAudienceSyncService.IsSending(botId)");
        Assert.Less(hold, firstCall, "held before the first server call — Delete Bot Files removes the line's decisions");
        Assert.IsTrue(drain > hold && drain < firstCall, "a request already on the wire lands before the server wipes the rows");
        Assert.IsTrue(server.Contains("ChatAudienceSyncService.RequestTimeoutSeconds"), "…waited for as long as its own timeout");
        int keep = At(server, "if (!outcome.RemovesBot)");
        Assert.Greater(At(server, "ChatAudienceSyncService.Release(botId);"), keep, "a bot the server keeps gets its queue back");
        Assert.IsFalse(server.Contains("ChatAudienceSyncService.ForgetBot("), "never forgotten before the verdict");
    }

    [Test]
    public void DeleteBotLocally_ForgetsTheBot_AndClearsBothFlags()
    {
        string local = MethodBody(Src("Scripts/Main/Bot.cs"), "public void DeleteBotLocally");
        int forget = At(local, "ChatAudienceSyncService.ForgetBot(transform.name);");
        Assert.Less(forget, At(local, "ProfileRetirement.Clear(transform.name)"),
            "forgotten while the retired ids are still stored — their mirror goes too");
        Assert.IsTrue(local.Contains("PersonalChatsOnboarding.Clear(transform.name, telegram: false);")
                      && local.Contains("PersonalChatsOnboarding.Clear(transform.name, telegram: true);"),
            "«Кто из них клиенты?» is not owed for a bot that is gone — a recreated one gets it afresh");
        Assert.IsTrue(local.Contains("PersonalChatsOnboarding.ClearChannelsSeen(transform.name);"),
            "…its channels are forgotten too: a bot made anew connects them for the first time");

        string forgetBot = MethodBody(Service, "private void Forget(string botName)");
        Assert.IsTrue(forgetBot.Contains("ChatAudienceSyncQueue.ForgetBot(_model, botName,") && forgetBot.Contains("ChatAudienceStore.ForgetProfile("),
            "the bot's queued entries and the mirror of its profiles");
        Assert.IsTrue(forgetBot.Contains("_held.Remove(botName)"), "forgetting a bot also drops its hold");
        Assert.IsTrue(forgetBot.Contains("_forgets++"), "a list on its way must not bring the bot's decisions back");
    }

    [Test]
    public void FullWipe_ForgetsAll_BeforePlayerPrefsDeleteAll()
    {
        string wipe = MethodBody(Src("Scripts/Main/ProfileSubPages.Account.cs"), "private void FinishWipe");
        Assert.Less(At(wipe, "ChatAudienceSyncService.ForgetAll();"), At(wipe, "PlayerPrefs.DeleteAll"),
            "the in-memory queue re-persists itself — empty it before the wipe");
        string everything = MethodBody(Service, "private void ForgetEverything()");
        Assert.IsTrue(everything.Contains("ChatAudienceSyncQueue.ForgetAll(_model)") && everything.Contains("ChatAudienceStore.ClearAll()"),
            "the queue and the mirror's cache file both go");
    }

    // ── the service ──────────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Service_ExposesTheContract()
    {
        var type = typeof(ChatAudienceSyncService);
        var flags = BindingFlags.Public | BindingFlags.Static;
        Type list = typeof(IReadOnlyList<string>);
        foreach (var (name, parameters) in new[]
                 {
                     ("MakeBusiness", new[] { typeof(string), typeof(string), list }),
                     ("MakePersonal", new[] { typeof(string), typeof(string), list }),
                     ("ReportConnected", new[] { typeof(string) }),
                     ("RequestList", new[] { typeof(bool) }),
                     ("Hold", new[] { typeof(string) }),
                     ("IsSending", new[] { typeof(string) }),
                     ("Release", new[] { typeof(string) }),
                     ("ForgetBot", new[] { typeof(string) }),
                     ("ForgetAll", Type.EmptyTypes),
                 })
            Assert.IsNotNull(type.GetMethod(name, flags, null, parameters, null), $"{name}({string.Join(", ", parameters.Select(p => p.Name))})");
        Assert.AreEqual(typeof(bool), type.GetMethod("IsSending", flags).ReturnType);
        Assert.IsTrue(type.GetMethod("RequestList", flags).GetParameters()[0].IsOptional, "RequestList(bool force = false)");
        var failed = type.GetEvent("OnWriteFailed", flags);
        Assert.IsNotNull(failed);
        Assert.AreEqual(typeof(Action<string, IReadOnlyList<string>>), failed.EventHandlerType, "(botName, chatIds)");
        Assert.AreEqual("CrAudience.Queue", ChatAudienceSyncService.StoreKey);
        Assert.AreEqual(2f, ChatAudienceSyncService.ListAfterDeliverySeconds, "the list is read again 2 s after a delivery");
        Assert.AreEqual(60_000, ChatAudienceSyncService.ListFocusGapMs, "a return to the app reads it at most once a minute");
    }

    [Test]
    public void Service_SendsOnlyThroughAuthorizedRequest()
    {
        string svc = Service;
        Assert.IsFalse(Regex.IsMatch(svc, @"\.SendWebRequest\(\)"), "never a direct send");
        Assert.IsFalse(svc.Contains("/webhook/"), "the path lives in the pure seam");
        var sends = Regex.Matches(svc,
            @"AuthorizedRequest\.Send\(\s*\(\)\s*=>\s*AuthorizedRequest\.JsonPost\(\$""\{Manager\.n8nBaseUrl\}\{ChatAudienceRequest\.Path\}"",\s*ChatAudienceRequest\.(?<op>ListBody|SetBody|ConnectedBody)\(");
        CollectionAssert.AreEquivalent(new[] { "ListBody", "SetBody", "ConnectedBody" }, sends.Cast<Match>().Select(m => m.Groups["op"].Value).ToArray(),
            "each op is built by the factory AuthorizedRequest.Send stamps (A1)");
        Assert.AreEqual(3, Regex.Matches(svc, @"AuthorizedRequest\.Send\(").Count, "and nothing else is sent");

        foreach (var (method, classify) in new[]
                 {
                     ("private IEnumerator FetchList()", "ChatAudienceResponse.ClassifyList(www.responseCode"),
                     ("private IEnumerator SendSet(", "ChatAudienceResponse.ClassifySet(www.responseCode"),
                     ("private IEnumerator SendConnected(", "ChatAudienceResponse.ClassifyConnected(www.responseCode"),
                 })
            Assert.IsTrue(MethodBody(svc, method).Contains(classify), $"{method}: every answer is read by the pure seam");
        Assert.IsTrue(MethodBody(svc, "private IEnumerator SendSet(").Contains("ChatAudienceSyncQueue.ApplySetResult(_model, profileId, sent,"),
            "a set answer settles only the versions it was sent for");
        Assert.IsTrue(MethodBody(svc, "private IEnumerator SendConnected(").Contains("ChatAudienceSyncQueue.ApplyConnectedResult(_model, entry, sentReport,"),
            "a connected answer settles only the report it was sent for");
    }

    [Test]
    public void Service_BootsItself_AndWaitsAtTheSendGate()
    {
        var boot = typeof(ChatAudienceSyncService).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(m => m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null);
        Assert.IsNotNull(boot, "the queue must resume on every launch (RuntimeInitializeOnLoadMethod)");
        Assert.AreEqual(RuntimeInitializeLoadType.AfterSceneLoad, boot.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>().loadType);

        string svc = Service;
        Assert.IsTrue(MethodBody(svc, "private static void " + boot.Name).Contains("Instance"), "the boot creates the service");
        string awake = MethodBody(svc, "private void Awake()");
        int load = At(awake, "ChatAudienceSyncQueue.Deserialize(PlayerPrefs.GetString(StoreKey, \"\"), NowMs())");
        Assert.Greater(At(awake, "DrawUndelivered();"), load, "the moves still on their way are drawn where the owner put them");
        Assert.IsTrue(MethodBody(svc, "private void DrawUndelivered()").Contains("ChatAudienceStore.ApplyDesire("));

        string loop = MethodBody(svc, "private IEnumerator Loop()");
        int gate = At(loop, "ReplyModeSendGate.CanSend(sendGateOpen,");
        int connected = At(loop, "yield return SendConnected(");
        int set = At(loop, "yield return SendSet(");
        Assert.IsTrue(gate < connected && connected < set, "no send before billing + the session; connection reports go first");
        string listLoop = MethodBody(svc, "private IEnumerator ListLoop()");
        Assert.Less(At(listLoop, "ReplyModeSendGate.CanSend(sendGateOpen,"), At(listLoop, "yield return FetchList()"), "no read before the session either");

        string opener = MethodBody(Src("Scripts/Main/Manager.ReplyModeSync.cs"), "private IEnumerator EnsureSessionThenOpenReplyModeSendGate");
        Assert.Less(At(opener, "yield return AuthClient.Instance.EnsureSession()"), At(opener, "ChatAudienceSyncService.OpenSendGate()"),
            "the gate opens with the reply-mode one, after the session");
    }

    [Test]
    public void Service_ReadsTheList_OnBoot_BotChange_Return_AndAfterADelivery()
    {
        string svc = Service;
        Assert.IsTrue(MethodBody(svc, "private void Awake()").Contains("ReadList(force: true);"), "at launch (it waits at the gate)");
        Assert.IsTrue(MethodBody(svc, "private void Start()").Contains("OnActiveBotChanged += HandleActiveBotChanged"));
        Assert.IsTrue(MethodBody(svc, "private void OnDestroy()").Contains("OnActiveBotChanged -= HandleActiveBotChanged"));
        Assert.IsTrue(Regex.IsMatch(svc, @"private void HandleActiveBotChanged\(string botId\)\s*=>\s*ReadList\(force: true\);"), "on a bot switch");

        string focus = MethodBody(svc, "private void OnApplicationFocus(bool hasFocus)");
        int policy = At(focus, "ChatAudienceSyncQueue.ShouldRearmOnFocus(hasFocus, now, _lastFocusRearmMs)");
        int stamp = At(focus, "_lastFocusRearmMs = now;");
        int rearm = At(focus, "ChatAudienceSyncQueue.Rearm(_model, now)");
        int read = At(focus, "ReadList(force: false);");
        Assert.IsTrue(policy < stamp && stamp < rearm && rearm < read, "back in the app, at most once a minute: re-arm, then read");
        Assert.IsTrue(MethodBody(svc, "private void ReadList(bool force)").Contains("ListFocusGapMs"), "an unforced read waits out the minute");

        foreach (var method in new[] { "private IEnumerator SendSet(", "private IEnumerator SendConnected(" })
            Assert.IsTrue(MethodBody(svc, method).Contains("ListAfterDelivery();"), $"{method}: a delivered write is read back");
        string after = MethodBody(svc, "private IEnumerator ListAfterDeliveryRoutine()");
        Assert.Less(At(after, "yield return new WaitForSecondsRealtime(ListAfterDeliverySeconds);"), At(after, "ReadList(force: true);"));
    }

    [Test]
    public void Service_AStaleListNeverOverwritesAFresherWrite()
    {
        // A list read the server answered before a write landed would put the chat back where it was (a flicker until the
        // read after the write): a list answer is applied only when no write was delivered — and no bot forgotten — while it
        // was on its way. The read 2 s after that write is the fresher one.
        string fetch = MethodBody(Service, "private IEnumerator FetchList()");
        int capture = At(fetch, "int deliveries = _deliveries, forgets = _forgets;");
        int request = At(fetch, "AuthorizedRequest.Send(");
        int check = At(fetch, "if (deliveries != _deliveries || forgets != _forgets) continue;");
        int apply = At(fetch, "ChatAudienceStore.ApplyList(");
        Assert.IsTrue(capture < request && request < check && check < apply);
        Assert.IsTrue(MethodBody(Service, "private IEnumerator SendSet(").Contains("_deliveries++;"));
        Assert.IsTrue(MethodBody(Service, "private IEnumerator SendConnected(").Contains("_deliveries++;"));
    }

    [Test]
    public void Service_HoldsAndDrains_LikeTheOtherQueues()
    {
        string svc = Service;
        foreach (var method in new[] { "private IEnumerator SendSet(", "private IEnumerator SendConnected(" })
        {
            string send = MethodBody(svc, method);
            int recheck = At(send, "_held.Contains(");
            int marked = At(send, "_sendingBot = ");
            int request = At(send, "AuthorizedRequest.Send(");
            Assert.IsTrue(recheck < marked && marked < request, $"{method}: re-checked after the session wait, marked right before the request");
            Assert.IsFalse(send.Substring(recheck, marked - recheck).Contains("yield return"),
                $"{method}: nothing suspends between the re-check and the mark — a hold either stops the send or finds it on the wire");
            Assert.IsTrue(Regex.IsMatch(send, @"finally\s*\{\s*_sendingBot = null;\s*\}"), $"{method}: the mark is cleared however the request ends");
        }
        string loop = MethodBody(svc, "private IEnumerator Loop()");
        Assert.IsTrue(loop.Contains("ChatAudienceSyncQueue.NextDueConnection(_model, now, _held)")
                      && loop.Contains("ChatAudienceSyncQueue.NextDueBatch(_model, now, _held)")
                      && loop.Contains("ChatAudienceSyncQueue.AnyPending(_model, _held)"),
            "a held bot's entries are never picked up and do not keep the loop spinning");
        Assert.IsTrue(Regex.IsMatch(svc, @"public static void Release\(string botName\)[\s\S]*?Kick\(\)"), "a release sends the kept bot's entries");
    }

    [Test]
    public void Service_DropsAReport_ForAProfileThatIsNoLongerOurs()
    {
        // A wizard Create that failed deletes the profile it just paired, a re-pairing replaces it: such a report could never
        // be answered and would be re-armed on every launch. Decided at send time, after the session wait — the
        // ScheduleSync rule (ids resolved when they travel, a retired one never sent).
        string send = MethodBody(Service, "private IEnumerator SendConnected(");
        int session = At(send, "yield return AuthClient.Instance.EnsureSession();");
        int check = At(send, "if (!auth.Owns(entry.profileId) || BotHolding(entry.profileId).Length == 0)");
        int drop = At(send, "ChatAudienceSyncQueue.DropConnection(_model, entry);");
        Assert.IsTrue(session < check && check < drop && drop < At(send, "AuthorizedRequest.Send("));
        Assert.IsTrue(MethodBody(Service, "private void Connected(string profileId)")
            .Contains("ChatAudienceSyncQueue.AddConnection(_model, BotHolding(profileId), profileId, NowMs())"),
            "a report names the bot that holds the profile — its hold and its forget scope the report");
    }

    [Test]
    public void Service_ARefusedMove_GoesBackOnScreen_ThenTellsTheToast()
    {
        string settle = MethodBody(Service, "private void Settle(");
        int adopt = At(settle, "ChatAudienceStore.ApplyDesireResult(profileId,");
        int back = At(settle, "ChatAudienceStore.RetractDesire(profileId,");
        int told = At(settle, "OnWriteFailed?.Invoke(botId,");
        Assert.IsTrue(adopt < told && back < told, "the chats are back in place (OnChanged) before the event the toast listens to");
        Assert.IsTrue(settle.Contains("outcome.NotAccepted()"), "a refusal and a chat the server refused are the moves it did not accept");
        Assert.IsTrue(settle.Contains("ChatAudienceStore.Batch("), "many chats settled at once repaint once");
    }
}

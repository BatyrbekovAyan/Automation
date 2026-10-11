using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// F1 + F2 (plan 2026-10-11 §1.7, task T8): the client half of «один привязанный клон на канал бота». Source guards —
/// EditMode cannot run the network — over the Create calls and the settings flows that lead to them:
/// <list type="bullet">
/// <item>both Create paths send <c>CreateOrigin</c> (the wizard never names a predecessor, settings name the channel's
/// retired profile as <c>PreviousProfileId</c>), wait 45 s, and read the answer through <c>CreateWorkflowResponse</c>
/// (an id is the only success; <c>channel_limit</c> means the server deleted the profile; everything else is a retry);</item>
/// <item>a refused Create from settings keeps the paired profile and records <c>CreatePending</c> — the next settings
/// open or channel switch-on repeats it (Create is idempotent on the server since F1) — and only the server's
/// <c>adoptedProfiles</c> clears the channel's retired set;</item>
/// <item>«Сменить номер» only logs the profile out: the clone stays, and the Create after the new pairing reuses it
/// (deleting it fire-and-forget could land AFTER that reuse and leave the bot silent — F1 §2.10).</item>
/// </list>
/// T9 (plan §5): the screens around it — after a purchase the session is refreshed, the boot claim loop runs again (it
/// finds channels the sweep retired after launch) and only then the «Подключите ботов снова» sheet is offered; the
/// server's wait for the payment stands between the client gate and <c>profile.add</c>; the card, the chats empty state
/// and the settings screen name a retired channel; the paywall stays the only gate subscriber.
/// </summary>
public class ReconnectWiringTests
{
    private static string Src(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, rel));

    private static string MethodBody(string text, string signature)
    {
        int i = text.IndexOf(signature, System.StringComparison.Ordinal);
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

    private static readonly (string method, string channel, string ch)[] FromEdit =
    {
        ("private IEnumerator CreateWhatsappWorkflowFromEdit(", "Whatsapp", "ChatChannel.WhatsApp"),
        ("private IEnumerator CreateTelegramWorkflowFromEdit(", "Telegram", "ChatChannel.Telegram"),
    };

    private static readonly (string method, string channel, string ch)[] FromStart =
    {
        ("private IEnumerator CreateWhatsappWorkflowFromStart(", "Whatsapp", "ChatChannel.WhatsApp"),
        ("private IEnumerator CreateTelegramWorkflowFromStart(", "Telegram", "ChatChannel.Telegram"),
    };

    [Test]
    public void CreateFromSettings_NamesTheRetiredProfile_AndItsOrigin()
    {
        string manager = Src("Scripts/Main/Manager.cs");
        foreach (var (method, _, ch) in FromEdit)
        {
            string body = MethodBody(manager, method);
            StringAssert.Contains("form.AddField(CreateWorkflowResponse.FieldCreateOrigin, CreateWorkflowResponse.OriginEdit)", body,
                $"{method}: settings tell the server this is an existing bot (it may adopt the channel's predecessor)");
            StringAssert.Contains("form.AddField(CreateWorkflowResponse.FieldPreviousProfileId, ProfileRetirement.PreviousProfileId(", body,
                $"{method}: the channel's retired profile is named — the server proves it in its own database");
            StringAssert.Contains(ch, body.Substring(body.IndexOf("ProfileRetirement.PreviousProfileId(", System.StringComparison.Ordinal)),
                $"{method}: the predecessor of THIS channel");
        }
    }

    [Test]
    public void CreateFromTheWizard_NeverNamesAPredecessor()
    {
        string manager = Src("Scripts/Main/Manager.cs");
        foreach (var (method, _, _) in FromStart)
        {
            string body = MethodBody(manager, method);
            StringAssert.Contains("form.AddField(CreateWorkflowResponse.FieldCreateOrigin, CreateWorkflowResponse.OriginStart)", body,
                $"{method}: a new bot from the wizard never inherits anything by its slot name");
            StringAssert.DoesNotContain("FieldPreviousProfileId", body, $"{method}: a new bot has no predecessor");
        }
        string response = Src("Scripts/Main/CreateWorkflowResponse.cs");
        StringAssert.Contains("public const string OriginStart = \"start\";", response, "the contract's wizard origin (plan §1.7)");
        StringAssert.Contains("public const string OriginEdit = \"edit\";", response, "the contract's settings origin (plan §1.7)");
        StringAssert.Contains("public const string FieldCreateOrigin = \"CreateOrigin\";", response);
        StringAssert.Contains("public const string FieldPreviousProfileId = \"PreviousProfileId\";", response);
    }

    [Test]
    public void EveryCreate_Waits45Seconds_AndReadsTheAnswerThroughTheContract()
    {
        StringAssert.Contains("public const int TimeoutSeconds = 45;", Src("Scripts/Main/CreateWorkflowResponse.cs"),
            "plan §1.7: 45 s for both paths — a timeout after the server finished is harmless, the retry reuses the clone");
        string manager = Src("Scripts/Main/Manager.cs");
        foreach (var (method, _, _) in new[] { FromEdit[0], FromEdit[1], FromStart[0], FromStart[1] })
        {
            string body = MethodBody(manager, method);
            StringAssert.Contains("r.timeout = CreateWorkflowResponse.TimeoutSeconds", body, $"{method}: the Create timeout");
            StringAssert.DoesNotContain("r.timeout = 30", body, $"{method}: the old 30 s timeout");
            StringAssert.Contains("CreateWorkflowResponse.Classify(", body, $"{method}: the answer is classified by the contract");
            Assert.IsFalse(Regex.IsMatch(body, @"Contains\(""\\""(id|error)\\"""), $"{method}: no substring sniffing of the body any more");
        }
    }

    [Test]
    public void CreateFromSettings_ClearsTheRetiredSet_OnlyForAnAdoptedProfile()
    {
        string manager = Src("Scripts/Main/Manager.cs");
        foreach (var (method, _, ch) in FromEdit)
        {
            string body = MethodBody(manager, method);
            StringAssert.Contains($"ProfileRetirement.AfterCreate(bot, {ch}, result.AdoptedProfiles)", body,
                $"{method}: the retired keys go only when the server named that profile among the adopted ones");
            StringAssert.DoesNotContain("ProfileRetirement.Clear(", body, $"{method}: never an unconditional wipe of the retired set");
            StringAssert.Contains($"ClearCreatePending(bot, {ch})", body, $"{method}: a Create that went through is no longer pending");
        }
    }

    [Test]
    public void ARefusedCreateFromSettings_KeepsThePairedProfile_UnlessTheServerDeletedIt()
    {
        string manager = Src("Scripts/Main/Manager.cs");
        foreach (var (method, _, ch) in FromEdit)
        {
            string body = MethodBody(manager, method);
            int gone = body.IndexOf("case CreateWorkflowResponse.Kind.ProfileGone:", System.StringComparison.Ordinal);
            int retry = body.IndexOf("case CreateWorkflowResponse.Kind.Retryable:", System.StringComparison.Ordinal);
            Assert.IsTrue(gone >= 0 && retry >= 0, $"{method}: both refusals must be handled by name");
            StringAssert.Contains($"ApplyCreateProfileGone(bot, {ch})", body.Substring(gone), $"{method}: channel_limit — the server deleted the profile");
            StringAssert.Contains($"MarkCreatePending(bot, {ch})", body.Substring(retry), $"{method}: any other refusal keeps the profile and records the retry");
        }
        string pending = MethodBody(manager, "private static void MarkCreatePending(");
        StringAssert.Contains("CreateWorkflowResponse.PendingSuffix(channel)", pending);
        StringAssert.Contains("PendingProfileLedger.MarkWhatsappClaimed()", pending,
            "the profile now belongs to the bot — a pending ledger entry would delete it at quit");
        StringAssert.Contains("PendingProfileLedger.MarkTelegramClaimed()", pending);
        StringAssert.Contains("PlayerPrefs.Save()", pending);
        string goneBody = MethodBody(manager, "private void ApplyCreateProfileGone(");
        StringAssert.Contains("Bot.UnauthedProfileSentinel", goneBody, "the deleted profile's id goes");
        StringAssert.Contains("isOn", goneBody, "the channel is off");
        StringAssert.DoesNotContain("Retired", goneBody, "the channel's retired set stays — the next attempt names the same predecessor");
    }

    [Test]
    public void ChangeNumber_LogsOutOnly_TheCloneStaysForTheReuse()
    {
        string auth = Src("Scripts/Main/BotSettings.Auth.cs");
        foreach (var (confirm, unauthorize, op) in new[]
                 {
                     ("public void ConfirmChangeWhatsappNumber(", "private IEnumerator UnauthorizeWhatsapp(", "WappiProxy.Op.ProfileLogout"),
                     ("public void ConfirmChangeTelegramNumber(", "private IEnumerator UnauthorizeTelegram(", "WappiProxy.Op.ProfileLogout"),
                 })
        {
            string flow = MethodBody(auth, confirm) + MethodBody(auth, unauthorize);
            StringAssert.Contains(op, flow, $"{unauthorize}: the number change is a logout of the same profile");
            foreach (string delete in new[] { "GetDeleteWhatsappWorkflow", "GetDeleteTelegramWorkflow", "WorkflowStateRequest.Delete", "WorkflowId = \"-1\"" })
                StringAssert.DoesNotContain(delete, flow, $"{confirm}: the clone must stay — the Create after the new pairing reuses it ({delete})");
        }
        string manager = Src("Scripts/Main/Manager.cs");
        Assert.IsFalse(manager.Contains("public void GetDeleteWhatsappWorkflow(") || manager.Contains("public void GetDeleteTelegramWorkflow("),
            "the fire-and-forget clone delete of the settings screen is gone — nothing may race the Create that reuses the clone");
    }

    [Test]
    public void AnAuthorisedProfileWithAFailedCreate_RetriesTheCreate()
    {
        string auth = Src("Scripts/Main/BotSettings.Auth.cs");
        foreach (var (check, probe, ch, create) in new[]
                 {
                     ("private IEnumerator CheckWhatsappAuthorization(", "private IEnumerator CheckWhatsappUnauthorizationOutsideApp(",
                      "ChatChannel.WhatsApp", "Manager.Instance.GetCreateWhatsappWorkflow()"),
                     ("private IEnumerator CheckTelegramAuthorization(", "private IEnumerator CheckTelegramUnauthorizationOutsideApp(",
                      "ChatChannel.Telegram", "Manager.Instance.GetCreateTelegramWorkflow()"),
                 })
        {
            string on = MethodBody(auth, check);
            int decide = on.IndexOf("CreateWorkflowResponse.RetryOnChannelOn(", System.StringComparison.Ordinal);
            int hide = on.LastIndexOf("Manager.Instance.LoadingPanel.SetActive(false)", System.StringComparison.Ordinal);
            int retry = on.LastIndexOf(create, System.StringComparison.Ordinal);
            Assert.IsTrue(decide >= 0, $"{check}: an authorised profile with a failed Create (or no clone) must repeat the Create");
            Assert.IsTrue(hide > decide && retry > hide,
                $"{check}: the retry starts AFTER this check drops its loading cover — the Create raises its own");
            StringAssert.Contains($"CreatePending(bot, {ch})", on, $"{check}: the decision reads the channel's pending flag");

            string open = MethodBody(auth, probe);
            int decideOpen = open.IndexOf("CreateWorkflowResponse.RetryOnSettingsOpen(", System.StringComparison.Ordinal);
            Assert.IsTrue(decideOpen >= 0 && open.IndexOf(create, decideOpen, System.StringComparison.Ordinal) > decideOpen,
                $"{probe}: opening the settings repeats a Create that failed before");
        }
        string manager = Src("Scripts/Main/Manager.cs");
        foreach (string entry in new[] { "public void GetCreateWhatsappWorkflow(", "public void GetCreateTelegramWorkflow(" })
            StringAssert.Contains("IsCreateInFlight(", MethodBody(manager, entry), $"{entry}: one Create per channel at a time");
    }

    // ---------------- T9: screens, purchase, texts ----------------

    private static int At(string body, string needle, string what)
    {
        int i = body.IndexOf(needle, System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(i, 0, $"{what}: «{needle}» not found");
        return i;
    }

    [Test]
    public void APurchase_RefreshesTheSession_ReClaims_ThenOffersTheSheet()
    {
        string host = Src("Scripts/Main/ReconnectAfterPurchase.cs");
        StringAssert.Contains("BillingService.OnEntitlementChanged += ", host,
            "the sheet follows the entitlement change itself — not the paywall request (PaywallController owns that one)");
        StringAssert.DoesNotContain("OnPaywallRequested", host, "a second gate subscriber would open the sheet AND the paywall");
        StringAssert.Contains("ReconnectSettlePolicy.IsPurchaseTransition(", host,
            "only a move from no paid plan to a paid one is a purchase — the boot resolution of an existing plan is not");

        string routine = MethodBody(host, "private static IEnumerator AfterPurchase(");
        int refresh = At(routine, ".ForceRefresh()", "the token must list today's live rows before the claim loop reads it");
        int claim = At(routine, "ProfileClaimFlow.RunAtBoot(", "the boot claim loop runs again after the purchase");
        int list = At(routine, "ProfileRetirement.ReconnectCandidates(", "the candidates are the channels the server retired");
        int show = At(routine, "ReconnectBotsSheet.Show(", "the sheet is offered");
        Assert.IsTrue(refresh < claim && claim < list && list < show,
            "refresh → re-claim (marks channels the sweep retired after launch) → list → sheet, in that order");
        StringAssert.Contains("ProfileClaimFlow.IsRunning", routine, "a claim loop still running from boot is waited for, never skipped");
    }

    [Test]
    public void ThePaywall_StaysTheOnlyGateSubscriber()
    {
        string scripts = Path.Combine(Application.dataPath, "Scripts");
        foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "PaywallController.cs") continue;
            StringAssert.DoesNotContain("OnPaywallRequested +=", File.ReadAllText(file),
                $"{Path.GetFileName(file)}: PaywallController.HandlePaywallRequested is the ONLY subscriber (sheet-vs-paywall is decided there)");
        }
    }

    [Test]
    public void TheServerWait_StandsBetweenTheClientGate_AndProfileAdd()
    {
        string auth = Src("Scripts/Main/BotSettings.Auth.cs");
        foreach (var (check, add, ch) in new[]
                 {
                     ("private IEnumerator CheckWhatsappAuthorization(", "GetCreateWhatsappProfile(", "ChatChannel.WhatsApp"),
                     ("private IEnumerator CheckTelegramAuthorization(", "GetCreateTelegramProfile(", "ChatChannel.Telegram"),
                 })
        {
            string body = MethodBody(auth, check);
            int gate = At(body, "EntitlementGate.CanConnectChannel(", check);
            int wait = At(body, "WaitForServerChannelSlot(" + ch, $"{check}: the server must have seen the payment before a profile is created");
            int create = At(body, add, check);
            Assert.IsTrue(gate < wait && wait < create,
                $"{check}: client gate → server wait → profile.add (a pairing refused by Create would delete the paired profile)");
        }
        string reconnect = Src("Scripts/Main/BotSettings.Reconnect.cs");
        string waitBody = MethodBody(reconnect, "private IEnumerator WaitForServerChannelSlot(");
        StringAssert.Contains("ReconnectSettlePolicy.ShouldWaitBeforePairing(UsageStore.Current)", waitBody, "the decision is the pure policy's");
        StringAssert.Contains("ReconnectSettlePolicy.DelayBeforeRead(", waitBody, "the re-reads follow the settle schedule");
        StringAssert.Contains("UsageClient.FetchRoutine()", waitBody, "each step re-reads GetUsage");
        StringAssert.Contains("RetiredChannelCopy.Settling", waitBody, "the owner sees «Подтверждаем оплату…» while it waits");
        StringAssert.Contains("RetiredChannelCopy.SettleTimedOut", waitBody, "and why nothing was paired when it gave up");
    }

    /// <summary>
    /// Plan §5.2 step 5: a Create from settings refused with <c>channel_limit</c> (the server counted no free channel and
    /// deleted the paired profile) opens the existing channel-limit sheet — the owner learns why the channel is off again
    /// instead of a bare «Не удалось сохранить». Every other refusal keeps the profile and repeats the Create: never a limit.
    /// </summary>
    [Test]
    public void ARefusedCreateFromSettings_ForChannelLimit_OpensTheChannelLimitSheet()
    {
        string manager = Src("Scripts/Main/Manager.cs");
        foreach (var (method, _, ch) in FromEdit)
        {
            string body = MethodBody(manager, method);
            int gone = At(body, "case CreateWorkflowResponse.Kind.ProfileGone:", method);
            int retry = At(body, "case CreateWorkflowResponse.Kind.Retryable:", method);
            Assert.Less(gone, retry, $"{method}: the channel_limit case is read before the retry case");
            string goneCase = body.Substring(gone, retry - gone);
            string retryCase = body.Substring(retry);

            int applied = At(goneCase, $"ApplyCreateProfileGone(bot, {ch})", method);
            int sheet = At(goneCase, "EntitlementGate.RequestPaywall(PaywallTrigger.ChannelLimit)",
                $"{method}: channel_limit opens the channel-limit sheet (PaywallController routes it to BillingGateSheet)");
            Assert.Less(applied, sheet, $"{method}: the sheet comes after the channel is reset, so the gate counts the freed slot");

            StringAssert.DoesNotContain("RequestPaywall", retryCase,
                $"{method}: a retryable refusal is not a limit — the paired profile stays and the Create repeats");
        }
    }

    [Test]
    public void TheCard_OffersConnect_OnlyWhenNoChannelIsLive()
    {
        string bot = Src("Scripts/Main/Bot.cs");
        StringAssert.Contains("BotCardModel.ShowConnectPill(", bot, "the capsule's state is the pure model's decision");
        string tap = MethodBody(bot, "private void OnAutoPillTapped(");
        int connect = At(tap, "showsConnect", "the capsule knows which face it wears");
        At(tap, "OpenSettingsAtGeneralTab()", "«Подключить» opens the bot's settings on the channel rows");
        Assert.Less(connect, tap.IndexOf("ReplyModeToggleBinder", System.StringComparison.Ordinal),
            "a «Подключить» tap never flips the reply mode of a bot with no live channel");
    }

    [Test]
    public void TheChatsEmptyState_NamesARetiredChannel()
    {
        string state = MethodBody(Src("Scripts/Main/ChatManager.BotState.cs"), "private EmptyStateReason NoConnectionEmptyState(");
        StringAssert.Contains("EmptyStateReason.BotChannelRetired", state, "«Бот отключён» instead of the plain «… не подключён»");
        StringAssert.Contains("RetiredChannelPolicy", state, "the retired verdict is the policy's");
        string view = Src("Scripts/UI/EmptyStateView.cs");
        int @case = At(view, "case EmptyStateReason.BotChannelRetired:", "EmptyStateView");
        StringAssert.Contains("RetiredChannelCopy.EmptyTitle", view.Substring(@case));
        StringAssert.Contains("OpenCurrentBotAuth", view.Substring(@case), "the CTA opens the bot's settings");
    }

    [Test]
    public void TheSettingsScreen_BuildsItsReconnectUi_AtRuntime()
    {
        string settings = Src("Scripts/Main/BotSettings.cs");
        StringAssert.Contains("RefreshReconnectUi();", MethodBody(settings, "public void OnEnable("),
            "the banner and the row hints are runtime-added on every open (the prefab stays untouched)");
        StringAssert.Contains("openBotSettings.RefreshReconnectUi();", MethodBody(Src("Scripts/Main/Manager.cs"), "public void SaveSettings("),
            "a save re-shows the number fields — a retired channel's must stay hidden behind its hint");
        string reconnect = Src("Scripts/Main/BotSettings.Reconnect.cs");
        StringAssert.Contains("RetiredChannelPolicy.SettingsRow(", reconnect, "each row's state is the policy's");
        StringAssert.Contains("RetiredChannelPolicy.ShowBanner(", reconnect, "so is the banner's");
        StringAssert.Contains("EntitlementGate.RequestPaywall(", reconnect, "«Выбрать тариф» opens the paywall through the gate");
    }
}

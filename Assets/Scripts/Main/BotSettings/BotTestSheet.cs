using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stage 7 «Проверка бота»: a runtime-built card on its own overlay canvas (ProfileClaimSheet's pattern,
/// sortingOrder 3, top-anchored so the keyboard raised by the input never covers the field or the buttons).
/// Shows the four readiness facts, lets the owner talk to the bot's REAL prompt through
/// <c>/webhook/TestDialog</c> (nothing is sent to a client, no quota is spent, the handoff the model would
/// create is reported as data), and lists the bot's last reply outcomes from the ledger with a named
/// reason each (<see cref="ReplyOutcomeCopy"/>).
/// R16: every call carries a <see cref="BotTestTicket"/> (sequence, bot, profile, workflow) captured at send — a late
/// answer never touches another screen's reply, history or busy latch, and «тест пройден» is written only under the
/// ticket's own bot (<see cref="BotTestResultPolicy"/>). The ID line names the tested version (R15 promptVersion).
/// </summary>
public static class BotTestSheet
{
    public const float CardWidth = 1000f, CardTop = 72f, CardHeight = 1400f, SideInset = 40f;
    public const float TitleTop = 36f, TitleHeight = 56f;
    public const float ReadinessTop = 108f, ReadinessHeight = 180f;
    public const float FieldTop = 300f, FieldHeight = 104f;
    public const float ButtonsTop = 424f, ButtonHeight = 92f, ButtonWidth = 440f, ButtonX = 250f;
    public const float NoteTop = 532f, NoteHeight = 44f;
    public const float ReplyTop = 588f, ReplyHeight = 400f;
    public const float MetaTop = 1000f, MetaHeight = 96f;
    public const float RecentTop = 1108f, RecentHeight = 224f;
    public const float CorrelationTop = 1344f, CorrelationHeight = 36f;
    public const string LastTestKeySuffix = "LastTestOk";
    public const int MaxRecentRows = 4;
    private const int OverlaySortingOrder = 3;

    private sealed class Host : MonoBehaviour { }

    private static GameObject panel;
    private static Host host;
    private static Image cardImage, fieldImage, primaryImage, secondaryImage;
    private static TextMeshProUGUI titleTmp, readinessTmp, noteTmp, replyTmp, metaTmp, recentTmp, correlationTmp, primaryLabel, secondaryLabel, placeholderTmp, fieldTextTmp;
    private static TMP_InputField field;
    private static Type cachedRoundedType;

    private static string botName;
    private static Bot bot;
    private static BotTestTarget target;
    private static BotReadiness readiness;
    private static List<TestDialogTurn> history = new();
    private static bool busy;
    private static bool eventsBound;

    // R16: the sheet is ONE static surface reused for every bot, and a test call outlives the screen that sent it
    // (the coroutine runs on the always-active host). Every Show and every send bumps the sequence; a result whose
    // ticket is not the current one belongs to a screen that is gone.
    private static int screenSeq;
    private static readonly Dictionary<string, int> recordedSeq = new();   // newest ticket whose verdict was written, per bot

    public static bool IsShowing => panel != null && panel.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        panel = null; host = null; busy = false; eventsBound = false;
        history = new List<TestDialogTurn>();
        screenSeq = 0;
        recordedSeq.Clear();
    }

    public static void Show(string name, Bot openBot, TMP_FontAsset titleFont, TMP_FontAsset bodyFont)
    {
        if (string.IsNullOrEmpty(name) || openBot == null) return;
        EnsureBuilt(titleFont, bodyFont);
        screenSeq++;                       // anything still in flight now belongs to a screen that is gone
        botName = name;
        bot = openBot;
        history = new List<TestDialogTurn>();
        busy = false;
        field.text = "";
        replyTmp.text = "";
        metaTmp.text = "";
        correlationTmp.text = "";
        target = BotTestTarget.Resolve(bot.whatsappProfileId, bot.whatsappWorkflowId, bot.telegramProfileId, bot.telegramWorkflowId);
        RefreshReadiness();
        RenderRecent();
        Paint();
        PopupUI.Show(panel);
        HandoffSyncService.Existing?.RefreshStatus();     // the ledger rows arrive through Changed → RenderRecent
    }

    public static void Hide()
    {
        if (panel != null && panel.activeSelf) PopupUI.Hide(panel);
    }

    // ---- readiness -------------------------------------------------------------------------

    private static void RefreshReadiness()
    {
        int items = 0, documents = 0;
        string description = "";
        try
        {
            var knowledge = N8nSuggestionsProvider.BuildKnowledge(botName);
            items = knowledge.items.Count;
            documents = knowledge.documents;
            description = knowledge.description ?? "";
        }
        catch (Exception) { }
        var sync = ReplyModeSyncService.Existing;
        bool pending = sync != null && sync.IsPending(botName);
        bool failed = sync != null && sync.HasFailed(botName);
        int stored = PlayerPrefs.GetInt(botName + LastTestKeySuffix, -1);
        bool? tested = stored < 0 ? null : stored == 1;
        readiness = BotReadiness.Compute(target != null ? target.ProfileId : null, pending, failed, items, documents, description, tested);
        RenderReadiness();
        SetSendEnabled(!busy && readiness.ChannelConnected);
    }

    private static void RenderReadiness()
    {
        string ok = ColorUtility.ToHtmlStringRGB(Theme.Color(ThemeRole.AccentText));
        string no = ColorUtility.ToHtmlStringRGB(Theme.Color(ThemeRole.InkTertiary));
        var sb = new StringBuilder();
        Append(sb, readiness.ChannelConnected, BotReadinessCopy.Line(readiness.ChannelConnected, BotReadinessCopy.Channel, BotReadinessCopy.ChannelNo), ok, no);
        Append(sb, readiness.ModeConfirmed, BotReadinessCopy.Line(readiness.ModeConfirmed, BotReadinessCopy.Mode, BotReadinessCopy.ModeNo), ok, no);
        Append(sb, readiness.KnowledgeAvailable, BotReadinessCopy.Line(readiness.KnowledgeAvailable, BotReadinessCopy.Knowledge, BotReadinessCopy.KnowledgeNo), ok, no);
        Append(sb, readiness.TestPassed == true, BotReadinessCopy.TestLine(readiness.TestPassed), ok, no);
        readinessTmp.text = sb.ToString().TrimEnd('\n');
    }

    private static void Append(StringBuilder sb, bool ok, string line, string okHex, string noHex)
        => sb.Append("<color=#").Append(ok ? okHex : noHex).Append('>').Append(line).Append("</color>\n");

    // ---- the test call ---------------------------------------------------------------------

    private static void OnSend()
    {
        if (busy || field == null) return;
        string text = (field.text ?? "").Trim();
        if (text.Length == 0) return;
        if (target == null) { replyTmp.text = BotReadinessCopy.ChannelNo; return; }
        busy = true;
        field.DeactivateInputField();
        SetSendEnabled(false);
        replyTmp.text = BotReadinessCopy.Sending;
        metaTmp.text = "";
        var request = new TestDialogRequest
        {
            profileId = target.ProfileId, workflowId = target.WorkflowId, channel = target.Channel,
            text = text, history = new List<TestDialogTurn>(history),
        };
        var ticket = new BotTestTicket(++screenSeq, botName, target.ProfileId, target.WorkflowId);
        var ticketBot = bot;
        Action<TestDialogResult> done = result => OnResult(ticket, ticketBot, text, result);
        if (TransportOverride != null) TransportOverride(request, done);
        else host.StartCoroutine(TestDialogClient.Send(request, done));
    }

    /// <summary>EditMode seam: when set, a test call is handed to it instead of the network, so a test can park the
    /// request and answer it later (a controlled response delay). Always null in the app.</summary>
    internal static Action<TestDialogRequest, Action<TestDialogResult>> TransportOverride;

    private static void OnResult(BotTestTicket ticket, Bot ticketBot, string clientText, TestDialogResult result)
    {
        bool alive = ticketBot != null;   // Unity-null once the bot was deleted
        var targetNow = alive ? BotTestTarget.Resolve(ticketBot.whatsappProfileId, ticketBot.whatsappWorkflowId, ticketBot.telegramProfileId, ticketBot.telegramWorkflowId) : null;
        recordedSeq.TryGetValue(ticket.BotName ?? "", out int lastSeq);
        var outcome = BotTestResultPolicy.Decide(ticket, screenSeq, alive, targetNow, lastSeq, result);
        RecordVerdict(ticket, outcome);
        if (!outcome.ApplyToScreen)
        {
            // A stale answer: the screen, its history and its busy latch belong to a newer request.
            if (outcome.Verdict.HasValue && IsShowing && ticket.BotName == botName) RefreshReadiness();
            return;
        }
        busy = false;
        if (panel == null) return;
        SetSendEnabled(readiness.ChannelConnected);
        if (result.IsOk)
        {
            var response = result.Response;
            replyTmp.text = response.reply;
            history = TestDialogClient.AppendTurn(history, clientText, response.reply);
            field.text = "";
            metaTmp.text = SourcesLine(response.sources) + "\n" + (response.wouldHandoff != null ? BotReadinessCopy.ActionHandoff : BotReadinessCopy.ActionReply);
        }
        else
        {
            replyTmp.text = BotReadinessCopy.ErrorFor(result.ErrorKind);
        }
        correlationTmp.text = CorrelationLine(result.Response);
        RefreshReadiness();
        HandoffSyncService.Existing?.RefreshStatus();
    }

    /// <summary>R16: «тест пройден/не пройден» is written ONLY under the ticket's own bot, only for a bot-side outcome,
    /// never for a deleted or re-targeted bot, and never over a newer test's verdict (<see cref="BotTestResultPolicy"/>).</summary>
    private static void RecordVerdict(BotTestTicket ticket, BotTestOutcome outcome)
    {
        if (!outcome.Verdict.HasValue) return;
        recordedSeq[ticket.BotName] = ticket.Seq;
        PlayerPrefs.SetInt(ticket.BotName + LastTestKeySuffix, outcome.Verdict.Value);
        PlayerPrefs.Save();
    }

    private static string SourcesLine(TestDialogSources sources)
    {
        if (sources == null) return BotReadinessCopy.Sources(false, false, 0);
        return BotReadinessCopy.Sources(sources.catalog, sources.contacts, sources.documents != null ? sources.documents.Count : 0);
    }

    private static string CorrelationLine(TestDialogResponse response)
    {
        if (response == null) return "";
        var version = response.promptVersion;
        string note = version != null ? BotReadinessCopy.VersionNote(version.source, version.versionId, version.draftPending) : "";
        return BotReadinessCopy.CorrelationLine(response.correlationId, note);
    }

    // ---- last outcomes ---------------------------------------------------------------------

    private static void RenderRecent()
    {
        if (recentTmp == null) return;
        var sb = new StringBuilder();
        sb.Append(BotReadinessCopy.RecentTitle).Append('\n');
        int shown = 0;
        var service = HandoffSyncService.Existing;
        long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string alert = ColorUtility.ToHtmlStringRGB(Theme.Color(ThemeRole.Destructive));
        if (service != null && bot != null)
        {
            foreach (var row in service.Recent)
            {
                if (row == null || !(row.profileId == bot.whatsappProfileId || row.profileId == bot.telegramProfileId)) continue;
                string line = DashboardTimeFormat.Relative(row.at / 1000L, nowSec) + " · …" + (row.chatTail ?? "") + " · " + ReplyOutcomeCopy.Label(row.state);
                if (ReplyOutcomeCopy.NeedsAttention(row.state)) line = "<color=#" + alert + ">" + line + "</color>";
                sb.Append(line).Append('\n');
                if (++shown >= MaxRecentRows) break;
            }
        }
        if (shown == 0) sb.Append(BotReadinessCopy.RecentNone);
        recentTmp.text = sb.ToString().TrimEnd('\n');
    }

    private static void SetSendEnabled(bool enabled)
    {
        var button = primaryImage != null ? primaryImage.GetComponent<Button>() : null;
        if (button != null) button.interactable = enabled;
        if (primaryImage != null) primaryImage.color = enabled ? Theme.Color(ThemeRole.AccentFill) : Theme.Color(ThemeRole.Background);
        if (primaryLabel != null) primaryLabel.color = enabled ? Theme.Color(ThemeRole.AccentOnFill) : Theme.Color(ThemeRole.InkTertiary);
    }

    // ---- build ------------------------------------------------------------------------------

    private static void EnsureBuilt(TMP_FontAsset titleFont, TMP_FontAsset bodyFont)
    {
        if (panel != null) return;
        var canvasGo = new GameObject("BotTestSheetCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.layer = LayerMask.NameToLayer("UI");
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = OverlaySortingOrder;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0f;
        host = canvasGo.AddComponent<Host>();

        panel = NewChild(canvasGo.transform, "Panel", typeof(Image));
        StretchFull((RectTransform)panel.transform);
        var backdrop = panel.GetComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, PopupUI.DefaultBackdropAlpha);
        backdrop.raycastTarget = true;

        BuildCard(titleFont, bodyFont);
        panel.SetActive(false);
        if (!eventsBound)
        {
            HandoffSyncService.Changed += OnHandoffsChanged;
            Theme.Changed += OnThemeChanged;
            eventsBound = true;
        }
    }

    private static void OnHandoffsChanged() { if (IsShowing) RenderRecent(); }
    private static void OnThemeChanged() { if (panel != null) { Paint(); RenderReadiness(); RenderRecent(); } }

    private static void BuildCard(TMP_FontAsset titleFont, TMP_FontAsset bodyFont)
    {
        var card = NewChild(panel.transform, "Content", typeof(Image));
        var rt = (RectTransform)card.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -CardTop);
        rt.sizeDelta = new Vector2(CardWidth, CardHeight);
        cardImage = card.GetComponent<Image>();
        cardImage.raycastTarget = true;
        AddRounded(card, 40f);
        PopupUI.AbsorbEvents(cardImage);

        titleTmp = BuildTmp(card.transform, "Title", BotReadinessCopy.Title, 40f, FontStyles.Bold, titleFont, TextAlignmentOptions.Center);
        SetTopStretch((RectTransform)titleTmp.transform, TitleTop, TitleHeight, SideInset);

        readinessTmp = BuildTmp(card.transform, "Readiness", "", 28f, FontStyles.Normal, bodyFont, TextAlignmentOptions.TopLeft);
        readinessTmp.richText = true;
        readinessTmp.lineSpacing = 8f;
        SetTopStretch((RectTransform)readinessTmp.transform, ReadinessTop, ReadinessHeight, SideInset);

        BuildField(card.transform, bodyFont);

        (secondaryImage, secondaryLabel) = BuildButton(card.transform, "CloseButton", BotReadinessCopy.Close, titleFont, -ButtonX, Hide);
        (primaryImage, primaryLabel) = BuildButton(card.transform, "SendButton", BotReadinessCopy.Send, titleFont, ButtonX, OnSend);

        noteTmp = BuildTmp(card.transform, "Note", BotReadinessCopy.SafeNote, 22f, FontStyles.Normal, bodyFont, TextAlignmentOptions.Center);
        noteTmp.textWrappingMode = TextWrappingModes.Normal;
        SetTopStretch((RectTransform)noteTmp.transform, NoteTop, NoteHeight, SideInset);

        replyTmp = BuildTmp(card.transform, "Reply", "", 30f, FontStyles.Normal, bodyFont, TextAlignmentOptions.TopLeft);
        replyTmp.textWrappingMode = TextWrappingModes.Normal;
        replyTmp.overflowMode = TextOverflowModes.Ellipsis;
        SetTopStretch((RectTransform)replyTmp.transform, ReplyTop, ReplyHeight, SideInset);

        metaTmp = BuildTmp(card.transform, "Meta", "", 26f, FontStyles.Normal, bodyFont, TextAlignmentOptions.TopLeft);
        metaTmp.textWrappingMode = TextWrappingModes.Normal;
        metaTmp.overflowMode = TextOverflowModes.Ellipsis;
        SetTopStretch((RectTransform)metaTmp.transform, MetaTop, MetaHeight, SideInset);

        recentTmp = BuildTmp(card.transform, "Recent", "", 26f, FontStyles.Normal, bodyFont, TextAlignmentOptions.TopLeft);
        recentTmp.richText = true;
        recentTmp.overflowMode = TextOverflowModes.Ellipsis;
        SetTopStretch((RectTransform)recentTmp.transform, RecentTop, RecentHeight, SideInset);

        correlationTmp = BuildTmp(card.transform, "Correlation", "", 22f, FontStyles.Normal, bodyFont, TextAlignmentOptions.Left);
        SetTopStretch((RectTransform)correlationTmp.transform, CorrelationTop, CorrelationHeight, SideInset);
    }

    private static void BuildField(Transform card, TMP_FontAsset font)
    {
        var go = NewChild(card, "MessageField", typeof(Image), typeof(TMP_InputField));
        SetTopStretch((RectTransform)go.transform, FieldTop, FieldHeight, SideInset);
        fieldImage = go.GetComponent<Image>();
        fieldImage.raycastTarget = true;
        AddRounded(go, 24f);

        var area = NewChild(go.transform, "Text Area", typeof(RectMask2D));
        var areaRt = (RectTransform)area.transform;
        StretchFull(areaRt);
        areaRt.offsetMin = new Vector2(28f, 12f);
        areaRt.offsetMax = new Vector2(-28f, -12f);

        placeholderTmp = BuildTmp(area.transform, "Placeholder", BotReadinessCopy.Placeholder, 30f, FontStyles.Normal, font, TextAlignmentOptions.Left);
        StretchFull((RectTransform)placeholderTmp.transform);
        fieldTextTmp = BuildTmp(area.transform, "Text", "", 30f, FontStyles.Normal, font, TextAlignmentOptions.Left);
        StretchFull((RectTransform)fieldTextTmp.transform);

        field = go.GetComponent<TMP_InputField>();
        field.textViewport = areaRt;
        field.textComponent = fieldTextTmp;
        field.placeholder = placeholderTmp;
        field.characterLimit = 2000;
        field.contentType = TMP_InputField.ContentType.Standard;
        field.inputType = TMP_InputField.InputType.Standard;
        field.keyboardType = TouchScreenKeyboardType.Default;
        field.lineType = TMP_InputField.LineType.MultiLineSubmit;   // house rule: every field MultiLineSubmit (keyboard-dip fix)
        field.targetGraphic = fieldImage;
        field.transition = Selectable.Transition.None;
        field.onSubmit.AddListener(_ => OnSend());
    }

    private static (Image, TextMeshProUGUI) BuildButton(Transform card, string name, string label, TMP_FontAsset font, float anchoredX, Action action)
    {
        var go = NewChild(card, name, typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(anchoredX, -ButtonsTop);
        rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
        AddRounded(go, 46f);
        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = go.GetComponent<Image>();
        PopupUI.WireFingerUp(button, action);
        var tmp = BuildTmp(go.transform, "Label", label, 32f, FontStyles.Bold, font, TextAlignmentOptions.Center);
        StretchFull((RectTransform)tmp.transform);
        return (go.GetComponent<Image>(), tmp);
    }

    private static void Paint()
    {
        cardImage.color = Theme.Color(ThemeRole.Surface);
        titleTmp.color = Theme.Color(ThemeRole.InkPrimary);
        readinessTmp.color = Theme.Color(ThemeRole.InkSecondary);
        fieldImage.color = Theme.Color(ThemeRole.Background);
        fieldTextTmp.color = Theme.Color(ThemeRole.InkPrimary);
        placeholderTmp.color = Theme.Color(ThemeRole.InkTertiary);
        noteTmp.color = Theme.Color(ThemeRole.InkTertiary);
        replyTmp.color = Theme.Color(ThemeRole.InkPrimary);
        metaTmp.color = Theme.Color(ThemeRole.InkSecondary);
        recentTmp.color = Theme.Color(ThemeRole.InkSecondary);
        correlationTmp.color = Theme.Color(ThemeRole.InkTertiary);
        secondaryImage.color = Theme.Color(ThemeRole.Background);
        secondaryLabel.color = Theme.Color(ThemeRole.InkSecondary);
        SetSendEnabled(!busy && readiness.ChannelConnected);
    }

    // ---- build helpers (ProfileClaimSheet's set) ----

    private static GameObject NewChild(Transform parent, string name, params Type[] components)
    {
        var go = new GameObject(name, components);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void SetTopStretch(RectTransform rt, float top, float height, float sideInset)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(-sideInset * 2f, height);
    }

    internal static TextMeshProUGUI BuildTmp(Transform parent, string name, string text, float fontSize, FontStyles style, TMP_FontAsset font, TextAlignmentOptions alignment)
    {
        var go = NewChild(parent, name, typeof(TextMeshProUGUI));
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.characterSpacing = -1f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>Nobi lives in its own UPM assembly — resolve by scanning, never Type.GetType (memory: roundedcorners assembly).</summary>
    internal static void AddRounded(GameObject go, float radius)
    {
        if (cachedRoundedType == null)
        {
            const string fullName = "Nobi.UiRoundedCorners.ImageWithRoundedCorners";
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                cachedRoundedType = asm.GetType(fullName);
                if (cachedRoundedType != null) break;
            }
        }
        if (cachedRoundedType == null) return;
        var comp = go.AddComponent(cachedRoundedType);
        cachedRoundedType.GetField("radius")?.SetValue(comp, radius);
        cachedRoundedType.GetMethod("Validate")?.Invoke(comp, null);
        cachedRoundedType.GetMethod("Refresh")?.Invoke(comp, null);
    }
}

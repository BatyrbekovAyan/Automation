using System;
using DG.Tweening;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// «Личные и рабочие чаты» (spec §5.5, §5.6, sketch 009 screens 5–6): the open chat's two bottom sheets.
///   • «Сделать чат рабочим?» — <see cref="AskMakeBusiness"/>, raised by the «Личный» pill of a personal chat. The text says
///     what the assistant will do once the chat is working (<see cref="MakeBusinessCopy"/>: answer the waiting message
///     itself, answer new ones, or only suggest), with a small note when a waiting message is answered or skipped by its
///     age. «Сделать рабочим» moves the chat (<see cref="ChatAudienceSyncService.MakeBusiness"/>); «Оставить личным», a tap
///     outside, a drag down and the system Back leave it where it is.
///   • «Сделать личным» — <see cref="AskMakePersonal"/>, raised by a tap on a working chat's name or avatar: the chat's
///     name, its number with «в контактах» / «новый» (<see cref="NumberLine"/>), the action card «Сделать личным» with what
///     it means (<see cref="ChatAudienceSyncService.MakePersonal"/> + «Чат перенесён в «Личные»»), «Отмена».
/// The move is drawn at once (the header swaps the pill and the chip, the panel comes or goes, the list re-filters under the
/// open chat) and the queue delivers it. Only one sheet is up at a time; <see cref="TryCloseTop"/> is Back.
///
/// Built LAZILY at runtime on their own overlay canvas (sortingOrder 3, CanvasScaler 1080×1920) like AutoLockSheet and
/// BillingGateSheet — the scene is untouched. The fonts come from the caller (there is no runtime path to AssetDatabase;
/// the header hands in its own labels' SF Pro Regular and Semibold), and so does the lock glyph of the action card; the
/// colours are stamped from the theme on every show (a transient overlay needs no live binding). The sheet is a fixed 1080
/// wide — the match-width canvas's own width — so every text column has a width the fit can measure. Each sheet is FITTED
/// to its copy after it is shown (ConfirmCardFitter: TMP measures only an active text): the title and the body grow by
/// their overflow and the sheet with them, upward, while the buttons stay on the bottom edge.
/// </summary>
public static class PersonalChatSheets
{
    // ---- metrics (1080×1920 canvas reference units) ----

    /// <summary>The match-width canvas is always this wide: the sheet spans the screen.</summary>
    public const float SheetWidth = 1080f;
    public const float SideInset = 72f;
    private const float TopRadius = 60f;              // the house bottom sheets' (BillingGateSheet, ProfileSubPages)
    private const float GrabberWidth = 108f, GrabberHeight = 12f, GrabberTop = 24f;
    private const float DragZoneHeight = 156f;        // the grabber strip and the title — never a button
    private const float BottomSafePad = 96f;          // the home bar
    private const float SecondaryHeight = 120f;       // the house touch floor
    private const float SecondarySize = 40f;
    private const float TitleSize = 44f;              // H3
    private const int OverlaySortingOrder = 3;        // above the screens, below SelectionOverlay (4)
    private const float SlideInSeconds = 0.25f, SlideOutSeconds = 0.2f, ScrimFadeSeconds = 0.2f;
    private const float Unbounded = 32767f;           // TMP's own «no limit» for a measurement
    private const string ZeroWidthSpace = "​";

    /// <summary>«Сделать чат рабочим?» — authored geometry, from the sheet's top edge down (the buttons from its bottom up).</summary>
    public static class BusinessLayout
    {
        public const float TitleTop = 76f, TitleHeight = 60f;
        public const float BodyTop = 156f, BodyHeight = 96f, BodySize = 38f;    // two lines at 38 — every variant has at least two
        public const float NoteGap = 16f, NoteSize = 32f, NoteFallbackHeight = 40f;
        public const float BodyToPrimary = 48f;
        public const float PrimaryBottom = 248f, PrimaryHeight = 132f, PrimarySize = 44f;
        public const float SheetHeight = BodyTop + BodyHeight + BodyToPrimary + PrimaryHeight + PrimaryBottom;   // 680
    }

    /// <summary>«Сделать личным» — authored geometry, from the sheet's top edge down («Отмена» from its bottom up).</summary>
    public static class PersonalLayout
    {
        public const float NameTop = 76f, NameHeight = 56f;
        public const float NumberTop = 136f, NumberHeight = 44f, NumberSize = 34f;
        public const float CardTop = 212f, CardPadding = 40f, CardRadius = 36f;
        public const float LockSize = 52f, LockGap = 28f;
        public const float ActionTitleHeight = 52f, ActionTitleSize = 40f;
        public const float ActionBodyGap = 8f, ActionBodyHeight = 88f, ActionBodySize = 34f;   // two lines at 34
        public const float CardHeight = CardPadding + ActionTitleHeight + ActionBodyGap + ActionBodyHeight + CardPadding;   // 228
        public const float CardToSecondary = 32f;
        public const float SheetHeight = CardTop + CardHeight + CardToSecondary + SecondaryHeight + BottomSafePad;   // 688
    }

    // ---- state ----

    private static Transform canvas;
    private static Shell business, personal;
    private static Target businessTarget, personalTarget;

    // «Сделать чат рабочим?»
    private static TextMeshProUGUI businessTitle, businessBody, businessNote, businessYes, businessNo;
    private static Image businessYesFill;

    // «Сделать личным»
    private static TextMeshProUGUI personalName, personalNumber, actionTitle, actionBody, personalCancel;
    private static Image actionCard, actionLock;

    /// <summary>
    /// Test seam: (kind, botName, profileId, chatId) of every move a sheet makes, raised right before the queue is called
    /// (in EditMode the queue has no instance and sends nothing).
    /// </summary>
    internal static event Action<AudienceKind, string, string, string> Committed;

    /// <summary>True while one of the two sheets is up and not already leaving (Back router).</summary>
    public static bool IsShowing => (business != null && business.IsOpen) || (personal != null && personal.IsOpen);

    internal static GameObject MakeBusinessPanelForTests => business?.Panel;
    internal static GameObject MakePersonalPanelForTests => personal?.Panel;

    /// <summary>Statics survive a domain-reload-free play-mode enter; the objects they point at do not.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => Forget();

    // ---- the two sheets ----

    /// <summary>
    /// «Сделать чат рабочим?» for <paramref name="chat"/> of the bot's <paramref name="profileId"/>. <paramref name="autoOn"/>
    /// — «Авто» would hold in the chat once it is working (its own mode, the schedule, the quota); whether the client waits
    /// and for how long is read off the chat's last message.
    /// </summary>
    public static void AskMakeBusiness(string botName, string profileId, ChatViewModel chat, bool autoOn, TMP_FontAsset regular, TMP_FontAsset semibold)
    {
        if (chat == null) return;
        CancelMakePersonal();
        EnsureMakeBusiness();
        businessTarget = new Target(botName, profileId, chat.ChatId);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        (string body, string note) = MakeBusinessCopy.For(autoOn, !chat.IsLastMessageMine, MakeBusinessCopy.AgeHours(chat.LastMessageTime, now));
        Stamp(semibold, businessTitle, businessYes);
        Stamp(regular, businessBody, businessNote, businessNo);
        businessTitle.text = PersonalChatsCopy.MakeBusinessTitle;
        businessBody.text = body;
        businessNote.text = note ?? string.Empty;
        PaintMakeBusiness();
        business.Show(FitMakeBusiness);
    }

    /// <summary>
    /// «Сделать личным» for <paramref name="chat"/>. <paramref name="numberLine"/> is the line under the name
    /// (<see cref="NumberLine"/>); <paramref name="lockGlyph"/> — the «Личный» pill's own lock, worn by the action card.
    /// </summary>
    public static void AskMakePersonal(string botName, string profileId, ChatViewModel chat, string numberLine, TMP_FontAsset regular,
        TMP_FontAsset semibold, Sprite lockGlyph = null)
    {
        if (chat == null) return;
        KeepPersonal();
        EnsureMakePersonal();
        personalTarget = new Target(botName, profileId, chat.ChatId);

        Stamp(semibold, personalName, actionTitle);
        Stamp(regular, personalNumber, actionBody, personalCancel);
        // The name as the header shows it — an emoji whose sprite has not arrived yet is left out, never shown as a tag.
        personalName.text = UnicodeEmojiConverter.ConvertRealEmojisToSprites(chat.Title ?? string.Empty, MissingEmojiMode.Hide);
        personalNumber.text = numberLine ?? string.Empty;
        personalNumber.gameObject.SetActive(!string.IsNullOrEmpty(personalNumber.text));
        actionLock.sprite = lockGlyph;
        actionLock.enabled = lockGlyph != null;
        actionBody.text = PersonalChatsCopy.MakePersonalBody(ChatAudienceRule.WipeDays);
        PaintMakePersonal();
        personal.Show(FitMakePersonal);
    }

    /// <summary>The system Back: the sheet that is up closes by its quiet action — «Оставить личным» / «Отмена».</summary>
    public static bool TryCloseTop()
    {
        if (personal != null && personal.IsOpen)
        {
            CancelMakePersonal();
            return true;
        }
        if (business != null && business.IsOpen)
        {
            KeepPersonal();
            return true;
        }
        return false;
    }

    /// <summary>
    /// The «Сделать личным» sheet's second line: the chat's number as the header shows a chat without a name (a WhatsApp
    /// person's id without its suffix) and « · в контактах» for a saved contact or « · новый» for a new person. An id that
    /// carries no phone number (Telegram, a WhatsApp @lid) and a number the name line already shows are left out.
    /// </summary>
    public static string NumberLine(ChatViewModel chat, AudienceView view)
    {
        if (chat == null) return string.Empty;
        string number = ChatIdFormat.PhoneNumber(chat.ChatId);
        // A chat without a name is titled with its number (the emoji converter prefixes every title with a zero-width space).
        if (number != null && number == chat.Title?.Replace(ZeroWidthSpace, string.Empty)) number = null;
        string mark = chat.Facts.Contact == true ? PersonalChatsCopy.ContactMark
            : view != null && view.Reason == AudienceReason.New ? PersonalChatsCopy.NewMark
            : null;
        if (string.IsNullOrEmpty(number)) return mark ?? string.Empty;
        return mark == null ? number : $"{number} · {mark}";
    }

    // ---- the buttons ----

    // «Сделать рабочим»: the chat becomes working — drawn at once, delivered by the queue.
    private static void OnMakeBusiness()
    {
        if (business == null || !business.IsOpen) return;
        Target target = businessTarget;
        businessTarget = default;
        business.Hide();
        if (!target.IsSet) return;
        Committed?.Invoke(AudienceKind.Business, target.BotName, target.ProfileId, target.ChatId);
        ChatAudienceSyncService.MakeBusiness(target.BotName, target.ProfileId, new[] { target.ChatId });
    }

    // «Сделать личным»: the chat goes to «Личные» — the pill shows, the panel goes, and the toast says so.
    private static void OnMakePersonal()
    {
        if (personal == null || !personal.IsOpen) return;
        Target target = personalTarget;
        personalTarget = default;
        personal.Hide();
        if (!target.IsSet) return;
        Committed?.Invoke(AudienceKind.Personal, target.BotName, target.ProfileId, target.ChatId);
        ChatAudienceSyncService.MakePersonal(target.BotName, target.ProfileId, new[] { target.ChatId });
        AudienceToast.Show(PersonalChatsCopy.MovedToPersonal);
    }

    // «Оставить личным», the scrim, a drag down, Back: the chat stays personal.
    private static void KeepPersonal()
    {
        businessTarget = default;
        business?.Hide();
    }

    // «Отмена», the scrim, a drag down, Back: the chat stays working.
    private static void CancelMakePersonal()
    {
        personalTarget = default;
        personal?.Hide();
    }

    // ---- fit: after the show, TMP measures only an active text ----

    private static void FitMakeBusiness()
    {
        ConfirmCardFitter.Fit(business.Sheet, businessTitle, businessBody, ref business.Baseline);
        SeatNote();
    }

    // The note hangs under the body, however far the body grew, and the sheet grows by it (upward — the buttons stay put).
    private static void SeatNote()
    {
        bool shown = !string.IsNullOrEmpty(businessNote.text);
        if (businessNote.gameObject.activeSelf != shown) businessNote.gameObject.SetActive(shown);
        if (!shown) return;

        RectTransform body = businessBody.rectTransform;
        RectTransform note = businessNote.rectTransform;
        float top = -body.anchoredPosition.y + body.sizeDelta.y + BusinessLayout.NoteGap;
        float measured = businessNote.GetPreferredValues(note.rect.width, Unbounded).y;
        float height = ConfirmCardLayout.IsMeasured(measured) ? Mathf.Ceil(measured) : BusinessLayout.NoteFallbackHeight;
        note.anchoredPosition = new Vector2(note.anchoredPosition.x, -top);
        note.sizeDelta = new Vector2(note.sizeDelta.x, height);
        Vector2 sheet = business.Sheet.sizeDelta;
        business.Sheet.sizeDelta = new Vector2(sheet.x, sheet.y + BusinessLayout.NoteGap + height);
    }

    // The action card encloses its text: it grows by exactly what the fit added to the sheet.
    private static void FitMakePersonal()
    {
        ConfirmCardFitter.Fit(personal.Sheet, actionTitle, actionBody, ref personal.Baseline);
        float growth = personal.Sheet.sizeDelta.y - PersonalLayout.SheetHeight;
        RectTransform card = actionCard.rectTransform;
        card.sizeDelta = new Vector2(card.sizeDelta.x, PersonalLayout.CardHeight + growth);
    }

    // ---- paint: a theme snapshot per show ----

    private static void PaintMakeBusiness()
    {
        PaintShell(business);
        businessTitle.color = Theme.Color(ThemeRole.InkPrimary);
        businessBody.color = Theme.Color(ThemeRole.InkSecondary);
        businessNote.color = Theme.Color(ThemeRole.InkTertiary);
        businessYesFill.color = Theme.Color(ThemeRole.AccentFill);
        businessYes.color = Theme.Color(ThemeRole.AccentOnFill);
        businessNo.color = Theme.Color(ThemeRole.AccentText);
    }

    private static void PaintMakePersonal()
    {
        PaintShell(personal);
        personalName.color = Theme.Color(ThemeRole.InkPrimary);
        personalNumber.color = Theme.Color(ThemeRole.InkSecondary);
        actionCard.color = Theme.Color(ThemeRole.Hairline);
        actionLock.color = Theme.Color(ThemeRole.InkPrimary);
        actionTitle.color = Theme.Color(ThemeRole.InkPrimary);
        actionBody.color = Theme.Color(ThemeRole.InkSecondary);
        personalCancel.color = Theme.Color(ThemeRole.AccentText);
    }

    // ThemeRole.Scrim is opaque black in both themes — the veil's alpha is authored per scrim, as on every modal here.
    private static void PaintShell(Shell shell)
    {
        Color veil = Theme.Color(ThemeRole.Scrim);
        shell.Scrim.color = new Color(veil.r, veil.g, veil.b, PopupUI.DefaultBackdropAlpha);
        shell.Surface.color = Theme.Color(ThemeRole.Surface);
        shell.Grabber.color = Theme.Color(ThemeRole.Border);
    }

    // A label wears the font the caller offers; null keeps what it wears (TMP's default until a real one arrives).
    private static void Stamp(TMP_FontAsset font, params TextMeshProUGUI[] labels)
    {
        if (font == null) return;
        foreach (TextMeshProUGUI label in labels)
            if (label.font != font) label.font = font;
    }

    // ---- build ----

    private static void EnsureMakeBusiness()
    {
        if (business != null && business.Panel != null) return;
        business = Shell.Build(Canvas(), "MakeBusinessSheet", BusinessLayout.SheetHeight, KeepPersonal);
        Transform sheet = business.Sheet;

        businessTitle = BuildTmp(sheet, "Title", TitleSize, TextAlignmentOptions.Left, wrap: true);
        SetTopStretch(businessTitle.rectTransform, BusinessLayout.TitleTop, BusinessLayout.TitleHeight, SideInset, SideInset);
        businessBody = BuildTmp(sheet, "Body", BusinessLayout.BodySize, TextAlignmentOptions.TopLeft, wrap: true);
        SetTopStretch(businessBody.rectTransform, BusinessLayout.BodyTop, BusinessLayout.BodyHeight, SideInset, SideInset);
        businessNote = BuildTmp(sheet, "Note", BusinessLayout.NoteSize, TextAlignmentOptions.TopLeft, wrap: true);
        SetTopStretch(businessNote.rectTransform, BusinessLayout.BodyTop + BusinessLayout.BodyHeight + BusinessLayout.NoteGap,
            BusinessLayout.NoteFallbackHeight, SideInset, SideInset);

        (businessYesFill, businessYes) = BuildPrimary(sheet, PersonalChatsCopy.MakeBusinessYes, OnMakeBusiness);
        businessNo = BuildSecondary(sheet, PersonalChatsCopy.MakeBusinessNo, KeepPersonal);

        business.Finish(businessTitle, businessBody, KeepPersonal);
    }

    private static void EnsureMakePersonal()
    {
        if (personal != null && personal.Panel != null) return;
        personal = Shell.Build(Canvas(), "MakePersonalSheet", PersonalLayout.SheetHeight, CancelMakePersonal);
        Transform sheet = personal.Sheet;

        personalName = BuildTmp(sheet, "Name", TitleSize, TextAlignmentOptions.Left, wrap: false);
        SetTopStretch(personalName.rectTransform, PersonalLayout.NameTop, PersonalLayout.NameHeight, SideInset, SideInset);
        personalNumber = BuildTmp(sheet, "Number", PersonalLayout.NumberSize, TextAlignmentOptions.Left, wrap: false);
        SetTopStretch(personalNumber.rectTransform, PersonalLayout.NumberTop, PersonalLayout.NumberHeight, SideInset, SideInset);

        BuildActionCard(sheet);
        personalCancel = BuildSecondary(sheet, PersonalChatsCopy.Cancel, CancelMakePersonal);

        personal.Finish(actionTitle, actionBody, CancelMakePersonal);
    }

    // The card is the button: the lock, «Сделать личным» and what it means, on a Hairline fill (sketch 009, screen 6).
    private static void BuildActionCard(Transform sheet)
    {
        GameObject card = NewChild(sheet, "ActionCard", typeof(RectTransform), typeof(Image), typeof(Button));
        SetTopStretch((RectTransform)card.transform, PersonalLayout.CardTop, PersonalLayout.CardHeight, SideInset, SideInset);
        actionCard = card.GetComponent<Image>();
        actionCard.raycastTarget = true;
        AddRounded(card, PersonalLayout.CardRadius);
        var button = card.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = actionCard;
        PopupUI.WireFingerUp(button, OnMakePersonal);

        actionLock = NewChild(card.transform, "Lock", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        actionLock.preserveAspect = true;
        actionLock.raycastTarget = false;
        RectTransform lockRect = actionLock.rectTransform;
        lockRect.anchorMin = lockRect.anchorMax = new Vector2(0f, 1f);
        lockRect.pivot = new Vector2(0f, 1f);
        lockRect.anchoredPosition = new Vector2(PersonalLayout.CardPadding, -PersonalLayout.CardPadding);
        lockRect.sizeDelta = Vector2.one * PersonalLayout.LockSize;

        float textLeft = PersonalLayout.CardPadding + PersonalLayout.LockSize + PersonalLayout.LockGap;
        actionTitle = BuildTmp(card.transform, "ActionTitle", PersonalLayout.ActionTitleSize, TextAlignmentOptions.Left, wrap: true);
        actionTitle.text = PersonalChatsCopy.MakePersonalAction;
        SetTopStretch(actionTitle.rectTransform, PersonalLayout.CardPadding, PersonalLayout.ActionTitleHeight, textLeft, PersonalLayout.CardPadding);
        actionBody = BuildTmp(card.transform, "ActionBody", PersonalLayout.ActionBodySize, TextAlignmentOptions.TopLeft, wrap: true);
        SetTopStretch(actionBody.rectTransform, PersonalLayout.CardPadding + PersonalLayout.ActionTitleHeight + PersonalLayout.ActionBodyGap,
            PersonalLayout.ActionBodyHeight, textLeft, PersonalLayout.CardPadding);
    }

    // A full-width pill in AccentFill: the sheet's one strong action, in the thumb zone.
    private static (Image, TextMeshProUGUI) BuildPrimary(Transform sheet, string text, Action action)
    {
        GameObject go = NewChild(sheet, "Primary", typeof(RectTransform), typeof(Image), typeof(Button));
        SetBottomStretch((RectTransform)go.transform, BusinessLayout.PrimaryBottom, BusinessLayout.PrimaryHeight, SideInset);
        var fill = go.GetComponent<Image>();
        fill.raycastTarget = true;
        AddRounded(go, BusinessLayout.PrimaryHeight / 2f);
        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = fill;
        PopupUI.WireFingerUp(button, action);

        TextMeshProUGUI label = BuildTmp(go.transform, "Label", BusinessLayout.PrimarySize, TextAlignmentOptions.Center, wrap: false);
        label.text = text;
        StretchFull(label.rectTransform);
        return (fill, label);
    }

    // The quiet action, a 120-unit row on the bottom edge: the label itself is the tap target (BillingGateSheet's «Позже»).
    private static TextMeshProUGUI BuildSecondary(Transform sheet, string text, Action action)
    {
        GameObject go = NewChild(sheet, "Secondary", typeof(RectTransform), typeof(Button));
        SetBottomStretch((RectTransform)go.transform, BottomSafePad, SecondaryHeight, SideInset);
        TextMeshProUGUI label = BuildTmp(go.transform, "Label", SecondarySize, TextAlignmentOptions.Center, wrap: false);
        label.text = text;
        label.raycastTarget = true;
        StretchFull(label.rectTransform);
        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = label;
        PopupUI.WireFingerUp(button, action);
        return label;
    }

    private static Transform Canvas()
    {
        if (canvas != null) return canvas;
        var go = new GameObject("PersonalChatSheetsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.layer = LayerMask.NameToLayer("UI");
        var overlay = go.GetComponent<Canvas>();
        overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        overlay.sortingOrder = OverlaySortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0f;   // match WIDTH, like the main canvas
        canvas = go.transform;
        return canvas;
    }

    // ---- test seams ----

    /// <summary>EditMode seam: destroys the overlay and forgets every static, like a fresh play-mode enter.</summary>
    internal static void ResetForTests()
    {
        if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas.gameObject);
        Forget();
    }

    private static void Forget()
    {
        business?.KillTweens();
        personal?.KillTweens();
        canvas = null;
        business = personal = null;
        businessTarget = personalTarget = default;
        businessTitle = businessBody = businessNote = businessYes = businessNo = null;
        businessYesFill = null;
        personalName = personalNumber = actionTitle = actionBody = personalCancel = null;
        actionCard = actionLock = null;
    }

    // ---- one sheet: the scrim, the surface with its grabber, the slide, the drag down ----

    private sealed class Shell
    {
        public GameObject Panel;
        public RectTransform Sheet;
        public Image Scrim, Surface, Grabber;
        public ConfirmCardFitter.Baseline Baseline;
        private CanvasGroup scrimGroup;
        private Tween slide, fade;
        private bool hiding;

        public bool IsOpen => Panel != null && Panel.activeSelf && !hiding;

        public static Shell Build(Transform canvas, string name, float height, Action dismiss)
        {
            var shell = new Shell { Panel = NewChild(canvas, name, typeof(RectTransform)) };
            StretchFull((RectTransform)shell.Panel.transform);

            GameObject scrim = NewChild(shell.Panel.transform, "Scrim", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            StretchFull((RectTransform)scrim.transform);
            shell.Scrim = scrim.GetComponent<Image>();
            shell.Scrim.raycastTarget = true;
            shell.scrimGroup = scrim.GetComponent<CanvasGroup>();
            PopupUI.WireFingerUp(scrim, dismiss);   // a tap outside the sheet

            GameObject sheet = NewChild(shell.Panel.transform, "SheetRoot", typeof(RectTransform), typeof(Image));
            shell.Sheet = (RectTransform)sheet.transform;
            shell.Sheet.anchorMin = shell.Sheet.anchorMax = new Vector2(0.5f, 0f);
            shell.Sheet.pivot = new Vector2(0.5f, 0f);
            shell.Sheet.sizeDelta = new Vector2(SheetWidth, height);
            shell.Surface = sheet.GetComponent<Image>();
            shell.Surface.raycastTarget = true;   // the sheet covers the scrim's dismiss
            // Top-only corners: Nobi's Vector4 is x=TL, y=TR, z=BR, w=BL, 1:1 with the visual radius.
            var corners = sheet.AddComponent<ImageWithIndependentRoundedCorners>();
            corners.r = new Vector4(TopRadius, TopRadius, 0f, 0f);
            corners.Validate();
            corners.Refresh();
            PopupUI.AbsorbEvents(shell.Surface);

            GameObject grabber = NewChild(sheet.transform, "Grabber", typeof(RectTransform), typeof(Image));
            var grabRect = (RectTransform)grabber.transform;
            grabRect.anchorMin = grabRect.anchorMax = new Vector2(0.5f, 1f);
            grabRect.pivot = new Vector2(0.5f, 1f);
            grabRect.anchoredPosition = new Vector2(0f, -GrabberTop);
            grabRect.sizeDelta = new Vector2(GrabberWidth, GrabberHeight);
            shell.Grabber = grabber.GetComponent<Image>();
            shell.Grabber.raycastTarget = false;
            AddRounded(grabber, GrabberHeight / 2f);
            return shell;
        }

        // After the content: the drag strip on top of the title (the LAST sibling wins the raycast), the authored geometry
        // captured for the fit, and the hidden state the first show travels from.
        public void Finish(TextMeshProUGUI title, TextMeshProUGUI body, Action dismiss)
        {
            GameObject zone = NewChild(Sheet, "DragZone", typeof(RectTransform), typeof(Image));
            SetTopStretch((RectTransform)zone.transform, 0f, DragZoneHeight, 0f, 0f);
            var image = zone.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            var drag = zone.AddComponent<SheetDragDismiss>();
            drag.Bind(Sheet, scrimGroup);
            drag.onDismiss.AddListener(() => dismiss());

            ConfirmCardFitter.Capture(Sheet, title, body, ref Baseline);
            Sheet.anchoredPosition = new Vector2(0f, -Sheet.sizeDelta.y);
            scrimGroup.alpha = 0f;
            Panel.SetActive(false);
        }

        // Up from wherever it is: hidden it sits a full height below the edge, a cut-short exit leaves it part-way down. The
        // fit runs once the panel is active and before the first frame, so nothing is ever seen at the wrong size.
        public void Show(Action fit)
        {
            bool wasHidden = !Panel.activeSelf;
            bool settledOpen = !wasHidden && !hiding && (slide == null || !slide.IsActive());
            KillTweens();
            Panel.SetActive(true);
            fit();
            if (settledOpen) return;
            if (wasHidden) Sheet.anchoredPosition = new Vector2(0f, -Sheet.sizeDelta.y);
            slide = Sheet.DOAnchorPosY(0f, SlideInSeconds).SetEase(Ease.OutCubic).SetLink(Panel);
            fade = scrimGroup.DOFade(1f, ScrimFadeSeconds).SetLink(Panel);
        }

        // Down and off. A sheet already leaving is left to finish — a second tap must not restart it.
        public void Hide()
        {
            if (Panel == null || !Panel.activeSelf || hiding) return;
            KillTweens();
            hiding = true;
            slide = Sheet.DOAnchorPosY(-Sheet.sizeDelta.y, SlideOutSeconds)
                .SetEase(Ease.InCubic)
                .SetLink(Panel)
                .OnComplete(() =>
                {
                    hiding = false;
                    if (Panel != null) Panel.SetActive(false);
                });
            fade = scrimGroup.DOFade(0f, ScrimFadeSeconds).SetLink(Panel);
        }

        // Kill() does not complete: a cut-short exit never fires its OnComplete (which would hide a sheet being re-shown).
        public void KillTweens()
        {
            slide?.Kill();
            fade?.Kill();
            slide = fade = null;
            hiding = false;
        }
    }

    // What a sheet acts on, captured when it is asked — never re-read at the tap.
    private readonly struct Target
    {
        public readonly string BotName, ProfileId, ChatId;

        public Target(string botName, string profileId, string chatId)
        {
            BotName = botName;
            ProfileId = profileId;
            ChatId = chatId;
        }

        public bool IsSet => !string.IsNullOrEmpty(ChatId);
    }

    // ---- build helpers ----

    private static GameObject NewChild(Transform parent, string name, params Type[] components)
    {
        var go = new GameObject(name, components);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static TextMeshProUGUI BuildTmp(Transform parent, string name, float size, TextAlignmentOptions alignment, bool wrap)
    {
        var tmp = NewChild(parent, name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.alignment = alignment;   // never TMP's default
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;   // the project's default is NoWrap
        tmp.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void AddRounded(GameObject go, float radius)
    {
        var rounded = go.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = radius;   // 1:1 with the visual radius
        rounded.Validate();
        rounded.Refresh();
    }

    private static void StretchFull(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetTopStretch(RectTransform rect, float top, float height, float leftInset, float rightInset)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(-(leftInset + rightInset), height);
        rect.anchoredPosition = new Vector2((leftInset - rightInset) / 2f, -top);
    }

    private static void SetBottomStretch(RectTransform rect, float bottom, float height, float sideInset)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, bottom);
        rect.sizeDelta = new Vector2(-sideInset * 2f, height);
    }
}

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Automation.BotSettingsUI;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the Profile tab's six sub-page panels (Аккаунт / Уведомления /
/// Конфиденциальность / Поддержка / О приложении / Лицензии) inside
/// Screen_Profile, plus the support bottom sheet, the shared confirm popup,
/// and the NotificationFx component on ChatManager. Idempotent
/// delete-and-rebuild; also removes the legacy LogoutButton/LogoutPopup
/// (superseded by Аккаунт → «Удалить все данные»). All sizes in 1080×1920
/// canvas reference units. Save the scene after running (headless entry saves
/// automatically).
/// </summary>
public static class ProfileSubPagesBuilder
{
    // ── Design tokens ───────────────────────────────────────────────────────
    private const float HeaderHeight = 300f;
    private const float Gutter = 44f;
    private const float CardGap = 32f;
    private const float RowHeight = 150f;
    private const float IconSize = 100f;
    private const float IconRadius = 28f;
    private const float IconGlyphInset = 24f;
    private const float CardRadius = 40f;
    private const float ButtonHeight = 144f;
    private const float SwipeStripWidth = 150f;

    private static readonly Color Bg = Hex("#F0F2F5");
    private static readonly Color Card = Color.white;
    private static readonly Color Ink = Hex("#1A1A2E");
    private static readonly Color Muted = Hex("#65676B");
    private static readonly Color Divider = Hex("#E4E6EB");
    private static readonly Color Primary = Hex("#1B7CEB");
    private static readonly Color PrimaryLight = Hex("#E8F2FD");
    private static readonly Color DangerInk = Hex("#E53935");
    private static readonly Color DangerFill = Hex("#FFCED5");
    private static readonly Color ConfirmDangerFill = Hex("#EB4545");
    private static readonly Color NeutralButton = Hex("#E4E6EB");
    private static readonly Color PopupBody = Hex("#636366");
    private static readonly Color Grabber = Hex("#C7C7CC");

    private static readonly Color IconBlue = Hex("#1B7CEB");
    private static readonly Color IconOrange = Hex("#FF9800");
    private static readonly Color IconGreen = Hex("#4CAF50");
    private static readonly Color IconPurple = Hex("#9C27B0");
    private static readonly Color IconBlueGray = Hex("#607D8B");

    // Fonts by GUID (default font's weight table is empty — always assign explicitly).
    private const string RegularGuid = "e0cdfe2d6a51446bcba7d2df147e2415";
    private const string MediumGuid = "d091b0cad5d964a53a41de97ba932a27";
    private const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";
    private const string BoldGuid = "1cd715823fef34be4a3d3f3c5572594c";

    private static TMP_FontAsset _regular, _medium, _semibold, _bold;

    private const string IconsDir = "Assets/Images/ProfileSubPages";

    private static Sprite _chevronLeft, _chevronRight, _chevronDown, _editIcon, _toggleBg, _toggleHandle;
    private static readonly Dictionary<string, Sprite> _psIcons = new Dictionary<string, Sprite>();
    private static readonly List<Component> _roundedToRefresh = new List<Component>();

    private struct PanelParts
    {
        public GameObject panel;
        public Button backButton;
        public SwipeToBackPanel swipe;
        public RectTransform content;
        public ScrollRect scroll;
    }

    // ── Entry points ────────────────────────────────────────────────────────

    [MenuItem("Tools/Profile Sub-Pages/Build")]
    public static void Build()
    {
        var subPages = BuildInternal();
        Selection.activeGameObject = subPages;
        EditorSceneManager.MarkSceneDirty(subPages.scene);
        Debug.Log("[ProfileSubPagesBuilder] Build complete — SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod ProfileSubPagesBuilder.BuildHeadless -quit
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        var subPages = BuildInternal();

        var controller = subPages.GetComponent<ProfileSubPages>();
        var so = new SerializedObject(controller);
        if (so.FindProperty("pages").GetArrayElementAtIndex(0).FindPropertyRelative("panel").objectReferenceValue == null)
            throw new System.InvalidOperationException("Headless build left pages[0].panel unwired.");

        EditorSceneManager.SaveScene(scene);
        Debug.Log("[ProfileSubPagesBuilder] Headless build + save complete.");
    }

    // ── Main build ──────────────────────────────────────────────────────────

    private static GameObject BuildInternal()
    {
        AssetDatabase.Refresh();
        EnsureIconImportSettings();
        LoadAssets();
        _roundedToRefresh.Clear();

        var profilePage = Object.FindFirstObjectByType<ProfilePage>(FindObjectsInactive.Include);
        if (profilePage == null)
            throw new System.InvalidOperationException("ProfilePage not found — is Main.unity open?");
        Transform screenProfile = profilePage.transform;

        // Idempotent teardown + legacy logout removal.
        DestroyAllByName(screenProfile, "SubPages");
        DestroyAllByName(screenProfile, "LogoutButton");
        DestroyAllByName(screenProfile, "LogoutPopup");

        // Root: active, invisible, stretched; panels start inactive inside it.
        var root = NewChild(screenProfile.gameObject, "SubPages", out var rootRt);
        StretchFill(rootRt);
        root.transform.SetAsLastSibling();
        var controller = root.AddComponent<ProfileSubPages>();

        // The edit popup must render above sub-pages (Account opens it).
        Transform editPopup = screenProfile.Find("EditPopup");
        if (editPopup != null) editPopup.SetAsLastSibling();

        // Panels
        var account = BuildPanelShell(root, "PanelAccount", "Аккаунт");
        var notifications = BuildPanelShell(root, "PanelNotifications", "Уведомления");
        var privacy = BuildPanelShell(root, "PanelPrivacy", "Конфиденциальность");
        var support = BuildPanelShell(root, "PanelSupport", "Поддержка", extraBottomPadding: ButtonHeight + 80f);
        var about = BuildPanelShell(root, "PanelAbout", "О приложении");
        var licenses = BuildPanelShell(root, "PanelLicenses", "Лицензии");

        var so = new SerializedObject(controller);

        BuildAccountContent(account, so);
        BuildNotificationsContent(notifications, so);
        BuildPrivacyContent(privacy, so);
        BuildSupportContent(support, so);
        BuildAboutContent(about, so);
        BuildLicensesContent(licenses, so);
        BuildConfirmPopup(root, so);

        StampPages(so, account, notifications, privacy, support, about, licenses);
        so.ApplyModifiedPropertiesWithoutUndo();

        WireNotificationFx();

        // Radius bake needs sized rects.
        Canvas.ForceUpdateCanvases();
        foreach (var rounded in _roundedToRefresh)
            RefreshRounded(rounded);

        return root;
    }

    // ── Panel shell: bg + header + scroll column + swipe strip ─────────────

    private static PanelParts BuildPanelShell(GameObject root, string name, string title, float extraBottomPadding = 0f)
    {
        var panel = NewChild(root, name, out var panelRt);
        StretchFill(panelRt);
        var bg = panel.AddComponent<Image>();
        bg.color = Bg;
        bg.raycastTarget = true;

        // Header — mirrors Screen_Profile/Header (h=300, safe area baked in).
        var header = NewChild(panel, "Header", out var headerRt);
        SetAnchors(headerRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        headerRt.offsetMin = new Vector2(0f, -HeaderHeight);
        headerRt.offsetMax = Vector2.zero;
        header.AddComponent<Image>().color = Card;

        var hairline = NewChild(header, "Border", out var hairRt);
        SetAnchors(hairRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        hairRt.offsetMin = Vector2.zero;
        hairRt.offsetMax = new Vector2(0f, 2f);
        hairline.AddComponent<Image>().color = Divider;

        var titleGo = NewChild(header, "Title", out var titleRt);
        SetAnchors(titleRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        titleRt.offsetMin = new Vector2(150f, 60f);
        titleRt.offsetMax = new Vector2(-150f, 120f);
        var titleTmp = AddText(titleGo, title, 48f, _semibold, Ink);
        titleTmp.alignment = TextAlignmentOptions.Center;

        var backGo = NewChild(header, "BackButton", out var backRt);
        SetAnchors(backRt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
        backRt.anchoredPosition = new Vector2(70f, 90f);
        backRt.sizeDelta = new Vector2(120f, 120f);
        var backHit = backGo.AddComponent<Image>();
        backHit.color = new Color(0f, 0f, 0f, 0f);
        backHit.raycastTarget = true;
        var backButton = backGo.AddComponent<Button>();
        backButton.targetGraphic = backHit;
        var backIcon = NewChild(backGo, "Icon", out var backIconRt);
        SetAnchors(backIconRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        backIconRt.sizeDelta = new Vector2(60f, 60f);
        AddIconImage(backIcon, _chevronLeft, Primary);

        // Scroll column below the header.
        var scrollGo = NewChild(panel, "ScrollView", out var scrollRt);
        StretchFill(scrollRt);
        scrollRt.offsetMax = new Vector2(0f, -HeaderHeight);
        var scrollHit = scrollGo.AddComponent<Image>();
        scrollHit.color = new Color(0f, 0f, 0f, 0f);
        scrollHit.raycastTarget = true;
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 40f;

        var viewport = NewChild(scrollGo, "Viewport", out var viewportRt);
        StretchFill(viewportRt);
        var viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = new Color(1f, 1f, 1f, 0.01f);
        viewportImg.raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        var content = NewChild(viewport, "Content", out var contentRt);
        SetAnchors(contentRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)Gutter, (int)Gutter, 50, (int)(96f + extraBottomPadding));
        vlg.spacing = CardGap;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRt;
        scroll.content = contentRt;

        // Left-edge swipe-back strip (BotSettingsSwipeWirer pattern + memory:
        // ClickPassthrough.deliverPressToAllBehind so taps reach content).
        var strip = NewChild(panel, "SwipeBack", out var stripRt);
        SetAnchors(stripRt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f));
        stripRt.anchoredPosition = new Vector2(SwipeStripWidth / 2f, 0f);
        stripRt.sizeDelta = new Vector2(SwipeStripWidth, 0f);
        var stripImg = strip.AddComponent<Image>();
        stripImg.color = new Color(1f, 1f, 1f, 0f);
        stripImg.raycastTarget = true;
        var swipe = strip.AddComponent<SwipeToBackPanel>();
        var swipeSo = new SerializedObject(swipe);
        swipeSo.FindProperty("panelToSlide").objectReferenceValue = panelRt;
        swipeSo.FindProperty("contentScrollRect").objectReferenceValue = scroll;
        swipeSo.ApplyModifiedPropertiesWithoutUndo();
        var passthrough = strip.AddComponent<ClickPassthrough>();
        passthrough.allowedPanel = panel.transform;
        var passSo = new SerializedObject(passthrough);
        passSo.FindProperty("deliverPressToAllBehind").boolValue = true;
        passSo.ApplyModifiedPropertiesWithoutUndo();
        strip.transform.SetAsLastSibling();

        panel.SetActive(false);

        return new PanelParts { panel = panel, backButton = backButton, swipe = swipe, content = contentRt, scroll = scroll };
    }

    // ── Page contents ───────────────────────────────────────────────────────

    private static void BuildAccountContent(PanelParts parts, SerializedObject so)
    {
        // Profile summary card (mirrors the main screen's ProfileCard).
        var card = MakeCard(parts.content.gameObject, "ProfileCard");
        var cardLayout = card.GetComponent<VerticalLayoutGroup>();
        Object.DestroyImmediate(cardLayout);
        var hlg = card.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(44, 44, 44, 44);
        hlg.spacing = 40f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        var avatar = NewChild(card, "AvatarBg", out _);
        SetPreferredSize(avatar, 176f, 176f);
        avatar.AddComponent<Image>().color = Primary;
        AddRounded(avatar, 88f);
        var initial = NewChild(avatar, "Initial", out var initialRt);
        StretchFill(initialRt);
        var initialTmp = AddText(initial, "И", 72f, _bold, Color.white);
        initialTmp.alignment = TextAlignmentOptions.Center;

        var info = NewChild(card, "InfoGroup", out _);
        var infoVlg = info.AddComponent<VerticalLayoutGroup>();
        infoVlg.spacing = 12f;
        infoVlg.childAlignment = TextAnchor.MiddleLeft;
        infoVlg.childForceExpandWidth = true;
        infoVlg.childForceExpandHeight = false;
        infoVlg.childControlWidth = true;
        infoVlg.childControlHeight = true;
        info.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var nameTmp = AddText(NewChild(info, "NameText", out _), "Иван Петров", 50f, _bold, Ink);
        var emailTmp = AddText(NewChild(info, "EmailText", out _), "ivan.petrov@email.com", 36f, _regular, Muted);

        var editBtnGo = NewChild(card, "EditButton", out _);
        SetPreferredSize(editBtnGo, 110f, 110f);
        var editBg = editBtnGo.AddComponent<Image>();
        editBg.color = PrimaryLight;
        AddRounded(editBtnGo, 55f);
        var editButton = editBtnGo.AddComponent<Button>();
        editButton.targetGraphic = editBg;
        var editIconGo = NewChild(editBtnGo, "EditIcon", out var editIconRt);
        StretchFill(editIconRt, 32f);
        AddIconImage(editIconGo, _editIcon, Primary);

        // «Данные»
        MakeCaption(parts.content.gameObject, "Данные");
        var dataCard = MakeCard(parts.content.gameObject, "DataCard");
        MakeInfoRow(dataCard, PsIcon("PS_Smartphone"), IconBlueGray,
            "Хранятся на этом устройстве",
            "Боты, чаты и настройки не привязаны к облачному аккаунту — они живут только в этом приложении.");

        // «Опасная зона»
        MakeCaption(parts.content.gameObject, "Опасная зона");
        var deleteButton = MakeBigButton(parts.content.gameObject, "DeleteAllButton",
            "Удалить все данные", DangerFill, DangerInk, PsIcon("PS_Trash"));
        MakeFinePrint(parts.content.gameObject,
            "Удалит всех ботов, историю и настройки на этом устройстве.\nДействие нельзя отменить.", center: true);

        so.FindProperty("accountAvatarInitial").objectReferenceValue = initialTmp;
        so.FindProperty("accountName").objectReferenceValue = nameTmp;
        so.FindProperty("accountEmail").objectReferenceValue = emailTmp;
        so.FindProperty("accountEditButton").objectReferenceValue = editButton;
        so.FindProperty("accountDeleteButton").objectReferenceValue = deleteButton;
    }

    private static void BuildNotificationsContent(PanelParts parts, SerializedObject so)
    {
        MakeCaption(parts.content.gameObject, "В приложении");
        var card = MakeCard(parts.content.gameObject, "TogglesCard");

        var sound = MakeToggleRow(card, PsIcon("PS_Speaker"), IconOrange, "Звук новых сообщений");
        MakeDivider(card);
        var vibration = MakeToggleRow(card, PsIcon("PS_Vibrate"), IconPurple, "Вибрация");
        MakeDivider(card);
        var unread = MakeToggleRow(card, PsIcon("PS_Unread"), IconBlue, "Счётчик непрочитанных");

        MakeFinePrint(parts.content.gameObject, "Действуют, пока приложение открыто.", center: false);

        so.FindProperty("soundToggle").objectReferenceValue = sound;
        so.FindProperty("vibrationToggle").objectReferenceValue = vibration;
        so.FindProperty("unreadBadgeToggle").objectReferenceValue = unread;
    }

    private static void BuildPrivacyContent(PanelParts parts, SerializedObject so)
    {
        MakeCaption(parts.content.gameObject, "Ваши данные");
        var dataCard = MakeCard(parts.content.gameObject, "DataCard");
        MakeInfoRow(dataCard, PsIcon("PS_Smartphone"), IconBlue,
            "На этом устройстве",
            "Боты и их настройки, история чатов, кэш фото и видео.");
        MakeDivider(dataCard);
        MakeInfoRow(dataCard, PsIcon("PS_Cloud"), IconBlueGray,
            "Обрабатываются на серверах",
            "Сообщения клиентов — чтобы ИИ мог на них ответить; прайс-листы — в защищённом хранилище.");

        MakeCaption(parts.content.gameObject, "Управление");
        var actionsCard = MakeCard(parts.content.gameObject, "ActionsCard");
        var media = MakeActionRow(actionsCard, PsIcon("PS_Media"), IconOrange, "Очистить кэш медиа",
            out var mediaLabel, out var mediaValue);
        MakeDivider(actionsCard);
        var history = MakeActionRow(actionsCard, PsIcon("PS_Bubble"), IconGreen, "Очистить историю чатов",
            out _, out _);

        MakeFinePrint(parts.content.gameObject,
            "Переписка в WhatsApp и Telegram не удаляется — очищаются только локальные копии в приложении.",
            center: false);

        so.FindProperty("mediaCacheButton").objectReferenceValue = media;
        so.FindProperty("mediaCacheLabel").objectReferenceValue = mediaLabel;
        so.FindProperty("mediaCacheValue").objectReferenceValue = mediaValue;
        so.FindProperty("historyButton").objectReferenceValue = history;
    }

    private static void BuildSupportContent(PanelParts parts, SerializedObject so)
    {
        MakeCaption(parts.content.gameObject, "Частые вопросы");
        var faqCard = MakeCard(parts.content.gameObject, "FaqCard");

        var faqItems = new FaqItemView[5];
        for (int i = 0; i < 5; i++)
        {
            if (i > 0) MakeDivider(faqCard);
            faqItems[i] = MakeFaqItem(faqCard, i);
        }

        // Pinned CTA in the thumb zone (outside the scroll column).
        var cta = NewChild(parts.panel, "SupportCta", out var ctaRt);
        SetAnchors(ctaRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        ctaRt.offsetMin = new Vector2(Gutter, 60f);
        ctaRt.offsetMax = new Vector2(-Gutter, 60f + ButtonHeight);
        var ctaBg = cta.AddComponent<Image>();
        ctaBg.color = Primary;
        AddRounded(cta, CardRadius);
        var ctaButton = cta.AddComponent<Button>();
        ctaButton.targetGraphic = ctaBg;
        BuildCenteredIconLabel(cta, PsIcon("PS_Send"), Color.white, "Написать в поддержку", 44f, _semibold, Color.white, 40f);

        var sheetRefs = BuildSupportSheet(parts.panel, so);

        so.FindProperty("supportCtaButton").objectReferenceValue = ctaButton;
        var itemsProp = so.FindProperty("faqItems");
        itemsProp.arraySize = faqItems.Length;
        for (int i = 0; i < faqItems.Length; i++)
            itemsProp.GetArrayElementAtIndex(i).objectReferenceValue = faqItems[i];
    }

    private static GameObject BuildSupportSheet(GameObject panel, SerializedObject so)
    {
        var sheetRoot = NewChild(panel, "SupportSheet", out var sheetRootRt);
        StretchFill(sheetRootRt);

        var backdrop = NewChild(sheetRoot, "Backdrop", out var backdropRt);
        StretchFill(backdropRt);
        var backdropImg = backdrop.AddComponent<Image>();
        backdropImg.color = new Color(0f, 0f, 0f, 0.5f);
        backdropImg.raycastTarget = true;
        var backdropGroup = backdrop.AddComponent<CanvasGroup>();
        var backdropButton = backdrop.AddComponent<Button>();
        backdropButton.targetGraphic = backdropImg;
        backdropButton.transition = Selectable.Transition.None;

        var sheet = NewChild(sheetRoot, "Sheet", out var sheetRt);
        SetAnchors(sheetRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        sheetRt.offsetMin = Vector2.zero;
        sheetRt.offsetMax = Vector2.zero;
        sheet.AddComponent<Image>().color = Card;
        var rounded = sheet.AddComponent<ImageWithIndependentRoundedCorners>();
        rounded.r = new Vector4(60f, 60f, 0f, 0f);
        _roundedToRefresh.Add(rounded);


        var vlg = sheet.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)Gutter, (int)Gutter, 20, 84);
        vlg.spacing = 24f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        sheet.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var grabberHolder = NewChild(sheet, "GrabberArea", out _);
        grabberHolder.AddComponent<LayoutElement>().preferredHeight = 24f;
        var grabber = NewChild(grabberHolder, "Grabber", out var grabberRt);
        SetAnchors(grabberRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        grabberRt.sizeDelta = new Vector2(108f, 12f);
        grabber.AddComponent<Image>().color = Grabber;
        AddRounded(grabber, 6f);

        var sheetTitle = AddText(NewChild(sheet, "Title", out _), "Написать в поддержку", 48f, _bold, Ink);
        sheetTitle.alignment = TextAlignmentOptions.Center;

        var messageInput = MakeInputField(sheet, "MessageInput", "Опишите проблему или вопрос…", 280f, multiline: true);
        var contactInput = MakeInputField(sheet, "ContactInput", "Телефон или @telegram для ответа", 120f, multiline: false);

        // Field-relative keyboard lift: raises the sheet just enough that the
        // FOCUSED input clears the keyboard (KeyboardAwarePanel would glue the
        // sheet's bottom to the keyboard and overshoot by the sheet's height).
        // Disabled by default — ProfileSubPages.Support enables it only after
        // the open slide settles.
        var keyboardLift = sheet.AddComponent<FocusedFieldKeyboardLift>();
        keyboardLift.enabled = false;
        var liftSo = new SerializedObject(keyboardLift);
        liftSo.FindProperty("panel").objectReferenceValue = sheetRt;
        var fieldsProp = liftSo.FindProperty("fields");
        fieldsProp.arraySize = 2;
        fieldsProp.GetArrayElementAtIndex(0).objectReferenceValue = messageInput;
        fieldsProp.GetArrayElementAtIndex(1).objectReferenceValue = contactInput;
        liftSo.ApplyModifiedPropertiesWithoutUndo();

        var caption = AddText(NewChild(sheet, "Caption", out _),
            "К сообщению добавятся версия приложения и модель устройства — так мы быстрее разберёмся.",
            30f, _regular, Muted);
        caption.alignment = TextAlignmentOptions.Left;

        var send = NewChild(sheet, "SendButton", out _);
        send.AddComponent<LayoutElement>().preferredHeight = ButtonHeight;
        var sendBg = send.AddComponent<Image>();
        sendBg.color = Primary;
        AddRounded(send, CardRadius);
        var sendButton = send.AddComponent<Button>();
        sendButton.targetGraphic = sendBg;
        sendButton.interactable = false;
        var sendLabelGo = NewChild(send, "Label", out var sendLabelRt);
        StretchFill(sendLabelRt);
        var sendLabel = AddText(sendLabelGo, "Отправить", 44f, _semibold, Color.white);
        sendLabel.alignment = TextAlignmentOptions.Center;

        sheetRoot.SetActive(false);

        so.FindProperty("supportSheetRoot").objectReferenceValue = sheetRoot;
        so.FindProperty("supportSheetBackdrop").objectReferenceValue = backdropGroup;
        so.FindProperty("supportBackdropButton").objectReferenceValue = backdropButton;
        so.FindProperty("supportSheetPanel").objectReferenceValue = sheetRt;
        so.FindProperty("supportSheetKeyboard").objectReferenceValue = keyboardLift;
        so.FindProperty("supportMessageInput").objectReferenceValue = messageInput;
        so.FindProperty("supportContactInput").objectReferenceValue = contactInput;
        so.FindProperty("supportSendButton").objectReferenceValue = sendButton;
        so.FindProperty("supportSendLabel").objectReferenceValue = sendLabel;
        return sheetRoot;
    }

    private static void BuildAboutContent(PanelParts parts, SerializedObject so)
    {
        // Hero: icon square + name + version, centered, no card.
        var hero = NewChild(parts.content.gameObject, "Hero", out _);
        var heroVlg = hero.AddComponent<VerticalLayoutGroup>();
        heroVlg.padding = new RectOffset(0, 0, 40, 8);
        heroVlg.spacing = 0f;
        heroVlg.childAlignment = TextAnchor.UpperCenter;
        heroVlg.childForceExpandWidth = false;
        heroVlg.childForceExpandHeight = false;
        heroVlg.childControlWidth = true;
        heroVlg.childControlHeight = true;

        var appIcon = NewChild(hero, "AppIcon", out _);
        SetPreferredSize(appIcon, 252f, 252f);
        appIcon.AddComponent<Image>().color = Primary;
        AddRounded(appIcon, 60f);
        var robot = NewChild(appIcon, "Robot", out var robotRt);
        SetAnchors(robotRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        robotRt.sizeDelta = new Vector2(150f, 150f);
        AddIconImage(robot, PsIcon("PS_Robot"), Color.white);

        var nameGo = NewChild(hero, "AppName", out _);
        var nameTmp = AddText(nameGo, ProfileSubPages.ProductName, 56f, _bold, Ink);
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.margin = new Vector4(0f, 36f, 0f, 0f);

        var versionGo = NewChild(hero, "Version", out _);
        var versionTmp = AddText(versionGo, "Версия 1.0", 36f, _regular, Muted);
        versionTmp.alignment = TextAlignmentOptions.Center;
        versionTmp.margin = new Vector4(0f, 10f, 0f, 0f);

        // Value prop card.
        var propCard = MakeCard(parts.content.gameObject, "ValuePropCard");
        var propText = AddText(NewChild(propCard, "Text", out _),
            "ИИ-ассистент для вашего WhatsApp и Telegram. Отвечает клиентам 24/7 — вы видите каждый диалог и можете вмешаться в любой момент.",
            39f, _regular, Ink);
        propText.alignment = TextAlignmentOptions.Center;
        propText.margin = new Vector4(44f, 40f, 44f, 40f);

        MakeCaption(parts.content.gameObject, "Документы");
        var docsCard = MakeCard(parts.content.gameObject, "DocsCard");
        var licensesRow = MakeActionRow(docsCard, PsIcon("PS_Doc"), IconBlueGray, "Лицензии открытого ПО",
            out _, out _);

        MakeFinePrint(parts.content.gameObject,
            "© 2026 SynergySoft\nСделано для бизнеса в Казахстане и СНГ", center: true);

        so.FindProperty("aboutVersionLabel").objectReferenceValue = versionTmp;
        so.FindProperty("licensesButton").objectReferenceValue = licensesRow;
    }

    private static void BuildLicensesContent(PanelParts parts, SerializedObject so)
    {
        var card = MakeCard(parts.content.gameObject, "LicensesCard");
        var text = AddText(NewChild(card, "Text", out _), "…", 36f, _regular, Ink);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.margin = new Vector4(44f, 40f, 44f, 40f);
        so.FindProperty("licensesText").objectReferenceValue = text;
    }

    // ── Shared confirm popup (PopupUI contract: scrim root + "Card" child) ──

    private static void BuildConfirmPopup(GameObject root, SerializedObject so)
    {
        var popup = NewChild(root, "ConfirmPopup", out var popupRt);
        StretchFill(popupRt);
        var scrim = popup.AddComponent<Image>();
        scrim.color = new Color(0f, 0f, 0f, 0.5f);
        scrim.raycastTarget = true;

        var card = NewChild(popup, "Card", out var cardRt);
        SetAnchors(cardRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        cardRt.sizeDelta = new Vector2(900f, 480f);
        card.AddComponent<Image>().color = Card;
        AddRounded(card, 48f);
        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(50, 50, 60, 60);
        vlg.spacing = 30f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        card.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = AddText(NewChild(card, "Title", out _), "Заголовок", 48f, _bold, Ink);
        title.alignment = TextAlignmentOptions.Center;

        var message = AddText(NewChild(card, "Message", out _), "Сообщение", 40f, _regular, PopupBody);
        message.alignment = TextAlignmentOptions.Center;

        var buttonRow = NewChild(card, "ButtonRow", out _);
        buttonRow.AddComponent<LayoutElement>().preferredHeight = 120f;
        var rowHlg = buttonRow.AddComponent<HorizontalLayoutGroup>();
        rowHlg.spacing = 24f;
        rowHlg.childAlignment = TextAnchor.MiddleCenter;
        rowHlg.childForceExpandWidth = true;
        rowHlg.childForceExpandHeight = true;
        rowHlg.childControlWidth = true;
        rowHlg.childControlHeight = true;

        var cancel = MakePopupButton(buttonRow, "CancelButton", "Отмена", NeutralButton, Muted);
        var action = MakePopupButton(buttonRow, "ActionButton", "Удалить", ConfirmDangerFill, Color.white);

        popup.SetActive(false);

        so.FindProperty("confirmPopup").objectReferenceValue = popup;
        so.FindProperty("confirmTitle").objectReferenceValue = title;
        so.FindProperty("confirmMessage").objectReferenceValue = message;
        so.FindProperty("confirmActionButton").objectReferenceValue = action.button;
        so.FindProperty("confirmActionLabel").objectReferenceValue = action.label;
        so.FindProperty("confirmCancelButton").objectReferenceValue = cancel.button;
    }

    private static (Button button, TextMeshProUGUI label) MakePopupButton(
        GameObject parent, string name, string text, Color fill, Color ink)
    {
        var go = NewChild(parent, name, out _);
        var bg = go.AddComponent<Image>();
        bg.color = fill;
        AddRounded(go, 28f);
        var button = go.AddComponent<Button>();
        button.targetGraphic = bg;
        var labelGo = NewChild(go, "Label", out var labelRt);
        StretchFill(labelRt);
        var label = AddText(labelGo, text, 42f, _semibold, ink);
        label.alignment = TextAlignmentOptions.Center;
        return (button, label);
    }

    // ── Row / card factories ────────────────────────────────────────────────

    private static GameObject MakeCard(GameObject parent, string name)
    {
        var card = NewChild(parent, name, out _);
        card.AddComponent<Image>().color = Card;
        AddRounded(card, CardRadius);
        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 0f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        return card;
    }

    private static void MakeCaption(GameObject parent, string text)
    {
        var go = NewChild(parent, "Caption", out _);
        var tmp = AddText(go, text.ToUpperInvariant(), 30f, _semibold, Muted);
        tmp.characterSpacing = 6f;
        tmp.margin = new Vector4(12f, 24f, 12f, 0f);
    }

    private static void MakeFinePrint(GameObject parent, string text, bool center)
    {
        var go = NewChild(parent, "FinePrint", out _);
        var tmp = AddText(go, text, 30f, _regular, Muted);
        tmp.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
        tmp.margin = new Vector4(center ? 12f : 16f, -8f, center ? 12f : 16f, 0f);
        tmp.lineSpacing = 8f;
    }

    private static void MakeDivider(GameObject card)
    {
        var divider = NewChild(card, "Divider", out _);
        divider.AddComponent<LayoutElement>().preferredHeight = 2f;
        divider.AddComponent<Image>().color = Divider;
    }

    private static GameObject MakeIconSquircle(GameObject row, Sprite glyph, Color bgColor)
    {
        var iconBg = NewChild(row, "IconBg", out _);
        SetPreferredSize(iconBg, IconSize, IconSize);
        iconBg.AddComponent<Image>().color = bgColor;
        AddRounded(iconBg, IconRadius);
        var icon = NewChild(iconBg, "Icon", out var iconRt);
        StretchFill(iconRt, IconGlyphInset);
        AddIconImage(icon, glyph, Color.white);
        return iconBg;
    }

    private static HorizontalLayoutGroup MakeRowLayout(GameObject row, int topBottomPad = 25)
    {
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(44, 54, topBottomPad, topBottomPad);
        hlg.spacing = 40f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        return hlg;
    }

    // Tappable row: icon + label + optional value + chevron. Returns the Button.
    private static Button MakeActionRow(GameObject card, Sprite glyph, Color iconColor, string labelText,
        out TextMeshProUGUI label, out TextMeshProUGUI value)
    {
        var row = NewChild(card, "Row_" + labelText, out _);
        row.AddComponent<LayoutElement>().preferredHeight = RowHeight;
        var hit = row.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        var button = row.AddComponent<Button>();
        button.targetGraphic = hit;
        MakeRowLayout(row);

        MakeIconSquircle(row, glyph, iconColor);

        var labelGo = NewChild(row, "Label", out _);
        label = AddText(labelGo, labelText, 42f, _medium, Ink);
        labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var valueGo = NewChild(row, "Value", out _);
        value = AddText(valueGo, "", 36f, _regular, Muted);
        value.alignment = TextAlignmentOptions.MidlineRight;

        var chevronGo = NewChild(row, "Chevron", out _);
        SetPreferredSize(chevronGo, 32f, 32f);
        AddIconImage(chevronGo, _chevronRight, Muted);

        return button;
    }

    // Non-tappable two-line disclosure row.
    private static void MakeInfoRow(GameObject card, Sprite glyph, Color iconColor, string title, string subtitle)
    {
        var row = NewChild(card, "InfoRow_" + title, out _);
        var hlg = MakeRowLayout(row, topBottomPad: 30);
        hlg.childAlignment = TextAnchor.UpperLeft;

        MakeIconSquircle(row, glyph, iconColor);

        var textGroup = NewChild(row, "TextGroup", out _);
        var vlg = textGroup.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        textGroup.AddComponent<LayoutElement>().flexibleWidth = 1f;

        AddText(NewChild(textGroup, "Title", out _), title, 42f, _medium, Ink);
        var sub = AddText(NewChild(textGroup, "Subtitle", out _), subtitle, 33f, _regular, Muted);
        sub.lineSpacing = 6f;
    }

    private static ToggleRow MakeToggleRow(GameObject card, Sprite glyph, Color iconColor, string labelText)
    {
        var row = NewChild(card, "Toggle_" + labelText, out _);
        row.AddComponent<LayoutElement>().preferredHeight = RowHeight;
        MakeRowLayout(row);

        MakeIconSquircle(row, glyph, iconColor);

        var labelGo = NewChild(row, "Label", out _);
        var label = AddText(labelGo, labelText, 42f, _medium, Ink);
        labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // Toggle geometry mirrors BotSettingsRebuilder.CreateToggleRow.
        var toggleGo = NewChild(row, "Toggle", out _);
        var toggleLe = toggleGo.AddComponent<LayoutElement>();
        toggleLe.preferredWidth = 130f;
        toggleLe.preferredHeight = 80f;
        var toggle = toggleGo.AddComponent<Toggle>();

        var track = NewChild(toggleGo, "Track", out var trackRt);
        StretchFill(trackRt);
        var trackImg = track.AddComponent<Image>();
        trackImg.sprite = _toggleBg;
        trackImg.color = Color.white; // tinted by ToggleRow at runtime
        AddRounded(track, 40f);

        var thumb = NewChild(track, "Thumb", out var thumbRt);
        SetAnchors(thumbRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f));
        thumbRt.anchoredPosition = new Vector2(40f, 0f);
        thumbRt.sizeDelta = new Vector2(70f, 70f);
        var thumbImg = thumb.AddComponent<Image>();
        thumbImg.sprite = _toggleHandle;
        thumbImg.preserveAspect = true;

        toggle.targetGraphic = trackImg;

        var toggleRow = row.AddComponent<ToggleRow>();
        var so = new SerializedObject(toggleRow);
        so.FindProperty("toggle").objectReferenceValue = toggle;
        so.FindProperty("trackImage").objectReferenceValue = trackImg;
        so.FindProperty("thumb").objectReferenceValue = thumbRt;
        so.FindProperty("labelText").objectReferenceValue = label;
        so.FindProperty("thumbOffsetX").floatValue = 50f; // track 130 − thumb 70 − 2×edge 5
        so.ApplyModifiedPropertiesWithoutUndo();

        return toggleRow;
    }

    private static FaqItemView MakeFaqItem(GameObject card, int index)
    {
        var item = NewChild(card, $"FaqItem{index}", out _);
        var vlg = item.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 0f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        var question = NewChild(item, "Question", out _);
        question.AddComponent<LayoutElement>().preferredHeight = 130f;
        var hit = question.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        var button = question.AddComponent<Button>();
        button.targetGraphic = hit;
        var qHlg = MakeRowLayout(question);
        qHlg.padding = new RectOffset(44, 54, 25, 25);
        qHlg.spacing = 24f;

        var qLabelGo = NewChild(question, "Label", out _);
        var qLabel = AddText(qLabelGo, "Вопрос", 40f, _medium, Ink);
        qLabelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var chevronGo = NewChild(question, "Chevron", out var chevronRt);
        SetPreferredSize(chevronGo, 32f, 32f);
        AddIconImage(chevronGo, _chevronDown, Muted);

        var answer = NewChild(item, "Answer", out _);
        var answerLe = answer.AddComponent<LayoutElement>();
        answerLe.preferredHeight = 0f;
        answer.AddComponent<RectMask2D>();

        var answerTextGo = NewChild(answer, "AnswerText", out var answerTextRt);
        SetAnchors(answerTextRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        answerTextRt.offsetMin = new Vector2(44f, -400f);
        answerTextRt.offsetMax = new Vector2(-54f, 0f);
        var answerText = AddText(answerTextGo, "Ответ", 36f, _regular, Muted);
        answerText.alignment = TextAlignmentOptions.TopLeft;
        answerText.lineSpacing = 8f;

        var view = item.AddComponent<FaqItemView>();
        var so = new SerializedObject(view);
        so.FindProperty("questionButton").objectReferenceValue = button;
        so.FindProperty("questionText").objectReferenceValue = qLabel;
        so.FindProperty("chevron").objectReferenceValue = chevronRt;
        so.FindProperty("answerText").objectReferenceValue = answerText;
        so.FindProperty("answerLayout").objectReferenceValue = answerLe;
        so.ApplyModifiedPropertiesWithoutUndo();

        return view;
    }

    private static Button MakeBigButton(GameObject parent, string name, string text, Color fill, Color ink, Sprite icon)
    {
        var go = NewChild(parent, name, out _);
        go.AddComponent<LayoutElement>().preferredHeight = ButtonHeight;
        var bg = go.AddComponent<Image>();
        bg.color = fill;
        AddRounded(go, CardRadius);
        var button = go.AddComponent<Button>();
        button.targetGraphic = bg;
        BuildCenteredIconLabel(go, icon, ink, text, 44f, _semibold, ink, 44f);
        return button;
    }

    private static void BuildCenteredIconLabel(GameObject parent, Sprite icon, Color iconTint,
        string text, float fontSize, TMP_FontAsset font, Color textColor, float iconSize)
    {
        var holder = NewChild(parent, "Content", out var holderRt);
        StretchFill(holderRt);
        var hlg = holder.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 20f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        if (icon != null)
        {
            var iconGo = NewChild(holder, "Icon", out _);
            SetPreferredSize(iconGo, iconSize, iconSize);
            AddIconImage(iconGo, icon, iconTint);
        }

        var label = AddText(NewChild(holder, "Label", out _), text, fontSize, font, textColor);
        label.alignment = TextAlignmentOptions.Center;
    }

    private static TMP_InputField MakeInputField(GameObject parent, string name, string placeholder,
        float height, bool multiline)
    {
        var go = NewChild(parent, name, out _);
        go.AddComponent<LayoutElement>().preferredHeight = height;
        var bg = go.AddComponent<Image>();
        bg.color = Bg;
        AddRounded(go, 28f);

        var textArea = NewChild(go, "TextArea", out var areaRt);
        StretchFill(areaRt, new RectOffset(32, 32, 24, 24));
        textArea.AddComponent<RectMask2D>();

        var placeholderGo = NewChild(textArea, "Placeholder", out var placeholderRt);
        StretchFill(placeholderRt);
        var placeholderTmp = AddText(placeholderGo, placeholder, 40f, _regular, Muted);
        placeholderTmp.fontStyle = FontStyles.Italic;
        placeholderTmp.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;

        var textGo = NewChild(textArea, "Text", out var textRt);
        StretchFill(textRt);
        var textTmp = AddText(textGo, "", 40f, _regular, Ink);
        textTmp.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;

        var input = go.AddComponent<TMP_InputField>();
        input.targetGraphic = bg;
        input.textViewport = areaRt;
        input.textComponent = textTmp;
        input.placeholder = placeholderTmp;
        input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        // No native input bar over the keyboard — the in-sheet field stays
        // visible via FocusedFieldKeyboardLift (baked in: a manual checkbox
        // would be wiped on every rebuild).
        input.shouldHideMobileInput = true;
        return input;
    }

    // ── Wiring beyond Screen_Profile ────────────────────────────────────────

    private static void WireNotificationFx()
    {
        var chatManager = Object.FindFirstObjectByType<ChatManager>(FindObjectsInactive.Include);
        if (chatManager == null)
        {
            Debug.LogWarning("[ProfileSubPagesBuilder] ChatManager not found — NotificationFx not wired.");
            return;
        }

        var fx = chatManager.GetComponent<NotificationFx>();
        if (fx == null) fx = chatManager.gameObject.AddComponent<NotificationFx>();

        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/notification_pop.wav");
        if (clip == null)
            Debug.LogWarning("[ProfileSubPagesBuilder] notification_pop.wav not found — run Tools/make_notification_sound.py.");

        var so = new SerializedObject(fx);
        so.FindProperty("incomingClip").objectReferenceValue = clip;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(fx);
    }

    private static void StampPages(SerializedObject so, params PanelParts[] parts)
    {
        var pagesProp = so.FindProperty("pages");
        pagesProp.arraySize = parts.Length;
        for (int i = 0; i < parts.Length; i++)
        {
            var element = pagesProp.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("panel").objectReferenceValue = parts[i].panel.GetComponent<RectTransform>();
            element.FindPropertyRelative("backButton").objectReferenceValue = parts[i].backButton;
            element.FindPropertyRelative("swipe").objectReferenceValue = parts[i].swipe;
        }
    }

    // ── Asset loading / import settings ─────────────────────────────────────

    private static void EnsureIconImportSettings()
    {
        if (!Directory.Exists(IconsDir)) return;

        foreach (string path in Directory.GetFiles(IconsDir, "*.png"))
        {
            string assetPath = path.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;

            bool dirty = importer.textureType != TextureImporterType.Sprite
                         || importer.spriteImportMode != SpriteImportMode.Single
                         || importer.mipmapEnabled
                         || !importer.alphaIsTransparency;
            if (!dirty) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }
    }

    private static void LoadAssets()
    {
        _regular = LoadFont(RegularGuid);
        _medium = LoadFont(MediumGuid);
        _semibold = LoadFont(SemiboldGuid);
        _bold = LoadFont(BoldGuid);

        _chevronLeft = LoadSprite("Assets/Images/Chat/chevron-left.png");
        _chevronRight = LoadSprite("Assets/Images/Chat/chevron-right.png");
        _chevronDown = LoadSprite("Assets/Images/Chat/chevron-down.png");
        _editIcon = LoadSprite("Assets/Images/New/Edit.png");
        _toggleBg = LoadSprite("Assets/Images/Toggle/bg2.png");
        _toggleHandle = LoadSprite("Assets/Images/Toggle/handle.png");

        _psIcons.Clear();
        foreach (string name in new[]
                 {
                     "PS_Speaker", "PS_Vibrate", "PS_Unread", "PS_Smartphone", "PS_Cloud",
                     "PS_Media", "PS_Bubble", "PS_Trash", "PS_Doc", "PS_Send", "PS_Robot",
                 })
            _psIcons[name] = LoadSprite($"{IconsDir}/{name}.png");
    }

    private static Sprite PsIcon(string name) => _psIcons.TryGetValue(name, out var sprite) ? sprite : null;

    private static TMP_FontAsset LoadFont(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null) Debug.LogWarning($"[ProfileSubPagesBuilder] Font missing for GUID {guid}");
        return font;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[ProfileSubPagesBuilder] Sprite missing: {path}");
        return sprite;
    }

    // ── Low-level helpers (BotSettingsRebuilder idiom) ─────────────────────

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }

    private static GameObject NewChild(GameObject parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivot;
    }

    private static void StretchFill(RectTransform rt, float uniformInset = 0f)
    {
        StretchFill(rt, new RectOffset((int)uniformInset, (int)uniformInset, (int)uniformInset, (int)uniformInset));
    }

    private static void StretchFill(RectTransform rt, RectOffset padding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(padding.left, padding.bottom);
        rt.offsetMax = new Vector2(-padding.right, -padding.top);
    }

    private static void SetPreferredSize(GameObject go, float width, float height)
    {
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;
        le.minWidth = width;
        le.minHeight = height;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;
    }

    private static TextMeshProUGUI AddText(GameObject go, string text, float size, TMP_FontAsset font, Color color)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        if (font != null) tmp.font = font;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        // This project's TMP default is NO wrapping — without this, long
        // subtitles/popup messages render one line tall and spill off-card.
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void AddIconImage(GameObject go, Sprite sprite, Color tint)
    {
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = tint;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private static void AddRounded(GameObject go, float radius)
    {
        var rounded = go.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = radius;
        _roundedToRefresh.Add(rounded);
    }

    private static void RefreshRounded(Component rounded)
    {
        if (rounded == null) return;
        switch (rounded)
        {
            case ImageWithRoundedCorners simple:
                simple.Validate();
                simple.Refresh();
                break;
            case ImageWithIndependentRoundedCorners independent:
                independent.Validate();
                independent.Refresh();
                break;
        }
    }

    private static void DestroyAllByName(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t != null && t != root && t.name == name)
                Object.DestroyImmediate(t.gameObject);
        }
    }
}
#endif

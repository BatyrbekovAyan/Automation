// ============================================================
//  AddBotPageSetup.cs  (Editor-only)
//
//  Menu:  Tools > Setup Add Bot Page
//
//  Canvas reference resolution: 1080 × 1920
//  All dimensions are in canvas units scaled for 1080-wide canvas.
//  Design reference: chat-app-ui.html — Add Bot form page.
// ============================================================

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AddBotPageSetup
{
    // ── Fonts ─────────────────────────────────────────────────────────────
    private const string FontRegularPath  = "Assets/TextMesh Pro/Fonts/SFProText-Regular SDF.asset";
    private const string FontBoldPath     = "Assets/TextMesh Pro/Fonts/SFProText-Bold SDF.asset";
    private const string FontMediumPath   = "Assets/TextMesh Pro/Fonts/SFProText-Medium SDF.asset";
    private const string FontSemiboldPath = "Assets/TextMesh Pro/Fonts/SFProText-Semibold SDF.asset";

    // ── Scale: canvas is 1080-wide, design at 390pt ─────────────────────
    // Design-pt × 2.77 ≈ canvas units (1080/390 = 2.769)
    private const float S = 2.769f;

    // Layout
    private const float HeaderH       = 139f;   // 50pt × S
    private const float ContentPadX   = 44f;    // 16pt × S
    private const float ContentPadTop = 44f;
    private const float ContentPadBot = 44f;
    private const float SectionGap    = 28f;    // ~10pt × S

    // Hero section
    private const float HeroPadTop    = 66f;    // 24pt × S
    private const float HeroPadSide   = 55f;    // 20pt × S
    private const float HeroPadBot    = 44f;    // 16pt × S
    private const float HeroSpacing   = 22f;    // 8pt × S
    private const float RobotW        = 360f;   // 130pt × S
    private const float RobotH        = 410f;   // 148pt × S
    private const float FHeroTitle    = 55f;    // 20pt × S
    private const float FHeroSubtitle = 39f;    // 14pt × S

    // Form card
    private const float CardRadius    = 39f;    // 14pt × S
    private const float CardMarginX   = 44f;    // 16pt × S

    // Form row
    private const float RowH          = 130f;   // ~47pt × S
    private const float RowPadX       = 44f;    // 16pt × S
    private const float RowPadY       = 42f;    // 15pt × S
    private const float FRowLabel     = 44f;    // 16pt × S
    private const float FRowValue     = 42f;    // 15pt × S
    private const float ChevronW      = 19f;    // 7pt × S
    private const float ChevronH      = 33f;    // 12pt × S
    private const float FChevron      = 42f;
    private const float DividerH      = 2f;     // ~1px at retina
    private const float IconSize      = 60f;    // platform icon in row

    // Create button
    private const float BtnH          = 138f;   // 50pt total (17pt padding × 2 + 17pt text)
    private const float BtnMarginX    = 44f;    // ~16pt × S (matches card margin)
    private const float BtnMarginTop  = 50f;    // 18pt × S
    private const float BtnRadius     = 39f;    // 14pt × S
    private const float FBtn          = 47f;    // 17pt × S

    // Popups
    private const float PopupCardW    = 900f;
    private const float PopupPadX     = 50f;
    private const float PopupPadY     = 60f;
    private const float PopupSpacing  = 30f;
    private const float PopupTitleH   = 70f;
    private const float PopupInputH   = 120f;
    private const float PopupBtnH     = 120f;
    private const float FPopupTitle   = 48f;
    private const float FPopupInput   = 42f;
    private const float FPopupBtn     = 42f;

    // Platform selector option buttons
    private const float OptionBtnH    = 138f;
    private const float OptionSpacing = 20f;

    // ── Palette ───────────────────────────────────────────────────────────
    private static readonly Color ColIosBlue     = Hex("#007AFF");
    private static readonly Color ColBg          = Hex("#F2F2F7");
    private static readonly Color ColWhite       = Color.white;
    private static readonly Color ColTextPrimary = Hex("#1C1C1E");
    private static readonly Color ColTextSec     = Hex("#8E8E93");
    private static readonly Color ColTextTert    = Hex("#C7C7CC");
    private static readonly Color ColBorder      = Hex("#E5E5EA");
    private static readonly Color ColWaGreen     = Hex("#25D366");
    private static readonly Color ColTgBlue      = Hex("#2AABEE");
    private static readonly Color ColOverlay     = new Color(0f, 0f, 0f, 0.5f);

    // ── Round sprite ──────────────────────────────────────────────────────
    private static Sprite _roundSprite;
    private static Sprite RoundSprite
    {
        get
        {
            if (_roundSprite == null)
                _roundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            return _roundSprite;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Setup Add Bot Page")]
    public static void Build()
    {
        // ── 1. Find Canvas ────────────────────────────────────────────────
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[AddBotPageSetup] No Canvas found in scene.");
            return;
        }

        // ── 2. Find Manager ──────────────────────────────────────────────
        var manager = Object.FindFirstObjectByType<Manager>();
        if (manager == null)
        {
            Debug.LogError("[AddBotPageSetup] No Manager component found in scene.");
            return;
        }

        // ── 3. Find BottomTabManager ─────────────────────────────────────
        var btm = Object.FindFirstObjectByType<BottomTabManager>();
        if (btm == null)
        {
            Debug.LogWarning("[AddBotPageSetup] BottomTabManager not found — tab wiring will be skipped.");
        }

        // ── 4. Find BotsPage ─────────────────────────────────────────────
        var botsPage = Object.FindFirstObjectByType<BotsPage>();
        if (botsPage == null)
        {
            Debug.LogWarning("[AddBotPageSetup] BotsPage not found — Chanel wiring will be skipped.");
        }

        // ── 5. Load fonts ────────────────────────────────────────────────
        var fontRegular  = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontRegularPath);
        var fontBold     = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBoldPath);
        var fontMedium   = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontMediumPath);
        var fontSemibold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSemiboldPath);

        if (fontRegular == null || fontBold == null)
        {
            Debug.LogError("[AddBotPageSetup] Could not load SFProText fonts. Check paths.");
            return;
        }

        var fontMed  = fontMedium ?? fontSemibold ?? fontBold;
        var fontSemi = fontSemibold ?? fontBold;

        // ── 6. Create root: AddBotFormPage ───────────────────────────────
        // Parent it under Canvas (same level as other screen panels)
        var root = MakeRect("AddBotFormPage", canvas.transform);
        Stretch(root);
        var rootImg = root.gameObject.AddComponent<Image>();
        rootImg.color = ColBg;
        // Force nested ContentSizeFitter + LayoutGroup chains to rebuild immediately
        // on first activation — otherwise hero/form card pop into place one frame late.
        root.gameObject.AddComponent<LayoutRebuildOnEnable>();
        root.gameObject.SetActive(false); // hidden by default

        // ══════════════════════════════════════════════════════════════════
        // HEADER
        // ══════════════════════════════════════════════════════════════════
        var header = MakeRect("Header", root);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot     = new Vector2(0.5f, 1f);
        header.offsetMin = Vector2.zero;
        header.offsetMax = Vector2.zero;
        header.sizeDelta = new Vector2(0, HeaderH);
        header.gameObject.AddComponent<Image>().color = ColWhite;

        // Header bottom border
        var hLine = MakeRect("Border", header);
        hLine.anchorMin = new Vector2(0, 0);
        hLine.anchorMax = new Vector2(1, 0);
        hLine.pivot = new Vector2(0.5f, 0f);
        hLine.sizeDelta = new Vector2(0, DividerH);
        hLine.anchoredPosition = Vector2.zero;
        hLine.gameObject.AddComponent<Image>().color = ColBorder;

        // Header title
        var headerTitle = MakeTMP("HeaderTitle", header, fontSemi, 50f, ColTextPrimary,
            "Добавить Бота");
        Stretch(headerTitle.rectTransform);
        headerTitle.alignment = TextAlignmentOptions.Center;

        // ══════════════════════════════════════════════════════════════════
        // SCROLL VIEW
        // ══════════════════════════════════════════════════════════════════
        var scrollGo = MakeRect("ScrollContent", root);
        scrollGo.anchorMin = Vector2.zero;
        scrollGo.anchorMax = Vector2.one;
        scrollGo.pivot = new Vector2(0.5f, 0.5f);
        scrollGo.offsetMin = new Vector2(0, 0);
        scrollGo.offsetMax = new Vector2(0, -HeaderH);

        scrollGo.gameObject.AddComponent<Image>().color = Color.clear;
        var scrollRect = scrollGo.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        // Viewport
        var viewport = MakeRect("Viewport", scrollGo);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        scrollRect.viewport = viewport;

        // Content
        var content = MakeRect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth  = true;  vlg.childForceExpandWidth  = true;
        vlg.childControlHeight = false; vlg.childForceExpandHeight = false;
        vlg.spacing = SectionGap;
        vlg.padding = new RectOffset((int)ContentPadX, (int)ContentPadX,
                                     (int)ContentPadTop, (int)ContentPadBot);

        var csf = content.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = content;

        // ══════════════════════════════════════════════════════════════════
        // HERO SECTION
        // ══════════════════════════════════════════════════════════════════
        var hero = MakeRect("HeroSection", content);
        var heroImg = hero.gameObject.AddComponent<Image>();
        heroImg.sprite = RoundSprite;
        heroImg.type = Image.Type.Sliced;
        heroImg.color = ColWhite;

        var heroVlg = hero.gameObject.AddComponent<VerticalLayoutGroup>();
        heroVlg.childAlignment = TextAnchor.UpperCenter;
        heroVlg.childControlWidth  = false; heroVlg.childForceExpandWidth  = false;
        heroVlg.childControlHeight = false; heroVlg.childForceExpandHeight = false;
        heroVlg.spacing = HeroSpacing;
        heroVlg.padding = new RectOffset((int)HeroPadSide, (int)HeroPadSide,
                                         (int)HeroPadTop, (int)HeroPadBot);

        var heroCsf = hero.gameObject.AddComponent<ContentSizeFitter>();
        heroCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Robot image placeholder
        var robotGo = MakeRect("RobotImage", hero);
        robotGo.sizeDelta = new Vector2(RobotW, RobotH);
        var robotLE = robotGo.gameObject.AddComponent<LayoutElement>();
        robotLE.minWidth = RobotW; robotLE.preferredWidth = RobotW;
        robotLE.minHeight = RobotH; robotLE.preferredHeight = RobotH;
        var robotImg = robotGo.gameObject.AddComponent<Image>();
        robotImg.color = Hex("#E8E8ED"); // light placeholder color

        // Hero title
        var heroTitle = MakeTMP("HeroTitle", hero, fontBold, FHeroTitle, ColTextPrimary,
            "Создайте нового бота");
        heroTitle.alignment = TextAlignmentOptions.Center;
        heroTitle.rectTransform.sizeDelta = new Vector2(0, FHeroTitle + 20f);
        var htLE = heroTitle.rectTransform.gameObject.AddComponent<LayoutElement>();
        htLE.minHeight = FHeroTitle + 20f;
        htLE.preferredHeight = FHeroTitle + 20f;

        // Hero subtitle
        var heroSubtitle = MakeTMP("HeroSubtitle", hero, fontRegular, FHeroSubtitle, ColTextSec,
            "Настройте автоматизацию для вашего мессенджера");
        heroSubtitle.alignment = TextAlignmentOptions.Center;
        heroSubtitle.enableWordWrapping = true;
        heroSubtitle.rectTransform.sizeDelta = new Vector2(0, FHeroSubtitle * 2 + 20f);
        var hsLE = heroSubtitle.rectTransform.gameObject.AddComponent<LayoutElement>();
        hsLE.minHeight = FHeroSubtitle * 2 + 20f;
        hsLE.preferredHeight = FHeroSubtitle * 2 + 20f;

        // ══════════════════════════════════════════════════════════════════
        // FORM CARD
        // ══════════════════════════════════════════════════════════════════
        var formCard = MakeRect("FormCard", content);
        var formCardImg = formCard.gameObject.AddComponent<Image>();
        formCardImg.sprite = RoundSprite;
        formCardImg.type = Image.Type.Sliced;
        formCardImg.color = ColWhite;

        var formVlg = formCard.gameObject.AddComponent<VerticalLayoutGroup>();
        formVlg.childAlignment = TextAnchor.UpperCenter;
        formVlg.childControlWidth  = true; formVlg.childForceExpandWidth  = true;
        formVlg.childControlHeight = false; formVlg.childForceExpandHeight = false;
        formVlg.spacing = 0;

        var formCsf = formCard.gameObject.AddComponent<ContentSizeFitter>();
        formCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ── Form rows ─────────────────────────────────────────────────────
        BuildFormRow(formCard, "PlatformRow", fontMed, fontRegular,
            "Платформа", "Выберите", ColTextSec, true, true,
            out Button platformRowBtn, out TextMeshProUGUI platformValTxt,
            out Image platformWaIcon, out Image platformTgIcon,
            out GameObject platformWaGroup, out GameObject platformTgGroup,
            out GameObject platformPlusSep);

        BuildFormRow(formCard, "BotNameRow", fontMed, fontRegular,
            "Имя Бота", "Введите имя", ColTextSec, true, false,
            out Button botNameRowBtn, out TextMeshProUGUI botNameValTxt,
            out _, out _, out _, out _, out _);

        BuildFormRow(formCard, "BusinessTypeRow", fontMed, fontRegular,
            "Тип бизнеса", "Выберите тип", ColTextSec, true, false,
            out Button businessTypeRowBtn, out TextMeshProUGUI businessTypeValTxt,
            out _, out _, out _, out _, out _);

        BuildFormRow(formCard, "DescriptionRow", fontMed, fontRegular,
            "Описание", "Необязательно", ColTextTert, false, false,
            out Button descriptionRowBtn, out TextMeshProUGUI descriptionValTxt,
            out _, out _, out _, out _, out _);

        // ══════════════════════════════════════════════════════════════════
        // CREATE BOT BUTTON
        // ══════════════════════════════════════════════════════════════════
        var createBtnGo = MakeRect("CreateBotButton", content);
        createBtnGo.sizeDelta = new Vector2(0, BtnH);
        var cbLE = createBtnGo.gameObject.AddComponent<LayoutElement>();
        cbLE.minHeight = BtnH; cbLE.preferredHeight = BtnH;

        var cbImg = createBtnGo.gameObject.AddComponent<Image>();
        cbImg.sprite = RoundSprite;
        cbImg.type = Image.Type.Sliced;
        cbImg.color = ColIosBlue;

        var createBtn = createBtnGo.gameObject.AddComponent<Button>();
        var cbColors = createBtn.colors;
        cbColors.highlightedColor = Hex("#006AE0");
        createBtn.colors = cbColors;

        var createBtnTxt = MakeTMP("Label", createBtnGo, fontBold, FBtn, ColWhite, "Создать Бота");
        Stretch(createBtnTxt.rectTransform);
        createBtnTxt.alignment = TextAlignmentOptions.Center;

        // ══════════════════════════════════════════════════════════════════
        // PLATFORM SELECTOR PANEL (overlay popup)
        // ══════════════════════════════════════════════════════════════════
        var platformPanel = BuildPlatformSelector(root, fontBold, fontSemi,
            out Button waBtn, out Button tgBtn, out Button bothBtn);

        // ══════════════════════════════════════════════════════════════════
        // BOT NAME INPUT PANEL (overlay popup)
        // ══════════════════════════════════════════════════════════════════
        var botNamePanel = BuildInputPopup(root, "BotNameInputPanel", fontBold, fontRegular, fontSemi,
            "Имя Бота", "Введите имя бота", false,
            out TMP_InputField botNameInput);

        // ══════════════════════════════════════════════════════════════════
        // BUSINESS SELECTOR PANEL (overlay popup)
        // ══════════════════════════════════════════════════════════════════
        var businessPanel = BuildBusinessSelector(root, fontBold);

        // ══════════════════════════════════════════════════════════════════
        // DESCRIPTION INPUT PANEL (overlay popup)
        // ══════════════════════════════════════════════════════════════════
        var descPanel = BuildInputPopup(root, "DescriptionInputPanel", fontBold, fontRegular, fontSemi,
            "Описание", "Введите описание бота", true,
            out TMP_InputField descInput);

        // ══════════════════════════════════════════════════════════════════
        // WIRE MANAGER SERIALIZED FIELDS
        // ══════════════════════════════════════════════════════════════════
        var managerSO = new SerializedObject(manager);

        managerSO.FindProperty("AddBotFormPage").objectReferenceValue         = root.gameObject;
        managerSO.FindProperty("platformValueText").objectReferenceValue      = platformValTxt;
        // Assign sprites on the two platform icon Images directly
        var waSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/Icons/WhatsApp.svg.png");
        var tgSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Images/Icons/Telegram_2019_Logo.svg.png");
        if (waSprite != null) platformWaIcon.sprite = waSprite;
        if (tgSprite != null) platformTgIcon.sprite = tgSprite;
        managerSO.FindProperty("platformWhatsappGroup").objectReferenceValue  = platformWaGroup;
        managerSO.FindProperty("platformTelegramGroup").objectReferenceValue  = platformTgGroup;
        managerSO.FindProperty("platformPlusSeparator").objectReferenceValue  = platformPlusSep;
        managerSO.FindProperty("botNameValueText").objectReferenceValue       = botNameValTxt;
        managerSO.FindProperty("businessTypeValueText").objectReferenceValue  = businessTypeValTxt;
        managerSO.FindProperty("descriptionValueText").objectReferenceValue   = descriptionValTxt;
        managerSO.FindProperty("createBotFormButton").objectReferenceValue    = createBtn;
        managerSO.FindProperty("platformSelectorPanel").objectReferenceValue  = platformPanel;
        managerSO.FindProperty("botNameInputPanel").objectReferenceValue      = botNamePanel;
        managerSO.FindProperty("businessSelectorPanel").objectReferenceValue  = businessPanel;
        managerSO.FindProperty("descriptionInputPanel").objectReferenceValue  = descPanel;
        managerSO.FindProperty("botNamePopupInput").objectReferenceValue      = botNameInput;
        managerSO.FindProperty("descriptionPopupInput").objectReferenceValue  = descInput;
        managerSO.FindProperty("platformRowButton").objectReferenceValue      = platformRowBtn;
        managerSO.FindProperty("botNameRowButton").objectReferenceValue       = botNameRowBtn;
        managerSO.FindProperty("businessTypeRowButton").objectReferenceValue  = businessTypeRowBtn;
        managerSO.FindProperty("descriptionRowButton").objectReferenceValue   = descriptionRowBtn;
        managerSO.FindProperty("whatsappOptionButton").objectReferenceValue   = waBtn;
        managerSO.FindProperty("telegramOptionButton").objectReferenceValue   = tgBtn;
        managerSO.FindProperty("bothOptionButton").objectReferenceValue       = bothBtn;

        managerSO.ApplyModifiedProperties();

        // ══════════════════════════════════════════════════════════════════
        // WIRE BOTTOMTABMANAGER tabs[2].screenPanel
        // ══════════════════════════════════════════════════════════════════
        if (btm != null)
        {
            var btmSO = new SerializedObject(btm);
            var tabsArray = btmSO.FindProperty("tabs");
            if (tabsArray != null && tabsArray.arraySize > 2)
            {
                var tab2 = tabsArray.GetArrayElementAtIndex(2);
                var screenPanelProp = tab2.FindPropertyRelative("screenPanel");
                if (screenPanelProp != null)
                {
                    screenPanelProp.objectReferenceValue = root.gameObject;
                }
            }
            btmSO.ApplyModifiedProperties();
        }

        // ══════════════════════════════════════════════════════════════════
        // WIRE BOTSPAGE Chanel
        // ══════════════════════════════════════════════════════════════════
        if (botsPage != null)
        {
            var bpSO = new SerializedObject(botsPage);
            var chanelProp = bpSO.FindProperty("Chanel");
            if (chanelProp != null)
            {
                chanelProp.objectReferenceValue = root.gameObject;
            }
            bpSO.ApplyModifiedProperties();
        }

        // ══════════════════════════════════════════════════════════════════
        // MARK DIRTY
        // ══════════════════════════════════════════════════════════════════
        EditorUtility.SetDirty(root.gameObject);
        EditorUtility.SetDirty(manager);
        if (btm != null) EditorUtility.SetDirty(btm);
        if (botsPage != null) EditorUtility.SetDirty(botsPage);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);

        Debug.Log("[AddBotPageSetup] Done — save the scene (Ctrl+S / Cmd+S).");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Form row builder
    // ══════════════════════════════════════════════════════════════════════
    private static void BuildFormRow(RectTransform parent, string name,
        TMP_FontAsset fontLabel, TMP_FontAsset fontValue,
        string label, string valuePlaceholder, Color valueColor,
        bool addDivider, bool addPlatformIcon,
        out Button button, out TextMeshProUGUI valueText,
        out Image whatsappIcon, out Image telegramIcon,
        out GameObject whatsappGroup, out GameObject telegramGroup,
        out GameObject plusSeparator)
    {
        // Row container (= the button)
        var row = MakeRect(name, parent);
        row.sizeDelta = new Vector2(0, RowH);
        var rowLE = row.gameObject.AddComponent<LayoutElement>();
        rowLE.minHeight = RowH; rowLE.preferredHeight = RowH;
        row.gameObject.AddComponent<Image>().color = Color.clear;
        button = row.gameObject.AddComponent<Button>();
        var bc = button.colors;
        bc.highlightedColor = Hex("#F7F8FA");
        button.colors = bc;

        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment     = TextAnchor.MiddleLeft;
        hlg.childControlWidth  = true;  hlg.childForceExpandWidth  = false;
        hlg.childControlHeight = true;  hlg.childForceExpandHeight = false;
        hlg.spacing = 16f;
        hlg.padding = new RectOffset((int)RowPadX, (int)RowPadX, (int)RowPadY, (int)RowPadY);

        // Label text (left side)
        var labelGo = MakeRect("Label", row);
        var labLE = labelGo.gameObject.AddComponent<LayoutElement>();
        labLE.flexibleWidth = 1;
        var labTxt = MakeTMP("Text", labelGo, fontLabel, FRowLabel, ColTextPrimary, label);
        Stretch(labTxt.rectTransform);

        // Spacer (flexible)
        var spacer = MakeRect("Spacer", row);
        var spLE = spacer.gameObject.AddComponent<LayoutElement>();
        spLE.flexibleWidth = 1;

        // Platform icon+label groups (WhatsApp and Telegram, each toggled independently)
        whatsappIcon = null;
        telegramIcon = null;
        whatsappGroup = null;
        telegramGroup = null;
        plusSeparator = null;
        if (addPlatformIcon)
        {
            var iconContainer = MakeRect("PlatformIcon", row);
            var containerLE = iconContainer.gameObject.AddComponent<LayoutElement>();
            containerLE.minHeight = IconSize; containerLE.preferredHeight = IconSize;
            containerLE.flexibleWidth = 0;
            var iconHlg = iconContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            iconHlg.childAlignment = TextAnchor.MiddleRight;
            iconHlg.childControlWidth = false; iconHlg.childForceExpandWidth = false;
            iconHlg.childControlHeight = false; iconHlg.childForceExpandHeight = false;
            iconHlg.spacing = 12f;
            var iconCsf = iconContainer.gameObject.AddComponent<ContentSizeFitter>();
            iconCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            whatsappGroup = BuildPlatformGroup(iconContainer, "WhatsappGroup", "WhatsApp",
                new Color32(37, 211, 102, 255), fontValue, out whatsappIcon);
            whatsappGroup.SetActive(false);

            // Plus separator (shown only when both platforms selected)
            var plusGo = MakeRect("PlusSeparator", iconContainer);
            var plusLE = plusGo.gameObject.AddComponent<LayoutElement>();
            plusLE.minHeight = IconSize; plusLE.preferredHeight = IconSize;
            var plusTmp = plusGo.gameObject.AddComponent<TextMeshProUGUI>();
            plusTmp.font = fontValue;
            plusTmp.fontSize = FRowValue;
            plusTmp.color = valueColor;
            plusTmp.text = "+";
            plusTmp.alignment = TextAlignmentOptions.Midline;
            plusTmp.enableWordWrapping = false;
            plusTmp.raycastTarget = false;
            var plusCsf = plusGo.gameObject.AddComponent<ContentSizeFitter>();
            plusCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            plusSeparator = plusGo.gameObject;
            plusSeparator.SetActive(false);

            telegramGroup = BuildPlatformGroup(iconContainer, "TelegramGroup", "Telegram",
                new Color32(42, 171, 238, 255), fontValue, out telegramIcon);
            telegramGroup.SetActive(false);
        }

        // Value text (right side)
        var valGo = MakeRect("ValueText", row);
        var valLE = valGo.gameObject.AddComponent<LayoutElement>();
        valLE.flexibleWidth = 1;
        valLE.preferredWidth = 350f;
        valueText = valGo.gameObject.AddComponent<TextMeshProUGUI>();
        valueText.font = fontValue;
        valueText.fontSize = FRowValue;
        valueText.color = valueColor;
        valueText.text = valuePlaceholder;
        valueText.enableWordWrapping = false;
        valueText.overflowMode = TextOverflowModes.Ellipsis;
        valueText.raycastTarget = false;
        valueText.alignment = TextAlignmentOptions.MidlineRight;

        // Chevron (right arrow ›)
        var chevGo = MakeRect("Chevron", row);
        var chLE = chevGo.gameObject.AddComponent<LayoutElement>();
        chLE.minWidth = ChevronW + 20f; chLE.preferredWidth = ChevronW + 20f;
        var chTxt = MakeTMP("Text", chevGo, fontValue, FChevron, ColTextTert, "›");
        Stretch(chTxt.rectTransform);
        chTxt.alignment = TextAlignmentOptions.Center;

        // Separator (1px line at bottom of row)
        if (addDivider)
        {
            var div = MakeRect("Separator", parent);
            div.sizeDelta = new Vector2(0, DividerH);
            var divLE = div.gameObject.AddComponent<LayoutElement>();
            divLE.minHeight = DividerH; divLE.preferredHeight = DividerH;
            div.gameObject.AddComponent<Image>().color = ColBorder;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Platform selector popup
    // ══════════════════════════════════════════════════════════════════════
    private static GameObject BuildPlatformSelector(RectTransform pageRoot,
        TMP_FontAsset fontBold, TMP_FontAsset fontSemi,
        out Button waBtn, out Button tgBtn, out Button bothBtn)
    {
        var overlay = MakeRect("PlatformSelectorPanel", pageRoot);
        Stretch(overlay);
        overlay.SetAsLastSibling();
        overlay.gameObject.AddComponent<Image>().color = ColOverlay;
        // Tap overlay background to dismiss
        var overlayBtn = overlay.gameObject.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlay.gameObject.SetActive(false);

        // White card
        var card = MakeRect("Content", overlay);
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(PopupCardW, 10f);
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.sprite = RoundSprite; cardImg.type = Image.Type.Sliced; cardImg.color = ColWhite;

        var cvlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        cvlg.childControlWidth = true; cvlg.childForceExpandWidth = true;
        cvlg.childControlHeight = false; cvlg.childForceExpandHeight = false;
        cvlg.spacing = OptionSpacing;
        cvlg.padding = new RectOffset((int)PopupPadX, (int)PopupPadX,
                                      (int)PopupPadY, (int)PopupPadY);
        var ccsf = card.gameObject.AddComponent<ContentSizeFitter>();
        ccsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Close button (top-right, absolute-positioned, ignored by layout)
        BuildCloseButton(card, fontBold);

        // Title
        var titleTxt = MakeTMP("Title", card, fontBold, FPopupTitle, ColTextPrimary, "Платформа");
        titleTxt.rectTransform.sizeDelta = new Vector2(0, PopupTitleH);
        LE(titleTxt.rectTransform, PopupTitleH);
        titleTxt.alignment = TextAlignmentOptions.Center;

        // Options container
        var options = MakeRect("Options", card);
        var optVlg = options.gameObject.AddComponent<VerticalLayoutGroup>();
        optVlg.childControlWidth = true; optVlg.childForceExpandWidth = true;
        optVlg.childControlHeight = false; optVlg.childForceExpandHeight = false;
        optVlg.spacing = OptionSpacing;
        var optCsf = options.gameObject.AddComponent<ContentSizeFitter>();
        optCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        waBtn   = BuildColorButton(options, "WhatsAppOption", fontSemi, "WhatsApp", ColWaGreen);
        tgBtn   = BuildColorButton(options, "TelegramOption", fontSemi, "Telegram", ColTgBlue);
        bothBtn = BuildColorButton(options, "BothOption", fontSemi, "WhatsApp + Telegram", ColIosBlue);

        return overlay.gameObject;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Business selector popup (empty — buttons reparented via Inspector)
    // ══════════════════════════════════════════════════════════════════════
    private static GameObject BuildBusinessSelector(RectTransform pageRoot, TMP_FontAsset fontBold)
    {
        var overlay = MakeRect("BusinessSelectorPanel", pageRoot);
        Stretch(overlay);
        overlay.SetAsLastSibling();
        overlay.gameObject.AddComponent<Image>().color = ColOverlay;
        // Tap overlay background to dismiss
        var overlayBtn = overlay.gameObject.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlay.gameObject.SetActive(false);

        var card = MakeRect("Content", overlay);
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(PopupCardW, 10f);
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.sprite = RoundSprite; cardImg.type = Image.Type.Sliced; cardImg.color = ColWhite;

        var cvlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        cvlg.childControlWidth = true; cvlg.childForceExpandWidth = true;
        cvlg.childControlHeight = false; cvlg.childForceExpandHeight = false;
        cvlg.spacing = PopupSpacing;
        cvlg.padding = new RectOffset((int)PopupPadX, (int)PopupPadX,
                                      (int)PopupPadY, (int)PopupPadY);
        var ccsf = card.gameObject.AddComponent<ContentSizeFitter>();
        ccsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Close button (top-right, absolute-positioned, ignored by layout)
        BuildCloseButton(card, fontBold);

        var titleTxt = MakeTMP("Title", card, fontBold, FPopupTitle, ColTextPrimary, "Тип бизнеса");
        titleTxt.rectTransform.sizeDelta = new Vector2(0, PopupTitleH);
        LE(titleTxt.rectTransform, PopupTitleH);
        titleTxt.alignment = TextAlignmentOptions.Center;

        // BusinessTypesList buttons will be reparented here via Inspector
        return overlay.gameObject;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Input popup (bot name / description)
    // ══════════════════════════════════════════════════════════════════════
    private static GameObject BuildInputPopup(RectTransform pageRoot, string name,
        TMP_FontAsset fontBold, TMP_FontAsset fontReg, TMP_FontAsset fontSemi,
        string title, string placeholder, bool multiline,
        out TMP_InputField inputField)
    {
        var overlay = MakeRect(name, pageRoot);
        Stretch(overlay);
        overlay.SetAsLastSibling();
        overlay.gameObject.AddComponent<Image>().color = ColOverlay;
        // Tap overlay background to dismiss
        var overlayBtn = overlay.gameObject.AddComponent<Button>();
        overlayBtn.transition = Selectable.Transition.None;
        overlay.gameObject.SetActive(false);

        var card = MakeRect("Content", overlay);
        card.anchorMin = new Vector2(0.5f, 0.5f);
        card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(PopupCardW, 10f);
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.sprite = RoundSprite; cardImg.type = Image.Type.Sliced; cardImg.color = ColWhite;

        var cvlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        cvlg.childControlWidth = true; cvlg.childForceExpandWidth = true;
        cvlg.childControlHeight = false; cvlg.childForceExpandHeight = false;
        cvlg.spacing = PopupSpacing;
        cvlg.padding = new RectOffset((int)PopupPadX, (int)PopupPadX,
                                      (int)PopupPadY, (int)PopupPadY);
        var ccsf = card.gameObject.AddComponent<ContentSizeFitter>();
        ccsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Close button (top-right, absolute-positioned, ignored by layout)
        BuildCloseButton(card, fontBold);

        // Title
        var titleTxt = MakeTMP("Title", card, fontBold, FPopupTitle, ColTextPrimary, title);
        titleTxt.rectTransform.sizeDelta = new Vector2(0, PopupTitleH);
        LE(titleTxt.rectTransform, PopupTitleH);
        titleTxt.alignment = TextAlignmentOptions.Center;

        // Input field
        float inputH = multiline ? 277f : PopupInputH; // 100pt × S ≈ 277 for multiline
        inputField = BuildTMPInput("InputField", card, fontReg, placeholder, inputH, multiline);

        // Confirm button
        var confirmGo = MakeRect("ConfirmButton", card);
        confirmGo.sizeDelta = new Vector2(0, PopupBtnH);
        LE(confirmGo, PopupBtnH);
        var cfImg = confirmGo.gameObject.AddComponent<Image>();
        cfImg.sprite = RoundSprite; cfImg.type = Image.Type.Sliced; cfImg.color = ColIosBlue;
        confirmGo.gameObject.AddComponent<Button>();
        var cfTxt = MakeTMP("Label", confirmGo, fontSemi, FPopupBtn, ColWhite, "Готово");
        Stretch(cfTxt.rectTransform); cfTxt.alignment = TextAlignmentOptions.Center;

        // Cancel button
        var cancelGo = MakeRect("CancelButton", card);
        cancelGo.sizeDelta = new Vector2(0, PopupBtnH);
        LE(cancelGo, PopupBtnH);
        var cnImg = cancelGo.gameObject.AddComponent<Image>();
        cnImg.sprite = RoundSprite; cnImg.type = Image.Type.Sliced; cnImg.color = ColBg;
        cancelGo.gameObject.AddComponent<Button>();
        var cnTxt = MakeTMP("Label", cancelGo, fontSemi, FPopupBtn, ColIosBlue, "Отмена");
        Stretch(cnTxt.rectTransform); cnTxt.alignment = TextAlignmentOptions.Center;

        return overlay.gameObject;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TMP_InputField builder
    // ══════════════════════════════════════════════════════════════════════
    private static TMP_InputField BuildTMPInput(string name, RectTransform parent,
        TMP_FontAsset font, string placeholder, float height, bool multiline)
    {
        var container = MakeRect(name, parent);
        container.sizeDelta = new Vector2(0, height);
        LE(container, height);
        var cImg = container.gameObject.AddComponent<Image>();
        cImg.sprite = RoundSprite; cImg.type = Image.Type.Sliced; cImg.color = ColBg;

        var field = container.gameObject.AddComponent<TMP_InputField>();
        if (multiline)
        {
            field.lineType = TMP_InputField.LineType.MultiLineNewline;
        }

        // Text Area (required child with RectTransform for textViewport)
        var area = MakeRect("Text Area", container);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(30, 8); area.offsetMax = new Vector2(-30, -8);
        area.gameObject.AddComponent<RectMask2D>();

        // Placeholder TMP
        var ph = MakeTMP("Placeholder", area, font, FPopupInput, ColTextSec, placeholder);
        Stretch(ph.rectTransform);
        ph.fontStyle = FontStyles.Italic;
        ph.enableWordWrapping = multiline;

        // Text TMP
        var txt = MakeTMP("Text", area, font, FPopupInput, ColTextPrimary, "");
        Stretch(txt.rectTransform);
        txt.enableWordWrapping = multiline;

        field.textViewport  = area;
        field.textComponent = txt;
        field.placeholder   = ph;

        return field;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Colored option button (for platform selector)
    // ══════════════════════════════════════════════════════════════════════
    private static Button BuildColorButton(RectTransform parent, string name,
        TMP_FontAsset font, string label, Color bgColor)
    {
        var go = MakeRect(name, parent);
        go.sizeDelta = new Vector2(0, OptionBtnH);
        LE(go, OptionBtnH);
        var img = go.gameObject.AddComponent<Image>();
        img.sprite = RoundSprite; img.type = Image.Type.Sliced; img.color = bgColor;
        var btn = go.gameObject.AddComponent<Button>();
        var bc = btn.colors;
        bc.highlightedColor = bgColor * 0.88f;
        btn.colors = bc;
        var txt = MakeTMP("Label", go, font, FPopupBtn, ColWhite, label);
        Stretch(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Platform group (icon + colored label, horizontal)
    // ══════════════════════════════════════════════════════════════════════
    private static GameObject BuildPlatformGroup(RectTransform parent, string name,
        string label, Color labelColor, TMP_FontAsset font, out Image icon)
    {
        var group = MakeRect(name, parent);
        var groupLE = group.gameObject.AddComponent<LayoutElement>();
        groupLE.minHeight = IconSize; groupLE.preferredHeight = IconSize;
        var hlg = group.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false; hlg.childForceExpandWidth = false;
        hlg.childControlHeight = false; hlg.childForceExpandHeight = false;
        hlg.spacing = 8f;
        var csf = group.gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        var iconGo = MakeRect("Icon", group);
        var icLE = iconGo.gameObject.AddComponent<LayoutElement>();
        icLE.minWidth = IconSize; icLE.preferredWidth = IconSize;
        icLE.minHeight = IconSize; icLE.preferredHeight = IconSize;
        icon = iconGo.gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.color = Color.white;

        var labelGo = MakeRect("Label", group);
        var lblLE = labelGo.gameObject.AddComponent<LayoutElement>();
        lblLE.minHeight = IconSize; lblLE.preferredHeight = IconSize;
        var lblTmp = labelGo.gameObject.AddComponent<TextMeshProUGUI>();
        lblTmp.font = font;
        lblTmp.fontSize = FRowValue;
        lblTmp.color = labelColor;
        lblTmp.text = label;
        lblTmp.alignment = TextAlignmentOptions.MidlineLeft;
        lblTmp.enableWordWrapping = false;
        lblTmp.raycastTarget = false;
        var lblCsf = labelGo.gameObject.AddComponent<ContentSizeFitter>();
        lblCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        return group.gameObject;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Close button (top-right of popup cards, named "CloseButton")
    // ══════════════════════════════════════════════════════════════════════
    private static Button BuildCloseButton(RectTransform card, TMP_FontAsset font)
    {
        const float Size = 64f;
        const float Margin = 20f;
        var go = MakeRect("CloseButton", card);
        // Anchor to top-right of card, ignore layout so VLG doesn't push it
        go.anchorMin = new Vector2(1f, 1f);
        go.anchorMax = new Vector2(1f, 1f);
        go.pivot = new Vector2(1f, 1f);
        go.anchoredPosition = new Vector2(-Margin, -Margin);
        go.sizeDelta = new Vector2(Size, Size);
        var ignore = go.gameObject.AddComponent<LayoutElement>();
        ignore.ignoreLayout = true;
        var img = go.gameObject.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0); // invisible hit area
        var btn = go.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        var txt = MakeTMP("Text", go, font, 56f, ColTextTert, "✕");
        Stretch(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;
        go.SetAsLastSibling();
        return btn;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Low-level utilities (matching ProfilePageBuilder pattern)
    // ══════════════════════════════════════════════════════════════════════
    private static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.layer = LayerMask.NameToLayer("UI");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    private static TextMeshProUGUI MakeTMP(string name, RectTransform parent,
        TMP_FontAsset font, float size, Color color, string text)
    {
        var rt = MakeRect(name, parent);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font; tmp.fontSize = size; tmp.color = color; tmp.text = text;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void LE(RectTransform rt, float height)
    {
        var le = rt.gameObject.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height; le.preferredHeight = height;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}
#endif

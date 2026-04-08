// ============================================================
//  ProfilePageBuilder.cs  (Editor-only)
//
//  Menu:  Tools > Build Profile Page UI
//
//  Canvas reference resolution: 1080 × 1920
//  All dimensions are in canvas units scaled for 1080-wide canvas.
//  Design reference: mockup.html PAGE 5 (Profile screen).
// ============================================================

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class ProfilePageBuilder
{
    // ── Fonts ─────────────────────────────────────────────────────────────
    private const string FontRegularPath  = "Assets/TextMesh Pro/Fonts/SFProText-Regular SDF.asset";
    private const string FontBoldPath     = "Assets/TextMesh Pro/Fonts/SFProText-Bold SDF.asset";
    private const string FontMediumPath   = "Assets/TextMesh Pro/Fonts/SFProText-Medium SDF.asset";
    private const string FontSemiboldPath = "Assets/TextMesh Pro/Fonts/SFProText-Semibold SDF.asset";

    // ── Scale: canvas is 1080-wide, mockup designed at 390pt ─────────────
    // All sizes below are already pre-scaled for 1080×1920 canvas units.
    // Design-pt × 2.77 ≈ canvas units (1080/390 = 2.769)

    // Layout
    private const float HeaderH    = 155f;   // header bar
    private const float ContentPadH = 50f;   // top/bottom padding inside Content
    private const float ContentPadX = 44f;   // left/right padding inside Content (screen edge margins)
    private const float SectionGap  = 32f;   // vertical gap between cards in the scroll view

    // Profile card
    private const float CardPad     = 44f;   // padding inside ProfileCard HLG
    private const float CardSpacing = 40f;   // HLG child spacing
    private const float AvatarSize  = 176f;
    private const float EditBtnSize = 110f;

    // Settings rows
    private const float RowH        = 150f;
    private const float RowPadX     = 44f;
    private const float RowPadY     = 27f;
    private const float RowSpacing  = 40f;
    private const float IconSize    = 100f;
    private const float DividerH    = 2f;

    // Section heights (pre-calculated)
    // Section1: 3 rows × 150 + 2 dividers × 2 = 454
    private const float Sec1H = 454f;
    // Section2: 2 rows × 150 + 1 divider × 2 = 302
    private const float Sec2H = 302f;

    // Logout button
    private const float LogoutH = 144f;

    // Fonts
    private const float FHeader   = 48f;
    private const float FAvatar   = 72f;
    private const float FName     = 50f;
    private const float FEmail    = 36f;
    private const float FEditIcon = 50f;
    private const float FGearIcon = 60f;
    private const float FRowIcon  = 46f;
    private const float FRowLabel = 42f;
    private const float FChevron  = 60f;
    private const float FLogout   = 44f;

    // Popups
    private const float PopupCardW    = 900f;
    private const float PopupTitleH   = 70f;
    private const float PopupMsgH     = 110f;
    private const float PopupInputH   = 120f;
    private const float PopupBtnH     = 120f;
    private const float PopupPadX     = 50f;
    private const float PopupPadY     = 60f;
    private const float PopupSpacing  = 30f;
    private const float FPopupTitle   = 48f;
    private const float FPopupMsg     = 40f;
    private const float FPopupInput   = 42f;
    private const float FPopupBtn     = 42f;

    // ── Palette (matches mockup CSS variables) ────────────────────────────
    private static readonly Color ColPrimary     = Hex("#1B7CEB");
    private static readonly Color ColBg          = Hex("#F0F2F5");
    private static readonly Color ColCard        = Color.white;
    private static readonly Color ColText        = Hex("#1A1A2E");
    private static readonly Color ColTextSec     = Hex("#65676B");
    private static readonly Color ColBorder      = Hex("#E4E6EB");
    private static readonly Color ColEditBtnBg   = Hex("#E8F2FD");
    private static readonly Color ColOrange      = Hex("#FF9800");
    private static readonly Color ColGreen       = Hex("#4CAF50");
    private static readonly Color ColPurple      = Hex("#9C27B0");
    private static readonly Color ColSlate       = Hex("#607D8B");
    private static readonly Color ColDanger      = Hex("#E53935");
    private static readonly Color ColDangerLight = Hex("#FFEBEE");
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
    [MenuItem("Tools/Build Profile Page UI")]
    public static void Build()
    {
        // ── 1. Locate Screen_Profile ──────────────────────────────────────
        var root = GameObject.Find("Canvas/ScreenContainer/Screen_Profile");
        if (root == null)
        {
            Debug.LogError("[ProfilePageBuilder] Screen_Profile not found. Expected path: Canvas/ScreenContainer/Screen_Profile");
            return;
        }

        // ── 2. Load fonts ─────────────────────────────────────────────────
        var fontRegular  = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontRegularPath);
        var fontBold     = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBoldPath);
        var fontMedium   = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontMediumPath);
        var fontSemibold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSemiboldPath);

        if (fontRegular == null || fontBold == null)
        {
            Debug.LogError("[ProfilePageBuilder] Could not load SFProText fonts.");
            return;
        }

        var fontMed  = fontMedium  ?? fontSemibold ?? fontBold;
        var fontSemi = fontSemibold ?? fontBold;

        // ── 3. Clear old children ─────────────────────────────────────────
        for (int i = root.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

        var rootImg = root.GetComponent<Image>();
        if (rootImg != null) rootImg.color = ColBg;

        // ── 4. ProfilePage component ──────────────────────────────────────
        var page = root.GetComponent<ProfilePage>() ?? root.AddComponent<ProfilePage>();

        // ── 5. BottomTabManager ───────────────────────────────────────────
        var btm = Object.FindFirstObjectByType<BottomTabManager>();

        // ══════════════════════════════════════════════════════════════════
        // HEADER
        // ══════════════════════════════════════════════════════════════════
        var header = MakeRect("Header", root.transform);
        // Anchor to top, stretch horizontally
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot     = new Vector2(0.5f, 1f);
        header.offsetMin = Vector2.zero;
        header.offsetMax = Vector2.zero;
        header.sizeDelta = new Vector2(0, HeaderH);

        header.gameObject.AddComponent<Image>().color = Color.white;

        // Bottom border
        var hLine = MakeRect("Border", header);
        hLine.anchorMin = new Vector2(0, 0); hLine.anchorMax = new Vector2(1, 0);
        hLine.pivot = new Vector2(0.5f, 0f);
        hLine.sizeDelta = new Vector2(0, DividerH);
        hLine.anchoredPosition = Vector2.zero;
        hLine.gameObject.AddComponent<Image>().color = ColBorder;

        // Title (centered)
        var titleTxt = MakeTMP("Title", header, fontSemi, FHeader, ColText, "Профиль");
        Stretch(titleTxt.rectTransform);
        titleTxt.alignment = TextAlignmentOptions.Center;

        // Settings gear button (top-right)
        var gearBtn = MakeRect("SettingsBtn", header);
        gearBtn.anchorMin = new Vector2(1, 0); gearBtn.anchorMax = new Vector2(1, 1);
        gearBtn.pivot = new Vector2(1f, 0.5f);
        gearBtn.sizeDelta = new Vector2(120f, 0);
        gearBtn.anchoredPosition = new Vector2(-20f, 0);
        gearBtn.gameObject.AddComponent<Image>().color = Color.clear;
        gearBtn.gameObject.AddComponent<Button>();
        var gearTxt = MakeTMP("Icon", gearBtn, fontRegular, FGearIcon, ColTextSec, "⚙");
        Stretch(gearTxt.rectTransform);
        gearTxt.alignment = TextAlignmentOptions.Center;

        // ══════════════════════════════════════════════════════════════════
        // SCROLL VIEW  (fills Screen_Profile below header)
        // ScreenContainer already excludes the nav bar, so no bottom offset.
        // ══════════════════════════════════════════════════════════════════
        var scrollGo = MakeRect("ScrollView", root.transform);
        scrollGo.anchorMin = Vector2.zero;
        scrollGo.anchorMax = Vector2.one;
        scrollGo.pivot = new Vector2(0.5f, 0.5f);
        scrollGo.offsetMin = new Vector2(0, 0);
        scrollGo.offsetMax = new Vector2(0, -HeaderH);

        scrollGo.gameObject.AddComponent<Image>().color = Color.clear;
        var scrollRect = scrollGo.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;

        // Viewport (RectMask2D — no stencil issues)
        var viewport = MakeRect("Viewport", scrollGo);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        scrollRect.viewport = viewport;

        // Content (top-anchored, auto-height via ContentSizeFitter)
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
        vlg.padding = new RectOffset((int)ContentPadX, (int)ContentPadX, (int)ContentPadH, (int)ContentPadH);

        var csf = content.gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = content;

        // ══════════════════════════════════════════════════════════════════
        // PROFILE CARD
        // ══════════════════════════════════════════════════════════════════
        float profileCardH = AvatarSize + CardPad * 2;  // 176 + 88 = 264
        var profileCard = MakeCard("ProfileCard", content, profileCardH);

        var cardHlg = profileCard.gameObject.AddComponent<HorizontalLayoutGroup>();
        cardHlg.childAlignment      = TextAnchor.MiddleLeft;
        cardHlg.childControlWidth   = true;  cardHlg.childForceExpandWidth  = false;
        cardHlg.childControlHeight  = true;  cardHlg.childForceExpandHeight = false;
        cardHlg.spacing = CardSpacing;
        cardHlg.padding = new RectOffset((int)CardPad, (int)CardPad, (int)CardPad, (int)CardPad);

        // Avatar circle
        var avatarBg = MakeRect("AvatarBg", profileCard);
        var avLE = avatarBg.gameObject.AddComponent<LayoutElement>();
        avLE.minWidth = AvatarSize; avLE.preferredWidth  = AvatarSize;
        avLE.minHeight = AvatarSize; avLE.preferredHeight = AvatarSize;
        var avImg = avatarBg.gameObject.AddComponent<Image>();
        avImg.sprite = RoundSprite; avImg.type = Image.Type.Sliced; avImg.color = ColPrimary;
        var avatarTxt = MakeTMP("Initial", avatarBg, fontBold, FAvatar, Color.white, "И");
        Stretch(avatarTxt.rectTransform);
        avatarTxt.alignment = TextAlignmentOptions.Center;

        // Info group (stretches to fill remaining width)
        var infoGo = MakeRect("InfoGroup", profileCard);
        var infoLE = infoGo.gameObject.AddComponent<LayoutElement>();
        infoLE.flexibleWidth = 1; infoLE.minHeight = AvatarSize * 0.6f;
        var infoVlg = infoGo.gameObject.AddComponent<VerticalLayoutGroup>();
        infoVlg.childAlignment = TextAnchor.MiddleLeft;
        infoVlg.childControlWidth  = true;  infoVlg.childForceExpandWidth  = true;
        infoVlg.childControlHeight = true;  infoVlg.childForceExpandHeight = false;
        infoVlg.spacing = 12f;

        var nameTxt  = MakeTMP("NameText",  infoGo, fontBold,    FName,  ColText,    "Иван Петров");
        nameTxt.overflowMode = TextOverflowModes.Ellipsis; nameTxt.enableWordWrapping = false;

        var emailTxt = MakeTMP("EmailText", infoGo, fontRegular, FEmail, ColTextSec, "ivan.petrov@email.com");
        emailTxt.overflowMode = TextOverflowModes.Ellipsis; emailTxt.enableWordWrapping = false;

        // Edit button (fixed size, right-aligned)
        var editBtnGo = MakeRect("EditButton", profileCard);
        var ebLE = editBtnGo.gameObject.AddComponent<LayoutElement>();
        ebLE.minWidth = EditBtnSize; ebLE.preferredWidth = EditBtnSize;
        ebLE.minHeight = EditBtnSize; ebLE.preferredHeight = EditBtnSize;
        var ebImg = editBtnGo.gameObject.AddComponent<Image>();
        ebImg.sprite = RoundSprite; ebImg.type = Image.Type.Sliced; ebImg.color = ColEditBtnBg;
        var editBtn = editBtnGo.gameObject.AddComponent<Button>();
        var ebColors = editBtn.colors;
        ebColors.highlightedColor = Hex("#D0E8FB"); editBtn.colors = ebColors;
        var editIconTxt = MakeTMP("EditIcon", editBtnGo, fontRegular, FEditIcon, ColPrimary, "✎");
        Stretch(editIconTxt.rectTransform); editIconTxt.alignment = TextAlignmentOptions.Center;

        // ══════════════════════════════════════════════════════════════════
        // SECTION 1  (Аккаунт, Уведомления, Приватность)
        // ══════════════════════════════════════════════════════════════════
        var sec1 = MakeCard("Section1", content, Sec1H);
        var s1vlg = sec1.gameObject.AddComponent<VerticalLayoutGroup>();
        s1vlg.childControlWidth = true; s1vlg.childForceExpandWidth = true;
        s1vlg.childControlHeight = false; s1vlg.childForceExpandHeight = false;
        s1vlg.spacing = 0;

        var accountBtn = BuildRow(sec1, fontRegular, fontMed, "Аккаунт",     ColPrimary, "👤", true);
        var notifBtn   = BuildRow(sec1, fontRegular, fontMed, "Уведомления", ColOrange,  "🔔", true);
        var privBtn    = BuildRow(sec1, fontRegular, fontMed, "Приватность", ColGreen,   "🔒", false);

        // ══════════════════════════════════════════════════════════════════
        // SECTION 2  (Поддержка, О приложении)
        // ══════════════════════════════════════════════════════════════════
        var sec2 = MakeCard("Section2", content, Sec2H);
        var s2vlg = sec2.gameObject.AddComponent<VerticalLayoutGroup>();
        s2vlg.childControlWidth = true; s2vlg.childForceExpandWidth = true;
        s2vlg.childControlHeight = false; s2vlg.childForceExpandHeight = false;
        s2vlg.spacing = 0;

        var supportBtn = BuildRow(sec2, fontRegular, fontMed, "Поддержка",    ColPurple, "❓", true);
        var aboutBtn   = BuildRow(sec2, fontRegular, fontMed, "О приложении", ColSlate,  "ℹ",  false);

        // ══════════════════════════════════════════════════════════════════
        // LOGOUT BUTTON
        // ══════════════════════════════════════════════════════════════════
        var logoutGo = MakeRect("LogoutButton", content);
        logoutGo.sizeDelta = new Vector2(0, LogoutH);
        var loLE = logoutGo.gameObject.AddComponent<LayoutElement>();
        loLE.minHeight = LogoutH; loLE.preferredHeight = LogoutH;
        var loImg = logoutGo.gameObject.AddComponent<Image>();
        loImg.sprite = RoundSprite; loImg.type = Image.Type.Sliced; loImg.color = ColDangerLight;
        var logoutBtn = logoutGo.gameObject.AddComponent<Button>();
        var loColors = logoutBtn.colors;
        loColors.highlightedColor = Hex("#FFCDD2"); logoutBtn.colors = loColors;
        var logoutTxt = MakeTMP("LogoutText", logoutGo, fontSemi, FLogout, ColDanger, "Выйти");
        Stretch(logoutTxt.rectTransform); logoutTxt.alignment = TextAlignmentOptions.Center;

        // ══════════════════════════════════════════════════════════════════
        // EDIT POPUP
        // ══════════════════════════════════════════════════════════════════
        var editPopupGo = BuildPopup(root.transform, "EditPopup", fontBold, fontRegular, fontSemi,
            "Редактировать профиль",
            out TMP_InputField editNameField,
            out TMP_InputField editEmailField,
            out Button editSaveBtn,
            out Button editCancelBtn);

        // ══════════════════════════════════════════════════════════════════
        // LOGOUT CONFIRM POPUP
        // ══════════════════════════════════════════════════════════════════
        var logoutPopupGo = BuildConfirmPopup(root.transform, "LogoutPopup", fontBold, fontRegular, fontSemi,
            "Выйти из аккаунта?",
            "Вы уверены? Все боты будут деактивированы.",
            out Button logoutConfirmBtn,
            out Button logoutCancelBtn);

        // ══════════════════════════════════════════════════════════════════
        // WIRE SerializeField REFERENCES
        // ══════════════════════════════════════════════════════════════════
        var so = new SerializedObject(page);
        so.FindProperty("avatarInitialText").objectReferenceValue       = avatarTxt;
        so.FindProperty("nameText").objectReferenceValue                = nameTxt;
        so.FindProperty("emailText").objectReferenceValue               = emailTxt;
        so.FindProperty("editButton").objectReferenceValue              = editBtn;
        so.FindProperty("accountButton").objectReferenceValue           = accountBtn;
        so.FindProperty("notificationsButton").objectReferenceValue     = notifBtn;
        so.FindProperty("privacyButton").objectReferenceValue           = privBtn;
        so.FindProperty("supportButton").objectReferenceValue           = supportBtn;
        so.FindProperty("aboutButton").objectReferenceValue             = aboutBtn;
        so.FindProperty("logoutButton").objectReferenceValue            = logoutBtn;
        so.FindProperty("editPopup").objectReferenceValue               = editPopupGo;
        so.FindProperty("editNameInput").objectReferenceValue           = editNameField;
        so.FindProperty("editEmailInput").objectReferenceValue          = editEmailField;
        so.FindProperty("editSaveButton").objectReferenceValue          = editSaveBtn;
        so.FindProperty("editCancelButton").objectReferenceValue        = editCancelBtn;
        so.FindProperty("logoutPopup").objectReferenceValue             = logoutPopupGo;
        so.FindProperty("logoutConfirmButton").objectReferenceValue     = logoutConfirmBtn;
        so.FindProperty("logoutCancelButton").objectReferenceValue      = logoutCancelBtn;
        if (btm != null)
            so.FindProperty("bottomTabManager").objectReferenceValue    = btm;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[ProfilePageBuilder] ✅ Done — save the scene (Ctrl+S).");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Row builder
    // ══════════════════════════════════════════════════════════════════════
    private static Button BuildRow(RectTransform parent,
        TMP_FontAsset fontReg, TMP_FontAsset fontMed,
        string label, Color iconColor, string iconChar, bool addDivider)
    {
        // Row container (= the button)
        var row = MakeRect(label + "Row", parent);
        row.sizeDelta = new Vector2(0, RowH);
        var rowLE = row.gameObject.AddComponent<LayoutElement>();
        rowLE.minHeight = RowH; rowLE.preferredHeight = RowH;
        row.gameObject.AddComponent<Image>().color = Color.clear;
        var btn = row.gameObject.AddComponent<Button>();
        var bc = btn.colors; bc.highlightedColor = Hex("#F7F8FA"); btn.colors = bc;

        var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment     = TextAnchor.MiddleLeft;
        hlg.childControlWidth  = true;  hlg.childForceExpandWidth  = false;
        hlg.childControlHeight = true;  hlg.childForceExpandHeight = false;
        hlg.spacing = RowSpacing;
        hlg.padding = new RectOffset((int)RowPadX, (int)RowPadX, (int)RowPadY, (int)RowPadY);

        // Coloured icon circle
        var iconBg = MakeRect("IconBg", row);
        var icLE = iconBg.gameObject.AddComponent<LayoutElement>();
        icLE.minWidth = IconSize; icLE.preferredWidth = IconSize;
        icLE.minHeight = IconSize; icLE.preferredHeight = IconSize;
        var icImg = iconBg.gameObject.AddComponent<Image>();
        icImg.sprite = RoundSprite; icImg.type = Image.Type.Sliced; icImg.color = iconColor;
        var icTxt = MakeTMP("Icon", iconBg, fontReg, FRowIcon, Color.white, iconChar);
        Stretch(icTxt.rectTransform); icTxt.alignment = TextAlignmentOptions.Center;

        // Label (expands)
        var labelGo = MakeRect("Label", row);
        var labLE = labelGo.gameObject.AddComponent<LayoutElement>();
        labLE.flexibleWidth = 1;
        var labTxt = MakeTMP("Text", labelGo, fontMed, FRowLabel, ColText, label);
        Stretch(labTxt.rectTransform);

        // Chevron (fixed)
        var chevGo = MakeRect("Chevron", row);
        var chLE = chevGo.gameObject.AddComponent<LayoutElement>();
        chLE.minWidth = 50f; chLE.preferredWidth = 50f;
        var chTxt = MakeTMP("Text", chevGo, fontReg, FChevron, ColTextSec, "›");
        Stretch(chTxt.rectTransform); chTxt.alignment = TextAlignmentOptions.Center;

        // Divider
        if (addDivider)
        {
            var div = MakeRect("Divider", parent);
            div.sizeDelta = new Vector2(0, DividerH);
            var divLE = div.gameObject.AddComponent<LayoutElement>();
            divLE.minHeight = DividerH; divLE.preferredHeight = DividerH;
            var divImg = div.gameObject.AddComponent<Image>();
            divImg.color = ColBorder;
        }

        return btn;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Edit popup (with two input fields)
    // ══════════════════════════════════════════════════════════════════════
    private static GameObject BuildPopup(Transform parent, string name,
        TMP_FontAsset fontBold, TMP_FontAsset fontReg, TMP_FontAsset fontSemi,
        string title,
        out TMP_InputField nameField, out TMP_InputField emailField,
        out Button saveBtn, out Button cancelBtn)
    {
        var overlay = MakeRect(name, parent);
        Stretch(overlay); overlay.SetAsLastSibling();
        overlay.gameObject.AddComponent<Image>().color = ColOverlay;
        overlay.gameObject.SetActive(false);

        var card = MakeRect("Card", overlay);
        card.anchorMin = new Vector2(0.5f, 0.5f); card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(PopupCardW, 10f); // width fixed; height by CSF
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.sprite = RoundSprite; cardImg.type = Image.Type.Sliced; cardImg.color = Color.white;

        var cvlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        cvlg.childControlWidth = true; cvlg.childForceExpandWidth = true;
        cvlg.childControlHeight = false; cvlg.childForceExpandHeight = false;
        cvlg.spacing = PopupSpacing;
        cvlg.padding = new RectOffset((int)PopupPadX, (int)PopupPadX, (int)PopupPadY, (int)PopupPadY);
        var ccsf = card.gameObject.AddComponent<ContentSizeFitter>();
        ccsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Title
        var titleTxt = MakeTMP("Title", card, fontBold, FPopupTitle, ColText, title);
        titleTxt.rectTransform.sizeDelta = new Vector2(0, PopupTitleH);
        LE(titleTxt.rectTransform, PopupTitleH);
        titleTxt.alignment = TextAlignmentOptions.Center;

        nameField  = BuildInput("NameInput",  card, fontReg, "Имя");
        emailField = BuildInput("EmailInput", card, fontReg, "Email");

        // Button row
        var btnRow = MakeRect("ButtonRow", card);
        btnRow.sizeDelta = new Vector2(0, PopupBtnH);
        LE(btnRow, PopupBtnH);
        var bhlg = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        bhlg.childControlWidth = true; bhlg.childForceExpandWidth = true;
        bhlg.childControlHeight = false; bhlg.childForceExpandHeight = false;
        bhlg.spacing = 24f;

        cancelBtn = BuildDialogBtn(btnRow, fontSemi, "Отмена",    ColTextSec, ColBorder);
        saveBtn   = BuildDialogBtn(btnRow, fontSemi, "Сохранить", Color.white, ColPrimary);

        return overlay.gameObject;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Logout confirm popup
    // ══════════════════════════════════════════════════════════════════════
    private static GameObject BuildConfirmPopup(Transform parent, string name,
        TMP_FontAsset fontBold, TMP_FontAsset fontReg, TMP_FontAsset fontSemi,
        string title, string message,
        out Button confirmBtn, out Button cancelBtn)
    {
        var overlay = MakeRect(name, parent);
        Stretch(overlay); overlay.SetAsLastSibling();
        overlay.gameObject.AddComponent<Image>().color = ColOverlay;
        overlay.gameObject.SetActive(false);

        var card = MakeRect("Card", overlay);
        card.anchorMin = new Vector2(0.5f, 0.5f); card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(PopupCardW, 10f);
        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.sprite = RoundSprite; cardImg.type = Image.Type.Sliced; cardImg.color = Color.white;

        var cvlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        cvlg.childControlWidth = true; cvlg.childForceExpandWidth = true;
        cvlg.childControlHeight = false; cvlg.childForceExpandHeight = false;
        cvlg.spacing = PopupSpacing;
        cvlg.padding = new RectOffset((int)PopupPadX, (int)PopupPadX, (int)PopupPadY, (int)PopupPadY);
        var ccsf = card.gameObject.AddComponent<ContentSizeFitter>();
        ccsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var titleTxt = MakeTMP("Title", card, fontBold, FPopupTitle, ColText, title);
        titleTxt.rectTransform.sizeDelta = new Vector2(0, PopupTitleH);
        LE(titleTxt.rectTransform, PopupTitleH);
        titleTxt.alignment = TextAlignmentOptions.Center;

        var msgTxt = MakeTMP("Message", card, fontReg, FPopupMsg, ColTextSec, message);
        msgTxt.rectTransform.sizeDelta = new Vector2(0, PopupMsgH);
        LE(msgTxt.rectTransform, PopupMsgH);
        msgTxt.alignment = TextAlignmentOptions.Center;
        msgTxt.enableWordWrapping = true;

        var btnRow = MakeRect("ButtonRow", card);
        btnRow.sizeDelta = new Vector2(0, PopupBtnH);
        LE(btnRow, PopupBtnH);
        var bhlg = btnRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        bhlg.childControlWidth = true; bhlg.childForceExpandWidth = true;
        bhlg.childControlHeight = false; bhlg.childForceExpandHeight = false;
        bhlg.spacing = 24f;

        cancelBtn  = BuildDialogBtn(btnRow, fontSemi, "Отмена", ColTextSec, ColBorder);
        confirmBtn = BuildDialogBtn(btnRow, fontSemi, "Выйти",  Color.white, ColDanger);

        return overlay.gameObject;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TMP_InputField
    // ══════════════════════════════════════════════════════════════════════
    private static TMP_InputField BuildInput(string name, RectTransform parent,
        TMP_FontAsset font, string placeholder)
    {
        var container = MakeRect(name, parent);
        container.sizeDelta = new Vector2(0, PopupInputH);
        LE(container, PopupInputH);
        var cImg = container.gameObject.AddComponent<Image>();
        cImg.sprite = RoundSprite; cImg.type = Image.Type.Sliced; cImg.color = ColBg;

        var field = container.gameObject.AddComponent<TMP_InputField>();

        var area = MakeRect("Text Area", container);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(30, 8); area.offsetMax = new Vector2(-30, -8);
        area.gameObject.AddComponent<RectMask2D>();

        var ph = MakeTMP("Placeholder", area, font, FPopupInput, ColTextSec, placeholder);
        Stretch(ph.rectTransform); ph.fontStyle = FontStyles.Italic; ph.enableWordWrapping = false;

        var txt = MakeTMP("Text", area, font, FPopupInput, ColText, "");
        Stretch(txt.rectTransform); txt.enableWordWrapping = false;

        field.textViewport  = area;
        field.textComponent = txt;
        field.placeholder   = ph;

        return field;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Dialog button
    // ══════════════════════════════════════════════════════════════════════
    private static Button BuildDialogBtn(RectTransform parent, TMP_FontAsset font,
        string label, Color textColor, Color bgColor)
    {
        var go = MakeRect(label + "Btn", parent);
        go.sizeDelta = new Vector2(0, PopupBtnH);
        LE(go, PopupBtnH);
        var img = go.gameObject.AddComponent<Image>();
        img.sprite = RoundSprite; img.type = Image.Type.Sliced; img.color = bgColor;
        var btn = go.gameObject.AddComponent<Button>();
        var bc = btn.colors; bc.highlightedColor = bgColor * 0.88f; btn.colors = bc;
        var txt = MakeTMP("Label", go, font, FPopupBtn, textColor, label);
        Stretch(txt.rectTransform); txt.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Card helper
    // ══════════════════════════════════════════════════════════════════════
    private static RectTransform MakeCard(string name, RectTransform parent, float height)
    {
        var rt = MakeRect(name, parent);
        rt.sizeDelta = new Vector2(0, height);
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height; le.preferredHeight = height;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = RoundSprite; img.type = Image.Type.Sliced; img.color = ColCard;
        return rt;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Low-level utilities
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

    /// <summary>Stretch-fill (anchor 0,0→1,1, all offsets 0).</summary>
    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    /// <summary>Add a LayoutElement with a fixed preferred height.</summary>
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

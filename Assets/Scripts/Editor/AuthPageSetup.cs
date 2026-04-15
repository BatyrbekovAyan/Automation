// ============================================================
//  AuthPageSetup.cs  (Editor-only)
//
//  Menu:  Tools > Setup Auth Pages
//
//  Canvas reference resolution: 1080 × 1920
//  All dimensions are in canvas units scaled for 1080-wide canvas.
//  Design reference: mockup.html PAGE 9 — WhatsApp & Telegram auth.
//
//  CRITICAL: Manager.cs accesses panel children by index:
//    WhatsApp CodePanel: GetChild(3)=PhoneInputGroup, GetChild(4)=CodeDisplay, GetChild(5)=CodeInstruction
//    Telegram CodePanel: GetChild(3)=PhoneInputGroup, GetChild(4)=CodeInputLabel
//  QR StatusText is inside QRContainer (serialized field, no index dependency).
//  Do NOT add/remove children before these indices without updating Manager.cs.
//
//  QR auto-loads when auth page opens (ShowWhatsappAuth/ShowTelegramAuth).
//  No overlay buttons — panels are directly interactive.
// ============================================================

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AuthPageSetup
{
    // ── Fonts ─────────────────────────────────────────────────────────────
    private const string FontRegularPath  = "Assets/TextMesh Pro/Fonts/SFProText-Regular SDF.asset";
    private const string FontBoldPath     = "Assets/TextMesh Pro/Fonts/SFProText-Bold SDF.asset";
    private const string FontMediumPath   = "Assets/TextMesh Pro/Fonts/SFProText-Medium SDF.asset";
    private const string FontSemiboldPath = "Assets/TextMesh Pro/Fonts/SFProText-Semibold SDF.asset";

    // ── Scale: canvas is 1080-wide, design at 390pt ─────────────────────
    // Design-pt × 2.77 ≈ canvas units (1080/390 = 2.769)
    // All constants below are pre-multiplied, so S isn't used at runtime.

    // Layout
    private const float HeaderH       = 139f;
    private const float ContentPadX   = 55f;
    private const float ContentPadTop = 66f;
    private const float ContentPadBot = 44f;

    // QR container
    private const float QRContainerSize = 609f;
    private const float QRImageSize     = 498f;

    // Phone input / buttons
    private const float InputH        = 138f;
    private const float InputPadX     = 39f;
    private const float BtnH          = 138f;
    private const float FBtn          = 47f;
    private const float PrefixW       = 140f;

    // Typography
    private const float FH3           = 50f;
    private const float FBody         = 39f;
    private const float FDivider      = 36f;
    private const float FInput        = 44f;
    private const float FBackBtn      = 60f;

    // Spacing
    private const float DividerH      = 2f;
    // VLG spacing used inside QR and Code panels — gives baseline gap
    // between children; extra gaps handled by LE height padding on text elements
    private const float PanelSpacing  = 22f;

    // ── Palette ───────────────────────────────────────────────────────────
    private static readonly Color ColBg          = Hex("#F2F2F7");
    private static readonly Color ColWhite       = Color.white;
    private static readonly Color ColTextPrimary = Hex("#1C1C1E");
    private static readonly Color ColTextSec     = Hex("#8E8E93");
    private static readonly Color ColTextTert    = Hex("#C7C7CC");
    private static readonly Color ColBorder      = Hex("#E5E5EA");
    private static readonly Color ColIosBlue     = Hex("#007AFF");
    private static readonly Color ColWaGreen     = Hex("#25D366");
    private static readonly Color ColTgBlue      = Hex("#2AABEE");

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
    [MenuItem("Tools/Setup Auth Pages")]
    public static void Build()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) { Debug.LogError("[AuthPageSetup] No Canvas found."); return; }

        var manager = Object.FindFirstObjectByType<Manager>();
        if (manager == null) { Debug.LogError("[AuthPageSetup] No Manager found."); return; }

        var fontRegular  = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontRegularPath);
        var fontBold     = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBoldPath);
        var fontMedium   = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontMediumPath);
        var fontSemibold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSemiboldPath);
        if (fontRegular == null || fontBold == null)
        { Debug.LogError("[AuthPageSetup] Could not load SFProText fonts."); return; }

        var fontMed  = fontMedium ?? fontSemibold ?? fontBold;
        var fontSemi = fontSemibold ?? fontBold;

        // Delete existing
        DeleteExisting(canvas.transform, "WhatsappAuth");
        DeleteExisting(canvas.transform, "TelegramAuth");

        // Build both pages
        BuildAuthPage(canvas.transform, manager, fontRegular, fontBold, fontMed, fontSemi, true);
        BuildAuthPage(canvas.transform, manager, fontRegular, fontBold, fontMed, fontSemi, false);

        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        Debug.Log("[AuthPageSetup] Done — save the scene (Ctrl+S / Cmd+S).");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Build one auth page (WhatsApp or Telegram)
    // ══════════════════════════════════════════════════════════════════════
    private static void BuildAuthPage(Transform canvasTransform, Manager manager,
        TMP_FontAsset fontRegular, TMP_FontAsset fontBold,
        TMP_FontAsset fontMed, TMP_FontAsset fontSemi, bool isWa)
    {
        string pageName   = isWa ? "WhatsappAuth" : "TelegramAuth";
        string titleText  = isWa ? "WhatsApp" : "Telegram";
        var platformColor = isWa ? ColWaGreen : ColTgBlue;

        string qrBodyText = isWa
            ? "Откройте WhatsApp на телефоне,\nперейдите в Связанные устройства\nи отсканируйте код."
            : "Откройте Telegram на телефоне,\nперейдите в Настройки > Устройства\nи отсканируйте код.";

        // ── Root ──────────────────────────────────────────────────────────
        var root = MakeRect(pageName, canvasTransform);
        Stretch(root);
        root.gameObject.AddComponent<Image>().color = ColBg;
        root.gameObject.AddComponent<LayoutRebuildOnEnable>();
        root.gameObject.AddComponent<KeyboardScrollFix>();
        root.gameObject.SetActive(false);

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

        // Back button (left side)
        var backBtnGo = MakeRect("BackButton", header);
        backBtnGo.anchorMin = new Vector2(0, 0);
        backBtnGo.anchorMax = new Vector2(0, 1);
        backBtnGo.pivot = new Vector2(0, 0.5f);
        backBtnGo.anchoredPosition = new Vector2(10f, 0);
        backBtnGo.sizeDelta = new Vector2(100f, 0);
        backBtnGo.gameObject.AddComponent<Image>().color = Color.clear;
        var backBtn = backBtnGo.gameObject.AddComponent<Button>();
        backBtn.transition = Selectable.Transition.None;
        var backTxt = MakeTMP("Text", backBtnGo, fontSemi, FBackBtn, platformColor, "‹");
        Stretch(backTxt.rectTransform);
        backTxt.alignment = TextAlignmentOptions.MidlineLeft;
        backTxt.margin = new Vector4(20f, 0, 0, 0);

        // Header title
        var headerTitle = MakeTMP("HeaderTitle", header, fontSemi, FH3, ColTextPrimary, titleText);
        Stretch(headerTitle.rectTransform);
        headerTitle.alignment = TextAlignmentOptions.Center;
        headerTitle.margin = new Vector4(0, 24f, 0, 0);

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

        var viewport = MakeRect("Viewport", scrollGo);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        scrollRect.viewport = viewport;

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
        vlg.spacing = 0;
        vlg.padding = new RectOffset((int)ContentPadX, (int)ContentPadX,
                                     (int)ContentPadTop, (int)ContentPadBot);
        var contentCsf = content.gameObject.AddComponent<ContentSizeFitter>();
        contentCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = content;

        // ══════════════════════════════════════════════════════════════════
        // QR PANEL
        //
        // Child index layout:
        //   [0] QRTitle
        //   [1] QRBody
        //   [2] QRContainer  (→ QRCodeImage, StatusText, SuccessOverlay inside)
        //
        // StatusText lives INSIDE QRContainer so toggling it doesn't
        // change the QR panel's VLG height (no layout jump).
        // Manager.cs uses a direct serialized field reference.
        // ══════════════════════════════════════════════════════════════════
        var qrPanel = MakeRect("QRPanel", content);
        var qrVlg = qrPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        qrVlg.childAlignment = TextAnchor.UpperCenter;
        qrVlg.childControlWidth  = true;  qrVlg.childForceExpandWidth  = true;
        qrVlg.childControlHeight = false; qrVlg.childForceExpandHeight = false;
        qrVlg.spacing = PanelSpacing;
        var qrCsf = qrPanel.gameObject.AddComponent<ContentSizeFitter>();
        qrCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // [0] QR Title
        var qrTitle = MakeTMP("QRTitle", qrPanel, fontBold, FH3, ColTextPrimary,
            "Отсканируйте QR-код");
        qrTitle.alignment = TextAlignmentOptions.Center;
        float qrTitleH = FH3 + 20f;
        qrTitle.rectTransform.sizeDelta = new Vector2(0, qrTitleH);
        LE(qrTitle.rectTransform, qrTitleH);

        // [1] QR Body — extra LE height embeds 33px bottom margin (22 spacing + 33 = 55px gap to container)
        var qrBody = MakeTMP("QRBody", qrPanel, fontRegular, FBody, ColTextSec, qrBodyText);
        qrBody.alignment = TextAlignmentOptions.Center;
        qrBody.enableWordWrapping = true;
        qrBody.lineSpacing = 25f;
        float qrBodyTextH = FBody * 3.5f + 20f;
        float qrBodyH = qrBodyTextH + 33f; // 33 extra so gap to QRContainer = 22+33 = 55
        qrBody.rectTransform.sizeDelta = new Vector2(0, qrBodyH);
        LE(qrBody.rectTransform, qrBodyH);

        // [2] QR Container (white card with RawImage)
        var qrContainer = MakeRect("QRContainer", qrPanel);
        qrContainer.sizeDelta = new Vector2(QRContainerSize, QRContainerSize);
        var qrContainerLE = qrContainer.gameObject.AddComponent<LayoutElement>();
        qrContainerLE.minWidth = QRContainerSize; qrContainerLE.preferredWidth = QRContainerSize;
        qrContainerLE.minHeight = QRContainerSize; qrContainerLE.preferredHeight = QRContainerSize;
        var qrContainerImg = qrContainer.gameObject.AddComponent<Image>();
        qrContainerImg.sprite = RoundSprite;
        qrContainerImg.type = Image.Type.Sliced;
        qrContainerImg.color = ColWhite;

        var qrImageGo = MakeRect("QRCodeImage", qrContainer);
        qrImageGo.anchorMin = new Vector2(0.5f, 0.5f);
        qrImageGo.anchorMax = new Vector2(0.5f, 0.5f);
        qrImageGo.pivot = new Vector2(0.5f, 0.5f);
        qrImageGo.sizeDelta = new Vector2(QRImageSize, QRImageSize);
        var qrRawImage = qrImageGo.gameObject.AddComponent<RawImage>();
        qrRawImage.color = ColWhite;

        // StatusText — centered overlay INSIDE QRContainer (no VLG height impact)
        var qrStatusTxt = MakeTMP("StatusText", qrContainer, fontRegular, FBody, ColTextSec,
            "Server Unavailable.\n\nTry Again Later");
        qrStatusTxt.alignment = TextAlignmentOptions.Center;
        qrStatusTxt.enableWordWrapping = true;
        Stretch(qrStatusTxt.rectTransform);
        qrStatusTxt.rectTransform.offsetMin = new Vector2(40f, 40f);
        qrStatusTxt.rectTransform.offsetMax = new Vector2(-40f, -40f);
        qrStatusTxt.gameObject.SetActive(false);

        // ══════════════════════════════════════════════════════════════════
        // DIVIDER ("или")
        // ══════════════════════════════════════════════════════════════════
        var dividerRow = MakeRect("Divider", content);
        var dividerHlg = dividerRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        dividerHlg.childAlignment = TextAnchor.MiddleCenter;
        dividerHlg.childControlWidth  = true;  dividerHlg.childForceExpandWidth  = false;
        dividerHlg.childControlHeight = false; dividerHlg.childForceExpandHeight = false;
        dividerHlg.spacing = 0;
        dividerHlg.padding = new RectOffset(0, 0, 55, 55);
        var dividerCsf = dividerRow.gameObject.AddComponent<ContentSizeFitter>();
        dividerCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var leftLine = MakeRect("LeftLine", dividerRow);
        leftLine.sizeDelta = new Vector2(0, DividerH);
        var leftLineLE = leftLine.gameObject.AddComponent<LayoutElement>();
        leftLineLE.flexibleWidth = 1;
        leftLineLE.minHeight = DividerH; leftLineLE.preferredHeight = DividerH;
        leftLine.gameObject.AddComponent<Image>().color = ColBorder;

        var orText = MakeTMP("OrText", dividerRow, fontMed, FDivider, ColTextSec, "или");
        orText.alignment = TextAlignmentOptions.Center;
        orText.enableWordWrapping = false;
        var orLE = orText.rectTransform.gameObject.AddComponent<LayoutElement>();
        orLE.minHeight = FDivider + 10f; orLE.preferredHeight = FDivider + 10f;
        orLE.minWidth = FDivider * 3f + 88f; orLE.preferredWidth = FDivider * 3f + 88f;

        var rightLine = MakeRect("RightLine", dividerRow);
        rightLine.sizeDelta = new Vector2(0, DividerH);
        var rightLineLE = rightLine.gameObject.AddComponent<LayoutElement>();
        rightLineLE.flexibleWidth = 1;
        rightLineLE.minHeight = DividerH; rightLineLE.preferredHeight = DividerH;
        rightLine.gameObject.AddComponent<Image>().color = ColBorder;

        // ══════════════════════════════════════════════════════════════════
        // CODE PANEL
        //
        // WhatsApp child layout:
        //   [0] PhoneTitle
        //   [1] PhoneBody
        //   [2] Spacer (index filler — VLG spacing gives ~44px total)
        //   [3] PhoneInputGroup        ← Manager.GetChild(3)
        //   [4] CodeDisplayCard        ← Manager.GetChild(4) (hidden)
        //   [5] CodeInstructionText    ← Manager.GetChild(5) (hidden)
        //   [6] GetCodeButton          (text changes to "Получить другой код" after first use)
        //   [7] CodeTimer              (inactive)
        //   [8] ChangeNumberButton     (hidden)
        //
        // Telegram child layout:
        //   [0] PhoneTitle
        //   [1] PhoneBody
        //   [2] Spacer (index filler)
        //   [3] PhoneInputGroup        ← Manager.GetChild(3)
        //   [4] CodeInputLabel         ← Manager.GetChild(4) (hidden)
        //   [5] TelegramCodeInput      (inactive)
        //   [6] SendTelegramCodeButton (inactive)
        //   [7] GetCodeButton          (text → "Получить другой код" after first use)
        //   [8] CodeTimer              (inactive)
        //   [9] ChangeNumberButton     (hidden)
        // ══════════════════════════════════════════════════════════════════
        var codePanel = MakeRect("CodePanel", content);
        var codeVlg = codePanel.gameObject.AddComponent<VerticalLayoutGroup>();
        codeVlg.childAlignment = TextAnchor.UpperCenter;
        codeVlg.childControlWidth  = true;  codeVlg.childForceExpandWidth  = true;
        codeVlg.childControlHeight = false; codeVlg.childForceExpandHeight = false;
        codeVlg.spacing = PanelSpacing;
        var codeCsf = codePanel.gameObject.AddComponent<ContentSizeFitter>();
        codeCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // [0] PhoneTitle
        var phoneTitle = MakeTMP("PhoneTitle", codePanel, fontBold, FH3, ColTextPrimary,
            "Войти по номеру");
        phoneTitle.alignment = TextAlignmentOptions.Center;
        float phoneTitleH = FH3 + 20f;
        phoneTitle.rectTransform.sizeDelta = new Vector2(0, phoneTitleH);
        LE(phoneTitle.rectTransform, phoneTitleH);

        // [2] PhoneBody
        var phoneBody = MakeTMP("PhoneBody", codePanel, fontRegular, FBody, ColTextSec,
            "Введите номер телефона для\nполучения кода подтверждения");
        phoneBody.alignment = TextAlignmentOptions.Center;
        phoneBody.enableWordWrapping = true;
        phoneBody.lineSpacing = 25f;
        float phoneBodyH = FBody * 2.5f + 20f;
        phoneBody.rectTransform.sizeDelta = new Vector2(0, phoneBodyH);
        LE(phoneBody.rectTransform, phoneBodyH);

        // [3] Spacer — index filler so PhoneInputGroup lands at [4]
        // Height = 1px; combined with 22px VLG spacing on each side = ~45px gap
        var indexSpacer = MakeRect("Spacer", codePanel);
        indexSpacer.sizeDelta = new Vector2(0, 1f);
        var spacerLE = indexSpacer.gameObject.AddComponent<LayoutElement>();
        spacerLE.minHeight = 1f; spacerLE.preferredHeight = 1f;

        // [4] PhoneInputGroup ← Manager.GetChild(4)
        var phoneGroup = MakeRect("PhoneInputGroup", codePanel);
        var phoneHlg = phoneGroup.gameObject.AddComponent<HorizontalLayoutGroup>();
        phoneHlg.childAlignment = TextAnchor.MiddleLeft;
        phoneHlg.childControlWidth  = true;  phoneHlg.childForceExpandWidth  = false;
        phoneHlg.childControlHeight = true;  phoneHlg.childForceExpandHeight = false;
        phoneHlg.spacing = 28f;
        phoneGroup.sizeDelta = new Vector2(0, InputH);
        var phoneGroupLE = phoneGroup.gameObject.AddComponent<LayoutElement>();
        phoneGroupLE.minHeight = InputH; phoneGroupLE.preferredHeight = InputH;

        // Phone prefix "+7"
        var prefixGo = MakeRect("PhonePrefix", phoneGroup);
        var prefixImg = prefixGo.gameObject.AddComponent<Image>();
        prefixImg.sprite = RoundSprite; prefixImg.type = Image.Type.Sliced; prefixImg.color = ColWhite;
        var prefixLE = prefixGo.gameObject.AddComponent<LayoutElement>();
        prefixLE.minWidth = PrefixW; prefixLE.preferredWidth = PrefixW;
        var prefixTxt = MakeTMP("Text", prefixGo, fontSemi, FInput, ColTextPrimary, "+7");
        Stretch(prefixTxt.rectTransform);
        prefixTxt.alignment = TextAlignmentOptions.Center;

        // Phone input field
        var phoneField = BuildTMPInput("PhoneInput", phoneGroup, fontRegular,
            "(700) 000-00-00", InputH, false, true);
        var phoneInputLE = phoneField.GetComponent<LayoutElement>()
            ?? phoneField.gameObject.AddComponent<LayoutElement>();
        phoneInputLE.flexibleWidth = 1;

        // ── Platform-specific children ────────────────────────────────────

        Button getCodeBtn;
        GameObject codeTimerGo;
        // codeSendingGo removed — status shown inline in button text
        // codeSendingGo removed — status shown inline in button text
        Button changeNumberBtn;
        TMP_InputField telegramCodeField = null;
        Button sendTelegramBtn = null;

        if (isWa)
        {
            // [5] CodeDisplayCard ← Manager.GetChild(5) — hidden, child(0) = TMP for auth code
            var codeDisplayCard = MakeRect("CodeDisplayCard", codePanel);
            codeDisplayCard.sizeDelta = new Vector2(0, 160f);
            var cdLE = codeDisplayCard.gameObject.AddComponent<LayoutElement>();
            cdLE.minHeight = 160f; cdLE.preferredHeight = 160f;
            var cdImg = codeDisplayCard.gameObject.AddComponent<Image>();
            cdImg.sprite = RoundSprite; cdImg.type = Image.Type.Sliced; cdImg.color = ColWhite;
            // child(0) of CodeDisplayCard = TMP text for auth code
            var codeText = MakeTMP("CodeText", codeDisplayCard, fontBold, FH3, ColTextPrimary, "");
            Stretch(codeText.rectTransform);
            codeText.alignment = TextAlignmentOptions.Center;
            codeDisplayCard.gameObject.SetActive(false);

            // [6] CodeInstructionText ← Manager.GetChild(6) — hidden
            var codeInstruction = MakeTMP("CodeInstructionText", codePanel, fontRegular, FBody,
                ColTextSec, "Введите этот код в WhatsApp");
            codeInstruction.alignment = TextAlignmentOptions.Center;
            float ciH = FBody + 20f;
            codeInstruction.rectTransform.sizeDelta = new Vector2(0, ciH);
            LE(codeInstruction.rectTransform, ciH);
            codeInstruction.gameObject.SetActive(false);

            // [6] GetCodeButton — text changes to "Получить другой код" after first use
            getCodeBtn = BuildActionButton("GetCodeButton", codePanel, fontBold, "Получить код",
                platformColor);

            // [7] CodeTimer (inactive)
            codeTimerGo = BuildTimerGO("CodeTimer", codePanel, fontRegular, isWa);

            // [8] ChangeNumberButton — hidden, shown when code is displayed
            changeNumberBtn = BuildOutlineButton("ChangeNumberButton", codePanel, fontBold,
                "Изменить номер", platformColor);
            changeNumberBtn.gameObject.SetActive(false);
        }
        else
        {
            // [4] CodeInputLabel ← Manager.GetChild(4) — initially hidden, shown after phone code sent
            var codeInputLabel = MakeTMP("CodeInputLabel", codePanel, fontBold, FH3, ColTextPrimary,
                "Введите код");
            codeInputLabel.alignment = TextAlignmentOptions.Center;
            float cilH = FH3 + 20f;
            codeInputLabel.rectTransform.sizeDelta = new Vector2(0, cilH);
            LE(codeInputLabel.rectTransform, cilH);
            codeInputLabel.gameObject.SetActive(false);

            // [5] TelegramCodeInput (inactive)
            telegramCodeField = BuildTMPInput("TelegramCodeInput", codePanel, fontRegular,
                "Код подтверждения", InputH, false, true);
            telegramCodeField.gameObject.SetActive(false);

            // [6] SendTelegramCodeButton (inactive)
            sendTelegramBtn = BuildActionButton("SendTelegramCodeButton", codePanel, fontBold,
                "Подтвердить код", ColTgBlue);
            sendTelegramBtn.gameObject.SetActive(false);

            // [7] GetCodeButton — text changes to "Получить другой код" after first use
            getCodeBtn = BuildActionButton("GetCodeButton", codePanel, fontBold, "Получить код",
                platformColor);

            // [8] CodeTimer (inactive)
            codeTimerGo = BuildTimerGO("CodeTimer", codePanel, fontRegular, isWa);

            // [9] ChangeNumberButton — hidden, shown in code-entry mode
            changeNumberBtn = BuildOutlineButton("ChangeNumberButton", codePanel, fontBold,
                "Изменить номер", platformColor);
            changeNumberBtn.gameObject.SetActive(false);
        }

        // ══════════════════════════════════════════════════════════════════
        // SUCCESS OVERLAY (inside QRContainer — replaces QR image in-place)
        // ══════════════════════════════════════════════════════════════════
        var successPanel = MakeRect("SuccessOverlay", qrContainer);
        Stretch(successPanel);
        // White background to cover the QR image underneath
        var spImg = successPanel.gameObject.AddComponent<Image>();
        spImg.color = ColWhite;

        // Center container for checkmark + text
        var spCenter = MakeRect("Center", successPanel);
        spCenter.anchorMin = new Vector2(0.5f, 0.5f);
        spCenter.anchorMax = new Vector2(0.5f, 0.5f);
        spCenter.pivot = new Vector2(0.5f, 0.5f);
        spCenter.sizeDelta = new Vector2(400f, 300f);
        var spVlg = spCenter.gameObject.AddComponent<VerticalLayoutGroup>();
        spVlg.childAlignment = TextAnchor.MiddleCenter;
        spVlg.childControlWidth  = true;  spVlg.childForceExpandWidth  = true;
        spVlg.childControlHeight = false; spVlg.childForceExpandHeight = false;
        spVlg.spacing = 30f;

        // Checkmark circle
        var checkCircle = MakeRect("CheckCircle", spCenter);
        checkCircle.sizeDelta = new Vector2(140f, 140f);
        var ccLE = checkCircle.gameObject.AddComponent<LayoutElement>();
        ccLE.minWidth = 140f; ccLE.preferredWidth = 140f;
        ccLE.minHeight = 140f; ccLE.preferredHeight = 140f;
        var ccImg = checkCircle.gameObject.AddComponent<Image>();
        ccImg.sprite = RoundSprite; ccImg.type = Image.Type.Sliced; ccImg.color = platformColor;
        var checkTxt = MakeTMP("Checkmark", checkCircle, fontBold, 72f, ColWhite, "✓");
        Stretch(checkTxt.rectTransform);
        checkTxt.alignment = TextAlignmentOptions.Center;

        // "Авторизовано!" text
        var successTxt = MakeTMP("SuccessText", spCenter, fontBold, FH3, ColTextPrimary, "Авторизовано!");
        successTxt.alignment = TextAlignmentOptions.Center;
        float stH = FH3 + 20f;
        successTxt.rectTransform.sizeDelta = new Vector2(0, stH);
        LE(successTxt.rectTransform, stH);

        successPanel.gameObject.SetActive(false);

        // ══════════════════════════════════════════════════════════════════
        // WIRE MANAGER SERIALIZED FIELDS
        // ══════════════════════════════════════════════════════════════════
        var so = new SerializedObject(manager);
        string p = isWa ? "Whatsapp" : "Telegram";

        so.FindProperty($"{p}Auth").objectReferenceValue                       = root.gameObject;
        so.FindProperty($"{p}QRPanel").objectReferenceValue                    = qrPanel.gameObject;
        so.FindProperty($"{p}CodePanel").objectReferenceValue                  = codePanel.gameObject;
        so.FindProperty($"{p}QRCodeImage").objectReferenceValue                = qrRawImage;
        so.FindProperty($"{p}QRStatusText").objectReferenceValue              = qrStatusTxt.gameObject;
        so.FindProperty($"{p}NumberInput").objectReferenceValue                = phoneField;
        so.FindProperty($"{p}CodeTimer").objectReferenceValue                  = codeTimerGo;
        // Status is shown inline in button text — no separate CodeSendingMessage GO
        so.FindProperty($"{p}AuthSuccessPanel").objectReferenceValue           = successPanel.gameObject;
        so.FindProperty($"{p}AuthBackButton").objectReferenceValue             = backBtn;
        so.FindProperty($"Get{p}CodeButton").objectReferenceValue              = getCodeBtn;
        // GetAnother button removed — single GetCodeButton changes text after first use
        so.FindProperty($"Change{p}NumberButton").objectReferenceValue        = changeNumberBtn;

        if (!isWa)
        {
            so.FindProperty("TelegramCodeInput").objectReferenceValue          = telegramCodeField;
            so.FindProperty("SendTelegramCodeButton").objectReferenceValue     = sendTelegramBtn;
        }

        so.ApplyModifiedProperties();

        // Wire timer script SerializedFields (GetCodeButton + NumberInput)
        var timerComponent = codeTimerGo.GetComponent(isWa ? typeof(WhatsappCodeTimer) : typeof(TelegramCodeTimer));
        if (timerComponent != null)
        {
            var timerSO = new SerializedObject(timerComponent);
            string timerBtnProp = isWa ? "GetWhatsappCodeButton" : "GetTelegramCodeButton";
            string timerInputProp = isWa ? "WhatsappNumberInput" : "TelegramNumberInput";
            timerSO.FindProperty(timerBtnProp).objectReferenceValue = getCodeBtn;
            timerSO.FindProperty(timerInputProp).objectReferenceValue = phoneField;
            timerSO.ApplyModifiedProperties();
        }

        EditorUtility.SetDirty(root.gameObject);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Builders
    // ══════════════════════════════════════════════════════════════════════

    private static Button BuildActionButton(string name, RectTransform parent,
        TMP_FontAsset font, string label, Color bgColor)
    {
        var go = MakeRect(name, parent);
        go.sizeDelta = new Vector2(0, BtnH);
        LE(go, BtnH);
        var img = go.gameObject.AddComponent<Image>();
        img.sprite = RoundSprite; img.type = Image.Type.Sliced; img.color = bgColor;
        var btn = go.gameObject.AddComponent<Button>();
        var bc = btn.colors;
        bc.highlightedColor = bgColor * 0.88f;
        btn.colors = bc;
        var txt = MakeTMP("Label", go, font, FBtn, ColWhite, label);
        Stretch(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    private static Button BuildOutlineButton(string name, RectTransform parent,
        TMP_FontAsset font, string label, Color textColor)
    {
        var go = MakeRect(name, parent);
        go.sizeDelta = new Vector2(0, BtnH);
        LE(go, BtnH);
        var img = go.gameObject.AddComponent<Image>();
        img.sprite = RoundSprite; img.type = Image.Type.Sliced; img.color = Color.clear;
        var btn = go.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        var txt = MakeTMP("Label", go, font, FBtn, textColor, label);
        Stretch(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    // Timer GOs need TextMeshProUGUI on the ROOT GameObject
    // because WhatsappCodeTimer/TelegramCodeTimer call GetComponent<TMP>() on themselves.

    private static GameObject BuildTimerGO(string name, RectTransform parent,
        TMP_FontAsset font, bool isWa)
    {
        var go = MakeRect(name, parent);
        float h = FBody + 20f;
        go.sizeDelta = new Vector2(0, h);
        LE(go, h);
        // TMP must be on this GO — timer script calls GetComponent<TMP>() on itself
        var tmp = go.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font; tmp.fontSize = FBody; tmp.color = ColTextSec; tmp.text = "";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        if (isWa)
            go.gameObject.AddComponent<WhatsappCodeTimer>();
        else
            go.gameObject.AddComponent<TelegramCodeTimer>();
        go.gameObject.SetActive(false);
        return go.gameObject;
    }

    // BuildStatusGO removed — status is now shown inline in button text

    private static TMP_InputField BuildTMPInput(string name, RectTransform parent,
        TMP_FontAsset font, string placeholder, float height, bool multiline, bool integerOnly)
    {
        var container = MakeRect(name, parent);
        container.sizeDelta = new Vector2(0, height);
        LE(container, height);
        var cImg = container.gameObject.AddComponent<Image>();
        cImg.sprite = RoundSprite; cImg.type = Image.Type.Sliced; cImg.color = ColWhite;

        var field = container.gameObject.AddComponent<TMP_InputField>();
        if (integerOnly)
            field.contentType = TMP_InputField.ContentType.IntegerNumber;
        if (multiline)
            field.lineType = TMP_InputField.LineType.MultiLineNewline;

        var area = MakeRect("Text Area", container);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(InputPadX, 8); area.offsetMax = new Vector2(-InputPadX, -8);
        area.gameObject.AddComponent<RectMask2D>();

        var ph = MakeTMP("Placeholder", area, font, FInput, ColTextTert, placeholder);
        Stretch(ph.rectTransform);

        var txt = MakeTMP("Text", area, font, FInput, ColTextPrimary, "");
        Stretch(txt.rectTransform);

        field.textViewport  = area;
        field.textComponent = txt;
        field.placeholder   = ph;

        return field;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Utility helpers (matching AddBotPageSetup pattern)
    // ══════════════════════════════════════════════════════════════════════

    private static void DeleteExisting(Transform parent, string name)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child.name == name)
                Object.DestroyImmediate(child.gameObject);
        }
    }

    private static void AddSpacer(RectTransform parent, float height)
    {
        var spacer = MakeRect("Spacer", parent);
        spacer.sizeDelta = new Vector2(0, height);
        var le = spacer.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height; le.preferredHeight = height;
    }

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

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AttachmentPreviewScreenBuilder
{
    private const string ScreenName    = "AttachmentPreviewScreen";
    private const string RootName      = "Root";

    // Layout — canvas-space px at the project's 1080×2400 reference resolution.
    //
    // Safe zones are STATIC in this app (same pattern as the messages screen the
    // preview overlays): bars sit flush against the physical screen edges with the
    // notch / home-indicator inset baked into their height, and content lives in
    // the safe portion. Measured from the live scene: messages TopBar = 284 tall
    // (content row in the bottom 126), MessagesBottomPanel = 204 tall.
    // KeyboardAwarePanel depends on this — it subtracts safeArea.y from the
    // keyboard rise assuming the bar's resting height already contains the gap.
    private const float TopBarHeight       = 284f;  // top ~158 = status/notch zone
    private const float TopContentHeight   = 100f;  // back/title row, mirrors messages LeftZone
    private const float TopContentOffsetY  = 26f;   // row bottom edge above bar bottom (row spans 26..126)
    private const float BottomBarMinHeight = 204f;  // bottom 92 = home-indicator zone (see HLG padding)
    private const float BottomBarMaxHeight = 412f;  // caption growth ceiling — matches messages ExpandableInput
    private const float CaptionFieldHeight = 80f;   // taller so Body2 text breathes; pill ends at radius = h/2
    private const float SendButtonSize     = 88f;
    private const float SendIconSize       = 44f;   // white glyph centered inside the green circle
    private const float BackButtonSize     = 88f;
    private const float PlayOverlaySize    = 80f;
    private const float PlayIconSize       = 56f;
    private const float DurationBadgeWidth  = 96f;
    private const float DurationBadgeHeight = 36f;
    private const float DurationBadgeOffset = 16f;

    // Document "paper page" hero — a white A4-proportioned page with an
    // extension chip and abstract text lines, filename + meta below it.
    private const float PageWidth       = 560f;
    private const float PageHeight      = 792f;  // PageWidth × √2 → A4 ratio
    private const float PagePadding     = 48f;
    private const float PageLineSpacing = 32f;
    private const float BarHeight       = 20f;   // abstract "text line" bars
    private const float DocNameGap      = 64f;   // page bottom → filename
    private const float DocMetaGap      = 12f;   // filename → meta line
    private const float DocNameHeight   = 56f;
    private const float DocMetaHeight   = 40f;
    private const float DocTextWidth    = 920f;  // filename/meta label width

    // Type scale — project-calibrated reference units (see unity-ui-builder skill).
    // These replace the old mockup-px sizes that rendered ~⅓ too small on device.
    private const float TitleFontSize    = 50f;  // H1 — page title
    private const float CaptionFontSize  = 38f;  // Body2 — caption input + placeholder
    private const float DocNameFontSize  = 44f;  // H3 — filename under the page
    private const float DocSizeFontSize  = 32f;  // Caption — "PDF · 2.4 MB" meta
    private const float ChipFontSize     = 30f;  // Caption — extension chip label
    private const float DurationFontSize = 26f;  // Micro — badge

    // Corner radii (reference units). Half-the-height radii give true circles / pill ends.
    private const float SendRadius     = 44f;   // SendButtonSize / 2 → circle
    private const float PlayRadius     = 40f;   // PlayOverlaySize / 2 → circle
    private const float DurationRadius = 18f;   // DurationBadgeHeight / 2 → pill
    private const float CaptionRadius  = 40f;   // CaptionFieldHeight / 2 → pill
    private const float PageRadius     = 28f;   // paper page corners
    private const float ChipRadius     = 12f;   // extension chip
    private const float BarRadius      = 10f;   // BarHeight / 2 → pill text lines

    private static readonly Color RootBg         = new Color(0.055f, 0.078f, 0.086f); // #0E1416
    private static readonly Color BarBg          = new Color(0.118f, 0.145f, 0.157f); // #1E2528
    private static readonly Color CaptionFieldBg = new Color(0.165f, 0.196f, 0.212f); // #2A3236
    private static readonly Color SendGreen      = new Color(0.145f, 0.827f, 0.400f); // #25D366
    private static readonly Color White          = Color.white;
    private static readonly Color SubtleText     = new Color(0.604f, 0.631f, 0.651f); // #9AA1A6
    private static readonly Color PlaceholderText = new Color(0.435f, 0.455f, 0.475f); // #6F7479
    private static readonly Color PlayOverlayBg  = new Color(0f, 0f, 0f, 0.50f);
    private static readonly Color PaperBg        = new Color(0.925f, 0.937f, 0.945f); // #ECEFF1
    private static readonly Color PaperLine      = new Color(0.773f, 0.804f, 0.824f); // #C5CDD2
    private static readonly Color ChipPdfRed     = new Color(0.898f, 0.282f, 0.302f); // #E5484D — editor default; runtime recolors per type

    [MenuItem("Tools/Attach Sheet/Build Preview Screen")]
    public static void Build()
    {
        var existingPreview = Object.FindFirstObjectByType<AttachmentPreviewScreen>(FindObjectsInactive.Include);
        Transform parent;
        if (existingPreview != null)
        {
            parent = existingPreview.transform.parent;
            Object.DestroyImmediate(existingPreview.gameObject);
        }
        else
        {
            var attachSheet = Object.FindFirstObjectByType<AttachSheet>(FindObjectsInactive.Include);
            if (attachSheet == null)
            {
                Debug.LogError("[AttachmentPreviewScreenBuilder] AttachSheet not found in scene. Build the AttachSheet first via Tools > Attach Sheet > Build.");
                return;
            }
            var canvas = attachSheet.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[AttachmentPreviewScreenBuilder] AttachSheet has no Canvas ancestor.");
                return;
            }
            parent = canvas.rootCanvas.transform;
        }

        // ── ScriptHolder (always-on) + Root (toggled) ────────────────
        var screenGo = new GameObject(ScreenName, typeof(RectTransform), typeof(AttachmentPreviewScreen));
        screenGo.transform.SetParent(parent, false);

        // Place preview screen BELOW AttachSheet in sibling order (renders behind)
        // so AttachSheet stays on top during the brief overlap if both are ever alive
        // on the same frame. Idempotent: if AttachSheet's index shifts on rebuild, we
        // recompute against the live ref.
        var attachSheetInScene = Object.FindFirstObjectByType<AttachSheet>(FindObjectsInactive.Include);
        if (attachSheetInScene != null && attachSheetInScene.transform.parent == screenGo.transform.parent)
            screenGo.transform.SetSiblingIndex(attachSheetInScene.transform.GetSiblingIndex());

        var screenRt = (RectTransform)screenGo.transform;
        Stretch(screenRt);

        var rootGo = new GameObject(RootName, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        rootGo.transform.SetParent(screenGo.transform, false);
        rootGo.SetActive(false);
        var rootRt = (RectTransform)rootGo.transform;
        Stretch(rootRt);
        var rootBg = rootGo.GetComponent<Image>();
        rootBg.color = RootBg;
        rootBg.raycastTarget = true;
        var rootCg = rootGo.GetComponent<CanvasGroup>();
        rootCg.alpha = 0f;
        rootCg.interactable = false;
        rootCg.blocksRaycasts = false;

        // ── TopBar ────────────────────────────────────────────────────
        var topBar = NewChild(rootGo.transform, "TopBar", typeof(RectTransform));
        var topBarRt = (RectTransform)topBar.transform;
        topBarRt.anchorMin = new Vector2(0f, 1f);
        topBarRt.anchorMax = new Vector2(1f, 1f);
        topBarRt.pivot     = new Vector2(0.5f, 1f);
        topBarRt.sizeDelta = new Vector2(0f, TopBarHeight);
        topBarRt.anchoredPosition = Vector2.zero;

        // Content row pinned to the bar's safe bottom portion — the area above it
        // (the notch/status zone) stays empty, matching the messages screen.
        var topContent = NewChild(topBar.transform, "Content", typeof(RectTransform));
        var topContentRt = (RectTransform)topContent.transform;
        topContentRt.anchorMin = new Vector2(0f, 0f);
        topContentRt.anchorMax = new Vector2(1f, 0f);
        topContentRt.pivot     = new Vector2(0.5f, 0f);
        topContentRt.sizeDelta = new Vector2(0f, TopContentHeight);
        topContentRt.anchoredPosition = new Vector2(0f, TopContentOffsetY);

        var backBtnGo = NewChild(topContent.transform, "BackButton",
                                  typeof(RectTransform), typeof(Image), typeof(Button));
        var backRt = (RectTransform)backBtnGo.transform;
        backRt.anchorMin = new Vector2(0f, 0.5f);
        backRt.anchorMax = new Vector2(0f, 0.5f);
        backRt.pivot     = new Vector2(0f, 0.5f);
        backRt.sizeDelta = new Vector2(BackButtonSize, BackButtonSize);
        backRt.anchoredPosition = new Vector2(24f, 0f);
        var backImg = backBtnGo.GetComponent<Image>();
        backImg.color = White;
        backImg.raycastTarget = true;
        var backBtn = backBtnGo.GetComponent<Button>();
        var backNav = backBtn.navigation; backNav.mode = Navigation.Mode.None; backBtn.navigation = backNav;

        var titleGo = NewChild(topContent.transform, "Title",
                                typeof(RectTransform), typeof(TextMeshProUGUI));
        var titleRt = (RectTransform)titleGo.transform;
        Stretch(titleRt);
        var titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
        titleTmp.text          = "Preview";
        titleTmp.fontSize      = TitleFontSize;
        titleTmp.color         = White;
        titleTmp.alignment     = TextAlignmentOptions.Center;
        titleTmp.raycastTarget = false;

        // ── BottomBar (incl. KeyboardAwarePanel + DeferredDismissInputField + SendButton) ──
        var bottomBar = NewChild(rootGo.transform, "BottomBar",
                                  typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup),
                                  typeof(KeyboardAwarePanel));
        var bottomRt = (RectTransform)bottomBar.transform;
        bottomRt.anchorMin = new Vector2(0f, 0f);
        bottomRt.anchorMax = new Vector2(1f, 0f);
        bottomRt.pivot     = new Vector2(0.5f, 0f);
        bottomRt.sizeDelta = new Vector2(0f, BottomBarMinHeight);
        bottomRt.anchoredPosition = Vector2.zero;
        var bottomBg = bottomBar.GetComponent<Image>();
        bottomBg.color = BarBg;
        bottomBg.raycastTarget = true;
        var hl = bottomBar.GetComponent<HorizontalLayoutGroup>();
        // Bottom 92 = home-indicator zone: 24 (top) + 88 (send FAB row) + 92 = 204.
        // The bar background extends under the home bar; content stays above it.
        hl.padding = new RectOffset(32, 32, 24, 92);
        hl.spacing = 24;
        // LowerCenter (not MiddleCenter): the bar grows UPWARD as the caption wraps
        // (pivot is at the bar's bottom). Bottom-aligning the row pins the fixed-size
        // SendButton to the bottom padding zone so it stays put while the caption
        // pill expands above it — WhatsApp behavior. MiddleCenter re-centered the
        // button every line, displacing it upward.
        hl.childAlignment = TextAnchor.LowerCenter;
        hl.childControlWidth      = true;
        hl.childControlHeight     = true;
        hl.childForceExpandWidth  = false;
        hl.childForceExpandHeight = false;

        // Caption scroll host — pill visual + mask + scroller. The input field
        // is its CONTENT and grows unbounded; the host clamps the visible
        // window and overflow drag-scrolls inside it. Mirrors the messages
        // input's "Input" scroll host structure exactly.
        var captionScrollGo = NewChild(bottomBar.transform, "CaptionScroll",
                                        typeof(RectTransform), typeof(Image), typeof(ScrollRect),
                                        typeof(RectMask2D), typeof(LayoutElement));
        var captionScrollImg = captionScrollGo.GetComponent<Image>();
        captionScrollImg.color = CaptionFieldBg;
        captionScrollImg.raycastTarget = true;
        AddRoundedCorners(captionScrollGo, CaptionRadius);
        var captionScrollLe = captionScrollGo.GetComponent<LayoutElement>();
        captionScrollLe.flexibleWidth   = 1;
        captionScrollLe.minHeight       = CaptionFieldHeight;
        captionScrollLe.preferredHeight = CaptionFieldHeight;

        // Caption field (scroll content) — transparent raycast image like the
        // messages InputField; the pill visual stays on the static host so its
        // rounded ends never scroll out of the masked window.
        var captionGo = NewChild(captionScrollGo.transform, "CaptionField",
                                  typeof(RectTransform), typeof(Image));
        var captionRt = (RectTransform)captionGo.transform;
        captionRt.anchorMin = new Vector2(0f, 1f);
        captionRt.anchorMax = new Vector2(1f, 1f);
        captionRt.pivot     = new Vector2(0.5f, 1f);
        captionRt.sizeDelta = new Vector2(0f, CaptionFieldHeight);
        captionRt.anchoredPosition = Vector2.zero;
        var captionImg = captionGo.GetComponent<Image>();
        captionImg.color = new Color(1f, 1f, 1f, 0f);
        captionImg.raycastTarget = true;

        var captionField = captionGo.AddComponent<DeferredDismissInputField>();
        captionField.lineType = TMP_InputField.LineType.MultiLineNewline;
        captionField.transition = Selectable.Transition.None;
        captionField.targetGraphic = captionImg;
        captionField.textViewport = MakeTextArea(captionGo.transform, out var textComp, out var placeholderComp);
        captionField.textComponent = textComp;
        captionField.placeholder   = placeholderComp;

        var captionScroll = captionScrollGo.GetComponent<ScrollRect>();
        ConfigureCaptionScroll(captionScroll, (RectTransform)captionScrollGo.transform, captionRt);
        EnsureCaptionDragShield(captionGo, captionField, captionScroll);

        // Send button — green circular FAB (rounded bg) + centered white icon child.
        // The bg is a pure green circle (no sprite); assign the paper-plane/arrow
        // sprite to the "Icon" child in the inspector, NOT to the button background.
        var sendBtnGo = NewChild(bottomBar.transform, "SendButton",
                                  typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        var sendImg = sendBtnGo.GetComponent<Image>();
        sendImg.color = SendGreen;
        sendImg.raycastTarget = true;
        AddRoundedCorners(sendBtnGo, SendRadius);
        var sendLe = sendBtnGo.GetComponent<LayoutElement>();
        sendLe.minWidth = sendLe.preferredWidth = SendButtonSize;
        sendLe.minHeight = sendLe.preferredHeight = SendButtonSize;
        var sendBtn = sendBtnGo.GetComponent<Button>();
        var sendNav = sendBtn.navigation; sendNav.mode = Navigation.Mode.None; sendBtn.navigation = sendNav;

        var sendIconGo = NewChild(sendBtnGo.transform, "Icon",
                                   typeof(RectTransform), typeof(Image));
        var sendIconRt = (RectTransform)sendIconGo.transform;
        sendIconRt.anchorMin = sendIconRt.anchorMax = new Vector2(0.5f, 0.5f);
        sendIconRt.pivot     = new Vector2(0.5f, 0.5f);
        sendIconRt.sizeDelta = new Vector2(SendIconSize, SendIconSize);
        sendIconRt.anchoredPosition = Vector2.zero;
        var sendIconImg = sendIconGo.GetComponent<Image>();
        sendIconImg.color         = White;
        sendIconImg.raycastTarget = false;

        // ── ContentArea (sits between TopBar and BottomBar) ──────────
        var contentGo = NewChild(rootGo.transform, "ContentArea", typeof(RectTransform));
        var contentRt = (RectTransform)contentGo.transform;
        contentRt.anchorMin = new Vector2(0f, 0f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot     = new Vector2(0.5f, 0.5f);
        contentRt.offsetMin = new Vector2(0f, BottomBarMinHeight);
        contentRt.offsetMax = new Vector2(0f, -TopBarHeight);

        // Caption grows with multi-line text — same component as the messages
        // screen. The input (scroll content) grows unbounded via sizeDelta;
        // the host's LayoutElement tracks it, clamped at BottomBarMaxHeight.
        WireExpandableInput(bottomBar, bottomRt, captionGo, captionField,
                            captionScrollLe, captionScroll);

        // ── ImagePanel ───────────────────────────────────────────────
        var imagePanel = NewChild(contentGo.transform, "ImagePanel", typeof(RectTransform));
        StretchWithPad((RectTransform)imagePanel.transform, 48);
        var imagePreviewGo = NewChild(imagePanel.transform, "RawImage",
                                       typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        Stretch((RectTransform)imagePreviewGo.transform);
        var imagePreview = imagePreviewGo.GetComponent<RawImage>();
        imagePreview.color = White;
        var imageArf = imagePreviewGo.GetComponent<AspectRatioFitter>();
        imageArf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        imagePanel.SetActive(false);

        // ── VideoPanel ───────────────────────────────────────────────
        var videoPanel = NewChild(contentGo.transform, "VideoPanel", typeof(RectTransform));
        StretchWithPad((RectTransform)videoPanel.transform, 48);
        var videoPreviewGo = NewChild(videoPanel.transform, "RawImage",
                                       typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        Stretch((RectTransform)videoPreviewGo.transform);
        var videoPreview = videoPreviewGo.GetComponent<RawImage>();
        videoPreview.color = White;
        var videoArf = videoPreviewGo.GetComponent<AspectRatioFitter>();
        videoArf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        var playOverlayGo = NewChild(videoPanel.transform, "PlayOverlay",
                                      typeof(RectTransform), typeof(Image));
        var playRt = (RectTransform)playOverlayGo.transform;
        playRt.anchorMin = new Vector2(0.5f, 0.5f);
        playRt.anchorMax = new Vector2(0.5f, 0.5f);
        playRt.pivot     = new Vector2(0.5f, 0.5f);
        playRt.sizeDelta = new Vector2(PlayOverlaySize, PlayOverlaySize);
        var playBg = playOverlayGo.GetComponent<Image>();
        playBg.color = PlayOverlayBg;
        playBg.raycastTarget = false;
        AddRoundedCorners(playOverlayGo, PlayRadius);

        var playIconGo = NewChild(playOverlayGo.transform, "PlayIcon",
                                   typeof(RectTransform), typeof(Image));
        var playIconRt = (RectTransform)playIconGo.transform;
        playIconRt.anchorMin = new Vector2(0.5f, 0.5f);
        playIconRt.anchorMax = new Vector2(0.5f, 0.5f);
        playIconRt.pivot     = new Vector2(0.5f, 0.5f);
        playIconRt.sizeDelta = new Vector2(PlayIconSize, PlayIconSize);
        var playIcon = playIconGo.GetComponent<Image>();
        playIcon.color = White;
        playIcon.raycastTarget = false;

        var durationBadge = NewChild(videoPanel.transform, "DurationBadge",
                                      typeof(RectTransform), typeof(Image));
        var dbRt = (RectTransform)durationBadge.transform;
        dbRt.anchorMin = new Vector2(1f, 0f);
        dbRt.anchorMax = new Vector2(1f, 0f);
        dbRt.pivot     = new Vector2(1f, 0f);
        dbRt.sizeDelta = new Vector2(DurationBadgeWidth, DurationBadgeHeight);
        dbRt.anchoredPosition = new Vector2(-DurationBadgeOffset, DurationBadgeOffset);
        var dbBg = durationBadge.GetComponent<Image>();
        dbBg.color = PlayOverlayBg;
        dbBg.raycastTarget = false;
        AddRoundedCorners(durationBadge, DurationRadius);

        var durationLabelGo = NewChild(durationBadge.transform, "Label",
                                        typeof(RectTransform), typeof(TextMeshProUGUI));
        Stretch((RectTransform)durationLabelGo.transform);
        var durationLabel = durationLabelGo.GetComponent<TextMeshProUGUI>();
        durationLabel.text = "0:00";
        durationLabel.fontSize = DurationFontSize;
        durationLabel.color = White;
        durationLabel.alignment = TextAlignmentOptions.Center;
        durationLabel.raycastTarget = false;
        videoPanel.SetActive(false);

        // ── DocumentPanel (paper page hero) ──────────────────────────
        var docRefs = BuildDocumentPanel(contentGo.transform);

        // ── Wire serialized refs ─────────────────────────────────────
        var screen = screenGo.GetComponent<AttachmentPreviewScreen>();
        var so = new SerializedObject(screen);

        SetObjectRef(so, "attachSheet",       Object.FindFirstObjectByType<AttachSheet>(FindObjectsInactive.Include));
        SetObjectRef(so, "root",              rootGo);
        SetObjectRef(so, "rootCanvasGroup",   rootCg);
        SetObjectRef(so, "imagePanel",        imagePanel);
        SetObjectRef(so, "videoPanel",        videoPanel);
        SetObjectRef(so, "documentPanel",     docRefs.panel);
        SetObjectRef(so, "bottomBarRect",     (RectTransform)bottomBar.transform);
        SetObjectRef(so, "imagePreview",      imagePreview);
        SetObjectRef(so, "videoPreview",      videoPreview);
        SetObjectRef(so, "videoPlayOverlay",  playOverlayGo);
        SetObjectRef(so, "videoDurationBadge", durationBadge);
        SetObjectRef(so, "videoDurationLabel", durationLabel);
        SetObjectRef(so, "documentFileName",  docRefs.fileName);
        SetObjectRef(so, "documentFileSize",  docRefs.meta);
        SetObjectRef(so, "documentChipBackground", docRefs.chipBackground);
        SetObjectRef(so, "documentChipLabel", docRefs.chipLabel);
        SetObjectRef(so, "captionField",      captionField);
        SetObjectRef(so, "sendButton",        sendBtn);
        SetObjectRef(so, "backButton",        backBtn);

        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(screenGo.scene);
        Debug.Log("[AttachmentPreviewScreenBuilder] Built AttachmentPreviewScreen. Assign sprite refs in the inspector: "
                + "back arrow → BackButton (Image), send glyph → SendButton/Icon (the new white child, NOT the green bg), "
                + "play glyph → PlayOverlay/PlayIcon. The document panel needs no sprites — chip color/label are set at runtime.");
    }

    /// <summary>
    /// Surgical wirer for an already-built (and hand-configured) preview screen —
    /// upgrades it in place to full messages-input parity without rebuilding:
    /// inserts the CaptionScroll host (pill + mask + ScrollRect) above CaptionField
    /// if missing, moves the pill visual onto it, and (re)wires ExpandableInput +
    /// DragShield. Replaces the earlier separate Expandable/DragShield wirers.
    /// Idempotent — safe to run repeatedly.
    /// </summary>
    [MenuItem("Tools/Attach Sheet/Wire Caption Input")]
    public static void WireCaptionInput()
    {
        var screen = Object.FindFirstObjectByType<AttachmentPreviewScreen>(FindObjectsInactive.Include);
        var root      = screen != null ? screen.transform.Find(RootName) : null;
        var bottomBar = root != null ? root.Find("BottomBar") : null;
        if (bottomBar == null)
        {
            Debug.LogError("[AttachmentPreviewScreenBuilder] Expected Root/BottomBar hierarchy not found — cannot wire.");
            return;
        }

        // Pre-migration the field sits directly under the HLG; post-migration
        // it is the content of the CaptionScroll host.
        var host      = bottomBar.Find("CaptionScroll");
        var captionTr = host != null ? host.Find("CaptionField") : bottomBar.Find("CaptionField");
        var captionField = captionTr != null ? captionTr.GetComponent<TMP_InputField>() : null;
        if (captionField == null)
        {
            Debug.LogError("[AttachmentPreviewScreenBuilder] CaptionField with TMP_InputField not found — cannot wire.");
            return;
        }
        var captionGo = captionTr.gameObject;

        if (host == null)
            host = MigrateCaptionUnderScrollHost(bottomBar, captionGo, captionField);

        // Always re-ensure the pill corners — covers re-runs after a failed
        // type resolve (AddRoundedCorners is get-or-add, so this is idempotent).
        AddRoundedCorners(host.gameObject, CaptionRadius);

        var captionScroll = host.GetComponent<ScrollRect>();
        ConfigureCaptionScroll(captionScroll, (RectTransform)host, (RectTransform)captionTr);
        EnsureCaptionDragShield(captionGo, captionField, captionScroll);
        WireExpandableInput(bottomBar.gameObject, (RectTransform)bottomBar, captionGo, captionField,
                            host.GetComponent<LayoutElement>(), captionScroll);

        EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
        Debug.Log("[AttachmentPreviewScreenBuilder] Caption input wired to messages parity "
                + "(scroll host + ExpandableInput + DragShield). Save the scene to persist.");
    }

    /// <summary>
    /// Surgical redesign of ONLY the document panel on an already-built screen —
    /// replaces the old small card with the paper-page hero and rewires the four
    /// document refs. Leaves the rest of the screen (and its hand-assigned
    /// back/send/play sprites) untouched. Idempotent.
    /// </summary>
    [MenuItem("Tools/Attach Sheet/Rebuild Document Panel")]
    public static void RebuildDocumentPanel()
    {
        var screen = Object.FindFirstObjectByType<AttachmentPreviewScreen>(FindObjectsInactive.Include);
        var contentArea = screen != null ? screen.transform.Find($"{RootName}/ContentArea") : null;
        if (contentArea == null)
        {
            Debug.LogError("[AttachmentPreviewScreenBuilder] Root/ContentArea not found — build the full screen first via Tools > Attach Sheet > Build Preview Screen.");
            return;
        }

        var oldPanel = contentArea.Find("DocumentPanel");
        if (oldPanel != null) Object.DestroyImmediate(oldPanel.gameObject);

        var docRefs = BuildDocumentPanel(contentArea);

        var so = new SerializedObject(screen);
        SetObjectRef(so, "documentPanel",          docRefs.panel);
        SetObjectRef(so, "documentFileName",       docRefs.fileName);
        SetObjectRef(so, "documentFileSize",       docRefs.meta);
        SetObjectRef(so, "documentChipBackground", docRefs.chipBackground);
        SetObjectRef(so, "documentChipLabel",      docRefs.chipLabel);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
        Debug.Log("[AttachmentPreviewScreenBuilder] Document panel rebuilt as paper-page hero and refs rewired. Save the scene to persist.");
    }

    // ── helpers ───────────────────────────────────────────────────

    private struct DocumentPanelRefs
    {
        public GameObject panel;
        public TextMeshProUGUI fileName;
        public TextMeshProUGUI meta;
        public Image chipBackground;
        public TextMeshProUGUI chipLabel;
    }

    /// <summary>
    /// Document preview as a "paper page" hero: a white A4-proportioned page
    /// holding a colored extension chip and abstract text-line bars, with the
    /// filename and "TYPE · size" meta centered below. The whole stack is
    /// vertically centered in the content area. No sprites required — the chip
    /// label is TMP text and AttachmentPreviewScreen recolors it per file type.
    /// </summary>
    private static DocumentPanelRefs BuildDocumentPanel(Transform contentParent)
    {
        var refs = new DocumentPanelRefs();

        var panel = NewChild(contentParent, "DocumentPanel", typeof(RectTransform));
        Stretch((RectTransform)panel.transform);
        refs.panel = panel;

        float stackHeight = PageHeight + DocNameGap + DocNameHeight + DocMetaGap + DocMetaHeight;
        float stackTop    = stackHeight * 0.5f;

        // Page — white rounded rect, VLG lays out chip + text-line bars top-down.
        var pageGo = NewChild(panel.transform, "Page",
                              typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        var pageRt = (RectTransform)pageGo.transform;
        pageRt.anchorMin = pageRt.anchorMax = new Vector2(0.5f, 0.5f);
        pageRt.pivot     = new Vector2(0.5f, 0.5f);
        pageRt.sizeDelta = new Vector2(PageWidth, PageHeight);
        pageRt.anchoredPosition = new Vector2(0f, stackTop - PageHeight * 0.5f);
        var pageImg = pageGo.GetComponent<Image>();
        pageImg.color = PaperBg;
        pageImg.raycastTarget = false;
        AddRoundedCorners(pageGo, PageRadius);
        var pageVl = pageGo.GetComponent<VerticalLayoutGroup>();
        pageVl.padding = new RectOffset((int)PagePadding, (int)PagePadding, (int)PagePadding, (int)PagePadding);
        pageVl.spacing = PageLineSpacing;
        pageVl.childAlignment = TextAnchor.UpperLeft;
        pageVl.childControlWidth      = false;
        pageVl.childControlHeight     = false;
        pageVl.childForceExpandWidth  = false;
        pageVl.childForceExpandHeight = false;

        // Extension chip — colored tag that hugs its label via ContentSizeFitter.
        var chipGo = NewChild(pageGo.transform, "TypeChip",
                              typeof(RectTransform), typeof(Image),
                              typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var chipImg = chipGo.GetComponent<Image>();
        chipImg.color = ChipPdfRed;
        chipImg.raycastTarget = false;
        AddRoundedCorners(chipGo, ChipRadius);
        var chipHl = chipGo.GetComponent<HorizontalLayoutGroup>();
        chipHl.padding = new RectOffset(20, 20, 6, 6);
        chipHl.childAlignment = TextAnchor.MiddleCenter;
        chipHl.childControlWidth      = true;
        chipHl.childControlHeight     = true;
        chipHl.childForceExpandWidth  = false;
        chipHl.childForceExpandHeight = false;
        var chipFit = chipGo.GetComponent<ContentSizeFitter>();
        chipFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        chipFit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var chipLabelGo = NewChild(chipGo.transform, "Label",
                                   typeof(RectTransform), typeof(TextMeshProUGUI));
        var chipLabel = chipLabelGo.GetComponent<TextMeshProUGUI>();
        chipLabel.text          = "PDF";
        chipLabel.fontSize      = ChipFontSize;
        chipLabel.fontStyle     = FontStyles.Bold;
        chipLabel.color         = White;
        chipLabel.alignment     = TextAlignmentOptions.Center;
        chipLabel.raycastTarget = false;
        refs.chipBackground = chipImg;
        refs.chipLabel      = chipLabel;

        // Abstract "text line" bars — varying widths read as a document at a glance.
        float innerWidth = PageWidth - PagePadding * 2f;
        float[] lineWidthFractions = { 1f, 1f, 0.82f, 1f, 0.64f, 0.90f, 1f, 0.52f };
        for (int i = 0; i < lineWidthFractions.Length; i++)
        {
            var barGo = NewChild(pageGo.transform, $"Line{i}", typeof(RectTransform), typeof(Image));
            var barRt = (RectTransform)barGo.transform;
            barRt.sizeDelta = new Vector2(innerWidth * lineWidthFractions[i], BarHeight);
            var barImg = barGo.GetComponent<Image>();
            barImg.color = PaperLine;
            barImg.raycastTarget = false;
            AddRoundedCorners(barGo, BarRadius);
        }

        // Filename + meta, centered under the page.
        var docNameGo = NewChild(panel.transform, "FileName",
                                 typeof(RectTransform), typeof(TextMeshProUGUI));
        var docNameRt = (RectTransform)docNameGo.transform;
        docNameRt.anchorMin = docNameRt.anchorMax = new Vector2(0.5f, 0.5f);
        docNameRt.pivot     = new Vector2(0.5f, 0.5f);
        docNameRt.sizeDelta = new Vector2(DocTextWidth, DocNameHeight);
        docNameRt.anchoredPosition = new Vector2(
            0f, stackTop - PageHeight - DocNameGap - DocNameHeight * 0.5f);
        var docName = docNameGo.GetComponent<TextMeshProUGUI>();
        docName.text               = "filename.pdf";
        docName.fontSize           = DocNameFontSize;
        docName.fontStyle          = FontStyles.Bold;
        docName.color              = White;
        docName.alignment          = TextAlignmentOptions.Center;
        docName.textWrappingMode   = TextWrappingModes.NoWrap;
        docName.overflowMode       = TextOverflowModes.Ellipsis;
        docName.raycastTarget      = false;
        refs.fileName = docName;

        var docMetaGo = NewChild(panel.transform, "Meta",
                                 typeof(RectTransform), typeof(TextMeshProUGUI));
        var docMetaRt = (RectTransform)docMetaGo.transform;
        docMetaRt.anchorMin = docMetaRt.anchorMax = new Vector2(0.5f, 0.5f);
        docMetaRt.pivot     = new Vector2(0.5f, 0.5f);
        docMetaRt.sizeDelta = new Vector2(DocTextWidth, DocMetaHeight);
        docMetaRt.anchoredPosition = new Vector2(
            0f, stackTop - PageHeight - DocNameGap - DocNameHeight - DocMetaGap - DocMetaHeight * 0.5f);
        var docMeta = docMetaGo.GetComponent<TextMeshProUGUI>();
        docMeta.text          = "PDF · 0 KB";
        docMeta.fontSize      = DocSizeFontSize;
        docMeta.color         = SubtleText;
        docMeta.alignment     = TextAlignmentOptions.Center;
        docMeta.raycastTarget = false;
        refs.meta = docMeta;

        panel.SetActive(false);
        return refs;
    }

    /// <summary>
    /// In-place migration of a pre-scroll-host caption: creates the host at the
    /// field's sibling slot, moves the pill visual (color, rounded corners,
    /// HLG LayoutElement role) onto it, and turns the field into transparent
    /// scroll content — the exact structure of the messages input.
    /// </summary>
    private static Transform MigrateCaptionUnderScrollHost(Transform bottomBar, GameObject captionGo,
                                                           TMP_InputField captionField)
    {
        var captionImg = captionGo.GetComponent<Image>();

        var hostGo = NewChild(bottomBar, "CaptionScroll",
                              typeof(RectTransform), typeof(Image), typeof(ScrollRect),
                              typeof(RectMask2D), typeof(LayoutElement));
        hostGo.transform.SetSiblingIndex(captionGo.transform.GetSiblingIndex());

        var hostImg = hostGo.GetComponent<Image>();
        hostImg.color = captionImg != null ? captionImg.color : CaptionFieldBg;
        hostImg.raycastTarget = true;
        AddRoundedCorners(hostGo, CaptionRadius);

        var hostLe = hostGo.GetComponent<LayoutElement>();
        hostLe.flexibleWidth   = 1;
        hostLe.minHeight       = CaptionFieldHeight;
        hostLe.preferredHeight = CaptionFieldHeight;

        var captionRt = (RectTransform)captionGo.transform;
        captionRt.SetParent(hostGo.transform, false);
        captionRt.anchorMin = new Vector2(0f, 1f);
        captionRt.anchorMax = new Vector2(1f, 1f);
        captionRt.pivot     = new Vector2(0.5f, 1f);
        captionRt.sizeDelta = new Vector2(0f, CaptionFieldHeight);
        captionRt.anchoredPosition = Vector2.zero;

        // The content keeps only a transparent raycast image (messages parity);
        // the pill visual now lives on the static host so its rounded ends never
        // scroll out of the masked window. The HLG no longer controls the field,
        // so its LayoutElement goes too.
        var rounded = captionGo.GetComponent("ImageWithRoundedCorners");
        if (rounded != null) Object.DestroyImmediate(rounded);
        var le = captionGo.GetComponent<LayoutElement>();
        if (le != null) Object.DestroyImmediate(le);
        if (captionImg != null)
        {
            captionImg.color    = new Color(1f, 1f, 1f, 0f);
            captionImg.material = null;
        }
        captionField.transition    = Selectable.Transition.None;
        captionField.targetGraphic = captionImg;

        return hostGo.transform;
    }

    /// <summary>Vertical elastic scroller, viewport = the host itself — mirrors the messages "Input" host.</summary>
    private static void ConfigureCaptionScroll(ScrollRect scroll, RectTransform hostRt, RectTransform contentRt)
    {
        scroll.content           = contentRt;
        scroll.viewport          = hostRt;
        scroll.horizontal        = false;
        scroll.vertical          = true;
        scroll.movementType      = ScrollRect.MovementType.Elastic;
        scroll.inertia           = true;
        scroll.decelerationRate  = 0.135f;
        scroll.scrollSensitivity = 1f;
    }

    /// <summary>
    /// Transparent full-stretch raycast overlay that owns all pointer events on
    /// the caption — mirrors the messages input's DragShield child. Drags are
    /// forwarded to the caption scroll host so overflowing text drag-scrolls.
    /// Must be the LAST child so it raycasts above the Text Area.
    /// </summary>
    private static void EnsureCaptionDragShield(GameObject captionGo, TMP_InputField captionField,
                                                ScrollRect captionScroll)
    {
        var existing = captionGo.transform.Find("DragShield");
        var shieldGo = existing != null
            ? existing.gameObject
            : NewChild(captionGo.transform, "DragShield", typeof(RectTransform), typeof(Image), typeof(DragShield));

        var shieldRt = (RectTransform)shieldGo.transform;
        Stretch(shieldRt);
        shieldGo.transform.SetAsLastSibling();

        var shieldImg = shieldGo.GetComponent<Image>();
        if (shieldImg == null) shieldImg = shieldGo.AddComponent<Image>();
        shieldImg.color = new Color(1f, 1f, 1f, 0f);   // invisible, raycast-only
        shieldImg.raycastTarget = true;

        var shield = shieldGo.GetComponent<DragShield>();
        if (shield == null) shield = shieldGo.AddComponent<DragShield>();
        var so = new SerializedObject(shield);
        so.FindProperty("inputField").objectReferenceValue = captionField;
        so.FindProperty("parentScrollRect").objectReferenceValue = captionScroll;   // drags scroll the caption overflow
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireExpandableInput(GameObject bottomBar, RectTransform bottomRt, GameObject captionGo,
                                            TMP_InputField captionField, LayoutElement hostLe,
                                            ScrollRect captionScroll)
    {
        var expand = bottomBar.GetComponent<ExpandableInput>();
        if (expand == null) expand = bottomBar.AddComponent<ExpandableInput>();

        var so = new SerializedObject(expand);
        so.FindProperty("bottomPanelRect").objectReferenceValue    = bottomRt;
        so.FindProperty("inputFieldRect").objectReferenceValue     = (RectTransform)captionGo.transform;
        so.FindProperty("inputField").objectReferenceValue         = captionField;
        so.FindProperty("inputLayoutElement").objectReferenceValue = hostLe;        // host tracks content, clamped at max
        // messageListRect stays null: the media preview keeps its size and the
        // bar grows over it (WhatsApp-style) — consistent with the keyboard,
        // which already slides the bar over the content area.
        so.FindProperty("messageListRect").objectReferenceValue    = null;
        so.FindProperty("scrollRect").objectReferenceValue         = captionScroll; // over-max growth scrolls caret into view
        so.FindProperty("maxHeight").floatValue                    = BottomBarMaxHeight;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject NewChild(Transform parent, string name, params System.Type[] components)
    {
        var go = new GameObject(name, components);
        go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>
    /// Adds the project's RoundedCorners component (Nobi.UiRoundedCorners.ImageWithRoundedCorners)
    /// to an Image-bearing GameObject. A radius of half the smaller dimension yields a true
    /// circle / pill. Resolved by type name to avoid a hard compile dependency in this editor
    /// script — mirrors ChatsSearchBarBuilder.
    /// </summary>
    private static void AddRoundedCorners(GameObject go, float radius)
    {
        // The component ships in the RoundedCorners UPM package's own assembly,
        // so a bare Type.GetType (which only searches Assembly-CSharp/mscorlib)
        // misses it — scan all loaded assemblies instead.
        System.Type roundedType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            roundedType = asm.GetType("Nobi.UiRoundedCorners.ImageWithRoundedCorners");
            if (roundedType != null) break;
        }
        if (roundedType == null)
        {
            Debug.LogWarning(
                "[AttachmentPreviewScreenBuilder] ImageWithRoundedCorners type not found — "
                + $"'{go.name}' will render as a hard rectangle. Add the rounded-corner component manually if needed.");
            return;
        }

        var rounded = go.GetComponent(roundedType) ?? go.AddComponent(roundedType);
        var radiusField = roundedType.GetField("radius");
        if (radiusField != null) radiusField.SetValue(rounded, radius);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
    }

    private static void StretchWithPad(RectTransform rt, float pad)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(pad, pad);
        rt.offsetMax = new Vector2(-pad, -pad);
        rt.pivot     = new Vector2(0.5f, 0.5f);
    }

    /// <summary>
    /// Build the TMP_InputField's text + placeholder children. Mirrors what
    /// Unity's "GameObject > UI > Input Field (TMP)" menu produces.
    /// </summary>
    private static RectTransform MakeTextArea(Transform parent, out TMP_Text text, out TMP_Text placeholder)
    {
        var areaGo = NewChild(parent, "Text Area", typeof(RectTransform), typeof(RectMask2D));
        var areaRt = (RectTransform)areaGo.transform;
        areaRt.anchorMin = Vector2.zero;
        areaRt.anchorMax = Vector2.one;
        areaRt.offsetMin = new Vector2(20f, 12f);
        areaRt.offsetMax = new Vector2(-20f, -12f);

        var placeholderGo = NewChild(areaGo.transform, "Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
        Stretch((RectTransform)placeholderGo.transform);
        var ph = placeholderGo.GetComponent<TextMeshProUGUI>();
        ph.text          = "Add a caption…";
        ph.fontSize      = CaptionFontSize;
        ph.color         = PlaceholderText;
        ph.fontStyle     = FontStyles.Italic;
        ph.alignment     = TextAlignmentOptions.Left;
        ph.raycastTarget = false;
        placeholder = ph;

        var textGo = NewChild(areaGo.transform, "Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        Stretch((RectTransform)textGo.transform);
        var tx = textGo.GetComponent<TextMeshProUGUI>();
        tx.text          = "";
        tx.fontSize      = CaptionFontSize;
        tx.color         = White;
        tx.alignment     = TextAlignmentOptions.Left;
        tx.raycastTarget = false;
        text = tx;

        return areaRt;
    }

    private static void SetObjectRef(SerializedObject so, string propertyName, Object value)
    {
        var p = so.FindProperty(propertyName);
        if (p == null)
        {
            Debug.LogWarning($"[AttachmentPreviewScreenBuilder] Property {propertyName} not found on {so.targetObject}");
            return;
        }
        p.objectReferenceValue = value;
        if (value == null)
            Debug.LogWarning($"[AttachmentPreviewScreenBuilder] {so.targetObject.GetType().Name}.{propertyName} was set to null — assign manually in the inspector.");
    }
}
#endif

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
    private const float TopBarHeight       = 88f;
    private const float BottomBarMinHeight = 88f;
    private const float CaptionFieldHeight = 64f;
    private const float SendButtonSize     = 88f;
    private const float BackButtonSize     = 88f;
    private const float DocCardWidth       = 360f;
    private const float DocCardHeight      = 220f;
    private const float DocIconSize        = 56f;
    private const float PlayOverlaySize    = 80f;
    private const float PlayIconSize       = 56f;

    private static readonly Color RootBg         = new Color(0.055f, 0.078f, 0.086f); // #0E1416
    private static readonly Color BarBg          = new Color(0.118f, 0.145f, 0.157f); // #1E2528
    private static readonly Color CaptionFieldBg = new Color(0.165f, 0.196f, 0.212f); // #2A3236
    private static readonly Color SendGreen      = new Color(0.145f, 0.827f, 0.400f); // #25D366
    private static readonly Color White          = Color.white;
    private static readonly Color SubtleText     = new Color(0.604f, 0.631f, 0.651f); // #9AA1A6
    private static readonly Color PlaceholderText = new Color(0.435f, 0.455f, 0.475f); // #6F7479
    private static readonly Color PlayOverlayBg  = new Color(0f, 0f, 0f, 0.50f);

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

        var backBtnGo = NewChild(topBar.transform, "BackButton",
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

        var titleGo = NewChild(topBar.transform, "Title",
                                typeof(RectTransform), typeof(TextMeshProUGUI));
        var titleRt = (RectTransform)titleGo.transform;
        Stretch(titleRt);
        var titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
        titleTmp.text          = "Preview";
        titleTmp.fontSize      = 32f;
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
        hl.padding = new RectOffset(32, 32, 24, 24);
        hl.spacing = 24;
        hl.childAlignment = TextAnchor.MiddleCenter;
        hl.childControlWidth      = true;
        hl.childControlHeight     = true;
        hl.childForceExpandWidth  = false;
        hl.childForceExpandHeight = false;

        // Caption field
        var captionGo = NewChild(bottomBar.transform, "CaptionField",
                                  typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        var captionImg = captionGo.GetComponent<Image>();
        captionImg.color = CaptionFieldBg;
        captionImg.raycastTarget = true;
        var captionLe = captionGo.GetComponent<LayoutElement>();
        captionLe.flexibleWidth   = 1;
        captionLe.minHeight       = CaptionFieldHeight;
        captionLe.preferredHeight = CaptionFieldHeight;

        var captionField = captionGo.AddComponent<DeferredDismissInputField>();
        captionField.lineType = TMP_InputField.LineType.MultiLineNewline;
        captionField.textViewport = MakeTextArea(captionGo.transform, out var textComp, out var placeholderComp);
        captionField.textComponent = textComp;
        captionField.placeholder   = placeholderComp;

        // Send button
        var sendBtnGo = NewChild(bottomBar.transform, "SendButton",
                                  typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        var sendImg = sendBtnGo.GetComponent<Image>();
        sendImg.color = SendGreen;
        sendImg.raycastTarget = true;
        var sendLe = sendBtnGo.GetComponent<LayoutElement>();
        sendLe.minWidth = sendLe.preferredWidth = SendButtonSize;
        sendLe.minHeight = sendLe.preferredHeight = SendButtonSize;
        var sendBtn = sendBtnGo.GetComponent<Button>();
        var sendNav = sendBtn.navigation; sendNav.mode = Navigation.Mode.None; sendBtn.navigation = sendNav;

        // ── ContentArea (sits between TopBar and BottomBar) ──────────
        var contentGo = NewChild(rootGo.transform, "ContentArea", typeof(RectTransform));
        var contentRt = (RectTransform)contentGo.transform;
        contentRt.anchorMin = new Vector2(0f, 0f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot     = new Vector2(0.5f, 0.5f);
        contentRt.offsetMin = new Vector2(0f, BottomBarMinHeight);
        contentRt.offsetMax = new Vector2(0f, -TopBarHeight);

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
        dbRt.sizeDelta = new Vector2(96f, 36f);
        dbRt.anchoredPosition = new Vector2(-16f, 16f);
        var dbBg = durationBadge.GetComponent<Image>();
        dbBg.color = PlayOverlayBg;
        dbBg.raycastTarget = false;

        var durationLabelGo = NewChild(durationBadge.transform, "Label",
                                        typeof(RectTransform), typeof(TextMeshProUGUI));
        Stretch((RectTransform)durationLabelGo.transform);
        var durationLabel = durationLabelGo.GetComponent<TextMeshProUGUI>();
        durationLabel.text = "0:00";
        durationLabel.fontSize = 24f;
        durationLabel.color = White;
        durationLabel.alignment = TextAlignmentOptions.Center;
        durationLabel.raycastTarget = false;
        videoPanel.SetActive(false);

        // ── DocumentPanel ────────────────────────────────────────────
        var documentPanel = NewChild(contentGo.transform, "DocumentPanel", typeof(RectTransform));
        var docRt = (RectTransform)documentPanel.transform;
        docRt.anchorMin = new Vector2(0.5f, 0.5f);
        docRt.anchorMax = new Vector2(0.5f, 0.5f);
        docRt.pivot     = new Vector2(0.5f, 0.5f);
        docRt.sizeDelta = new Vector2(DocCardWidth, DocCardHeight);
        var docCardGo = NewChild(documentPanel.transform, "Card",
                                  typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        var docCardRt = (RectTransform)docCardGo.transform;
        Stretch(docCardRt);
        var docCardBg = docCardGo.GetComponent<Image>();
        docCardBg.color = BarBg;
        docCardBg.raycastTarget = false;
        var docVl = docCardGo.GetComponent<VerticalLayoutGroup>();
        docVl.padding = new RectOffset(24, 24, 24, 24);
        docVl.spacing = 12;
        docVl.childAlignment = TextAnchor.MiddleCenter;
        docVl.childControlWidth      = true;
        docVl.childControlHeight     = false;
        docVl.childForceExpandWidth  = true;
        docVl.childForceExpandHeight = false;

        var docIconGo = NewChild(docCardGo.transform, "Icon",
                                  typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        var docIconLe = docIconGo.GetComponent<LayoutElement>();
        docIconLe.minWidth = docIconLe.preferredWidth = DocIconSize;
        docIconLe.minHeight = docIconLe.preferredHeight = DocIconSize;
        var docIconImg = docIconGo.GetComponent<Image>();
        docIconImg.color = White;
        docIconImg.raycastTarget = false;

        var docNameGo = NewChild(docCardGo.transform, "FileName",
                                  typeof(RectTransform), typeof(TextMeshProUGUI));
        var docName = docNameGo.GetComponent<TextMeshProUGUI>();
        docName.text = "filename.pdf";
        docName.fontSize = 32f;
        docName.fontStyle = FontStyles.Bold;
        docName.color = White;
        docName.alignment = TextAlignmentOptions.Center;
        docName.enableWordWrapping = false;
        docName.overflowMode = TextOverflowModes.Ellipsis;
        docName.raycastTarget = false;

        var docSizeGo = NewChild(docCardGo.transform, "FileSize",
                                  typeof(RectTransform), typeof(TextMeshProUGUI));
        var docSize = docSizeGo.GetComponent<TextMeshProUGUI>();
        docSize.text = "0 B";
        docSize.fontSize = 24f;
        docSize.color = SubtleText;
        docSize.alignment = TextAlignmentOptions.Center;
        docSize.raycastTarget = false;
        documentPanel.SetActive(false);

        // ── Wire serialized refs ─────────────────────────────────────
        var screen = screenGo.GetComponent<AttachmentPreviewScreen>();
        var so = new SerializedObject(screen);

        SetObjectRef(so, "attachSheet",       Object.FindFirstObjectByType<AttachSheet>(FindObjectsInactive.Include));
        SetObjectRef(so, "root",              rootGo);
        SetObjectRef(so, "rootCanvasGroup",   rootCg);
        SetObjectRef(so, "imagePanel",        imagePanel);
        SetObjectRef(so, "videoPanel",        videoPanel);
        SetObjectRef(so, "documentPanel",     documentPanel);
        SetObjectRef(so, "imagePreview",      imagePreview);
        SetObjectRef(so, "videoPreview",      videoPreview);
        SetObjectRef(so, "videoPlayOverlay",  playOverlayGo);
        SetObjectRef(so, "videoDurationBadge", durationBadge);
        SetObjectRef(so, "videoDurationLabel", durationLabel);
        SetObjectRef(so, "documentFileName",  docName);
        SetObjectRef(so, "documentFileSize",  docSize);
        SetObjectRef(so, "documentIcon",      docIconImg);
        SetObjectRef(so, "captionField",      captionField);
        SetObjectRef(so, "sendButton",        sendBtn);
        SetObjectRef(so, "backButton",        backBtn);

        // Seed the MIME-icon list with empty slots — user drops sprites in inspector.
        var mimeIconsProp = so.FindProperty("mimeIcons");
        mimeIconsProp.arraySize = 0;
        AddMimeIconEntry(mimeIconsProp, "application/pdf");
        AddMimeIconEntry(mimeIconsProp, "application/vnd.openxmlformats-officedocument");
        AddMimeIconEntry(mimeIconsProp, "application/msword");
        AddMimeIconEntry(mimeIconsProp, "application/vnd.ms-excel");
        AddMimeIconEntry(mimeIconsProp, "image/");
        AddMimeIconEntry(mimeIconsProp, "video/");
        AddMimeIconEntry(mimeIconsProp, "text/");

        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(screenGo.scene);
        Debug.Log("[AttachmentPreviewScreenBuilder] Built AttachmentPreviewScreen. Assign sprite refs (back/send/play/doc icons) in the inspector.");
    }

    // ── helpers ───────────────────────────────────────────────────

    private static GameObject NewChild(Transform parent, string name, params System.Type[] components)
    {
        var go = new GameObject(name, components);
        go.transform.SetParent(parent, false);
        return go;
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
        ph.fontSize      = 28f;
        ph.color         = PlaceholderText;
        ph.fontStyle     = FontStyles.Italic;
        ph.alignment     = TextAlignmentOptions.Left;
        ph.raycastTarget = false;
        placeholder = ph;

        var textGo = NewChild(areaGo.transform, "Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        Stretch((RectTransform)textGo.transform);
        var tx = textGo.GetComponent<TextMeshProUGUI>();
        tx.text          = "";
        tx.fontSize      = 28f;
        tx.color         = White;
        tx.alignment     = TextAlignmentOptions.Left;
        tx.raycastTarget = false;
        text = tx;

        return areaRt;
    }

    private static void AddMimeIconEntry(SerializedProperty arrayProp, string prefix)
    {
        arrayProp.arraySize++;
        var element = arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1);
        var prefixProp = element.FindPropertyRelative("mimePrefix");
        var spriteProp = element.FindPropertyRelative("sprite");
        prefixProp.stringValue = prefix;
        spriteProp.objectReferenceValue = null;
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

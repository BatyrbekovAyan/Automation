#if UNITY_EDITOR
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the WhatsApp-style attach sheet: rounded-top white sheet with a
/// grabber pill, full-screen dim backdrop, and a row of three solid-color
/// circles (Camera / Gallery / Document) with white glyph icons.
/// Spec: docs/superpowers/specs/2026-06-12-attach-sheet-circle-grid-design.md
/// </summary>
public static class AttachSheetBuilder
{
    private const string SheetName      = "AttachSheet";
    private const string BackdropName   = "AttachSheetBackdrop";
    private const string MovingAreaName = "MovingArea";

    private const string CameraSpritePath   = "Assets/Images/Icons/Attach/AttachCamera.png";
    private const string GallerySpritePath  = "Assets/Images/Icons/Attach/AttachGallery.png";
    private const string DocumentSpritePath = "Assets/Images/Icons/Attach/AttachDocument.png";

    // All sizes in 1080x1920 canvas reference units (1 dp ~= 3 units).
    private const float SheetHeight     = 440f;
    private const float TopCornerRadius = 60f;

    private const float GrabberAreaHeight = 72f;
    private const float GrabberWidth      = 108f;
    private const float GrabberHeight     = 12f;

    private const int   SidePadding      = 72;
    // Bottom padding includes home-indicator allowance — safe zones are baked
    // into sizes in this project, never read from Screen.safeArea at runtime.
    private const int   BottomPadding    = 96;
    private const float GrabberToRowGap  = 24f;

    private const float CircleSize     = 180f;
    private const float IconSize       = 84f;
    private const float CircleLabelGap = 24f;
    private const float LabelWidth     = 240f;
    private const float LabelHeight    = 44f;
    private const float LabelFontSize  = 32f;

    private const float BackdropDimAlpha = 0.38f;

    private static readonly Color SheetColor   = Color.white;
    private static readonly Color GrabberColor = new Color(0.78f, 0.78f, 0.80f);
    private static readonly Color LabelColor   = new Color(0.45f, 0.45f, 0.48f);
    private static readonly Color CameraTint   = new Color(0.91f, 0.27f, 0.27f); // #E84545
    private static readonly Color GalleryTint  = new Color(0.48f, 0.36f, 0.85f); // #7B5BD8
    private static readonly Color DocumentTint = new Color(0.29f, 0.56f, 0.89f); // #4A90E2
    private static readonly Color PressedTint  = new Color(0.85f, 0.85f, 0.85f, 1f);

    // Headless entry point (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod AttachSheetBuilder.BuildHeadless -quit
    // Throws (→ nonzero Unity exit) if the build leaves the scene unwired.
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        Build();

        var sheet = Object.FindFirstObjectByType<AttachSheet>(FindObjectsInactive.Include);
        if (sheet == null)
            throw new System.InvalidOperationException("Headless build produced no AttachSheet.");

        var preview = Object.FindFirstObjectByType<AttachmentPreviewScreen>(FindObjectsInactive.Include);
        if (preview != null &&
            new SerializedObject(preview).FindProperty("attachSheet").objectReferenceValue == null)
            throw new System.InvalidOperationException("AttachmentPreviewScreen.attachSheet still null after build.");

        EditorSceneManager.SaveScene(scene);
        Debug.Log("[AttachSheetBuilder] Headless build + save complete.");
    }

    [MenuItem("Tools/Attach Sheet/Build")]
    public static void Build()
    {
        var bottomPanel = Object.FindFirstObjectByType<MessagesBottomPanel>(FindObjectsInactive.Include);
        if (bottomPanel == null)
        {
            Debug.LogError("[AttachSheetBuilder] MessagesBottomPanel not found in the open scene.");
            return;
        }

        var canvas = bottomPanel.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[AttachSheetBuilder] No Canvas found in MessagesBottomPanel's parent chain.");
            return;
        }
        var rootCanvas = canvas.rootCanvas;

        AssetDatabase.Refresh();
        Sprite cameraSprite   = LoadGlyph(CameraSpritePath);
        Sprite gallerySprite  = LoadGlyph(GallerySpritePath);
        Sprite documentSprite = LoadGlyph(DocumentSpritePath);
        if (cameraSprite == null || gallerySprite == null || documentSprite == null) return;

        // The sheet lives in the messages screen's MovingArea (the keyboard-aware
        // container that also hosts the bottom panel) — resolved from the panel's
        // parent chain so a stale canvas-root instance can't poison the placement.
        Transform parent = bottomPanel.transform;
        while (parent != null && parent.name != MovingAreaName) parent = parent.parent;
        var existingSheets = rootCanvas.GetComponentsInChildren<AttachSheet>(includeInactive: true);
        if (parent == null)
            parent = existingSheets.Length > 0 ? existingSheets[0].transform.parent : rootCanvas.transform;

        foreach (var existing in existingSheets)
            Object.DestroyImmediate(existing.gameObject);
        foreach (var t in rootCanvas.GetComponentsInChildren<RectTransform>(includeInactive: true))
            if (t != null && t.gameObject != null && t.gameObject.name == BackdropName)
                Object.DestroyImmediate(t.gameObject);

        // ── Backdrop ──────────────────────────────────────────────
        // Full-screen dim. CanvasGroup alpha animates 0→1 in AttachSheet;
        // the Image carries the resting dim strength.
        var backdropGo = new GameObject(BackdropName, typeof(RectTransform), typeof(Image),
                                        typeof(Button), typeof(CanvasGroup));
        backdropGo.transform.SetParent(parent, false);
        backdropGo.SetActive(false); // hidden until sheet opens

        var backdropRt = (RectTransform)backdropGo.transform;
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;

        var backdropImg = backdropGo.GetComponent<Image>();
        backdropImg.color         = new Color(0f, 0f, 0f, BackdropDimAlpha);
        backdropImg.raycastTarget = true;

        var backdropGroup = backdropGo.GetComponent<CanvasGroup>();
        backdropGroup.alpha = 0f;

        var backdropBtn = backdropGo.GetComponent<Button>();
        var backdropNav = backdropBtn.navigation;
        backdropNav.mode = Navigation.Mode.None;
        backdropBtn.navigation = backdropNav;
        backdropBtn.transition = Selectable.Transition.None;

        // ── Sheet ─────────────────────────────────────────────────
        var sheetGo = new GameObject(SheetName, typeof(RectTransform), typeof(Image), typeof(AttachSheet));
        sheetGo.transform.SetParent(parent, false);
        sheetGo.SetActive(false); // hidden until opened

        var rt = (RectTransform)sheetGo.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.sizeDelta        = new Vector2(0f, SheetHeight);
        rt.anchoredPosition = new Vector2(0f, -SheetHeight);

        var bg = sheetGo.GetComponent<Image>();
        bg.color         = SheetColor;
        bg.raycastTarget = true; // catches taps on the sheet so they don't dismiss

        // Top-only rounded corners. Vector4 mapping: x=TL, y=TR, z=BR, w=BL.
        var rounded = sheetGo.AddComponent<ImageWithIndependentRoundedCorners>();
        rounded.r = new Vector4(TopCornerRadius, TopCornerRadius, 0f, 0f);
        rounded.Validate();
        rounded.Refresh();

        BuildGrabber(sheetGo.transform);

        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(sheetGo.transform, false);
        var rowRt = (RectTransform)row.transform;
        rowRt.anchorMin = Vector2.zero;
        rowRt.anchorMax = Vector2.one;
        rowRt.offsetMin = new Vector2(SidePadding, BottomPadding);
        rowRt.offsetMax = new Vector2(-SidePadding, -(GrabberAreaHeight + GrabberToRowGap));
        var hl = row.GetComponent<HorizontalLayoutGroup>();
        hl.childAlignment         = TextAnchor.UpperCenter;
        hl.childControlWidth      = true;
        hl.childControlHeight     = true;
        hl.childForceExpandWidth  = true;
        hl.childForceExpandHeight = true;
        hl.spacing                = 0;

        var cameraTile   = BuildTile(row.transform, "CameraOpt",   "Camera",   CameraTint,   cameraSprite);
        var galleryTile  = BuildTile(row.transform, "GalleryOpt",  "Gallery",  GalleryTint,  gallerySprite);
        var documentTile = BuildTile(row.transform, "DocumentOpt", "Document", DocumentTint, documentSprite);

        // ── Wire AttachSheet ──────────────────────────────────────
        var attachSheet = sheetGo.GetComponent<AttachSheet>();
        var so = new SerializedObject(attachSheet);
        so.FindProperty("sheetHeightCanvasPx").floatValue = SheetHeight;
        SetObjectRef(so, "inputField",     bottomPanel.inputField);
        SetObjectRef(so, "backdrop",       backdropGo);
        SetObjectRef(so, "backdropButton", backdropBtn);
        SetObjectRef(so, "backdropGroup",  backdropGroup);
        SetObjectRef(so, "cameraButton",   cameraTile.button);
        SetObjectRef(so, "galleryButton",  galleryTile.button);
        SetObjectRef(so, "documentButton", documentTile.button);
        so.ApplyModifiedPropertiesWithoutUndo();

        // Wire MessagesBottomPanel.attachSheet
        var soPanel = new SerializedObject(bottomPanel);
        SetObjectRef(soPanel, "attachSheet", attachSheet);
        soPanel.ApplyModifiedPropertiesWithoutUndo();

        // Re-point AttachmentPreviewScreen at the recreated sheet — it opens off
        // OnPicked through this serialized ref, which dies with the old instance.
        var previewScreen = Object.FindFirstObjectByType<AttachmentPreviewScreen>(FindObjectsInactive.Include);
        if (previewScreen != null)
        {
            var soPreview = new SerializedObject(previewScreen);
            SetObjectRef(soPreview, "attachSheet", attachSheet);
            soPreview.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning("[AttachSheetBuilder] AttachmentPreviewScreen not found — " +
                             "its OnPicked subscription is not wired; previews won't open.");
        }

        EditorSceneManager.MarkSceneDirty(bottomPanel.gameObject.scene);
        Debug.Log("[AttachSheetBuilder] Built circle-grid AttachSheet + dim backdrop under " +
                  $"'{parent.name}'.");
    }

    private static void BuildGrabber(Transform sheet)
    {
        var pillGo = new GameObject("Grabber", typeof(RectTransform), typeof(Image));
        pillGo.transform.SetParent(sheet, false);

        var pillRt = (RectTransform)pillGo.transform;
        pillRt.anchorMin = new Vector2(0.5f, 1f);
        pillRt.anchorMax = new Vector2(0.5f, 1f);
        pillRt.pivot     = new Vector2(0.5f, 0.5f);
        pillRt.sizeDelta        = new Vector2(GrabberWidth, GrabberHeight);
        pillRt.anchoredPosition = new Vector2(0f, -GrabberAreaHeight * 0.5f);

        var pillImg = pillGo.GetComponent<Image>();
        pillImg.color         = GrabberColor;
        pillImg.raycastTarget = false;

        var pillRounded = pillGo.AddComponent<ImageWithRoundedCorners>();
        pillRounded.radius = GrabberHeight * 0.5f;
        pillRounded.Validate();
        pillRounded.Refresh();
    }

    private struct Tile { public GameObject root; public Button button; }

    private static Tile BuildTile(Transform parent, string name, string label, Color tint, Sprite glyph)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button),
                                typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);

        var bg = go.GetComponent<Image>();
        bg.color         = new Color(0, 0, 0, 0); // invisible, whole tile is tappable
        bg.raycastTarget = true;

        var vl = go.GetComponent<VerticalLayoutGroup>();
        vl.childAlignment         = TextAnchor.UpperCenter;
        vl.childControlWidth      = false;
        vl.childControlHeight     = false;
        vl.childForceExpandWidth  = false;
        vl.childForceExpandHeight = false;
        vl.spacing                = CircleLabelGap;
        vl.padding                = new RectOffset(0, 0, 0, 0);

        var circleGo = new GameObject("Circle", typeof(RectTransform), typeof(Image));
        circleGo.transform.SetParent(go.transform, false);
        var circleRt = (RectTransform)circleGo.transform;
        circleRt.sizeDelta = new Vector2(CircleSize, CircleSize);
        var circleImg = circleGo.GetComponent<Image>();
        circleImg.color         = tint;
        circleImg.raycastTarget = false;
        var circleRounded = circleGo.AddComponent<ImageWithRoundedCorners>();
        circleRounded.radius = CircleSize * 0.5f;
        circleRounded.Validate();
        circleRounded.Refresh();

        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(circleGo.transform, false);
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.anchorMin = new Vector2(0.5f, 0.5f);
        iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(IconSize, IconSize);
        var iconImg = iconGo.GetComponent<Image>();
        iconImg.sprite         = glyph;
        iconImg.color          = Color.white;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget  = false;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.sizeDelta = new Vector2(LabelWidth, LabelHeight);
        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.text          = label;
        tmp.fontSize      = LabelFontSize;
        tmp.color         = LabelColor;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.fontStyle     = FontStyles.Normal;
        tmp.raycastTarget = false;

        // Pressed feedback: darken the circle while the finger is down.
        var button = go.GetComponent<Button>();
        button.targetGraphic = circleImg;
        button.transition    = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor     = PressedTint;
        colors.selectedColor    = Color.white;
        colors.fadeDuration     = 0.1f;
        button.colors = colors;

        return new Tile { root = go, button = button };
    }

    private static Sprite LoadGlyph(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"[AttachSheetBuilder] Glyph not found or not imported yet: {path}");
            return null;
        }
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogError($"[AttachSheetBuilder] Failed to load sprite at {path}");
        return sprite;
    }

    private static void SetObjectRef(SerializedObject so, string propertyName, Object value)
    {
        var p = so.FindProperty(propertyName);
        if (p == null)
        {
            Debug.LogWarning($"[AttachSheetBuilder] Property {propertyName} not found on {so.targetObject}");
            return;
        }
        p.objectReferenceValue = value;
        if (value == null)
            Debug.LogWarning($"[AttachSheetBuilder] {so.targetObject.GetType().Name}.{propertyName} was set to null — wiring incomplete, please assign manually in the inspector.");
    }
}
#endif

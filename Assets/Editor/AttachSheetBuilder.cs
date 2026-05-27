#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AttachSheetBuilder
{
    private const string SheetName    = "AttachSheet";
    private const string BackdropName = "AttachSheetBackdrop";

    // Layout — canvas-space px at the project's 1080×2400 reference resolution.
    private const float SheetHeight   = 700f;
    private const float TilePrefWidth = 160f;
    private const float TileHeight    = 200f;
    private const float IconSize      = 96f;
    private const float IconSpacing   = 16f;
    private const float LabelFontSize = 28f;
    private const int   PaddingX      = 48;
    private const int   PaddingY      = 40;

    private static readonly Color BackgroundColor = Color.white;
    private static readonly Color LabelColor      = new Color(0.33f, 0.33f, 0.33f);
    private static readonly Color CameraTint      = new Color(0.91f, 0.27f, 0.27f); // #E84545
    private static readonly Color GalleryTint     = new Color(0.48f, 0.36f, 0.85f); // #7B5BD8
    private static readonly Color DocumentTint    = new Color(0.29f, 0.56f, 0.89f); // #4A90E2

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
        Transform parent = rootCanvas.transform;

        // Idempotent: remove any pre-existing sheets + backdrops anywhere under the root Canvas.
        foreach (var existing in rootCanvas.GetComponentsInChildren<AttachSheet>(includeInactive: true))
            Object.DestroyImmediate(existing.gameObject);
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child != null && child.gameObject.name == BackdropName)
                Object.DestroyImmediate(child.gameObject);
        }

        // ── Backdrop ──────────────────────────────────────────────
        // Full-screen invisible click catcher. Anchored full-screen, with the bottom
        // edge raised by SheetHeight so the sheet area is NOT covered (taps on the
        // sheet itself go to sheet content / tiles, not the backdrop).
        var backdropGo = new GameObject(BackdropName, typeof(RectTransform), typeof(Image), typeof(Button));
        backdropGo.transform.SetParent(parent, false);
        backdropGo.SetActive(false); // hidden until sheet opens

        var backdropRt = (RectTransform)backdropGo.transform;
        backdropRt.anchorMin = new Vector2(0f, 0f);
        backdropRt.anchorMax = new Vector2(1f, 1f);
        backdropRt.offsetMin = new Vector2(0f, SheetHeight); // bottom edge above where sheet sits
        backdropRt.offsetMax = new Vector2(0f, 0f);

        var backdropImg = backdropGo.GetComponent<Image>();
        backdropImg.color         = new Color(0f, 0f, 0f, 0f); // transparent
        backdropImg.raycastTarget = true;

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
        bg.color         = BackgroundColor;
        bg.raycastTarget = true; // catches taps on the sheet so they don't dismiss

        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(sheetGo.transform, false);
        var rowRt = (RectTransform)row.transform;
        rowRt.anchorMin = Vector2.zero;
        rowRt.anchorMax = Vector2.one;
        rowRt.offsetMin = new Vector2(PaddingX, PaddingY);
        rowRt.offsetMax = new Vector2(-PaddingX, -PaddingY);
        var hl = row.GetComponent<HorizontalLayoutGroup>();
        hl.childAlignment         = TextAnchor.MiddleCenter;
        hl.childControlWidth      = true;
        hl.childControlHeight     = false;
        hl.childForceExpandWidth  = true;
        hl.childForceExpandHeight = false;
        hl.spacing                = 0;

        var cameraTile   = BuildTile(row.transform, "CameraOpt",   "Camera",   CameraTint);
        var galleryTile  = BuildTile(row.transform, "GalleryOpt",  "Gallery",  GalleryTint);
        var documentTile = BuildTile(row.transform, "DocumentOpt", "Document", DocumentTint);

        // ── Wire AttachSheet ──────────────────────────────────────
        var attachSheet = sheetGo.GetComponent<AttachSheet>();
        var so = new SerializedObject(attachSheet);
        SetObjectRef(so, "inputField",     bottomPanel.inputField);
        SetObjectRef(so, "backdrop",       backdropGo);
        SetObjectRef(so, "backdropButton", backdropBtn);
        SetObjectRef(so, "cameraButton",   cameraTile.button);
        SetObjectRef(so, "galleryButton",  galleryTile.button);
        SetObjectRef(so, "documentButton", documentTile.button);
        so.ApplyModifiedPropertiesWithoutUndo();

        // Wire MessagesBottomPanel.attachSheet
        var soPanel = new SerializedObject(bottomPanel);
        SetObjectRef(soPanel, "attachSheet", attachSheet);
        soPanel.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(bottomPanel.gameObject.scene);
        Debug.Log("[AttachSheetBuilder] Built AttachSheet + backdrop. Assign tile icon sprites in the inspector.");
    }

    private struct Tile { public GameObject root; public Button button; public Image icon; }

    private static Tile BuildTile(Transform parent, string name, string label, Color tint)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button),
                                typeof(VerticalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(TilePrefWidth, TileHeight);

        var bg = go.GetComponent<Image>();
        bg.color         = new Color(0, 0, 0, 0);
        bg.raycastTarget = true;

        var vl = go.GetComponent<VerticalLayoutGroup>();
        vl.childAlignment         = TextAnchor.UpperCenter;
        vl.childControlWidth      = false;
        vl.childControlHeight     = false;
        vl.childForceExpandWidth  = false;
        vl.childForceExpandHeight = false;
        vl.spacing                = IconSpacing;
        vl.padding                = new RectOffset(0, 0, 0, 0);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth  = TilePrefWidth;
        le.preferredHeight = TileHeight;
        le.flexibleWidth   = 1;

        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(go.transform, false);
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.sizeDelta = new Vector2(IconSize, IconSize);
        var iconImg = iconGo.GetComponent<Image>();
        iconImg.color         = tint;
        iconImg.raycastTarget = false;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.sizeDelta = new Vector2(TilePrefWidth, 40);
        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.text          = label;
        tmp.fontSize      = LabelFontSize;
        tmp.color         = LabelColor;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.fontStyle     = FontStyles.Normal;
        tmp.raycastTarget = false;

        return new Tile { root = go, button = go.GetComponent<Button>(), icon = iconImg };
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

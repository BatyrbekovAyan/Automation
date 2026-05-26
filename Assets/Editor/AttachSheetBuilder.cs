#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AttachSheetBuilder
{
    private const string SheetName = "AttachSheet";

    // Layout — canvas-space px at the project's 1080×2400 reference resolution
    private const float SheetHeight   = 290f;
    private const float TilePrefWidth = 88f;
    private const float TileHeight    = 120f;
    private const float IconSize      = 56f;
    private const float IconSpacing   = 8f;
    private const float LabelFontSize = 11f;
    private const int   PaddingX      = 24;
    private const int   PaddingY      = 16;

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

        Transform parent = bottomPanel.transform.parent; // sheet is sibling of the input bar
        if (parent == null)
        {
            Debug.LogError("[AttachSheetBuilder] MessagesBottomPanel has no parent — unexpected hierarchy.");
            return;
        }

        // Idempotent: nuke any existing sheet
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child.name == SheetName) Object.DestroyImmediate(child.gameObject);
        }

        var sheetGo = new GameObject(SheetName, typeof(RectTransform), typeof(Image), typeof(AttachSheet));
        sheetGo.transform.SetParent(parent, false);

        var rt = (RectTransform)sheetGo.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot     = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(0f, SheetHeight);          // height in canvas px; width stretched by anchors
        rt.anchoredPosition = new Vector2(0f, -SheetHeight);  // start off-screen; AttachSheet.Update will track

        var bg = sheetGo.GetComponent<Image>();
        bg.color = BackgroundColor;
        bg.raycastTarget = true; // catches stray taps so they don't bubble through

        var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(sheetGo.transform, false);
        var rowRt = (RectTransform)row.transform;
        rowRt.anchorMin = Vector2.zero;
        rowRt.anchorMax = Vector2.one;
        rowRt.offsetMin = new Vector2(PaddingX, PaddingY);
        rowRt.offsetMax = new Vector2(-PaddingX, -PaddingY);
        var hl = row.GetComponent<HorizontalLayoutGroup>();
        hl.childAlignment         = TextAnchor.MiddleCenter;
        hl.childControlWidth      = true;   // distribute remaining row width across 3 tiles
        hl.childControlHeight     = false;
        hl.childForceExpandWidth  = true;
        hl.childForceExpandHeight = false;
        hl.spacing                = 0;

        var cameraTile   = BuildTile(row.transform, "CameraOpt",   "Camera",   CameraTint);
        var galleryTile  = BuildTile(row.transform, "GalleryOpt",  "Gallery",  GalleryTint);
        var documentTile = BuildTile(row.transform, "DocumentOpt", "Document", DocumentTint);

        // Wire via SerializedObject for undo safety
        var attachSheet = sheetGo.GetComponent<AttachSheet>();
        var so = new SerializedObject(attachSheet);
        SetObjectRef(so, "inputField",          bottomPanel.inputField);
        SetObjectRef(so, "keyboardPanel",       bottomPanel.GetComponent<KeyboardAwarePanel>());
        SetObjectRef(so, "messagesBottomPanel", bottomPanel);
        SetObjectRef(so, "cameraButton",        cameraTile.button);
        SetObjectRef(so, "galleryButton",       galleryTile.button);
        SetObjectRef(so, "documentButton",      documentTile.button);
        so.ApplyModifiedPropertiesWithoutUndo();

        // Wire MessagesBottomPanel.attachSheet + attachButtonIcon
        var soPanel = new SerializedObject(bottomPanel);
        SetObjectRef(soPanel, "attachSheet", attachSheet);
        var attachIcon = FindButtonIconImage(bottomPanel.attachButton);
        if (attachIcon != null) SetObjectRef(soPanel, "attachButtonIcon", attachIcon);
        else Debug.LogWarning("[AttachSheetBuilder] Could not find Image child inside attachButton — please assign attachButtonIcon manually.");
        soPanel.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(bottomPanel.gameObject.scene);
        Debug.Log("[AttachSheetBuilder] Built AttachSheet. Now assign sprites in the inspector: " +
                  "MessagesBottomPanel.plusIconSprite/keyboardIconSprite, and each tile's Icon.sprite.");
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
        bg.color = new Color(0, 0, 0, 0);   // transparent tile bg; only icon shows color
        bg.raycastTarget = true;            // tap target

        var vl = go.GetComponent<VerticalLayoutGroup>();
        vl.childAlignment       = TextAnchor.UpperCenter;
        vl.childControlWidth    = false;
        vl.childControlHeight   = false;
        vl.childForceExpandWidth  = false;
        vl.childForceExpandHeight = false;
        vl.spacing = IconSpacing;
        vl.padding = new RectOffset(0, 0, 0, 0);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth  = TilePrefWidth;
        le.preferredHeight = TileHeight;
        le.flexibleWidth   = 0;

        // Icon circle
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(go.transform, false);
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.sizeDelta = new Vector2(IconSize, IconSize);
        var iconImg = iconGo.GetComponent<Image>();
        iconImg.color = tint;
        // Sprite slot stays empty — user drops the white glyph PNG in via inspector after authoring.
        iconImg.raycastTarget = false;

        // Label
        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.sizeDelta = new Vector2(TilePrefWidth, 16);
        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.text         = label;
        tmp.fontSize     = LabelFontSize;
        tmp.color        = LabelColor;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.fontStyle    = FontStyles.Normal;
        tmp.raycastTarget = false;

        return new Tile { root = go, button = go.GetComponent<Button>(), icon = iconImg };
    }

    private static void SetObjectRef(SerializedObject so, string propertyName, Object value)
    {
        var p = so.FindProperty(propertyName);
        if (p != null) p.objectReferenceValue = value;
        else Debug.LogWarning($"[AttachSheetBuilder] Property {propertyName} not found on {so.targetObject}");
    }

    private static Image FindButtonIconImage(Button button)
    {
        if (button == null) return null;
        // Prefer a child named "Icon" or the first Image that's not the button's own background.
        foreach (var img in button.GetComponentsInChildren<Image>(true))
        {
            if (img.gameObject == button.gameObject) continue; // skip button's own bg
            return img;
        }
        return null;
    }
}
#endif

#if UNITY_EDITOR
using Automation.BotSettingsUI;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bakes the «Загрузить прайс-лист» source sheet into BotSettings.prefab:
/// a slide-up bottom sheet offering «Файл» (document picker) and
/// «Фото из галереи» (gallery multi-select), plus «Отмена».
///
/// The sheet lives INSIDE the BotSettings prefab (not the scene) because each
/// bot gets its own runtime-instantiated BotSettings instance — the same
/// reason ItemEditSheet / the ConfirmChange popups are baked into the prefab
/// rather than the scene. The two option buttons are serialized onto the
/// UploadSourceSheet controller, which self-wires them in Awake and raises
/// OnFilePressed / OnGalleryPressed; BotSettings subscribes at runtime
/// (BotSettings.Files.WireUploadedFiles) and resumes the existing upload flow.
///
/// Idempotent delete-and-rebuild — safe to re-run. Sizes are in 1080×1920
/// canvas reference units (see .claude/skills/unity-ui-builder/SKILL.md).
/// </summary>
public static class BotSettingsUploadSourceSheetBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string SheetName = "UploadSourceSheet";

    // Palette — matches BotSettingsRebuilder's tokens.
    private static readonly Color Card = Hex("#FFFFFF");
    private static readonly Color Text = Hex("#1A1A2E");
    private static readonly Color TextMuted = Hex("#8E8E93");
    private static readonly Color Primary = Hex("#1B7CEB");
    private static readonly Color OptionFill = Hex("#F0F2F5");
    private static readonly Color CancelFill = Hex("#E4E6EB");

    [MenuItem("Tools/BotSettings/Build Upload Source Sheet")]
    public static void Build()
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[UploadSourceSheet] Failed to load prefab at {PrefabPath}");
            return;
        }

        try
        {
            var settings = prefabRoot.GetComponent<BotSettings>();
            if (settings == null)
            {
                Debug.LogError("[UploadSourceSheet] BotSettings component not found on prefab root.");
                return;
            }

            // Idempotent: sweep any prior sheet GameObject before rebuilding.
            for (int i = prefabRoot.transform.childCount - 1; i >= 0; i--)
            {
                var child = prefabRoot.transform.GetChild(i);
                if (child.name == SheetName) Object.DestroyImmediate(child.gameObject);
            }

            var sheet = BuildSheet(prefabRoot);

            // Rewire the one serialized consumer: BotSettings.uploadSourceSheet.
            var so = new SerializedObject(settings);
            var prop = so.FindProperty("uploadSourceSheet");
            if (prop == null)
            {
                Debug.LogError("[UploadSourceSheet] SerializedProperty 'uploadSourceSheet' not found on BotSettings.");
                return;
            }
            prop.objectReferenceValue = sheet;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log($"[UploadSourceSheet] Sheet built and wired at {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static UploadSourceSheet BuildSheet(GameObject prefabRoot)
    {
        // Container fills the screen; holds the dim scrim + the sheet card.
        var container = NewChild(prefabRoot.transform, SheetName, out RectTransform containerRt);
        StretchFill(containerRt);
        container.transform.SetAsLastSibling();
        container.SetActive(false);

        // Scrim behind: full-screen dim, tap-outside-to-close.
        var scrimGo = NewChild(container.transform, "ScrimBehind", out RectTransform scrimRt);
        StretchFill(scrimRt);
        scrimGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 1f);
        var scrimCg = scrimGo.AddComponent<CanvasGroup>();
        scrimCg.alpha = 0f;
        var scrimFinger = scrimGo.AddComponent<DelayedFingerUpAction>();

        // Sheet card: bottom-anchored, slides up from below.
        var sheetGo = NewChild(container.transform, "SheetRoot", out RectTransform sheetRt);
        sheetGo.AddComponent<Image>().color = Card;
        sheetRt.anchorMin = new Vector2(0f, 0f);
        sheetRt.anchorMax = new Vector2(1f, 0f);
        sheetRt.pivot = new Vector2(0.5f, 0f);
        sheetRt.sizeDelta = new Vector2(0f, 640f);
        sheetRt.anchoredPosition = Vector2.zero;
        AddRoundedCorners(sheetGo, 48f); // top corners; bottom sits off-screen

        // Title.
        var titleGo = NewChild(sheetGo.transform, "Title", out RectTransform titleRt);
        var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
        titleTmp.text = "Загрузить прайс-лист";
        titleTmp.fontSize = 48f;
        titleTmp.fontStyle = FontStyles.Bold;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = Text;
        titleTmp.raycastTarget = false;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(-96f, 72f);
        titleRt.anchoredPosition = new Vector2(0f, -48f);

        // Two full-width option buttons + a cancel button below.
        // Touch height 132 units (~44dp) — comfortably above the 88px minimum.
        var fileBtn = BuildButton(sheetGo, "FileButton", "Файл",
            fill: OptionFill, labelColor: Text, isBold: false,
            anchoredY: -168f, height: 132f);
        var galleryBtn = BuildButton(sheetGo, "GalleryButton", "Фото из галереи",
            fill: OptionFill, labelColor: Text, isBold: false,
            anchoredY: -324f, height: 132f);
        var cancelBtn = BuildButton(sheetGo, "CancelButton", "Отмена",
            fill: CancelFill, labelColor: TextMuted, isBold: true,
            anchoredY: -504f, height: 132f);

        // Controller + serialized wiring.
        var sheet = container.AddComponent<UploadSourceSheet>();
        var so = new SerializedObject(sheet);
        so.FindProperty("sheetRoot").objectReferenceValue = sheetRt;
        so.FindProperty("fileButton").objectReferenceValue = fileBtn;
        so.FindProperty("galleryButton").objectReferenceValue = galleryBtn;
        so.FindProperty("cancelButton").objectReferenceValue = cancelBtn;
        so.FindProperty("scrimBehind").objectReferenceValue = scrimGo;
        so.FindProperty("scrimBehindGroup").objectReferenceValue = scrimCg;
        so.FindProperty("scrimBehindFinger").objectReferenceValue = scrimFinger;
        so.ApplyModifiedPropertiesWithoutUndo();

        return sheet;
    }

    // Full-width, top-anchored button with a centered label. anchoredY is the
    // distance from the sheet's top edge to the button's top.
    private static Button BuildButton(GameObject sheet, string goName, string label,
        Color fill, Color labelColor, bool isBold, float anchoredY, float height)
    {
        var go = NewChild(sheet.transform, goName, out RectTransform rt);
        var img = go.AddComponent<Image>();
        img.color = fill;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(-96f, height);
        rt.anchoredPosition = new Vector2(0f, anchoredY);
        AddRoundedCorners(go, height * 0.5f); // pill-shaped

        var labelGo = NewChild(go.transform, "Label", out RectTransform labelRt);
        StretchFill(labelRt);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 44f;
        tmp.fontStyle = isBold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = labelColor;
        tmp.raycastTarget = false;

        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.ColorTint;
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.94f, 0.94f, 0.95f, 1f);
        colors.pressedColor = new Color(0.86f, 0.86f, 0.88f, 1f);
        btn.colors = colors;
        return btn;
    }

    private static GameObject NewChild(Transform parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static ImageWithRoundedCorners AddRoundedCorners(GameObject go, float radius)
    {
        var r = go.GetComponent<ImageWithRoundedCorners>();
        if (r == null) r = go.AddComponent<ImageWithRoundedCorners>();
        r.radius = radius;
        return r;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}
#endif

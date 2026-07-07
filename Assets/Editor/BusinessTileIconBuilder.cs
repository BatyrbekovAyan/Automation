using System.IO;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Restructures the bot-creation wizard's BusinessTypeButtonTemplate row so each
/// business-type tile carries a leading icon squircle (tileColor bg + white glyph),
/// matching the ProfileSubPages row style. Manager.PopulateBusinessTypes stamps the
/// per-entry sprite and tileColor onto the clones at runtime (looked up by name:
/// "IconBg"/"Icon"), so this builder only shapes the template. Idempotent:
/// delete-and-rebuild of the IconBg child, ensure-style for layout components.
/// </summary>
public static class BusinessTileIconBuilder
{
    private const float IconSize = 100f;
    private const float IconRadius = 28f;
    private const float IconGlyphInset = 24f;

    private const string IconsDir = "Assets/Images/BusinessIcons";
    private const string BusinessTypesAssetPath = "Assets/Data/BusinessTypes.asset";

    [MenuItem("Tools/Business Selector/Build Tile Icons")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        EnsureIconImportSettings();

        var manager = Object.FindFirstObjectByType<Manager>(FindObjectsInactive.Include);
        if (manager == null)
            throw new System.InvalidOperationException("Manager not found — is Main.unity open?");

        var so = new SerializedObject(manager);
        var template = so.FindProperty("BusinessTypeButtonTemplate").objectReferenceValue as GameObject;
        if (template == null)
            throw new System.InvalidOperationException("Manager.BusinessTypeButtonTemplate is unwired.");

        // Idempotent teardown.
        Transform stale = template.transform.Find("IconBg");
        if (stale != null) Object.DestroyImmediate(stale.gameObject);

        // Row layout: leading squircle + label, profile-row metrics (160-tall row).
        var hlg = template.GetComponent<HorizontalLayoutGroup>();
        if (hlg == null) hlg = template.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(44, 54, 30, 30);
        hlg.spacing = 40f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        // Preview values from the first entry; runtime overwrites per clone.
        var types = AssetDatabase.LoadAssetAtPath<BusinessTypesSO>(BusinessTypesAssetPath);
        Sprite previewGlyph = null;
        Color previewColor = new Color(0.56f, 0.56f, 0.58f);
        if (types != null && types.TryGetByIndex(0, out var first))
        {
            previewGlyph = first.sprite;
            previewColor = first.tileColor;
        }

        var iconBg = NewChild(template, "IconBg", out _);
        SetPreferredSize(iconBg, IconSize, IconSize);
        iconBg.AddComponent<Image>().color = previewColor;
        var rounded = iconBg.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = IconRadius;
        iconBg.transform.SetSiblingIndex(0);

        var icon = NewChild(iconBg, "Icon", out var iconRt);
        StretchFill(iconRt, IconGlyphInset);
        var iconImg = icon.AddComponent<Image>();
        iconImg.sprite = previewGlyph;
        iconImg.color = Color.white;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;

        var label = template.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.alignment = TextAlignmentOptions.MidlineLeft;
            var le = label.GetComponent<LayoutElement>();
            if (le == null) le = label.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
        }

        // Radius bake needs sized rects.
        Canvas.ForceUpdateCanvases();
        rounded.Validate();
        rounded.Refresh();

        EditorSceneManager.MarkSceneDirty(template.scene);
        EditorSceneManager.SaveScene(template.scene);
        Debug.Log("[BusinessTileIconBuilder] Template rebuilt (IconBg + Icon) and scene saved.");
    }

    private static void EnsureIconImportSettings()
    {
        if (!Directory.Exists(IconsDir)) return;

        foreach (string path in Directory.GetFiles(IconsDir, "BT_*.png"))
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

    private static GameObject NewChild(GameObject parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        rt = go.GetComponent<RectTransform>();
        return go;
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

    private static void StretchFill(RectTransform rt, float uniformInset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(uniformInset, uniformInset);
        rt.offsetMax = new Vector2(-uniformInset, -uniformInset);
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Points the bottom nav bar at the generated icon set in Assets/Images/Nav and
/// squares up the icon rects. Additive and idempotent — it only writes the two
/// sprite refs and the size of each tab's icon Image, never rebuilds the bar.
///
/// The rect fix is not cosmetic: the bar shipped with icon widths of 80/52/80/80
/// against a height of 64, because each tab carried an unrelated stock PNG of its
/// own aspect ratio. Feeding square glyphs into those rects would stretch them.
///
/// Glyph geometry is authored in Tools/icon-lab/glyphs and published to
/// Assets/Images/Nav by Tools/icon-lab/publish.js — run that before this.
/// </summary>
public static class NavIconSetBuilder
{
    private const string NavDir = "Assets/Images/Nav";
    private const float IconSide = 64f;

    /// <summary>Tab order is the scene's, mirrored by BottomTabManager's index constants.</summary>
    private static readonly string[] GlyphPerTab = { "nav_chats", "nav_dashboard", "nav_bots", "nav_profile" };

    [MenuItem("Tools/Nav Icons/Apply Icon Set")]
    public static void Apply()
    {
        AssetDatabase.Refresh();
        EnsureImportSettings();

        var bar = Object.FindFirstObjectByType<BottomTabManager>(FindObjectsInactive.Include);
        if (bar == null)
            throw new System.InvalidOperationException("BottomTabManager not found — is Main.unity open?");

        var so = new SerializedObject(bar);
        var tabs = so.FindProperty("tabs");
        if (tabs.arraySize != GlyphPerTab.Length)
            throw new System.InvalidOperationException(
                $"Expected {GlyphPerTab.Length} tabs, scene has {tabs.arraySize} — icon mapping is index-based.");

        int defaultTab = so.FindProperty("defaultTabIndex").intValue;

        for (int i = 0; i < tabs.arraySize; i++)
        {
            var tab = tabs.GetArrayElementAtIndex(i);
            string glyph = GlyphPerTab[i];
            Sprite outline = LoadGlyph($"{glyph}_outline");
            Sprite filled = LoadGlyph($"{glyph}_filled");
            tab.FindPropertyRelative("inactiveIcon").objectReferenceValue = outline;
            tab.FindPropertyRelative("activeIcon").objectReferenceValue = filled;

            bool isActive = i == defaultTab;
            var icon = tab.FindPropertyRelative("iconImage").objectReferenceValue as Image;
            SquareUpIcon(icon, glyph, isActive ? filled : outline, isActive);
        }

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(bar.gameObject.scene);
        EditorSceneManager.SaveScene(bar.gameObject.scene);
        Debug.Log($"[NavIconSetBuilder] Wired {tabs.arraySize} tabs to the icon set and squared their rects.");
    }

    private static Sprite LoadGlyph(string fileName)
    {
        string path = $"{NavDir}/{fileName}.png";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            throw new FileNotFoundException($"Missing nav glyph {path} — run Tools/icon-lab/publish.js first.");
        return sprite;
    }

    /// <summary>
    /// Forces the icon rect to a square so a square glyph is not stretched, and
    /// turns on preserveAspect so a future non-square asset fails visibly small
    /// rather than silently distorted.
    ///
    /// Also stamps the sprite and tint the tab shows at rest. BottomTabManager
    /// assigns both at runtime, so leaving them alone still works on device —
    /// but the scene view would keep rendering the stale sprite in plain white,
    /// which reads as "the new icons did not apply" and hides the glyph
    /// entirely once the editor is looking at a light-theme bar.
    /// </summary>
    private static void SquareUpIcon(Image icon, string glyph, Sprite restingSprite, bool isActive)
    {
        if (icon == null)
        {
            Debug.LogWarning($"[NavIconSetBuilder] Tab '{glyph}' has no iconImage wired — rect left alone.");
            return;
        }

        var rt = (RectTransform)icon.transform;
        var rectSo = new SerializedObject(rt);
        rectSo.FindProperty("m_SizeDelta").vector2Value = new Vector2(IconSide, IconSide);
        rectSo.ApplyModifiedProperties();

        var imgSo = new SerializedObject(icon);
        imgSo.FindProperty("m_PreserveAspect").boolValue = true;
        imgSo.FindProperty("m_Sprite").objectReferenceValue = restingSprite;
        imgSo.FindProperty("m_Color").colorValue = NavTabPalette.ColorFor(isActive);
        imgSo.ApplyModifiedProperties();
    }

    private static void EnsureImportSettings()
    {
        if (!Directory.Exists(NavDir)) return;

        foreach (string file in Directory.GetFiles(NavDir, "nav_*.png"))
        {
            string assetPath = file.Replace('\\', '/');
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) continue;

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
}

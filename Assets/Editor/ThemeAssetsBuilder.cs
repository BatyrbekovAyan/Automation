using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates or re-seeds the two ThemeAsset instances in Assets/Resources/Theme/.
///
/// SCENE-SAFE BY CONSTRUCTION: this builder writes ONLY the two .asset files.
/// It never opens, edits or saves Main.unity, so it cannot clobber any of the
/// hand-tuning done on scene objects after other builders ran.
///
/// Idempotent: re-running overwrites the token VALUES on the existing assets
/// (keeping their GUIDs, so nothing referencing them breaks).
///
/// Light = today's app palette (identical to ThemeAsset's code defaults) —
/// creating it changes nothing visually. Dark = the approved
/// «Графит» × «Чернильный» set from docs/design/ui-restyle (gen-accent dump).
/// </summary>
public static class ThemeAssetsBuilder
{
    private const string Dir = "Assets/Resources/Theme";

    [MenuItem("Tools/Theme/Create Or Update Theme Assets")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Dir))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateFolder("Assets/Resources", "Theme");
        }

        var light = LoadOrCreate($"{Dir}/Theme_Light.asset");
        SeedLight(light);

        var dark = LoadOrCreate($"{Dir}/Theme_Dark.asset");
        SeedDark(dark);

        EditorUtility.SetDirty(light);
        EditorUtility.SetDirty(dark);
        AssetDatabase.SaveAssets();
        Debug.Log("[ThemeAssetsBuilder] Theme_Light + Theme_Dark created/updated. Scene untouched.");
    }

    private static ThemeAsset LoadOrCreate(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<ThemeAsset>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<ThemeAsset>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    /// <summary>Today's app values — MUST stay byte-identical to ThemeAsset's code defaults.</summary>
    private static void SeedLight(ThemeAsset t)
    {
        t.background   = Hex("#F0F2F5");
        t.surface      = Hex("#FFFFFF");
        t.hairline     = Hex("#E4E6EB");
        t.border       = Hex("#E1E5EC");
        t.inputBorder  = Hex("#C6CBD3");

        t.inkPrimary   = Hex("#000000");
        t.inkSecondary = Hex("#666666");
        t.inkTertiary  = Hex("#8E8E93");

        t.accentFill   = Hex("#1B7CEB");
        t.accentText   = Hex("#1B7CEB");
        t.accentOnFill = Hex("#FFFFFF");

        t.switchOffTrack = Hex("#E9E9EA");

        t.statusOrderCollected = Hex("#34C759");
        t.statusOwnerNeeded    = Hex("#F57C00");
        t.statusInDialog       = Hex("#007AFF");
        t.statusClientSilent   = Hex("#8E8E93");
        t.statusQuestionClosed = Hex("#65676B");

        t.destructive = Hex("#E24848");
        t.positiveBg  = Hex("#E8F8EE");
        t.positiveInk = Hex("#206A2C");

        t.chatWallpaper  = Hex("#F5F2EA");
        t.bubbleIncoming = Hex("#FFFFFF");
        t.bubbleOutgoing = Hex("#C5EEB6");
    }

    /// <summary>«Графит» × «Чернильный» — approved dark set (docs/design/ui-restyle).</summary>
    private static void SeedDark(ThemeAsset t)
    {
        t.background   = Hex("#0E1116");
        t.surface      = Hex("#171C24");
        t.hairline     = Hex("#242C38");
        t.border       = Hex("#333E4E");
        t.inputBorder  = Hex("#556882");

        t.inkPrimary   = Hex("#ECF0F6");
        t.inkSecondary = Hex("#9AA6B8");
        t.inkTertiary  = Hex("#79869A");

        t.accentFill   = Hex("#3E61C6");
        t.accentText   = Hex("#5981D6");
        t.accentOnFill = Hex("#FFFFFF");

        t.switchOffTrack = Hex("#556882");

        // Dark-theme status siblings: same semantics, tuned for the dark ground.
        t.statusOrderCollected = Hex("#3A934C");
        t.statusOwnerNeeded    = Hex("#E46602");
        t.statusInDialog       = Hex("#8F7AFA");
        t.statusClientSilent   = Hex("#8A94A6");
        t.statusQuestionClosed = Hex("#9B5DE0");

        t.destructive = Hex("#A01B12");
        t.positiveBg  = Hex("#123324");
        t.positiveInk = Hex("#57DE95");

        t.chatWallpaper  = Hex("#090B0E"); // authored dark doodle wallpaper is a follow-up asset
        t.bubbleIncoming = Hex("#252A31");
        t.bubbleOutgoing = Hex("#202F59");
    }

    private static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}

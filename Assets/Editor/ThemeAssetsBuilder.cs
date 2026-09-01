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
/// Both themes are the owner-chosen «Чернильный» palette, transcribed from the
/// verified generator dump in docs/design/ui-restyle (every value cleared the
/// contrast + collision gates there). Light = «Петроль» ground, dark = «Графит».
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
        t.background = Hex("#F4F8F8");
        t.surface = Hex("#FFFFFF");
        t.hairline = Hex("#E3EDED");
        t.border = Hex("#C4D6D7");
        t.inputBorder = Hex("#6F9B9D");
        t.inkPrimary = Hex("#08181B");
        t.inkSecondary = Hex("#4C6265");
        t.inkTertiary = Hex("#64797C");
        t.accentFill = Hex("#243A7A");
        t.accentText = Hex("#243A7A");
        t.accentOnFill = Hex("#FFFFFF");
        t.switchOffTrack = Hex("#6F9B9D");
        t.statusOrderCollected = Hex("#3A934C");
        t.statusOwnerNeeded = Hex("#E46602");
        t.statusInDialog = Hex("#3B72E6");
        t.statusClientSilent = Hex("#8E8E93");
        t.statusQuestionClosed = Hex("#65676B");
        t.destructive = Hex("#A01B12");
        t.positiveBg = Hex("#E6F6EE");
        t.positiveInk = Hex("#0A6B3E");
        t.chatWallpaper = Hex("#F3F1EB");     // scene truth: paper + thread bars
        t.bubbleIncoming = Hex("#FFFFFF");
        // Store audit 2026-09-01 (Apple 4.1 / Play Impersonation risk reduction): the old
        // #D8FDD4 was WhatsApp's pale-green outgoing bubble — shifted into the app's own
        // indigo family (accent #243A7A). Do not drift this back toward green.
        t.bubbleOutgoing = Hex("#DCE7FB");
        t.chatWallpaperInk = Hex("#FFFFFF");  // baked doodle art passes through
        t.bubbleBorder = Hex("#D9D4CA");      // MessageItemView's light border
        t.sendButton = Hex("#1FAA61");        // today's send-circle green
        t.accentSoft = Hex("#E8F2FD");        // today's edit-button chip
        t.destructiveSoft = Hex("#FFCED5");   // today's delete-button chip
        t.scrim = Hex("#000000");             // modal veil

    }

    /// <summary>«Графит» × «Чернильный» — approved dark set (docs/design/ui-restyle).</summary>
    private static void SeedDark(ThemeAsset t)
    {
        t.background = Hex("#0E1116");
        t.surface = Hex("#171C24");
        t.hairline = Hex("#242C38");
        t.border = Hex("#333E4E");
        t.inputBorder = Hex("#556882");
        t.inkPrimary = Hex("#ECF0F6");
        t.inkSecondary = Hex("#9AA6B8");
        t.inkTertiary = Hex("#79869A");
        t.accentFill = Hex("#3E61C6");
        t.accentText = Hex("#5981D6");
        t.accentOnFill = Hex("#FFFFFF");
        t.switchOffTrack = Hex("#556882");
        t.statusOrderCollected = Hex("#3A934C");
        t.statusOwnerNeeded = Hex("#E46602");
        t.statusInDialog = Hex("#8F7AFA");
        t.statusClientSilent = Hex("#8A94A6");
        t.statusQuestionClosed = Hex("#9B5DE0");
        // Lifted from the light theme's #A01B12, which measured 2.17:1 on the
        // dark card — unreadable, and it is the INK that sits on destructiveSoft.
        // #F2555A gives 5.07:1 on the chip and ~5.6:1 on the page ground.
        t.destructive = Hex("#F2555A");
        t.positiveBg = Hex("#123324");
        t.positiveInk = Hex("#57DE95");
        t.chatWallpaper = Hex("#090B0E");
        t.bubbleIncoming = Hex("#252A31");
        // Store audit 2026-09-01: #005C4B was byte-exact WhatsApp dark outgoing — the
        // single most WhatsApp-identified pixel in the thread. Deep indigo from the
        // accent hue (#3E61C6 family); inkPrimary keeps ~10:1 on it. Do not revert.
        t.bubbleOutgoing = Hex("#2C3A61");
        // Multiplies the baked #E5DAC6 strokes down to ≈#1C242E — the same subtle
        // paper-vs-ink ΔL the light wallpaper has, no dark art regen needed.
        t.chatWallpaperInk = Hex("#1F2A3B");
        t.bubbleBorder = Hex("#333B45");      // a step lighter than the bubble fill
        t.sendButton = Hex("#128A50");        // same hue, dropped for the dark panel
        t.accentSoft = Hex("#1E2A44");        // navy-tinted chip on the dark card
        t.destructiveSoft = Hex("#2A1719");   // red-tinted chip; 5.07:1 under destructive
        // Deliberately identical to light: a veil is black in both themes. The
        // dimmed dark ground still separates from the #171C24 card above it.
        t.scrim = Hex("#000000");

    }

    private static Color Hex(string hex) =>
        ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}

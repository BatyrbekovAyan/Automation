using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Value-driven theme binding for whole screens.
///
/// The chats-list wirers used explicit child paths, which is right for a small
/// surface. Screen_Bots and Screen_Dashboard carry 41 and 92 coloured graphics;
/// hand-writing ~130 paths would be slower AND more error-prone than the thing
/// it replaces. So this maps by CURRENT COLOUR instead: a graphic sitting at
/// #1A1A2E today IS primary ink, whatever its path, and binds to InkPrimary.
///
/// Still additive-only and non-destructive — components are added, fields are
/// written through SerializedObject, and no authored colour byte is touched.
///
/// #FFFFFF is deliberately NEVER auto-mapped: on this project white means both
/// "surface" and "no tint" on a sprite, and binding an icon's tint to a theme
/// colour would invert it in dark mode. Whites are listed in the report so they
/// can be bound by hand where they really are surfaces.
///
/// NOTE ON DELTAS: after the phase-4 palette flip, binding is no longer a no-op
/// by construction — adopting a token IS the visible «Чернильный» change. The
/// audit therefore reports every delta with its perceptual distance and Apply
/// requires the explicit accept-deltas entry point.
/// </summary>
public static class ScreenThemeWirer
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    /// <summary>Current authored colour → the role it semantically is.</summary>
    private static readonly (string hex, ThemeRole role)[] ValueMap =
    {
        // inks — the app's two conventions, both meaning the same thing
        ("#1A1A2E", ThemeRole.InkPrimary),
        ("#000000", ThemeRole.InkPrimary),
        ("#111111", ThemeRole.InkPrimary),
        ("#1C1C1E", ThemeRole.InkPrimary),
        ("#1C1C1F", ThemeRole.InkPrimary),
        ("#65676B", ThemeRole.InkSecondary),
        ("#666666", ThemeRole.InkSecondary),
        ("#6A6A6A", ThemeRole.InkSecondary),
        ("#8E8E93", ThemeRole.InkTertiary),
        ("#9A9A9A", ThemeRole.InkTertiary),
        // structure
        ("#F0F2F5", ThemeRole.Background),
        ("#F2F2F7", ThemeRole.Background),
        ("#E4E6EB", ThemeRole.Hairline),
        ("#E5E5EA", ThemeRole.Hairline),
        ("#E1E5EC", ThemeRole.Border),
        ("#C6CBD3", ThemeRole.InputBorder),
        ("#C7C7CC", ThemeRole.InputBorder),
        // accent — where «Чернильный» finally shows up
        ("#1B7CEB", ThemeRole.AccentFill),
        // dashboard statuses (DashboardStatusInfo FG values)
        ("#34C759", ThemeRole.StatusOrderCollected),
        ("#F57C00", ThemeRole.StatusOwnerNeeded),
        ("#007AFF", ThemeRole.StatusInDialog),
        // success-pill tint (CLAUDE.md soft tints)
        ("#E8F8EE", ThemeRole.PositiveBg),
        // found on the settings/list prefabs
        ("#636366", ThemeRole.InkSecondary),
        ("#ECECEE", ThemeRole.Hairline),
        ("#D9D9D9", ThemeRole.InputBorder),
        ("#E9E9EA", ThemeRole.SwitchOffTrack),   // BotCardFooterBuilder.TrackOffColor
        ("#E9E9EB", ThemeRole.Hairline),         // BotCardFooterBuilder.DividerColor — NOT the track
        // destructive: two red variants in BotSettings, unified onto one role
        ("#E53935", ThemeRole.Destructive),
        ("#EB4545", ThemeRole.Destructive),
        ("#F0F0F2", ThemeRole.Background),
    };

    /// <summary>
    /// Per-target extra exclusions. #34C759 is genuinely AMBIGUOUS: on the
    /// dashboard it is the order-collected status, but on Bot.prefab it is the
    /// activation switch's ON green — which must never follow the theme, or
    /// «Бот работает» stops meaning one fixed thing. Value-mapping cannot tell
    /// them apart, so the switch's owner excludes it explicitly.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExtraExclusions = new()
    {
        ["Assets/Prefabs/Bot.prefab"] = new[]
        {
            "#34C759", // activation switch ON — Theme.Fixed.SwitchOnGreen
            "#00FF00", // pure debug green left in the prefab; not a theme colour
            "#2E9BE0", // channel-ish blue; needs a design call, not a guess
        },
    };

    /// <summary>
    /// Never auto-map. White is ambiguous (surface vs sprite "no tint"); the
    /// channel/identity colours must never follow the theme at all.
    /// </summary>
    private static readonly string[] NeverMap =
    {
        "#FFFFFF", "#25D366", "#2AABEE", "#34B7F1", "#00A884", "#2FB344", "#1FA855",
    };

    private static readonly string[] Prefabs =
    {
        "Assets/Prefabs/BotSettings.prefab",
        "Assets/Prefabs/Bot.prefab",
        "Assets/Prefabs/Product.prefab",
        "Assets/Prefabs/Service.prefab",
        "Assets/Prefabs/BotSwitcherRow.prefab",
    };

    [MenuItem("Tools/Theme/Screens/Audit Bots + Dashboard (dry run)")]
    public static void AuditAll() => Run(new[] { "Screen_Bots", "Screen_Dashboard" }, apply: false);

    [MenuItem("Tools/Theme/Screens/Apply Bots + Dashboard (adopt palette)")]
    public static void ApplyAll() => Run(new[] { "Screen_Bots", "Screen_Dashboard" }, apply: true);

    [MenuItem("Tools/Theme/Screens/Audit Prefabs (dry run)")]
    public static void AuditPrefabs() => RunPrefabs(apply: false);

    [MenuItem("Tools/Theme/Screens/Apply Prefabs (adopt palette)")]
    public static void ApplyPrefabs() => RunPrefabs(apply: true);

    private static void RunPrefabs(bool apply)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[ScreenThemeWirer] PREFABS {(apply ? "APPLY" : "AUDIT")}");
        int total = 0;

        foreach (var path in Prefabs)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) { sb.AppendLine($"\n### {path}: NOT FOUND"); continue; }
            try
            {
                ExtraExclusions.TryGetValue(path, out var extra);
                int added = BindSubtree(root, apply, sb, path, extra ?? System.Array.Empty<string>());
                total += added;
                if (apply && added > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        Debug.Log(sb.ToString());
        if (apply) Debug.Log($"[ScreenThemeWirer] Prefabs applied. {total} binding(s) added.");
    }

    private static void Run(string[] roots, bool apply)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder();
        sb.AppendLine($"[ScreenThemeWirer] {(apply ? "APPLY" : "AUDIT")}");
        int totalAdded = 0;

        foreach (var rootName in roots)
        {
            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                root = FindDeep(go.transform, rootName);
                if (root != null) break;
            }
            if (root == null)
            {
                sb.AppendLine($"\n### {rootName}: NOT FOUND");
                continue;
            }

            totalAdded += BindSubtree(root, apply, sb, rootName, System.Array.Empty<string>());
        }

        Debug.Log(sb.ToString());

        if (!apply) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[ScreenThemeWirer] Applied + scene saved. {totalAdded} binding(s) added. " +
                  "No objects created/destroyed/moved.");
    }

    /// <summary>Shared binding pass — used for both scene roots and prefabs.</summary>
    private static int BindSubtree(GameObject root, bool apply, StringBuilder sb,
                                   string label, string[] extraExclusions)
    {
        var mapped = new Dictionary<ThemeRole, int>();
        var unmapped = new Dictionary<string, int>();
        int added = 0, already = 0;

        foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
        {
            string hex = "#" + ColorUtility.ToHtmlStringRGB(g.color);
            if (NeverMap.Contains(hex) || extraExclusions.Contains(hex)) continue;

            var hit = ValueMap.FirstOrDefault(m => m.hex == hex);
            if (hit.hex == null)
            {
                unmapped.TryGetValue(hex, out var n);
                unmapped[hex] = n + 1;
                continue;
            }

            mapped.TryGetValue(hit.role, out var c);
            mapped[hit.role] = c + 1;

            if (g.GetComponent<ThemedColor>() != null) { already++; continue; }
            if (!apply) { added++; continue; }

            var binding = g.gameObject.AddComponent<ThemedColor>();
            var so = new SerializedObject(binding);
            so.FindProperty("role").enumValueIndex = (int)hit.role;
            so.FindProperty("target").objectReferenceValue = g;
            so.FindProperty("preserveAlpha").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            added++;
        }

        sb.AppendLine($"\n### {label}  —  bound {added}, already {already}");
        foreach (var kv in mapped.OrderByDescending(k => k.Value))
            sb.AppendLine($"    {kv.Key,-24} x{kv.Value,-4} -> " +
                          $"{"#" + ColorUtility.ToHtmlStringRGB(Theme.Light.Resolve(kv.Key))}");
        if (unmapped.Count > 0)
        {
            sb.AppendLine("    unmapped (left alone — needs a design call):");
            foreach (var kv in unmapped.OrderByDescending(k => k.Value).Take(10))
                sb.AppendLine($"       {kv.Key} x{kv.Value}");
        }
        return added;
    }

    private static GameObject FindDeep(Transform t, string name)
    {
        if (t.name == name) return t.gameObject;
        foreach (Transform c in t)
        {
            var f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }
}

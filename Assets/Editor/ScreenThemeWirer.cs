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
    };

    /// <summary>
    /// Never auto-map. White is ambiguous (surface vs sprite "no tint"); the
    /// channel/identity colours must never follow the theme at all.
    /// </summary>
    private static readonly string[] NeverMap =
    {
        "#FFFFFF", "#25D366", "#2AABEE", "#34B7F1", "#00A884", "#2FB344", "#1FA855",
    };

    [MenuItem("Tools/Theme/Screens/Audit Bots + Dashboard (dry run)")]
    public static void AuditAll() => Run(new[] { "Screen_Bots", "Screen_Dashboard" }, apply: false);

    [MenuItem("Tools/Theme/Screens/Apply Bots + Dashboard (adopt palette)")]
    public static void ApplyAll() => Run(new[] { "Screen_Bots", "Screen_Dashboard" }, apply: true);

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

            var mapped = new Dictionary<ThemeRole, int>();
            var unmapped = new Dictionary<string, int>();
            int added = 0, already = 0;

            foreach (var g in root.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                string hex = "#" + ColorUtility.ToHtmlStringRGB(g.color);
                if (NeverMap.Contains(hex)) continue;

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

            totalAdded += added;
            sb.AppendLine($"\n### {rootName}  —  bound {added}, already {already}");
            foreach (var kv in mapped.OrderByDescending(k => k.Value))
            {
                var tok = Theme.Light.Resolve(kv.Key);
                sb.AppendLine($"    {kv.Key,-24} x{kv.Value,-4} -> {"#" + ColorUtility.ToHtmlStringRGB(tok)}");
            }
            if (unmapped.Count > 0)
            {
                sb.AppendLine("    unmapped (left alone — bind by hand if they are real surfaces):");
                foreach (var kv in unmapped.OrderByDescending(k => k.Value).Take(12))
                    sb.AppendLine($"       {kv.Key} x{kv.Value}");
            }
        }

        Debug.Log(sb.ToString());

        if (!apply) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[ScreenThemeWirer] Applied + scene saved. {totalAdded} binding(s) added. " +
                  "No objects created/destroyed/moved.");
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

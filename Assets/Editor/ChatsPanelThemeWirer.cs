using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 2, second slice: binds the chats-list CHROME in Main.unity — the panel
/// ground, the scroll viewport, the search bar and the top bar.
///
/// Same contract as <see cref="ChatsListThemeWirer"/>: additive only, never
/// creates/destroys/renames/re-parents anything, and writes the binding's fields
/// through SerializedObject so not a single authored colour byte moves. Audit
/// first; Apply refuses while anything mismatches.
///
/// Deliberately NOT bound:
///   • WhatsApp green / Telegram blue fills (channel identity — Theme.Fixed).
///   • The Авто/Вместе mode toggle greens (reply-mode semantics, owner-confirmed
///     as NOT channel-recoloured; they need their own decision, not a guess).
///   • Avatar/icon sprite tints sitting at #FFFFFF (white = "no tint"; binding
///     them to Surface would couple a sprite tint to a theme colour).
///   • EmptyState / SyncingState / Sheet_BotSwitcher / the two confirm popups —
///     separate surfaces, each with its own semantics; later passes.
/// </summary>
public static class ChatsPanelThemeWirer
{
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string PanelPath = "ChatsPanel";

    /// <summary>
    /// Path relative to the ChatsPanel GameObject → semantic role.
    /// Everything except the two InkPrimary entries is a verified visual no-op
    /// (dE <= 0.7). Those two are an owner-approved unification — see below.
    /// </summary>
    private static readonly (string path, ThemeRole role)[] Spec =
    {
        ("",                                                          ThemeRole.Surface),
        ("Scroll/Viewport",                                           ThemeRole.Surface),
        ("TopBar/Background",                                         ThemeRole.Surface),
        ("TopBar/Background/Line",                                    ThemeRole.Hairline),
        ("TopBar/LeftZone/BotSwitcherTitle/Chevron",                  ThemeRole.InkSecondary),
        ("Scroll/Viewport/Content/ChatsSearchBar/Pill/Magnifier",      ThemeRole.InkTertiary),
        ("Scroll/Viewport/Content/ChatsSearchBar/Pill/Input/Text Area/Placeholder", ThemeRole.InkTertiary),
        // Owner-approved unification onto a single InkPrimary (#000000). These two
        // are the app's only remaining near-black variants on this screen; both get
        // marginally blacker, and both must be bound for dark mode to work at all.
        ("TopBar/LeftZone/BotSwitcherTitle/BotName",                            ThemeRole.InkPrimary),
        ("Scroll/Viewport/Content/ChatsSearchBar/Pill/Input/Text Area/Text",    ThemeRole.InkPrimary),
    };

    [MenuItem("Tools/Theme/Chats Panel/Audit (dry run)")]
    public static void Audit() => Run(apply: false, acceptDeltas: false);

    [MenuItem("Tools/Theme/Chats Panel/Apply Bindings")]
    public static void Apply() => Run(apply: true, acceptDeltas: false);

    [MenuItem("Tools/Theme/Chats Panel/Apply Bindings (accept deltas)")]
    public static void ApplyAcceptingDeltas() => Run(apply: true, acceptDeltas: true);

    private static void Run(bool apply, bool acceptDeltas)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject panel = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            panel = FindDeep(root.transform, PanelPath);
            if (panel != null) break;
        }

        if (panel == null)
        {
            Debug.LogError($"[ChatsPanelThemeWirer] '{PanelPath}' not found in {ScenePath}");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[ChatsPanelThemeWirer] {(apply ? "APPLY" : "AUDIT")} — {PanelPath}");
        sb.AppendLine($"{"path",-64}{"role",-16}{"scene",-10}{"token",-10}{"dE",-6}match");
        sb.AppendLine(new string('-', 112));

        var mismatches = new List<string>();
        var missing = new List<string>();
        int toAdd = 0, bound = 0;

        foreach (var (path, role) in Spec)
        {
            var t = string.IsNullOrEmpty(path) ? panel.transform : panel.transform.Find(path);
            if (t == null)
            {
                missing.Add(path);
                sb.AppendLine($"{Label(path),-64}{role,-16}{"—",-10}{"—",-10}{"—",-6}MISSING");
                continue;
            }

            var g = t.GetComponent<Graphic>();
            if (g == null)
            {
                missing.Add(path + " (no Graphic)");
                sb.AppendLine($"{Label(path),-64}{role,-16}{"—",-10}{"—",-10}{"—",-6}NO GRAPHIC");
                continue;
            }

            Color scn = g.color, tok = Theme.Light.Resolve(role);
            int dmax = MaxByteDelta(scn, tok);
            float de = DeltaE(scn, tok);
            bool same = de <= 1.0f; // perceptual, not byte distance — see DeltaE
            if (!same) mismatches.Add($"{Label(path)}: scene {Hex(scn)} vs token {Hex(tok)} " +
                                      $"(dE {de:F1}, delta {dmax}, {role})");

            bool has = t.GetComponent<ThemedColor>() != null;
            if (has) bound++; else toAdd++;

            sb.AppendLine($"{Label(path),-64}{role,-16}{Hex(scn),-10}{Hex(tok),-10}{de,-6:F1}" +
                          $"{(same ? "yes" : "NO")}{(has ? "  [bound]" : "")}");
        }

        sb.AppendLine();
        sb.AppendLine($"bound: {bound}   to add: {toAdd}   mismatches: {mismatches.Count}   missing: {missing.Count}");
        if (mismatches.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("MISMATCHES — binding these would repaint the chrome:");
            foreach (var m in mismatches) sb.AppendLine("  " + m);
        }
        Debug.Log(sb.ToString());

        if (!apply) return;

        if (missing.Count > 0)
        {
            Debug.LogError("[ChatsPanelThemeWirer] Aborted: spec paths not found. Fix the spec, do not guess.");
            return;
        }

        if (mismatches.Count > 0 && !acceptDeltas)
        {
            Debug.LogError($"[ChatsPanelThemeWirer] Aborted: {mismatches.Count} mismatch(es) would repaint " +
                           "the chrome. Reconcile the tokens, or re-run via 'Apply Bindings (accept deltas)'.");
            return;
        }

        int added = 0;
        foreach (var (path, role) in Spec)
        {
            var t = string.IsNullOrEmpty(path) ? panel.transform : panel.transform.Find(path);
            var g = t.GetComponent<Graphic>();
            var binding = t.GetComponent<ThemedColor>();
            if (binding == null)
            {
                binding = t.gameObject.AddComponent<ThemedColor>();
                added++;
            }

            // Fields only, via SerializedObject — Configure() would repaint now and
            // rewrite the authored float. The runtime OnEnable does the painting.
            var so = new SerializedObject(binding);
            so.FindProperty("role").enumValueIndex = (int)role;
            so.FindProperty("target").objectReferenceValue = g;
            so.FindProperty("preserveAlpha").boolValue = true; // TopBar Line is a=0.51
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[ChatsPanelThemeWirer] Applied + scene saved. Added {added} binding(s). " +
                  "No objects created/destroyed/moved.");
    }

    private static string Label(string path) => string.IsNullOrEmpty(path) ? "(ChatsPanel root)" : path;

    private static GameObject FindDeep(Transform t, string name)
    {
        if (t.name == name) return t.gameObject;
        foreach (Transform c in t)
        {
            var found = FindDeep(c, name);
            if (found != null) return found;
        }
        return null;
    }

    private static int MaxByteDelta(Color a, Color b)
    {
        int dr = Mathf.Abs(Mathf.RoundToInt(a.r * 255) - Mathf.RoundToInt(b.r * 255));
        int dg = Mathf.Abs(Mathf.RoundToInt(a.g * 255) - Mathf.RoundToInt(b.g * 255));
        int db = Mathf.Abs(Mathf.RoundToInt(a.b * 255) - Mathf.RoundToInt(b.b * 255));
        return Mathf.Max(dr, Mathf.Max(dg, db));
    }

    /// <summary>
    /// Perceptual distance in OKLab ×100. Byte distance is a poor gate: the same
    /// 5/255 step is invisible on a mid grey (ΔE 0.7) but large on a near-black,
    /// because OKLab's lightness axis is compressed down there. ~2.3 is the
    /// classic just-noticeable difference; this wirer accepts ≤1.0.
    /// </summary>
    private static float DeltaE(Color a, Color b)
    {
        var (l1, a1, b1) = ToOkLab(a);
        var (l2, a2, b2) = ToOkLab(b);
        float dl = l1 - l2, da = a1 - a2, db = b1 - b2;
        return Mathf.Sqrt(dl * dl + da * da + db * db) * 100f;
    }

    private static (float l, float a, float b) ToOkLab(Color c)
    {
        float r = Linear(c.r), g = Linear(c.g), bl = Linear(c.b);
        float l = Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * bl);
        float m = Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * bl);
        float s = Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * bl);
        return (0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    private static float Linear(float c) =>
        c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float Cbrt(float v) => v < 0f ? -Mathf.Pow(-v, 1f / 3f) : Mathf.Pow(v, 1f / 3f);

    private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
}

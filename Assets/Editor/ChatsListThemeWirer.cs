using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 2 of the restyle: attach <see cref="ThemedColor"/> bindings to the
/// chats-list row (Assets/Prefabs/ChatItem.prefab).
///
/// ADDITIVE ONLY. Never creates, destroys, renames or re-parents a GameObject
/// and never edits a value other than adding the binding component — the prefab
/// carries hand-tuning done after the original builders ran, and that must
/// survive untouched.
///
/// SAFETY GATE: Apply refuses to run while any element's current colour differs
/// from the token it would bind to, because ThemedColor repaints on enable and
/// a mismatch would be a silent visual change. Run Audit first, reconcile the
/// token values (or consciously accept the delta via "Apply (accept deltas)"),
/// then Apply.
///
/// Not covered here — these are painted by ChatItemView at bind time, so a
/// binding component would simply be overwritten. They are routed through the
/// Theme facade in code instead:
///   Time/Text (unread vs read), UnreadBadge (ChannelAccent), Avatar/Default +
///   DefaultImage (per-contact AvatarColors, which must stay multi-hue).
/// </summary>
public static class ChatsListThemeWirer
{
    private const string PrefabPath = "Assets/Prefabs/ChatItem.prefab";

    /// <summary>Child path (relative to the prefab root) → semantic role.</summary>
    private static readonly (string path, ThemeRole role)[] Spec =
    {
        ("SwipeContent",                                 ThemeRole.Surface),
        ("SwipeContent/TextBlock/TopRow/Name",           ThemeRole.InkPrimary),
        ("SwipeContent/TextBlock/Message",               ThemeRole.InkSecondary),
        ("SwipeContent/TextBlock/Divider",               ThemeRole.Hairline),
        ("DeleteButton",                                 ThemeRole.Destructive),
        ("DeleteButton/Label",                           ThemeRole.AccentOnFill),
        ("SwipeContent/TextBlock/UnreadBadge/CountText", ThemeRole.AccentOnFill),
    };

    [MenuItem("Tools/Theme/Chats List/Audit (dry run)")]
    public static void Audit() => Run(apply: false, acceptDeltas: false);

    [MenuItem("Tools/Theme/Chats List/Apply Bindings")]
    public static void Apply() => Run(apply: true, acceptDeltas: false);

    [MenuItem("Tools/Theme/Chats List/Apply Bindings (accept deltas)")]
    public static void ApplyAcceptingDeltas() => Run(apply: true, acceptDeltas: true);

    private static void Run(bool apply, bool acceptDeltas)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[ChatsListThemeWirer] Could not load {PrefabPath}");
            return;
        }

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[ChatsListThemeWirer] {(apply ? "APPLY" : "AUDIT")} — {PrefabPath}");
            sb.AppendLine($"{"path",-46}{"role",-22}{"current",-10}{"token",-10}match");
            sb.AppendLine(new string('-', 96));

            var mismatches = new List<string>();
            var missing = new List<string>();
            int wouldAdd = 0, alreadyBound = 0;

            foreach (var (path, role) in Spec)
            {
                var t = root.transform.Find(path);
                if (t == null)
                {
                    missing.Add(path);
                    sb.AppendLine($"{path,-46}{role,-22}{"—",-10}{"—",-10}MISSING");
                    continue;
                }

                var g = t.GetComponent<Graphic>();
                if (g == null)
                {
                    missing.Add(path + " (no Graphic)");
                    sb.AppendLine($"{path,-46}{role,-22}{"—",-10}{"—",-10}NO GRAPHIC");
                    continue;
                }

                Color current = g.color;
                Color token = Theme.Light.Resolve(role); // audit against the LIGHT theme = today's look
                bool same = Approximately(current, token);
                if (!same) mismatches.Add($"{path}: scene {Hex(current)} vs token {Hex(token)} ({role})");

                bool bound = t.GetComponent<ThemedColor>() != null;
                if (bound) alreadyBound++; else wouldAdd++;

                sb.AppendLine($"{path,-46}{role,-22}{Hex(current),-10}{Hex(token),-10}" +
                              $"{(same ? "yes" : "NO")}{(bound ? "  [already bound]" : "")}");
            }

            sb.AppendLine();
            sb.AppendLine($"bindings present: {alreadyBound}   to add: {wouldAdd}   " +
                          $"mismatches: {mismatches.Count}   missing: {missing.Count}");

            if (mismatches.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("MISMATCHES — binding these would repaint the row:");
                foreach (var m in mismatches) sb.AppendLine("  " + m);
            }

            Debug.Log(sb.ToString());

            if (!apply) return;

            if (missing.Count > 0)
            {
                Debug.LogError("[ChatsListThemeWirer] Aborted: spec paths not found. " +
                               "The prefab hierarchy changed — fix the spec, do not guess.");
                return;
            }

            if (mismatches.Count > 0 && !acceptDeltas)
            {
                Debug.LogError($"[ChatsListThemeWirer] Aborted: {mismatches.Count} colour mismatch(es). " +
                               "Applying would silently change the row's appearance. Either re-seed the " +
                               "light tokens to the scene values (keeps this a no-op) or re-run via " +
                               "'Apply Bindings (accept deltas)' to adopt the token values deliberately.");
                return;
            }

            int added = 0;
            foreach (var (path, role) in Spec)
            {
                var t = root.transform.Find(path);
                var g = t.GetComponent<Graphic>();
                var existing = t.GetComponent<ThemedColor>();
                if (existing == null)
                {
                    existing = t.gameObject.AddComponent<ThemedColor>();
                    added++;
                }

                // Write the binding's fields ONLY, via SerializedObject. Deliberately
                // NOT Configure() — that repaints immediately, which would rewrite the
                // authored m_Color float (e.g. 0.886 → 0.8862745). Same colour once
                // quantised, but it is still a write to a hand-tuned value, and the
                // prefab diff must stay purely additive. The runtime OnEnable paints.
                var so = new SerializedObject(existing);
                so.FindProperty("role").enumValueIndex = (int)role;
                so.FindProperty("target").objectReferenceValue = g;
                so.FindProperty("preserveAlpha").boolValue = true; // alphas are hand-tuned
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[ChatsListThemeWirer] Applied. Added {added} ThemedColor binding(s), " +
                      $"re-configured {Spec.Length - added}. No objects created/destroyed/moved.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.002f && Mathf.Abs(a.g - b.g) < 0.002f && Mathf.Abs(a.b - b.b) < 0.002f;

    private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
}

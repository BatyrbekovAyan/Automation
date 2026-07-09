#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using System.Text;

/// <summary>
/// Idempotent tool: wires thin line/divider/border objects across the open scene and the
/// known prefabs to a whole-pixel snap component. Two routes:
///  • Route 1 → <see cref="PixelSnapLine"/> when thickness lives in the RectTransform's fixed
///    (non-stretched, non-layout-controlled) axis with a small sizeDelta.
///  • Route 2 → <see cref="PixelSnapLayoutThickness"/> when thickness is a
///    <c>LayoutElement.preferredHeight</c> driven by a parent VLG (childControlHeight).
///
/// Classification reads SERIALIZED anchors + sizeDelta + the parent LayoutGroup, never the live
/// <c>RectTransform.rect</c> — most line objects in this single-scene app live inside inactive
/// (SetActive-false) panels, so their live rect is unresolved (default 100x100 or anchor-driven
/// zero). Serialized values are valid regardless of active state.
///
/// Anything that isn't a thin single-axis line (both axes long = frame/container, both thin,
/// invisible spacers, or non-line names) is FLAGGED, never modified — a human resolves flags
/// from the log. Design thickness is stamped ONLY when a component is newly added (re-reading
/// the [ExecuteAlways]-snapped live geometry on a re-run would drift the pristine value).
///
/// Run "Tools/Pixel Snap/Report (dry run)" first to review decisions without mutating,
/// then "Tools/Pixel Snap/Wire All" to apply.
/// </summary>
public static class PixelSnapWirer
{
    private const float MaxThicknessUnits = 8f; // thicker than this isn't a hairline; flag it
    private const float StretchEps = 0.0001f;   // anchor span above this = stretched axis

    private static readonly string[] PrefabPaths =
    {
        "Assets/Prefabs/ChatItem.prefab",
        "Assets/Prefabs/Bot.prefab",
        "Assets/Prefabs/BotSettings.prefab",
        "Assets/Prefabs/DateSeparator.prefab",
        "Assets/Prefabs/UnreadSeparator.prefab",
    };

    [MenuItem("Tools/Pixel Snap/Report (dry run)")]
    public static void Report() => Run(apply: false);

    [MenuItem("Tools/Pixel Snap/Wire All")]
    public static void WireAll() => Run(apply: true);

    private static void Run(bool apply)
    {
        var log = new StringBuilder();
        int wired = 0, flagged = 0;

        var scene = EditorSceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            Walk(root.transform, "scene", apply, log, ref wired, ref flagged);
        if (apply)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        foreach (var path in PrefabPaths)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            int before = wired;
            Walk(contents.transform, path, apply, log, ref wired, ref flagged);
            if (apply && wired != before) PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        if (apply) AssetDatabase.SaveAssets();
        Debug.Log($"[PixelSnapWirer] mode={(apply ? "WIRE" : "DRY-RUN")} wired={wired} flagged={flagged}\n{log}");
    }

    private static void Walk(Transform t, string where, bool apply, StringBuilder log, ref int wired, ref int flagged)
    {
        Classify(t, where, apply, log, ref wired, ref flagged);
        foreach (Transform child in t)
            Walk(child, where, apply, log, ref wired, ref flagged);
    }

    // Name gate: only objects that read as a line/divider/border/rule. Case-sensitive
    // "Line" endings deliberately skip "Outline"/"TailOutline" (bubble outlines — Workstream B).
    private static bool IsLineCandidate(string n) =>
        n == "Divider"   || n.EndsWith("Divider")   ||
        n == "Separator" || n.EndsWith("Separator") ||
        n == "Border"    || n.EndsWith("Border")    ||
        n == "Line"      || n.EndsWith("Line")      || n.StartsWith("Line") ||
        n == "Rule"      || n.EndsWith("Rule");

    private static void Classify(Transform t, string where, bool apply, StringBuilder log, ref int wired, ref int flagged)
    {
        if (t is not RectTransform rt) return;
        string n = t.name;
        if (!IsLineCandidate(n)) return;

        Vector2 aMin = rt.anchorMin, aMax = rt.anchorMax, sd = rt.sizeDelta;
        bool stretchX = (aMax.x - aMin.x) > StretchEps;
        bool stretchY = (aMax.y - aMin.y) > StretchEps;

        // A parent layout group can make an axis "long" (childControl OR forceExpand → it can be
        // the long axis in Route 1).
        var pLayout = t.parent != null ? t.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
        bool parentExpandsWidth  = pLayout != null && (pLayout.childControlWidth  || pLayout.childForceExpandWidth);
        bool parentExpandsHeight = pLayout != null && (pLayout.childControlHeight || pLayout.childForceExpandHeight);

        // A "layout-driven" thin line is one the parent VLG actually SIZES on its thin axis: it
        // controls child height, the object opts into layout (not ignoreLayout), and carries a
        // small preferredHeight. Only these belong to Route 2 — PixelSnapLine's SetSize would be
        // overwritten by the layout. An anchored line under a childControlHeight parent that opts
        // out (ignoreLayout) or has no preferredHeight is NOT layout-driven and stays Route 1.
        var le = t.GetComponent<LayoutElement>();
        bool hasGraphic = t.GetComponent<Graphic>() != null;
        bool parentControlsHeight = pLayout != null && pLayout.childControlHeight;
        bool ignoresLayout = le != null && le.ignoreLayout;
        bool layoutDrivenHeight = parentControlsHeight && !ignoresLayout
            && le != null && le.preferredHeight > 0f && le.preferredHeight <= MaxThicknessUnits;

        string geo = $"aMin={V(aMin)} aMax={V(aMax)} sizeDelta={V(sd)}";

        // Route 1 — thickness from the RectTransform's fixed axis (PixelSnapLine). The "long"
        // axis may be stretched by anchors, larger by sizeDelta, or expanded by a parent layout.
        // Exclude only genuine layout-driven-height lines (they belong to Route 2).
        bool horiz = !stretchY && !layoutDrivenHeight && sd.y > 0f && sd.y <= MaxThicknessUnits && (stretchX || parentExpandsWidth  || sd.x > sd.y);
        bool vert  = !stretchX && sd.x > 0f && sd.x <= MaxThicknessUnits && (stretchY || parentExpandsHeight || sd.y > sd.x);
        if (horiz && !vert) { WireLine(t, where, PixelSnapLine.SnapAxis.Height, sd.y, apply, log, ref wired, geo); return; }
        if (vert && !horiz) { WireLine(t, where, PixelSnapLine.SnapAxis.Width,  sd.x, apply, log, ref wired, geo); return; }

        // Route 2 — thickness from LayoutElement.preferredHeight (PixelSnapLayoutThickness).
        // Require a Graphic so invisible spacers (LayoutElements with no rendered line) are skipped.
        if (hasGraphic && layoutDrivenHeight)
        {
            WireLayout(t, where, le.preferredHeight, apply, log, ref wired, geo);
            return;
        }

        log.AppendLine($"FLAG [{where}] {n} — not a snap-able thin line ({geo})");
        flagged++;
    }

    private static void WireLine(Transform t, string where, PixelSnapLine.SnapAxis axis, float thickness,
        bool apply, StringBuilder log, ref int wired, string geo)
    {
        // Stamp designThickness ONLY when adding the component. `thickness` is read from live
        // geometry, and once a PixelSnapLine exists its [ExecuteAlways] snap has already mutated
        // that geometry to the Editor's scale factor — re-stamping would drift the design value.
        bool exists = t.GetComponent<PixelSnapLine>() != null;
        if (apply)
        {
            // Retire the legacy NativeHairline on any wired object (only ChatItem's Divider has
            // one). GetComponent(string) avoids a compile-time reference, so this tool still
            // builds after NativeHairline.cs is deleted.
            var legacy = t.GetComponent("NativeHairline");
            if (legacy != null) Object.DestroyImmediate(legacy, true);

            if (!exists)
            {
                var line = t.gameObject.AddComponent<PixelSnapLine>();
                line.EditorConfigure(axis, thickness);
                EditorUtility.SetDirty(t.gameObject);
            }
        }
        log.AppendLine($"WIRE-LINE({(exists ? "kept" : "add")}) [{where}] {t.name} — axis={axis} liveH={thickness:0.##} ({geo})");
        wired++;
    }

    private static void WireLayout(Transform t, string where, float thickness,
        bool apply, StringBuilder log, ref int wired, string geo)
    {
        bool exists = t.GetComponent<PixelSnapLayoutThickness>() != null;
        if (apply && !exists)
        {
            var snap = t.gameObject.AddComponent<PixelSnapLayoutThickness>();
            snap.EditorConfigure(thickness);
            EditorUtility.SetDirty(t.gameObject);
        }
        log.AppendLine($"WIRE-LAYOUT({(exists ? "kept" : "add")}) [{where}] {t.name} — prefH={thickness:0.##} ({geo})");
        wired++;
    }

    private static string V(Vector2 v) => $"({v.x:0.##},{v.y:0.##})";
}
#endif

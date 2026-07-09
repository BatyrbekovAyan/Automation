#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using System.Text;

/// <summary>
/// Idempotent tool: adds <see cref="PixelSnapLine"/> to thin single-axis line/divider/
/// border objects across the open scene and the known prefabs.
///
/// Classification reads the SERIALIZED anchors + sizeDelta, never the live
/// <c>RectTransform.rect</c> — most line objects in this single-scene app live inside
/// inactive (SetActive-false) panels, so their live rect is unresolved (default 100x100
/// or anchor-driven zero). Anchors + sizeDelta are valid regardless of active state.
///
/// A "line" is an object whose FIXED axis (the one not driven by stretched anchors) has a
/// small sizeDelta (1..MaxThicknessUnits) while the other axis is long (stretched, or a
/// clearly larger sizeDelta). The small fixed axis is the thickness; the object snaps that
/// axis to a whole pixel. Anything else (both axes long = a frame/container, both axes thin,
/// or non-line names) is FLAGGED, never modified — a human resolves flags from the log.
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

        // A parent layout group can drive an axis the RectTransform leaves at 0 (e.g. a
        // full-width divider whose own width is 0 but the VLG expands it).
        var pLayout = t.parent != null ? t.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
        bool parentExpandsWidth  = pLayout != null && (pLayout.childControlWidth  || pLayout.childForceExpandWidth);
        bool parentExpandsHeight = pLayout != null && (pLayout.childControlHeight || pLayout.childForceExpandHeight);

        string geo = $"aMin={V(aMin)} aMax={V(aMax)} sizeDelta={V(sd)}";

        // Route 1 — thickness from the RectTransform's fixed axis (PixelSnapLine). The "long"
        // axis may be stretched by anchors, larger by sizeDelta, or expanded by a parent layout.
        bool horiz = !stretchY && sd.y > 0f && sd.y <= MaxThicknessUnits && (stretchX || parentExpandsWidth  || sd.x > sd.y);
        bool vert  = !stretchX && sd.x > 0f && sd.x <= MaxThicknessUnits && (stretchY || parentExpandsHeight || sd.y > sd.x);
        if (horiz && !vert) { WireLine(t, where, PixelSnapLine.SnapAxis.Height, sd.y, apply, log, ref wired, geo); return; }
        if (vert && !horiz) { WireLine(t, where, PixelSnapLine.SnapAxis.Width,  sd.x, apply, log, ref wired, geo); return; }

        // Route 2 — thickness from LayoutElement.preferredHeight, driven by a parent VLG that
        // controls child height (PixelSnapLayoutThickness). Require a Graphic so invisible
        // spacers (LayoutElements with no rendered line) are not wired.
        var le = t.GetComponent<LayoutElement>();
        bool parentControlsHeight = pLayout != null && pLayout.childControlHeight;
        bool hasGraphic = t.GetComponent<Graphic>() != null;
        if (hasGraphic && parentControlsHeight && le != null && le.preferredHeight > 0f && le.preferredHeight <= MaxThicknessUnits)
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

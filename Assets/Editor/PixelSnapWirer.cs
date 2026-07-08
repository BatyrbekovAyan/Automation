#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot, idempotent tool: adds PixelSnapLine to confidently-classified thin
/// line objects across the open scene and the known prefabs, and prints a review
/// log. Ambiguous objects (Border frames, odd-named or non-thin candidates) are
/// FLAGGED, never modified — a human resolves them from the log.
/// </summary>
public static class PixelSnapWirer
{
    private const float MaxThicknessUnits = 8f; // anything thicker isn't a hairline; flag it

    private static readonly string[] PrefabPaths =
    {
        "Assets/Prefabs/ChatItem.prefab",
        "Assets/Prefabs/Bot.prefab",
        "Assets/Prefabs/BotSettings.prefab",
        "Assets/Prefabs/DateSeparator.prefab",
        "Assets/Prefabs/UnreadSeparator.prefab",
    };

    [MenuItem("Tools/Pixel Snap/Wire All")]
    public static void WireAll()
    {
        var log = new StringBuilder();
        int wired = 0, flagged = 0;

        var scene = EditorSceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            Walk(root.transform, "scene", log, ref wired, ref flagged);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        foreach (var path in PrefabPaths)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            int before = wired;
            Walk(contents.transform, path, log, ref wired, ref flagged);
            if (wired != before) PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[PixelSnapWirer] wired={wired} flagged={flagged}\n{log}");
    }

    private static void Walk(Transform t, string where, StringBuilder log, ref int wired, ref int flagged)
    {
        Classify(t, where, log, ref wired, ref flagged);
        foreach (Transform child in t)
            Walk(child, where, log, ref wired, ref flagged);
    }

    private static void Classify(Transform t, string where, StringBuilder log, ref int wired, ref int flagged)
    {
        if (t is not RectTransform rt) return;
        string n = t.name;

        // Border frames: never auto-wired — need a per-object human decision.
        if (n.StartsWith("Border"))
        {
            log.AppendLine($"FLAG [{where}] {n} — Border frame, decide manually (size={rt.rect.size})");
            flagged++;
            return;
        }

        bool looksLikeLine =
            n == "Divider" || n.EndsWith("Divider") ||
            n == "Separator" || n.EndsWith("Separator") ||
            n == "LeftLine" || n == "RightLine" ||
            n.StartsWith("Line") || n.EndsWith("Line");
        if (!looksLikeLine) return;

        float w = rt.rect.width, h = rt.rect.height;

        // Axis: explicit vertical names first, else infer from which side is thin.
        PixelSnapLine.SnapAxis axis;
        float thickness;
        if (n == "LeftLine" || n == "RightLine") { axis = PixelSnapLine.SnapAxis.Width; thickness = w; }
        else if (h > 0f && h <= MaxThicknessUnits && w > h) { axis = PixelSnapLine.SnapAxis.Height; thickness = h; }
        else if (w > 0f && w <= MaxThicknessUnits && h > w) { axis = PixelSnapLine.SnapAxis.Width;  thickness = w; }
        else
        {
            log.AppendLine($"FLAG [{where}] {n} — ambiguous aspect (w={w:0.#}, h={h:0.#}), decide manually");
            flagged++;
            return;
        }

        if (thickness <= 0f || thickness > MaxThicknessUnits)
        {
            log.AppendLine($"FLAG [{where}] {n} — thickness {thickness:0.##} out of 1..{MaxThicknessUnits} range, verify");
            flagged++;
            return;
        }

        var line = t.GetComponent<PixelSnapLine>();
        if (line == null) line = t.gameObject.AddComponent<PixelSnapLine>();
        line.EditorConfigure(axis, thickness);
        EditorUtility.SetDirty(t.gameObject);
        log.AppendLine($"WIRE [{where}] {n} — axis={axis} thickness={thickness:0.##}");
        wired++;
    }
}
#endif

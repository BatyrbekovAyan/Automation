using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot additive wirer (drill redesign 2026-08-18): assigns SuggestionsPanel.headerTitle
/// to the existing «ПРЕДЛОЖЕНИЯ» overline TMP via SerializedObject. Additive on purpose —
/// SuggestionsPanelBuilder carries uncommitted parallel work, so the scene is wired WITHOUT
/// a rebuild; fold the same stamping into the builder's BuildHeader once that work lands.
/// Idempotent: re-running re-assigns the same reference. Edit Mode only.
/// </summary>
public static class SuggestionsHeaderTitleWirer
{
    [MenuItem("Tools/Suggestions/Wire Header Title")]
    public static void Run()
    {
        var panels = Object.FindObjectsByType<SuggestionsPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (panels.Length == 0) { Debug.LogError("[HeaderTitleWirer] No SuggestionsPanel in the open scene."); return; }
        SuggestionsPanel panel = panels[0];

        TextMeshProUGUI title = FindHeaderTitle(panel);
        if (title == null)
        {
            Debug.LogError("[HeaderTitleWirer] No 'Title' TMP reading «ПРЕДЛОЖЕНИЯ» under the panel — is it built?");
            return;
        }

        var so = new SerializedObject(panel);
        SerializedProperty prop = so.FindProperty("headerTitle");
        if (prop == null) { Debug.LogError("[HeaderTitleWirer] SuggestionsPanel has no 'headerTitle' field — recompile first."); return; }
        prop.objectReferenceValue = title;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[HeaderTitleWirer] headerTitle -> {Path(title.transform)} (scene saved)");
    }

    private static TextMeshProUGUI FindHeaderTitle(SuggestionsPanel panel)
    {
        foreach (var tmp in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (tmp.name == "Title" && tmp.text == SuggestionsPanel.DefaultHeaderTitle) return tmp;
        return null;
    }

    private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
}

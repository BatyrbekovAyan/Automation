using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// [MenuItem] that attaches <see cref="SuggestionsController"/> to the composer host and wires its
/// serialized refs (panel, toggle, MessagesBottomPanel) via SerializedObject. Build-time only,
/// idempotent (reuses an existing controller). Pure Editor wiring tool — no networking.
/// </summary>
public static class SuggestionsControllerWirer
{
    [MenuItem("Tools/UI/Wire Suggestions Controller")]
    public static void Wire()
    {
        // The panel/toggle live under the (inactive) WhatsApp screen — include inactive in the search.
        var panel = Object.FindFirstObjectByType<SuggestionsPanel>(FindObjectsInactive.Include);
        var toggle = Object.FindFirstObjectByType<SemiAutoToggle>(FindObjectsInactive.Include);
        var bottomPanel = Object.FindFirstObjectByType<MessagesBottomPanel>(FindObjectsInactive.Include);

        if (panel == null || toggle == null || bottomPanel == null)
        {
            Debug.LogError("SuggestionsControllerWirer: missing dependency — " +
                           $"panel:{panel != null} toggle:{toggle != null} bottomPanel:{bottomPanel != null}. " +
                           "Run 'Tools/UI/Build Suggestions Panel' first.");
            return;
        }

        // Host the controller on the composer it drives (active while a chat is open, so OnEnable/
        // OnDisable track the live-message subscription correctly). Reuse an existing one.
        var controller = Object.FindFirstObjectByType<SuggestionsController>(FindObjectsInactive.Include);
        if (controller == null)
            controller = bottomPanel.gameObject.AddComponent<SuggestionsController>();

        var so = new SerializedObject(controller);
        so.FindProperty("_panel").objectReferenceValue = panel;
        so.FindProperty("_toggle").objectReferenceValue = toggle;
        so.FindProperty("_bottomPanel").objectReferenceValue = bottomPanel;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Selection.activeGameObject = controller.gameObject;
        Debug.Log("SuggestionsControllerWirer: wired SuggestionsController (panel, toggle, bottomPanel).");
    }
}

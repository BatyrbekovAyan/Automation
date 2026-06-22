using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-shot, idempotent scene installer for the delete-confirm popup:
/// finds the ChatListView in the open scene, builds the confirm panel under its
/// GameObject (the chat-list screen panel) via ChatDeleteConfirmBuilder, wires
/// ChatListView.deleteConfirm to it, and saves the scene. Re-running is a no-op
/// (aborts if deleteConfirm is already set).
/// </summary>
public static class ChatDeleteConfirmInstaller
{
    [MenuItem("Tools/Chat/Install Delete-Confirm Popup")]
    public static void Install()
    {
        var civ = Object.FindFirstObjectByType<ChatListView>(FindObjectsInactive.Include);
        if (civ == null)
        {
            Debug.LogError("[ChatDeleteConfirmInstaller] No ChatListView in the open scene. Aborting.");
            return;
        }

        var soCheck = new SerializedObject(civ);
        var existing = soCheck.FindProperty("deleteConfirm");
        if (existing == null)
        {
            Debug.LogError("[ChatDeleteConfirmInstaller] ChatListView has no 'deleteConfirm' field. Aborting.");
            return;
        }
        if (existing.objectReferenceValue != null)
        {
            Debug.LogWarning("[ChatDeleteConfirmInstaller] deleteConfirm already set — aborting (already installed).");
            return;
        }

        // Build the panel under the chat-list screen panel (ChatListView's GameObject).
        Selection.activeGameObject = civ.gameObject;
        ChatDeleteConfirmBuilder.Build();

        var panelGo = Selection.activeGameObject;
        var confirm = panelGo != null ? panelGo.GetComponent<ChatDeleteConfirm>() : null;
        if (confirm == null)
        {
            Debug.LogError("[ChatDeleteConfirmInstaller] Builder did not produce a ChatDeleteConfirm panel. Aborting (no wiring).");
            return;
        }

        // Keep the full-screen modal out of any parent layout group on the screen panel.
        var le = panelGo.GetComponent<LayoutElement>();
        if (le == null) le = panelGo.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        var so = new SerializedObject(civ);
        so.FindProperty("deleteConfirm").objectReferenceValue = confirm;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(civ);

        EditorSceneManager.MarkSceneDirty(civ.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[ChatDeleteConfirmInstaller] DONE — '{panelGo.name}' built under '{civ.gameObject.name}' " +
                  "and wired to ChatListView.deleteConfirm; scene saved.");
    }
}

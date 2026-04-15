#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AuthPageMigration
{
    [MenuItem("Tools/Patch Auth Pages (add KeyboardScrollFix)")]
    public static void Patch()
    {
        var manager = Object.FindFirstObjectByType<Manager>();
        if (manager == null) { Debug.LogError("[AuthPageMigration] No Manager found."); return; }

        int added = 0;

        // Find WhatsappAuth and TelegramAuth via serialized fields
        var so = new SerializedObject(manager);

        var waAuth = so.FindProperty("WhatsappAuth").objectReferenceValue as GameObject;
        var tgAuth = so.FindProperty("TelegramAuth").objectReferenceValue as GameObject;

        if (waAuth != null && waAuth.GetComponent<KeyboardScrollFix>() == null)
        {
            waAuth.AddComponent<KeyboardScrollFix>();
            EditorUtility.SetDirty(waAuth);
            added++;
            Debug.Log("[AuthPageMigration] Added KeyboardScrollFix to WhatsappAuth.");
        }

        if (tgAuth != null && tgAuth.GetComponent<KeyboardScrollFix>() == null)
        {
            tgAuth.AddComponent<KeyboardScrollFix>();
            EditorUtility.SetDirty(tgAuth);
            added++;
            Debug.Log("[AuthPageMigration] Added KeyboardScrollFix to TelegramAuth.");
        }

        if (added > 0)
        {
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Debug.Log($"[AuthPageMigration] Done — added {added} component(s). Save the scene.");
        }
        else
        {
            Debug.Log("[AuthPageMigration] Nothing to do — KeyboardScrollFix already present on both pages.");
        }
    }
}
#endif

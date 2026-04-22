#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only: attach SwipeToBackBotSettings to the BotSettings prefab root
// and wire its two RectTransform references (the wrapper that slides + the
// BotsPage panel that receives parallax). Run once after pulling the swipe
// change, or any time the BotSettings prefab root is replaced.
//
// Menu: Tools/BotSettings/Wire Swipe Back
public static class BotSettingsSwipeWirer
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string MenuPath = "Tools/BotSettings/Wire Swipe Back";

    [MenuItem(MenuPath)]
    public static void Wire()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[SwipeWirer] Prefab not found at {PrefabPath}");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var component = root.GetComponent<SwipeToBackBotSettings>();
            if (component == null) component = root.AddComponent<SwipeToBackBotSettings>();

            var botSettingsRect = root.GetComponent<RectTransform>();
            if (botSettingsRect == null)
            {
                Debug.LogError("[SwipeWirer] BotSettings prefab root has no RectTransform.");
                return;
            }

            var botsPageRect = FindBotsPageRectInScene();
            if (botsPageRect == null)
            {
                Debug.LogWarning("[SwipeWirer] Could not find BotsPage in the open scene. " +
                                 "Open Assets/Scenes/Main.unity and re-run.");
            }

            var serialized = new SerializedObject(component);
            serialized.FindProperty("botSettingsPanelToSlide").objectReferenceValue = botSettingsRect;
            if (botsPageRect != null)
                serialized.FindProperty("botsPagePanel").objectReferenceValue = botsPageRect;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[SwipeWirer] Wired SwipeToBackBotSettings on {PrefabPath}" +
                      (botsPageRect != null ? " (BotsPage linked from scene)." : " (BotsPage ref missing — open Main.unity and re-run)."));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        EditorSceneManager.MarkAllScenesDirty();
    }

    // Looks for the BotsPage singleton in the currently-open scene. Must be
    // called while Main.unity is loaded in the editor.
    private static RectTransform FindBotsPageRectInScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return null;

        foreach (var go in scene.GetRootGameObjects())
        {
            var page = go.GetComponentInChildren<BotsPage>(true);
            if (page != null) return page.GetComponent<RectTransform>();
        }
        return null;
    }
}
#endif

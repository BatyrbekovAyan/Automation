#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot migration utility. Walks the project's prefabs and the Main scene,
/// and swaps every TMP_InputField MonoBehaviour's m_Script reference to
/// DeferredDismissInputField. Idempotent — instances already on the new
/// script are skipped.
/// </summary>
public static class InputFieldMigrator
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Tools/Input Fields/Migrate to DeferredDismissInputField")]
    public static void Migrate()
    {
        MonoScript newScript = FindDeferredScript();
        if (newScript == null)
        {
            Debug.LogError("[InputFieldMigrator] DeferredDismissInputField MonoScript not found in project.");
            return;
        }

        int totalSwapped = 0;

        // Pass 1: prefab assets
        foreach (var prefabPath in EnumeratePrefabAssetPaths())
        {
            totalSwapped += MigratePrefab(prefabPath, newScript);
        }

        // Pass 2: Main scene
        totalSwapped += MigrateScene(newScript);

        Debug.Log($"[InputFieldMigrator] Done. Swapped {totalSwapped} TMP_InputField instance(s) to DeferredDismissInputField.");
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Input Fields/Migrate Scene Only")]
    public static void MigrateSceneOnly()
    {
        MonoScript newScript = FindDeferredScript();
        if (newScript == null)
        {
            Debug.LogError("[InputFieldMigrator] DeferredDismissInputField MonoScript not found in project.");
            return;
        }

        int swapped = MigrateScene(newScript);
        Debug.Log($"[InputFieldMigrator] Scene-only pass. Swapped {swapped} instance(s).");
        AssetDatabase.SaveAssets();
    }

    private static MonoScript FindDeferredScript()
    {
        string[] guids = AssetDatabase.FindAssets("DeferredDismissInputField t:MonoScript");
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null && script.GetClass() == typeof(DeferredDismissInputField))
                return script;
        }
        return null;
    }

    private static IEnumerable<string> EnumeratePrefabAssetPaths()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        var paths = new List<string>(guids.Length);
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/"))
                paths.Add(path);
        }
        return paths;
    }

    private static int MigratePrefab(string prefabPath, MonoScript newScript)
    {
        var contents = PrefabUtility.LoadPrefabContents(prefabPath);
        if (contents == null) return 0;

        int swapped = 0;
        try
        {
            foreach (var input in contents.GetComponentsInChildren<TMP_InputField>(includeInactive: true))
            {
                if (input is DeferredDismissInputField) continue;
                SwapScript(input, newScript);
                swapped++;
            }

            if (swapped > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
                Debug.Log($"[InputFieldMigrator] {prefabPath}: swapped {swapped}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        return swapped;
    }

    private static int MigrateScene(MonoScript newScript)
    {
        var openScene = EditorSceneManager.GetActiveScene();
        bool openedHere = false;
        Scene scene;

        if (openScene.path == ScenePath && openScene.isLoaded)
        {
            scene = openScene;
        }
        else
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            openedHere = true;
        }

        int swapped = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var input in root.GetComponentsInChildren<TMP_InputField>(includeInactive: true))
            {
                if (input is DeferredDismissInputField) continue;
                SwapScript(input, newScript);
                swapped++;
            }
        }

        if (swapped > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[InputFieldMigrator] {ScenePath}: swapped {swapped}");
        }

        if (openedHere && swapped == 0)
        {
            // No changes — leave the scene in whatever state the user had it.
        }

        return swapped;
    }

    private static void SwapScript(TMP_InputField component, MonoScript newScript)
    {
        var so = new SerializedObject(component);
        var scriptProp = so.FindProperty("m_Script");
        scriptProp.objectReferenceValue = newScript;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(component);
    }
}
#endif

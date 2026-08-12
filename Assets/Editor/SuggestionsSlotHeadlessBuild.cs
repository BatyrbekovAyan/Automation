using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Headless entry point for the sketch-003 slot build: opens Main.unity, runs the panel builder
/// and the controller wirer, saves the scene. Lets the full build run from the CLI with the
/// Editor closed:
///   Unity -batchmode -projectPath . -executeMethod SuggestionsSlotHeadlessBuild.Run -quit
/// Exits non-zero on failure so scripts can gate on it.
/// </summary>
public static class SuggestionsSlotHeadlessBuild
{
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError("[SuggestionsSlotHeadlessBuild] Assets/Scenes/Main.unity failed to open.");
            EditorApplication.Exit(1);
            return;
        }

        SuggestionsPanelBuilder.Build();
        SuggestionsControllerWirer.Wire();

        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError("[SuggestionsSlotHeadlessBuild] scene save FAILED.");
            EditorApplication.Exit(1);
            return;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[SuggestionsSlotHeadlessBuild] build + wire + save complete.");
    }
}

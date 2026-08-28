using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu entry that arms <see cref="StoreScreenshotDriver"/> and enters Play Mode.
///
/// Why the Editor and not the iOS Simulator: the Editor renders the SAME UI with the
/// same canvas, fonts and shaders, so a Game-view capture is a genuine screenshot of
/// the app — which keeps Xcode, the iOS runtime and an IL2CPP build out of the loop.
///
/// Two preconditions the entry point enforces itself, because getting either wrong
/// produces a blank PNG that still looks like a successful run:
///   * Main.unity must be the open scene. Play Mode in an empty/Untitled scene leaves
///     Manager / ChatManager / BottomTabManager null and captures the camera's
///     background colour (measured 2026-08-28).
///   * The Game view must be at a store-accepted pixel size — that is the ONE thing
///     still set by hand (Game view → размер), since the size list is Editor UI state.
///     Apple accepts 1290×2796 (6.7") and 1284×2778 / 1242×2688 (6.5").
///
/// Deliberately NO confirmation dialog: a modal blocks the Editor for anything driving
/// it over IPC, and this menu item is already an explicit action.
///
/// Seed the demo data first: python3 Tools/store/seed-demo-data.py
/// </summary>
public static class StoreScreenshotCapture
{
    private const string MainScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Tools/Store/Capture Screenshots")]
    private static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[StoreCapture] Play Mode уже идёт — останови его и запусти пункт меню заново.");
            return;
        }

        var active = EditorSceneManager.GetActiveScene();
        if (active.path != MainScenePath)
        {
            // Never silently discard someone's unsaved work — a parallel session shares
            // this Editor (see the multi-session Unity hazard in the project notes).
            if (active.isDirty)
            {
                Debug.LogError($"[StoreCapture] Открыта несохранённая сцена «{active.name}». " +
                               $"Сохрани или закрой её, затем повтори — переключать не буду.");
                return;
            }
            Debug.Log($"[StoreCapture] открываю {MainScenePath} (была «{active.name}»)");
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }

        // Seed from inside the Editor, every run: an external seeder loses to the Editor's
        // own PlayerPrefs cache, and the previous Play Mode exit may have flushed the demo
        // data away.
        if (!StoreDemoDataSeeder.Seed()) return;

        PlayerPrefs.SetInt(StoreScreenshotDriver.RunFlagKey, 1);
        PlayerPrefs.Save();
        Debug.Log("[StoreCapture] запускаю Play Mode; кадры лягут в Tools/store/screenshots/");
        EditorApplication.isPlaying = true;
    }
}

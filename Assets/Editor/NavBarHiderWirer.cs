#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Attaches <see cref="NavBarHider"/> to the full-screen overlays that used to show a
/// dead nav bar over themselves (owner check 2026-09-01): the add-bot wizard
/// (Screen_New) and the two auth pages that stack over it (WhatsappAuth/TelegramAuth
/// are LAST in ScreenContainer by NavRestructureBuilder's ordering contract, so they
/// render above the opaque form — and above them, the bar). Additive + idempotent
/// (skips objects that already carry the component); node names are the contract the
/// nav restructure already enforces. Re-run after Tools/Nav Restructure/Build.
///
/// The paywall keeps its own hide in PaywallController (shipped first, works, and its
/// capture-prior-state logic composes with this one). Profile sub-pages deliberately
/// keep the bar — it works honestly there.
/// </summary>
public static class NavBarHiderWirer
{
    private static readonly string[] OverlayNames = { "Screen_New", "WhatsappAuth", "TelegramAuth" };

    [MenuItem("Tools/Nav Restructure/Wire Nav Bar Hiders")]
    public static void Run()
    {
        RunInternal();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[NavBarHiderWirer] Wired — SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod NavBarHiderWirer.RunHeadless -quit
    public static void RunHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        RunInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[NavBarHiderWirer] Headless wire + save complete");
    }

    private static void RunInternal()
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int added = 0;
        foreach (string name in OverlayNames)
        {
            var node = all.FirstOrDefault(t => t.name == name);
            if (node == null)
                throw new System.InvalidOperationException(
                    $"[NavBarHiderWirer] {name} not found — run Tools/Nav Restructure/Build first.");
            if (node.GetComponent<NavBarHider>() != null) continue;
            node.gameObject.AddComponent<NavBarHider>();
            EditorUtility.SetDirty(node.gameObject);
            added++;
        }
        Debug.Log($"[NavBarHiderWirer] {added} hider(s) added ({OverlayNames.Length - added} already present).");
    }
}
#endif

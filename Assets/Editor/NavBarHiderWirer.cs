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
///
/// Second job (2026-09-02, device check): ScreenContainer stops 208u above the screen
/// bottom — the nav bar's zone — and Screen_New / Screen_Paywall stretch-fill only the
/// container, so hiding the bar exposed a BLACK band under them (the camera clear
/// colour; nothing draws there). WhatsappAuth/TelegramAuth were already authored to
/// reach the screen bottom (offsetMin.y = -208); this wirer gives the other two the
/// same rect, derived from the container's actual bottom offset. Safe by inspection:
/// both roots carry a themed background Image (it stretches with the rect), the
/// paywall's BottomBar is bottom-anchored WITH its own 96u home-bar padding (it was
/// designed for the true bottom), Screen_New's panels have no bottom-anchored
/// children, and both slide animations tween X only, so anchoredPosition.y survives.
/// </summary>
public static class NavBarHiderWirer
{
    private static readonly string[] OverlayNames = { "Screen_New", "WhatsappAuth", "TelegramAuth" };
    // Overlays that fill only ScreenContainer and must be stretched into the nav zone.
    private static readonly string[] StretchToBottomNames = { "Screen_New", "Screen_Paywall" };

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

        StretchOverlaysToScreenBottom(all);
    }

    private static void StretchOverlaysToScreenBottom(Transform[] all)
    {
        var container = all.FirstOrDefault(t => t.name == "ScreenContainer") as RectTransform;
        if (container == null)
            throw new System.InvalidOperationException("[NavBarHiderWirer] ScreenContainer not found.");

        // The container's bottom inset IS the nav zone (208u today) — read it, never hardcode.
        float navZone = container.offsetMin.y;
        if (navZone <= 0f)
            throw new System.InvalidOperationException(
                $"[NavBarHiderWirer] ScreenContainer bottom inset is {navZone} — expected the nav-bar zone; scene shape drifted.");

        foreach (string name in StretchToBottomNames)
        {
            var overlay = all.FirstOrDefault(t => t.name == name) as RectTransform;
            if (overlay == null)
                throw new System.InvalidOperationException($"[NavBarHiderWirer] {name} not found.");
            if (Mathf.Approximately(overlay.offsetMin.y, -navZone)) continue;   // idempotent

            overlay.offsetMin = new Vector2(overlay.offsetMin.x, -navZone);
            overlay.offsetMax = new Vector2(overlay.offsetMax.x, 0f);
            EditorUtility.SetDirty(overlay);
            Debug.Log($"[NavBarHiderWirer] {name} stretched to the screen bottom (offsetMin.y = {-navZone}).");
        }
    }
}
#endif

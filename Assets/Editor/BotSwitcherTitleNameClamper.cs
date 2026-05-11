#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Surgical patcher for ONLY the BotName TMP inside the WhatsApp header's
/// BotSwitcherTitle. Switches the row to a dynamic-width slot capped at a
/// max so short bot names render tight against the avatar and chevron, and
/// long ones ellipsize at maxWidth. Does not touch the avatar, chevron,
/// binder, or layout group — safe to re-run after any post-rebuild scene
/// tweaks. Drops the older static LayoutElement (preferredWidth=160) if a
/// previous version of this menu installed one, so the dynamic component
/// isn't out-voted on layoutPriority.
/// </summary>
public static class BotSwitcherTitleNameClamper
{
    private const string ScreenName = "Screen_Whatsapp";
    private const string TitleName = "BotSwitcherTitle";
    private const string NameChild = "BotName";

    // Cap chosen so avatar(24) + spacing(8) + name(<=160) + spacing(8) +
    // chevron(16) + padding(16) tops out at 232px, fitting inside the title
    // shell's 240px and leaving visible margin against the "Chats" tile.
    private const float NameMaxWidth = 160f;

    [MenuItem("Tools/Bot Switcher/Clamp Title Name Width")]
    public static void Clamp()
    {
        GameObject screen = FindGameObjectByNameIncludeInactive(ScreenName);
        if (screen == null)
        {
            Debug.LogError($"[BotSwitcherTitleNameClamper] Could not find '{ScreenName}' in any open scene. Open the Main scene.");
            return;
        }

        Transform title = FindDescendantByName(screen.transform, TitleName);
        Transform nameT = title != null ? title.Find(NameChild) : null;
        if (nameT == null)
        {
            Debug.LogError($"[BotSwitcherTitleNameClamper] No '{TitleName}/{NameChild}' under '{ScreenName}'. Run 'Tools/Bot Switcher/Rebuild Whatsapp Header' first to create the title shell.");
            return;
        }

        var nameText = nameT.GetComponent<TextMeshProUGUI>();
        if (nameText == null)
        {
            Debug.LogError($"[BotSwitcherTitleNameClamper] '{NameChild}' has no TextMeshProUGUI. Re-run 'Tools/Bot Switcher/Rebuild Whatsapp Header' to recreate it.");
            return;
        }

        nameText.overflowMode = TextOverflowModes.Ellipsis;
        nameText.enableWordWrapping = false;

        // Drop any legacy fixed-width LayoutElement — it and the dynamic
        // component below default to layoutPriority=1, so the layout system
        // would take MAX preferredWidth across them, freezing the slot at
        // the legacy value and defeating the shrink-to-fit behavior.
        LayoutElement legacy = nameT.GetComponent<LayoutElement>();
        if (legacy != null) Object.DestroyImmediate(legacy, allowDestroyingAssets: true);

        var maxLE = nameT.GetComponent<TMPMaxWidthLayoutElement>();
        if (maxLE == null) maxLE = nameT.gameObject.AddComponent<TMPMaxWidthLayoutElement>();
        maxLE.MaxWidth = NameMaxWidth;

        EditorUtility.SetDirty(nameText);
        EditorUtility.SetDirty(maxLE);
        EditorSceneManager.MarkSceneDirty(nameT.gameObject.scene);
        Selection.activeGameObject = nameT.gameObject;

        Debug.Log($"[BotSwitcherTitleNameClamper] '{NameChild}' now grows with text up to {NameMaxWidth:F0}px, then ellipsizes.");
    }

    private static GameObject FindGameObjectByNameIncludeInactive(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == name) return all[i].gameObject;
        }
        return null;
    }

    /// <summary>
    /// Depth-first search for a Transform with the given name anywhere under root,
    /// inclusive of inactive descendants. Used so the patcher doesn't have to know
    /// the current TopBar layout (it has gone through several iterations).
    /// </summary>
    private static Transform FindDescendantByName(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        Transform[] descendants = root.GetComponentsInChildren<Transform>(includeInactive: true);
        for (int i = 0; i < descendants.Length; i++)
        {
            if (descendants[i] != null && descendants[i] != root && descendants[i].name == name)
                return descendants[i];
        }
        return null;
    }
}
#endif

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Surgical patcher for ONLY the BotName TMP inside the chats header's
/// BotSwitcherTitle. Switches the row to a dynamic-width slot capped at a
/// max so short bot names render tight against the avatar and chevron, and
/// long ones ellipsize at maxWidth. Does not touch the avatar, chevron,
/// binder, or layout group — safe to re-run after any post-rebuild scene
/// tweaks. Drops the older static LayoutElement (preferredWidth=160) if a
/// previous version of this menu installed one, so the dynamic component
/// isn't out-voted on layoutPriority.
///
/// This is the ONLY owner of the name's width cap: neither
/// ChatsTopBarRestyleBuilder nor ReplyModeToggleBuilder writes it, so the
/// scene kept a 240u cap from an earlier, smaller header long after the
/// identity block moved into a 640u LeftZone — «Авто-Деталь KZ» rendered as
/// «Авто-Дет» with half the row empty (store screenshot review, 2026-09-02).
/// </summary>
public static class BotSwitcherTitleNameClamper
{
    // The chats screen was renamed when Telegram joined; the old "Screen_Whatsapp" no longer exists.
    private const string ScreenName = "Screen_Messanger";
    private const string TitleName = "BotSwitcherTitle";
    private const string NameChild = "BotName";

    // Derived from the live two-tier header (chats-topbar-spec.md, «Bot identity»):
    // LeftZone is 640u wide, the title sits at x=40 inside it, and the row spends
    // padding(8+8) + avatar(88) + spacing(16+16) + chevron(32) = 168u on chrome —
    // 640 - 40 - 168 = 432u remain for the name; 420 keeps a 12u margin. RightZone
    // (the «Авто» capsule) starts at x=740, so the block can never reach it.
    private const float NameMaxWidth = 420f;

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

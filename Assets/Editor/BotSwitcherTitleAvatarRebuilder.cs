#if UNITY_EDITOR
using Nobi.UiRoundedCorners;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Surgical rebuilder for ONLY the Avatar GameObject inside the WhatsApp
/// header's BotSwitcherTitle. Does not touch BotName, Chevron, or any
/// other sibling — Screen_WhatsappHeaderRebuilder owns the title shell;
/// this menu owns just the Avatar's visual config and child hierarchy.
///
/// Preserves the existing Avatar's RectTransform (size, anchors, position)
/// and LayoutElement (preferredWidth/Height) so post-build sizing tweaks
/// survive. Re-run this after resizing the Avatar to refresh the rounded
/// corner radius.
/// </summary>
public static class BotSwitcherTitleAvatarRebuilder
{
    private const string ScreenName = "Screen_Whatsapp";
    private const string ChatsPanelName = "ChatsPanel";
    private const string HeaderChildName = "TopBar";
    private const string TitleName = "BotSwitcherTitle";
    private const string AvatarName = "Avatar";
    private const string IconChildName = "IconSprite";
    private const float IconChildScale = 0.64f;

    [MenuItem("Tools/Bot Switcher/Rebuild Title Avatar")]
    public static void Rebuild()
    {
        GameObject screen = FindGameObjectByNameIncludeInactive(ScreenName);
        if (screen == null)
        {
            Debug.LogError($"[BotSwitcherTitleAvatarRebuilder] Could not find '{ScreenName}' in any open scene. Open the Main scene.");
            return;
        }

        Transform chatsPanel = screen.transform.Find(ChatsPanelName);
        Transform header = chatsPanel != null ? chatsPanel.Find(HeaderChildName) : null;
        Transform title = header != null ? header.Find(TitleName) : null;
        Transform avatar = title != null ? title.Find(AvatarName) : null;
        if (avatar == null)
        {
            Debug.LogError($"[BotSwitcherTitleAvatarRebuilder] Path '{ScreenName}/{ChatsPanelName}/{HeaderChildName}/{TitleName}/{AvatarName}' not found. Run 'Tools/Bot Switcher/Rebuild Whatsapp Header' first to create the title shell.");
            return;
        }

        BotSwitcherTitleBinder binder = title.GetComponent<BotSwitcherTitleBinder>();
        if (binder == null)
        {
            Debug.LogError($"[BotSwitcherTitleAvatarRebuilder] '{TitleName}' has no BotSwitcherTitleBinder. Re-run 'Tools/Bot Switcher/Rebuild Whatsapp Header' to attach it.");
            return;
        }

        // 1. Tile Image — use existing if present, else add. Preserve color
        //    only loosely (runtime overwrites it); ensure raycastTarget on
        //    so the parent button still receives clicks through this child.
        Image tileImage = avatar.GetComponent<Image>();
        if (tileImage == null) tileImage = avatar.gameObject.AddComponent<Image>();
        // Unity's Image renders nothing without a sprite. Built-in UISprite is
        // a flat 9-sliced rounded rect; ImageWithRoundedCorners then masks it
        // to a true circle at our chosen radius. The tile color is overwritten
        // at runtime by the binder/row view per the active bot's business type.
        if (tileImage.sprite == null)
            tileImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        tileImage.type = Image.Type.Simple;
        tileImage.color = new Color(0.85f, 0.85f, 0.85f);
        tileImage.raycastTarget = true;

        // 2. ImageWithRoundedCorners — radius derives from current size so
        //    the user's 44×44 (or any size) becomes a circle automatically.
        var roundedExisting = avatar.GetComponents<ImageWithRoundedCorners>();
        for (int i = 1; i < roundedExisting.Length; i++) Object.DestroyImmediate(roundedExisting[i]);
        ImageWithRoundedCorners rounded = avatar.GetComponent<ImageWithRoundedCorners>();
        if (rounded == null) rounded = avatar.gameObject.AddComponent<ImageWithRoundedCorners>();
        RectTransform avRT = avatar.GetComponent<RectTransform>();
        rounded.radius = avRT.sizeDelta.x * 0.5f;
        rounded.Validate();
        rounded.Refresh();

        // 3. Wipe children, create the IconSprite child centered at 64% size.
        for (int i = avatar.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(avatar.GetChild(i).gameObject);
        }

        GameObject iconGO = new GameObject(IconChildName, typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(avatar, false);
        RectTransform iconRT = iconGO.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 0.5f);
        iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = Vector2.zero;
        iconRT.sizeDelta = avRT.sizeDelta * IconChildScale;
        Image iconImage = iconGO.GetComponent<Image>();
        iconImage.sprite = null;
        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;

        // 4. Wire the binder's avatarImage + avatarIcon — leave nameLabel alone.
        var so = new SerializedObject(binder);
        so.FindProperty("avatarImage").objectReferenceValue = tileImage;
        so.FindProperty("avatarIcon").objectReferenceValue = iconImage;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(binder);
        EditorUtility.SetDirty(avatar);
        EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
        Selection.activeGameObject = avatar.gameObject;

        Debug.Log($"[BotSwitcherTitleAvatarRebuilder] Rebuilt {AvatarName} at radius {rounded.radius:F1}px (size {avRT.sizeDelta.x:F0}×{avRT.sizeDelta.y:F0}). Re-run after resizing.");
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
}
#endif

#if UNITY_EDITOR
using Nobi.UiRoundedCorners;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Surgical rebuilder for ONLY the Avatar GameObject inside the bot
/// switcher row prefab template. Does not touch SelectedBackground,
/// SelectedAccentBar, the Stack/Name/SubLine subtree, or rowButton —
/// BotSwitcherSheetBuilder owns the row shell; this menu owns just the
/// Avatar's visual config and child hierarchy.
///
/// Preserves the existing Avatar's RectTransform and LayoutElement so
/// post-build sizing tweaks survive. Re-run after resizing to refresh
/// the rounded corner radius.
/// </summary>
public static class BotSwitcherRowAvatarRebuilder
{
    private const string HolderName = "BotSwitcherRowPrefabHolder";
    private const string RowName = "BotSwitcherRow";
    private const string AvatarName = "Avatar";
    private const string IconChildName = "IconSprite";
    private const float IconChildScale = 0.64f;

    [MenuItem("Tools/Bot Switcher/Rebuild Row Avatar")]
    public static void Rebuild()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            Debug.LogError("[BotSwitcherRowAvatarRebuilder] No Canvas found in any open scene. Open the Main scene.");
            return;
        }

        Transform holder = canvas.transform.Find(HolderName);
        Transform row = holder != null ? holder.Find(RowName) : null;
        Transform avatar = row != null ? row.Find(AvatarName) : null;
        if (avatar == null)
        {
            Debug.LogError($"[BotSwitcherRowAvatarRebuilder] Path '{HolderName}/{RowName}/{AvatarName}' not found under Canvas. Run 'Tools/Bot Switcher/Build Sheet' first to create the row template.");
            return;
        }

        BotSwitcherRowView rowView = row.GetComponent<BotSwitcherRowView>();
        if (rowView == null)
        {
            Debug.LogError($"[BotSwitcherRowAvatarRebuilder] '{RowName}' has no BotSwitcherRowView. Re-run 'Tools/Bot Switcher/Build Sheet' to attach it.");
            return;
        }

        // 1. Tile Image — use existing if present, else add.
        Image tileImage = avatar.GetComponent<Image>();
        if (tileImage == null) tileImage = avatar.gameObject.AddComponent<Image>();
        if (tileImage.sprite == null)
            tileImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        tileImage.type = Image.Type.Simple;
        tileImage.color = new Color(0.85f, 0.85f, 0.85f);
        tileImage.raycastTarget = true;

        // 2. ImageWithRoundedCorners — radius from current size.
        var roundedExisting = avatar.GetComponents<ImageWithRoundedCorners>();
        for (int i = 1; i < roundedExisting.Length; i++) Object.DestroyImmediate(roundedExisting[i]);
        ImageWithRoundedCorners rounded = avatar.GetComponent<ImageWithRoundedCorners>();
        if (rounded == null) rounded = avatar.gameObject.AddComponent<ImageWithRoundedCorners>();
        RectTransform avRT = avatar.GetComponent<RectTransform>();
        rounded.radius = avRT.sizeDelta.x * 0.5f;
        rounded.Validate();
        rounded.Refresh();

        // 3. Wipe children, create IconSprite at 64% size.
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

        // 4. Wire avatarImage + avatarIcon on BotSwitcherRowView. Leave
        //    nameLabel/subLineLabel/statusDot/selectedBackground/etc. alone.
        var so = new SerializedObject(rowView);
        so.FindProperty("avatarImage").objectReferenceValue = tileImage;
        so.FindProperty("avatarIcon").objectReferenceValue = iconImage;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(rowView);
        EditorUtility.SetDirty(avatar);
        EditorSceneManager.MarkSceneDirty(avatar.gameObject.scene);
        Selection.activeGameObject = avatar.gameObject;

        Debug.Log($"[BotSwitcherRowAvatarRebuilder] Rebuilt {AvatarName} at radius {rounded.radius:F1}px (size {avRT.sizeDelta.x:F0}×{avRT.sizeDelta.y:F0}). Re-run after resizing.");
    }
}
#endif

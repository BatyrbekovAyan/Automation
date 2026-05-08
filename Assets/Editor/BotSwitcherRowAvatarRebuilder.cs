#if UNITY_EDITOR
using Nobi.UiRoundedCorners;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Surgical rebuilder for ONLY the Avatar GameObject inside the bot switcher
/// row prefab asset. Resolves the prefab via BotSwitcherSheet.rowPrefab in
/// the open scene, loads it via PrefabUtility, restructures the Avatar, and
/// saves the prefab back to disk. Scene instances inherit the change
/// automatically through the prefab system — no scene edit needed.
///
/// Does not touch SelectedBackground, SelectedAccentBar, the Stack/Name/SubLine
/// subtree, or rowButton — only the Avatar's visual config and child hierarchy.
/// Preserves the existing Avatar's RectTransform and LayoutElement so
/// post-build sizing tweaks survive. Re-run after resizing to refresh the
/// rounded corner radius.
/// </summary>
public static class BotSwitcherRowAvatarRebuilder
{
    private const string AvatarName = "Avatar";
    private const string IconChildName = "IconSprite";
    private const float IconChildScale = 0.64f;

    [MenuItem("Tools/Bot Switcher/Rebuild Row Avatar")]
    public static void Rebuild()
    {
        // 1. Locate a BotSwitcherSheet in any open scene to discover the row prefab reference.
        BotSwitcherSheet sheet = Object.FindFirstObjectByType<BotSwitcherSheet>(FindObjectsInactive.Include);
        if (sheet == null)
        {
            Debug.LogError("[BotSwitcherRowAvatarRebuilder] No BotSwitcherSheet found in any open scene. Open the Main scene first.");
            return;
        }

        // 2. Read the rowPrefab serialized reference and resolve its asset path.
        var sheetSO = new SerializedObject(sheet);
        SerializedProperty rowPrefabProp = sheetSO.FindProperty("rowPrefab");
        if (rowPrefabProp == null || rowPrefabProp.objectReferenceValue == null)
        {
            Debug.LogError("[BotSwitcherRowAvatarRebuilder] BotSwitcherSheet.rowPrefab is unwired. Wire it to a BotSwitcherRow prefab asset in the Inspector first.");
            return;
        }

        string prefabPath = AssetDatabase.GetAssetPath(rowPrefabProp.objectReferenceValue);
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogError($"[BotSwitcherRowAvatarRebuilder] BotSwitcherSheet.rowPrefab references '{rowPrefabProp.objectReferenceValue.name}', which is not a saved prefab asset. Save the row template as a prefab in Assets/ and re-wire the field.");
            return;
        }

        // 3. Load the prefab in an editable in-memory scene.
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            BotSwitcherRowView rowView = prefabRoot.GetComponent<BotSwitcherRowView>();
            if (rowView == null)
            {
                Debug.LogError($"[BotSwitcherRowAvatarRebuilder] Prefab at '{prefabPath}' has no BotSwitcherRowView component on the root.");
                return;
            }

            Transform avatar = prefabRoot.transform.Find(AvatarName);
            if (avatar == null)
            {
                Debug.LogError($"[BotSwitcherRowAvatarRebuilder] Prefab at '{prefabPath}' has no child named '{AvatarName}'. Re-run 'Tools/Bot Switcher/Build Sheet' to regenerate the row template.");
                return;
            }

            // 4. Tile Image — use existing if present, else add. UISprite is a flat
            //    9-sliced rounded rect; ImageWithRoundedCorners then masks it to a
            //    true circle. The tile color is overwritten at runtime per active bot.
            Image tileImage = avatar.GetComponent<Image>();
            if (tileImage == null) tileImage = avatar.gameObject.AddComponent<Image>();
            if (tileImage.sprite == null)
                tileImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            tileImage.type = Image.Type.Simple;
            tileImage.color = new Color(0.85f, 0.85f, 0.85f);
            tileImage.raycastTarget = true;

            // 5. ImageWithRoundedCorners — radius from current size.
            var roundedExisting = avatar.GetComponents<ImageWithRoundedCorners>();
            for (int i = 1; i < roundedExisting.Length; i++) Object.DestroyImmediate(roundedExisting[i]);
            ImageWithRoundedCorners rounded = avatar.GetComponent<ImageWithRoundedCorners>();
            if (rounded == null) rounded = avatar.gameObject.AddComponent<ImageWithRoundedCorners>();
            RectTransform avRT = avatar.GetComponent<RectTransform>();
            rounded.radius = avRT.sizeDelta.x * 0.5f;
            rounded.Validate();
            rounded.Refresh();

            // 6. Wipe children, create IconSprite at 64% size.
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

            // 7. Wire avatarImage + avatarIcon on BotSwitcherRowView. Leave
            //    nameLabel/subLineLabel/statusDot/selectedBackground/etc. alone.
            var so = new SerializedObject(rowView);
            so.FindProperty("avatarImage").objectReferenceValue = tileImage;
            so.FindProperty("avatarIcon").objectReferenceValue = iconImage;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 8. Save the prefab back. Scene instances pick up the change via the
            //    prefab system — no scene edit, no MarkSceneDirty.
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);

            Debug.Log($"[BotSwitcherRowAvatarRebuilder] Rebuilt {AvatarName} at radius {rounded.radius:F1}px (size {avRT.sizeDelta.x:F0}×{avRT.sizeDelta.y:F0}) in '{prefabPath}'. Re-run after resizing.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }
}
#endif

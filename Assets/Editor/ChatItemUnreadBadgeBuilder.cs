#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Nobi.UiRoundedCorners;

/// <summary>
/// Editor maintenance for ChatItem.prefab.
///
/// Adds an UnreadBadge GameObject to the right side of the chat row, anchored
/// to the bottom-right of TextBlock and floating over the existing Message
/// element's 80 px right margin. The badge is layout-isolated via
/// LayoutElement.ignoreLayout = true so it never disturbs the VerticalLayoutGroup
/// that owns the Name / Message rows.
///
/// Target row structure (TextBlock children, after this builder runs):
///
///   TextBlock (VerticalLayoutGroup)
///     TopRow
///     Message                              ← unchanged
///     UnreadBadge                          ← NEW. ignoreLayout. Anchored bottom-right.
///       CountText (TMP, white, bold)
///     Divider                              ← unchanged
///
/// Idempotent — re-running destroys any existing UnreadBadge and rebuilds.
/// </summary>
public static class ChatItemUnreadBadgeBuilder
{
    private const string PrefabPath = "Assets/Prefabs/ChatItem.prefab";
    private const string TextBlockName = "TextBlock";
    private const string BadgeName = "UnreadBadge";
    private const string CountTextName = "CountText";

    private static readonly Color WhatsAppGreen = new Color32(0x25, 0xD3, 0x66, 0xFF);

    [MenuItem("Tools/Chat List/Add Unread Badge To ChatItem")]
    public static void Build()
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[ChatItemUnreadBadge] Failed to load prefab at {PrefabPath}");
            return;
        }

        try
        {
            var textBlock = FindChildRecursive(prefabRoot.transform, TextBlockName);
            if (textBlock == null)
            {
                Debug.LogError($"[ChatItemUnreadBadge] '{TextBlockName}' not found under {PrefabPath}");
                return;
            }

            var existing = textBlock.Find(BadgeName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var badge = BuildBadge(textBlock);
            var countText = BuildCountText(badge.transform);

            WireChatItemViewRefs(prefabRoot, badge, countText);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log($"[ChatItemUnreadBadge] Built badge under {PrefabPath} → TextBlock/{BadgeName}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static GameObject BuildBadge(Transform parent)
    {
        var badge = new GameObject(
            BadgeName,
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter),
            typeof(ImageWithRoundedCorners));
        badge.transform.SetParent(parent, false);

        var rt = (RectTransform)badge.transform;
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-8f, 60f);
        rt.sizeDelta = new Vector2(60f, 60f);

        var image = badge.GetComponent<Image>();
        image.color = WhatsAppGreen;
        image.raycastTarget = false;

        var le = badge.GetComponent<LayoutElement>();
        le.ignoreLayout = true;

        var hlg = badge.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 16, 0, 0);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var fitter = badge.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var rounded = badge.GetComponent<ImageWithRoundedCorners>();
        var radiusField = typeof(ImageWithRoundedCorners).GetField(
            "radius", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (radiusField != null) radiusField.SetValue(rounded, 30f);

        return badge;
    }

    private static TextMeshProUGUI BuildCountText(Transform parent)
    {
        var go = new GameObject(CountTextName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.fontSize = 36f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        return tmp;
    }

    private static void WireChatItemViewRefs(GameObject prefabRoot, GameObject badge, TextMeshProUGUI countText)
    {
        var view = prefabRoot.GetComponent<ChatItemView>();
        if (view == null)
        {
            Debug.LogError($"[ChatItemUnreadBadge] No ChatItemView component on prefab root.");
            return;
        }

        var so = new SerializedObject(view);
        var badgeProp = so.FindProperty("unreadBadge");
        var countProp = so.FindProperty("unreadCountText");

        if (badgeProp == null || countProp == null)
        {
            Debug.LogError(
                "[ChatItemUnreadBadge] ChatItemView is missing 'unreadBadge' or 'unreadCountText' fields. " +
                "Did Task 4 land?");
            return;
        }

        badgeProp.objectReferenceValue = badge;
        countProp.objectReferenceValue = countText;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindChildRecursive(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }
}
#endif

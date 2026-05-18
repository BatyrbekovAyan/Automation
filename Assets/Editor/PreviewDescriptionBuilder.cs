#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

public static class PreviewDescriptionBuilder
{
    private static readonly string[] PrefabPaths =
    {
        "Assets/Prefabs/MessageTextIncoming.prefab",
        "Assets/Prefabs/MessageTextOutgoing.prefab"
    };

    // Mirrors WhatsApp's description row sitting between title and domain.
    // Kept smaller and more muted than the title so the title remains primary.
    private const float DescriptionFontSize = 28f;
    private static readonly Color DescriptionColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    private const int DescriptionMaxLines = 2;
    private static readonly Vector4 DescriptionMargin = new Vector4(18f, 0f, 18f, 0f);

    [MenuItem("Tools/Link Preview/Build Description Element")]
    public static void Build()
    {
        foreach (string path in PrefabPaths)
        {
            BuildForPrefab(path);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[PreviewDescriptionBuilder] PreviewDescription wired into both message prefabs.");
    }

    private static void BuildForPrefab(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform linkPreview = root.transform.Find("Bubble/LinkPreview");
            if (linkPreview == null)
            {
                Debug.LogError($"[PreviewDescriptionBuilder] LinkPreview missing in {prefabPath}");
                return;
            }

            Transform existing = linkPreview.Find("PreviewDescription");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            Transform title = linkPreview.Find("PreviewTitle");
            Transform domain = linkPreview.Find("PreviewDomain");
            if (title == null || domain == null)
            {
                Debug.LogError($"[PreviewDescriptionBuilder] PreviewTitle or PreviewDomain missing in {prefabPath}");
                return;
            }

            GameObject description = Object.Instantiate(title.gameObject, linkPreview);
            description.name = "PreviewDescription";
            description.transform.SetSiblingIndex(title.GetSiblingIndex() + 1);

            var tmp = description.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = DescriptionFontSize;
            tmp.color = DescriptionColor;
            tmp.maxVisibleLines = DescriptionMaxLines;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.margin = DescriptionMargin;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.text = string.Empty;

            var view = root.GetComponent<MessageItemView>();
            if (view != null)
            {
                var so = new SerializedObject(view);
                var prop = so.FindProperty("linkPreviewDescription");
                if (prop != null)
                {
                    prop.objectReferenceValue = tmp;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    Debug.LogWarning($"[PreviewDescriptionBuilder] linkPreviewDescription field not found on MessageItemView in {prefabPath}");
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif

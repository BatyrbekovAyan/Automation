#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Nobi.UiRoundedCorners;

/// <summary>
/// Editor tooling for the reusable OutlineFrame pattern.
///
/// Menu item:
///   - "Tools/UI/Create OutlineFrame Prefab" — generates Assets/Prefabs/OutlineFrame.prefab,
///     a layout-driven wrapper for adding consistent outlines to any UI element whose
///     parent can be sized to its content. (Not used for the existing message bubbles —
///     those keep MirrorSize because their root must remain full scroll-width.)
///
/// Idempotent — rerunning rebuilds cleanly.
/// </summary>
public static class OutlineFrameBuilder
{
    private const string PrefabPath = "Assets/Prefabs/OutlineFrame.prefab";
    private const float DefaultContentRadius = 28f;

    [MenuItem("Tools/UI/Create OutlineFrame Prefab")]
    public static void CreatePrefab()
    {
        var root = new GameObject(
            "OutlineFrame",
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter),
            typeof(OutlineFrame));

        ConfigureLayoutGroup(root.GetComponent<HorizontalLayoutGroup>());
        ConfigureSizeFitter(root.GetComponent<ContentSizeFitter>());

        var outline = BuildOutlineChild(root.transform);
        var content = BuildContentChild(root.transform);

        var frame = root.GetComponent<OutlineFrame>();
        var so = new SerializedObject(frame);
        so.FindProperty("outlineImage").objectReferenceValue = outline.GetComponent<Image>();
        so.FindProperty("outlineRounded").objectReferenceValue = outline.GetComponent<ImageWithRoundedCorners>();
        so.FindProperty("content").objectReferenceValue = (RectTransform)content.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();
        Debug.Log($"[OutlineFrameBuilder] Created {PrefabPath}");
    }

    private static void ConfigureLayoutGroup(HorizontalLayoutGroup hlg)
    {
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.spacing = 0;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childScaleWidth = false;
        hlg.childScaleHeight = false;
    }

    private static void ConfigureSizeFitter(ContentSizeFitter csf)
    {
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private static GameObject BuildOutlineChild(Transform parent)
    {
        var outline = new GameObject(
            "Outline",
            typeof(RectTransform),
            typeof(Image),
            typeof(ImageWithRoundedCorners),
            typeof(LayoutElement));
        outline.transform.SetParent(parent, false);

        var rt = (RectTransform)outline.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(-OutlineFrame.Thickness, -OutlineFrame.Thickness);
        rt.offsetMax = new Vector2(OutlineFrame.Thickness, OutlineFrame.Thickness);

        var image = outline.GetComponent<Image>();
        image.color = OutlineFrame.DefaultColor;
        image.raycastTarget = false;

        var rounded = outline.GetComponent<ImageWithRoundedCorners>();
        rounded.radius = DefaultContentRadius + OutlineFrame.RadiusOffset;
        rounded.Validate();

        outline.GetComponent<LayoutElement>().ignoreLayout = true;
        outline.transform.SetAsFirstSibling();
        return outline;
    }

    private static GameObject BuildContentChild(Transform parent)
    {
        var content = new GameObject("Content", typeof(RectTransform), typeof(LayoutElement));
        content.transform.SetParent(parent, false);

        var rt = (RectTransform)content.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(120, 60);

        var le = content.GetComponent<LayoutElement>();
        le.preferredWidth = 120;
        le.preferredHeight = 60;
        return content;
    }
}
#endif

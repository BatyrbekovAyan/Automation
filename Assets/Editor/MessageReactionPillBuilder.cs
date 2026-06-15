#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Nobi.UiRoundedCorners;

/// <summary>
/// Adds a floating ReactionPill to a message bubble prefab and wires the
/// MessageItemView.reactionPill + ReactionPillView.label serialized refs.
///
/// Pill structure (under the bubble container, ignoreLayout so it does not
/// disturb the bubble's VerticalLayoutGroup). The outer object IS the border —
/// BorderThickness larger on each side than the inner white fill, so the
/// BorderColor shows through as a thin ring:
///
///   ReactionPill (border: Image + RoundedCorners + LayoutElement + HLG + ContentSizeFitter + ReactionPillView)
///     PillFill (white: Image + RoundedCorners + HLG + ContentSizeFitter)
///       Label (TMP — emoji sprites + optional count)
///
/// Run BOTH menu items after the MessageItemView 'reactionPill' field exists.
/// Idempotent — re-running destroys any existing pill.
/// </summary>
public static class MessageReactionPillBuilder
{
    private const string IncomingPath = "Assets/Prefabs/MessageTextIncoming.prefab";
    private const string OutgoingPath = "Assets/Prefabs/MessageTextOutgoing.prefab";
    private const string BubbleName = "Bubble";
    private const string PillName = "ReactionPill";
    private const string FillName = "PillFill";
    private const string LabelName = "Label";

    // The outer border object is BorderThickness reference-units larger on each
    // side than the inner white fill — a 1-unit hairline ring.
    private const int BorderThickness = 1;

    private static readonly Color PillFill = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    // Matches the app's standard outline color (OutlineFrame default).
    private static readonly Color BorderColor = new Color32(0xD9, 0xD4, 0xCA, 0xFF);
    private static readonly Color LabelColor = new Color32(0x11, 0x1B, 0x21, 0xFF);

    [MenuItem("Tools/Chat/Add Reaction Pill To Incoming Bubble")]
    public static void BuildIncoming() => Build(IncomingPath, incoming: true);

    [MenuItem("Tools/Chat/Add Reaction Pill To Outgoing Bubble")]
    public static void BuildOutgoing() => Build(OutgoingPath, incoming: false);

    private static void Build(string prefabPath, bool incoming)
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[ReactionPill] Failed to load prefab at {prefabPath}");
            return;
        }

        try
        {
            var bubble = FindChildRecursive(prefabRoot.transform, BubbleName);
            if (bubble == null)
            {
                Debug.LogError($"[ReactionPill] '{BubbleName}' not found under {prefabPath}. " +
                               "Set BubbleName to the actual bubble-container object name.");
                return;
            }

            var existing = bubble.Find(PillName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var pill = BuildPill(bubble, incoming);       // outer border (holds ReactionPillView)
            var fill = BuildInnerFill(pill.transform);    // inner white capsule
            var label = BuildLabel(fill.transform);       // emoji + count, inside the fill
            var view = pill.GetComponent<ReactionPillView>();

            WireViewLabel(view, label);

            if (!WireMessageItemViewRef(prefabRoot, view))
                return;

            pill.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            Debug.Log($"[ReactionPill] Built pill under {prefabPath} → {BubbleName}/{PillName} (incoming={incoming})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // Outer object = the border. Holds positioning + ReactionPillView, and wraps the
    // inner fill with BorderThickness of padding on every side (the BorderColor shows
    // through that band). Sizes to content in both axes via ContentSizeFitter.
    private static GameObject BuildPill(Transform parent, bool incoming)
    {
        var pill = new GameObject(
            PillName,
            typeof(RectTransform),
            typeof(Image),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter),
            typeof(ImageWithRoundedCorners),
            typeof(ReactionPillView));
        pill.transform.SetParent(parent, false);
        pill.transform.SetAsLastSibling();

        var rt = (RectTransform)pill.transform;
        // Incoming bubbles sit on the left → pill on the LEFT edge; outgoing bubbles
        // sit on the right → pill on the RIGHT edge.
        float x = incoming ? 0f : 1f;
        rt.anchorMin = new Vector2(x, 0f);
        rt.anchorMax = new Vector2(x, 0f);
        rt.pivot = new Vector2(x, 1f);
        rt.anchoredPosition = new Vector2(incoming ? 16f : -16f, 8f);

        var image = pill.GetComponent<Image>();
        image.color = BorderColor;
        image.raycastTarget = false;

        var le = pill.GetComponent<LayoutElement>();
        le.ignoreLayout = true;

        var hlg = pill.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(BorderThickness, BorderThickness, BorderThickness, BorderThickness);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var fitter = pill.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var rounded = pill.GetComponent<ImageWithRoundedCorners>();
        rounded.radius = 26f;   // below half-height -> clean capsule, no oval overshoot
        rounded.Validate();
        rounded.Refresh();

        return pill;
    }

    // Inner white capsule. Wraps the Label with the real content padding and sizes to
    // it; the outer border then wraps this.
    private static GameObject BuildInnerFill(Transform parent)
    {
        var fill = new GameObject(
            FillName,
            typeof(RectTransform),
            typeof(Image),
            typeof(HorizontalLayoutGroup),
            typeof(ContentSizeFitter),
            typeof(ImageWithRoundedCorners));
        fill.transform.SetParent(parent, false);

        var image = fill.GetComponent<Image>();
        image.color = PillFill;
        image.raycastTarget = false;

        var hlg = fill.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(15, 15, 5, 5);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var fitter = fill.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var rounded = fill.GetComponent<ImageWithRoundedCorners>();
        rounded.radius = 25f;   // outer radius (26) minus the 1-unit border -> concentric corners
        rounded.Validate();
        rounded.Refresh();

        return fill;
    }

    private static TextMeshProUGUI BuildLabel(Transform parent)
    {
        var go = new GameObject(LabelName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.fontSize = 38f;
        tmp.color = LabelColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        return tmp;
    }

    private static void WireViewLabel(ReactionPillView view, TextMeshProUGUI label)
    {
        var so = new SerializedObject(view);
        var labelProp = so.FindProperty("label");
        if (labelProp == null)
        {
            Debug.LogError("[ReactionPill] ReactionPillView is missing the 'label' field.");
            return;
        }
        labelProp.objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static bool WireMessageItemViewRef(GameObject prefabRoot, ReactionPillView view)
    {
        var item = prefabRoot.GetComponent<MessageItemView>();
        if (item == null)
        {
            Debug.LogError("[ReactionPill] No MessageItemView component on prefab root.");
            return false;
        }

        var so = new SerializedObject(item);
        var pillProp = so.FindProperty("reactionPill");
        if (pillProp == null)
        {
            Debug.LogError("[ReactionPill] MessageItemView is missing the 'reactionPill' field. Did Task 7 land?");
            return false;
        }
        pillProp.objectReferenceValue = view;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
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

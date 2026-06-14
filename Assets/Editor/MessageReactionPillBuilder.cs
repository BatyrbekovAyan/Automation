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
/// disturb the bubble's VerticalLayoutGroup):
///
///   ReactionPill (Image + ImageWithRoundedCorners + LayoutElement + HLG + ContentSizeFitter + ReactionPillView)
///     Label (TMP — emoji sprites + optional count)
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
    private const string LabelName = "Label";

    private static readonly Color PillFill = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
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

            var pill = BuildPill(bubble, incoming);
            var label = BuildLabel(pill.transform);
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
        float x = incoming ? 1f : 0f;
        rt.anchorMin = new Vector2(x, 0f);
        rt.anchorMax = new Vector2(x, 0f);
        rt.pivot = new Vector2(x, 1f);
        rt.anchoredPosition = new Vector2(incoming ? -16f : 16f, 4f);
        rt.sizeDelta = new Vector2(0f, 52f);

        var image = pill.GetComponent<Image>();
        image.color = PillFill;
        image.raycastTarget = false;

        var le = pill.GetComponent<LayoutElement>();
        le.ignoreLayout = true;

        var hlg = pill.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 16, 4, 4);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var fitter = pill.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var rounded = pill.GetComponent<ImageWithRoundedCorners>();
        rounded.radius = 26f;
        rounded.Validate();
        rounded.Refresh();

        return pill;
    }

    private static TextMeshProUGUI BuildLabel(Transform parent)
    {
        var go = new GameObject(LabelName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.fontSize = 30f;
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

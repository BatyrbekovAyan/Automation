#if UNITY_EDITOR
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the shared long-press reaction-bar overlay into the open Main scene and
/// wires the ReactionBarController serialized refs, plus a pair of menu items that
/// attach the MessageBubbleLongPress gesture to the two message bubble prefabs.
///
/// Overlay hierarchy (parented to the sliding chat panel so it overlays the whole
/// chat screen; the controller root stays active, the Content child is toggled):
///
///   ReactionBarOverlay (RectTransform full-stretch + ReactionBarController)   ← root, no Image (non-blocking)
///     Content (RectTransform full-stretch, pivot center)                       ← 'content' (toggled)
///       Scrim (Image dim + Button)                                             ← 'scrimButton' (full-panel dismiss)
///       Bar (Image white pill + HLG + ContentSizeFitter + RoundedCorners)      ← 'bar' (floats over the bubble)
///         Emoji0..Emoji5 (Image circle + Button + LayoutElement + RoundedCorners)
///           Label (TMP — emoji sprite, filled at runtime)
///
/// Plan A ships the six quick emoji only; the '+' full picker is Plan B, so
/// plusButton is left unassigned (the controller null-guards it). Idempotent.
///
/// Sizes in 1080x1920 canvas reference units (1 dp ~= 3 units).
/// </summary>
public static class ReactionBarBuilder
{
    private const string IncomingPath = "Assets/Prefabs/MessageTextIncoming.prefab";
    private const string OutgoingPath = "Assets/Prefabs/MessageTextOutgoing.prefab";

    private const string OverlayName = "ReactionBarOverlay";

    // Bar
    private const int   BarPadding   = 20;   // ~7dp
    private const int   BarSpacing   = 12;   // ~4dp between emoji
    private const float BarRadius    = 66f;  // ~half of the 140-unit bar height -> pill
    // Emoji button
    private const float ButtonSize   = 100f; // ~33dp — compact reaction bar
    private const float ButtonRadius = 50f;  // half size -> circle
    private const float EmojiFont    = 60f;
    // Scrim
    private const float ScrimAlpha   = 0.28f;

    private static readonly Color BarColor   = Color.white;
    private static readonly Color ScrimColor = new Color(0f, 0f, 0f, ScrimAlpha);

    [MenuItem("Tools/Chat/Build Reaction Bar Overlay")]
    public static void BuildOverlay()
    {
        RectTransform chatPanel = ResolveChatPanel();
        if (chatPanel == null)
        {
            Debug.LogError("[ReactionBar] Could not resolve the chat panel (SwipeToBack.chatPanelToSlide). " +
                           "Open Main.unity and ensure the chat screen exists.");
            return;
        }

        // Idempotent: remove any prior overlay under the chat panel.
        foreach (var prior in chatPanel.GetComponentsInChildren<ReactionBarController>(includeInactive: true))
            Object.DestroyImmediate(prior.gameObject);

        // ── Root (non-blocking, holds the controller, stays active) ──
        var rootGo = new GameObject(OverlayName, typeof(RectTransform), typeof(ReactionBarController));
        rootGo.transform.SetParent(chatPanel, false);
        rootGo.transform.SetAsLastSibling();
        Stretch((RectTransform)rootGo.transform);

        // ── Content (toggled scrim + bar) ──
        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(rootGo.transform, false);
        var contentRt = (RectTransform)contentGo.transform;
        Stretch(contentRt);
        contentRt.pivot = new Vector2(0.5f, 0.5f);   // PositionBarOver measures from center

        // ── Scrim (full-panel dim + dismiss button) ──
        var scrimGo = new GameObject("Scrim", typeof(RectTransform), typeof(Image), typeof(Button));
        scrimGo.transform.SetParent(contentGo.transform, false);
        Stretch((RectTransform)scrimGo.transform);
        var scrimImg = scrimGo.GetComponent<Image>();
        scrimImg.color = ScrimColor;
        scrimImg.raycastTarget = true;
        var scrimButton = scrimGo.GetComponent<Button>();
        scrimButton.transition = Selectable.Transition.None;
        var scrimNav = scrimButton.navigation; scrimNav.mode = Navigation.Mode.None; scrimButton.navigation = scrimNav;

        // ── Bar (white pill, sizes to its 6 buttons) ──
        var barGo = new GameObject("Bar", typeof(RectTransform), typeof(Image),
                                   typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter),
                                   typeof(ImageWithRoundedCorners));
        barGo.transform.SetParent(contentGo.transform, false);
        var barRt = (RectTransform)barGo.transform;
        barRt.anchorMin = barRt.anchorMax = new Vector2(0.5f, 0.5f);
        barRt.pivot = new Vector2(0.5f, 0.5f);

        var barImg = barGo.GetComponent<Image>();
        barImg.color = BarColor;
        barImg.raycastTarget = true;   // taps on the bar itself don't fall through to the scrim

        var barHlg = barGo.GetComponent<HorizontalLayoutGroup>();
        barHlg.padding = new RectOffset(BarPadding, BarPadding, BarPadding, BarPadding);
        barHlg.spacing = BarSpacing;
        barHlg.childAlignment = TextAnchor.MiddleCenter;
        barHlg.childControlWidth = true;
        barHlg.childControlHeight = true;
        barHlg.childForceExpandWidth = false;
        barHlg.childForceExpandHeight = false;

        var barFitter = barGo.GetComponent<ContentSizeFitter>();
        barFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        barFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var barRounded = barGo.GetComponent<ImageWithRoundedCorners>();
        barRounded.radius = BarRadius;
        barRounded.Validate();
        barRounded.Refresh();

        var buttons = new Button[6];
        for (int i = 0; i < 6; i++)
            buttons[i] = BuildEmojiButton(barGo.transform, i);

        // ── Wire the controller ──
        var controller = rootGo.GetComponent<ReactionBarController>();
        var so = new SerializedObject(controller);
        SetRef(so, "content", contentGo);
        SetRef(so, "scrimButton", scrimButton);
        SetRef(so, "bar", barRt);
        var arr = so.FindProperty("emojiButtons");
        arr.arraySize = 6;
        for (int i = 0; i < 6; i++)
            arr.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        contentGo.SetActive(false);   // hidden until a long-press shows it

        // The left-edge SwipeBack strip sits in front of the messages and forwards taps via
        // ClickPassthrough — but its loop stops at the first button, shadowing a bubble's
        // long-press handler. Opt that one passthrough into delivering press events to all
        // stacked targets so long-press works over the swipe strip too.
        ConfigureSwipeBackPassthrough();

        EditorSceneManager.MarkSceneDirty(chatPanel.gameObject.scene);
        Debug.Log($"[ReactionBar] Built reaction-bar overlay under '{chatPanel.name}'. " +
                  "Verify the 6 quick-emoji sprites (1f44d 2764-fe0f 1f602 1f62e 1f622 1f64f) are in the static atlas. " +
                  "Save the scene to persist.");
    }

    private static Button BuildEmojiButton(Transform parent, int index)
    {
        var go = new GameObject($"Emoji{index}", typeof(RectTransform), typeof(Image),
                                typeof(Button), typeof(LayoutElement), typeof(ImageWithRoundedCorners));
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = ButtonSize;
        le.preferredHeight = ButtonSize;

        var img = go.GetComponent<Image>();
        img.color = Color.white;          // ReactionBarController tints this for the selected emoji
        img.raycastTarget = true;

        var rounded = go.GetComponent<ImageWithRoundedCorners>();
        rounded.radius = ButtonRadius;
        rounded.Validate();
        rounded.Refresh();

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;   // selected highlight is driven by the controller
        button.targetGraphic = img;
        var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(go.transform, false);
        Stretch((RectTransform)labelGo.transform);
        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;          // filled at runtime via UnicodeEmojiConverter
        tmp.fontSize = EmojiFont;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        return button;
    }

    [MenuItem("Tools/Chat/Attach Long-Press To Both Bubbles")]
    public static void AttachToBothBubbles()
    {
        AttachGesture(IncomingPath);
        AttachGesture(OutgoingPath);
    }

    private static void AttachGesture(string prefabPath)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) { Debug.LogError($"[ReactionBar] Failed to load prefab at {prefabPath}"); return; }
        try
        {
            var item = root.GetComponent<MessageItemView>();
            if (item == null) { Debug.LogError($"[ReactionBar] No MessageItemView on {prefabPath}"); return; }
            if (item.bubbleBackground == null)
            { Debug.LogError($"[ReactionBar] MessageItemView.bubbleBackground unassigned on {prefabPath}"); return; }

            var bubbleGo = item.bubbleBackground.gameObject;
            item.bubbleBackground.raycastTarget = true;          // gesture must receive pointer events
            if (bubbleGo.GetComponent<MessageBubbleLongPress>() == null)
                bubbleGo.AddComponent<MessageBubbleLongPress>();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ReactionBar] Attached MessageBubbleLongPress to '{bubbleGo.name}' in {prefabPath}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void ConfigureSwipeBackPassthrough()
    {
        var swipe = Object.FindFirstObjectByType<SwipeToBack>(FindObjectsInactive.Include);
        var passthrough = swipe != null ? swipe.GetComponent<ClickPassthrough>() : null;
        if (passthrough == null)
        {
            Debug.LogWarning("[ReactionBar] No ClickPassthrough on the SwipeBack panel — long-press over the " +
                             "left swipe strip won't fire until 'deliverPressToAllBehind' is enabled there.");
            return;
        }

        var so = new SerializedObject(passthrough);
        var p = so.FindProperty("deliverPressToAllBehind");
        if (p == null)
        {
            Debug.LogWarning("[ReactionBar] ClickPassthrough has no 'deliverPressToAllBehind' field — recompile, then re-run.");
            return;
        }
        p.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log("[ReactionBar] Enabled ClickPassthrough.deliverPressToAllBehind on the SwipeBack strip.");
    }

    private static RectTransform ResolveChatPanel()
    {
        var swipe = Object.FindFirstObjectByType<SwipeToBack>(FindObjectsInactive.Include);
        if (swipe != null && swipe.chatPanelToSlide != null) return swipe.chatPanelToSlide;

        // Fallback: the panel hosting the message list.
        var list = Object.FindFirstObjectByType<MessageListView>(FindObjectsInactive.Include);
        if (list != null)
        {
            var canvas = list.GetComponentInParent<Canvas>();
            if (canvas != null) return (RectTransform)canvas.rootCanvas.transform;
        }
        return null;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetRef(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p == null) { Debug.LogWarning($"[ReactionBar] Property '{prop}' not found on {so.targetObject}"); return; }
        p.objectReferenceValue = value;
        if (value == null)
            Debug.LogWarning($"[ReactionBar] {so.targetObject.GetType().Name}.{prop} set to null — wire manually.");
    }
}
#endif

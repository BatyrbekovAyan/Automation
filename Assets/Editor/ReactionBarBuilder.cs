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

    // Emoji picker bottom sheet
    private const float PickerSheetHeight  = 1180f;
    private const float PickerTopRadius    = 60f;
    private const float PickerHeaderHeight = 132f;
    private const float PickerCell         = 144f;
    private const int   PickerColumns      = 6;

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
        var plus = BuildPlusButton(barGo.transform);   // 7th item — opens the full picker (Plan B)

        // ── Wire the controller ──
        var controller = rootGo.GetComponent<ReactionBarController>();
        var so = new SerializedObject(controller);
        SetRef(so, "content", contentGo);
        SetRef(so, "scrimButton", scrimButton);
        SetRef(so, "bar", barRt);
        SetRef(so, "plusButton", plus);
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

    // The "+" that opens the full picker. A light-gray circle with a "+" drawn from two
    // Image bars — TMP-text glyph icons don't render in this project, so never use a TMP "+".
    private static Button BuildPlusButton(Transform parent)
    {
        var go = new GameObject("Plus", typeof(RectTransform), typeof(Image),
                                typeof(Button), typeof(LayoutElement), typeof(ImageWithRoundedCorners));
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = ButtonSize;
        le.preferredHeight = ButtonSize;

        var bg = go.GetComponent<Image>();
        bg.color = new Color32(0xEC, 0xEC, 0xEE, 0xFF);   // light gray so it reads against the white bar
        bg.raycastTarget = true;

        var rounded = go.GetComponent<ImageWithRoundedCorners>();
        rounded.radius = ButtonRadius;
        rounded.Validate();
        rounded.Refresh();

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = bg;
        var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;

        var glyph = new Color32(0x6E, 0x6E, 0x73, 0xFF);
        MakePlusBar(go.transform, new Vector2(44f, 8f), glyph);   // horizontal
        MakePlusBar(go.transform, new Vector2(8f, 44f), glyph);   // vertical
        return button;
    }

    private static void MakePlusBar(Transform parent, Vector2 size, Color color)
    {
        var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(parent, false);
        var rt = (RectTransform)bar.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = size;
        var img = bar.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
    }

    [MenuItem("Tools/Chat/Build Emoji Picker")]
    public static void BuildEmojiPicker()
    {
        RectTransform chatPanel = ResolveChatPanel();
        if (chatPanel == null)
        {
            Debug.LogError("[ReactionBar] Could not resolve the chat panel for the emoji picker.");
            return;
        }

        foreach (var prior in chatPanel.GetComponentsInChildren<EmojiPickerController>(includeInactive: true))
            Object.DestroyImmediate(prior.gameObject);

        var rootGo = new GameObject("EmojiPickerOverlay", typeof(RectTransform), typeof(EmojiPickerController));
        rootGo.transform.SetParent(chatPanel, false);
        rootGo.transform.SetAsLastSibling();
        Stretch((RectTransform)rootGo.transform);

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(rootGo.transform, false);
        Stretch((RectTransform)contentGo.transform);

        // Scrim
        var scrimGo = new GameObject("Scrim", typeof(RectTransform), typeof(Image), typeof(Button));
        scrimGo.transform.SetParent(contentGo.transform, false);
        Stretch((RectTransform)scrimGo.transform);
        var scrimImg = scrimGo.GetComponent<Image>();
        scrimImg.color = ScrimColor;
        scrimImg.raycastTarget = true;
        var scrimButton = scrimGo.GetComponent<Button>();
        scrimButton.transition = Selectable.Transition.None;
        var sNav = scrimButton.navigation; sNav.mode = Navigation.Mode.None; scrimButton.navigation = sNav;

        // Sheet — bottom-anchored, top corners rounded
        var sheetGo = new GameObject("Sheet", typeof(RectTransform), typeof(Image), typeof(ImageWithIndependentRoundedCorners));
        sheetGo.transform.SetParent(contentGo.transform, false);
        var sheetRt = (RectTransform)sheetGo.transform;
        sheetRt.anchorMin = new Vector2(0f, 0f);
        sheetRt.anchorMax = new Vector2(1f, 0f);
        sheetRt.pivot = new Vector2(0.5f, 0f);
        sheetRt.sizeDelta = new Vector2(0f, PickerSheetHeight);
        sheetRt.anchoredPosition = Vector2.zero;
        var sheetImg = sheetGo.GetComponent<Image>();
        sheetImg.color = Color.white;
        sheetImg.raycastTarget = true;   // taps on the sheet don't fall through to the scrim
        var sheetRounded = sheetGo.GetComponent<ImageWithIndependentRoundedCorners>();
        sheetRounded.r = new Vector4(PickerTopRadius, PickerTopRadius, 0f, 0f);
        sheetRounded.Validate();
        sheetRounded.Refresh();

        // Grabber pill
        var grab = new GameObject("Grabber", typeof(RectTransform), typeof(Image), typeof(ImageWithRoundedCorners));
        grab.transform.SetParent(sheetGo.transform, false);
        var grabRt = (RectTransform)grab.transform;
        grabRt.anchorMin = grabRt.anchorMax = new Vector2(0.5f, 1f);
        grabRt.pivot = new Vector2(0.5f, 1f);
        grabRt.sizeDelta = new Vector2(108f, 12f);
        grabRt.anchoredPosition = new Vector2(0f, -28f);
        grab.GetComponent<Image>().color = new Color32(0xC8, 0xC8, 0xCC, 0xFF);
        grab.GetComponent<Image>().raycastTarget = false;
        var grabRound = grab.GetComponent<ImageWithRoundedCorners>();
        grabRound.radius = 6f; grabRound.Validate(); grabRound.Refresh();

        // Title
        var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleGo.transform.SetParent(sheetGo.transform, false);
        var titleRt = (RectTransform)titleGo.transform;
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(0f, 56f);
        titleRt.anchoredPosition = new Vector2(0f, -56f);
        var title = titleGo.GetComponent<TextMeshProUGUI>();
        title.text = "React";
        title.fontSize = 44f;
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color32(0x11, 0x1B, 0x21, 0xFF);
        title.raycastTarget = false;

        // ScrollRect → Viewport (mask) → GridContent
        var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(sheetGo.transform, false);
        var scrollRt = (RectTransform)scrollGo.transform;
        scrollRt.anchorMin = new Vector2(0f, 0f);
        scrollRt.anchorMax = new Vector2(1f, 1f);
        scrollRt.offsetMin = new Vector2(0f, 24f);
        scrollRt.offsetMax = new Vector2(0f, -PickerHeaderHeight);

        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        Stretch((RectTransform)viewportGo.transform);
        var vpImg = viewportGo.GetComponent<Image>();
        vpImg.color = new Color(1f, 1f, 1f, 0.004f);   // near-invisible but a raycast target so empty space drag-scrolls
        vpImg.raycastTarget = true;

        var gridGo = new GameObject("GridContent", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        gridGo.transform.SetParent(viewportGo.transform, false);
        var gridRt = (RectTransform)gridGo.transform;
        gridRt.anchorMin = new Vector2(0f, 1f);
        gridRt.anchorMax = new Vector2(1f, 1f);
        gridRt.pivot = new Vector2(0.5f, 1f);
        gridRt.anchoredPosition = Vector2.zero;
        gridRt.sizeDelta = new Vector2(0f, 0f);
        var grid = gridGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(PickerCell, PickerCell);
        grid.spacing = new Vector2(12f, 12f);
        grid.padding = new RectOffset(48, 48, 12, 48);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = PickerColumns;
        grid.childAlignment = TextAnchor.UpperCenter;
        var gridFitter = gridGo.GetComponent<ContentSizeFitter>();
        gridFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        gridFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.viewport = (RectTransform)viewportGo.transform;
        scroll.content = gridRt;

        // Wire EmojiPickerController
        var controller = rootGo.GetComponent<EmojiPickerController>();
        var so = new SerializedObject(controller);
        SetRef(so, "content", contentGo);
        SetRef(so, "scrimButton", scrimButton);
        SetRef(so, "sheet", sheetRt);
        SetRef(so, "gridContent", gridRt);
        so.ApplyModifiedPropertiesWithoutUndo();

        contentGo.SetActive(false);

        EditorSceneManager.MarkSceneDirty(chatPanel.gameObject.scene);
        Debug.Log($"[ReactionBar] Built emoji picker sheet under '{chatPanel.name}' ({ReactionEmojiCatalog.All.Length} emoji, " +
                  $"{PickerColumns} cols). Save the scene to persist.");
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

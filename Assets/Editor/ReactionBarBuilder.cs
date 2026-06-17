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
        var plus = BuildPlusButton(barGo.transform);    // 7th item — opens the full picker

        // ── Action menu (Reply / Copy / Forward card below the bar) ──
        var (menuRt, replyBtn, copyBtn, forwardBtn) = BuildActionMenu(contentGo.transform);

        // ── Wire the controller ──
        var controller = rootGo.GetComponent<ReactionBarController>();
        var so = new SerializedObject(controller);
        SetRef(so, "content", contentGo);
        SetRef(so, "scrimButton", scrimButton);
        SetRef(so, "bar", barRt);
        SetRef(so, "plusButton", plus);
        SetRef(so, "actionMenu", menuRt);
        SetRef(so, "replyAction", replyBtn);
        SetRef(so, "copyAction", copyBtn);
        SetRef(so, "forwardAction", forwardBtn);
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

    // The action menu card — a white rounded card with three tap rows (Reply/Copy/Forward)
    // positioned by the controller just below the emoji bar. Lives under Content so it
    // hides/shows with the overlay. Returns: (card RectTransform, reply, copy, forward buttons).
    private static (RectTransform menu, Button reply, Button copy, Button forward)
        BuildActionMenu(Transform contentParent)
    {
        // ── Card (white, rounded, VLG hugs rows) ──
        var cardGo = new GameObject("ActionMenu", typeof(RectTransform), typeof(Image),
                                    typeof(VerticalLayoutGroup), typeof(ContentSizeFitter),
                                    typeof(ImageWithRoundedCorners));
        cardGo.transform.SetParent(contentParent, false);

        var cardRt = (RectTransform)cardGo.transform;
        cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        // Fixed width; height driven by VLG + ContentSizeFitter.
        cardRt.sizeDelta = new Vector2(360f, 0f);

        var cardImg = cardGo.GetComponent<Image>();
        cardImg.color = Color.white;
        cardImg.raycastTarget = true;   // taps on the card don't fall through to scrim

        var vlg = cardGo.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.spacing = 0f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var fitter = cardGo.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var cardRounded = cardGo.GetComponent<ImageWithRoundedCorners>();
        cardRounded.radius = 16f;
        cardRounded.Validate();
        cardRounded.Refresh();

        // Read message font from an existing TMP in the scene for correct GUID wiring.
        TMP_FontAsset messageFont = FindMessageFont();

        // Build the three rows.
        var replyBtn   = BuildMenuRow(cardGo.transform, "RowReply",   "Reply",   false, messageFont);
        BuildDivider(cardGo.transform);
        var copyBtn    = BuildMenuRow(cardGo.transform, "RowCopy",    "Copy",    false, messageFont);
        BuildDivider(cardGo.transform);
        var forwardBtn = BuildMenuRow(cardGo.transform, "RowForward", "Forward", false, messageFont);

        return (cardRt, replyBtn, copyBtn, forwardBtn);
    }

    // One tap row: HLG with optional reply-arrow icon + TMP label. Height ~88 ref units.
    private static Button BuildMenuRow(Transform parent, string name, string label,
                                       bool showArrowIcon, TMP_FontAsset font)
    {
        const float RowHeight   = 88f;
        const float LeadingPad  = 24f;   // left padding before icon / text
        const float IconSize    = 40f;   // small icon square
        const float IconSpacing = 16f;   // gap between icon and label

        var rowGo = new GameObject(name, typeof(RectTransform), typeof(Image),
                                   typeof(Button), typeof(LayoutElement));
        rowGo.transform.SetParent(parent, false);

        var rowImg = rowGo.GetComponent<Image>();
        rowImg.color = Color.white;
        rowImg.raycastTarget = true;

        var rowBtn = rowGo.GetComponent<Button>();
        rowBtn.transition = Selectable.Transition.ColorTint;
        rowBtn.targetGraphic = rowImg;
        var rowNav = rowBtn.navigation; rowNav.mode = Navigation.Mode.None; rowBtn.navigation = rowNav;

        var rowLe = rowGo.GetComponent<LayoutElement>();
        rowLe.preferredHeight = RowHeight;
        rowLe.flexibleWidth = 1f;

        // Inner HLG so icon + label sit side by side with left-aligned padding.
        var hlgGo = new GameObject("Inner", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        hlgGo.transform.SetParent(rowGo.transform, false);
        var hlgRt = (RectTransform)hlgGo.transform;
        hlgRt.anchorMin = Vector2.zero;
        hlgRt.anchorMax = Vector2.one;
        hlgRt.offsetMin = hlgRt.offsetMax = Vector2.zero;
        var hlg = hlgGo.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset((int)LeadingPad, (int)LeadingPad, 0, 0);
        hlg.spacing = IconSpacing;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        if (showArrowIcon)
        {
            // Icon container — a fixed-size transparent pivot that holds the arrow bars.
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(LayoutElement));
            iconGo.transform.SetParent(hlgGo.transform, false);
            var iconLe = iconGo.GetComponent<LayoutElement>();
            iconLe.preferredWidth  = IconSize;
            iconLe.preferredHeight = IconSize;

            var glyph = new Color32(0x6E, 0x6E, 0x73, 0xFF);
            // Scaled-down arrow bars to fit the smaller icon container.
            MakeArrowBar(iconGo.transform, new Vector2(22f, 5f),   0f,  new Vector2(4f, 0f),   glyph);
            MakeArrowBar(iconGo.transform, new Vector2(13f, 5f),  45f,  new Vector2(-8f, 4f),  glyph);
            MakeArrowBar(iconGo.transform, new Vector2(13f, 5f), -45f,  new Vector2(-8f, -4f), glyph);
        }

        // TMP label.
        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI),
                                     typeof(LayoutElement));
        labelGo.transform.SetParent(hlgGo.transform, false);
        var labelLe = labelGo.GetComponent<LayoutElement>();
        labelLe.flexibleWidth = 1f;

        var tmp = labelGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 36f;
        tmp.color = new Color32(0x11, 0x1B, 0x21, 0xFF);
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        if (font != null) tmp.font = font;

        return rowBtn;
    }

    // Thin 1px divider between rows.
    private static void BuildDivider(Transform parent)
    {
        var divGo = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        divGo.transform.SetParent(parent, false);
        var divImg = divGo.GetComponent<Image>();
        divImg.color = new Color32(0xE9, 0xED, 0xEF, 0xFF);
        divImg.raycastTarget = false;
        var divLe = divGo.GetComponent<LayoutElement>();
        divLe.preferredHeight = 1f;
        divLe.flexibleWidth = 1f;
    }

    // Scan the scene for any TMP in the message list to reuse its font asset.
    private static TMP_FontAsset FindMessageFont()
    {
        var tmp = Object.FindFirstObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
        return tmp != null ? tmp.font : null;
    }

    // The reply-arrow button. Same circle background as the "+" button.
    // Arrow icon drawn from three Image bars (no TMP glyph):
    //   • a horizontal shaft pointing left
    //   • an upper-left arrowhead arm (rotated +45°)
    //   • a lower-left arrowhead arm (rotated -45°)
    private static Button BuildReplyButton(Transform parent)
    {
        var go = new GameObject("Reply", typeof(RectTransform), typeof(Image),
                                typeof(Button), typeof(LayoutElement), typeof(ImageWithRoundedCorners));
        go.transform.SetParent(parent, false);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth  = ButtonSize;
        le.preferredHeight = ButtonSize;

        var bg = go.GetComponent<Image>();
        bg.color = new Color32(0xEC, 0xEC, 0xEE, 0xFF);   // matches the "+" button background
        bg.raycastTarget = true;

        var rounded = go.GetComponent<ImageWithRoundedCorners>();
        rounded.radius = ButtonRadius;
        rounded.Validate();
        rounded.Refresh();

        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = bg;
        var nav = button.navigation; nav.mode = Navigation.Mode.None; button.navigation = nav;

        // Draw a left-pointing reply arrow using Image bars — same technique as BuildPlusButton.
        // The arrow consists of: horizontal shaft + two arrowhead arms at ±45°.
        var glyph = new Color32(0x6E, 0x6E, 0x73, 0xFF);   // matches "+" glyph color

        // Horizontal shaft: offset slightly right of center so the arrowhead tip lands near center.
        MakeArrowBar(go.transform, new Vector2(36f, 7f), 0f,   new Vector2(6f, 0f),  glyph);  // shaft
        MakeArrowBar(go.transform, new Vector2(20f, 7f), 45f,  new Vector2(-14f, 8f), glyph); // upper arm
        MakeArrowBar(go.transform, new Vector2(20f, 7f), -45f, new Vector2(-14f, -8f), glyph); // lower arm

        return button;
    }

    // Like MakePlusBar but supports rotation and positional offset.
    private static void MakeArrowBar(Transform parent, Vector2 size, float angleDeg,
                                     Vector2 offset, Color color)
    {
        var bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(parent, false);
        var rt = (RectTransform)bar.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
        rt.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
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

        // Vertical list — EmojiPickerController adds one (header + grid) section per category at runtime.
        var listGo = new GameObject("ListContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        listGo.transform.SetParent(viewportGo.transform, false);
        var listRt = (RectTransform)listGo.transform;
        listRt.anchorMin = new Vector2(0f, 1f);
        listRt.anchorMax = new Vector2(1f, 1f);
        listRt.pivot = new Vector2(0.5f, 1f);
        listRt.anchoredPosition = Vector2.zero;
        listRt.sizeDelta = Vector2.zero;
        var vlg = listGo.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(24, 24, 12, 48);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var listFitter = listGo.GetComponent<ContentSizeFitter>();
        listFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.viewport = (RectTransform)viewportGo.transform;
        scroll.content = listRt;

        // Wire EmojiPickerController
        var controller = rootGo.GetComponent<EmojiPickerController>();
        var so = new SerializedObject(controller);
        SetRef(so, "content", contentGo);
        SetRef(so, "scrimButton", scrimButton);
        SetRef(so, "sheet", sheetRt);
        SetRef(so, "listContent", listRt);
        so.ApplyModifiedPropertiesWithoutUndo();

        contentGo.SetActive(false);

        EditorSceneManager.MarkSceneDirty(chatPanel.gameObject.scene);
        Debug.Log($"[ReactionBar] Built emoji picker sheet under '{chatPanel.name}' " +
                  $"({ReactionEmojiCatalog.All.Length} emoji across {ReactionEmojiCatalog.Categories.Length} categories). Save the scene to persist.");
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

            // Primary: bubble background Image.
            var bubbleGo = item.bubbleBackground.gameObject;
            item.bubbleBackground.raycastTarget = true;
            EnsureLongPress(bubbleGo, "bubble background");

            // Media-region targets: messageImage covers Image/Video/Sticker taps.
            // downloadButton, audioPanel, documentPanel cover their respective media surfaces.
            // Each MessageBubbleLongPress.Awake() calls GetComponentInParent<MessageItemView>()
            // so view resolution is automatic — no extra wiring needed.
            if (item.messageImage != null)
                EnsureLongPress(item.messageImage.gameObject, "messageImage");

            if (item.downloadButton != null)
                EnsureLongPress(item.downloadButton.gameObject, "downloadButton");

            if (item.audioPanel != null)
                EnsureLongPress(item.audioPanel, "audioPanel");

            if (item.documentPanel != null)
                EnsureLongPress(item.documentPanel, "documentPanel");

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ReactionBar] Attached MessageBubbleLongPress to bubble + media surfaces in {prefabPath}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>Adds MessageBubbleLongPress to <paramref name="go"/> if not already present.</summary>
    private static void EnsureLongPress(GameObject go, string label)
    {
        if (go == null) return;
        if (go.GetComponent<MessageBubbleLongPress>() == null)
        {
            go.AddComponent<MessageBubbleLongPress>();
            Debug.Log($"[ReactionBar]   + MessageBubbleLongPress on '{go.name}' ({label})");
        }
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

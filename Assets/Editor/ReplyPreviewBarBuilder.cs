#if UNITY_EDITOR
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the compose-time reply-preview bar into the open Main scene and wires the
/// four MessagesBottomPanel.[Header("Reply Preview")] serialized refs.
///
/// Placement: sibling of BottomPanel inside MovingArea, inserted ABOVE BottomPanel.
/// ExpandableInput lives on BottomPanel and snapshots bottomPanelRect.rect.height once
/// in Start() as its own minHeight, then stomps bottomPanelRect.sizeDelta every frame.
/// Placing the bar INSIDE BottomPanel would corrupt that baseline; as a sibling it adds
/// height independently and KeyboardAwarePanel lifts the entire MovingArea above the
/// keyboard without knowing the bar exists.
///
/// Bar hierarchy (sibling of BottomPanel in MovingArea):
///
///   ReplyPreviewBar (HLG + LayoutElement)
///     Bg (Image white + RoundedCorners ~12)
///       AccentBar (Image green #1FA855 — 6px wide)
///       TextColumn (VLG)
///         Sender (TMP bold green — one line, ellipsis)
///         Snippet (TMP normal gray — one line, ellipsis)
///       CancelButton (Button)
///         CancelBg (Image light-gray + RoundedCorners circle)
///           XHoriz (Image bar)
///           XVert  (Image bar)
///
/// Sizes in 1080×1920 canvas reference units (1 dp ~= 3 units).
/// </summary>
public static class ReplyPreviewBarBuilder
{
    private const string BarName        = "ReplyPreviewBar";
    private const string MovingAreaName = "MovingArea";

    // ── Layout ────────────────────────────────────────────────────────────────
    private const float BarHeight       = 150f;  // tall enough for two text rows (sender 38 + snippet 42) inside the V padding
    private const float BgPaddingH      = 24f;   // left/right inside Bg
    private const float BgPaddingV      = 14f;   // top/bottom inside Bg
    private const float BgSpacing       = 16f;   // between Accent, TextColumn, Cancel
    private const float BgRadius        = 12f;   // rounded card

    private const float AccentWidth     = 6f;    // thin left-edge colored strip
    private const float TextColSpacing  = 4f;    // gap between Sender and Snippet rows

    private const float SenderFontSize  = 38f;   // matches SenderName in message prefabs
    private const float SnippetFontSize = 42f;   // matches message body TMP

    private const float CancelSize      = 66f;   // ~22dp tap circle
    private const float CancelRadius    = 33f;   // half → perfect circle
    private const float XBarLong        = 32f;
    private const float XBarThick       = 6f;

    // ── Colors ────────────────────────────────────────────────────────────────
    private static readonly Color BgColor      = Color.white;
    private static readonly Color AccentColor  = new Color32(0x1F, 0xA8, 0x55, 0xFF); // #1FA855 WhatsApp green
    private static readonly Color SenderColor  = new Color32(0x1F, 0xA8, 0x55, 0xFF); // #1FA855
    private static readonly Color SnippetColor = new Color32(0x66, 0x77, 0x81, 0xFF); // #667781 gray
    private static readonly Color CancelBgColor= new Color32(0xEC, 0xEC, 0xEE, 0xFF); // light gray
    private static readonly Color XColor       = new Color32(0x6E, 0x6E, 0x73, 0xFF); // dark gray

    // ── Menu item ─────────────────────────────────────────────────────────────

    [MenuItem("Tools/Chat/Build Reply Preview Bar")]
    public static void Build()
    {
        var bottomPanel = Object.FindFirstObjectByType<MessagesBottomPanel>(FindObjectsInactive.Include);
        if (bottomPanel == null)
        {
            Debug.LogError("[ReplyPreviewBar] MessagesBottomPanel not found in the open scene. Open Main.unity first.");
            return;
        }

        // Walk up from BottomPanel to find MovingArea.
        Transform movingArea = bottomPanel.transform.parent;
        while (movingArea != null && movingArea.name != MovingAreaName)
            movingArea = movingArea.parent;

        if (movingArea == null)
        {
            Debug.LogError($"[ReplyPreviewBar] Could not find '{MovingAreaName}' in the parent chain " +
                           $"of '{bottomPanel.gameObject.name}'. Placement aborted.");
            return;
        }

        // Idempotent: destroy any existing bar anywhere under MovingArea.
        foreach (Transform child in movingArea)
        {
            if (child.name == BarName)
            {
                Object.DestroyImmediate(child.gameObject);
                break;
            }
        }

        // Read the font from the panel's own input field so we never hardcode GUIDs.
        TMP_FontAsset inputFont = bottomPanel.inputField != null
            ? bottomPanel.inputField.textComponent?.font
            : null;

        // ── Root row ──────────────────────────────────────────────────────────
        // The root is transparent (no Image) so only the Bg card draws. A LayoutElement
        // fixes the height so nothing else in MovingArea is disturbed.
        var rootGo = new GameObject(BarName, typeof(RectTransform), typeof(LayoutElement));
        rootGo.transform.SetParent(movingArea, false);

        var rootRt = (RectTransform)rootGo.transform;
        rootRt.anchorMin = new Vector2(0f, 1f);
        rootRt.anchorMax = new Vector2(1f, 1f);
        rootRt.pivot     = new Vector2(0.5f, 1f);
        rootRt.sizeDelta = new Vector2(0f, BarHeight);

        // Position root directly above BottomPanel by placing it at the bottom-left
        // anchor of MovingArea but offset by BottomPanel height. MovingArea is full-stretch;
        // we anchor to bottom and push up by the BottomPanel's sizeDelta.y so the bar
        // sits flush on top of the bottom panel regardless of keyboard state.
        var bottomPanelRt = (RectTransform)bottomPanel.transform;
        rootRt.anchorMin        = new Vector2(0f, 0f);
        rootRt.anchorMax        = new Vector2(1f, 0f);
        rootRt.pivot            = new Vector2(0.5f, 0f);
        rootRt.anchoredPosition = new Vector2(0f, bottomPanelRt.sizeDelta.y);
        rootRt.sizeDelta        = new Vector2(0f, BarHeight);

        // Place it just before BottomPanel in the sibling order so it visually sits on top.
        int bottomPanelIndex = bottomPanel.transform.GetSiblingIndex();
        rootGo.transform.SetSiblingIndex(bottomPanelIndex);

        var le = rootGo.GetComponent<LayoutElement>();
        le.preferredHeight = BarHeight;
        le.minHeight       = BarHeight;

        // ── White card background ──────────────────────────────────────────────
        var bgGo = new GameObject("Bg",
            typeof(RectTransform), typeof(Image),
            typeof(ImageWithRoundedCorners), typeof(HorizontalLayoutGroup));
        bgGo.transform.SetParent(rootGo.transform, false);

        var bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        var bgImg = bgGo.GetComponent<Image>();
        bgImg.color       = BgColor;
        bgImg.sprite      = null;        // null sprite + RoundedCorners for clean edges
        bgImg.raycastTarget = true;      // block taps falling through to messages

        var bgRounded = bgGo.GetComponent<ImageWithRoundedCorners>();
        bgRounded.radius = BgRadius;

        var bgHlg = bgGo.GetComponent<HorizontalLayoutGroup>();
        bgHlg.padding = new RectOffset((int)BgPaddingH, (int)BgPaddingH, (int)BgPaddingV, (int)BgPaddingV);
        bgHlg.spacing              = BgSpacing;
        bgHlg.childAlignment       = TextAnchor.MiddleLeft;
        bgHlg.childControlWidth    = true;
        bgHlg.childControlHeight   = true;
        bgHlg.childForceExpandWidth  = false;
        bgHlg.childForceExpandHeight = false;

        // ── Accent bar ────────────────────────────────────────────────────────
        var accentGo = new GameObject("AccentBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        accentGo.transform.SetParent(bgGo.transform, false);

        var accentImg = accentGo.GetComponent<Image>();
        accentImg.color       = AccentColor;
        accentImg.sprite      = null;
        accentImg.raycastTarget = false;

        var accentLe = accentGo.GetComponent<LayoutElement>();
        accentLe.preferredWidth  = AccentWidth;
        accentLe.flexibleHeight  = 1f;   // stretch to full card height

        // ── Text column (Sender + Snippet) ────────────────────────────────────
        var textColGo = new GameObject("TextColumn",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        textColGo.transform.SetParent(bgGo.transform, false);

        var textColVlg = textColGo.GetComponent<VerticalLayoutGroup>();
        textColVlg.spacing              = TextColSpacing;
        textColVlg.childAlignment       = TextAnchor.UpperLeft;
        textColVlg.childControlWidth    = true;
        textColVlg.childControlHeight   = true;
        textColVlg.childForceExpandWidth  = true;
        textColVlg.childForceExpandHeight = false;
        textColVlg.padding = new RectOffset(0, 0, 0, 0);

        var textColLe = textColGo.GetComponent<LayoutElement>();
        textColLe.flexibleWidth = 1f;    // consume remaining width after accent + cancel

        // Sender TMP
        var senderGo = new GameObject("Sender", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        senderGo.transform.SetParent(textColGo.transform, false);
        var senderTmp = senderGo.GetComponent<TextMeshProUGUI>();
        senderTmp.text      = "Sender";
        if (inputFont != null) senderTmp.font = inputFont;
        senderTmp.fontSize  = SenderFontSize;
        senderTmp.fontStyle = FontStyles.Bold;
        senderTmp.color     = SenderColor;
        senderTmp.alignment = TextAlignmentOptions.MidlineLeft;
        senderTmp.enableWordWrapping = false;
        senderTmp.overflowMode  = TextOverflowModes.Ellipsis;
        senderTmp.maxVisibleLines = 1;
        senderTmp.raycastTarget = false;
        // Pin a one-line row height so a sprite-only value can't collapse the VLG row to ~0.
        var senderLe = senderGo.GetComponent<LayoutElement>();
        senderLe.minHeight = senderLe.preferredHeight = SenderFontSize * 1.32f;

        // Snippet TMP
        var snippetGo = new GameObject("Snippet", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        snippetGo.transform.SetParent(textColGo.transform, false);
        var snippetTmp = snippetGo.GetComponent<TextMeshProUGUI>();
        snippetTmp.text      = "Quoted message";
        if (inputFont != null) snippetTmp.font = inputFont;
        snippetTmp.fontSize  = SnippetFontSize;
        snippetTmp.fontStyle = FontStyles.Normal;
        snippetTmp.color     = SnippetColor;
        snippetTmp.alignment = TextAlignmentOptions.MidlineLeft;
        snippetTmp.enableWordWrapping = false;
        snippetTmp.overflowMode  = TextOverflowModes.Ellipsis;
        snippetTmp.maxVisibleLines = 1;
        snippetTmp.raycastTarget = false;
        // Pin a one-line row height so a sprite-only (emoji) snippet can't collapse the VLG row to ~0.
        var snippetLe = snippetGo.GetComponent<LayoutElement>();
        snippetLe.minHeight = snippetLe.preferredHeight = SnippetFontSize * 1.32f;

        // ── Cancel button ─────────────────────────────────────────────────────
        // A light-gray circle with an "×" drawn from two Image bars (TMP glyphs
        // don't render in this project, so never use a TMP "×" here).
        var cancelGo = new GameObject("CancelButton",
            typeof(RectTransform), typeof(Button), typeof(LayoutElement));
        cancelGo.transform.SetParent(bgGo.transform, false);

        var cancelBtn = cancelGo.GetComponent<Button>();
        cancelBtn.transition = Selectable.Transition.None;
        var cancelNav = cancelBtn.navigation;
        cancelNav.mode = Navigation.Mode.None;
        cancelBtn.navigation = cancelNav;

        var cancelLe = cancelGo.GetComponent<LayoutElement>();
        cancelLe.preferredWidth  = CancelSize;
        cancelLe.preferredHeight = CancelSize;

        // Circle background inside the button
        var cancelBgGo = new GameObject("CancelBg",
            typeof(RectTransform), typeof(Image), typeof(ImageWithRoundedCorners));
        cancelBgGo.transform.SetParent(cancelGo.transform, false);

        var cancelBgRt = (RectTransform)cancelBgGo.transform;
        cancelBgRt.anchorMin = Vector2.zero;
        cancelBgRt.anchorMax = Vector2.one;
        cancelBgRt.offsetMin = Vector2.zero;
        cancelBgRt.offsetMax = Vector2.zero;

        var cancelBgImg = cancelBgGo.GetComponent<Image>();
        cancelBgImg.color       = CancelBgColor;
        cancelBgImg.sprite      = null;
        cancelBgImg.raycastTarget = true;   // the button needs a raycastable graphic to receive taps
        cancelBtn.targetGraphic = cancelBgImg;

        var cancelBgRounded = cancelBgGo.GetComponent<ImageWithRoundedCorners>();
        cancelBgRounded.radius = CancelRadius;

        // "×" from two Image bars rotated 45°
        MakeXBar(cancelBgGo.transform, new Vector2(XBarLong, XBarThick),  45f);
        MakeXBar(cancelBgGo.transform, new Vector2(XBarLong, XBarThick), -45f);

        // ── RoundedCorners: Validate + Refresh before SetActive(false) ─────────
        bgRounded.Validate();
        bgRounded.Refresh();
        cancelBgRounded.Validate();
        cancelBgRounded.Refresh();

        // ── Deactivate last (after V+R) ────────────────────────────────────────
        rootGo.SetActive(false);

        // ── Wire MessagesBottomPanel serialized refs ───────────────────────────
        var so = new SerializedObject(bottomPanel);
        SetRef(so, "replyPreviewBar",    rootGo);
        SetRef(so, "replyPreviewSender", senderTmp);
        SetRef(so, "replyPreviewSnippet", snippetTmp);
        SetRef(so, "replyPreviewCancel", cancelBtn);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(bottomPanel.gameObject.scene);
        Debug.Log($"[ReplyPreviewBar] Built '{BarName}' as sibling of '{bottomPanel.gameObject.name}' " +
                  $"inside '{movingArea.name}' (siblingIndex={rootGo.transform.GetSiblingIndex()}). " +
                  "The bar is INACTIVE — MessagesBottomPanel.HandleReplyTargetChanged activates it. " +
                  "Save the scene to persist.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a single bar of the "×" glyph, rotated to the given angle.
    /// Using Image bars avoids the TMP-glyph rendering issue documented in project memory.
    /// </summary>
    private static void MakeXBar(Transform parent, Vector2 size, float zRotationDeg)
    {
        var bar = new GameObject("XBar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(parent, false);

        var rt = (RectTransform)bar.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = size;
        rt.localRotation    = Quaternion.Euler(0f, 0f, zRotationDeg);

        var img = bar.GetComponent<Image>();
        img.color         = XColor;
        img.sprite        = null;
        img.raycastTarget = false;
    }

    private static void SetRef(SerializedObject so, string propName, Object value)
    {
        var prop = so.FindProperty(propName);
        if (prop == null)
        {
            Debug.LogError($"[ReplyPreviewBar] Property '{propName}' not found on " +
                           $"{so.targetObject.GetType().Name}. " +
                           "Ensure the [Header(\"Reply Preview\")] fields exist in MessagesBottomPanel.");
            return;
        }
        prop.objectReferenceValue = value;
        if (value == null)
            Debug.LogWarning($"[ReplyPreviewBar] {so.targetObject.GetType().Name}.{propName} wired to null.");
    }
}
#endif

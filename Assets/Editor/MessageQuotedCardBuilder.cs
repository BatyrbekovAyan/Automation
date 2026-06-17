#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Nobi.UiRoundedCorners;

/// <summary>
/// Builds a QuotedCard child inside the Bubble container of each message-bubble prefab
/// and wires the five MessageItemView.[Header("Reply Quote")] serialized refs:
///   quotedCard, quotedAccentBar, quotedSenderText, quotedSnippetText, quotedThumbnail.
///
/// Card hierarchy (child of Bubble, inserted right after SenderName):
///
///   QuotedCard (Image bg + RoundedCorners + HLG + LayoutElement)
///     Accent   (Image — colored vertical bar, full card height)
///     TextColumn (VerticalLayoutGroup)
///       Sender   (TextMeshProUGUI — sender name, bold, accent-colored at runtime)
///       Snippet  (TextMeshProUGUI — quoted text preview, gray)
///     Thumbnail (Image + RoundedCorners — starts inactive; shown for media quotes)
///
/// Background tints:
///   Incoming prefab → light gray  #F0F2F0
///   Outgoing prefab → soft green  #C5EEB6
///
/// Sizes are in 1080-ref canvas units (dp × 3). RoundedCorners pattern follows the
/// project convention: add ImageWithRoundedCorners to the new GameObject ctor, call
/// Validate()+Refresh() once the hierarchy is wired, then deactivate LAST.
///
/// Idempotent — re-running destroys and rebuilds any existing QuotedCard.
/// </summary>
public static class MessageQuotedCardBuilder
{
    // ── Prefab paths ──────────────────────────────────────────────────────────
    private const string IncomingPath = "Assets/Prefabs/MessageTextIncoming.prefab";
    private const string OutgoingPath = "Assets/Prefabs/MessageTextOutgoing.prefab";

    // ── Object names ──────────────────────────────────────────────────────────
    private const string BubbleName      = "Bubble";
    private const string SenderNameObj   = "SenderName";
    private const string CardName        = "QuotedCard";
    private const string AccentName      = "Accent";
    private const string TextColumnName  = "TextColumn";
    private const string SenderTmpName   = "Sender";
    private const string SnippetTmpName  = "Snippet";
    private const string ThumbnailName   = "Thumbnail";

    // ── Layout constants (canvas ref units, dp×3) ─────────────────────────────
    private const int   CardPaddingH     = 12;   // HLG left/right/top/bottom
    private const int   CardSpacing      = 12;   // HLG between Accent, TextColumn, Thumbnail
    private const float CardMinHeight    = 120f;
    private const float CardRadius       = 16f;

    private const float AccentWidth      = 8f;

    private const int   TextColSpacing   = 2;    // VLG between Sender and Snippet

    // SenderName object in the prefab: font GUID 1cd715823fef34be4a3d3f3c5572594c, size 38.
    // Message body ("Text" object): font GUID e0cdfe2d6a51446bcba7d2df147e2415, size 42.
    // We load both at build time from the prefab instead of hardcoding GUIDs.
    private const float SenderFontSize   = 38f;  // matches SenderName TMP in the prefab
    private const float SnippetFontSize  = 42f;  // matches message body TMP in the prefab

    private const float ThumbSize        = 96f;  // ~32dp
    private const float ThumbRadius      = 8f;

    // ── Colors ────────────────────────────────────────────────────────────────
    private static readonly Color IncomingCardBg  = new Color32(0xF0, 0xF2, 0xF0, 0xFF);
    private static readonly Color OutgoingCardBg  = new Color32(0xC5, 0xEE, 0xB6, 0xFF);
    private static readonly Color DefaultAccent   = new Color32(0x1F, 0xA8, 0x55, 0xFF); // #1FA855 — overwritten at runtime
    private static readonly Color SnippetColor    = new Color32(0x66, 0x77, 0x81, 0xFF); // #667781

    // ── Menu items ────────────────────────────────────────────────────────────

    [MenuItem("Tools/Chat/Build Quoted Card")]
    public static void BuildBoth()
    {
        Build(IncomingPath, incoming: true);
        Build(OutgoingPath, incoming: false);
    }

    // ── Core builder ─────────────────────────────────────────────────────────

    private static void Build(string prefabPath, bool incoming)
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[QuotedCard] Failed to load prefab at {prefabPath}");
            return;
        }

        try
        {
            // 1. Find Bubble
            var bubble = FindChildRecursive(prefabRoot.transform, BubbleName);
            if (bubble == null)
            {
                Debug.LogError($"[QuotedCard] '{BubbleName}' not found under {prefabPath}.");
                return;
            }

            // 2. Idempotent: remove any prior QuotedCard
            var existing = bubble.Find(CardName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            // 3. Read font assets off the live prefab objects so we never hardcode GUIDs.
            var senderNameTmp = GetTmpFromChild(bubble, SenderNameObj);
            var bodyTmp       = FindTmpBodyInBubble(bubble);
            TMP_FontAsset senderFont = senderNameTmp != null ? senderNameTmp.font : null;
            TMP_FontAsset bodyFont   = bodyTmp   != null ? bodyTmp.font   : null;

            // Fall back to the other font if one is missing (both prefabs have both assets).
            if (senderFont == null) senderFont = bodyFont;
            if (bodyFont   == null) bodyFont   = senderFont;

            // 4. Determine insertion index: right after SenderName sibling.
            int insertIndex = GetInsertIndex(bubble, SenderNameObj);

            // 5. Build the card and its children.
            var card       = BuildCard(bubble, incoming);
            var accent     = BuildAccent(card.transform);
            var textColumn = BuildTextColumn(card.transform);
            var senderTmp  = BuildSenderTmp(textColumn.transform, senderFont);
            var snippetTmp = BuildSnippetTmp(textColumn.transform, bodyFont);
            var thumbnail  = BuildThumbnail(card.transform);

            // Insert at correct sibling position (after SenderName, or 0 if not found).
            card.transform.SetSiblingIndex(insertIndex);

            // 6. Force a layout pass so RoundedCorners knows its final size, then Validate/Refresh.
            //    (ImageWithRoundedCorners was already added in the ctor; Validate+Refresh here.)
            var cardRounded = card.GetComponent<ImageWithRoundedCorners>();
            if (cardRounded != null) { cardRounded.Validate(); cardRounded.Refresh(); }

            var thumbRounded = thumbnail.GetComponent<ImageWithRoundedCorners>();
            if (thumbRounded != null) { thumbRounded.Validate(); thumbRounded.Refresh(); }

            // 7. Deactivate last (after Validate/Refresh), per project convention.
            thumbnail.SetActive(false);
            card.SetActive(false);

            // 8. Wire the 5 refs onto MessageItemView.
            if (!WireMessageItemView(prefabRoot, card, accent, senderTmp, snippetTmp,
                                     thumbnail.GetComponent<Image>()))
                return;

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            Debug.Log($"[QuotedCard] Built QuotedCard under {prefabPath} → {BubbleName}/{CardName} " +
                      $"(incoming={incoming}, siblingIndex={card.transform.GetSiblingIndex()}). " +
                      $"senderFont={senderFont?.name ?? "null"}, bodyFont={bodyFont?.name ?? "null"}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // ── Child builders ────────────────────────────────────────────────────────

    /// <summary>
    /// QuotedCard root — Image bg + RoundedCorners + HLG + LayoutElement.
    /// Sprite is null; color provides the tinted background (per project rule: no UISprite.psd
    /// on surfaces — null sprite + RoundedCorners gives clean rounded bg).
    /// </summary>
    private static GameObject BuildCard(Transform bubble, bool incoming)
    {
        var go = new GameObject(
            CardName,
            typeof(RectTransform),
            typeof(Image),
            typeof(ImageWithRoundedCorners),
            typeof(HorizontalLayoutGroup),
            typeof(LayoutElement));
        go.transform.SetParent(bubble, false);

        var img = go.GetComponent<Image>();
        img.color = incoming ? IncomingCardBg : OutgoingCardBg;
        img.sprite = null;
        img.raycastTarget = false;

        var rounded = go.GetComponent<ImageWithRoundedCorners>();
        rounded.radius = CardRadius;

        var hlg = go.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(CardPaddingH, CardPaddingH, CardPaddingH, CardPaddingH);
        hlg.spacing = CardSpacing;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth  = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = CardMinHeight;

        return go;
    }

    /// <summary>
    /// Accent — colored vertical bar. LayoutElement flexibleHeight=1 stretches it to full card height.
    /// Color is overwritten at runtime per sender; default is the app's outgoing green.
    /// </summary>
    private static GameObject BuildAccent(Transform parent)
    {
        var go = new GameObject(AccentName, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = DefaultAccent;
        img.sprite = null;
        img.raycastTarget = false;

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth  = AccentWidth;
        le.flexibleHeight  = 1f;

        return go;
    }

    /// <summary>
    /// TextColumn — pure layout container for Sender + Snippet text rows.
    /// LayoutElement flexibleWidth=1 lets it consume all remaining card width.
    /// </summary>
    private static GameObject BuildTextColumn(Transform parent)
    {
        var go = new GameObject(TextColumnName, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = TextColSpacing;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(0, 0, 0, 0);

        var le = go.GetComponent<LayoutElement>();
        le.flexibleWidth = 1f;

        return go;
    }

    /// <summary>
    /// Sender TMP — bold sender name, accent-colored at runtime.
    /// Uses the same font asset as the SenderName object in the bubble (GUID 1cd715823fef34be4a3d3f3c5572594c, size 38).
    /// </summary>
    private static TextMeshProUGUI BuildSenderTmp(Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject(SenderTmpName, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = "Sender";
        if (font != null) tmp.font = font;
        tmp.fontSize = SenderFontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = DefaultAccent;   // overwritten at runtime to match the accent bar
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.maxVisibleLines = 1;
        tmp.raycastTarget = false;

        // Pin a one-line row height so a sprite-only (emoji) value can't collapse the VLG row to ~0.
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = SenderFontSize * 1.32f;

        return tmp;
    }

    /// <summary>
    /// Snippet TMP — quoted message preview, neutral gray.
    /// Uses the same font asset as the message body TMP (GUID e0cdfe2d6a51446bcba7d2df147e2415, size 42).
    /// </summary>
    private static TextMeshProUGUI BuildSnippetTmp(Transform parent, TMP_FontAsset font)
    {
        var go = new GameObject(SnippetTmpName, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = "Quoted message";
        if (font != null) tmp.font = font;
        tmp.fontSize = SnippetFontSize;
        tmp.fontStyle = FontStyles.Normal;
        tmp.color = SnippetColor;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.maxVisibleLines = 1;
        tmp.raycastTarget = false;

        // Pin a one-line row height so a sprite-only (emoji) snippet can't collapse the VLG row to ~0.
        var le = go.GetComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = SnippetFontSize * 1.32f;

        return tmp;
    }

    /// <summary>
    /// Thumbnail — ~32dp square with rounded corners. Starts inactive; shown only for media quotes.
    /// RoundedCorners Validate/Refresh is called after parenting (before SetActive(false)).
    /// </summary>
    private static GameObject BuildThumbnail(Transform parent)
    {
        var go = new GameObject(
            ThumbnailName,
            typeof(RectTransform),
            typeof(Image),
            typeof(ImageWithRoundedCorners),
            typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = Color.white;
        img.sprite = null;
        img.preserveAspect = true;
        img.raycastTarget = false;

        var rounded = go.GetComponent<ImageWithRoundedCorners>();
        rounded.radius = ThumbRadius;

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth  = ThumbSize;
        le.preferredHeight = ThumbSize;

        return go;
    }

    // ── Wiring ────────────────────────────────────────────────────────────────

    private static bool WireMessageItemView(
        GameObject prefabRoot,
        GameObject card,
        GameObject accent,
        TextMeshProUGUI senderTmp,
        TextMeshProUGUI snippetTmp,
        Image thumbnailImage)
    {
        var view = prefabRoot.GetComponent<MessageItemView>();
        if (view == null)
        {
            Debug.LogError("[QuotedCard] No MessageItemView on prefab root.");
            return false;
        }

        var so = new SerializedObject(view);

        SetRef(so, "quotedCard",        card);
        SetRef(so, "quotedAccentBar",   accent.GetComponent<Image>());
        SetRef(so, "quotedSenderText",  senderTmp);
        SetRef(so, "quotedSnippetText", snippetTmp);
        SetRef(so, "quotedThumbnail",   thumbnailImage);

        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the sibling index to insert QuotedCard at — the slot immediately after
    /// SenderName. If SenderName is not found, inserts at index 0 (top of the bubble).
    /// </summary>
    private static int GetInsertIndex(Transform bubble, string afterChildName)
    {
        for (int i = 0; i < bubble.childCount; i++)
        {
            if (bubble.GetChild(i).name == afterChildName)
                return i + 1;
        }
        return 0;
    }

    /// <summary>
    /// Gets the TextMeshProUGUI component from a direct child of parent by name.
    /// Returns null without error if the child doesn't exist — we null-guard at call sites.
    /// </summary>
    private static TextMeshProUGUI GetTmpFromChild(Transform parent, string childName)
    {
        var child = parent.Find(childName);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    /// <summary>
    /// Finds the message-body TMP in the bubble — the "Text" child that holds the chat text.
    /// Returns null if not found; caller falls back to senderFont.
    /// </summary>
    private static TextMeshProUGUI FindTmpBodyInBubble(Transform bubble)
    {
        // Direct child named "Text" is the standard message-body TMP in both prefabs.
        var direct = bubble.Find("Text");
        if (direct != null)
        {
            var tmp = direct.GetComponent<TextMeshProUGUI>();
            if (tmp != null) return tmp;
        }
        // Fallback: recursive search for any TMP with a large font size (the message body).
        return FindTmpRecursive(bubble);
    }

    private static TextMeshProUGUI FindTmpRecursive(Transform root)
    {
        var tmp = root.GetComponent<TextMeshProUGUI>();
        if (tmp != null && root.name == "Text") return tmp;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindTmpRecursive(root.GetChild(i));
            if (hit != null) return hit;
        }
        return null;
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

    private static void SetRef(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p == null)
        {
            Debug.LogError($"[QuotedCard] Property '{prop}' not found on {so.targetObject.GetType().Name}. " +
                           "Ensure the [Header(\"Reply Quote\")] fields exist in MessageItemView.");
            return;
        }
        p.objectReferenceValue = value;
        if (value == null)
            Debug.LogWarning($"[QuotedCard] {so.targetObject.GetType().Name}.{prop} wired to null.");
    }
}
#endif

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Nobi.UiRoundedCorners;

/// <summary>
/// Builds the scroll-to-bottom FAB (in the chat panel, beside the message ScrollRect) and
/// the UnreadSeparator prefab, then wires MessageListView's serialized refs. All sizes are
/// in 1080×1920 reference units (~3u = 1dp). Idempotent: destroys any prior FAB first.
///
/// Run: Tools ▸ Chat ▸ Build Unread Markers. Afterward, assign the chevron sprite on
/// ScrollToBottomFab/Circle/Chevron in the inspector (a down-chevron, ~52u, #54656F).
/// </summary>
public static class UnreadMarkersBuilder
{
    private const string FabName       = "ScrollToBottomFab";
    private const string SeparatorPath = "Assets/Prefabs/UnreadSeparator.prefab";

    // Reference units (dp × 3).
    private const float FabHitSize    = 132f; // 44dp touch target
    private const float FabCircleSize = 120f; // 40dp visible circle
    private const float ChevronSize   = 52f;
    private const float BadgeMinSize  = 48f;
    private const float RightMargin   = 48f;
    private const float BottomMargin  = 160f; // clears the input bar; nudge in-scene if needed
    private const float SeparatorHeight = 72f;

    private static readonly Color UnreadGreen     = new Color32(0x26, 0xB2, 0x5A, 0xFF); // #26B25A
    private static readonly Color SeparatorBarBg  = new Color32(0x26, 0xB2, 0x5A, 0x1F); // #26B25A @ ~12%
    private static readonly Color SeparatorLabel  = new Color32(0x1E, 0x7E, 0x45, 0xFF); // #1E7E45
    private static readonly Color ChevronGrey     = new Color32(0x54, 0x65, 0x6F, 0xFF); // #54656F
    private static readonly Color White           = Color.white;

    [MenuItem("Tools/Chat/Build Unread Markers")]
    public static void Build()
    {
        var mlv = Object.FindFirstObjectByType<MessageListView>(FindObjectsInactive.Include);
        if (mlv == null)
        {
            Debug.LogError("[UnreadMarkersBuilder] MessageListView not found in the open scene.");
            return;
        }
        if (mlv.scrollRect == null)
        {
            Debug.LogError("[UnreadMarkersBuilder] MessageListView.scrollRect is not assigned.");
            return;
        }

        Transform panel = mlv.scrollRect.transform.parent; // chat panel that holds the scroll view + input bar
        if (panel == null)
        {
            Debug.LogError("[UnreadMarkersBuilder] Could not resolve the chat panel (scrollRect has no parent).");
            return;
        }

        var fab = BuildFab(panel);
        var separatorPrefab = BuildSeparatorPrefab();

        WireMessageListView(mlv, fab, separatorPrefab);

        EditorSceneManager.MarkSceneDirty(mlv.gameObject.scene);
        Debug.Log("[UnreadMarkersBuilder] Built FAB + UnreadSeparator.prefab and wired MessageListView. " +
                  "Assign the down-chevron sprite on ScrollToBottomFab/Circle/Chevron in the inspector.");
    }

    // ── FAB ───────────────────────────────────────────────────────────────────────
    private static ScrollToBottomFab BuildFab(Transform panel)
    {
        var existing = panel.Find(FabName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // Root = transparent hit area (≥132u touch target) + Button + CanvasGroup + script.
        var root = NewChild(panel, FabName,
            typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button),
            typeof(ScrollToBottomFab));
        root.transform.SetAsLastSibling(); // render above the message list
        var rootRt = (RectTransform)root.transform;
        rootRt.anchorMin = new Vector2(1f, 0f);
        rootRt.anchorMax = new Vector2(1f, 0f);
        rootRt.pivot     = new Vector2(1f, 0f);
        rootRt.sizeDelta = new Vector2(FabHitSize, FabHitSize);
        rootRt.anchoredPosition = new Vector2(-RightMargin, BottomMargin);

        var hitImg = root.GetComponent<Image>();
        hitImg.color = new Color(1f, 1f, 1f, 0f); // invisible, but raycastable
        hitImg.raycastTarget = true;

        var cg = root.GetComponent<CanvasGroup>();
        cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false;

        var btn = root.GetComponent<Button>();
        btn.targetGraphic = hitImg;
        var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;

        // Visible white circle (centered), rounded to a circle + soft shadow.
        var circle = NewChild(root.transform, "Circle",
            typeof(RectTransform), typeof(Image), typeof(ImageWithRoundedCorners), typeof(Shadow));
        var circleRt = (RectTransform)circle.transform;
        circleRt.anchorMin = circleRt.anchorMax = new Vector2(0.5f, 0.5f);
        circleRt.pivot = new Vector2(0.5f, 0.5f);
        circleRt.sizeDelta = new Vector2(FabCircleSize, FabCircleSize);
        var circleImg = circle.GetComponent<Image>();
        circleImg.color = White;
        circleImg.raycastTarget = false;
        var circleRounded = circle.GetComponent<ImageWithRoundedCorners>();
        circleRounded.radius = FabCircleSize / 2f;
        circleRounded.Validate();
        circleRounded.Refresh();
        var shadow = circle.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.25f);
        shadow.effectDistance = new Vector2(0f, -4f);

        // Chevron (Image sprite — NOT a TMP glyph; sprite assigned by user in inspector).
        var chevron = NewChild(circle.transform, "Chevron", typeof(RectTransform), typeof(Image));
        var chevronRt = (RectTransform)chevron.transform;
        chevronRt.anchorMin = chevronRt.anchorMax = new Vector2(0.5f, 0.5f);
        chevronRt.pivot = new Vector2(0.5f, 0.5f);
        chevronRt.sizeDelta = new Vector2(ChevronSize, ChevronSize);
        var chevronImg = chevron.GetComponent<Image>();
        chevronImg.color = ChevronGrey;
        chevronImg.raycastTarget = false;
        chevronImg.preserveAspect = true;

        // Badge pill (top-right of FAB), grows with text.
        var badge = NewChild(root.transform, "Badge",
            typeof(RectTransform), typeof(Image), typeof(ImageWithRoundedCorners),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var badgeRt = (RectTransform)badge.transform;
        badgeRt.anchorMin = badgeRt.anchorMax = new Vector2(1f, 1f);
        badgeRt.pivot = new Vector2(1f, 1f);
        badgeRt.sizeDelta = new Vector2(BadgeMinSize, BadgeMinSize);
        badgeRt.anchoredPosition = new Vector2(-6f, -6f);
        var badgeImg = badge.GetComponent<Image>();
        badgeImg.color = UnreadGreen;
        badgeImg.raycastTarget = false;
        var badgeRounded = badge.GetComponent<ImageWithRoundedCorners>();
        badgeRounded.radius = BadgeMinSize / 2f;
        badgeRounded.Validate();
        badgeRounded.Refresh();
        var badgeHlg = badge.GetComponent<HorizontalLayoutGroup>();
        badgeHlg.padding = new RectOffset(12, 12, 0, 0);
        badgeHlg.childAlignment = TextAnchor.MiddleCenter;
        badgeHlg.childControlWidth = badgeHlg.childControlHeight = true;
        badgeHlg.childForceExpandWidth = badgeHlg.childForceExpandHeight = false;
        var badgeFitter = badge.GetComponent<ContentSizeFitter>();
        badgeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        badgeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        var badgeText = NewChild(badge.transform, "BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
        var badgeTmp = badgeText.GetComponent<TextMeshProUGUI>();
        badgeTmp.text = "";
        badgeTmp.fontSize = 28f;
        badgeTmp.fontStyle = FontStyles.Bold;
        badgeTmp.color = White;
        badgeTmp.alignment = TextAlignmentOptions.Center;
        badgeTmp.enableWordWrapping = false;
        badgeTmp.overflowMode = TextOverflowModes.Overflow;
        badgeTmp.raycastTarget = false;

        // Wire the ScrollToBottomFab serialized refs.
        var fabScript = root.GetComponent<ScrollToBottomFab>();
        var so = new SerializedObject(fabScript);
        SetRef(so, "button",      btn);
        SetRef(so, "canvasGroup", cg);
        SetRef(so, "badgeRoot",   badge);
        SetRef(so, "badgeText",   badgeTmp);
        so.ApplyModifiedPropertiesWithoutUndo();

        badge.SetActive(false); // hidden until count > 0
        return fabScript;
    }

    // ── Separator prefab ────────────────────────────────────────────────────────────
    private static UnreadSeparatorView BuildSeparatorPrefab()
    {
        // Root: full-width bar (Image bg), flexibleWidth so the message list's
        // VerticalLayoutGroup stretches it edge-to-edge; fixed height via LayoutElement.
        var root = new GameObject("UnreadSeparator",
            typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(UnreadSeparatorView));
        var rootRt = (RectTransform)root.transform;
        rootRt.sizeDelta = new Vector2(0f, SeparatorHeight);
        var bar = root.GetComponent<Image>();
        bar.color = SeparatorBarBg;
        bar.raycastTarget = false;
        var le = root.GetComponent<LayoutElement>();
        le.minHeight = SeparatorHeight;
        le.preferredHeight = SeparatorHeight;
        le.flexibleWidth = 1f;

        var labelGo = NewChild(root.transform, "Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRt = (RectTransform)labelGo.transform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        label.text = "0 UNREAD MESSAGES";
        label.fontSize = 32f;
        label.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        label.characterSpacing = 6f; // ~+0.6 tracking
        label.color = SeparatorLabel;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        var view = root.GetComponent<UnreadSeparatorView>();
        var so = new SerializedObject(view);
        SetRef(so, "label", label);
        so.ApplyModifiedPropertiesWithoutUndo();

        var saved = PrefabUtility.SaveAsPrefabAsset(root, SeparatorPath, out bool ok);
        Object.DestroyImmediate(root);
        if (!ok || saved == null)
        {
            Debug.LogError($"[UnreadMarkersBuilder] Failed to save prefab at {SeparatorPath}");
            return null;
        }
        return saved.GetComponent<UnreadSeparatorView>();
    }

    private static void WireMessageListView(MessageListView mlv, ScrollToBottomFab fab, UnreadSeparatorView sepPrefab)
    {
        var so = new SerializedObject(mlv);
        SetRef(so, "scrollToBottomFab",   fab);
        SetRef(so, "unreadSeparatorPrefab", sepPrefab);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── helpers ────────────────────────────────────────────────────────────────────
    private static GameObject NewChild(Transform parent, string name, params System.Type[] components)
    {
        var go = new GameObject(name, components);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void SetRef(SerializedObject so, string propertyName, Object value)
    {
        var p = so.FindProperty(propertyName);
        if (p == null)
        {
            Debug.LogWarning($"[UnreadMarkersBuilder] Property '{propertyName}' not found on {so.targetObject.GetType().Name}.");
            return;
        }
        p.objectReferenceValue = value;
        if (value == null)
            Debug.LogWarning($"[UnreadMarkersBuilder] {so.targetObject.GetType().Name}.{propertyName} set to null — assign manually.");
    }
}
#endif

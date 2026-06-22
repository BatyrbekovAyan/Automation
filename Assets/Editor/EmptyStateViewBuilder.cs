#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds Screen_Whatsapp/ChatsPanel/EmptyState (the "no bots yet" screen) in the
/// "welcoming hero" layout: a soft mint disc with an icon slot, a bold headline,
/// a supportive line, and a full-width green CTA pinned in the thumb zone.
/// Sizes are in 1080x1920 canvas reference units (dp x 3).
/// </summary>
public static class EmptyStateViewBuilder
{
    private const string ScreenName = "Screen_Whatsapp";
    private const string ChatsPanelName = "ChatsPanel";
    private const string EmptyStateName = "EmptyState";

    private static readonly Color Brand = HexColor("#25D366"); // WhatsApp light green
    private static readonly Color BrandTint = HexColor("#DFF3EA");
    private static readonly Color TitleColor = HexColor("#111111");
    private static readonly Color BodyColor = HexColor("#6A6A6A");

    [MenuItem("Tools/Bot Switcher/Build EmptyState")]
    public static void Build()
    {
        GameObject screen = FindGameObjectByNameIncludeInactive(ScreenName);
        if (screen == null)
        {
            Debug.LogError($"[EmptyStateViewBuilder] Could not find {ScreenName} (active or inactive). Open the Main scene.");
            return;
        }

        Transform chatsPanel = screen.transform.Find(ChatsPanelName);
        if (chatsPanel == null)
        {
            Debug.LogError($"[EmptyStateViewBuilder] {ScreenName} has no child named '{ChatsPanelName}'.");
            return;
        }

        Transform existing = chatsPanel.Find(EmptyStateName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // Root — full-stretch overlay over the chat list, last sibling so it covers it.
        GameObject root = new GameObject(EmptyStateName, typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(chatsPanel, false);
        // Sit above the chat list but BELOW the header (TopBar) and the bot-switcher
        // sheet, so those stay visible and tappable (you can still switch bots here).
        PlaceAboveListBelowHeader(root.transform, chatsPanel);
        StretchFull(root.GetComponent<RectTransform>());

        // Opaque background so the screen actually covers the chat list behind it
        // (the CanvasGroup still fades the whole thing in/out via alpha).
        var rootBg = root.AddComponent<Image>();
        rootBg.color = Color.white;
        rootBg.raycastTarget = true;

        // Hero block — icon + title + body, centered slightly above middle.
        GameObject hero = new GameObject("Hero",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        hero.transform.SetParent(root.transform, false);
        var heroRT = hero.GetComponent<RectTransform>();
        heroRT.anchorMin = new Vector2(0f, 0.5f);
        heroRT.anchorMax = new Vector2(1f, 0.5f);
        heroRT.pivot = new Vector2(0.5f, 0.5f);
        heroRT.sizeDelta = Vector2.zero;
        heroRT.anchoredPosition = new Vector2(0f, 150f);
        var vlg = hero.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 48f;
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;
        var fitter = hero.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Mint disc (decorative surface — rounded corners, no sprite).
        GameObject circle = new GameObject("IconCircle", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        circle.transform.SetParent(hero.transform, false);
        circle.GetComponent<RectTransform>().sizeDelta = new Vector2(252f, 252f);
        circle.GetComponent<Image>().color = BrandTint;
        SetPreferred(circle, 252f, 252f);
        AddRoundedCorners(circle, 126f); // half of 252 -> full circle

        // Inner icon — the sprite slot the designer fills (left as a tinted placeholder).
        GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        icon.transform.SetParent(circle.transform, false);
        var iconRT = icon.GetComponent<RectTransform>();
        iconRT.anchorMin = iconRT.anchorMax = iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = Vector2.zero;
        iconRT.sizeDelta = new Vector2(120f, 120f);
        var iconImage = icon.GetComponent<Image>();
        iconImage.color = Brand;          // placeholder tint; assign a bot sprite here
        iconImage.preserveAspect = true;

        // Title (H1) + Body.
        var titleText = CreateText(hero.transform, "Title", "Create your first bot", 52f, FontStyles.Bold, TitleColor, 820f, 70f);
        var bodyText = CreateText(hero.transform, "Body",
            "An AI assistant that answers your customers on WhatsApp, day and night.",
            40f, FontStyles.Normal, BodyColor, 820f, 150f);
        bodyText.enableWordWrapping = true;

        // Primary CTA — full width minus side margins, pinned in the thumb zone.
        GameObject btn = new GameObject("PrimaryButton", typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(root.transform, false);
        var btnRT = btn.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0f, 0f);
        btnRT.anchorMax = new Vector2(1f, 0f);
        btnRT.pivot = new Vector2(0.5f, 0f);
        btnRT.sizeDelta = new Vector2(-192f, 132f);   // 96 margin each side, 132 tall
        btnRT.anchoredPosition = new Vector2(0f, 210f);
        btn.GetComponent<Image>().color = Brand;
        AddRoundedCorners(btn, 66f);                   // half of 132 -> pill

        var btnLabel = CreateText(btn.transform, "Label", "Create a bot", 42f, FontStyles.Bold, Color.white);
        StretchFull(btnLabel.rectTransform);

        // EmptyStateView + serialized wiring.
        var view = root.AddComponent<EmptyStateView>();
        var so = new SerializedObject(view);
        so.FindProperty("iconImage").objectReferenceValue = iconImage;
        so.FindProperty("titleLabel").objectReferenceValue = titleText;
        so.FindProperty("bodyLabel").objectReferenceValue = bodyText;
        so.FindProperty("primaryButton").objectReferenceValue = btn.GetComponent<Button>();
        so.FindProperty("primaryButtonLabel").objectReferenceValue = btnLabel;
        so.ApplyModifiedPropertiesWithoutUndo();

        root.GetComponent<CanvasGroup>().alpha = 0f; // starts hidden; EmptyStateView.Awake also hides

        // Force a layout pass so the rounded-corner shaders see final rects, then refresh them.
        LayoutRebuilder.ForceRebuildLayoutImmediate(heroRT);
        Canvas.ForceUpdateCanvases();
        RefreshAllRounded(root);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[EmptyStateViewBuilder] Rebuilt {EmptyStateName} (welcoming hero) under {ScreenName}/{ChatsPanelName}.");
        Selection.activeGameObject = root;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text,
        float size, FontStyles style, Color color, float prefW = -1f, float prefH = -1f)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        if (prefW > 0f || prefH > 0f)
        {
            var le = go.AddComponent<LayoutElement>();
            if (prefW > 0f) le.preferredWidth = prefW;
            if (prefH > 0f) le.preferredHeight = prefH;
        }
        return t;
    }

    private static void SetPreferred(GameObject go, float w, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.preferredWidth = w;
        le.preferredHeight = h;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // Keep this overlay below the header + bot-switcher sheet (so those stay on top
    // and interactive) while still covering the chat list behind it.
    private static void PlaceAboveListBelowHeader(Transform child, Transform chatsPanel)
    {
        int insertAt = chatsPanel.childCount;
        Transform topBar = chatsPanel.Find("TopBar");
        Transform sheet = chatsPanel.Find("Sheet_BotSwitcher");
        if (topBar != null) insertAt = Mathf.Min(insertAt, topBar.GetSiblingIndex());
        if (sheet != null) insertAt = Mathf.Min(insertAt, sheet.GetSiblingIndex());
        child.SetSiblingIndex(insertAt);
    }

    private static void AddRoundedCorners(GameObject go, float radius)
    {
        var roundedType = ResolveRoundedType();
        if (roundedType == null)
        {
            Debug.LogWarning("[EmptyStateViewBuilder] ImageWithRoundedCorners not found — corners render square.");
            return;
        }
        var rounded = go.AddComponent(roundedType);
        var radiusField = roundedType.GetField("radius");
        if (radiusField != null) radiusField.SetValue(rounded, radius);
    }

    // The type lives in the package's own assembly (Nobi.UiRoundedCorners), not
    // Assembly-CSharp — so a plain Type.GetType("...,Assembly-CSharp") misses it.
    private static System.Type ResolveRoundedType()
    {
        var t = System.Type.GetType("Nobi.UiRoundedCorners.ImageWithRoundedCorners, Nobi.UiRoundedCorners");
        if (t != null) return t;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType("Nobi.UiRoundedCorners.ImageWithRoundedCorners");
            if (t != null) return t;
        }
        return null;
    }

    // After layout settles, re-run each component's Validate()+Refresh() so the SDF
    // shader picks up the final rect and our radius (OnEnable ran with the default).
    private static void RefreshAllRounded(GameObject root)
    {
        var roundedType = ResolveRoundedType();
        if (roundedType == null) return;
        foreach (var c in root.GetComponentsInChildren(roundedType, true))
        {
            roundedType.GetMethod("Validate")?.Invoke(c, null);
            roundedType.GetMethod("Refresh")?.Invoke(c, null);
        }
    }

    private static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    private static GameObject FindGameObjectByNameIncludeInactive(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == name) return all[i].gameObject;
        }
        return null;
    }
}
#endif

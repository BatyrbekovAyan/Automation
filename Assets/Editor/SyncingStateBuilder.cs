#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds Screen_Whatsapp/ChatsPanel/SyncingState (the post-creation "Setting things
/// up" screen) in the "progress + reassurance" layout: a spinner, headline, body, a
/// time-based progress bar, a live countdown, and a reassuring footnote pinned low.
/// Sibling of EmptyState; covers the chat-list area while the WhatsApp top bar and the
/// bottom tabs stay usable. Sizes are in 1080x1920 canvas reference units (dp x 3).
/// </summary>
public static class SyncingStateBuilder
{
    private const string ScreenName = "Screen_Whatsapp";
    private const string ChatsPanelName = "ChatsPanel";
    private const string SyncingStateName = "SyncingState";

    private static readonly Color Brand = HexColor("#008069");
    private static readonly Color Track = HexColor("#ECECEC");
    private static readonly Color TitleColor = HexColor("#111111");
    private static readonly Color BodyColor = HexColor("#6A6A6A");
    private static readonly Color FootnoteColor = HexColor("#9A9A9A");

    [MenuItem("Tools/Bot Switcher/Build SyncingState")]
    public static void Build()
    {
        GameObject screen = FindGameObjectByNameIncludeInactive(ScreenName);
        if (screen == null)
        {
            Debug.LogError($"[SyncingStateBuilder] Could not find {ScreenName} (active or inactive). Open the Main scene.");
            return;
        }

        Transform chatsPanel = screen.transform.Find(ChatsPanelName);
        if (chatsPanel == null)
        {
            Debug.LogError($"[SyncingStateBuilder] {ScreenName} has no child named '{ChatsPanelName}'.");
            return;
        }

        Transform existing = chatsPanel.Find(SyncingStateName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // Root — full-stretch overlay, last sibling so it covers the chat list.
        GameObject root = new GameObject(SyncingStateName, typeof(RectTransform), typeof(CanvasGroup));
        root.transform.SetParent(chatsPanel, false);
        root.transform.SetAsLastSibling();
        StretchFull(root.GetComponent<RectTransform>());

        // Hero block — spinner + title + body + progress + countdown, centered.
        GameObject hero = new GameObject("Hero",
            typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        hero.transform.SetParent(root.transform, false);
        var heroRT = hero.GetComponent<RectTransform>();
        heroRT.anchorMin = new Vector2(0f, 0.5f);
        heroRT.anchorMax = new Vector2(1f, 0.5f);
        heroRT.pivot = new Vector2(0.5f, 0.5f);
        heroRT.sizeDelta = Vector2.zero;
        heroRT.anchoredPosition = new Vector2(0f, 120f);
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

        // Spinner — sprite slot the designer fills with a ring; plain Image so the
        // rotation is visible even as a placeholder (no rounded-corner material to
        // fight an assigned sprite later).
        GameObject spinnerGO = new GameObject("Spinner", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        spinnerGO.transform.SetParent(hero.transform, false);
        spinnerGO.GetComponent<RectTransform>().sizeDelta = new Vector2(180f, 180f);
        var spinnerImg = spinnerGO.GetComponent<Image>();
        spinnerImg.color = Brand;            // placeholder tint; assign a ring sprite here
        spinnerImg.preserveAspect = true;
        SetPreferred(spinnerGO, 180f, 180f);
        var spinnerRT = spinnerGO.GetComponent<RectTransform>();

        // Title (H1) + Body.
        var titleText = CreateText(hero.transform, "Title", "Setting things up", 52f, FontStyles.Bold, TitleColor, 820f, 70f);
        var bodyText = CreateText(hero.transform, "Body",
            "We're importing your chats and messages from WhatsApp.",
            40f, FontStyles.Normal, BodyColor, 820f, 110f);
        bodyText.enableWordWrapping = true;

        // Progress track + filled bar.
        GameObject track = new GameObject("ProgressTrack", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        track.transform.SetParent(hero.transform, false);
        track.GetComponent<RectTransform>().sizeDelta = new Vector2(760f, 18f);
        track.GetComponent<Image>().color = Track;
        SetPreferred(track, 760f, 18f);
        AddRoundedCorners(track, 9f);

        GameObject fill = new GameObject("ProgressFill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(track.transform, false);
        StretchFull(fill.GetComponent<RectTransform>());
        var fillImage = fill.GetComponent<Image>();
        fillImage.color = Brand;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 0f;

        // Countdown.
        var countdownText = CreateText(hero.transform, "Countdown", "About 5 min left", 36f, FontStyles.Bold, Brand, 760f, 48f);

        // Footnote — reassurance pinned in the lower zone.
        var footnoteText = CreateText(root.transform, "Footnote",
            "You can keep using the app. Chats appear here when ready.",
            30f, FontStyles.Normal, FootnoteColor);
        footnoteText.enableWordWrapping = true;
        var footRT = footnoteText.rectTransform;
        footRT.anchorMin = new Vector2(0f, 0f);
        footRT.anchorMax = new Vector2(1f, 0f);
        footRT.pivot = new Vector2(0.5f, 0f);
        footRT.sizeDelta = new Vector2(-192f, 80f);
        footRT.anchoredPosition = new Vector2(0f, 140f);

        // SyncingView + serialized wiring.
        var view = root.AddComponent<SyncingView>();
        var so = new SerializedObject(view);
        so.FindProperty("spinner").objectReferenceValue = spinnerRT;
        so.FindProperty("titleLabel").objectReferenceValue = titleText;
        so.FindProperty("bodyLabel").objectReferenceValue = bodyText;
        so.FindProperty("progressFill").objectReferenceValue = fillImage;
        so.FindProperty("countdownLabel").objectReferenceValue = countdownText;
        so.FindProperty("footnoteLabel").objectReferenceValue = footnoteText;
        so.ApplyModifiedPropertiesWithoutUndo();

        root.GetComponent<CanvasGroup>().alpha = 0f; // starts hidden; SyncingView.Awake also hides

        // Force a layout pass so the rounded-corner shaders see final rects, then refresh them.
        LayoutRebuilder.ForceRebuildLayoutImmediate(heroRT);
        Canvas.ForceUpdateCanvases();
        RefreshAllRounded(root);

        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[SyncingStateBuilder] Built {SyncingStateName} (progress + reassurance) under {ScreenName}/{ChatsPanelName}.");
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

    private static void AddRoundedCorners(GameObject go, float radius)
    {
        var roundedType = ResolveRoundedType();
        if (roundedType == null)
        {
            Debug.LogWarning("[SyncingStateBuilder] ImageWithRoundedCorners not found — corners render square.");
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

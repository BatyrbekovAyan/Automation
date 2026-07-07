#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Navigation restructure scene surgery (Task B5). Idempotent delete-and-rebuild
/// that performs three builds and one programmatic TabData rewire:
///
///   (a) Screen_Dashboard  — an empty placeholder screen (bg + header + content
///       root), sibling of the other Screen_* under the canvas container. The
///       tab-2 "New" slot is repointed here; the Add-Bot form becomes an overlay.
///   (b) Screen_New chrome  — a back chevron in the header + a left-edge
///       swipe-back strip, plus the AddBotPanel overlay controller. Screen_New
///       is left INACTIVE (it is now an overlay, not a tab panel).
///   (c) Bots empty state   — a centered hero + title + body + CTA under the
///       BotsPage host, shown when zero bots exist.
///
/// The tab-2 rewire (tabName/screenPanel/activeLabelColor + label text) is done
/// here via SerializedObject rather than by hand in the Inspector. Runtime button
/// listeners that target singletons (CloseAddBotForm, StartNewBot) are wired in
/// Manager.Start against three serialized fields this builder stamps.
///
/// All sizes in 1080×1920 canvas reference units. Save the scene after running
/// (headless entry saves automatically). Clones idioms verbatim from
/// ProfileSubPagesBuilder.
/// </summary>
public static class NavRestructureBuilder
{
    // ── Design tokens ───────────────────────────────────────────────────────
    private const float HeaderHeight = 300f;
    private const float Gutter = 44f;
    private const float CardRadius = 40f;
    private const float ButtonHeight = 144f;
    private const float SwipeStripWidth = 150f;

    private static readonly Color DashboardBg = Hex("#F0F2F5");
    private static readonly Color Card = Color.white;
    private static readonly Color Ink = Hex("#1A1A2E");
    private static readonly Color Muted = Hex("#65676B");
    private static readonly Color Divider = Hex("#E4E6EB");
    private static readonly Color Primary = Hex("#1B7CEB");

    // Fonts by GUID (default font's weight table is empty — always assign explicitly).
    private const string RegularGuid = "e0cdfe2d6a51446bcba7d2df147e2415";
    private const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";
    private const string BoldGuid = "1cd715823fef34be4a3d3f3c5572594c";

    private static TMP_FontAsset _regular, _semibold, _bold;

    private const string ChevronDir = "Assets/Images/Chat";
    private const string HeroPath = "Assets/Images/Chat/bot_hero.png";

    private static Sprite _chevronLeft, _hero;
    private static readonly List<Component> _roundedToRefresh = new List<Component>();

    // ── Entry points ────────────────────────────────────────────────────────

    [MenuItem("Tools/Nav Restructure/Build")]
    public static void Build()
    {
        var root = BuildInternal();
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[NavRestructureBuilder] Build complete: dashboard, overlay chrome, empty state, tab rewired. SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod NavRestructureBuilder.BuildHeadless -quit
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        BuildInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[NavRestructureBuilder] Headless build + save complete: dashboard, overlay chrome, empty state, tab rewired.");
    }

    // ── Main build ──────────────────────────────────────────────────────────

    private static GameObject BuildInternal()
    {
        AssetDatabase.Refresh();
        EnsureIconImportSettings();
        LoadAssets();
        _roundedToRefresh.Clear();

        var tabManager = Object.FindFirstObjectByType<BottomTabManager>(FindObjectsInactive.Include);
        if (tabManager == null)
            throw new System.InvalidOperationException("BottomTabManager not found — is Main.unity open?");

        // Grab the current tab-2 (New) screenPanel BEFORE the rewrite — that is Screen_New.
        var tabsSo = new SerializedObject(tabManager);
        var tabsProp = tabsSo.FindProperty("tabs");
        if (tabsProp == null || tabsProp.arraySize < 4)
            throw new System.InvalidOperationException("BottomTabManager.tabs list is missing or too short.");

        var newTab = tabsProp.GetArrayElementAtIndex(2);
        var botsTab = tabsProp.GetArrayElementAtIndex(3);

        var screenNew = newTab.FindPropertyRelative("screenPanel").objectReferenceValue as GameObject;
        if (screenNew == null)
            throw new System.InvalidOperationException("tabs[2].screenPanel (Screen_New) is unassigned.");

        var screenBots = botsTab.FindPropertyRelative("screenPanel").objectReferenceValue as GameObject;
        if (screenBots == null)
            throw new System.InvalidOperationException("tabs[3].screenPanel (Screen_Bots) is unassigned.");

        // Canvas container = the shared parent of the Screen_* panels.
        Transform container = screenBots.transform.parent;
        if (container == null)
            throw new System.InvalidOperationException("Screen_Bots has no parent container.");

        // (a) Screen_Dashboard placeholder.
        var dashboard = BuildDashboard(container);

        // (b) Screen_New overlay chrome (back button + swipe strip + AddBotPanel).
        BuildOverlayChrome(screenNew, out var backButton, out var swipeBack);

        // (c) Bots empty state.
        var emptyStateCta = BuildEmptyState(out var emptyStateGo, out var botsParent);

        // Tab-2 rewire (programmatic — replaces the manual Inspector step).
        RewriteNewTabToDashboard(tabsSo, newTab, dashboard);

        // Runtime singleton wiring targets → stamp the three Manager fields.
        StampManagerFields(backButton, swipeBack, emptyStateCta);

        // Radius bake needs sized rects.
        Canvas.ForceUpdateCanvases();
        foreach (var rounded in _roundedToRefresh)
            RefreshRounded(rounded);

        return dashboard;
    }

    // ── (a) Screen_Dashboard placeholder ────────────────────────────────────

    private static GameObject BuildDashboard(Transform container)
    {
        DestroyAllByName(container, "Screen_Dashboard");

        var screen = NewChild(container.gameObject, "Screen_Dashboard", out var screenRt);
        StretchFill(screenRt);
        var bg = screen.AddComponent<Image>();
        bg.color = DashboardBg;
        bg.raycastTarget = true;

        // Header — mirrors the other Screen_*/Header (h=300, safe area baked in).
        var header = NewChild(screen, "Header", out var headerRt);
        SetAnchors(headerRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        headerRt.offsetMin = new Vector2(0f, -HeaderHeight);
        headerRt.offsetMax = Vector2.zero;
        header.AddComponent<Image>().color = Card;

        var hairline = NewChild(header, "Border", out var hairRt);
        SetAnchors(hairRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        hairRt.offsetMin = Vector2.zero;
        hairRt.offsetMax = new Vector2(0f, 2f);
        hairline.AddComponent<Image>().color = Divider;

        var titleGo = NewChild(header, "Title", out var titleRt);
        SetAnchors(titleRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        titleRt.offsetMin = new Vector2(150f, 60f);
        titleRt.offsetMax = new Vector2(-150f, 120f);
        var titleTmp = AddText(titleGo, "Сводка", 55f, _bold, Ink);
        titleTmp.alignment = TextAlignmentOptions.Center;

        // Empty content root below the header (a later phase fills this).
        var content = NewChild(screen, "Content", out var contentRt);
        StretchFill(contentRt);
        contentRt.offsetMax = new Vector2(0f, -HeaderHeight);

        screen.SetActive(false);
        return screen;
    }

    // ── (b) Screen_New overlay chrome ───────────────────────────────────────

    private static void BuildOverlayChrome(GameObject screenNew, out Button backButton, out SwipeToBackPanel swipeBack)
    {
        // Idempotent teardown of our own nodes.
        DestroyAllByName(screenNew.transform, "AddBotBackButton");
        DestroyAllByName(screenNew.transform, "AddBotSwipeBack");
        DestroyAllByName(screenNew.transform, "OverlayHeaderChrome");

        var screenNewRt = screenNew.GetComponent<RectTransform>();

        // Header host: reuse the existing "Header" child if present, else a
        // transparent top-anchored chrome layer so the back chevron still lands.
        Transform header = screenNew.transform.Find("Header");
        GameObject backHost;
        if (header != null)
        {
            backHost = header.gameObject;
        }
        else
        {
            var chrome = NewChild(screenNew, "OverlayHeaderChrome", out var chromeRt);
            SetAnchors(chromeRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            chromeRt.offsetMin = new Vector2(0f, -HeaderHeight);
            chromeRt.offsetMax = Vector2.zero;
            var chromeImg = chrome.AddComponent<Image>();
            chromeImg.color = new Color(0f, 0f, 0f, 0f);
            chromeImg.raycastTarget = false;
            backHost = chrome;
            Debug.Log("[NavRestructureBuilder] Screen_New has no 'Header' child — back button placed on new OverlayHeaderChrome layer.");
        }

        // Back chevron (mirrors ProfileSubPages back-button geometry).
        var backGo = NewChild(backHost, "AddBotBackButton", out var backRt);
        SetAnchors(backRt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
        backRt.anchoredPosition = new Vector2(70f, 90f);
        backRt.sizeDelta = new Vector2(120f, 120f);
        var backHit = backGo.AddComponent<Image>();
        backHit.color = new Color(0f, 0f, 0f, 0f);
        backHit.raycastTarget = true;
        backButton = backGo.AddComponent<Button>();
        backButton.targetGraphic = backHit;
        var backIcon = NewChild(backGo, "Icon", out var backIconRt);
        SetAnchors(backIconRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        backIconRt.sizeDelta = new Vector2(60f, 60f);
        AddIconImage(backIcon, _chevronLeft, Primary);

        // Left-edge swipe-back strip (verbatim ProfileSubPagesBuilder block:
        // ClickPassthrough.deliverPressToAllBehind so taps still reach content).
        var strip = NewChild(screenNew, "AddBotSwipeBack", out var stripRt);
        SetAnchors(stripRt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f));
        stripRt.anchoredPosition = new Vector2(SwipeStripWidth / 2f, 0f);
        stripRt.sizeDelta = new Vector2(SwipeStripWidth, 0f);
        var stripImg = strip.AddComponent<Image>();
        stripImg.color = new Color(1f, 1f, 1f, 0f);
        stripImg.raycastTarget = true;
        swipeBack = strip.AddComponent<SwipeToBackPanel>();
        var swipeSo = new SerializedObject(swipeBack);
        swipeSo.FindProperty("panelToSlide").objectReferenceValue = screenNewRt;
        var scrollRect = screenNew.GetComponentInChildren<ScrollRect>(true);
        swipeSo.FindProperty("contentScrollRect").objectReferenceValue = scrollRect;
        swipeSo.ApplyModifiedPropertiesWithoutUndo();
        var passthrough = strip.AddComponent<ClickPassthrough>();
        passthrough.allowedPanel = screenNew.transform;
        var passSo = new SerializedObject(passthrough);
        passSo.FindProperty("deliverPressToAllBehind").boolValue = true;
        passSo.ApplyModifiedPropertiesWithoutUndo();
        strip.transform.SetAsLastSibling();

        // Overlay controller (idempotent — the component drives visibility).
        if (screenNew.GetComponent<AddBotPanel>() == null)
            screenNew.AddComponent<AddBotPanel>();

        // Screen_New is an overlay now, not a tab panel — start hidden.
        if (screenNew.activeSelf) screenNew.SetActive(false);
    }

    // ── (c) Bots empty state ────────────────────────────────────────────────

    private static Button BuildEmptyState(out GameObject emptyStateGo, out Transform botsParent)
    {
        var botsPage = Object.FindFirstObjectByType<BotsPage>(FindObjectsInactive.Include);
        if (botsPage == null)
            throw new System.InvalidOperationException("BotsPage not found — is Main.unity open?");

        DestroyAllByName(botsPage.transform, "EmptyState");

        // Centered, full-bleed overlay in the page (shown behind the Add-Bot form).
        emptyStateGo = NewChild(botsPage.gameObject, "EmptyState", out var emptyRt);
        StretchFill(emptyRt);

        // Vertical stack pinned to the page centre.
        var stack = NewChild(emptyStateGo, "Stack", out var stackRt);
        SetAnchors(stackRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        stackRt.sizeDelta = new Vector2(760f, 0f);
        var vlg = stack.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(0, 0, 0, 0);
        vlg.spacing = 36f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        stack.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Hero — tint MUST be white (add-bot hero memory).
        var hero = NewChild(stack, "Hero", out _);
        SetPreferredSize(hero, 360f, 360f);
        AddIconImage(hero, _hero, Color.white);

        var titleGo = NewChild(stack, "Title", out _);
        var titleTmp = AddText(titleGo, "Создайте первого бота", 50f, _bold, Ink);
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.margin = new Vector4(0f, 12f, 0f, 0f);

        var bodyGo = NewChild(stack, "Body", out _);
        var bodyTmp = AddText(bodyGo,
            "Бот-ассистент отвечает клиентам в WhatsApp круглосуточно",
            38f, _regular, Muted);
        bodyTmp.alignment = TextAlignmentOptions.Center;
        bodyTmp.lineSpacing = 8f;

        // Primary CTA.
        var cta = NewChild(stack, "EmptyStateCta", out _);
        cta.AddComponent<LayoutElement>().preferredHeight = ButtonHeight;
        var ctaBg = cta.AddComponent<Image>();
        ctaBg.color = Primary;
        AddRounded(cta, CardRadius);
        var ctaButton = cta.AddComponent<Button>();
        ctaButton.targetGraphic = ctaBg;
        var ctaLabelGo = NewChild(cta, "Label", out var ctaLabelRt);
        StretchFill(ctaLabelRt);
        var ctaLabel = AddText(ctaLabelGo, "Создать бота", 44f, _semibold, Color.white);
        ctaLabel.alignment = TextAlignmentOptions.Center;

        emptyStateGo.SetActive(false);

        // Locate the existing BotsParent (BotsPage/ScrollContent/Viewport/BotsParent).
        botsParent = FindDeepChild(botsPage.transform, "BotsParent");
        if (botsParent == null)
            Debug.LogWarning("[NavRestructureBuilder] 'BotsParent' not found under BotsPage — BotsPage.botsParent left unstamped.");

        // Stamp BotsPage serialized fields.
        var so = new SerializedObject(botsPage);
        so.FindProperty("emptyState").objectReferenceValue = emptyStateGo;
        if (botsParent != null)
            so.FindProperty("botsParent").objectReferenceValue = botsParent;
        so.ApplyModifiedPropertiesWithoutUndo();

        return ctaButton;
    }

    // ── Tab-2 rewire (programmatic) ─────────────────────────────────────────

    private static void RewriteNewTabToDashboard(SerializedObject tabsSo, SerializedProperty newTab, GameObject dashboard)
    {
        newTab.FindPropertyRelative("tabName").stringValue = "Сводка";
        newTab.FindPropertyRelative("screenPanel").objectReferenceValue = dashboard;
        newTab.FindPropertyRelative("activeLabelColor").colorValue = Primary;

        // Update the scene TMP label text on the tab.
        var labelTmp = newTab.FindPropertyRelative("labelText").objectReferenceValue as TextMeshProUGUI;
        tabsSo.ApplyModifiedPropertiesWithoutUndo();

        if (labelTmp != null)
        {
            var labelSo = new SerializedObject(labelTmp);
            labelSo.FindProperty("m_text").stringValue = "Сводка";
            labelSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(labelTmp);
        }
        else
        {
            Debug.LogWarning("[NavRestructureBuilder] tabs[2].labelText is null — tab label text not updated.");
        }
    }

    // ── Manager field stamping (runtime singleton wiring lives in Manager.Start) ──

    private static void StampManagerFields(Button addBotBackButton, SwipeToBackPanel addBotSwipeBack, Button botsEmptyStateCta)
    {
        var manager = Object.FindFirstObjectByType<Manager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogWarning("[NavRestructureBuilder] Manager not found — overlay/empty-state fields not stamped.");
            return;
        }

        var so = new SerializedObject(manager);
        so.FindProperty("addBotBackButton").objectReferenceValue = addBotBackButton;
        so.FindProperty("addBotSwipeBack").objectReferenceValue = addBotSwipeBack;
        so.FindProperty("botsEmptyStateCta").objectReferenceValue = botsEmptyStateCta;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
    }

    // ── Asset loading / import settings ─────────────────────────────────────

    private static void EnsureIconImportSettings()
    {
        foreach (string path in new[] { $"{ChevronDir}/chevron-left.png", HeroPath })
        {
            if (!File.Exists(path)) continue;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool dirty = importer.textureType != TextureImporterType.Sprite
                         || importer.spriteImportMode != SpriteImportMode.Single
                         || !importer.alphaIsTransparency;
            if (!dirty) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }

    private static void LoadAssets()
    {
        _regular = LoadFont(RegularGuid);
        _semibold = LoadFont(SemiboldGuid);
        _bold = LoadFont(BoldGuid);

        _chevronLeft = LoadSprite($"{ChevronDir}/chevron-left.png");
        _hero = LoadSprite(HeroPath);
    }

    private static TMP_FontAsset LoadFont(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null) Debug.LogWarning($"[NavRestructureBuilder] Font missing for GUID {guid}");
        return font;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[NavRestructureBuilder] Sprite missing: {path}");
        return sprite;
    }

    // ── Low-level helpers (verbatim ProfileSubPagesBuilder idiom) ──────────

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }

    private static GameObject NewChild(GameObject parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivot;
    }

    private static void StretchFill(RectTransform rt, float uniformInset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(uniformInset, uniformInset);
        rt.offsetMax = new Vector2(-uniformInset, -uniformInset);
    }

    private static void SetPreferredSize(GameObject go, float width, float height)
    {
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.preferredHeight = height;
        le.minWidth = width;
        le.minHeight = height;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;
    }

    private static TextMeshProUGUI AddText(GameObject go, string text, float size, TMP_FontAsset font, Color color)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        if (font != null) tmp.font = font;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        // This project's TMP default is NO wrapping — without this, long
        // subtitles render one line tall and spill off-screen.
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void AddIconImage(GameObject go, Sprite sprite, Color tint)
    {
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = tint;
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    private static void AddRounded(GameObject go, float radius)
    {
        var rounded = go.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = radius;
        _roundedToRefresh.Add(rounded);
    }

    private static void RefreshRounded(Component rounded)
    {
        if (rounded == null) return;
        switch (rounded)
        {
            case ImageWithRoundedCorners simple:
                simple.Validate();
                simple.Refresh();
                break;
            case ImageWithIndependentRoundedCorners independent:
                independent.Validate();
                independent.Refresh();
                break;
        }
    }

    private static void DestroyAllByName(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t != null && t != root && t.name == name)
                Object.DestroyImmediate(t.gameObject);
        }
    }

    private static Transform FindDeepChild(Transform root, string name)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            if (t != null && t != root && t.name == name)
                return t;
        }
        return null;
    }
}
#endif

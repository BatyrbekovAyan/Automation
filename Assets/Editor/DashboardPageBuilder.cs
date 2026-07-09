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
/// Builds the Variant-B «Сводка» dashboard UI (Task C7) into the existing
/// Screen_Dashboard placeholder created by NavRestructureBuilder (Task B5), then
/// stamps every <see cref="DashboardPage"/> [SerializeField] via SerializedObject.
///
/// Structure produced:
///   Screen_Dashboard [+DashboardPage]
///     ├─ Header (pre-existing — untouched)
///     ├─ Content (pre-existing below-header region)
///     │    └─ DashScroll (ScrollRect + Viewport(RectMask2D) + ScrollContent VLG)
///     │         ├─ Viewport/ScrollContent → period control, chips, hero card,
///     │         │    status rows card, «Последние заявки» + recentRoot + rowTemplate
///     │         ├─ LoadingState (overlay, inactive)
///     │         └─ EmptyState  (overlay, inactive)
///     └─ DashListPanel (full BuildPanelShell drill-down, inactive)
///
/// Idempotent delete-and-rebuild. All sizes in 1080×1920 canvas reference units.
/// Save the scene after running (headless entry saves automatically). Clones the
/// low-level helper idioms verbatim from ProfileSubPagesBuilder / NavRestructureBuilder.
///
/// CRITICAL: the controller resolves several elements by transform.Find on EXACT
/// child names (Dot/Label/Count on legend + status rows; Avatar/Initial, Name,
/// BotTag, Summary, Pill/Label on the row template). Do not rename those children.
/// </summary>
public static class DashboardPageBuilder
{
    // ── Design tokens ───────────────────────────────────────────────────────
    private const float HeaderHeight = 300f;
    private const float Gutter = 44f;
    private const float CardGap = 32f;
    private const float CardRadius = 40f;
    private const float ButtonHeight = 144f;
    private const float SwipeStripWidth = 150f;

    private const float ContentWidth = 1080f - 2f * Gutter;   // 992 at 1080 ref
    private const float TrackHeight = 96f;
    private const float TrackInset = 12f;
    private const float FunnelHeight = 24f;
    private const float LegendRowHeight = 46f;
    private const float StatusRowHeight = 120f;
    private const float RowTemplateHeight = 190f;
    private const float DotSize = 24f;

    private static readonly Color Bg = Hex("#F0F2F5");
    private static readonly Color Card = Color.white;
    private static readonly Color Ink = Hex("#1A1A2E");
    private static readonly Color Muted = Hex("#65676B");
    private static readonly Color Divider = Hex("#E4E6EB");
    private static readonly Color Primary = Hex("#1B7CEB");
    private static readonly Color TrackBg = Hex("#E4E6EB");

    // Fonts by GUID (default font's weight table is empty — always assign explicitly).
    private const string RegularGuid = "e0cdfe2d6a51446bcba7d2df147e2415";
    private const string MediumGuid = "d091b0cad5d964a53a41de97ba932a27";
    private const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";
    private const string BoldGuid = "1cd715823fef34be4a3d3f3c5572594c";

    private static TMP_FontAsset _regular, _medium, _semibold, _bold;

    private const string ChevronLeftPath = "Assets/Images/Chat/chevron-left.png";
    private const string ChevronRightPath = "Assets/Images/Chat/chevron-right.png";
    private const string HeroPath = "Assets/Images/Chat/bot_hero.png";
    private const string AvatarSilhouettePath = "Assets/Images/Chat/Avatar.png";  // chat-list default avatar

    private static Sprite _chevronLeft, _chevronRight, _hero, _avatarSilhouette;
    private static readonly List<Component> _roundedToRefresh = new List<Component>();

    private struct PanelParts
    {
        public GameObject panel;
        public Button backButton;
        public SwipeToBackPanel swipe;
        public RectTransform content;
        public ScrollRect scroll;
        public TextMeshProUGUI title;
    }

    // ── Entry points ────────────────────────────────────────────────────────

    [MenuItem("Tools/Dashboard/Build")]
    public static void Build()
    {
        var screen = BuildInternal();
        Selection.activeGameObject = screen;
        EditorSceneManager.MarkSceneDirty(screen.scene);
        Debug.Log("[DashboardPageBuilder] Build complete — SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod DashboardPageBuilder.BuildHeadless -quit
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        var screen = BuildInternal();

        var page = screen.GetComponent<DashboardPage>();
        if (page == null)
            throw new System.InvalidOperationException("Headless build left Screen_Dashboard without a DashboardPage component.");
        var so = new SerializedObject(page);
        foreach (var key in new[] { "heroCount", "funnelBar", "listPanel", "rowTemplate", "statusRowsRoot" })
        {
            if (so.FindProperty(key).objectReferenceValue == null)
                throw new System.InvalidOperationException($"Headless build left DashboardPage.{key} unwired.");
        }

        EditorSceneManager.SaveScene(scene);
        Debug.Log("[DashboardPageBuilder] Headless build + save complete: DashScroll + DashListPanel built, 20 fields stamped.");
    }

    // ── Main build ──────────────────────────────────────────────────────────

    private static GameObject BuildInternal()
    {
        AssetDatabase.Refresh();
        EnsureIconImportSettings();
        LoadAssets();
        _roundedToRefresh.Clear();

        // Resolve the screen container from BottomTabManager (NavRestructureBuilder idiom).
        var tabManager = Object.FindFirstObjectByType<BottomTabManager>(FindObjectsInactive.Include);
        if (tabManager == null)
            throw new System.InvalidOperationException("BottomTabManager not found — is Main.unity open?");
        var tabsSo = new SerializedObject(tabManager);
        var tabsProp = tabsSo.FindProperty("tabs");
        if (tabsProp == null || tabsProp.arraySize < 4)
            throw new System.InvalidOperationException("BottomTabManager.tabs list is missing or too short.");
        var screenBots = tabsProp.GetArrayElementAtIndex(3).FindPropertyRelative("screenPanel").objectReferenceValue as GameObject;
        if (screenBots == null)
            throw new System.InvalidOperationException("tabs[3].screenPanel (Screen_Bots) is unassigned.");
        Transform container = screenBots.transform.parent;
        if (container == null)
            throw new System.InvalidOperationException("Screen_Bots has no parent container.");

        Transform screenT = container.Find("Screen_Dashboard");
        if (screenT == null)
            throw new System.InvalidOperationException("Screen_Dashboard not found under the screen container — run Tools/Nav Restructure/Build (Task B5) first.");
        var screen = screenT.gameObject;

        Transform contentT = screen.transform.Find("Content");
        if (contentT == null)
            throw new System.InvalidOperationException("Screen_Dashboard has no 'Content' child — its B5 structure differs from expectation.");

        // Idempotent teardown.
        DestroyAllByName(contentT, "DashScroll");
        DestroyAllByName(screen.transform, "DashListPanel");

        var page = screen.GetComponent<DashboardPage>();
        if (page == null) page = screen.AddComponent<DashboardPage>();
        var so = new SerializedObject(page);

        // Temporarily activate so layout resolves and rounded corners bake against
        // real rects (edit-mode SetActive does NOT invoke Awake/OnEnable).
        bool wasActive = screen.activeSelf;
        if (!wasActive) screen.SetActive(true);

        // (1) Scroll column inside the existing below-header Content.
        var scrollGo = BuildScrollColumn(contentT.gameObject, out var scrollContent);

        // (2) Sections into the scroll content, in visual order.
        BuildPeriodControl(scrollContent.gameObject, out var today, out var week, out var month, out var highlight);
        BuildChipsRow(scrollContent.gameObject, out var chipsRow, out var chipHost);
        BuildHeroCard(scrollContent.gameObject, out var heroCount, out var heroDelta, out var heroSubtitle,
            out var funnelBar, out var legendRoot);
        var statusRoot = BuildStatusCard(scrollContent.gameObject);
        BuildSectionCaption(scrollContent.gameObject, "Последние заявки");
        BuildRecent(scrollContent.gameObject, out var recentRoot, out var rowTemplate);

        // (3) Overlay states parented to DashScroll so teardown of DashScroll clears them.
        var loadingState = BuildLoadingState(scrollGo);
        var emptyState = BuildEmptyState(scrollGo);

        // (4) Drill-down list panel (full shell) as a child of the screen.
        var parts = BuildPanelShell(screen, "DashListPanel", "Заявки");

        // (5) Stamp every serialized field once.
        StampController(so, today, week, month, highlight, chipsRow, chipHost,
            heroCount, heroDelta, heroSubtitle, funnelBar, legendRoot, statusRoot,
            recentRoot, rowTemplate, loadingState, emptyState, parts);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(page);

        // Radius bake needs sized rects.
        Canvas.ForceUpdateCanvases();
        foreach (var rounded in _roundedToRefresh)
            RefreshRounded(rounded);

        if (!wasActive) screen.SetActive(false);
        return screen;
    }

    // ── Scroll column (BuildPanelShell scroll block, no header/back) ─────────

    private static GameObject BuildScrollColumn(GameObject parent, out RectTransform contentRt)
    {
        var scrollGo = NewChild(parent, "DashScroll", out var scrollRt);
        StretchFill(scrollRt);
        var scrollHit = scrollGo.AddComponent<Image>();
        scrollHit.color = new Color(0f, 0f, 0f, 0f);
        scrollHit.raycastTarget = true;
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 40f;

        var viewport = NewChild(scrollGo, "Viewport", out var viewportRt);
        StretchFill(viewportRt);
        var viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = new Color(1f, 1f, 1f, 0.01f);
        viewportImg.raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        var content = NewChild(viewport, "ScrollContent", out contentRt);
        SetAnchors(contentRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)Gutter, (int)Gutter, 50, 120);
        vlg.spacing = CardGap;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRt;
        scroll.content = contentRt;
        return scrollGo;
    }

    // ── Period segmented control ────────────────────────────────────────────

    private static void BuildPeriodControl(GameObject parent, out Button today, out Button week,
        out Button month, out RectTransform highlight)
    {
        var track = NewChild(parent, "PeriodTrack", out _);
        track.AddComponent<LayoutElement>().preferredHeight = TrackHeight;
        var trackBg = track.AddComponent<Image>();
        trackBg.color = TrackBg;
        trackBg.raycastTarget = true;
        AddRounded(track, TrackHeight / 2f);

        float innerWidth = ContentWidth - 2f * TrackInset;
        float segWidth = innerWidth / 3f;
        float segHeight = TrackHeight - 2f * TrackInset;

        // Highlight FIRST so it renders behind the button labels.
        var hl = NewChild(track, "Highlight", out highlight);
        SetAnchors(highlight, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        highlight.sizeDelta = new Vector2(segWidth, segHeight);
        highlight.anchoredPosition = new Vector2(TrackInset, 0f);
        var hlImg = hl.AddComponent<Image>();
        hlImg.color = Color.white;
        hlImg.raycastTarget = false;
        AddRounded(hl, segHeight / 2f);

        today = MakeSegButton(track, "TodayButton", "Сегодня", TrackInset + 0f * segWidth, segWidth, segHeight);
        week = MakeSegButton(track, "WeekButton", "7 дней", TrackInset + 1f * segWidth, segWidth, segHeight);
        month = MakeSegButton(track, "MonthButton", "30 дней", TrackInset + 2f * segWidth, segWidth, segHeight);
    }

    private static Button MakeSegButton(GameObject track, string name, string label, float x, float width, float height)
    {
        var go = NewChild(track, name, out var rt);
        SetAnchors(rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(x, 0f);
        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        var button = go.AddComponent<Button>();
        button.targetGraphic = hit;
        var labelGo = NewChild(go, "Label", out var labelRt);
        StretchFill(labelRt);
        var tmp = AddText(labelGo, label, 38f, _semibold, Ink);
        tmp.alignment = TextAlignmentOptions.Center;
        return button;
    }

    // ── Bot chips row ───────────────────────────────────────────────────────

    private static void BuildChipsRow(GameObject parent, out Transform chipsRow, out GameObject chipHost)
    {
        var row = NewChild(parent, "ChipsRow", out _);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        chipHost = MakeChip(row, "Все боты");
        chipHost.SetActive(false);   // inactive template; controller clones it
        chipsRow = row.transform;
    }

    private static GameObject MakeChip(GameObject parent, string text)
    {
        var chip = NewChild(parent, "Chip", out _);
        var bg = chip.AddComponent<Image>();
        bg.color = Color.white;
        bg.raycastTarget = true;
        AddRounded(chip, 44f);
        chip.AddComponent<Button>().targetGraphic = bg;
        var hlg = chip.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(30, 30, 14, 14);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        var labelGo = NewChild(chip, "Label", out _);
        var tmp = AddText(labelGo, text, 30f, _medium, Ink);
        tmp.alignment = TextAlignmentOptions.Center;
        return chip;
    }

    // ── Hero card ───────────────────────────────────────────────────────────

    private static void BuildHeroCard(GameObject parent, out TextMeshProUGUI heroCount,
        out TextMeshProUGUI heroDelta, out TextMeshProUGUI heroSubtitle,
        out RectTransform funnelBar, out Transform legendRoot)
    {
        var card = MakeCard(parent, "HeroCard", 44, 20f);

        var caption = AddText(NewChild(card, "Caption", out _), "Заявки собраны".ToUpperInvariant(), 30f, _semibold, Muted);
        caption.characterSpacing = 6f;

        // Number row: big count + delta pill.
        var numberRow = NewChild(card, "NumberRow", out _);
        var nHlg = numberRow.AddComponent<HorizontalLayoutGroup>();
        nHlg.spacing = 24f;
        nHlg.childAlignment = TextAnchor.MiddleLeft;
        nHlg.childForceExpandWidth = false;
        nHlg.childForceExpandHeight = false;
        nHlg.childControlWidth = true;
        nHlg.childControlHeight = true;

        heroCount = AddText(NewChild(numberRow, "HeroCount", out _), "0", 72f, _bold, Ink);

        var deltaPill = NewChild(numberRow, "DeltaPill", out _);
        var deltaBg = deltaPill.AddComponent<Image>();
        deltaBg.color = Hex("#F2F2F7");   // neutral — SetDelta signals the sign via text color (green +, orange −), so the pill must not be green
        deltaBg.raycastTarget = false;
        AddRounded(deltaPill, 24f);
        var dHlg = deltaPill.AddComponent<HorizontalLayoutGroup>();
        dHlg.padding = new RectOffset(20, 20, 8, 8);
        dHlg.childAlignment = TextAnchor.MiddleCenter;
        dHlg.childForceExpandWidth = false;
        dHlg.childForceExpandHeight = false;
        dHlg.childControlWidth = true;
        dHlg.childControlHeight = true;
        heroDelta = AddText(NewChild(deltaPill, "Label", out _), "—", 30f, _semibold,
            DashboardStatusInfo.FgColor(OutcomeStatus.OrderCollected));
        heroDelta.alignment = TextAlignmentOptions.Center;

        heroSubtitle = AddText(NewChild(card, "HeroSubtitle", out _), "0 диалогов", 36f, _regular, Muted);

        // Funnel bar: exactly 5 Image segments, each with a LayoutElement.
        var funnel = NewChild(card, "FunnelBar", out funnelBar);
        funnel.AddComponent<LayoutElement>().preferredHeight = FunnelHeight;
        var fHlg = funnel.AddComponent<HorizontalLayoutGroup>();
        fHlg.spacing = 6f;
        fHlg.childAlignment = TextAnchor.MiddleLeft;
        fHlg.childForceExpandWidth = false;
        fHlg.childForceExpandHeight = true;
        fHlg.childControlWidth = true;
        fHlg.childControlHeight = true;
        for (int i = 0; i < DashboardStatusInfo.Ordered.Length; i++)
        {
            var seg = NewChild(funnel, $"Seg{i}", out _);
            var le = seg.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;   // controller re-weights by count
            le.minWidth = 0f;
            var img = seg.AddComponent<Image>();
            img.color = DashboardStatusInfo.FgColor(DashboardStatusInfo.Ordered[i]);
            img.raycastTarget = false;
            AddRounded(seg, FunnelHeight / 2f);
        }

        // Legend: 5 rows in Ordered order — children Dot/Label/Count.
        var legend = NewChild(card, "LegendRoot", out _);
        legendRoot = legend.transform;
        var lVlg = legend.AddComponent<VerticalLayoutGroup>();
        lVlg.spacing = 10f;
        lVlg.childAlignment = TextAnchor.UpperLeft;
        lVlg.childForceExpandWidth = true;
        lVlg.childForceExpandHeight = false;
        lVlg.childControlWidth = true;
        lVlg.childControlHeight = true;
        foreach (var status in DashboardStatusInfo.Ordered)
            BuildLegendRow(legend, status);
    }

    private static void BuildLegendRow(GameObject parent, OutcomeStatus status)
    {
        var row = NewChild(parent, "LegendRow", out _);
        row.AddComponent<LayoutElement>().preferredHeight = LegendRowHeight;
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        MakeDot(row, status);

        var labelGo = NewChild(row, "Label", out _);
        AddText(labelGo, DashboardStatusInfo.Label(status), 32f, _regular, Ink);
        labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var countGo = NewChild(row, "Count", out _);
        var count = AddText(countGo, "0", 32f, _semibold, Ink);
        count.alignment = TextAlignmentOptions.MidlineRight;
    }

    // ── Status rows card (statusRowsRoot: exactly 5 tappable rows) ──────────

    private static Transform BuildStatusCard(GameObject parent)
    {
        var card = MakeCard(parent, "StatusCard", 12, 4f);
        foreach (var status in DashboardStatusInfo.Ordered)
            BuildStatusRow(card, status);
        return card.transform;
    }

    private static void BuildStatusRow(GameObject card, OutcomeStatus status)
    {
        var row = NewChild(card, "StatusRow", out _);
        row.AddComponent<LayoutElement>().preferredHeight = StatusRowHeight;
        var hit = row.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        var button = row.AddComponent<Button>();
        button.targetGraphic = hit;
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(32, 32, 0, 0);
        hlg.spacing = 28f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;

        MakeDot(row, status);

        var labelGo = NewChild(row, "Label", out _);
        AddText(labelGo, DashboardStatusInfo.Label(status), 42f, _medium, Ink);
        labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var countGo = NewChild(row, "Count", out _);
        var count = AddText(countGo, "0", 42f, _semibold, Ink);
        count.alignment = TextAlignmentOptions.MidlineRight;

        var chevGo = NewChild(row, "Chev", out _);
        SetPreferredSize(chevGo, 32f, 32f);
        AddIconImage(chevGo, _chevronRight, Muted);
    }

    private static void MakeDot(GameObject parent, OutcomeStatus status)
    {
        var dot = NewChild(parent, "Dot", out _);
        SetPreferredSize(dot, DotSize, DotSize);
        var img = dot.AddComponent<Image>();
        img.color = DashboardStatusInfo.FgColor(status);
        img.raycastTarget = false;
        AddRounded(dot, DotSize / 2f);
    }

    // ── Section caption + recent list ───────────────────────────────────────

    private static void BuildSectionCaption(GameObject parent, string text)
    {
        var go = NewChild(parent, "SectionCaption", out _);
        var tmp = AddText(go, text.ToUpperInvariant(), 30f, _semibold, Muted);
        tmp.characterSpacing = 6f;
        tmp.margin = new Vector4(12f, 24f, 12f, 0f);
    }

    private static void BuildRecent(GameObject parent, out Transform recentRoot, out GameObject rowTemplate)
    {
        var recent = NewChild(parent, "RecentRoot", out _);
        recentRoot = recent.transform;
        var vlg = recent.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 16f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        rowTemplate = BuildRowTemplate(recent);
        rowTemplate.SetActive(false);   // controller Instantiates + activates per row
    }

    // Flat row: Name/BotTag/Summary/Pill/Avatar must be DIRECT children — the
    // controller resolves them via transform.Find (single-level). Absolute anchors.
    private static GameObject BuildRowTemplate(GameObject parent)
    {
        var row = NewChild(parent, "RowTemplate", out _);
        row.AddComponent<LayoutElement>().preferredHeight = RowTemplateHeight;
        var bg = row.AddComponent<Image>();
        bg.color = Card;
        bg.raycastTarget = true;
        AddRounded(row, CardRadius);
        row.AddComponent<Button>().targetGraphic = bg;

        // Avatar (circle) + centered initial.
        var avatar = NewChild(row, "Avatar", out var avatarRt);
        SetAnchors(avatarRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        avatarRt.sizeDelta = new Vector2(140f, 140f);
        avatarRt.anchoredPosition = new Vector2(28f, 0f);
        var avatarImg = avatar.AddComponent<Image>();
        avatarImg.color = Hex("#D6E4FB");
        avatarImg.raycastTarget = false;
        AddRounded(avatar, 70f);
        // Default-avatar silhouette (same sprite as the chat list) centered inside the
        // circle; the controller tints it + toggles it when no real photo is loaded.
        var defGo = NewChild(avatar, "DefaultImage", out var defRt);
        SetAnchors(defRt, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
        defRt.offsetMin = new Vector2(44f, 44f);
        defRt.offsetMax = new Vector2(-44f, -44f);
        AddIconImage(defGo, _avatarSilhouette, Hex("#1FA2FF"));

        const float textLeft = 196f;    // 28 + 140 + 28
        const float textRight = 300f;   // reserve the pill column

        var name = MakeStretchText(row, "Name", "Имя", 44f, _semibold, Ink, textLeft, textRight, -84f, -28f);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;

        MakeStretchText(row, "BotTag", "Бот", 30f, _regular, Muted, textLeft, textRight, -124f, -86f);

        var summary = MakeStretchText(row, "Summary", "Сводка диалога", 36f, _regular, Muted, textLeft, textRight, -170f, -126f);
        summary.textWrappingMode = TextWrappingModes.NoWrap;
        summary.overflowMode = TextOverflowModes.Ellipsis;

        // Time — top-right, muted; the controller fills it (local-time-wins). Pinned to
        // the top band (row-y ≈ [134,174]) so it clears the vertically-centered pill
        // below it (pill spans ≈ [65,125] in the same right column) — no overlap.
        var timeGo = NewChild(row, "Time", out var timeRt);
        SetAnchors(timeRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
        timeRt.sizeDelta = new Vector2(240f, 40f);
        timeRt.anchoredPosition = new Vector2(-28f, -16f);
        var timeTmp = AddText(timeGo, "", 30f, _regular, Muted);
        timeTmp.alignment = TextAlignmentOptions.TopRight;

        // Pill (status) — anchored right; hugs its label via ContentSizeFitter.
        var pill = NewChild(row, "Pill", out var pillRt);
        SetAnchors(pillRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
        pillRt.anchoredPosition = new Vector2(-28f, 0f);
        var pillBg = pill.AddComponent<Image>();
        pillBg.color = DashboardStatusInfo.BgColor(OutcomeStatus.OrderCollected);
        pillBg.raycastTarget = false;
        AddRounded(pill, 26f);
        var pHlg = pill.AddComponent<HorizontalLayoutGroup>();
        pHlg.padding = new RectOffset(24, 24, 10, 10);
        pHlg.childAlignment = TextAnchor.MiddleCenter;
        pHlg.childForceExpandWidth = false;
        pHlg.childForceExpandHeight = false;
        pHlg.childControlWidth = true;
        pHlg.childControlHeight = true;
        var pCsf = pill.AddComponent<ContentSizeFitter>();
        pCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        pCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var pillLabel = AddText(NewChild(pill, "Label", out _), "Заявка", 30f, _semibold,
            DashboardStatusInfo.FgColor(OutcomeStatus.OrderCollected));
        pillLabel.alignment = TextAlignmentOptions.Center;

        return row;
    }

    private static TextMeshProUGUI MakeStretchText(GameObject row, string name, string text, float size,
        TMP_FontAsset font, Color color, float left, float right, float bottomOffset, float topOffset)
    {
        var go = NewChild(row, name, out var rt);
        SetAnchors(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        rt.offsetMin = new Vector2(left, bottomOffset);
        rt.offsetMax = new Vector2(-right, topOffset);
        var tmp = AddText(go, text, size, font, color);
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        return tmp;
    }

    // ── Overlay states (parented to DashScroll → cleared with it) ───────────

    private static GameObject BuildLoadingState(GameObject dashScroll)
    {
        var loading = NewChild(dashScroll, "LoadingState", out var rt);
        StretchFill(rt);
        var bg = loading.AddComponent<Image>();
        bg.color = Bg;
        bg.raycastTarget = true;
        var textGo = NewChild(loading, "Text", out var textRt);
        SetAnchors(textRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        textRt.sizeDelta = new Vector2(600f, 80f);
        var tmp = AddText(textGo, "Загрузка…", 40f, _regular, Muted);
        tmp.alignment = TextAlignmentOptions.Center;
        loading.SetActive(false);
        return loading;
    }

    private static GameObject BuildEmptyState(GameObject dashScroll)
    {
        var empty = NewChild(dashScroll, "EmptyState", out var rt);
        StretchFill(rt);
        var bg = empty.AddComponent<Image>();
        bg.color = Bg;
        bg.raycastTarget = true;

        var stack = NewChild(empty, "Stack", out var stackRt);
        SetAnchors(stackRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        stackRt.sizeDelta = new Vector2(760f, 0f);
        var vlg = stack.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 28f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        stack.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var hero = NewChild(stack, "Hero", out _);
        SetPreferredSize(hero, 300f, 300f);
        AddIconImage(hero, _hero, Color.white);   // hero tint MUST be white

        var title = AddText(NewChild(stack, "Title", out _), "Бот пока не вёл диалогов", 44f, _semibold, Ink);
        title.alignment = TextAlignmentOptions.Center;

        empty.SetActive(false);
        return empty;
    }

    // ── Drill-down panel shell (verbatim ProfileSubPagesBuilder block) ──────

    private static PanelParts BuildPanelShell(GameObject root, string name, string title)
    {
        var panel = NewChild(root, name, out var panelRt);
        StretchFill(panelRt);
        var bg = panel.AddComponent<Image>();
        bg.color = Bg;
        bg.raycastTarget = true;

        var header = NewChild(panel, "Header", out var headerRt);
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
        var titleTmp = AddText(titleGo, title, 48f, _semibold, Ink);
        titleTmp.alignment = TextAlignmentOptions.Center;

        var backGo = NewChild(header, "BackButton", out var backRt);
        SetAnchors(backRt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
        backRt.anchoredPosition = new Vector2(70f, 90f);
        backRt.sizeDelta = new Vector2(120f, 120f);
        var backHit = backGo.AddComponent<Image>();
        backHit.color = new Color(0f, 0f, 0f, 0f);
        backHit.raycastTarget = true;
        var backButton = backGo.AddComponent<Button>();
        backButton.targetGraphic = backHit;
        var backIcon = NewChild(backGo, "Icon", out var backIconRt);
        SetAnchors(backIconRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        backIconRt.sizeDelta = new Vector2(60f, 60f);
        AddIconImage(backIcon, _chevronLeft, Primary);

        var scrollGo = NewChild(panel, "ScrollView", out var scrollRt);
        StretchFill(scrollRt);
        scrollRt.offsetMax = new Vector2(0f, -HeaderHeight);
        var scrollHit = scrollGo.AddComponent<Image>();
        scrollHit.color = new Color(0f, 0f, 0f, 0f);
        scrollHit.raycastTarget = true;
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 40f;

        var viewport = NewChild(scrollGo, "Viewport", out var viewportRt);
        StretchFill(viewportRt);
        var viewportImg = viewport.AddComponent<Image>();
        viewportImg.color = new Color(1f, 1f, 1f, 0.01f);
        viewportImg.raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        var content = NewChild(viewport, "Content", out var contentRt);
        SetAnchors(contentRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)Gutter, (int)Gutter, 50, 96);
        vlg.spacing = 16f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRt;
        scroll.content = contentRt;

        var strip = NewChild(panel, "SwipeBack", out var stripRt);
        SetAnchors(stripRt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f));
        stripRt.anchoredPosition = new Vector2(SwipeStripWidth / 2f, 0f);
        stripRt.sizeDelta = new Vector2(SwipeStripWidth, 0f);
        var stripImg = strip.AddComponent<Image>();
        stripImg.color = new Color(1f, 1f, 1f, 0f);
        stripImg.raycastTarget = true;
        var swipe = strip.AddComponent<SwipeToBackPanel>();
        var swipeSo = new SerializedObject(swipe);
        swipeSo.FindProperty("panelToSlide").objectReferenceValue = panelRt;
        swipeSo.FindProperty("contentScrollRect").objectReferenceValue = scroll;
        swipeSo.ApplyModifiedPropertiesWithoutUndo();
        var passthrough = strip.AddComponent<ClickPassthrough>();
        passthrough.allowedPanel = panel.transform;
        var passSo = new SerializedObject(passthrough);
        passSo.FindProperty("deliverPressToAllBehind").boolValue = true;
        passSo.ApplyModifiedPropertiesWithoutUndo();
        strip.transform.SetAsLastSibling();

        panel.SetActive(false);

        return new PanelParts
        {
            panel = panel, backButton = backButton, swipe = swipe,
            content = contentRt, scroll = scroll, title = titleTmp,
        };
    }

    // ── Card factory ────────────────────────────────────────────────────────

    private static GameObject MakeCard(GameObject parent, string name, int padding, float spacing)
    {
        var card = NewChild(parent, name, out _);
        card.AddComponent<Image>().color = Card;
        AddRounded(card, CardRadius);
        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(padding, padding, padding, padding);
        vlg.spacing = spacing;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        return card;
    }

    // ── Controller stamping ─────────────────────────────────────────────────

    private static void StampController(SerializedObject so,
        Button today, Button week, Button month, RectTransform highlight,
        Transform chipsRow, GameObject chipHost,
        TextMeshProUGUI heroCount, TextMeshProUGUI heroDelta, TextMeshProUGUI heroSubtitle,
        RectTransform funnelBar, Transform legendRoot, Transform statusRowsRoot,
        Transform recentRoot, GameObject rowTemplate, GameObject loadingState, GameObject emptyState,
        PanelParts parts)
    {
        so.FindProperty("todayButton").objectReferenceValue = today;
        so.FindProperty("weekButton").objectReferenceValue = week;
        so.FindProperty("monthButton").objectReferenceValue = month;
        so.FindProperty("periodHighlight").objectReferenceValue = highlight;
        so.FindProperty("chipsRow").objectReferenceValue = chipsRow;
        so.FindProperty("chipPrefabHost").objectReferenceValue = chipHost;
        so.FindProperty("heroCount").objectReferenceValue = heroCount;
        so.FindProperty("heroDelta").objectReferenceValue = heroDelta;
        so.FindProperty("heroSubtitle").objectReferenceValue = heroSubtitle;
        so.FindProperty("funnelBar").objectReferenceValue = funnelBar;
        so.FindProperty("legendRoot").objectReferenceValue = legendRoot;
        so.FindProperty("statusRowsRoot").objectReferenceValue = statusRowsRoot;
        so.FindProperty("recentRoot").objectReferenceValue = recentRoot;
        so.FindProperty("rowTemplate").objectReferenceValue = rowTemplate;
        so.FindProperty("loadingState").objectReferenceValue = loadingState;
        so.FindProperty("emptyState").objectReferenceValue = emptyState;
        so.FindProperty("listPanel").objectReferenceValue = parts.panel.GetComponent<RectTransform>();
        so.FindProperty("listBackButton").objectReferenceValue = parts.backButton;
        so.FindProperty("listTitle").objectReferenceValue = parts.title;
        so.FindProperty("listRoot").objectReferenceValue = parts.content;
    }

    // ── Asset loading / import settings ─────────────────────────────────────

    private static void EnsureIconImportSettings()
    {
        foreach (string path in new[] { ChevronLeftPath, ChevronRightPath, HeroPath })
        {
            if (!File.Exists(path)) continue;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool dirty = importer.textureType != TextureImporterType.Sprite
                         || importer.spriteImportMode != SpriteImportMode.Single
                         || importer.mipmapEnabled
                         || importer.filterMode != FilterMode.Bilinear
                         || importer.wrapMode != TextureWrapMode.Clamp
                         || !importer.alphaIsTransparency;
            if (!dirty) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }

    private static void LoadAssets()
    {
        _regular = LoadFont(RegularGuid);
        _medium = LoadFont(MediumGuid);
        _semibold = LoadFont(SemiboldGuid);
        _bold = LoadFont(BoldGuid);

        _chevronLeft = LoadSprite(ChevronLeftPath);
        _chevronRight = LoadSprite(ChevronRightPath);
        _hero = LoadSprite(HeroPath);
        _avatarSilhouette = LoadSprite(AvatarSilhouettePath);
    }

    private static TMP_FontAsset LoadFont(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null) Debug.LogWarning($"[DashboardPageBuilder] Font missing for GUID {guid}");
        return font;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[DashboardPageBuilder] Sprite missing: {path}");
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
        // This project's TMP default is NO wrapping — without this, long text
        // renders one line tall and spills off-card.
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
}
#endif

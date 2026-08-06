using System.Collections.Generic;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [MenuItem] builder that constructs the Reply Suggestions Panel into
/// Screen_Whatsapp/MessagesPanel of Main.unity. Implements the LOCKED sketch-002 winner "P"
/// (see .claude/skills/sketch-findings-automation/references/suggestions-panel.md):
/// a fixed-height Surface sheet (grabber + «✦ ПРЕДЛОЖЕНИЯ» header + quiet refresh icon) whose
/// full-text bordered cards scroll INSIDE a fixed viewport (cut card + bottom fade + thin bar
/// as the affordance); intent titles sit ON each card's top border (legend), and the
/// recommended card is tint-only (PositiveBg/PositiveInk + ✦). All static chrome binds theme
/// tokens via ThemedColor; per-card dynamic colors are resolved from Theme in
/// SuggestionCard.Setup. Build-time only; no networking.
///
/// The SemiAutoToggle is NOT part of this redesign: an existing toggle in the scene is left
/// untouched (scene is source of truth); it is built only if absent.
/// </summary>
public static class SuggestionsPanelBuilder
{
    private const string PanelName  = "SuggestionsPanel";
    private const string ToggleName = "SemiAutoToggle";
    private const string ToggleA11y = "Полуавтоматический режим";
    private const string RefreshA11y = "Обновить";

    // --- Reference-unit sizes (1080×1920, sketch CSS px × 3) ----------------
    private const float PanelHeight = 852f;        // 114 chrome + 738 card viewport (fixed footprint)
    private const float ChromeHeight = 114f;       // grabber zone + header row
    private const float SheetTopRadius = 48f;
    private const float GrabberW = 108f, GrabberH = 12f;
    private const float HeaderTop = 30f, HeaderH = 84f;
    private const float HeaderSparkSize = 33f, HeaderTitleSize = 28f;
    private const float RefreshHit = 120f, RefreshIconSize = 44f;
    private const string RefreshIconGuid = "aabd39746767444e984449139c957125";   // "relaod 1.png" — owner-assigned refresh sprite
    private const float ContentSidePad = 24f, ContentTopPad = 27f, ContentBottomPad = 27f, CardGap = 15f;
    private const float CardRadius = 42f, CardBorder = 3f;
    private const float CardPadSide = 33f, CardPadTop = 24f, CardPadBottom = 27f;
    private const float ReplySize = 38f;
    private const float LegendInsetX = 33f, LegendFont = 26f, LegendPadSide = 15f, LegendSparkSize = 27f;
    private const float FadeHeight = 72f;
    private const float ScrollbarW = 9f;
    private const float SkeletonHeight = 150f;
    private const float StateSize = 39f, RetryFont = 36f, RetryRadius = 36f;

    // Reply-mode switch (built only if absent — legacy geometry, untouched by the redesign).
    private static readonly Color SwitchTrackAuto = Hex("#2FB344");
    private static readonly Color SwitchInkAuto   = Hex("#206A2C");
    private static readonly Color SwitchFaintAuto = Hex("#C3EFCB");
    private const float SwitchW = 220f, SwitchH = 60f, SwitchThumbW = 100f, SwitchThumbH = 48f;
    private const float SwitchSlideX = 54f, SwitchFont = 24f;

    // Generated sprite assets (created once, then reused by guid-stable path).
    private const string SpriteFolder = "Assets/Sprites/Suggestions";
    private const string SparklePath = SpriteFolder + "/suggest_sparkle.png";
    private const string FadePath    = SpriteFolder + "/suggest_fade.png";

    [MenuItem("Tools/UI/Build Suggestions Panel")]
    public static void Build()
    {
        GameObject host = ResolveHost();
        if (host == null)
        {
            Debug.LogError("SuggestionsPanelBuilder: could not find 'Screen_Whatsapp/MessagesPanel'. " +
                           "Select the MessagesPanel GameObject in the Hierarchy, then re-run.");
            return;
        }

        Transform topBar = FindChildRecursive(host.transform, "TopBar");
        if (topBar == null)
        {
            Debug.LogError("SuggestionsPanelBuilder: MessagesPanel has no 'TopBar' child to host the toggle.");
            return;
        }

        Sprite sparkle = EnsureSparkleSprite();
        Sprite fade = EnsureFadeSprite();

        // Idempotent re-run for the PANEL only (delete-and-rebuild construction tool, no Undo
        // grouping). The toggle is deliberately preserved when present — it carries hand-tuning
        // and is not part of the sketch-002 redesign.
        Transform priorPanel = FindChildRecursive(host.transform, PanelName);
        if (priorPanel != null) Object.DestroyImmediate(priorPanel.gameObject);

        Transform quickReply = FindChildRecursive(host.transform, "QuickReplyPanel");
        Transform panelParent = quickReply != null ? quickReply.parent : host.transform;

        BuildPanel(panelParent, quickReply, sparkle, fade);

        if (FindChildRecursive(host.transform, ToggleName) == null) BuildToggle(topBar);

        EditorUtility.SetDirty(host);
        EditorSceneManager.MarkSceneDirty(host.scene);
        Debug.Log("SuggestionsPanelBuilder: built SuggestionsPanel (sketch-002 winner P).");
    }

    // === Panel ==============================================================

    private static void BuildPanel(Transform parent, Transform quickReplySibling, Sprite sparkle, Sprite fade)
    {
        // Sheet: Surface, top-rounded, slide root + fade group. Bottom-anchored, FIXED footprint.
        GameObject panelGo = ImageGo(PanelName, parent, Color.white);
        Themed(panelGo, ThemeRole.Surface);
        var rt = (RectTransform)panelGo.transform;
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(0, PanelHeight);
        rt.anchoredPosition = new Vector2(0, 204f);     // above the composer (controller re-seats via SetComposerHeight)
        AddRoundedTop(panelGo, SheetTopRadius);
        var canvasGroup = panelGo.AddComponent<CanvasGroup>();

        // Same render-order rule as before: just above the SwipeBack strip so the viewport's own
        // ScrollRect wins vertical drags over SwipeToBack's forwarding.
        Transform swipeStrip = parent.Find("SwipeBack");
        if (swipeStrip != null)
            panelGo.transform.SetSiblingIndex(swipeStrip.GetSiblingIndex() + 1);
        else if (quickReplySibling != null)
            panelGo.transform.SetSiblingIndex(quickReplySibling.GetSiblingIndex() + 1);

        BuildGrabber(panelGo.transform);
        Button refreshButton = null;
        BuildHeader(panelGo.transform, sparkle, ref refreshButton);

        // Fixed card viewport — the ONLY thing that scrolls; the sheet never changes height (D-12).
        RectTransform viewportRt;
        Transform cardsContainer = BuildCardViewport(panelGo.transform, out viewportRt);

        var skeletons = new List<GameObject>();
        for (int i = 0; i < 4; i++) skeletons.Add(BuildSkeleton(cardsContainer, i));
        SuggestionCard cardTemplate = BuildCard(cardsContainer, sparkle);

        BuildFadeOverlay(panelGo.transform, fade);

        GameObject empty = BuildEmptyState(panelGo.transform, viewportRt);
        Button errorRetry;
        GameObject error = BuildErrorState(panelGo.transform, viewportRt, out errorRetry);

        var panel = panelGo.AddComponent<SuggestionsPanel>();
        var so = new SerializedObject(panel);
        so.FindProperty("cardsContainer").objectReferenceValue = cardsContainer;
        so.FindProperty("cardPrefab").objectReferenceValue = cardTemplate;
        var skProp = so.FindProperty("skeletonCards");
        skProp.arraySize = skeletons.Count;
        for (int i = 0; i < skeletons.Count; i++)
            skProp.GetArrayElementAtIndex(i).objectReferenceValue = skeletons[i];
        so.FindProperty("emptyState").objectReferenceValue = empty;
        so.FindProperty("errorState").objectReferenceValue = error;
        so.FindProperty("refreshButton").objectReferenceValue = refreshButton;
        so.FindProperty("errorRetryButton").objectReferenceValue = errorRetry;
        so.FindProperty("rt").objectReferenceValue = rt;
        so.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildGrabber(Transform panel)
    {
        GameObject go = ImageGo("Grabber", panel, Color.white);
        Themed(go, ThemeRole.Border);
        go.GetComponent<Image>().raycastTarget = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(GrabberW, GrabberH);
        rt.anchoredPosition = new Vector2(0f, -12f);
        AddRounded(go, GrabberH / 2f);
    }

    private static void BuildHeader(Transform panel, Sprite sparkle, ref Button refreshButton)
    {
        GameObject header = Rect("Header", panel);
        var rt = (RectTransform)header.transform;
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, HeaderH);
        rt.anchoredPosition = new Vector2(0, -HeaderTop);
        var hlg = header.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset((int)LegendInsetX, (int)ContentSidePad, 0, 0);
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

        // ✦ + «ПРЕДЛОЖЕНИЯ» overline.
        Image spark = ImageGo("Spark", header.transform, Color.white).GetComponent<Image>();
        spark.sprite = sparkle; spark.preserveAspect = true; spark.raycastTarget = false;
        Themed(spark.gameObject, ThemeRole.PositiveInk);
        var sparkLe = spark.gameObject.AddComponent<LayoutElement>();
        sparkLe.preferredWidth = HeaderSparkSize; sparkLe.preferredHeight = HeaderSparkSize;
        sparkLe.minWidth = HeaderSparkSize; sparkLe.minHeight = HeaderSparkSize;

        TextMeshProUGUI title = Text("Title", header.transform, "ПРЕДЛОЖЕНИЯ", HeaderTitleSize, Color.black,
            FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 9f;
        title.raycastTarget = false;
        Themed(title.gameObject, ThemeRole.InkTertiary);

        GameObject spacer = Rect("Spacer", header.transform);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // Quiet refresh: full-size invisible hit target (≥120u), small glyph inside. Replaces the FAB.
        GameObject hit = ImageGo("RefreshButton", header.transform, new Color(0, 0, 0, 0));
        var hitLe = hit.AddComponent<LayoutElement>();
        hitLe.preferredWidth = RefreshHit; hitLe.preferredHeight = RefreshHit;
        hitLe.minWidth = RefreshHit; hitLe.minHeight = RefreshHit;
        Image icon = ImageGo("Icon", hit.transform, Color.white).GetComponent<Image>();
        icon.sprite = LoadSpriteByGuid(RefreshIconGuid);
        icon.preserveAspect = true; icon.raycastTarget = false;
        Themed(icon.gameObject, ThemeRole.InkSecondary);
        var irt = (RectTransform)icon.transform; irt.sizeDelta = new Vector2(RefreshIconSize, RefreshIconSize); Center(irt);
        Rect("A11y:" + RefreshA11y, hit.transform);
        refreshButton = hit.AddComponent<Button>();
        refreshButton.transition = Selectable.Transition.None;
    }

    // The fixed-height scroll region below the chrome. Returns the content (cards container).
    private static Transform BuildCardViewport(Transform panel, out RectTransform viewportRt)
    {
        GameObject viewport = ImageGo("CardsViewport", panel, new Color(0, 0, 0, 0));
        viewportRt = (RectTransform)viewport.transform;
        viewportRt.anchorMin = new Vector2(0, 0); viewportRt.anchorMax = new Vector2(1, 1);
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = new Vector2(0, -ChromeHeight);
        viewport.GetComponent<Image>().raycastTarget = true;    // drag anywhere in the region scrolls
        viewport.AddComponent<RectMask2D>();

        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        GameObject content = Rect("CardsContainer", viewport.transform);
        var crt = (RectTransform)content.transform;
        crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
        crt.sizeDelta = Vector2.zero;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset((int)ContentSidePad, (int)ContentSidePad, (int)ContentTopPad, (int)ContentBottomPad);
        vlg.spacing = CardGap;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRt;
        scroll.content = crt;

        // Thin overlay scrollbar (sketch detail): AutoHide keeps it away when content fits.
        GameObject barGo = ImageGo("Scrollbar", viewport.transform, new Color(0, 0, 0, 0));
        var brt = (RectTransform)barGo.transform;
        brt.anchorMin = new Vector2(1, 0); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(1, 0.5f);
        brt.sizeDelta = new Vector2(ScrollbarW, -12f);
        brt.anchoredPosition = new Vector2(-3f, 0f);
        var bar = barGo.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        GameObject slide = Rect("SlidingArea", barGo.transform);
        Stretch((RectTransform)slide.transform);
        GameObject handleGo = ImageGo("Handle", slide.transform, Color.white);
        Themed(handleGo, ThemeRole.Border);
        handleGo.GetComponent<Image>().raycastTarget = false;
        Stretch((RectTransform)handleGo.transform);
        AddRounded(handleGo, ScrollbarW / 2f);
        bar.handleRect = (RectTransform)handleGo.transform;
        bar.targetGraphic = handleGo.GetComponent<Image>();
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        return content.transform;
    }

    private static void BuildFadeOverlay(Transform panel, Sprite fadeSprite)
    {
        // Surface→transparent wash over the viewport's bottom edge — the "there's more" cue.
        // Per-pixel alpha comes from the generated sprite; ThemedColor repaints the hue only.
        GameObject go = ImageGo("BottomFade", panel, Color.white);
        Image img = go.GetComponent<Image>();
        img.sprite = fadeSprite;
        img.raycastTarget = false;
        Themed(go, ThemeRole.Surface);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(0, FadeHeight);
        rt.anchoredPosition = Vector2.zero;
    }

    // === Card ===============================================================

    private static SuggestionCard BuildCard(Transform parent, Sprite sparkle)
    {
        // Two-layer bordered card: root Image = border ring (rounded 42), inset "Fill" = surface
        // (rounded 39). The root's VerticalLayoutGroup sizes the card to the FULL reply text — no
        // per-card scrolling, no truncation; the padding bakes in the 3u border inset.
        GameObject card = ImageGo("SuggestionCard", parent, Color.white);
        Image borderImg = card.GetComponent<Image>();
        AddRounded(card, CardRadius);
        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(
            (int)(CardPadSide + CardBorder), (int)(CardPadSide + CardBorder),
            (int)(CardPadTop + CardBorder), (int)(CardPadBottom + CardBorder));
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        GameObject fill = ImageGo("Fill", card.transform, Color.white);
        fill.AddComponent<LayoutElement>().ignoreLayout = true;
        var frt = (RectTransform)fill.transform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(CardBorder, CardBorder); frt.offsetMax = new Vector2(-CardBorder, -CardBorder);
        fill.GetComponent<Image>().raycastTarget = false;
        AddRounded(fill, CardRadius - CardBorder);
        fill.transform.SetAsFirstSibling();

        // Full reply text — the card's only laid-out child (drives the card height).
        TextMeshProUGUI reply = Text("ReplyText", card.transform, "—", ReplySize, Color.black,
            FontStyles.Normal, TextAlignmentOptions.TopLeft);
        reply.textWrappingMode = TextWrappingModes.Normal;
        reply.overflowMode = TextOverflowModes.Overflow;
        reply.raycastTarget = false;
        Themed(reply.gameObject, ThemeRole.InkPrimary);

        // Legend — sits ON the card's top border (zero interior height). Two half-height strips
        // blend it into what's behind: upper = sheet Surface (static), lower = the card's own fill
        // (stamped per state in SuggestionCard.Setup, like the border/fill themselves).
        Image lowerStrip; GameObject spark; TextMeshProUGUI label;
        BuildLegend(card.transform, sparkle, out lowerStrip, out spark, out label);

        var button = card.AddComponent<Button>();
        button.transition = Selectable.Transition.None;

        var comp = card.AddComponent<SuggestionCard>();
        var so = new SerializedObject(comp);
        so.FindProperty("cardButton").objectReferenceValue = button;
        so.FindProperty("replyText").objectReferenceValue = reply;
        so.FindProperty("intentLabel").objectReferenceValue = label;
        so.FindProperty("cardBackground").objectReferenceValue = fill.GetComponent<Image>();
        so.FindProperty("borderImage").objectReferenceValue = borderImg;
        so.FindProperty("legendLowerStrip").objectReferenceValue = lowerStrip;
        so.FindProperty("sparkIcon").objectReferenceValue = spark;
        so.ApplyModifiedPropertiesWithoutUndo();

        card.SetActive(false);   // template — instantiated per item at runtime
        return comp;
    }

    private static void BuildLegend(Transform card, Sprite sparkle,
        out Image lowerStrip, out GameObject spark, out TextMeshProUGUI label)
    {
        GameObject legend = Rect("Legend", card);
        var lrt = (RectTransform)legend.transform;
        lrt.anchorMin = new Vector2(0, 1); lrt.anchorMax = new Vector2(0, 1); lrt.pivot = new Vector2(0, 0.5f);
        lrt.anchoredPosition = new Vector2(LegendInsetX, 0f);   // centered ON the top border, left-inset
        var le = legend.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        var hlg = legend.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset((int)LegendPadSide, (int)LegendPadSide, 2, 2);
        hlg.spacing = 6;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        var csf = legend.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Background strips (excluded from layout, stretched to the legend rect).
        GameObject upper = ImageGo("StripUpper", legend.transform, Color.white);
        upper.AddComponent<LayoutElement>().ignoreLayout = true;
        var urt = (RectTransform)upper.transform;
        urt.anchorMin = new Vector2(0, 0.5f); urt.anchorMax = Vector2.one;
        urt.offsetMin = Vector2.zero; urt.offsetMax = Vector2.zero;
        upper.GetComponent<Image>().raycastTarget = false;
        Themed(upper, ThemeRole.Surface);           // what's behind the border above = the sheet

        GameObject lower = ImageGo("StripLower", legend.transform, Color.white);
        lower.AddComponent<LayoutElement>().ignoreLayout = true;
        var drt = (RectTransform)lower.transform;
        drt.anchorMin = Vector2.zero; drt.anchorMax = new Vector2(1, 0.5f);
        drt.offsetMin = Vector2.zero; drt.offsetMax = Vector2.zero;
        lowerStrip = lower.GetComponent<Image>();
        lowerStrip.raycastTarget = false;           // color stamped in Setup (= card fill)

        spark = ImageGo("Spark", legend.transform, Color.white);
        Image sparkImg = spark.GetComponent<Image>();
        sparkImg.sprite = sparkle; sparkImg.preserveAspect = true; sparkImg.raycastTarget = false;
        Themed(spark, ThemeRole.PositiveInk);
        var sle = spark.AddComponent<LayoutElement>();
        sle.preferredWidth = LegendSparkSize; sle.preferredHeight = LegendSparkSize;
        sle.minWidth = LegendSparkSize; sle.minHeight = LegendSparkSize;
        spark.SetActive(false);                     // recommended card only (toggled in Setup)

        label = Text("Label", legend.transform, "ЦЕНА", LegendFont, Color.black,
            FontStyles.Bold | FontStyles.UpperCase, TextAlignmentOptions.Center);
        label.characterSpacing = 6f;
        label.raycastTarget = false;                // color stamped in Setup (InkSecondary / PositiveInk)
        upper.transform.SetAsFirstSibling();        // strips behind, then spark, then label on top
        lower.transform.SetSiblingIndex(1);
    }

    // Thinking-dots skeleton: card-shaped (same two-layer border look) with 3 bouncing dots.
    private static GameObject BuildSkeleton(Transform parent, int index)
    {
        GameObject sk = ImageGo("Skeleton" + index, parent, Color.white);
        Themed(sk, ThemeRole.Border);
        AddRounded(sk, CardRadius);
        var le = sk.AddComponent<LayoutElement>();
        le.preferredHeight = SkeletonHeight; le.minHeight = SkeletonHeight; le.flexibleWidth = 1f;
        sk.AddComponent<CanvasGroup>();

        GameObject fill = ImageGo("Fill", sk.transform, Color.white);
        var frt = (RectTransform)fill.transform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(CardBorder, CardBorder); frt.offsetMax = new Vector2(-CardBorder, -CardBorder);
        fill.GetComponent<Image>().raycastTarget = false;
        AddRounded(fill, CardRadius - CardBorder);
        Themed(fill, ThemeRole.Surface);

        GameObject row = Rect("Dots", sk.transform);
        var rrt = (RectTransform)row.transform; Center(rrt); rrt.sizeDelta = new Vector2(140f, 40f);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16; hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        var dots = new Graphic[3];
        for (int i = 0; i < 3; i++)
        {
            GameObject d = ImageGo("Dot" + i, row.transform, Color.white);
            Themed(d, ThemeRole.InkTertiary);
            AddRounded(d, 11f);
            var dle = d.AddComponent<LayoutElement>();
            dle.preferredWidth = 22f; dle.minWidth = 22f; dle.preferredHeight = 22f; dle.minHeight = 22f;
            dots[i] = d.GetComponent<Image>();
        }

        var anim = sk.AddComponent<ThinkingDotsSkeleton>();
        var so = new SerializedObject(anim);
        SerializedProperty arr = so.FindProperty("dots");
        arr.arraySize = dots.Length;
        for (int i = 0; i < dots.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = dots[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        return sk;
    }

    // === Empty / Error overlays ============================================

    private static GameObject BuildEmptyState(Transform panel, RectTransform area)
    {
        GameObject go = Rect("EmptyState", panel);
        OverlayOver(go, area);
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter; vlg.spacing = 24f;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        TextMeshProUGUI head = Text("Heading", go.transform, "Нет предложений", StateSize, Color.black,
            FontStyles.Bold, TextAlignmentOptions.Center);
        Themed(head.gameObject, ThemeRole.InkPrimary);
        TextMeshProUGUI body = Text("Body", go.transform, "Напишите ответ вручную", StateSize, Color.black,
            FontStyles.Normal, TextAlignmentOptions.Center);
        Themed(body.gameObject, ThemeRole.InkSecondary);
        go.SetActive(false);
        return go;
    }

    private static GameObject BuildErrorState(Transform panel, RectTransform area, out Button retry)
    {
        GameObject go = Rect("ErrorState", panel);
        OverlayOver(go, area);
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter; vlg.spacing = 24f;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        TextMeshProUGUI head = Text("Heading", go.transform, "Не удалось загрузить", StateSize, Color.black,
            FontStyles.Bold, TextAlignmentOptions.Center);
        Themed(head.gameObject, ThemeRole.InkPrimary);
        TextMeshProUGUI body = Text("Body", go.transform, "Проверьте соединение и попробуйте снова", StateSize, Color.black,
            FontStyles.Normal, TextAlignmentOptions.Center);
        Themed(body.gameObject, ThemeRole.InkSecondary);

        // Ghost retry: InputBorder outline ring + Surface fill + AccentText label (spec state style).
        GameObject retryGo = ImageGo("RetryButton", go.transform, Color.white);
        Themed(retryGo, ThemeRole.InputBorder);
        var le = retryGo.AddComponent<LayoutElement>(); le.minHeight = RefreshHit; le.minWidth = 280f;
        AddRounded(retryGo, RetryRadius);
        GameObject rfill = ImageGo("Fill", retryGo.transform, Color.white);
        var frt = (RectTransform)rfill.transform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(CardBorder, CardBorder); frt.offsetMax = new Vector2(-CardBorder, -CardBorder);
        rfill.GetComponent<Image>().raycastTarget = false;
        AddRounded(rfill, RetryRadius - CardBorder);
        Themed(rfill, ThemeRole.Surface);
        TextMeshProUGUI lab = Text("Label", retryGo.transform, "Обновить", RetryFont, Color.black,
            FontStyles.Bold, TextAlignmentOptions.Center);
        lab.raycastTarget = false;
        Themed(lab.gameObject, ThemeRole.AccentText);
        Stretch((RectTransform)lab.transform);
        retry = retryGo.AddComponent<Button>();
        retry.transition = Selectable.Transition.None;
        go.SetActive(false);
        return go;
    }

    // === Toggle (built only if absent — untouched by the redesign) =========

    private static void BuildToggle(Transform topBar)
    {
        GameObject go = ImageGo(ToggleName, topBar, SwitchTrackAuto);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f);
        rt.sizeDelta = new Vector2(SwitchW, SwitchH);
        rt.anchoredPosition = new Vector2(-48f, -40f);
        AddRounded(go, SwitchH / 2f);

        var button = go.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = go.GetComponent<Image>();

        TextMeshProUGUI faintVmeste = BuildSwitchLabel("FaintVmeste", go.transform, "Вместе", -SwitchSlideX, FontStyles.Normal, SwitchFaintAuto);
        TextMeshProUGUI faintAvto   = BuildSwitchLabel("FaintAvto",   go.transform, "Авто",    SwitchSlideX, FontStyles.Normal, SwitchFaintAuto);

        GameObject thumbGo = ImageGo("Thumb", go.transform, Color.white);
        var thumbRt = (RectTransform)thumbGo.transform;
        thumbRt.anchorMin = new Vector2(0.5f, 0.5f); thumbRt.anchorMax = new Vector2(0.5f, 0.5f); thumbRt.pivot = new Vector2(0.5f, 0.5f);
        thumbRt.sizeDelta = new Vector2(SwitchThumbW, SwitchThumbH);
        thumbRt.anchoredPosition = new Vector2(SwitchSlideX, 0f);
        thumbGo.GetComponent<Image>().raycastTarget = false;
        AddRounded(thumbGo, SwitchThumbH / 2f);

        TextMeshProUGUI thumbLabel = BuildSwitchLabel("ThumbLabel", thumbGo.transform, "Авто", 0f, FontStyles.Bold, SwitchInkAuto);
        Stretch((RectTransform)thumbLabel.transform);

        Rect("A11y:" + ToggleA11y, go.transform);

        var comp = go.AddComponent<SemiAutoToggle>();
        var so = new SerializedObject(comp);
        so.FindProperty("toggleButton").objectReferenceValue = button;
        so.FindProperty("trackImage").objectReferenceValue = go.GetComponent<Image>();
        so.FindProperty("thumb").objectReferenceValue = thumbRt;
        so.FindProperty("thumbLabel").objectReferenceValue = thumbLabel;
        so.FindProperty("faintAvto").objectReferenceValue = faintAvto;
        so.FindProperty("faintVmeste").objectReferenceValue = faintVmeste;
        so.FindProperty("thumbX").floatValue = SwitchSlideX;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static TextMeshProUGUI BuildSwitchLabel(string name, Transform parent, string text, float x,
        FontStyles style, Color color)
    {
        TextMeshProUGUI tmp = Text(name, parent, text, SwitchFont, color, style, TextAlignmentOptions.Center);
        tmp.characterSpacing = -2f;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        var rt = (RectTransform)tmp.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(SwitchThumbW + 20f, SwitchH);
        rt.anchoredPosition = new Vector2(x, 0f);
        return tmp;
    }

    // === Generated sprites ==================================================

    // 4-point star (✦), white on transparent — tinted at use sites (PositiveInk).
    private static Sprite EnsureSparkleSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(SparklePath);
        if (existing != null) return existing;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float half = size / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Concave-diamond star: |x|^p + |y|^p ≤ r^p with p<1 pinches the diagonals into a ✦.
            float nx = Mathf.Abs(x + 0.5f - half) / half;
            float ny = Mathf.Abs(y + 0.5f - half) / half;
            float v = Mathf.Pow(nx, 0.55f) + Mathf.Pow(ny, 0.55f);
            float a = Mathf.Clamp01((1f - v) * 8f);          // soft edge
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return SaveSprite(tex, SparklePath);
    }

    // Vertical alpha gradient — opaque at the bottom row, transparent at the top.
    private static Sprite EnsureFadeSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(FadePath);
        if (existing != null) return existing;
        const int w = 4, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            float a = 1f - (y / (float)(h - 1));
            for (int x = 0; x < w; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return SaveSprite(tex, FadePath);
    }

    private static Sprite SaveSprite(Texture2D tex, string path)
    {
        if (!AssetDatabase.IsValidFolder(SpriteFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Sprites")) AssetDatabase.CreateFolder("Assets", "Sprites");
            AssetDatabase.CreateFolder("Assets/Sprites", "Suggestions");
        }
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // === Helpers ============================================================

    private static void Themed(GameObject go, ThemeRole role)
    {
        var themed = go.GetComponent<ThemedColor>();
        if (themed == null) themed = go.AddComponent<ThemedColor>();
        var so = new SerializedObject(themed);
        so.FindProperty("role").enumValueIndex = (int)role;
        so.FindProperty("target").objectReferenceValue = go.GetComponent<Graphic>();
        so.FindProperty("preserveAlpha").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        // Stamp the resolved colour now so the Editor scene shows the design without entering play mode.
        Graphic g = go.GetComponent<Graphic>();
        if (g != null)
        {
            Color c = Theme.Color(role);
            c.a = g.color.a;
            g.color = c;
        }
    }

    private static GameObject Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return go;
    }

    private static GameObject ImageGo(string name, Transform parent, Color color)
    {
        GameObject go = Rect(name, parent);
        var img = go.AddComponent<Image>();
        img.color = color;                               // null-sprite Image (never UISprite.psd on surfaces)
        return go;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, string text, float size,
        Color color, FontStyles style, TextAlignmentOptions align)
    {
        GameObject go = Rect(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color;
        tmp.fontStyle = style; tmp.alignment = align;    // alignment set explicitly (skill gotcha)
        return tmp;
    }

    private static void AddRounded(GameObject go, float radius)
    {
        var rounded = go.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = radius;
        rounded.Validate();
        rounded.Refresh();
    }

    private static Sprite LoadSpriteByGuid(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void AddRoundedTop(GameObject go, float radius)
    {
        var rounded = go.AddComponent<ImageWithIndependentRoundedCorners>();
        rounded.r = new Vector4(radius, radius, 0f, 0f); // top-left, top-right only
        rounded.Validate();
        rounded.Refresh();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void Center(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero;
    }

    private static void OverlayOver(GameObject go, RectTransform area)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = area.anchorMin; rt.anchorMax = area.anchorMax; rt.pivot = area.pivot;
        rt.offsetMin = area.offsetMin; rt.offsetMax = area.offsetMax;
    }

    private static GameObject ResolveHost()
    {
        GameObject sel = Selection.activeGameObject;
        if (sel != null && sel.name == "MessagesPanel") return sel;
        Transform screen = FindInScene("Screen_Whatsapp");
        if (screen != null)
        {
            Transform mp = FindChildRecursive(screen, "MessagesPanel");
            if (mp != null) return mp.gameObject;
        }
        Transform any = FindInScene("MessagesPanel");
        return any != null ? any.gameObject : null;
    }

    private static Transform FindInScene(string name)
    {
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name) return root.transform;
            Transform found = FindChildRecursive(root.transform, name);
            if (found != null) return found;
        }
        return null;
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child != parent && child.name == name) return child;
        return null;
    }

    private static Color Hex(string hex)
        => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
}

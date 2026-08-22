#if UNITY_EDITOR
using System.Collections.Generic;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the «Боты» page's billing surface (Task 14c, spec §6) into Screen_Bots:
///
///  • <b>TrialPill</b> — first child of <c>NavHeader/HeaderIcons</c>, so the header's own
///    right-aligned HorizontalLayoutGroup seats it left of the «+» button;
///  • <b>UsageStrip</b> — a Surface card between the header and the list, a sibling of
///    ScrollContent under BotsPage;
///  • <b>AddBotCard</b> — an outlined ghost row under <c>ScrollContent/Viewport</c>,
///    deliberately a SIBLING of BotsParent rather than a child: <c>BotsParent.childCount</c>
///    is the app's authoritative «has bots» fact in six places (BotsPage.RefreshEmptyState,
///    Manager's two gates, FirstStepsCard, the wipe walk, OnboardingProgressReset), and a
///    non-Bot child would silently make every one of them count one too many. It is
///    <i>positioned</i> like a final row instead, by BotsPageBilling.
///
/// ADDITIVE: it touches nothing outside those three nodes, the <c>BotsPageBilling</c>
/// component it adds to the BotsPage GameObject, and the three page metrics that component
/// owns at runtime. Each of the three nodes is destroyed and rebuilt by NAME on every run,
/// so the GameObject census is stable (their fileIDs are new each run; nothing else in the
/// scene is). No other builder on this page is re-run — NavRestructureBuilder and
/// FirstStepsCardBuilder are both destroy-and-rebuild and would wipe the owner's tuning.
///
/// ⚠️ Because these three subtrees are rebuilt wholesale, hand-tuning inside them does NOT
/// survive a re-run. Tune here, in the builder, or stop re-running it.
///
/// The scene is authored WITH the strip's compact-state inset applied so the Scene/Game
/// view is not lying about the page; BotsPageBilling reads its un-inset bases from the
/// serialized fields this wirer stamps (captured once, preserved on every re-run), never
/// from the live rects. All sizes are 1080×1920 canvas reference units.
///
/// ONE authored value is a placeholder by necessity: the inner rows of the pill, the strip
/// and the card are LayoutGroup children, and a layout group skips every child that is not
/// activeInHierarchy — Screen_Bots is an inactive tab screen, so those rects serialise at
/// their default centred size and only take their real places the moment the tab activates
/// (in Play mode, or when the screen is switched on in the Scene view). The wirer asks for
/// the rebuild anyway, which does land when the page happens to be active. Activating the
/// ancestors to force it is deliberately NOT done: every [ExecuteInEditMode]
/// ImageWithRoundedCorners in the subtree would re-mint its material on OnEnable and churn
/// the scene diff far beyond this task. Same class of authored-placeholder as the quota
/// bar's 50% fill anchor, which the controller rewrites on every render.
/// </summary>
public static class BotsPageBillingWirer
{
    // ── Node names (also the idempotency keys) ───────────────────────────────
    private const string ScenePath = "Assets/Scenes/Main.unity";
    private const string PillName = "TrialPill";
    private const string StripName = "UsageStrip";
    private const string AddCardName = "AddBotCard";

    // ── Design tokens (reference units) ──────────────────────────────────────
    private const float PillChipHeight = 68f;
    private const float PillChipRadius = 34f;
    private const float PillFontSize = 30f;

    private const float StripRadius = 36f;
    private const float StripPadX = 32f;
    private const float StripPadTop = 28f;
    private const float StripTitleHeight = 44f;
    private const float StripTitleFont = 32f;
    private const float StripBarHeight = 18f;
    private const float StripHintHeight = 40f;
    private const float StripHintFont = 30f;

    private const float AddCardRadius = 40f;
    private const float AddCardRingWidth = 3f;
    private const float AddCardPadX = 44f;
    private const float AddCardGap = 36f;
    private const float AddBadgeSize = 100f;
    private const float AddBadgeRadius = 32f;
    private const float AddBadgeGlyphInset = 30f;
    private const float AddTitleFont = 42f;
    private const float AddSubFont = 32f;

    // Fonts by GUID (the default font's weight table is empty — always assign explicitly).
    private const string RegularGuid = "e0cdfe2d6a51446bcba7d2df147e2415";
    private const string MediumGuid = "d091b0cad5d964a53a41de97ba932a27";
    private const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";

    private const string PlusIconPath = "Assets/Images/New/plus.png";

    private static TMP_FontAsset _regular, _medium, _semibold;
    private static Sprite _plusIcon;
    private static readonly List<Component> _roundedToRefresh = new List<Component>();

    // ── Entry points ─────────────────────────────────────────────────────────

    [MenuItem("Tools/Billing/Wire Bots Page Billing")]
    public static void Build()
    {
        GameObject host = BuildInternal();
        Selection.activeGameObject = host;
        EditorSceneManager.MarkSceneDirty(host.scene);
        Debug.Log("[BotsPageBillingWirer] Build complete: TrialPill + UsageStrip + AddBotCard. SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod BotsPageBillingWirer.BuildHeadless -quit
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        BuildInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[BotsPageBillingWirer] Headless build + save complete.");
    }

    // ── Main build ───────────────────────────────────────────────────────────

    private static GameObject BuildInternal()
    {
        LoadAssets();
        _roundedToRefresh.Clear();

        var botsPage = Object.FindFirstObjectByType<BotsPage>(FindObjectsInactive.Include);
        if (botsPage == null)
            throw new System.InvalidOperationException("[BotsPageBillingWirer] BotsPage not found — is Main.unity open?");

        Transform page = botsPage.transform;
        Transform headerIcons = Require(page, "NavHeader/HeaderIcons");
        Transform scrollContent = Require(page, "ScrollContent");
        Transform viewport = Require(page, "ScrollContent/Viewport");
        Transform botsList = Require(page, "ScrollContent/Viewport/BotsParent");
        Transform firstSteps = page.Find("FirstStepsCard");
        if (firstSteps == null)
            Debug.LogWarning("[BotsPageBillingWirer] FirstStepsCard not found — the strip will not push it down.");

        // The page must be live for TMP measurement and the rounded-corner radius bake.
        bool pageWasActive = botsPage.gameObject.activeSelf;
        botsPage.gameObject.SetActive(true);

        var billing = botsPage.GetComponent<BotsPageBilling>()
                      ?? botsPage.gameObject.AddComponent<BotsPageBilling>();
        var so = new SerializedObject(billing);

        // Capture the un-inset bases ONCE (first run reads the untouched scene); on every
        // later run the stamped values are already the truth and the live rects are inset.
        float scrollTopBase = ResolveBase(so, "scrollTopBase", ((RectTransform)scrollContent).offsetMax.y);
        float firstStepsBaseY = ResolveBase(so, "firstStepsBaseY",
            firstSteps != null ? ((RectTransform)firstSteps).anchoredPosition.y : 0f);
        var listLayout = botsList.GetComponent<VerticalLayoutGroup>();
        int listPadBottomBase = (int)ResolveBase(so, "listPadBottomBase",
            listLayout != null ? listLayout.padding.bottom : 0);

        BuildPill(headerIcons, so);
        BuildStrip(page, scrollContent, scrollTopBase, so);
        BuildAddCard(viewport, botsList, listLayout, listPadBottomBase, so);

        so.FindProperty("botsPage").objectReferenceValue = botsPage;
        so.FindProperty("botsList").objectReferenceValue = botsList;
        so.FindProperty("scrollContent").objectReferenceValue = scrollContent;
        so.FindProperty("firstStepsCard").objectReferenceValue = firstSteps;
        so.FindProperty("scrollTopBase").floatValue = scrollTopBase;
        so.FindProperty("firstStepsBaseY").floatValue = firstStepsBaseY;
        so.FindProperty("listPadBottomBase").intValue = listPadBottomBase;
        so.FindProperty(BaseStampedFlag).boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Edit-time preview of the runtime compact state, so the Scene view shows the real
        // page. BotsPageBilling recomputes both from the stamped bases on its first Refresh.
        ApplyPreviewInset(scrollContent, firstSteps, scrollTopBase, firstStepsBaseY);

        // Lay the three new subtrees out for real before saving. Adding a child does not
        // schedule a rebuild in edit mode, so without this the header pill and both cards'
        // inner rows serialise at their default centred rects — the scene would look broken
        // in the Scene view even though runtime is correct. It is also what gives the
        // rounded-corner bake below a real width/height to read.
        Canvas.ForceUpdateCanvases();
        RebuildLayout(headerIcons);
        RebuildLayout(page.Find(StripName));
        RebuildLayout(viewport.Find(AddCardName));
        Canvas.ForceUpdateCanvases();

        foreach (Component rounded in _roundedToRefresh)
            RefreshRounded(rounded);

        botsPage.gameObject.SetActive(pageWasActive);
        return botsPage.gameObject;
    }

    /// <summary>
    /// A base is authoritative once stamped: on a re-run the live rect already carries this
    /// wirer's preview inset, so re-reading it would drift the base a strip-block per run.
    /// A never-stamped field still holds its C# default, which is why the sentinel is the
    /// FIELD's default rather than zero.
    /// </summary>
    private static float ResolveBase(SerializedObject so, string field, float liveValue)
    {
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null) return liveValue;

        bool stamped = so.FindProperty(BaseStampedFlag) != null && so.FindProperty(BaseStampedFlag).boolValue;
        if (stamped)
            return prop.propertyType == SerializedPropertyType.Integer ? prop.intValue : prop.floatValue;
        return liveValue;
    }

    private const string BaseStampedFlag = "layoutBasesStamped";

    private static void ApplyPreviewInset(Transform scrollContent, Transform firstSteps,
        float scrollTopBase, float firstStepsBaseY)
    {
        float block = BotsPageBilling.StripGap + BotsPageBilling.StripHeightCompact;

        var scrollRt = (RectTransform)scrollContent;
        Vector2 max = scrollRt.offsetMax;
        max.y = scrollTopBase - block;
        scrollRt.offsetMax = max;

        if (firstSteps == null) return;
        var stepsRt = (RectTransform)firstSteps;
        Vector2 pos = stepsRt.anchoredPosition;
        pos.y = firstStepsBaseY - block;
        stepsRt.anchoredPosition = pos;
    }

    // ── (a) Header trial pill ────────────────────────────────────────────────

    private static void BuildPill(Transform headerIcons, SerializedObject so)
    {
        DestroyAllByName(headerIcons, PillName);

        // FIRST child of the header's right-aligned HorizontalLayoutGroup, so it sits left
        // of the «+» button; the group has childControlWidth OFF, so the pill's own
        // sizeDelta is what it allocates — BotsPageBilling writes the measured width.
        var pill = NewChild(headerIcons.gameObject, PillName, out RectTransform pillRt);
        pill.transform.SetSiblingIndex(0);
        pillRt.sizeDelta = new Vector2(300f, BotsPageBilling.PillHeight);

        // Transparent hit area around the chip: the visible chip is 68 tall, the tap target
        // the house floor (120). Not the Button's targetGraphic — ColorTint multiplies into
        // the target's colour and an alpha-0 graphic shows nothing (14a's Restore lesson).
        var hit = pill.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        var chip = NewChild(pill, "Chip", out RectTransform chipRt);
        chipRt.anchorMin = new Vector2(0f, 0.5f);
        chipRt.anchorMax = new Vector2(1f, 0.5f);
        chipRt.pivot = new Vector2(0.5f, 0.5f);
        chipRt.offsetMin = new Vector2(0f, -PillChipHeight / 2f);
        chipRt.offsetMax = new Vector2(0f, PillChipHeight / 2f);
        var chipImg = chip.AddComponent<Image>();
        chipImg.color = Color.white;
        chipImg.raycastTarget = true;
        var chipTheme = chip.AddComponent<ThemedColor>();
        chipTheme.Configure(ThemeRole.AccentSoft, chipImg);
        AddRounded(chip, PillChipRadius);

        var labelGo = NewChild(chip, "Label", out RectTransform labelRt);
        StretchFill(labelRt);
        var label = AddText(labelGo, PaywallCopy.TrialPill(PlanCatalog.TrialDays), PillFontSize, _semibold, null);
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        var inkTheme = labelGo.AddComponent<ThemedColor>();
        inkTheme.Configure(ThemeRole.AccentText, label);

        var button = pill.AddComponent<Button>();
        button.targetGraphic = chipImg;                       // the opaque chip, so the tint shows
        button.transition = Selectable.Transition.ColorTint;

        so.FindProperty("pillRoot").objectReferenceValue = pill;
        so.FindProperty("pillRect").objectReferenceValue = pillRt;
        so.FindProperty("pillButton").objectReferenceValue = button;
        so.FindProperty("pillLabel").objectReferenceValue = label;
        so.FindProperty("pillBgTheme").objectReferenceValue = chipTheme;
        so.FindProperty("pillInkTheme").objectReferenceValue = inkTheme;
    }

    // ── (b) Usage strip ──────────────────────────────────────────────────────

    private static void BuildStrip(Transform page, Transform scrollContent, float scrollTopBase, SerializedObject so)
    {
        DestroyAllByName(page, StripName);

        var strip = NewChild(page.gameObject, StripName, out RectTransform stripRt);
        // Right after ScrollContent: later siblings win a pointer in uGUI, and the strip
        // must stay above the (transparent) list background it now sits beside.
        strip.transform.SetSiblingIndex(scrollContent.GetSiblingIndex() + 1);

        stripRt.anchorMin = new Vector2(0f, 1f);
        stripRt.anchorMax = new Vector2(1f, 1f);
        stripRt.pivot = new Vector2(0.5f, 1f);
        stripRt.offsetMin = new Vector2(ListSideInset, 0f);
        stripRt.offsetMax = new Vector2(-ListSideInset, 0f);
        stripRt.sizeDelta = new Vector2(stripRt.sizeDelta.x, BotsPageBilling.StripHeightCompact);
        stripRt.anchoredPosition = new Vector2(0f, scrollTopBase - BotsPageBilling.StripGap);

        var bg = strip.AddComponent<Image>();
        bg.color = Color.white;
        bg.raycastTarget = true;
        Themed(strip, ThemeRole.Surface);
        AddRounded(strip, StripRadius);
        var button = strip.AddComponent<Button>();
        button.targetGraphic = bg;
        button.transition = Selectable.Transition.ColorTint;

        // Title row: caption left, «214 из 300» right. Explicit anchored rows rather than a
        // VerticalLayoutGroup + ContentSizeFitter, because the strip's own height is what
        // drives the page inset and must be a number this component decides, not one a
        // layout pass hands back a frame later.
        var titleRow = NewChild(strip, "TitleRow", out RectTransform titleRt);
        TopRow(titleRt, StripPadTop, StripTitleHeight);
        AddHorizontalGroup(titleRow, new RectOffset(0, 0, 0, 0), 24f, TextAnchor.MiddleLeft);

        var titleGo = NewChild(titleRow, "Title", out _);
        var title = AddText(titleGo, BotsPageRows.MeterTitle(System.DateTime.Now), StripTitleFont,
            _medium, ThemeRole.InkSecondary);
        var titleLe = titleGo.AddComponent<LayoutElement>();
        titleLe.preferredWidth = 0f;      // mask the label's appetite so the value is never squeezed
        titleLe.flexibleWidth = 1f;

        var valueGo = NewChild(titleRow, "Value", out _);
        var value = AddText(valueGo, SubscriptionPageRows.UnknownUsageLine(PlanCatalog.TrialDialogCap).Text,
            StripTitleFont, _semibold, ThemeRole.InkPrimary);
        value.alignment = TextAlignmentOptions.MidlineRight;
        value.textWrappingMode = TextWrappingModes.NoWrap;
        valueGo.AddComponent<LayoutElement>().flexibleWidth = 0f;

        var bar = NewChild(strip, "Bar", out RectTransform barRt);
        TopRow(barRt, StripPadTop + StripTitleHeight + 18f, StripBarHeight);
        var barImg = bar.AddComponent<Image>();
        barImg.color = Color.white;
        barImg.raycastTarget = false;
        Themed(bar, ThemeRole.Hairline);
        AddRounded(bar, StripBarHeight / 2f);

        var fill = NewChild(bar, "Fill", out RectTransform fillRt);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0.5f, 1f);   // rewritten every render from the live fraction
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = Color.white;
        fillImg.raycastTarget = false;
        var fillTheme = fill.AddComponent<ThemedColor>();
        fillTheme.Configure(ThemeRole.AccentFill, fillImg);
        AddRounded(fill, StripBarHeight / 2f);

        var hintGo = NewChild(strip, "Hint", out RectTransform hintRt);
        TopRow(hintRt, StripPadTop + StripTitleHeight + 18f + StripBarHeight + 16f, StripHintHeight);
        var hint = AddText(hintGo, "", StripHintFont, _regular, null);
        hint.alignment = TextAlignmentOptions.MidlineLeft;
        hint.textWrappingMode = TextWrappingModes.NoWrap;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        var hintTheme = hintGo.AddComponent<ThemedColor>();
        hintTheme.Configure(ThemeRole.InkSecondary, hint);
        hintGo.SetActive(false);   // Ok state is the default; the strip is authored compact

        so.FindProperty("stripRoot").objectReferenceValue = strip;
        so.FindProperty("stripRect").objectReferenceValue = stripRt;
        so.FindProperty("stripButton").objectReferenceValue = button;
        so.FindProperty("stripTitle").objectReferenceValue = title;
        so.FindProperty("stripValue").objectReferenceValue = value;
        so.FindProperty("stripBarFill").objectReferenceValue = fillRt;
        so.FindProperty("stripBarFillTheme").objectReferenceValue = fillTheme;
        so.FindProperty("stripHintRoot").objectReferenceValue = hintGo;
        so.FindProperty("stripHint").objectReferenceValue = hint;
        so.FindProperty("stripHintTheme").objectReferenceValue = hintTheme;
    }

    // ── (c) «+ бот» ghost card ───────────────────────────────────────────────

    private static void BuildAddCard(Transform viewport, Transform botsList,
        VerticalLayoutGroup listLayout, int listPadBottomBase, SerializedObject so)
    {
        DestroyAllByName(viewport, AddCardName);

        var card = NewChild(viewport.gameObject, AddCardName, out RectTransform cardRt);
        card.transform.SetSiblingIndex(botsList.GetSiblingIndex() + 1);

        // Same anchors and side insets as a bot card, so it lines up with the list.
        float sideInset = listLayout != null ? listLayout.padding.left : ListSideInset;
        cardRt.anchorMin = new Vector2(0f, 1f);
        cardRt.anchorMax = new Vector2(1f, 1f);
        cardRt.pivot = new Vector2(0.5f, 1f);
        cardRt.offsetMin = new Vector2(sideInset, 0f);
        cardRt.offsetMax = new Vector2(-(listLayout != null ? listLayout.padding.right : ListSideInset), 0f);
        cardRt.sizeDelta = new Vector2(cardRt.sizeDelta.x, BotsPageBilling.AddCardHeight);
        // Edit-time preview only — BotsPageBilling re-seats it from the live content height
        // and scroll offset on every frame the card is showing.
        float spacing = listLayout != null ? listLayout.spacing : 0f;
        cardRt.anchoredPosition = new Vector2(0f,
            -(((RectTransform)botsList).rect.height - listPadBottomBase + spacing));

        // The project has no dashed-border primitive, so the ghost look is a solid Border
        // ring around a page-ground fill: a rounded rect drawn 3 units larger, with the
        // Background-coloured Fill punched over it (14a's tier-card Ring idiom).
        var ring = NewChild(card, "Ring", out RectTransform ringRt);
        Ignore(ring);
        StretchFill(ringRt, -AddCardRingWidth);
        var ringImg = ring.AddComponent<Image>();
        ringImg.color = Color.white;
        ringImg.raycastTarget = false;
        Themed(ring, ThemeRole.Border);
        AddRounded(ring, AddCardRadius + AddCardRingWidth);

        var fill = NewChild(card, "Fill", out RectTransform fillRt);
        Ignore(fill);
        StretchFill(fillRt);
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = Color.white;
        fillImg.raycastTarget = true;      // the whole card is the tap target
        Themed(fill, ThemeRole.Background);
        AddRounded(fill, AddCardRadius);

        AddHorizontalGroup(card, new RectOffset((int)AddCardPadX, (int)AddCardPadX, 0, 0),
            AddCardGap, TextAnchor.MiddleLeft);

        var badge = NewChild(card, "Badge", out _);
        SetFixedSize(badge, AddBadgeSize, AddBadgeSize);
        var badgeImg = badge.AddComponent<Image>();
        badgeImg.color = Color.white;
        badgeImg.raycastTarget = false;
        Themed(badge, ThemeRole.AccentSoft);
        AddRounded(badge, AddBadgeRadius);

        var glyph = NewChild(badge, "Icon", out RectTransform glyphRt);
        StretchFill(glyphRt, AddBadgeGlyphInset);
        var glyphImg = glyph.AddComponent<Image>();
        glyphImg.sprite = _plusIcon;
        glyphImg.color = Color.white;
        glyphImg.preserveAspect = true;
        glyphImg.raycastTarget = false;
        Themed(glyph, ThemeRole.AccentText);

        var column = NewChild(card, "Text", out _);
        AddVerticalGroup(column, new RectOffset(0, 0, 0, 0), 8f);
        var columnLe = column.AddComponent<LayoutElement>();
        columnLe.preferredWidth = 0f;      // mask the appetite (ItemCard-B2 rule)
        columnLe.flexibleWidth = 1f;

        var titleGo = NewChild(column, "Title", out _);
        var title = AddText(titleGo, BotsPageRows.AddBotTitle, AddTitleFont, _medium, ThemeRole.InkPrimary);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;

        var subGo = NewChild(column, "Subtext", out _);
        var sub = AddText(subGo, BotsPageRows.AddBotSubtext(PlanTier.Trial, 1), AddSubFont,
            _regular, ThemeRole.InkTertiary);
        sub.textWrappingMode = TextWrappingModes.NoWrap;
        sub.overflowMode = TextOverflowModes.Ellipsis;

        var button = card.AddComponent<Button>();
        // ColorTint on the LABEL, not on the Fill: the fill is the page ground, and tinting
        // it toward grey on press would read as the card turning into a hole.
        button.targetGraphic = title;
        button.transition = Selectable.Transition.ColorTint;

        // The list's bottom padding is the card's SLOT — but it is BotsPageBilling's to
        // write at runtime (it clears the reservation when the card hides), so the scene
        // keeps the un-reserved base and the edit-time card simply overlaps the empty
        // list's bottom padding. Restoring it here also repairs a base captured mid-play.
        if (listLayout != null && listLayout.padding.bottom != listPadBottomBase)
            listLayout.padding.bottom = listPadBottomBase;

        so.FindProperty("addCardRoot").objectReferenceValue = card;
        so.FindProperty("addCardRect").objectReferenceValue = cardRt;
        so.FindProperty("addCardButton").objectReferenceValue = button;
        so.FindProperty("addCardTitle").objectReferenceValue = title;
        so.FindProperty("addCardSubtext").objectReferenceValue = sub;
    }

    // ── Assets ───────────────────────────────────────────────────────────────

    /// <summary>Matches BotsParent's own VerticalLayoutGroup side padding.</summary>
    private const float ListSideInset = 55f;

    private static void LoadAssets()
    {
        _regular = LoadFont(RegularGuid);
        _medium = LoadFont(MediumGuid);
        _semibold = LoadFont(SemiboldGuid);
        _plusIcon = AssetDatabase.LoadAssetAtPath<Sprite>(PlusIconPath);
        if (_plusIcon == null) Debug.LogWarning($"[BotsPageBillingWirer] Sprite missing: {PlusIconPath}");
    }

    private static TMP_FontAsset LoadFont(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null) Debug.LogWarning($"[BotsPageBillingWirer] Font missing for GUID {guid}");
        return font;
    }

    // ── Low-level helpers (SubscriptionPageBuilder idiom) ───────────────────

    private static Transform Require(Transform root, string path)
    {
        Transform found = root.Find(path);
        if (found == null)
            throw new System.InvalidOperationException($"[BotsPageBillingWirer] '{path}' not found under BotsPage.");
        return found;
    }

    private static void RebuildLayout(Transform root)
    {
        if (root is RectTransform rt) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    private static void DestroyAllByName(Transform parent, string name)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            if (parent.GetChild(i).name == name)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    private static GameObject NewChild(GameObject parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    /// <summary>Full-width row pinned <paramref name="top"/> below the strip's top edge.</summary>
    private static void TopRow(RectTransform rt, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(StripPadX, 0f);
        rt.offsetMax = new Vector2(-StripPadX, 0f);
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        rt.anchoredPosition = new Vector2(0f, -top);
    }

    private static void StretchFill(RectTransform rt, float uniformInset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(uniformInset, uniformInset);
        rt.offsetMax = new Vector2(-uniformInset, -uniformInset);
    }

    private static void Ignore(GameObject go) => go.AddComponent<LayoutElement>().ignoreLayout = true;

    private static void AddVerticalGroup(GameObject go, RectOffset padding, float spacing)
    {
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = padding;
        vlg.spacing = spacing;
        vlg.childAlignment = TextAnchor.MiddleLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
    }

    private static void AddHorizontalGroup(GameObject go, RectOffset padding, float spacing, TextAnchor alignment)
    {
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = padding;
        hlg.spacing = spacing;
        hlg.childAlignment = alignment;
        hlg.childControlWidth = true;
        // childControlHeight TRUE everywhere: a group that sizes itself from a child's stale
        // sizeDelta ships permanently one layout pass behind (the bubble-row lesson).
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
    }

    private static void SetFixedSize(GameObject go, float width, float height)
    {
        var le = go.AddComponent<LayoutElement>();
        le.minWidth = width;
        le.minHeight = height;
        le.preferredWidth = width;
        le.preferredHeight = height;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;
    }

    private static TextMeshProUGUI AddText(GameObject go, string text, float size,
        TMP_FontAsset font, ThemeRole? role)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        if (font != null) tmp.font = font;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        // This project's TMP default is NO wrapping — set it explicitly either way.
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        if (role.HasValue) Themed(go, role.Value);
        return tmp;
    }

    private static void Themed(GameObject go, ThemeRole role)
    {
        var graphic = go.GetComponent<Graphic>();
        if (graphic == null)
        {
            Debug.LogWarning($"[BotsPageBillingWirer] Themed('{go.name}') found no Graphic — binding skipped.");
            return;
        }
        go.AddComponent<ThemedColor>().Configure(role, graphic, keepAlpha: true);
    }

    private static void AddRounded(GameObject go, float radius)
    {
        // Nobi radius is 1:1 with the VISUAL radius — do NOT halve or double it.
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
}
#endif

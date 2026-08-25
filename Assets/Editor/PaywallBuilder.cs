#if UNITY_EDITOR
using System.Collections.Generic;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds Screen_Paywall — the full-screen slide-in paywall overlay (Task 14a,
/// spec §6 «Пейволл», mockup «paywall_v2_dark_unified_features»).
///
/// Idempotent delete-and-rebuild of OUR OWN subtree only: the screen is destroyed by
/// name and re-created, then re-seated in ScreenContainer AFTER Screen_New/Screen_Onboarding
/// and BEFORE the auth pages (the same rule NavRestructureBuilder.ReorderScreens encodes,
/// which this task extended with "Screen_Paywall"). Nothing outside Screen_Paywall is
/// touched, so the owner's hand-tuning elsewhere in Main.unity is safe.
///
/// Every colour is a ThemedColor binding on a semantic role — no literals — except the two
/// period-segment labels, whose colour is state-dependent and therefore owned at runtime by
/// PaywallController (two owners would fight over the active segment).
///
/// All sizes are 1080×1920 canvas reference units. Save the scene after running
/// (the headless entry saves itself).
/// </summary>
public static class PaywallBuilder
{
    // ── Design tokens (reference units) ──────────────────────────────────────
    private const float TopBarHeight = 300f;      // safe area baked in, matches every other screen
    private const float Gutter = 44f;
    private const float CardRadius = 42f;
    private const float CardPadding = 48f;
    private const float CardGap = 40f;
    private const float RingWidth = 6f;
    // 132 = the house touch-target floor (44dp x 3). The 6u track padding makes each
    // segment 120 tall, still at/above the floor. The «до −17%» ribbon stays 48.
    private const float ToggleHeight = 132f;
    private const float CtaHeight = 132f;
    // Cta 132 + FinePrint 40 + Restore 120 + 2x20 spacing + 32 top + 96 bottom (home-bar
    // safe area) = 460. Re-derive this if any of those change, or the bar clips its own rows.
    private const float BottomBarHeight = 460f;
    private const float SwipeStripWidth = 150f;

    // Fonts by GUID (the default font's weight table is empty — always assign explicitly).
    private const string RegularGuid = "e0cdfe2d6a51446bcba7d2df147e2415";
    private const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";
    private const string BoldGuid = "1cd715823fef34be4a3d3f3c5572594c";

    private const string ChevronLeftPath = "Assets/Images/Chat/chevron-left.png";
    // WHITE tick on transparency (opening the PNG looks blank — that is the glyph, not a
    // missing asset), so it takes a PositiveInk tint cleanly. «Tick Green.png» is the green
    // artwork used elsewhere at Color.white and would multiply into mud under a tint.
    private const string TickPath = "Assets/Images/Icons/Tick.png";

    private static TMP_FontAsset _regular, _semibold, _bold;
    private static Sprite _chevronLeft, _tick;
    private static readonly List<Component> _roundedToRefresh = new List<Component>();

    // ── Entry points ─────────────────────────────────────────────────────────

    [MenuItem("Tools/Billing/Build Paywall")]
    public static void Build()
    {
        var screen = BuildInternal();
        Selection.activeGameObject = screen;
        EditorSceneManager.MarkSceneDirty(screen.scene);
        Debug.Log("[PaywallBuilder] Build complete: Screen_Paywall rebuilt + PaywallController stamped. SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod PaywallBuilder.BuildHeadless -quit
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        BuildInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PaywallBuilder] Headless build + save complete: Screen_Paywall.");
    }

    // ── Main build ───────────────────────────────────────────────────────────

    private static GameObject BuildInternal()
    {
        LoadAssets();
        _roundedToRefresh.Clear();

        Transform container = ResolveScreenContainer();

        // Idempotent: destroy only the screen this builder owns, then rebuild it.
        DestroyAllByName(container, "Screen_Paywall");

        var screen = NewChild(container.gameObject, "Screen_Paywall", out var screenRt);
        StretchFill(screenRt);
        var bg = screen.AddComponent<Image>();
        bg.color = Color.white;
        bg.raycastTarget = true;   // an overlay must never leak taps to the screen behind it
        Themed(screen, ThemeRole.Background);

        var controller = screen.AddComponent<PaywallController>();

        // (1) Scroll column — full height; the bottom bar floats over its tail, which the
        //     content's bottom padding accounts for.
        var scroll = BuildScrollColumn(screen, out RectTransform contentRt);

        // (2) Content blocks, top to bottom.
        BuildHeaderBlock(contentRt.gameObject, out var headerTitle, out var headerSubline);
        var receipt = BuildReceiptBlock(contentRt.gameObject, out var receiptTiles);
        BuildPeriodToggle(contentRt.gameObject, out var monthParts, out var yearParts);
        var cards = BuildTierCards(contentRt.gameObject);
        BuildAllPlansBlock(contentRt.gameObject);

        // (3) Fixed chrome above the scroll.
        var bottomBar = BuildBottomBar(screen, out var ctaButton, out var ctaLabel,
            out var finePrint, out var restoreButton, out var restoreLabel);
        var closeButton = BuildTopBar(screen);
        var swipe = BuildSwipeStrip(screen, screenRt, scroll);

        // (4) Wire the controller.
        StampController(controller, scroll, headerTitle, headerSubline, closeButton, swipe,
            monthParts, yearParts, cards, ctaButton, ctaLabel, finePrint, restoreButton,
            restoreLabel, receipt, receiptTiles);

        // (5) Seat it after Screen_New/Screen_Onboarding, before the auth pages.
        SeatBeforeAuthScreens(container, screen.transform);

        // Radius bake needs sized rects — do it while the screen is still active, before
        // it is put away (Nobi refreshes again on OnEnable + OnRectTransformDimensionsChange,
        // so this is for the scene view, not for correctness at runtime).
        Canvas.ForceUpdateCanvases();
        foreach (var rounded in _roundedToRefresh)
            RefreshRounded(rounded);

        screen.SetActive(false);

        if (bottomBar == null) Debug.LogWarning("[PaywallBuilder] Bottom bar missing.");
        return screen;
    }

    private static Transform ResolveScreenContainer()
    {
        var container = FindInactiveByName("ScreenContainer");
        if (container != null) return container.transform;

        // Fall back to "the parent of a known screen" so a rename of the container
        // can't silently strand the paywall at the canvas root.
        var known = FindInactiveByName("Screen_New") ?? FindInactiveByName("Screen_Bots");
        if (known != null && known.transform.parent != null) return known.transform.parent;

        throw new System.InvalidOperationException(
            "[PaywallBuilder] ScreenContainer not found — is Main.unity open?");
    }

    private static GameObject FindInactiveByName(string name)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == name) return t.gameObject;
        return null;
    }

    // ── (1) Scroll column ────────────────────────────────────────────────────

    private static ScrollRect BuildScrollColumn(GameObject screen, out RectTransform contentRt)
    {
        var scrollGo = NewChild(screen, "ScrollView", out var scrollRt);
        StretchFill(scrollRt);
        scrollRt.offsetMax = new Vector2(0f, -TopBarHeight);
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

        var content = NewChild(viewport, "Content", out contentRt);
        SetAnchors(contentRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;
        AddVerticalGroup(content, new RectOffset((int)Gutter, (int)Gutter, 24, (int)(BottomBarHeight + 48f)),
            CardGap, TextAnchor.UpperCenter, expandHeight: false);
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRt;
        scroll.content = contentRt;
        return scroll;
    }

    // ── (2a) Header ──────────────────────────────────────────────────────────

    private static void BuildHeaderBlock(GameObject content,
        out TextMeshProUGUI title, out TextMeshProUGUI subline)
    {
        var block = NewChild(content, "HeaderBlock", out _);
        AddVerticalGroup(block, new RectOffset(0, 0, 0, 8), 14f, TextAnchor.UpperLeft, expandHeight: false);

        var titleGo = NewChild(block, "Title", out _);
        title = AddText(titleGo, PaywallRows.HeaderTitle, 50f, _bold, ThemeRole.InkPrimary);
        title.alignment = TextAlignmentOptions.TopLeft;
        title.lineSpacing = 4f;

        var sublineGo = NewChild(block, "Subline", out _);
        subline = AddText(sublineGo, PaywallRows.HeaderSubline, 38f, _regular, ThemeRole.InkSecondary);
        subline.alignment = TextAlignmentOptions.TopLeft;
    }

    // ── (2b) Value receipt (день-5) ──────────────────────────────────────────

    private static GameObject BuildReceiptBlock(GameObject content, out GameObject[] tiles)
    {
        var block = NewChild(content, "Receipt", out _);
        AddVerticalGroup(block, new RectOffset(0, 0, 0, 0), 24f, TextAnchor.UpperCenter, expandHeight: false);

        tiles = new GameObject[4];
        for (int row = 0; row < 2; row++)
        {
            var rowGo = NewChild(block, $"Row{row}", out _);
            AddHorizontalGroup(rowGo, new RectOffset(0, 0, 0, 0), 24f, TextAnchor.UpperLeft,
                expandWidth: true, expandHeight: true);
            for (int col = 0; col < 2; col++)
            {
                int i = row * 2 + col;
                tiles[i] = BuildStatTile(rowGo, $"Tile{i}");
            }
        }

        block.SetActive(false);   // Browse/limit triggers never show it
        return block;
    }

    private static GameObject BuildStatTile(GameObject parent, string name)
    {
        var tile = NewChild(parent, name, out _);
        var fill = tile.AddComponent<Image>();
        fill.color = Color.white;
        fill.raycastTarget = false;
        Themed(tile, ThemeRole.Surface);
        AddRounded(tile, 32f);
        AddVerticalGroup(tile, new RectOffset(32, 32, 28, 28), 4f, TextAnchor.UpperLeft, expandHeight: false);

        var valueGo = NewChild(tile, "Value", out _);
        var value = AddText(valueGo, PaywallRows.StatUnknown, 60f, _bold, ThemeRole.InkPrimary);
        value.alignment = TextAlignmentOptions.TopLeft;

        var labelGo = NewChild(tile, "Label", out _);
        var label = AddText(labelGo, "", 30f, _regular, ThemeRole.InkTertiary);
        label.alignment = TextAlignmentOptions.TopLeft;
        return tile;
    }

    // ── (2c) Period toggle ───────────────────────────────────────────────────

    private static void BuildPeriodToggle(GameObject content,
        out GameObject[] monthParts, out GameObject[] yearParts)
    {
        var toggle = NewChild(content, "PeriodToggle", out _);
        SetPreferredHeight(toggle, ToggleHeight);
        var trackImg = toggle.AddComponent<Image>();
        trackImg.color = Color.white;
        trackImg.raycastTarget = false;
        Themed(toggle, ThemeRole.Surface);
        AddRounded(toggle, ToggleHeight / 2f);
        AddHorizontalGroup(toggle, new RectOffset(6, 6, 6, 6), 0f, TextAnchor.MiddleCenter,
            expandWidth: true, expandHeight: true);

        monthParts = BuildSegment(toggle, "MonthSegment", PaywallRows.PeriodMonth);
        yearParts = BuildSegment(toggle, "YearSegment", PaywallRows.PeriodYear);
        // Default period is Month (PaywallController.Open resets to it) — match that in the
        // scene so the un-run screen doesn't read as "both segments selected".
        yearParts[1].SetActive(false);

        // «до −17%» ribbon straddling the Год half's top edge. Deliberately NOT inside the
        // segment: over the selected segment's AccentFill, PositiveInk would fail contrast.
        var badge = NewChild(toggle, "YearSavingBadge", out var badgeRt);
        IgnoreLayout(badge);
        SetAnchors(badgeRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f));
        badgeRt.anchoredPosition = new Vector2(-40f, 0f);
        badgeRt.sizeDelta = new Vector2(230f, 48f);
        var badgeImg = badge.AddComponent<Image>();
        badgeImg.color = Color.white;
        badgeImg.raycastTarget = false;
        Themed(badge, ThemeRole.PositiveBg);
        AddRounded(badge, 24f);
        var badgeLabelGo = NewChild(badge, "Label", out var badgeLabelRt);
        StretchFill(badgeLabelRt);
        var badgeLabel = AddText(badgeLabelGo, PaywallCopy.YearSavingBadge, 26f, _semibold, ThemeRole.PositiveInk);
        badgeLabel.alignment = TextAlignmentOptions.Center;
    }

    /// <summary>Returns {segmentRoot, fill, label} — the controller needs the last two.</summary>
    private static GameObject[] BuildSegment(GameObject parent, string name, string text)
    {
        var seg = NewChild(parent, name, out _);
        // Alpha-0 hit target IS the Button's targetGraphic (ItemEditSheet rule): it must stay
        // enabled + raycastable, with transition None so uGUI never tints an invisible graphic.
        var hit = seg.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;
        var button = seg.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;

        var fill = NewChild(seg, "Fill", out var fillRt);
        StretchFill(fillRt);
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = Color.white;
        fillImg.raycastTarget = false;
        Themed(fill, ThemeRole.AccentFill);
        AddRounded(fill, (ToggleHeight - 12f) / 2f);

        var labelGo = NewChild(seg, "Label", out var labelRt);
        StretchFill(labelRt);
        // NO ThemedColor here on purpose — PaywallController.PaintPeriodLabels owns this colour.
        var label = AddText(labelGo, text, 40f, _semibold, null);
        label.alignment = TextAlignmentOptions.Center;

        return new[] { seg, fill, labelGo };
    }

    // ── (2d) Tier cards ──────────────────────────────────────────────────────

    private static GameObject[][] BuildTierCards(GameObject content)
    {
        var cards = new GameObject[PaywallRows.Order.Length][];
        for (int i = 0; i < PaywallRows.Order.Length; i++)
        {
            PaywallTierRow row = PaywallRows.Build(PaywallRows.Order[i], PaywallPeriod.Month);
            cards[i] = BuildTierCard(content, $"TierCard_{PaywallRows.Order[i]}", row);
        }
        return cards;
    }

    /// <summary>Returns {root, ring, popularBadge, crossBotRow, title, price, counts}.</summary>
    private static GameObject[] BuildTierCard(GameObject content, string name, PaywallTierRow row)
    {
        var card = NewChild(content, name, out _);
        AddVerticalGroup(card, new RectOffset((int)CardPadding, (int)CardPadding, (int)CardPadding, (int)CardPadding),
            14f, TextAnchor.UpperLeft, expandHeight: false);
        var button = card.AddComponent<Button>();
        button.transition = Selectable.Transition.None;

        // Selection ring FIRST so it draws behind the fill; ignoreLayout keeps the VLG off it.
        var ring = NewChild(card, "Ring", out var ringRt);
        IgnoreLayout(ring);
        StretchFill(ringRt, -RingWidth);
        var ringImg = ring.AddComponent<Image>();
        ringImg.color = Color.white;
        ringImg.raycastTarget = false;
        Themed(ring, ThemeRole.AccentFill);
        AddRounded(ring, CardRadius + RingWidth);

        var fill = NewChild(card, "Fill", out var fillRt);
        IgnoreLayout(fill);
        StretchFill(fillRt);
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = Color.white;
        fillImg.raycastTarget = true;   // the whole card is the tap target; the click bubbles to Button
        Themed(fill, ThemeRole.Surface);
        AddRounded(fill, CardRadius);
        button.targetGraphic = fillImg;

        // «Популярный» ribbon on the top edge.
        var badge = NewChild(card, "PopularBadge", out var badgeRt);
        IgnoreLayout(badge);
        SetAnchors(badgeRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f));
        badgeRt.anchoredPosition = new Vector2(-40f, 0f);
        badgeRt.sizeDelta = new Vector2(280f, 52f);
        var badgeImg = badge.AddComponent<Image>();
        badgeImg.color = Color.white;
        badgeImg.raycastTarget = false;
        Themed(badge, ThemeRole.AccentFill);
        AddRounded(badge, 26f);
        var badgeLabelGo = NewChild(badge, "Label", out var badgeLabelRt);
        StretchFill(badgeLabelRt);
        var badgeLabel = AddText(badgeLabelGo, PaywallRows.PopularBadge, 26f, _semibold, ThemeRole.AccentOnFill);
        badgeLabel.alignment = TextAlignmentOptions.Center;
        badge.SetActive(row.IsHighlighted);

        // Name + price row.
        var head = NewChild(card, "Head", out _);
        AddHorizontalGroup(head, new RectOffset(0, 0, 0, 0), 24f, TextAnchor.MiddleLeft,
            expandWidth: false, expandHeight: false);

        var titleGo = NewChild(head, "Name", out _);
        var title = AddText(titleGo, row.Title, 44f, _semibold, ThemeRole.InkPrimary);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        // The text column masks its real appetite (preferred 0 + flexible 1) so uGUI never
        // enters the shrink path that would squeeze the price — ItemCardB2 rule.
        var titleLe = titleGo.AddComponent<LayoutElement>();
        titleLe.minWidth = 0f;
        titleLe.preferredWidth = 0f;
        titleLe.flexibleWidth = 1f;

        var priceGo = NewChild(head, "Price", out _);
        var price = AddText(priceGo, row.PriceText, 42f, _bold, ThemeRole.AccentText);
        price.alignment = TextAlignmentOptions.MidlineRight;
        price.textWrappingMode = TextWrappingModes.NoWrap;   // a wrapped price is never right

        // Counts.
        var countsGo = NewChild(card, "Counts", out _);
        var counts = AddText(countsGo, row.CountsLine, 36f, _regular, ThemeRole.InkSecondary);
        counts.alignment = TextAlignmentOptions.TopLeft;

        // Cross-bot «Сводка» line (Бизнес/Сеть).
        var crossRow = NewChild(card, "CrossBotRow", out _);
        AddHorizontalGroup(crossRow, new RectOffset(0, 0, 8, 0), 16f, TextAnchor.MiddleLeft,
            expandWidth: false, expandHeight: false);
        var crossCheck = NewChild(crossRow, "Check", out _);
        SetFixedSize(crossCheck, 36f, 36f);
        AddIconImage(crossCheck, _tick, ThemeRole.PositiveInk);
        var crossLabelGo = NewChild(crossRow, "Label", out _);
        var crossLabel = AddText(crossLabelGo, PaywallRows.CrossBotLine, 36f, _regular, ThemeRole.InkSecondary);
        crossLabel.alignment = TextAlignmentOptions.MidlineLeft;
        var crossLe = crossLabelGo.AddComponent<LayoutElement>();
        crossLe.minWidth = 0f;
        crossLe.preferredWidth = 0f;
        crossLe.flexibleWidth = 1f;
        crossRow.SetActive(row.ShowCrossBotLine);

        ring.SetActive(row.IsHighlighted);   // Бизнес is preselected

        return new[] { card, ring, badge, crossRow, titleGo, priceGo, countsGo };
    }

    // ── (2e) «Во всех тарифах» ───────────────────────────────────────────────

    private static void BuildAllPlansBlock(GameObject content)
    {
        var overlineGo = NewChild(content, "AllPlansOverline", out _);
        var overline = AddText(overlineGo, PaywallRows.AllPlansOverline, 26f, _semibold, ThemeRole.InkTertiary);
        overline.alignment = TextAlignmentOptions.TopLeft;
        overline.characterSpacing = 8f;
        overline.margin = new Vector4(0f, 20f, 0f, 0f);

        var cardGo = NewChild(content, "AllPlansCard", out _);
        var fill = cardGo.AddComponent<Image>();
        fill.color = Color.white;
        fill.raycastTarget = false;
        Themed(cardGo, ThemeRole.Surface);
        AddRounded(cardGo, CardRadius);
        AddVerticalGroup(cardGo, new RectOffset((int)CardPadding, (int)CardPadding, (int)CardPadding, (int)CardPadding),
            28f, TextAnchor.UpperLeft, expandHeight: false);

        for (int i = 0; i < PaywallRows.AllPlansFeatures.Length; i++)
        {
            var rowGo = NewChild(cardGo, $"Feature{i}", out _);
            AddHorizontalGroup(rowGo, new RectOffset(0, 0, 0, 0), 24f, TextAnchor.UpperLeft,
                expandWidth: false, expandHeight: false);

            var check = NewChild(rowGo, "Check", out _);
            SetFixedSize(check, 48f, 48f);
            AddIconImage(check, _tick, ThemeRole.PositiveInk);

            var labelGo = NewChild(rowGo, "Label", out _);
            var label = AddText(labelGo, PaywallRows.AllPlansFeatures[i], 38f, _regular, ThemeRole.InkSecondary);
            label.alignment = TextAlignmentOptions.TopLeft;
            var le = labelGo.AddComponent<LayoutElement>();
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            le.flexibleWidth = 1f;
        }
    }

    // ── (3a) Bottom bar (thumb zone) ─────────────────────────────────────────

    private static GameObject BuildBottomBar(GameObject screen, out Button cta, out TextMeshProUGUI ctaLabel,
        out TextMeshProUGUI finePrint, out Button restore, out TextMeshProUGUI restoreLabel)
    {
        var bar = NewChild(screen, "BottomBar", out var barRt);
        SetAnchors(barRt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        barRt.offsetMin = Vector2.zero;
        barRt.offsetMax = new Vector2(0f, BottomBarHeight);
        var barBg = bar.AddComponent<Image>();
        barBg.color = Color.white;
        barBg.raycastTarget = true;   // scrolled content must never be tappable through the bar
        Themed(bar, ThemeRole.Background);

        var hairline = NewChild(bar, "Hairline", out var hairRt);
        IgnoreLayout(hairline);
        SetAnchors(hairRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        hairRt.offsetMin = new Vector2(0f, -2f);
        hairRt.offsetMax = Vector2.zero;
        var hairImg = hairline.AddComponent<Image>();
        hairImg.color = Color.white;
        hairImg.raycastTarget = false;
        Themed(hairline, ThemeRole.Hairline);

        AddVerticalGroup(bar, new RectOffset((int)Gutter, (int)Gutter, 32, 96), 20f,
            TextAnchor.UpperCenter, expandHeight: false);

        var ctaGo = NewChild(bar, "Cta", out _);
        SetPreferredHeight(ctaGo, CtaHeight);
        var ctaBg = ctaGo.AddComponent<Image>();
        ctaBg.color = Color.white;
        ctaBg.raycastTarget = true;
        Themed(ctaGo, ThemeRole.AccentFill);
        AddRounded(ctaGo, CardRadius);
        cta = ctaGo.AddComponent<Button>();
        cta.targetGraphic = ctaBg;
        // DELIBERATE exception to this screen's Transition.None house default: these two are the
        // only ACTIONS here (everything else is selection, which shows its own state), and both
        // go non-interactable while a purchase/restore is in flight — ColorTint is what makes
        // that dim visible. It works because each one's targetGraphic is a VISIBLE graphic: the
        // CTA's opaque fill here, and the label (not the alpha-0 hit area) on Restore below.
        cta.transition = Selectable.Transition.ColorTint;
        var ctaLabelGo = NewChild(ctaGo, "Label", out var ctaLabelRt);
        StretchFill(ctaLabelRt);
        ctaLabel = AddText(ctaLabelGo, PaywallCopy.TrialCta(), 44f, _semibold, ThemeRole.AccentOnFill);
        ctaLabel.alignment = TextAlignmentOptions.Center;

        var fineGo = NewChild(bar, "FinePrint", out _);
        SetPreferredHeight(fineGo, 40f);
        finePrint = AddText(fineGo, PaywallRows.FinePrint, 28f, _regular, ThemeRole.InkTertiary);
        finePrint.alignment = TextAlignmentOptions.Center;

        var restoreGo = NewChild(bar, "Restore", out _);
        SetPreferredHeight(restoreGo, 120f);   // house touch-target floor (44dp x 3)
        var restoreHit = restoreGo.AddComponent<Image>();
        restoreHit.color = new Color(0f, 0f, 0f, 0f);
        restoreHit.raycastTarget = true;
        restore = restoreGo.AddComponent<Button>();
        var restoreLabelGo = NewChild(restoreGo, "Label", out var restoreLabelRt);
        StretchFill(restoreLabelRt);
        restoreLabel = AddText(restoreLabelGo, PaywallRows.RestoreLabel, 36f, _semibold, ThemeRole.AccentText);
        restoreLabel.alignment = TextAlignmentOptions.Center;
        // targetGraphic is the LABEL, not the alpha-0 hit area: ColorTint multiplies into the
        // target's colour, so tinting a fully transparent graphic would produce no feedback at
        // all. The hit Image stays the raycast surface (full-row tap target); the label carries
        // the press + disabled look. Same reasoning as the CTA above.
        restore.targetGraphic = restoreLabel;
        restore.transition = Selectable.Transition.ColorTint;

        return bar;
    }

    // ── (3b) Top bar + back chevron ──────────────────────────────────────────

    private static Button BuildTopBar(GameObject screen)
    {
        var topBar = NewChild(screen, "TopBar", out var topRt);
        SetAnchors(topRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        topRt.offsetMin = new Vector2(0f, -TopBarHeight);
        topRt.offsetMax = Vector2.zero;
        var topImg = topBar.AddComponent<Image>();
        topImg.color = Color.white;
        topImg.raycastTarget = true;   // opaque chrome band, same ground as the page
        Themed(topBar, ThemeRole.Background);

        // Same geometry as ProfileSubPages/AddBotPanel: 120u target, 70/90 in from the corner.
        var backGo = NewChild(topBar, "BackButton", out var backRt);
        SetAnchors(backRt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
        backRt.anchoredPosition = new Vector2(70f, 90f);
        backRt.sizeDelta = new Vector2(120f, 120f);
        var backHit = backGo.AddComponent<Image>();
        backHit.color = new Color(0f, 0f, 0f, 0f);
        backHit.raycastTarget = true;
        var back = backGo.AddComponent<Button>();
        back.targetGraphic = backHit;
        back.transition = Selectable.Transition.None;
        var iconGo = NewChild(backGo, "Icon", out var iconRt);
        SetAnchors(iconRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        iconRt.sizeDelta = new Vector2(60f, 60f);
        AddIconImage(iconGo, _chevronLeft, ThemeRole.AccentText);
        return back;
    }

    // ── (3c) Left-edge swipe strip ───────────────────────────────────────────

    private static SwipeToBackPanel BuildSwipeStrip(GameObject screen, RectTransform screenRt, ScrollRect scroll)
    {
        var strip = NewChild(screen, "SwipeBack", out var stripRt);
        SetAnchors(stripRt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f));
        stripRt.anchoredPosition = new Vector2(SwipeStripWidth / 2f, 0f);
        stripRt.sizeDelta = new Vector2(SwipeStripWidth, 0f);
        var stripImg = strip.AddComponent<Image>();
        stripImg.color = new Color(1f, 1f, 1f, 0f);
        stripImg.raycastTarget = true;
        var swipe = strip.AddComponent<SwipeToBackPanel>();
        var swipeSo = new SerializedObject(swipe);
        swipeSo.FindProperty("panelToSlide").objectReferenceValue = screenRt;
        swipeSo.FindProperty("contentScrollRect").objectReferenceValue = scroll;
        swipeSo.ApplyModifiedPropertiesWithoutUndo();
        var passthrough = strip.AddComponent<ClickPassthrough>();
        passthrough.allowedPanel = screen.transform;
        var passSo = new SerializedObject(passthrough);
        passSo.FindProperty("deliverPressToAllBehind").boolValue = true;
        passSo.ApplyModifiedPropertiesWithoutUndo();
        strip.transform.SetAsLastSibling();
        return swipe;
    }

    // ── (4) Controller stamping ──────────────────────────────────────────────

    private static void StampController(PaywallController controller, ScrollRect scroll,
        TextMeshProUGUI headerTitle, TextMeshProUGUI headerSubline, Button closeButton,
        SwipeToBackPanel swipe, GameObject[] monthParts, GameObject[] yearParts,
        GameObject[][] cards, Button cta, TextMeshProUGUI ctaLabel, TextMeshProUGUI finePrint,
        Button restore, TextMeshProUGUI restoreLabel, GameObject receipt, GameObject[] receiptTiles)
    {
        var so = new SerializedObject(controller);
        so.FindProperty("scroll").objectReferenceValue = scroll;
        so.FindProperty("headerTitle").objectReferenceValue = headerTitle;
        so.FindProperty("headerSubline").objectReferenceValue = headerSubline;
        so.FindProperty("closeButton").objectReferenceValue = closeButton;
        so.FindProperty("swipeBack").objectReferenceValue = swipe;

        so.FindProperty("monthButton").objectReferenceValue = monthParts[0].GetComponent<Button>();
        so.FindProperty("yearButton").objectReferenceValue = yearParts[0].GetComponent<Button>();
        so.FindProperty("monthFill").objectReferenceValue = monthParts[1];
        so.FindProperty("yearFill").objectReferenceValue = yearParts[1];
        so.FindProperty("monthLabel").objectReferenceValue = monthParts[2].GetComponent<TextMeshProUGUI>();
        so.FindProperty("yearLabel").objectReferenceValue = yearParts[2].GetComponent<TextMeshProUGUI>();

        var cardsProp = so.FindProperty("tierCards");
        cardsProp.arraySize = cards.Length;
        for (int i = 0; i < cards.Length; i++)
        {
            var e = cardsProp.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("root").objectReferenceValue = cards[i][0];
            e.FindPropertyRelative("button").objectReferenceValue = cards[i][0].GetComponent<Button>();
            e.FindPropertyRelative("ring").objectReferenceValue = cards[i][1];
            e.FindPropertyRelative("popularBadge").objectReferenceValue = cards[i][2];
            e.FindPropertyRelative("crossBotRow").objectReferenceValue = cards[i][3];
            e.FindPropertyRelative("title").objectReferenceValue = cards[i][4].GetComponent<TextMeshProUGUI>();
            e.FindPropertyRelative("price").objectReferenceValue = cards[i][5].GetComponent<TextMeshProUGUI>();
            e.FindPropertyRelative("counts").objectReferenceValue = cards[i][6].GetComponent<TextMeshProUGUI>();
        }

        so.FindProperty("ctaButton").objectReferenceValue = cta;
        so.FindProperty("ctaLabel").objectReferenceValue = ctaLabel;
        so.FindProperty("finePrint").objectReferenceValue = finePrint;
        so.FindProperty("restoreButton").objectReferenceValue = restore;
        so.FindProperty("restoreLabel").objectReferenceValue = restoreLabel;

        so.FindProperty("receiptBlock").objectReferenceValue = receipt;
        var tilesProp = so.FindProperty("receiptTiles");
        tilesProp.arraySize = receiptTiles.Length;
        for (int i = 0; i < receiptTiles.Length; i++)
        {
            var e = tilesProp.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("value").objectReferenceValue =
                receiptTiles[i].transform.Find("Value").GetComponent<TextMeshProUGUI>();
            e.FindPropertyRelative("label").objectReferenceValue =
                receiptTiles[i].transform.Find("Label").GetComponent<TextMeshProUGUI>();
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
    }

    // ── (5) Sibling order ────────────────────────────────────────────────────

    /// <summary>
    /// Screen_Paywall must draw ABOVE the Add-Bot overlay (a bot/channel-limit gate fires
    /// while Screen_New is open) and BELOW the auth pages (which must always be able to
    /// come up over everything — the ScreenContainer invariant ReorderScreens encodes).
    /// </summary>
    private static void SeatBeforeAuthScreens(Transform container, Transform screen)
    {
        int target = -1;
        for (int i = 0; i < container.childCount; i++)
        {
            string n = container.GetChild(i).name;
            if (n == "WhatsappAuth" || n == "TelegramAuth") { target = i; break; }
        }

        if (target < 0)
        {
            Debug.LogWarning("[PaywallBuilder] No auth screen found under ScreenContainer — Screen_Paywall left last.");
            screen.SetAsLastSibling();
            return;
        }

        // The screen currently sits last; moving it to the auth index shifts auth down by one.
        screen.SetSiblingIndex(target);
    }

    // ── Assets ───────────────────────────────────────────────────────────────

    private static void LoadAssets()
    {
        _regular = LoadFont(RegularGuid);
        _semibold = LoadFont(SemiboldGuid);
        _bold = LoadFont(BoldGuid);
        _chevronLeft = LoadSprite(ChevronLeftPath);
        _tick = LoadSprite(TickPath);
    }

    private static TMP_FontAsset LoadFont(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null) Debug.LogWarning($"[PaywallBuilder] Font missing for GUID {guid}");
        return font;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[PaywallBuilder] Sprite missing: {path}");
        return sprite;
    }

    // ── Low-level helpers (ProfileSubPagesBuilder / NavRestructureBuilder idiom) ──

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

    private static void AddVerticalGroup(GameObject go, RectOffset padding, float spacing,
        TextAnchor alignment, bool expandHeight)
    {
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = padding;
        vlg.spacing = spacing;
        vlg.childAlignment = alignment;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = expandHeight;
    }

    private static void AddHorizontalGroup(GameObject go, RectOffset padding, float spacing,
        TextAnchor alignment, bool expandWidth, bool expandHeight)
    {
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = padding;
        hlg.spacing = spacing;
        hlg.childAlignment = alignment;
        hlg.childControlWidth = true;
        // childControlHeight TRUE everywhere: a group that sizes itself from a child's stale
        // sizeDelta ships permanently one layout pass behind (the bubble-row lesson).
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = expandWidth;
        hlg.childForceExpandHeight = expandHeight;
    }

    private static void IgnoreLayout(GameObject go) => go.AddComponent<LayoutElement>().ignoreLayout = true;

    private static void SetPreferredHeight(GameObject go, float height)
    {
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        le.flexibleHeight = 0f;
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
        // This project's TMP default is NO wrapping — without this, long lines render one
        // line tall and spill off-screen.
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.raycastTarget = false;
        if (role.HasValue) Themed(go, role.Value);
        return tmp;
    }

    private static void AddIconImage(GameObject go, Sprite sprite, ThemeRole role)
    {
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = Color.white;
        img.preserveAspect = true;
        img.raycastTarget = false;
        Themed(go, role);
    }

    private static void Themed(GameObject go, ThemeRole role)
    {
        var graphic = go.GetComponent<Graphic>();
        if (graphic == null)
        {
            Debug.LogWarning($"[PaywallBuilder] Themed('{go.name}') found no Graphic — binding skipped.");
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

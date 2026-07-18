#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds <c>Screen_Onboarding</c> — the first-run 3-slide welcome carousel — into
/// the shared ScreenContainer, following the <see cref="NavRestructureBuilder"/>
/// pattern (idempotent delete-and-rebuild, explicit font/anchor stamping, Image+sprite
/// icons, RoundedCorners, SerializedObject field stamping). It:
///
///   • builds a horizontal snap pager (viewport + 3-wide content) driven by the
///     Plan-02 <see cref="OnboardingPager"/> ScrollRect subclass (pageCount = 3);
///   • lays out 3 slides — hero composition + H1 title + body + a per-slide thumb-zone
///     CTA («Далее» on slides 1-2 wired to pager.GoToPage; «Создать бота» on slide 3
///     as OnboardingScreen.createBotButton) — no bypass affordance, the informative
///     slides advance only via those two CTAs (CONTEXT / spec §Locked constraint 2);
///   • adds a shared pinned dot-pill row;
///   • adds <see cref="OnboardingScreen"/> to the screen root and stamps its
///     pager / dots / createBotButton refs;
///   • stamps <see cref="BotsPage"/>.onboardingScreen = the new screen;
///   • reorders the container via the (internal, Onboarding-aware)
///     <see cref="NavRestructureBuilder.ReorderScreens"/> so the screen lands after
///     Screen_New and BEFORE the auth pages (auth stays LAST).
///
/// All sizes in 1080×1920 canvas reference units. Save the scene after running
/// (the headless entry saves automatically). Does NOT call NavRestructureBuilder.Build()
/// (it throws on the already-restructured scene).
/// </summary>
public static class OnboardingScreenBuilder
{
    // ── Design tokens (spec §Slides 1-3) ─────────────────────────────────────
    private const float CardRadius = 40f;
    private const float ButtonHeight = 150f;
    private const float PageMargin = 96f;
    private const float ContentWidth = 888f;        // 1080 − 2 × 96
    private const float BorderWidth = 3f;

    private static readonly Color Ink = Hex("#1A1A2E");
    private static readonly Color Muted = Hex("#65676B");
    private static readonly Color Primary = Hex("#1B7CEB");
    private static readonly Color Card = Color.white;
    private static readonly Color CardBorder = Hex("#E4E6EB");
    private static readonly Color BubbleIn = Hex("#F0F2F5");
    private static readonly Color BubbleOut = Hex("#D6E4FB");
    private static readonly Color ModeSelFill = Hex("#F4F8FE");
    private static readonly Color WaIconBg = Hex("#E8F8EE");
    private static readonly Color TgIconBg = Hex("#E7F3FB");
    private static readonly Color CheckGreen = Hex("#23A55A");
    private static readonly Color RadioOff = Hex("#C6CBD3");
    private static readonly Color DotInactive = new Color(0.106f, 0.486f, 0.922f, 0.30f);

    // Fonts by GUID (default font's weight table is empty — always assign explicitly).
    private const string RegularGuid = "e0cdfe2d6a51446bcba7d2df147e2415";
    private const string SemiboldGuid = "a2b0b38b6764047da9250bcff1b0f432";
    private const string BoldGuid = "1cd715823fef34be4a3d3f3c5572594c";

    private const string WhatsappIconPath = "Assets/Images/Icons/WhatsApp.svg.png";
    private const string TelegramIconPath = "Assets/Images/Icons/Telegram_2019_Logo.svg.png";
    private const string CheckIconPath = "Assets/Images/Icons/[CITYPNG.COM]HD Green Check True Tick Mark Icon Sign PNG - 3000x3000.png";

    private static TMP_FontAsset _regular, _semibold, _bold;
    private static Sprite _whatsapp, _telegram, _check;
    private static readonly List<Component> _roundedToRefresh = new List<Component>();

    // ── Entry points ─────────────────────────────────────────────────────────

    [MenuItem("Tools/Onboarding/Build")]
    public static void Build()
    {
        var screen = BuildInternal();
        Selection.activeGameObject = screen;
        EditorSceneManager.MarkSceneDirty(screen.scene);
        Debug.Log("[OnboardingScreenBuilder] Built Screen_Onboarding — SAVE THE SCENE (Cmd+S).");
    }

    // Headless entry (Editor closed):
    //   Unity -batchmode -nographics -projectPath . \
    //         -executeMethod OnboardingScreenBuilder.BuildHeadless -quit
    public static void BuildHeadless()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        BuildInternal();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[OnboardingScreenBuilder] Headless build + save complete");
    }

    // ── Main build ───────────────────────────────────────────────────────────

    private static GameObject BuildInternal()
    {
        AssetDatabase.Refresh();
        EnsureIconImportSettings();
        LoadAssets();
        _roundedToRefresh.Clear();

        // Resolve the shared ScreenContainer the same way NavRestructureBuilder does:
        // the parent of Screen_Bots (BotsPage's screen host).
        var botsPage = Object.FindFirstObjectByType<BotsPage>(FindObjectsInactive.Include);
        if (botsPage == null)
            throw new System.InvalidOperationException("BotsPage not found — is Main.unity open?");

        Transform container = ResolveScreenContainer(botsPage.transform);
        if (container == null)
            throw new System.InvalidOperationException(
                "Screen container not found — could not locate Screen_Bots' parent (Screen_* siblings).");

        // Idempotent teardown.
        DestroyAllByName(container, "Screen_Onboarding");

        // ── Screen root ──────────────────────────────────────────────────────
        var screen = NewChild(container.gameObject, "Screen_Onboarding", out var screenRt);
        StretchFill(screenRt);
        var screenBg = screen.AddComponent<Image>();
        screenBg.color = Card;
        screenBg.raycastTarget = true; // opaque overlay — blocks taps to whatever is behind

        // ── Pager (ScrollRect subclass) → Viewport → Content → 3 slides ──────
        var pagerGo = NewChild(screen, "Pager", out var pagerRt);
        StretchFill(pagerRt);
        var pager = pagerGo.AddComponent<OnboardingPager>();

        var viewportGo = NewChild(pagerGo, "Viewport", out var viewportRt);
        StretchFill(viewportRt);
        var viewportImg = viewportGo.AddComponent<Image>();
        viewportImg.color = new Color(1f, 1f, 1f, 0f); // transparent hit area for drags
        viewportImg.raycastTarget = true;
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = NewChild(viewportGo, "Content", out var contentRt);
        contentRt.anchorMin = new Vector2(0f, 0f);
        contentRt.anchorMax = new Vector2(0f, 1f);
        contentRt.pivot = new Vector2(0f, 0.5f);
        contentRt.sizeDelta = new Vector2(1080f * 3f, 0f);
        contentRt.anchoredPosition = Vector2.zero;

        // Wire the ScrollRect (Awake re-applies horizontal/clamped/no-inertia at runtime,
        // but content/viewport must be set here so the edit-time scene scrolls correctly).
        pager.content = contentRt;
        pager.viewport = viewportRt;
        pager.horizontal = true;
        pager.vertical = false;
        pager.movementType = ScrollRect.MovementType.Clamped;
        pager.inertia = false;
        var pagerSo = new SerializedObject(pager);
        pagerSo.FindProperty("pageCount").intValue = 3;
        pagerSo.ApplyModifiedPropertiesWithoutUndo();

        // ── Slides ───────────────────────────────────────────────────────────
        var slide0Cta = BuildSlide(contentGo, 0,
            "Бот отвечает клиентам за вас",
            "Круглосуточно, в WhatsApp и Telegram — на вашем обычном номере",
            "Далее", BuildValueHero);

        var slide1Cta = BuildSlide(contentGo, 1,
            "Вы решаете, сколько доверить",
            "Полный автопилот или подтверждение каждого ответа — можно менять в любой момент",
            "Далее", BuildControlHero);

        var createBotButton = BuildSlide(contentGo, 2,
            "Работает там, где ваши клиенты",
            "Подключите WhatsApp, Telegram или оба сразу — канал выберете при создании бота",
            "Создать бота", BuildChannelsHero);

        // ── Shared pinned dot row (rendered above the pager) ─────────────────
        var dots = BuildDotsRow(screen, out _);

        // ── OnboardingScreen controller + ref stamping ───────────────────────
        var controller = screen.AddComponent<OnboardingScreen>();
        var ctrlSo = new SerializedObject(controller);
        ctrlSo.FindProperty("pager").objectReferenceValue = pager;
        var dotsProp = ctrlSo.FindProperty("dots");
        dotsProp.arraySize = dots.Length;
        for (int i = 0; i < dots.Length; i++)
            dotsProp.GetArrayElementAtIndex(i).objectReferenceValue = dots[i];
        ctrlSo.FindProperty("createBotButton").objectReferenceValue = createBotButton;
        ctrlSo.ApplyModifiedPropertiesWithoutUndo();

        // «Далее» buttons advance the pager (persistent listeners survive serialization).
        UnityEventTools.AddIntPersistentListener(slide0Cta.onClick, pager.GoToPage, 1);
        UnityEventTools.AddIntPersistentListener(slide1Cta.onClick, pager.GoToPage, 2);

        // Stamp BotsPage.onboardingScreen so the gate can activate it.
        var botsSo = new SerializedObject(botsPage);
        botsSo.FindProperty("onboardingScreen").objectReferenceValue = screen;
        botsSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(botsPage);

        // Radius bake needs sized rects.
        Canvas.ForceUpdateCanvases();
        foreach (var rounded in _roundedToRefresh)
            RefreshRounded(rounded);

        // First-run overlay — start hidden; the gate activates it.
        screen.SetActive(false);

        // Deterministic order: after Screen_New, before the auth pages (auth LAST).
        NavRestructureBuilder.ReorderScreens(container);

        Debug.Log("[OnboardingScreenBuilder] Screen_Onboarding built: 3 slides, dots, CTAs; refs stamped; reordered.");
        return screen;
    }

    // Screen container = the shared parent of the Screen_* panels. Find Screen_Bots
    // among BotsPage's ancestors' siblings, then return its parent.
    private static Transform ResolveScreenContainer(Transform botsPage)
    {
        Transform t = botsPage;
        while (t != null)
        {
            if (t.name == "Screen_Bots" && t.parent != null)
                return t.parent;
            t = t.parent;
        }
        // Fallback: BotsPage may live under Screen_Bots directly.
        Transform p = botsPage;
        while (p != null)
        {
            if (p.parent != null && p.parent.Find("Screen_Bots") != null)
                return p.parent;
            p = p.parent;
        }
        return null;
    }

    // ── Slide construction ───────────────────────────────────────────────────

    private static Button BuildSlide(GameObject content, int index, string title, string body,
        string ctaLabel, System.Func<GameObject, GameObject> heroBuilder)
    {
        var slide = NewChild(content, $"Slide{index}", out var slideRt);
        slideRt.anchorMin = new Vector2(0f, 0f);
        slideRt.anchorMax = new Vector2(0f, 1f);
        slideRt.pivot = new Vector2(0f, 0.5f);
        slideRt.sizeDelta = new Vector2(1080f, 0f);
        slideRt.anchoredPosition = new Vector2(index * 1080f, 0f);
        var slideBg = slide.AddComponent<Image>();
        slideBg.color = new Color(1f, 1f, 1f, 0f); // full-slide drag hit area
        slideBg.raycastTarget = true;

        // Hero + title + body stack, centred and lifted above the thumb zone.
        var stack = NewChild(slide, "Stack", out var stackRt);
        SetAnchors(stackRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        stackRt.anchoredPosition = new Vector2(0f, 200f);
        stackRt.sizeDelta = new Vector2(ContentWidth, 0f);
        var vlg = stack.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 44f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = false;
        vlg.childForceExpandWidth = false;
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        stack.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        heroBuilder(stack);

        var titleGo = NewChild(stack, "Title", out var titleRt);
        titleRt.sizeDelta = new Vector2(ContentWidth, 0f);
        var titleTmp = AddText(titleGo, title, 50f, _bold, Ink);
        titleTmp.alignment = TextAlignmentOptions.Center;

        var bodyGo = NewChild(stack, "Body", out var bodyRt);
        bodyRt.sizeDelta = new Vector2(620f, 0f);
        var bodyTmp = AddText(bodyGo, body, 39f, _regular, Muted);
        bodyTmp.alignment = TextAlignmentOptions.Center;
        bodyTmp.lineSpacing = 6f;

        // Per-slide thumb-zone CTA.
        var cta = NewChild(slide, "Cta", out var ctaRt);
        SetAnchors(ctaRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        ctaRt.anchoredPosition = new Vector2(0f, 300f);
        ctaRt.sizeDelta = new Vector2(ContentWidth, ButtonHeight);
        var ctaBg = cta.AddComponent<Image>();
        ctaBg.color = Primary;
        AddRounded(cta, CardRadius);
        var ctaButton = cta.AddComponent<Button>();
        ctaButton.targetGraphic = ctaBg;
        var ctaLabelGo = NewChild(cta, "Label", out var ctaLabelRt);
        StretchFill(ctaLabelRt);
        var ctaLabelTmp = AddText(ctaLabelGo, ctaLabel, 44f, _semibold, Color.white);
        ctaLabelTmp.alignment = TextAlignmentOptions.Center;

        return ctaButton;
    }

    // ── Hero 1: mini chat mock (incoming Q → bot answer with price → typing) ──

    private static GameObject BuildValueHero(GameObject parent)
    {
        var hero = NewChild(parent, "Hero", out var heroRt);
        heroRt.sizeDelta = new Vector2(660f, 620f);
        SetPreferredSize(hero, 660f, 620f);

        // Incoming customer question (top-left).
        var inBubble = MakeBubble(hero, "Incoming", 540f, 140f, BubbleIn,
            new Vector2(0f, 1f), new Vector2(0f, 0f));
        var inText = AddPaddedText(inBubble, "Здравствуйте! Тормозные колодки на Camry 70 есть?",
            29f, _regular, Ink, new Vector2(22f, 18f));
        inText.alignment = TextAlignmentOptions.TopLeft;

        // Bot answer with a live price (right).
        var outBubble = MakeBubble(hero, "Outgoing", 566f, 232f, BubbleOut,
            new Vector2(1f, 1f), new Vector2(0f, -156f));
        var whoGo = NewChild(outBubble, "Who", out var whoRt);
        SetAnchors(whoRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        whoRt.offsetMin = new Vector2(22f, -46f);
        whoRt.offsetMax = new Vector2(-22f, -12f);
        var whoTmp = AddText(whoGo, "Бот-ассистент", 24f, _semibold, Primary);
        whoTmp.alignment = TextAlignmentOptions.TopLeft;
        var outBody = NewChild(outBubble, "Body", out var outBodyRt);
        SetAnchors(outBodyRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        outBodyRt.offsetMin = new Vector2(22f, -210f);
        outBodyRt.offsetMax = new Vector2(-22f, -52f);
        var outTmp = AddText(outBody, "Да, есть в наличии — 18 500 ₸. Оригинал и аналог. Какой подойдёт?",
            29f, _regular, Ink);
        outTmp.alignment = TextAlignmentOptions.TopLeft;

        // Typing indicator (right).
        var typing = MakeBubble(hero, "Typing", 150f, 76f, BubbleOut,
            new Vector2(1f, 1f), new Vector2(0f, -404f));
        var dotsRow = NewChild(typing, "Dots", out var dotsRowRt);
        SetAnchors(dotsRowRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        dotsRowRt.sizeDelta = new Vector2(90f, 20f);
        var hlg = dotsRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        for (int i = 0; i < 3; i++)
        {
            var dot = NewChild(dotsRow, $"Dot{i}", out var dotRt);
            dotRt.sizeDelta = new Vector2(16f, 16f);
            var dotImg = dot.AddComponent<Image>();
            dotImg.color = new Color(Primary.r, Primary.g, Primary.b, 0.4f + 0.2f * i);
            dot.AddComponent<LayoutElement>().preferredWidth = 16f;
            AddRounded(dot, 8f);
        }

        return hero;
    }

    // ── Hero 2: two mode cards (Авто selected / Вместе) ──────────────────────

    private static GameObject BuildControlHero(GameObject parent)
    {
        var hero = NewChild(parent, "Hero", out var heroRt);
        heroRt.sizeDelta = new Vector2(680f, 356f);
        SetPreferredSize(hero, 680f, 356f);

        BuildModeCard(hero, "AutoCard", 0f, true,
            "Авто", "Бот отвечает сам — вы видите каждый диалог");
        BuildModeCard(hero, "TogetherCard", -176f, false,
            "Вместе", "Бот предлагает варианты — отправляете вы");

        return hero;
    }

    private static void BuildModeCard(GameObject hero, string name, float topOffset, bool selected,
        string title, string desc)
    {
        var fill = MakeBorderedCard(hero, name, 680f, 160f,
            selected ? ModeSelFill : Card, selected ? Primary : CardBorder,
            new Vector2(0.5f, 1f), new Vector2(0f, topOffset));

        var titleGo = NewChild(fill, "Title", out var titleRt);
        SetAnchors(titleRt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        titleRt.anchoredPosition = new Vector2(30f, -26f);
        titleRt.sizeDelta = new Vector2(420f, 44f);
        var titleTmp = AddText(titleGo, title, 34f, _bold, Ink);
        titleTmp.alignment = TextAlignmentOptions.TopLeft;

        var descGo = NewChild(fill, "Desc", out var descRt);
        SetAnchors(descRt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        descRt.anchoredPosition = new Vector2(30f, -78f);
        descRt.sizeDelta = new Vector2(540f, 66f);
        var descTmp = AddText(descGo, desc, 27f, _regular, Muted);
        descTmp.alignment = TextAlignmentOptions.TopLeft;

        // Radio (right-centre): selected = filled Primary, unselected = light ring.
        var radio = NewChild(fill, "Radio", out var radioRt);
        SetAnchors(radioRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
        radioRt.anchoredPosition = new Vector2(-30f, 0f);
        radioRt.sizeDelta = new Vector2(40f, 40f);
        var radioImg = radio.AddComponent<Image>();
        radioImg.color = selected ? Primary : RadioOff;
        AddRounded(radio, 20f);
        if (selected)
        {
            var innerGo = NewChild(radio, "Inner", out var innerRt);
            SetAnchors(innerRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            innerRt.sizeDelta = new Vector2(16f, 16f);
            innerGo.AddComponent<Image>().color = Color.white;
            AddRounded(innerGo, 8f);
        }
        else
        {
            var innerGo = NewChild(radio, "Inner", out var innerRt);
            SetAnchors(innerRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            innerRt.sizeDelta = new Vector2(30f, 30f);
            innerGo.AddComponent<Image>().color = Card;
            AddRounded(innerGo, 15f);
        }
    }

    // ── Hero 3: WhatsApp + Telegram channel cards, both check-marked ─────────

    private static GameObject BuildChannelsHero(GameObject parent)
    {
        var hero = NewChild(parent, "Hero", out var heroRt);
        heroRt.sizeDelta = new Vector2(690f, 372f);
        SetPreferredSize(hero, 690f, 372f);

        BuildChannelCard(hero, "WhatsappCard", 0f, _whatsapp, WaIconBg,
            "WhatsApp", "Клиенты пишут на ваш номер");
        BuildChannelCard(hero, "TelegramCard", -186f, _telegram, TgIconBg,
            "Telegram", "Ваш личный аккаунт Telegram");

        return hero;
    }

    private static void BuildChannelCard(GameObject hero, string name, float topOffset,
        Sprite icon, Color iconBg, string title, string desc)
    {
        var fill = MakeBorderedCard(hero, name, 690f, 170f, Card, CardBorder,
            new Vector2(0.5f, 1f), new Vector2(0f, topOffset));

        // Icon square (Image+sprite, never a TMP glyph).
        var iconSquare = NewChild(fill, "IconSquare", out var iconSqRt);
        SetAnchors(iconSqRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        iconSqRt.anchoredPosition = new Vector2(30f, 0f);
        iconSqRt.sizeDelta = new Vector2(96f, 96f);
        iconSquare.AddComponent<Image>().color = iconBg;
        AddRounded(iconSquare, 24f);
        var logoGo = NewChild(iconSquare, "Logo", out var logoRt);
        StretchFill(logoRt, 20f);
        AddIconImage(logoGo, icon, Color.white);

        var titleGo = NewChild(fill, "Title", out var titleRt);
        SetAnchors(titleRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        titleRt.anchoredPosition = new Vector2(156f, 22f);
        titleRt.sizeDelta = new Vector2(360f, 44f);
        var titleTmp = AddText(titleGo, title, 33f, _bold, Ink);
        titleTmp.alignment = TextAlignmentOptions.Left;

        var descGo = NewChild(fill, "Desc", out var descRt);
        SetAnchors(descRt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        descRt.anchoredPosition = new Vector2(156f, -24f);
        descRt.sizeDelta = new Vector2(400f, 40f);
        var descTmp = AddText(descGo, desc, 26f, _regular, Muted);
        descTmp.alignment = TextAlignmentOptions.Left;

        // Green check (Image+sprite) at the right.
        var check = NewChild(fill, "Check", out var checkRt);
        SetAnchors(checkRt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
        checkRt.anchoredPosition = new Vector2(-30f, 0f);
        checkRt.sizeDelta = new Vector2(52f, 52f);
        AddIconImage(check, _check, _check != null ? Color.white : CheckGreen);
    }

    // ── Shared dot row (3 pills; controller elongates+tints the active one) ──

    private static RectTransform[] BuildDotsRow(GameObject screen, out GameObject rowGo)
    {
        rowGo = NewChild(screen, "Dots", out var rowRt);
        SetAnchors(rowRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
        rowRt.anchoredPosition = new Vector2(0f, 500f);
        rowRt.sizeDelta = new Vector2(300f, 40f);
        var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 30f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var dots = new RectTransform[3];
        for (int i = 0; i < 3; i++)
        {
            var dot = NewChild(rowGo, $"Dot{i}", out var dotRt);
            dotRt.sizeDelta = new Vector2(28f, 28f);
            var img = dot.AddComponent<Image>();
            img.color = i == 0 ? Primary : DotInactive; // controller re-applies on enable
            dot.AddComponent<LayoutElement>().preferredWidth = 28f;
            AddRounded(dot, 14f);
            dots[i] = dotRt;
        }
        return dots;
    }

    // ── Composite helpers ────────────────────────────────────────────────────

    // A border-backed rounded card; returns the inner Fill GameObject for content.
    private static GameObject MakeBorderedCard(GameObject parent, string name, float w, float h,
        Color fill, Color border, Vector2 anchor, Vector2 anchoredPos)
    {
        var root = NewChild(parent, name, out var rootRt);
        SetAnchors(rootRt, anchor, anchor, anchor);
        rootRt.sizeDelta = new Vector2(w, h);
        rootRt.anchoredPosition = anchoredPos;
        root.AddComponent<Image>().color = border;
        AddRounded(root, CardRadius);

        var fillGo = NewChild(root, "Fill", out var fillRt);
        StretchFill(fillRt, BorderWidth);
        fillGo.AddComponent<Image>().color = fill;
        AddRounded(fillGo, CardRadius - BorderWidth);
        return fillGo;
    }

    // A rounded chat bubble anchored to the hero's top edge; caller adds text children.
    private static GameObject MakeBubble(GameObject parent, string name, float w, float h,
        Color bg, Vector2 anchor, Vector2 anchoredPos)
    {
        var bubble = NewChild(parent, name, out var rt);
        SetAnchors(rt, anchor, anchor, anchor);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = anchoredPos;
        bubble.AddComponent<Image>().color = bg;
        AddRounded(bubble, 28f);
        return bubble;
    }

    // Padded text filling its parent bubble (StretchFill with uniform inset margins).
    private static TextMeshProUGUI AddPaddedText(GameObject parent, string text, float size,
        TMP_FontAsset font, Color color, Vector2 pad)
    {
        var go = NewChild(parent, "Text", out var rt);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(pad.x, pad.y);
        rt.offsetMax = new Vector2(-pad.x, -pad.y);
        return AddText(go, text, size, font, color);
    }

    // ── Asset loading / import settings ─────────────────────────────────────

    private static void EnsureIconImportSettings() =>
        EnsureIconImportSettings(new[] { WhatsappIconPath, TelegramIconPath, CheckIconPath });

    private static void EnsureIconImportSettings(IEnumerable<string> paths)
    {
        foreach (string path in paths)
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
        _semibold = LoadFont(SemiboldGuid);
        _bold = LoadFont(BoldGuid);

        _whatsapp = LoadSprite(WhatsappIconPath);
        _telegram = LoadSprite(TelegramIconPath);
        _check = LoadSprite(CheckIconPath);
    }

    private static TMP_FontAsset LoadFont(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null) Debug.LogWarning($"[OnboardingScreenBuilder] Font missing for GUID {guid}");
        return font;
    }

    private static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogWarning($"[OnboardingScreenBuilder] Sprite missing: {path}");
        return sprite;
    }

    // ── Low-level helpers (verbatim NavRestructureBuilder idiom) ─────────────

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
}
#endif

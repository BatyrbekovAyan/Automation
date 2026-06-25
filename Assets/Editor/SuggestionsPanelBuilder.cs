using System.Collections.Generic;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// [MenuItem] builder that constructs the Reply Suggestions Panel (PANEL-01..06) and the
/// semi-auto top-bar toggle (SEMI-01) into Screen_Whatsapp/MessagesPanel of Main.unity, with
/// RoundedCorners surfaces and SerializedObject-wired refs. Build-time only; no networking.
/// Models on ChatsSearchBarBuilder (skeleton + wiring) and BotSwitcherSheetBuilder (RoundedCorners).
/// </summary>
public static class SuggestionsPanelBuilder
{
    private const string PanelName  = "SuggestionsPanel";
    private const string ToggleName = "SemiAutoToggle";
    private const string ToggleA11y = "Полуавтоматический режим"; // SEMI-01 accessible label
    private const string RefreshA11y = "Обновить";                // INT-03 accessible label

    // --- UI-SPEC colors -----------------------------------------------------
    private static readonly Color PanelSurface = Hex("#FFFFFF");
    private static readonly Color CardSurface  = Hex("#FAF8F3");
    private static readonly Color AccentGreen  = Hex("#25D366");
    private static readonly Color ChipFill     = Hex("#ECEFF1");
    private static readonly Color ChipLabel    = Hex("#54656F");
    private static readonly Color BodyText     = Hex("#111B21");
    private static readonly Color Secondary    = Hex("#54656F");
    private static readonly Color SkeletonBase = Hex("#ECECEC");
    private static readonly Color White        = Color.white;

    // --- Reference-unit sizes (1080×1920, dp×3) -----------------------------
    private const float Sm = 24f, Md = 48f, Lg = 72f;     // spacing tokens
    private const float CellHeight = 190f;         // 2×2 grid cell height
    private const float CellWidth = 480f, GridGap = 24f;   // 2 columns across the ~984u content width
    private const float ReplyMinHeight = 50f;      // min scroll height; the scroll flexes to fill the card
    private const float CardRadius = 24f, PanelTopRadius = 36f;
    private const float ReplySize = 38f, ChipSize = 26f, BadgeSize = 24f, StateSize = 39f;   // smaller for narrow grid cards
    private const float ToggleHit = 132f, RefreshHit = 120f;
    private const float PanelHeight = 620f;        // 2×2 grid: 2 rows (190) + gap + refresh row (~half the old 1100)

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

        // Idempotent re-run: delete any prior build, then construct fresh (no Undo grouping —
        // registering created objects for undo causes "dangling component" warnings on
        // post-create AddComponent; this is a delete-and-rebuild construction tool).
        Transform priorPanel = FindChildRecursive(host.transform, PanelName);
        if (priorPanel != null) Object.DestroyImmediate(priorPanel.gameObject);
        Transform priorToggle = FindChildRecursive(host.transform, ToggleName);
        if (priorToggle != null) Object.DestroyImmediate(priorToggle.gameObject);

        // Place the panel as a sibling of quickReplyPanel (above the composer, in the same render
        // layer as the messages/composer so it is NOT occluded), per UI-SPEC. Fall back to the
        // MessagesPanel host if quickReplyPanel is absent.
        Transform quickReply = FindChildRecursive(host.transform, "QuickReplyPanel");
        Transform panelParent = quickReply != null ? quickReply.parent : host.transform;

        BuildPanel(panelParent, quickReply);
        BuildToggle(topBar);

        EditorUtility.SetDirty(host);
        EditorSceneManager.MarkSceneDirty(host.scene);
        Debug.Log("SuggestionsPanelBuilder: built SuggestionsPanel + SemiAutoToggle.");
    }

    // === Panel ==============================================================

    private static void BuildPanel(Transform parent, Transform quickReplySibling)
    {
        // Sheet: white surface, top-rounded, slide root + fade group. Bottom-anchored, fixed footprint.
        GameObject panelGo = ImageGo(PanelName, parent, PanelSurface);
        var rt = (RectTransform)panelGo.transform;
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(0, PanelHeight);
        rt.anchoredPosition = new Vector2(0, 204f);     // above the composer (user-tuned)
        AddRoundedTop(panelGo, PanelTopRadius);
        var canvasGroup = panelGo.AddComponent<CanvasGroup>();

        // Render above the messages: place just after quickReplyPanel in its parent.
        if (quickReplySibling != null)
            panelGo.transform.SetSiblingIndex(quickReplySibling.GetSiblingIndex() + 1);

        // Refresh control — top-right corner of the sheet, fully inset so it never clips.
        Button refreshButton = BuildRefreshControl(panelGo.transform);

        // Cards container — 2×2 grid (revises D-04's vertical stack per owner; cards scroll internally
        // so they no longer need full width). Best-first reads top-left → Z-order.
        GameObject cards = Rect("CardsContainer", panelGo.transform);
        var crt = (RectTransform)cards.transform;
        crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
        crt.offsetMin = new Vector2(Md, Md);                       // left/bottom inset
        crt.offsetMax = new Vector2(-Md, -(RefreshHit + Md));      // right/top inset (clears refresh row)
        var grid = cards.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(CellWidth, CellHeight);
        grid.spacing = new Vector2(GridGap, GridGap);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.childAlignment = TextAnchor.UpperCenter;

        // 4 skeleton placeholders (D-12) + the card template (cardPrefab).
        var skeletons = new List<GameObject>();
        for (int i = 0; i < 4; i++) skeletons.Add(BuildSkeleton(cards.transform, i));
        SuggestionCard cardTemplate = BuildCard(cards.transform, isTemplate: true);

        // Empty + error states overlay the cards area (ignored by the layout group).
        GameObject empty = BuildEmptyState(panelGo.transform, crt);
        Button errorRetry;
        GameObject error = BuildErrorState(panelGo.transform, crt, out errorRetry);

        // Component + SerializedObject wiring (builders MUST rewire serialized consumers).
        var panel = panelGo.AddComponent<SuggestionsPanel>();
        var so = new SerializedObject(panel);
        so.FindProperty("cardsContainer").objectReferenceValue = cards.transform;
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

    private static Button BuildRefreshControl(Transform parent)
    {
        GameObject go = ImageGo("RefreshButton", parent, new Color(0, 0, 0, 0));
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1);
        rt.sizeDelta = new Vector2(RefreshHit, RefreshHit);
        rt.anchoredPosition = new Vector2(-Md, -Md);     // inset from the sheet's top-right corner
        // Icon = Image + sprite (null sprite => assign the circular-arrow sprite at the checkpoint).
        Image icon = ImageGo("Icon", go.transform, Secondary).GetComponent<Image>();
        var irt = (RectTransform)icon.transform; irt.sizeDelta = new Vector2(56, 56); Center(irt);
        Rect("A11y:" + RefreshA11y, go.transform);       // accessible label node
        return go.AddComponent<Button>();
    }

    // === Card ===============================================================

    private static SuggestionCard BuildCard(Transform parent, bool isTemplate)
    {
        GameObject card = ImageGo("SuggestionCard", parent, CardSurface);
        AddRounded(card, CardRadius);
        // The grid sizes the card to the cell — no LayoutElement needed.
        var vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 14, 14);   // tight for the narrow grid card
        vlg.spacing = 8;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;   // chip/badge hug; reply fills
        vlg.childControlWidth = true; vlg.childControlHeight = true;

        // Order: recommended badge (top, top card only — PANEL-03/D-07) → reply scroll (fills) → chip (bottom).
        GameObject badge = BuildBadge(card.transform);

        // Reply text in a fixed-height in-card scroller — a long reply scrolls (no truncation) so
        // the card/panel never stretches and the FULL text is readable before tapping (revises PANEL-06).
        TextMeshProUGUI reply = BuildReplyScroll(card.transform);

        // Intent chip — ONE muted tone for all intents (D-06), bottom-left.
        TextMeshProUGUI chipLabel = BuildChip(card.transform);

        var button = card.AddComponent<Button>();
        button.transition = Selectable.Transition.None;

        var comp = card.AddComponent<SuggestionCard>();
        var so = new SerializedObject(comp);
        so.FindProperty("cardButton").objectReferenceValue = button;
        so.FindProperty("replyText").objectReferenceValue = reply;
        so.FindProperty("intentLabel").objectReferenceValue = chipLabel;
        so.FindProperty("recommendedBadge").objectReferenceValue = badge;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (isTemplate) card.SetActive(false);   // template — instantiated per item at runtime
        return comp;
    }

    // Fixed-height vertical scroller holding the FULL reply text. Returns the inner TMP (the
    // SuggestionCard.replyText ref). Tap on the card still selects (Button bubbling); drag scrolls.
    private static TextMeshProUGUI BuildReplyScroll(Transform cardParent)
    {
        GameObject scrollGo = Rect("ReplyScroll", cardParent);
        var scrollLe = scrollGo.AddComponent<LayoutElement>();
        scrollLe.minHeight = ReplyMinHeight; scrollLe.flexibleHeight = 1f; scrollLe.flexibleWidth = 1f;   // fills the card
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        // Viewport masks the content; transparent raycast target so a drag scrolls.
        GameObject viewport = ImageGo("Viewport", scrollGo.transform, new Color(0, 0, 0, 0));
        Stretch((RectTransform)viewport.transform);
        viewport.GetComponent<Image>().raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        // Content = the reply TMP itself, top-anchored, sized to the full text by a ContentSizeFitter.
        TextMeshProUGUI reply = Text("ReplyText", viewport.transform, "—", ReplySize, BodyText,
            FontStyles.Normal, TextAlignmentOptions.TopLeft);
        reply.textWrappingMode = TextWrappingModes.Normal;
        reply.overflowMode = TextOverflowModes.Overflow;     // no ellipsis/cap — the scroll reveals overflow
        var rrt = (RectTransform)reply.transform;
        rrt.anchorMin = new Vector2(0, 1); rrt.anchorMax = new Vector2(1, 1); rrt.pivot = new Vector2(0.5f, 1);
        rrt.sizeDelta = new Vector2(0f, 0f);             // full viewport width (TMP defaults to 200 → it overflowed); height driven below
        rrt.anchoredPosition = Vector2.zero;
        reply.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = (RectTransform)viewport.transform;
        scroll.content = rrt;
        return reply;
    }

    private static TextMeshProUGUI BuildChip(Transform parent)
    {
        GameObject chip = ImageGo("IntentChip", parent, ChipFill);
        var le = chip.AddComponent<LayoutElement>(); le.minHeight = 44f; le.preferredHeight = 44f;
        AddRounded(chip, 22f);                          // radius ≈ half height => pill
        var hlg = chip.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(18, 18, 4, 4);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        chip.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        return Text("Label", chip.transform, "Цена", ChipSize, ChipLabel,
            FontStyles.Bold, TextAlignmentOptions.Center);
    }

    private static GameObject BuildBadge(Transform parent)
    {
        // Flow pill at the card top-left (hugs its text) — toggled on by SuggestionCard.Setup for
        // the top card only. In the narrow grid card a top-right overlay would cover the reply text,
        // so it lives in the layout flow above the reply instead.
        GameObject badge = ImageGo("RecommendedBadge", parent, AccentGreen);
        var le = badge.AddComponent<LayoutElement>(); le.minHeight = 36f; le.preferredHeight = 36f;
        AddRounded(badge, 18f);                          // pill
        var hlg = badge.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 16, 4, 4);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true; hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
        badge.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        Text("Label", badge.transform, "Рекомендуем", BadgeSize, White, FontStyles.Bold, TextAlignmentOptions.Center);
        return badge;
    }

    private static GameObject BuildSkeleton(Transform parent, int index)
    {
        GameObject sk = ImageGo("Skeleton" + index, parent, SkeletonBase);
        AddRounded(sk, CardRadius);
        var le = sk.AddComponent<LayoutElement>();
        le.minHeight = CellHeight; le.flexibleWidth = 1f;   // grid overrides size; harmless
        sk.AddComponent<CanvasGroup>();                 // shimmer target (panel pulses it)
        // Highlight bar inside the placeholder for the sweep cue.
        GameObject hi = ImageGo("Highlight", sk.transform, Hex("#F5F5F5"));
        var hrt = (RectTransform)hi.transform;
        hrt.anchorMin = new Vector2(0, 0.5f); hrt.anchorMax = new Vector2(0.6f, 0.5f); hrt.pivot = new Vector2(0, 0.5f);
        hrt.sizeDelta = new Vector2(0, 40); hrt.anchoredPosition = new Vector2(Md, 0);
        return sk;
    }

    // === Empty / Error overlays ============================================

    private static GameObject BuildEmptyState(Transform panel, RectTransform area)
    {
        GameObject go = Rect("EmptyState", panel);
        OverlayOver(go, area);
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter; vlg.spacing = Sm;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        Text("Heading", go.transform, "Нет предложений", StateSize, BodyText,
            FontStyles.Bold, TextAlignmentOptions.Center);
        Text("Body", go.transform, "Напишите ответ вручную", StateSize, Secondary,
            FontStyles.Normal, TextAlignmentOptions.Center);
        go.SetActive(false);
        return go;
    }

    private static GameObject BuildErrorState(Transform panel, RectTransform area, out Button retry)
    {
        GameObject go = Rect("ErrorState", panel);
        OverlayOver(go, area);
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter; vlg.spacing = Sm;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true; vlg.childControlHeight = true;
        Text("Heading", go.transform, "Не удалось загрузить", StateSize, BodyText,
            FontStyles.Bold, TextAlignmentOptions.Center);
        Text("Body", go.transform, "Проверьте соединение и попробуйте снова", StateSize, Secondary,
            FontStyles.Normal, TextAlignmentOptions.Center);

        GameObject retryGo = ImageGo("RetryButton", go.transform, ChipFill);
        var le = retryGo.AddComponent<LayoutElement>(); le.minHeight = RefreshHit; le.minWidth = 240f;
        AddRounded(retryGo, 28f);
        TextMeshProUGUI rt = Text("Label", retryGo.transform, "Обновить", ChipSize, ChipLabel,
            FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch((RectTransform)rt.transform);
        retry = retryGo.AddComponent<Button>();
        go.SetActive(false);
        return go;
    }

    // === Toggle (open-chat top bar) ========================================

    private static void BuildToggle(Transform topBar)
    {
        GameObject go = Rect(ToggleName, topBar);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(1, 0.5f); rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f);
        rt.sizeDelta = new Vector2(ToggleHit, ToggleHit);
        rt.anchoredPosition = new Vector2(-Md, -40f);   // right side of the header; tune at checkpoint

        Image icon = ImageGo("Icon", go.transform, Secondary).GetComponent<Image>();
        var irt = (RectTransform)icon.transform; irt.sizeDelta = new Vector2(64, 64); Center(irt);
        Rect("A11y:" + ToggleA11y, go.transform);       // accessible label node

        var button = go.AddComponent<Button>();
        button.transition = Selectable.Transition.None;

        var comp = go.AddComponent<SemiAutoToggle>();
        var so = new SerializedObject(comp);
        so.FindProperty("toggleButton").objectReferenceValue = button;
        so.FindProperty("iconImage").objectReferenceValue = icon;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // === Helpers ============================================================

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

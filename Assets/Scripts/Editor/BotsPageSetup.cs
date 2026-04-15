// ============================================================
//  BotsPageSetup.cs  (Editor-only)
//
//  Menu:  Tools > Setup My Bots Page
//
//  Canvas reference resolution: 1080 × 1920
//  All dimensions are pre-multiplied for the 1080-wide canvas.
//  Design reference: Design/mockup.html — PAGE 4 (My Bots).
//
//  Rebuilds TWO things:
//    1. The scene's Bots page (children of the GameObject holding BotsPage.cs).
//       External refs on BotsPage (MainPage, Chanel) are preserved.
//    2. The Bot prefab at Assets/Prefabs/Bot.prefab — the source Manager.cs
//       instantiates from for each bot.
//
//  Bot.cs contract preserved:
//    - Status (TMP) + ActivationSwitch (Toggle) are wired. ActivationSwitch
//      is parked off-canvas with the GetChild(0)/GetChild(0).GetChild(0)
//      hierarchy its SetSwitches() coroutine requires.
//    - EditButton is wired to the card-root Button (whole card = edit tap).
//    - DeleteButton / DeletePopup / DeleteConfirmButton / DeleteCancelButton
//      left unassigned (all null-guarded in Bot.cs).
//    - backgroundActiveColor / handleActiveColor preserved from the existing
//      prefab (if found) so toggle visuals don't regress.
//
//  BotsPage.cs contract:
//    - BotsParent, MainPageButton, NewBotButton re-wired.
//    - AllBotsButton, ActiveBotsButton left unassigned (filter UI removed).
//    - MainPage and Chanel preserved verbatim from the existing component.
// ============================================================

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class BotsPageSetup
{
    // ── Fonts ─────────────────────────────────────────────────────────────
    private const string FontRegularPath  = "Assets/TextMesh Pro/Fonts/SFProText-Regular SDF.asset";
    private const string FontBoldPath     = "Assets/TextMesh Pro/Fonts/SFProText-Bold SDF.asset";
    private const string FontMediumPath   = "Assets/TextMesh Pro/Fonts/SFProText-Medium SDF.asset";
    private const string FontSemiboldPath = "Assets/TextMesh Pro/Fonts/SFProText-Semibold SDF.asset";

    // ── Scale (design-pt × 2.77 ≈ canvas units at 1080-wide) ─────────────
    // All constants below are already pre-multiplied.

    // Page layout
    private const float StatusBarH       = 122f;   // ~44pt × S
    private const float HeaderH          = 139f;
    private const float DividerH         = 2f;
    private const float ListPadX         = 55f;    // ~20pt × S
    private const float ListPadY         = 44f;    // 16pt × S
    private const float CardGap          = 28f;    // 10pt × S

    // Header internals
    private const float HeaderPadX       = 55f;
    private const float HeaderIconBox    = 80f;
    private const float HeaderIconGap    = 30f;
    private const float FHeaderTitle     = 66f;    // 24pt bold
    private const float FHeaderIcon      = 52f;

    // Bot card
    private const float CardH            = 227f;   // 82pt × S  (50 icon + 16×2 padding)
    private const float CardPadX         = 44f;    // 16pt
    private const float CardPadY         = 44f;    // 16pt
    private const float CardInnerGap     = 39f;    // 14pt
    private const float CardRadius       = 39f;    // 14pt
    private const float IconSize         = 139f;   // 50pt
    private const float IconRadius       = 39f;    // 14pt
    private const float FIconEmoji       = 66f;    // 24pt
    private const float FBotName         = 44f;    // 16pt semibold
    private const float FBotDesc         = 36f;    // 13pt regular
    private const float PillH            = 66f;    // ~24pt
    private const float PillPadX         = 33f;    // 12pt
    private const float PillRadius       = 55f;    // 20pt (rounded enough to read as a pill)
    private const float FPillLabel       = 33f;    // 12pt semibold
    private const float ArrowW           = 44f;
    private const float FArrow           = 50f;

    // Status bar
    private const float FStatusTime      = 38f;    // 13pt semibold

    // ── Palette ───────────────────────────────────────────────────────────
    private static readonly Color ColBg          = Hex("#F2F2F7");
    private static readonly Color ColCard        = Color.white;
    private static readonly Color ColTextPrimary = Hex("#1C1C1E");
    private static readonly Color ColTextSec     = Hex("#8E8E93");
    private static readonly Color ColTextTert    = Hex("#C7C7CC");
    private static readonly Color ColBorder      = Hex("#E5E5EA");
    private static readonly Color ColIosBlue     = Hex("#007AFF");

    // Gradient midpoint used as the card-icon tile color (mockup uses real
    // CSS gradients; Unity UI doesn't natively gradient — a saturated blue
    // midpoint reads fine and can be upgraded later).
    private static readonly Color ColIconTile    = Hex("#2E9BE0");

    // Pill fallback colors — the live pill is driven by BotStatusPill at
    // runtime. These are just the default-state values baked into the prefab.
    private static readonly Color BgPillActive = Hex("#E8F8EE");
    private static readonly Color FgPillActive = Hex("#34C759");

    private const string BotPrefabPath = "Assets/Prefabs/Bot.prefab";
    private const string BusinessTypesAssetPath = "Assets/Data/BusinessTypes.asset";
    private const string BusinessIconsSpritesDir = "Assets/Images/BusinessIcons";

    // ── Round sprite ──────────────────────────────────────────────────────
    private static Sprite _roundSprite;
    private static Sprite RoundSprite
    {
        get
        {
            if (_roundSprite == null)
                _roundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            return _roundSprite;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    [MenuItem("Tools/Setup My Bots Page")]
    public static void Build()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null) { Debug.LogError("[BotsPageSetup] No Canvas found."); return; }

        var manager = Object.FindFirstObjectByType<Manager>(FindObjectsInactive.Include);
        if (manager == null) { Debug.LogError("[BotsPageSetup] No Manager found."); return; }

        // BotsPage GameObject is typically inactive by default — include inactive.
        var botsPage = Object.FindFirstObjectByType<BotsPage>(FindObjectsInactive.Include);
        if (botsPage == null)
        {
            // Fallback: pull it off Manager's serialized reference.
            var managerBotsPageField = new SerializedObject(manager).FindProperty("BotsPage");
            var mbpObj = managerBotsPageField.objectReferenceValue as GameObject;
            if (mbpObj != null) botsPage = mbpObj.GetComponent<BotsPage>();
        }
        if (botsPage == null)
        {
            Debug.LogError("[BotsPageSetup] No BotsPage component found in scene " +
                           "(including inactive objects and Manager.BotsPage). " +
                           "Add BotsPage.cs to the My Bots page GameObject and try again.");
            return;
        }

        if (!TryLoadFonts(out var fonts)) return;

        // Preserve external refs on BotsPage before we tear its children down.
        var botsPageSO = new SerializedObject(botsPage);
        var prevMainPage = botsPageSO.FindProperty("MainPage").objectReferenceValue;
        var prevChanel   = botsPageSO.FindProperty("Chanel").objectReferenceValue;

        // Preserve the parked toggle colors if the existing prefab has them.
        var oldColors = ReadPrefabToggleColors();

        // 1. Rebuild the scene page.
        var pageRoot = (RectTransform)botsPage.transform;
        ClearChildren(pageRoot);
        var built = BuildPageContent(pageRoot, fonts);

        // 2. Rebuild the Bot prefab asset.
        var botPrefab = BuildBotPrefab(fonts, oldColors.bgActive, oldColors.handleActive);

        // 3. Wire BotsPage serialized fields.
        botsPageSO.Update();
        botsPageSO.FindProperty("MainPage").objectReferenceValue      = prevMainPage;
        botsPageSO.FindProperty("Chanel").objectReferenceValue        = prevChanel;
        botsPageSO.FindProperty("BotsParent").objectReferenceValue    = built.botsParent.gameObject;
        botsPageSO.FindProperty("MainPageButton").objectReferenceValue = null;
        botsPageSO.FindProperty("AllBotsButton").objectReferenceValue  = null;
        botsPageSO.FindProperty("ActiveBotsButton").objectReferenceValue = null;
        botsPageSO.FindProperty("NewBotButton").objectReferenceValue  = built.newBotButton;
        botsPageSO.ApplyModifiedProperties();

        // 4. Wire Manager.BotPrefab + Manager.BotsParent + Manager.BotsPage.
        var managerSO = new SerializedObject(manager);
        managerSO.FindProperty("BotPrefab").objectReferenceValue  = botPrefab;
        managerSO.FindProperty("BotsParent").objectReferenceValue = built.botsParent.gameObject;
        managerSO.FindProperty("BotsPage").objectReferenceValue   = botsPage.gameObject;
        managerSO.ApplyModifiedProperties();

        EditorUtility.SetDirty(botsPage);
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[BotsPageSetup] Done — save the scene (Ctrl+S / Cmd+S).");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Page content (inside BotsPage GameObject)
    // ══════════════════════════════════════════════════════════════════════
    private struct PageBuild
    {
        public RectTransform botsParent;
        public Button newBotButton;
    }

    private static PageBuild BuildPageContent(RectTransform pageRoot, Fonts fonts)
    {
        // Ensure the page root has the right anchors + background.
        Stretch(pageRoot);
        var rootImg = pageRoot.gameObject.GetComponent<Image>() ?? pageRoot.gameObject.AddComponent<Image>();
        rootImg.color = ColBg;

        // No status bar — the device renders the OS one above our canvas.

        // Nav header at the top.
        var newBotBtn = BuildNavHeader(pageRoot, fonts);

        // Scrollable list fills the area between header and the external
        // bottom-nav sibling (not a child of this page).
        const float bottomNavInset = 215f; // ~78pt × S
        var scrollGo = MakeRect("ScrollContent", pageRoot);
        scrollGo.anchorMin = new Vector2(0, 0);
        scrollGo.anchorMax = new Vector2(1, 1);
        scrollGo.pivot     = new Vector2(0.5f, 0.5f);
        scrollGo.offsetMin = new Vector2(0, bottomNavInset);
        scrollGo.offsetMax = new Vector2(0, -HeaderH);
        scrollGo.gameObject.AddComponent<Image>().color = Color.clear;
        var scrollRect = scrollGo.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;

        var viewport = MakeRect("Viewport", scrollGo);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        scrollRect.viewport = viewport;

        var botsParent = MakeRect("BotsParent", viewport);
        botsParent.anchorMin = new Vector2(0, 1);
        botsParent.anchorMax = new Vector2(1, 1);
        botsParent.pivot     = new Vector2(0.5f, 1f);
        botsParent.anchoredPosition = Vector2.zero;
        botsParent.sizeDelta = Vector2.zero;

        var listVlg = botsParent.gameObject.AddComponent<VerticalLayoutGroup>();
        listVlg.childAlignment       = TextAnchor.UpperCenter;
        listVlg.childControlWidth    = true;
        listVlg.childForceExpandWidth = true;
        listVlg.childControlHeight   = false;
        listVlg.childForceExpandHeight = false;
        listVlg.spacing = CardGap;
        listVlg.padding = new RectOffset((int)ListPadX, (int)ListPadX, (int)ListPadY, (int)ListPadY);

        var listCsf = botsParent.gameObject.AddComponent<ContentSizeFitter>();
        listCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = botsParent;

        return new PageBuild { botsParent = botsParent, newBotButton = newBotBtn };
    }

    // ── Nav header (title + search + plus) ───────────────────────────────
    private static Button BuildNavHeader(RectTransform pageRoot, Fonts fonts)
    {
        var header = MakeRect("NavHeader", pageRoot);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot     = new Vector2(0.5f, 1f);
        header.sizeDelta = new Vector2(0, HeaderH);
        header.anchoredPosition = Vector2.zero;
        header.gameObject.AddComponent<Image>().color = ColCard;

        var hLine = MakeRect("Border", header);
        hLine.anchorMin = new Vector2(0, 0);
        hLine.anchorMax = new Vector2(1, 0);
        hLine.pivot = new Vector2(0.5f, 0f);
        hLine.sizeDelta = new Vector2(0, DividerH);
        hLine.anchoredPosition = Vector2.zero;
        hLine.gameObject.AddComponent<Image>().color = ColBorder;

        var title = MakeTMP("Title", header, fonts.bold, FHeaderTitle, ColTextPrimary, "Мои Боты");
        title.rectTransform.anchorMin = new Vector2(0, 0);
        title.rectTransform.anchorMax = new Vector2(1, 1);
        title.rectTransform.pivot = new Vector2(0, 0.5f);
        title.rectTransform.anchoredPosition = new Vector2(HeaderPadX, 0);
        title.rectTransform.sizeDelta = new Vector2(-HeaderPadX, 0);
        title.alignment = TextAlignmentOptions.MidlineLeft;

        // Right-side icon row: [🔍] [+]
        var iconRow = MakeRect("HeaderIcons", header);
        iconRow.anchorMin = new Vector2(1, 0);
        iconRow.anchorMax = new Vector2(1, 1);
        iconRow.pivot = new Vector2(1, 0.5f);
        iconRow.anchoredPosition = new Vector2(-HeaderPadX, 0);
        iconRow.sizeDelta = new Vector2(HeaderIconBox * 2 + HeaderIconGap, 0);
        var rowHlg = iconRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowHlg.childAlignment = TextAnchor.MiddleRight;
        rowHlg.childControlWidth = false; rowHlg.childForceExpandWidth = false;
        rowHlg.childControlHeight = false; rowHlg.childForceExpandHeight = false;
        rowHlg.spacing = HeaderIconGap;

        BuildIconButton("SearchButton", iconRow, fonts.semi, "⌕");
        var plusBtn = BuildIconButton("NewBotButton", iconRow, fonts.semi, "+");

        return plusBtn;
    }

    private static Button BuildIconButton(string name, RectTransform parent, TMP_FontAsset font, string glyph)
    {
        var go = MakeRect(name, parent);
        go.sizeDelta = new Vector2(HeaderIconBox, HeaderIconBox);
        var le = go.gameObject.AddComponent<LayoutElement>();
        le.minWidth = HeaderIconBox; le.preferredWidth = HeaderIconBox;
        le.minHeight = HeaderIconBox; le.preferredHeight = HeaderIconBox;

        var img = go.gameObject.AddComponent<Image>();
        img.color = Color.clear;
        var btn = go.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;

        var txt = MakeTMP("Glyph", go, font, FHeaderIcon, ColTextPrimary, glyph);
        Stretch(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Bot prefab
    // ══════════════════════════════════════════════════════════════════════
    private static GameObject BuildBotPrefab(Fonts fonts, Color bgActive, Color handleActive)
    {
        // Build a temp hierarchy off-scene, save as prefab, destroy temp.
        var temp = new GameObject("__tempBotCard", typeof(RectTransform));
        var tempRt = (RectTransform)temp.transform;

        var card = BuildBotCard(tempRt, fonts, bgActive, handleActive);
        // The saved prefab's root is `tempRt`; give it the card component
        // mirrors. Simpler: use `card.root` directly by reparenting card out
        // and saving card itself. Do that.
        card.root.SetParent(null, false);
        Object.DestroyImmediate(temp);

        var saved = PrefabUtility.SaveAsPrefabAsset(card.root.gameObject, BotPrefabPath);
        Object.DestroyImmediate(card.root.gameObject);
        return saved;
    }

    private struct CardRefs
    {
        public RectTransform root;
        public Bot botComponent;
    }

    private static CardRefs BuildBotCard(RectTransform parent, Fonts fonts, Color bgActive, Color handleActive)
    {
        // ── Card root ────────────────────────────────────────────────────
        var card = MakeRect("BotCard", parent);
        card.sizeDelta = new Vector2(0, CardH);
        var cardLE = card.gameObject.AddComponent<LayoutElement>();
        cardLE.minHeight = CardH; cardLE.preferredHeight = CardH;

        var cardImg = card.gameObject.AddComponent<Image>();
        cardImg.sprite = RoundSprite; cardImg.type = Image.Type.Sliced; cardImg.color = ColCard;
        // UISprite has ~10px rounded corners; softening pixels/unit scales
        // them up to read as a 14pt card radius at 1080-canvas scale.
        cardImg.pixelsPerUnitMultiplier = 0.25f;

        var cardBtn = card.gameObject.AddComponent<Button>();
        var cbc = cardBtn.colors;
        cbc.highlightedColor = new Color(0.97f, 0.97f, 0.97f);
        cardBtn.colors = cbc;

        // ── Horizontal row (icon | details | pill | arrow) ──────────────
        var row = MakeRect("Row", card);
        Stretch(row);
        var rowHlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        rowHlg.childAlignment = TextAnchor.MiddleLeft;
        // childControlWidth=true lets flexibleWidth on BotDetails absorb the
        // remaining space and lets the pill's CSF-driven preferred width
        // propagate back up.
        rowHlg.childControlWidth = true;   rowHlg.childForceExpandWidth = false;
        rowHlg.childControlHeight = true;  rowHlg.childForceExpandHeight = false;
        rowHlg.spacing = CardInnerGap;
        rowHlg.padding = new RectOffset((int)CardPadX, (int)CardPadX, (int)CardPadY, (int)CardPadY);

        // [0] Icon tile
        var iconRefs = BuildIconTile(row, fonts);

        // [1] Details (name + desc) — flexible width
        var detailsRefs = BuildDetails(row, fonts);

        // [2] Status pill
        var pillRefs = BuildStatusPill(row, fonts);

        // [3] Arrow
        BuildArrow(row, fonts);

        // ── Parked ActivationSwitch (outside the layout row) ────────────
        var parked = BuildParkedToggle(card, bgActive, handleActive);

        // ── Wire Bot component ──────────────────────────────────────────
        var botComp = card.gameObject.AddComponent<Bot>();
        var so = new SerializedObject(botComp);
        var botNameProp = so.FindProperty("BotName");
        if (botNameProp == null)
            Debug.LogError("[BotsPageSetup] Bot.cs has no serialized 'BotName' field — " +
                           "make sure Bot.cs has `[SerializeField] public TextMeshProUGUI BotName;`");
        else
            botNameProp.objectReferenceValue = detailsRefs.botName;
        var botDescProp = so.FindProperty("BotDesc");
        if (botDescProp != null) botDescProp.objectReferenceValue = detailsRefs.botDesc;
        so.FindProperty("Status").objectReferenceValue           = pillRefs.statusSource;
        so.FindProperty("EditButton").objectReferenceValue       = cardBtn;
        so.FindProperty("DeleteButton").objectReferenceValue     = null;
        so.FindProperty("ActivationSwitch").objectReferenceValue = parked.toggle;
        so.FindProperty("DeletePopup").objectReferenceValue      = null;
        so.FindProperty("DeleteConfirmButton").objectReferenceValue = null;
        so.FindProperty("DeleteCancelButton").objectReferenceValue  = null;
        so.FindProperty("backgroundActiveColor").colorValue = bgActive;
        so.FindProperty("handleActiveColor").colorValue     = handleActive;
        so.FindProperty("BotIconTile").objectReferenceValue   = iconRefs.tileBg;
        so.FindProperty("BotIconImage").objectReferenceValue  = iconRefs.iconImage;
        so.FindProperty("businessTypes").objectReferenceValue = EnsureBusinessTypesAsset();
        so.ApplyModifiedProperties();

        // Wire BotStatusPill observer component.
        var pillComp = pillRefs.pillRoot.gameObject.AddComponent<BotStatusPill>();
        var pillSO = new SerializedObject(pillComp);
        pillSO.FindProperty("background").objectReferenceValue   = pillRefs.background;
        pillSO.FindProperty("pillLabel").objectReferenceValue    = pillRefs.pillLabel;
        pillSO.FindProperty("statusSource").objectReferenceValue = pillRefs.statusSource;
        pillSO.ApplyModifiedProperties();

        return new CardRefs { root = card, botComponent = botComp };
    }

    private struct IconTileRefs
    {
        public Image tileBg;
        public Image iconImage;
    }

    private static IconTileRefs BuildIconTile(RectTransform row, Fonts fonts)
    {
        var icon = MakeRect("BotIcon", row);
        icon.sizeDelta = new Vector2(IconSize, IconSize);
        var iconLE = icon.gameObject.AddComponent<LayoutElement>();
        iconLE.minWidth = IconSize; iconLE.preferredWidth = IconSize;
        iconLE.minHeight = IconSize; iconLE.preferredHeight = IconSize;
        iconLE.flexibleWidth = 0;

        var tileBg = icon.gameObject.AddComponent<Image>();
        tileBg.sprite = RoundSprite; tileBg.type = Image.Type.Sliced; tileBg.color = ColIconTile;
        tileBg.pixelsPerUnitMultiplier = 0.25f;

        // Foreground icon — sized at 55% of the tile, centered, white tint.
        // Sprite is assigned at runtime by Bot.ApplyBusinessIcon().
        var iconImg = MakeRect("IconImage", icon);
        const float inner = IconSize * 0.55f;
        iconImg.anchorMin = new Vector2(0.5f, 0.5f);
        iconImg.anchorMax = new Vector2(0.5f, 0.5f);
        iconImg.pivot = new Vector2(0.5f, 0.5f);
        iconImg.anchoredPosition = Vector2.zero;
        iconImg.sizeDelta = new Vector2(inner, inner);
        var iconImage = iconImg.gameObject.AddComponent<Image>();
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        return new IconTileRefs { tileBg = tileBg, iconImage = iconImage };
    }

    private struct DetailsRefs
    {
        public TextMeshProUGUI botName;
        public TextMeshProUGUI botDesc;
    }

    private static DetailsRefs BuildDetails(RectTransform row, Fonts fonts)
    {
        var details = MakeRect("BotDetails", row);
        var detailsLE = details.gameObject.AddComponent<LayoutElement>();
        detailsLE.flexibleWidth = 1;
        detailsLE.minWidth = 200f;
        detailsLE.minHeight = IconSize; detailsLE.preferredHeight = IconSize;

        var detailsVlg = details.gameObject.AddComponent<VerticalLayoutGroup>();
        detailsVlg.childAlignment = TextAnchor.MiddleLeft;
        detailsVlg.childControlWidth    = true; detailsVlg.childForceExpandWidth  = true;
        detailsVlg.childControlHeight   = true; detailsVlg.childForceExpandHeight = false;
        detailsVlg.spacing = 8f;

        // Default text is empty — Manager overwrites BotName at instantiate
        // time; BotDesc has no writer, so leaving it empty matches the user's
        // expectation of no placeholder showing.
        var name = MakeTMP("BotName", details, fonts.semi, FBotName, ColTextPrimary, "");
        name.alignment = TextAlignmentOptions.MidlineLeft;
        name.enableWordWrapping = false;
        name.overflowMode = TextOverflowModes.Ellipsis;
        LE(name.rectTransform, FBotName + 16f);

        var desc = MakeTMP("BotDesc", details, fonts.regular, FBotDesc, ColTextSec, "");
        desc.alignment = TextAlignmentOptions.MidlineLeft;
        desc.enableWordWrapping = false;
        desc.overflowMode = TextOverflowModes.Ellipsis;
        LE(desc.rectTransform, FBotDesc + 16f);

        return new DetailsRefs { botName = name, botDesc = desc };
    }

    private struct PillRefs
    {
        public RectTransform pillRoot;
        public Image background;
        public TextMeshProUGUI pillLabel;
        public TextMeshProUGUI statusSource;
    }

    private static PillRefs BuildStatusPill(RectTransform row, Fonts fonts)
    {
        // Fixed-width pill sized to fit "Подключение" (longest label). Avoids
        // CSF/HLG layout fights with the parent row.
        const float pillW = 330f;

        var pill = MakeRect("StatusPill", row);
        pill.sizeDelta = new Vector2(pillW, PillH);
        var pillLE = pill.gameObject.AddComponent<LayoutElement>();
        pillLE.minWidth = pillW; pillLE.preferredWidth = pillW; pillLE.flexibleWidth = 0;
        pillLE.minHeight = PillH; pillLE.preferredHeight = PillH;

        var bg = pill.gameObject.AddComponent<Image>();
        bg.sprite = RoundSprite; bg.type = Image.Type.Sliced;
        bg.color = BgPillActive;
        // At PillH ≈ 66 units, 0.3 pixelsPerUnit scales UISprite's 10px corner
        // to ~33 units — fully rounded pill ends.
        bg.pixelsPerUnitMultiplier = 0.3f;

        var label = MakeTMP("Label", pill, fonts.semi, FPillLabel, FgPillActive, "Активен");
        Stretch(label.rectTransform);
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.margin = new Vector4(PillPadX, 0, PillPadX, 0);

        // Hidden Status TMP — legacy data channel Bot.cs writes color into.
        // Kept alive so Bot.cs writes succeed; `.enabled = false` hides render.
        // Ignored by the pill's HLG via ignoreLayout.
        var status = MakeTMP("Status", pill, fonts.regular, 1f, Color.green, "Active");
        status.rectTransform.anchorMin = new Vector2(0, 0);
        status.rectTransform.anchorMax = new Vector2(0, 0);
        status.rectTransform.sizeDelta = new Vector2(1f, 1f);
        status.rectTransform.anchoredPosition = Vector2.zero;
        status.enabled = false;
        var statusLE = status.rectTransform.gameObject.AddComponent<LayoutElement>();
        statusLE.ignoreLayout = true;

        return new PillRefs
        {
            pillRoot = pill,
            background = bg,
            pillLabel = label,
            statusSource = status
        };
    }

    private static void BuildArrow(RectTransform row, Fonts fonts)
    {
        var arrow = MakeRect("BotArrow", row);
        arrow.sizeDelta = new Vector2(ArrowW, 0);
        var arrowLE = arrow.gameObject.AddComponent<LayoutElement>();
        arrowLE.minWidth = ArrowW; arrowLE.preferredWidth = ArrowW;

        var txt = MakeTMP("Glyph", arrow, fonts.semi, FArrow, ColTextTert, "›");
        Stretch(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;
    }

    // ── Parked toggle (off-canvas, keeps Bot.cs SetSwitches() happy) ─────
    private struct ParkedToggle
    {
        public Toggle toggle;
    }

    private static ParkedToggle BuildParkedToggle(RectTransform card, Color bgActive, Color handleActive)
    {
        var toggleGo = MakeRect("ActivationSwitch", card);
        toggleGo.anchorMin = new Vector2(0, 0);
        toggleGo.anchorMax = new Vector2(0, 0);
        toggleGo.pivot = new Vector2(0, 0);
        toggleGo.sizeDelta = new Vector2(100f, 40f);
        toggleGo.anchoredPosition = new Vector2(-9999f, -9999f);
        toggleGo.localScale = Vector3.zero;
        var toggleLE = toggleGo.gameObject.AddComponent<LayoutElement>();
        toggleLE.ignoreLayout = true;

        // Child[0] = Background (non-zero width — Bot.cs divides by 160).
        var bg = MakeRect("Background", toggleGo);
        bg.anchorMin = new Vector2(0, 0.5f);
        bg.anchorMax = new Vector2(0, 0.5f);
        bg.pivot = new Vector2(0, 0.5f);
        bg.sizeDelta = new Vector2(100f, 40f);
        bg.anchoredPosition = Vector2.zero;
        var bgImg = bg.gameObject.AddComponent<Image>();
        bgImg.sprite = RoundSprite; bgImg.type = Image.Type.Sliced;
        // Starting color matches the default-active toggle state; Bot.cs's
        // SetSwitches coroutine will tween this into place.
        bgImg.color = bgActive;

        // Child[0].[0] = Handle.
        var handle = MakeRect("Handle", bg);
        handle.anchorMin = new Vector2(0.5f, 0.5f);
        handle.anchorMax = new Vector2(0.5f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(36f, 36f);
        handle.anchoredPosition = Vector2.zero;
        var handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.sprite = RoundSprite; handleImg.type = Image.Type.Sliced;
        handleImg.color = handleActive;

        var toggle = toggleGo.gameObject.AddComponent<Toggle>();
        toggle.transition = Selectable.Transition.None;
        toggle.targetGraphic = bgImg;
        toggle.graphic = handleImg;
        toggle.isOn = true;

        return new ParkedToggle { toggle = toggle };
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════

    private struct Fonts
    {
        public TMP_FontAsset regular;
        public TMP_FontAsset medium;
        public TMP_FontAsset semi;
        public TMP_FontAsset bold;
    }

    private static bool TryLoadFonts(out Fonts fonts)
    {
        var regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontRegularPath);
        var bold    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontBoldPath);
        var medium  = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontMediumPath);
        var semi    = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSemiboldPath);
        if (regular == null || bold == null)
        {
            Debug.LogError("[BotsPageSetup] Could not load SFProText fonts.");
            fonts = default;
            return false;
        }
        fonts = new Fonts
        {
            regular = regular,
            medium = medium ?? semi ?? bold,
            semi = semi ?? bold,
            bold = bold
        };
        return true;
    }

    private struct ToggleColors
    {
        public Color bgActive;
        public Color handleActive;
    }

    private static ToggleColors ReadPrefabToggleColors()
    {
        var defaults = new ToggleColors
        {
            bgActive = Hex("#34C759"),
            handleActive = Color.white
        };
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(BotPrefabPath);
        if (existing == null) return defaults;
        var bot = existing.GetComponent<Bot>();
        if (bot == null) return defaults;
        var so = new SerializedObject(bot);
        return new ToggleColors
        {
            bgActive = so.FindProperty("backgroundActiveColor").colorValue,
            handleActive = so.FindProperty("handleActiveColor").colorValue
        };
    }

    // ── BusinessTypes ScriptableObject bootstrap ─────────────────────────
    // Index order MUST match the legacy hand-wired BusinessTypesList so the
    // first run after the rename creates an asset whose entries align with
    // any pre-existing dev PlayerPrefs (best-effort; pre-launch).
    // 0 Car Service, 1 Cafe, 2 Beauty Salon, 3 Dentist,
    // 4 Real Estate, 5 Tour Agency, 6 Flowers.
    private static readonly (string id, string displayName, string fileName, Color tile)[] BusinessTypeDefaults =
    {
        ("car_service",  "Car Service",  "CarService.png",  Hex("#8E8E93")),
        ("cafe",         "Cafe",         "Cafe.png",        Hex("#FF9500")),
        ("beauty_salon", "Beauty Salon", "BeautySalon.png", Hex("#FF375F")),
        ("dentist",      "Dentist",      "Dentist.png",     Hex("#30B0C7")),
        ("real_estate",  "Real Estate",  "RealEstate.png",  Hex("#5856D6")),
        ("tour_agency",  "Tour Agency",  "TourAgency.png",  Hex("#32ADE6")),
        ("flowers",      "Flowers",      "Flowers.png",     Hex("#FF2D55")),
    };

    private static BusinessTypesSO EnsureBusinessTypesAsset()
    {
        // Make sure Assets/Data exists.
        if (!AssetDatabase.IsValidFolder("Assets/Data"))
            AssetDatabase.CreateFolder("Assets", "Data");

        var so = AssetDatabase.LoadAssetAtPath<BusinessTypesSO>(BusinessTypesAssetPath);
        if (so == null)
        {
            so = ScriptableObject.CreateInstance<BusinessTypesSO>();
            AssetDatabase.CreateAsset(so, BusinessTypesAssetPath);
        }

        var serialized = new SerializedObject(so);
        var entriesProp = serialized.FindProperty("entries");

        // Grow (never shrink) to defaults length so user-added entries are kept.
        if (entriesProp.arraySize < BusinessTypeDefaults.Length)
            entriesProp.arraySize = BusinessTypeDefaults.Length;

        for (int i = 0; i < BusinessTypeDefaults.Length; i++)
        {
            var (id, displayName, fileName, tile) = BusinessTypeDefaults[i];
            var elem = entriesProp.GetArrayElementAtIndex(i);
            var idProp          = elem.FindPropertyRelative("id");
            var displayNameProp = elem.FindPropertyRelative("displayName");
            var spriteProp      = elem.FindPropertyRelative("sprite");
            var colorProp       = elem.FindPropertyRelative("tileColor");

            // Fill empty id / displayName only — never clobber user edits.
            if (string.IsNullOrEmpty(idProp.stringValue))          idProp.stringValue          = id;
            if (string.IsNullOrEmpty(displayNameProp.stringValue)) displayNameProp.stringValue = displayName;

            // Always overwrite tile color with the default (the SO is the
            // source of truth, and the design owns the color).
            colorProp.colorValue = tile;

            // Only assign sprite if currently null AND the file exists by
            // convention. Never clobber a sprite the user manually wired.
            if (spriteProp.objectReferenceValue == null)
            {
                var path = $"{BusinessIconsSpritesDir}/{fileName}";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                    spriteProp.objectReferenceValue = sprite;
                else
                    Debug.LogWarning($"[BotsPageSetup] No sprite at {path} for index {i} — drop the PNG in and re-run the menu item.");
            }
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(so);
        AssetDatabase.SaveAssets();
        return so;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    private static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.layer = LayerMask.NameToLayer("UI");
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    private static TextMeshProUGUI MakeTMP(string name, RectTransform parent,
        TMP_FontAsset font, float size, Color color, string text)
    {
        var rt = MakeRect(name, parent);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = font; tmp.fontSize = size; tmp.color = color; tmp.text = text;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private static void LE(RectTransform rt, float height)
    {
        var le = rt.gameObject.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = height; le.preferredHeight = height;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}
#endif

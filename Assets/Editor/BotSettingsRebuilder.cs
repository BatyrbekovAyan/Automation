// Editor-only tool. Rebuilds Product.prefab, Service.prefab, and the
// contents of BotSettings.prefab to match the new component architecture
// (EditableField / ToggleRow / FocusScrim / ItemEditSheet / card views).
//
// Run via: Tools > Rebuild Bot Settings Prefabs
//
// SAFETY: commit everything before running. The tool modifies existing
// prefab assets in place. The WhatsappAuthorization, TelegramAuthorization,
// ConfirmChange* popups, and Saved GameObjects are preserved verbatim so
// the auth coroutines and Manager.SaveSettings logic stay wired. Any other
// top-level child of BotSettings.prefab (old General/Business/Product/
// Service/Prompt tab containers, old button-overlay fields, etc.) is
// destroyed and rebuilt from scratch.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Automation.BotSettingsUI;
using Nobi.UiRoundedCorners;

internal static class Sprites
{
    public static Sprite BackIcon;
    public static Sprite ChevronRight;
    public static Sprite ArrowDown;
    public static Sprite Plus;
    public static Sprite ToggleBg;
    public static Sprite ToggleHandle;
    public static Sprite Bin;

    public static void Load()
    {
        BackIcon     = Load("Assets/Images/Chat/chevron-left.png");
        ChevronRight = Load("Assets/Images/Chat/chevron-right.png");
        ArrowDown    = Load("Assets/Images/Icons/arrowDown.png");
        Plus         = Load("Assets/Images/Chat/Plus.png");
        ToggleBg     = Load("Assets/Images/Toggle/bg2.png");
        ToggleHandle = Load("Assets/Images/Toggle/handle.png");
        Bin          = Load("Assets/Images/Icons/Bin.png");
    }

    private static Sprite Load(string path)
    {
        var s = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (s == null) Debug.LogWarning($"[BotSettingsRebuilder.Sprites] Missing: {path}");
        return s;
    }
}

public static class BotSettingsRebuilder
{
    private const string ProductPrefabPath = "Assets/Prefabs/Product.prefab";
    private const string ServicePrefabPath = "Assets/Prefabs/Service.prefab";
    private const string BotSettingsPrefabPath = "Assets/Prefabs/BotSettings.prefab";

    // UI scale: mockup values are authored at iPhone points (390-wide);
    // the project's Canvas Scaler reference is wider (1080-ish). Multiply
    // every size/font/padding by this to get readable sizing at runtime.
    // Tweak this if the rebuild looks wrong.
    private const float S = 2.5f;
    private static float Sz(float v) => v * S;
    private static int Szi(float v) => Mathf.RoundToInt(v * S);
    private static Vector2 Sv(float x, float y) => new Vector2(x * S, y * S);

    // Design tokens (pulled from Design/mockup.html :root)
    private static readonly Color Bg         = Hex("#F0F2F5");
    private static readonly Color Card       = Hex("#FFFFFF");
    private static readonly Color Text       = Hex("#1A1A2E");
    private static readonly Color TextMuted  = Hex("#8E8E93");
    private static readonly Color Border     = Hex("#E4E6EB");
    private static readonly Color Primary    = Hex("#1B7CEB");
    private static readonly Color ToggleOff  = Hex("#E0E0E0");
    private static readonly Color Chevron    = Hex("#C7C7CC");
    private static readonly Color Danger       = Hex("#E53935");
    private static readonly Color PrimaryLight = Hex("#E8F2FD");

    // Strict allowlist of top-level names to preserve.
    // Everything else at root level gets destroyed.
    private static readonly string[] PreserveTopLevelNames =
    {
        "WhatsappAuthorization",
        "TelegramAuthorization",
        "ConfirmChangeWhatsappNumberPopup",
        "ConfirmChangeTelegramNumberPopup",
        "Saved",
    };

    [MenuItem("Tools/Rebuild Bot Settings Prefabs")]
    public static void RebuildAll()
    {
        if (!EditorUtility.DisplayDialog(
            "Rebuild Bot Settings Prefabs",
            "This will modify Product.prefab, Service.prefab, and BotSettings.prefab.\n\n" +
            "Make sure you have committed your current state to git so you can revert.\n\n" +
            "Continue?",
            "Rebuild", "Cancel"))
        {
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            Sprites.Load();
            BuildProductPrefab();
            BuildServicePrefab();
            BuildBotSettingsPrefab();
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog(
            "Rebuild Complete",
            "Product.prefab, Service.prefab, and BotSettings.prefab rebuilt.\n\n" +
            "Open Main.unity and enter Play mode to smoke-test.\n\n" +
            "If anything looks off, report back with the specific issue.",
            "OK");
    }

    // ============================================================
    // Product / Service prefabs
    // ============================================================

    private static void BuildProductPrefab() => BuildCardPrefab<ProductCardView>(ProductPrefabPath);
    private static void BuildServicePrefab() => BuildCardPrefab<ServiceCardView>(ServicePrefabPath);

    private static void BuildCardPrefab<T>(string path) where T : MonoBehaviour
    {
        var root = new GameObject("CardRoot");
        var rt = root.AddComponent<RectTransform>();
        rt.sizeDelta = Sv(0, 78);
        SetAnchors(rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));

        var bg = root.AddComponent<Image>();
        bg.color = Card;
        bg.raycastTarget = true;
        AddRoundedCorners(root, 10f);
        AddShadow(root);

        var button = root.AddComponent<Button>();

        var thumb = NewChild(root, "Thumb", out RectTransform thumbRt);
        var thumbImg = thumb.AddComponent<Image>();
        thumbImg.color = Bg;
        AddRoundedCorners(thumb, 10f);
        SetAnchors(thumbRt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
        thumbRt.sizeDelta = Sv(50, 50);
        thumbRt.anchoredPosition = Sv(16 + 25, 0);

        var info = NewChild(root, "Info", out RectTransform infoRt);
        SetAnchors(infoRt, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 0.5f));
        infoRt.offsetMin = Sv(78, 10);
        infoRt.offsetMax = Sv(-44, -10);

        var nameGo = NewChild(info, "Name", out RectTransform nameRt);
        var nameTmp = AddStyledText(nameGo, "Название", Sz(15), FontWeight.Bold, Text);
        SetAnchors(nameRt, new Vector2(0, 0.66f), new Vector2(1, 1), new Vector2(0, 0.5f));

        var priceGo = NewChild(info, "Price", out RectTransform priceRt);
        var priceTmp = AddStyledText(priceGo, "0 ₸", Sz(13), FontWeight.Bold, Primary);
        SetAnchors(priceRt, new Vector2(0, 0.33f), new Vector2(1, 0.66f), new Vector2(0, 0.5f));

        var descGo = NewChild(info, "Desc", out RectTransform descRt);
        var descTmp = AddStyledText(descGo, "Описание", Sz(12), FontWeight.Regular, TextMuted);
        SetAnchors(descRt, new Vector2(0, 0), new Vector2(1, 0.33f), new Vector2(0, 0.5f));

        var chevronGo = NewChild(root, "Chevron", out RectTransform chevRt);
        var chevImg = chevronGo.AddComponent<Image>();
        chevImg.sprite = Sprites.ChevronRight;
        chevImg.color = Chevron;
        chevImg.preserveAspect = true;
        SetAnchors(chevRt, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f));
        chevRt.sizeDelta = Sv(12, 12);
        chevRt.anchoredPosition = Sv(-16, 0);

        var cardView = root.AddComponent<T>();
        var so = new SerializedObject(cardView);
        so.FindProperty("nameLabel").objectReferenceValue = nameTmp;
        so.FindProperty("priceLabel").objectReferenceValue = priceTmp;
        so.FindProperty("descLabel").objectReferenceValue = descTmp;
        so.FindProperty("thumb").objectReferenceValue = thumbImg;
        so.FindProperty("rootButton").objectReferenceValue = button;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        Debug.Log($"[BotSettingsRebuilder] Saved {path}");
    }

    // ============================================================
    // BotSettings prefab
    // ============================================================

    private static void BuildBotSettingsPrefab()
    {
        var root = PrefabUtility.LoadPrefabContents(BotSettingsPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[BotSettingsRebuilder] Could not load {BotSettingsPrefabPath}");
            return;
        }

        try
        {
            var botSettings = root.GetComponent<BotSettings>();
            if (botSettings == null) botSettings = root.AddComponent<BotSettings>();

            var stashedDropdown = StashBusinessTypeDropdown(root.transform);

            // Force root RectTransform to fill its parent flush, regardless of
            // any prior offsets left on the prefab asset.
            var rootRt = root.transform as RectTransform;
            if (rootRt != null)
            {
                rootRt.anchorMin = Vector2.zero;
                rootRt.anchorMax = Vector2.one;
                rootRt.pivot = new Vector2(0.5f, 0.5f);
                rootRt.offsetMin = Vector2.zero;
                rootRt.offsetMax = Vector2.zero;
                rootRt.anchoredPosition3D = Vector3.zero;
                rootRt.localScale = Vector3.one;
            }

            // Allowlist preserve at direct-child level. Only exact-name matches
            // from PreserveTopLevelNames survive. We do NOT preserve containers
            // based on "contains a dropdown" — that was grafting the whole old
            // Pages container into the new General tab.
            var preserve = new HashSet<Transform>();
            var preserveSet = new HashSet<string>(PreserveTopLevelNames);

            for (int i = 0; i < root.transform.childCount; i++)
            {
                var child = root.transform.GetChild(i);
                if (preserveSet.Contains(child.name))
                    preserve.Add(child);
            }

            int destroyedCount = 0;
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (preserve.Contains(child)) continue;
                Debug.Log($"[BotSettingsRebuilder] Destroying top-level child: {child.name}");
                Object.DestroyImmediate(child.gameObject);
                destroyedCount++;
            }
            Debug.Log($"[BotSettingsRebuilder] Destroyed {destroyedCount} legacy top-level children. Preserved {preserve.Count}: {string.Join(", ", preserve.Select(t => t.name))}");

            // Clear all BotSettings serialized refs we're about to rebuild.
            var clearSo = new SerializedObject(botSettings);
            var fieldsToClear = new[] {
                "General", "Business", "Product", "Service", "Prompt",
                "GeneralTabButton", "BusinessTabButton", "ProductTabButton",
                "ServiceTabButton", "PromptTabButton",
                "mainScrim",
                "BotNameField", "whatsappRow", "telegramRow",
                "WhatsappNumberField", "TelegramNumberField",
                "BusinessField", "PromptField",
                "ProductsParent", "ServicesParent",
                "addProductButton", "addServiceButton",
                "productEditSheet", "serviceEditSheet",
            };
            foreach (var name in fieldsToClear)
            {
                var p = clearSo.FindProperty(name);
                if (p != null) p.objectReferenceValue = null;
            }
            clearSo.ApplyModifiedPropertiesWithoutUndo();

            // Build fresh structure.
            BuildHeader(root, out var backBtn, out var saveBtn);
            BuildTabBar(root, out var tabVisuals);
            var tabButtons = new[] { tabVisuals[0].button, tabVisuals[1].button, tabVisuals[2].button,
                                     tabVisuals[3].button, tabVisuals[4].button };
            var tabs = BuildTabs(root);
            var mainScrim = BuildFocusScrim(root, "MainFocusScrim");
            var productSheet = BuildItemEditSheet(root, "ProductEditSheet");
            var serviceSheet = BuildItemEditSheet(root, "ServiceEditSheet");

            // General tab content. Dropdown is a skeleton; options are
            // populated at runtime from BusinessTypesSO by Manager code.
            var generalFields = BuildGeneralTab(tabs["General"].content, mainScrim);

            // Re-parent the preserved dropdown into position 3 (after Section "ИНФОРМАЦИЯ" + BotName + the skeleton dropdown line).
            // BuildGeneralTab's VLG order: SectionHeader, BotNameField, (dropdown slot), SectionHeader, WhatsappToggle, ...
            AttachDropdownToGeneralTab(stashedDropdown, tabs["General"].content, siblingIndex: 3);

            // Remove the skeleton dropdown that BuildGeneralTab creates (it would duplicate the preserved one).
            var skeletonDropdownGo = FindDescendantInChildren(tabs["General"].content.transform, "BusinessTypeDropdown");
            if (stashedDropdown != null && skeletonDropdownGo != null && skeletonDropdownGo != stashedDropdown.gameObject)
            {
                Object.DestroyImmediate(skeletonDropdownGo);
            }

            // Ensure the serialized reference on BotSettings points at whichever dropdown survived.
            if (stashedDropdown != null)
                generalFields.businessTypeDropdown = stashedDropdown;

            var businessField = BuildBusinessOrPromptTab(tabs["Business"].content, "ОПИСАНИЕ БИЗНЕСА", "Описание", mainScrim);
            var promptField   = BuildBusinessOrPromptTab(tabs["Prompt"].content,   "ПРОМПТ",              "Промпт",    mainScrim);
            var productTabRefs = BuildProductOrServiceTab(tabs["Product"].content, isProducts: true);
            var serviceTabRefs = BuildProductOrServiceTab(tabs["Service"].content, isProducts: false);

            // Wire BotSettings serialized fields.
            var so = new SerializedObject(botSettings);
            so.FindProperty("General").objectReferenceValue  = tabs["General"].tab;
            so.FindProperty("Business").objectReferenceValue = tabs["Business"].tab;
            so.FindProperty("Product").objectReferenceValue  = tabs["Product"].tab;
            so.FindProperty("Service").objectReferenceValue  = tabs["Service"].tab;
            so.FindProperty("Prompt").objectReferenceValue   = tabs["Prompt"].tab;
            so.FindProperty("GeneralTabButton").objectReferenceValue  = tabButtons[0];
            so.FindProperty("BusinessTabButton").objectReferenceValue = tabButtons[1];
            so.FindProperty("ProductTabButton").objectReferenceValue  = tabButtons[2];
            so.FindProperty("ServiceTabButton").objectReferenceValue  = tabButtons[3];
            so.FindProperty("PromptTabButton").objectReferenceValue   = tabButtons[4];
            so.FindProperty("saveButton").objectReferenceValue = saveBtn;
            so.FindProperty("backButton").objectReferenceValue = backBtn;

            // Serialize per-tab visual refs so BotSettings.SetActiveTab can toggle them at runtime.
            var tabVisualsProp = so.FindProperty("tabVisuals");
            if (tabVisualsProp != null)
            {
                tabVisualsProp.arraySize = tabVisuals.Length;
                for (int i = 0; i < tabVisuals.Length; i++)
                {
                    var elem = tabVisualsProp.GetArrayElementAtIndex(i);
                    elem.FindPropertyRelative("label").objectReferenceValue = tabVisuals[i].label;
                    elem.FindPropertyRelative("underline").objectReferenceValue = tabVisuals[i].underline;
                }
            }

            so.FindProperty("mainScrim").objectReferenceValue = mainScrim;
            so.FindProperty("BotNameField").objectReferenceValue          = generalFields.botName;
            so.FindProperty("BusinessTypeDropdown").objectReferenceValue  = generalFields.businessTypeDropdown;
            so.FindProperty("whatsappRow").objectReferenceValue           = generalFields.whatsappRow;
            so.FindProperty("telegramRow").objectReferenceValue           = generalFields.telegramRow;
            so.FindProperty("WhatsappNumberField").objectReferenceValue   = generalFields.whatsappNumberField;
            so.FindProperty("TelegramNumberField").objectReferenceValue   = generalFields.telegramNumberField;
            so.FindProperty("BusinessField").objectReferenceValue = businessField;
            so.FindProperty("PromptField").objectReferenceValue   = promptField;

            var productPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProductPrefabPath);
            var servicePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ServicePrefabPath);
            so.FindProperty("ProductPrefab").objectReferenceValue = productPrefab;
            so.FindProperty("ServicePrefab").objectReferenceValue = servicePrefab;
            so.FindProperty("ProductsParent").objectReferenceValue = productTabRefs.parent;
            so.FindProperty("ServicesParent").objectReferenceValue = serviceTabRefs.parent;
            so.FindProperty("addProductButton").objectReferenceValue = productTabRefs.addButton;
            so.FindProperty("addServiceButton").objectReferenceValue = serviceTabRefs.addButton;
            so.FindProperty("productEditSheet").objectReferenceValue = productSheet;
            so.FindProperty("serviceEditSheet").objectReferenceValue = serviceSheet;

            // Auth-related refs: only fill in if null (preserve existing wiring).
            AssignByDescendantIfNull(so, root.transform, "WhatsappAuthorization");
            AssignByDescendantIfNull(so, root.transform, "TelegramAuthorization");
            AssignByDescendantIfNull(so, root.transform, "ConfirmChangeWhatsappNumberPopup");
            AssignByDescendantIfNull(so, root.transform, "ConfirmChangeTelegramNumberPopup");
            AssignByDescendantIfNull(so, root.transform, "Saved");
            AssignByDescendantIfNull(so, root.transform, "WhatsappCodeTimer");
            AssignByDescendantIfNull(so, root.transform, "TelegramCodeTimer");
            AssignByDescendantIfNull(so, root.transform, "WhatsappQRPanel");
            AssignByDescendantIfNull(so, root.transform, "WhatsappCodePanel");
            AssignByDescendantIfNull(so, root.transform, "TelegramQRPanel");
            AssignByDescendantIfNull(so, root.transform, "TelegramCodePanel");

            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, BotSettingsPrefabPath);
            Debug.Log($"[BotSettingsRebuilder] Saved {BotSettingsPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ============================================================
    // Sub-builders
    // ============================================================

    private static GameObject BuildHeader(GameObject root, out Button backBtn, out Button saveBtn)
    {
        var header = NewChild(root, "HeaderGroup", out RectTransform rt);
        var img = header.AddComponent<Image>();
        img.color = Card;
        SetAnchors(rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
        rt.sizeDelta = Sv(0, 56);
        rt.anchoredPosition = Vector2.zero;

        var backGo = NewChild(header, "BackButton", out RectTransform backRt);
        var backImg = backGo.AddComponent<Image>();
        backImg.sprite = Sprites.BackIcon;
        backImg.color = Primary;
        backImg.preserveAspect = true;
        backBtn = backGo.AddComponent<Button>();
        SetAnchors(backRt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
        backRt.sizeDelta = Sv(24, 24);
        backRt.anchoredPosition = Sv(16, 0);

        var title = NewChild(header, "Title", out RectTransform titleRt);
        var titleTmp = AddStyledText(title, "ShopBot", Sz(20), FontWeight.Bold, Text);
        titleTmp.alignment = TextAlignmentOptions.Center;
        SetAnchors(titleRt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        titleRt.offsetMin = Sv(60, 0);
        titleRt.offsetMax = Sv(-110, 0);

        var saveGo = NewChild(header, "SaveButton", out RectTransform saveRt);
        var saveImg = saveGo.AddComponent<Image>();
        saveImg.color = Primary;
        AddRoundedCorners(saveGo, 10f);
        saveBtn = saveGo.AddComponent<Button>();
        SetAnchors(saveRt, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f));
        saveRt.sizeDelta = Sv(92, 36);
        saveRt.anchoredPosition = Sv(-16 - 46, 0);

        var saveLabelGo = NewChild(saveGo, "Label", out RectTransform saveLabelRt);
        var saveTmp = AddStyledText(saveLabelGo, "Сохранить", Sz(14), FontWeight.Bold, Color.white);
        saveTmp.alignment = TextAlignmentOptions.Center;
        StretchFill(saveLabelRt);

        return header;
    }

    private static GameObject BuildTabBar(GameObject root, out TabVisualRefs[] tabVisuals)
    {
        var tabBar = NewChild(root, "TabBarGroup", out RectTransform rt);
        var img = tabBar.AddComponent<Image>();
        img.color = Card;
        SetAnchors(rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
        rt.sizeDelta = Sv(0, 44);
        rt.anchoredPosition = Sv(0, -56);

        var hlg = tabBar.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = Sz(4);
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        var labels = new[] { "Основное", "Бизнес", "Продукты", "Услуги", "Промпты" };
        tabVisuals = new TabVisualRefs[5];
        for (int i = 0; i < 5; i++)
        {
            var tab = NewChild(tabBar, "Tab_" + labels[i], out RectTransform _);
            var tabImg = tab.AddComponent<Image>();
            tabImg.color = new Color(0, 0, 0, 0);
            tabImg.raycastTarget = true;
            var btn = tab.AddComponent<Button>();

            var labelGo = NewChild(tab, "Label", out RectTransform labelRt);
            var tmp = AddStyledText(labelGo, labels[i], Sz(13), FontWeight.Bold,
                                    i == 0 ? Primary : TextMuted);
            tmp.alignment = TextAlignmentOptions.Center;
            StretchFill(labelRt);

            var underlineGo = NewChild(tab, "Underline", out RectTransform underlineRt);
            var underlineImg = underlineGo.AddComponent<Image>();
            underlineImg.color = Primary;
            SetAnchors(underlineRt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0));
            underlineRt.sizeDelta = Sv(0, 2);
            underlineRt.anchoredPosition = Vector2.zero;
            underlineGo.SetActive(i == 0);

            tabVisuals[i] = new TabVisualRefs
            {
                button = btn,
                label = tmp,
                underline = underlineGo,
            };
        }
        return tabBar;
    }

    private struct TabRoots { public GameObject tab; public GameObject content; }

    private static Dictionary<string, TabRoots> BuildTabs(GameObject root)
    {
        var map = new Dictionary<string, TabRoots>();
        // Business and Prompt host a single multi-line input that scrolls
        // internally via TMP_InputField + RectMask2D, so the tab itself
        // does not need a ScrollRect — the page stays static.
        var nonScrollableTabs = new HashSet<string> { "Business", "Prompt" };

        foreach (var name in new[] { "General", "Business", "Product", "Service", "Prompt" })
        {
            var tab = NewChild(root, name, out RectTransform rt);
            var img = tab.AddComponent<Image>();
            img.color = Bg;
            SetAnchors(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Sv(0, -100); // below header (56) + tabs (44)

            GameObject content;
            RectTransform contentRt;

            if (nonScrollableTabs.Contains(name))
            {
                // Content fills the tab directly — no ScrollRect, no viewport.
                content = NewChild(tab, "Content", out contentRt);
                StretchFill(contentRt);
            }
            else
            {
                // ScrollRect so long content is scrollable.
                var scroll = tab.AddComponent<ScrollRect>();
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Elastic;
                scroll.inertia = true;
                scroll.scrollSensitivity = Sz(20);

                var viewport = NewChild(tab, "Viewport", out RectTransform vpRt);
                StretchFill(vpRt);
                var vpImg = viewport.AddComponent<Image>();
                vpImg.color = new Color(1, 1, 1, 0.003f); // effectively invisible; Mask needs a Graphic
                var vpMask = viewport.AddComponent<Mask>();
                vpMask.showMaskGraphic = false;

                content = NewChild(viewport, "Content", out contentRt);
                SetAnchors(contentRt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
                contentRt.sizeDelta = Vector2.zero;
                contentRt.anchoredPosition = Vector2.zero;

                var fitter = content.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                scroll.viewport = vpRt;
                scroll.content = contentRt;
            }

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(Szi(20), Szi(20), Szi(24), Szi(24));
            vlg.spacing = Sz(12);
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;

            tab.SetActive(name == "General");
            map[name] = new TabRoots { tab = tab, content = content };
        }
        return map;
    }

    private struct GeneralTabRefs
    {
        public EditableField botName;
        public TMP_Dropdown businessTypeDropdown;
        public ToggleRow whatsappRow;
        public ToggleRow telegramRow;
        public EditableField whatsappNumberField;
        public EditableField telegramNumberField;
    }

    private static GeneralTabRefs BuildGeneralTab(GameObject tab, FocusScrim scrim)
    {
        AddSectionHeader(tab, "ИНФОРМАЦИЯ");
        var refs = new GeneralTabRefs();
        refs.botName = CreateEditableField(tab, "Имя бота", scrim, multiline: false);

        // Skeleton dropdown (replaced with preserved one if present).
        var ddGo = NewChild(tab, "BusinessTypeDropdown", out RectTransform ddRt);
        ddGo.AddComponent<Image>().color = Card;
        ddRt.sizeDelta = Sv(0, 56);
        refs.businessTypeDropdown = ddGo.AddComponent<TMP_Dropdown>();
        var ddLabel = NewChild(ddGo, "Label", out RectTransform ddLabelRt);
        AddStyledText(ddLabel, "Тип бизнеса", Sz(16), FontWeight.Medium, Text);
        StretchFill(ddLabelRt, new RectOffset(Szi(16), Szi(16), Szi(0), Szi(0)));

        AddSectionHeader(tab, "ПОДКЛЮЧЕНИЯ");
        refs.whatsappRow = CreateToggleRow(tab, "WhatsApp");
        refs.telegramRow = CreateToggleRow(tab, "Telegram");

        refs.whatsappNumberField = CreateEditableField(tab, "Номер WhatsApp", scrim, multiline: false);
        refs.whatsappNumberField.gameObject.SetActive(false);
        refs.telegramNumberField = CreateEditableField(tab, "Номер Telegram", scrim, multiline: false);
        refs.telegramNumberField.gameObject.SetActive(false);

        BuildDeleteBotButton(tab);

        return refs;
    }

    private static EditableTextArea BuildBusinessOrPromptTab(
        GameObject tab, string sectionTitle, string fieldLabel, FocusScrim scrim)
    {
        AddSectionHeader(tab, sectionTitle);
        var field = CreateEditableField(tab, fieldLabel, scrim, multiline: true);
        var go = field.gameObject;
        Object.DestroyImmediate(field);
        var textArea = go.AddComponent<EditableTextArea>();
        RewireEditableField(textArea, go, scrim);
        go.GetComponent<RectTransform>().sizeDelta = Sv(0, 240);
        return textArea;
    }

    private static Button BuildDeleteBotButton(GameObject parent)
    {
        var go = NewChild(parent, "DeleteBotButton", out RectTransform rt);
        var img = go.AddComponent<Image>();
        img.color = Danger;
        AddRoundedCorners(go, 14f);
        AddShadow(go);
        var btn = go.AddComponent<Button>();
        rt.sizeDelta = Sv(0, 56);

        var labelGo = NewChild(go, "Label", out RectTransform labelRt);
        var tmp = AddStyledText(labelGo, "Удалить бота", Sz(17), FontWeight.Bold, Color.white);
        tmp.alignment = TextAlignmentOptions.Center;
        StretchFill(labelRt);

        return btn;
    }

    private struct CardTabRefs
    {
        public RectTransform parent;
        public AddItemButton addButton;
    }

    private struct TabVisualRefs
    {
        public Button button;
        public TextMeshProUGUI label;
        public GameObject underline;
    }

    private static CardTabRefs BuildProductOrServiceTab(GameObject tab, bool isProducts)
    {
        AddSectionHeader(tab, isProducts ? "КАТАЛОГ ТОВАРОВ" : "КАТАЛОГ УСЛУГ");

        var listGo = NewChild(tab, isProducts ? "ProductsParent" : "ServicesParent", out RectTransform listRt);
        var listVlg = listGo.AddComponent<VerticalLayoutGroup>();
        listVlg.spacing = Sz(10);
        listVlg.childForceExpandHeight = false;
        listVlg.childControlHeight = false;
        listVlg.childForceExpandWidth = true;
        listVlg.childControlWidth = true;
        var fitter = listGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var addGo = NewChild(tab, isProducts ? "AddProductButton" : "AddServiceButton", out RectTransform addRt);
        var addImg = addGo.AddComponent<Image>();
        addImg.color = PrimaryLight;
        AddRoundedCorners(addGo, 10f);
        var addBtn = addGo.AddComponent<Button>();
        addRt.sizeDelta = Sv(0, 52);

        var rowGo = NewChild(addGo, "Row", out RectTransform rowRt);
        StretchFill(rowRt);
        var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = Sz(8);
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;

        var plusGo = NewChild(rowGo, "PlusIcon", out RectTransform plusRt);
        var plusImg = plusGo.AddComponent<Image>();
        plusImg.sprite = Sprites.Plus;
        plusImg.color = Primary;
        plusImg.preserveAspect = true;
        var plusLe = plusGo.AddComponent<LayoutElement>();
        plusLe.preferredWidth = Sz(20);
        plusLe.preferredHeight = Sz(20);

        var addLabelGo = NewChild(rowGo, "Label", out RectTransform addLabelRt);
        var addTmp = AddStyledText(addLabelGo, isProducts ? "Добавить товар" : "Добавить услугу",
                                   Sz(15), FontWeight.Bold, Primary);
        addTmp.alignment = TextAlignmentOptions.Left;
        var labelLe = addLabelGo.AddComponent<LayoutElement>();
        labelLe.preferredWidth = Sz(180);
        labelLe.preferredHeight = Sz(20);

        var addItemBtn = addGo.AddComponent<AddItemButton>();
        var so = new SerializedObject(addItemBtn);
        so.FindProperty("button").objectReferenceValue = addBtn;
        so.ApplyModifiedPropertiesWithoutUndo();

        return new CardTabRefs { parent = listRt, addButton = addItemBtn };
    }

    // ============================================================
    // Component factories
    // ============================================================

    private static EditableField CreateEditableField(GameObject parent, string label, FocusScrim scrim, bool multiline)
    {
        var go = NewChild(parent, "Field_" + label, out RectTransform rt);
        var cardImg = go.AddComponent<Image>();
        cardImg.color = Card;
        AddRoundedCorners(go, 10f);
        AddShadow(go);
        rt.sizeDelta = Sv(0, multiline ? 240 : 64);

        var labelGo = NewChild(go, "Label", out RectTransform labelRt);
        AddStyledText(labelGo, label, Sz(12), FontWeight.Medium, TextMuted);
        SetAnchors(labelRt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1));
        labelRt.sizeDelta = Sv(-32, 14);
        labelRt.anchoredPosition = Sv(16, -10);

        var inputGo = NewChild(go, "Input", out RectTransform inputRt);
        inputRt.anchorMin = new Vector2(0, 0);
        inputRt.anchorMax = new Vector2(1, 1);
        inputRt.offsetMin = Sv(16, 10);
        inputRt.offsetMax = Sv(-16, -26);
        var inputBg = inputGo.AddComponent<Image>();
        inputBg.color = new Color(0, 0, 0, 0);
        var input = inputGo.AddComponent<TMP_InputField>();

        var textArea = NewChild(inputGo, "Text Area", out RectTransform taRt);
        StretchFill(taRt);
        textArea.AddComponent<RectMask2D>();

        var placeholderGo = NewChild(textArea, "Placeholder", out RectTransform phRt);
        var placeholderTmp = AddStyledText(placeholderGo, label, Sz(16), FontWeight.Regular, TextMuted);
        placeholderTmp.fontStyle = FontStyles.Italic;
        StretchFill(phRt);

        var textGo = NewChild(textArea, "Text", out RectTransform textRt);
        var textTmp = AddStyledText(textGo, "", Sz(16), FontWeight.Medium, Text);
        StretchFill(textRt);

        input.textViewport = taRt;
        input.textComponent = textTmp;
        input.placeholder = placeholderTmp;
        input.targetGraphic = inputBg;
        input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        if (multiline)
        {
            textTmp.enableWordWrapping = true;
            placeholderTmp.enableWordWrapping = true;
        }

        var field = go.AddComponent<EditableField>();
        RewireEditableField(field, go, scrim);
        return field;
    }

    private static void RewireEditableField(EditableField field, GameObject go, FocusScrim scrim)
    {
        var labelTmp = go.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
        var input = go.GetComponentInChildren<TMP_InputField>();
        var so = new SerializedObject(field);
        so.FindProperty("labelText").objectReferenceValue = labelTmp;
        so.FindProperty("input").objectReferenceValue = input;
        so.FindProperty("scrim").objectReferenceValue = scrim; // write explicitly so a null passed in overwrites any stale reference
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static ToggleRow CreateToggleRow(GameObject parent, string label)
    {
        var go = NewChild(parent, "ToggleRow_" + label, out RectTransform rt);
        var cardImg = go.AddComponent<Image>();
        cardImg.color = Card;
        AddRoundedCorners(go, 10f);
        AddShadow(go);
        rt.sizeDelta = Sv(0, 56);

        var labelGo = NewChild(go, "Label", out RectTransform labelRt);
        var labelTmp = AddStyledText(labelGo, label, Sz(16), FontWeight.Medium, Text);
        SetAnchors(labelRt, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, 0.5f));
        labelRt.sizeDelta = Sv(-92, 24);
        labelRt.anchoredPosition = Sv(16, 0);

        var toggleGo = NewChild(go, "Toggle", out RectTransform toggleRt);
        SetAnchors(toggleRt, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f));
        toggleRt.sizeDelta = Sv(52, 32);
        toggleRt.anchoredPosition = Sv(-16 - 26, 0);
        var toggle = toggleGo.AddComponent<Toggle>();

        var trackGo = NewChild(toggleGo, "Track", out RectTransform trackRt);
        StretchFill(trackRt);
        var trackImg = trackGo.AddComponent<Image>();
        trackImg.sprite = Sprites.ToggleBg;
        trackImg.color = Color.white; // tint applied by ToggleRow at runtime
        AddRoundedCorners(trackGo, 16f);

        var thumbGo = NewChild(toggleGo, "Thumb", out RectTransform thumbRt);
        var thumbImg = thumbGo.AddComponent<Image>();
        thumbImg.sprite = Sprites.ToggleHandle;
        thumbImg.color = Color.white;
        thumbImg.preserveAspect = true;
        SetAnchors(thumbRt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f));
        thumbRt.sizeDelta = Sv(28, 28);
        thumbRt.anchoredPosition = Sv(16, 0);

        toggle.targetGraphic = trackImg;

        var row = go.AddComponent<ToggleRow>();
        var so = new SerializedObject(row);
        so.FindProperty("toggle").objectReferenceValue = toggle;
        so.FindProperty("trackImage").objectReferenceValue = trackImg;
        so.FindProperty("thumb").objectReferenceValue = thumbRt;
        so.FindProperty("labelText").objectReferenceValue = labelTmp;
        so.ApplyModifiedPropertiesWithoutUndo();
        return row;
    }

    private static FocusScrim BuildFocusScrim(GameObject root, string name)
    {
        var go = NewChild(root, name, out RectTransform rt);
        StretchFill(rt);

        var scrimRootGo = NewChild(go, "ScrimRoot", out RectTransform scrimRt);
        StretchFill(scrimRt);
        var scrimImg = scrimRootGo.AddComponent<Image>();
        scrimImg.color = new Color(0, 0, 0, 1);
        var cg = scrimRootGo.AddComponent<CanvasGroup>();
        cg.alpha = 0;
        scrimRootGo.SetActive(false);

        var raisedGo = NewChild(go, "RaisedLayer", out RectTransform raisedRt);
        StretchFill(raisedRt);

        go.transform.SetAsLastSibling();

        var scrim = go.AddComponent<FocusScrim>();
        var so = new SerializedObject(scrim);
        so.FindProperty("scrimRoot").objectReferenceValue = scrimRootGo;
        so.FindProperty("scrimGroup").objectReferenceValue = cg;
        so.FindProperty("scrimImage").objectReferenceValue = scrimImg;
        so.FindProperty("raisedLayer").objectReferenceValue = raisedRt;
        so.ApplyModifiedPropertiesWithoutUndo();
        return scrim;
    }

    private static ItemEditSheet BuildItemEditSheet(GameObject root, string name)
    {
        var sheetContainer = NewChild(root, name, out RectTransform containerRt);
        StretchFill(containerRt);
        sheetContainer.SetActive(false);

        var scrimBehindGo = NewChild(sheetContainer, "ScrimBehind", out RectTransform scrimBehindRt);
        StretchFill(scrimBehindRt);
        scrimBehindGo.AddComponent<Image>().color = new Color(0, 0, 0, 1);
        var scrimBehindCg = scrimBehindGo.AddComponent<CanvasGroup>();
        scrimBehindCg.alpha = 0;
        // DelayedFingerUpAction: tap-outside closes the sheet only when both
        // the finger-down and the finger-up are on this scrim element.
        // Prevents false closures when a tap on the sheet lands on scrim at
        // finger-up because the sheet animated away mid-gesture.
        var scrimBehindFinger = scrimBehindGo.AddComponent<DelayedFingerUpAction>();

        var sheetGo = NewChild(sheetContainer, "SheetRoot", out RectTransform sheetRt);
        sheetGo.AddComponent<Image>().color = Card;
        SetAnchors(sheetRt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0));
        sheetRt.sizeDelta = Sv(0, 420);
        sheetRt.anchoredPosition = Vector2.zero;

        var titleGo = NewChild(sheetGo, "Title", out RectTransform titleRt);
        var titleTmp = AddStyledText(titleGo, "Изменить", Sz(18), FontWeight.Bold, Text);
        titleTmp.alignment = TextAlignmentOptions.Center;
        SetAnchors(titleRt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
        titleRt.sizeDelta = Sv(-32, 40);
        titleRt.anchoredPosition = Sv(0, -24);

        // Sheet is already modal (scrimBehind dims the page behind it).
        // Sheet fields edit inline without their own raise-to-scrim effect,
        // which prevents the "field disappears when tapped because scrim
        // raisedLayer sits behind sibling content" issue.
        var fieldsContainer = NewChild(sheetGo, "Fields", out RectTransform fieldsRt);
        SetAnchors(fieldsRt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
        fieldsRt.sizeDelta = Sv(-32, 240);
        fieldsRt.anchoredPosition = Sv(0, -76);
        var fvlg = fieldsContainer.AddComponent<VerticalLayoutGroup>();
        fvlg.spacing = Sz(12);
        fvlg.childForceExpandHeight = false;
        fvlg.childControlHeight = false;
        fvlg.childForceExpandWidth = true;
        fvlg.childControlWidth = true;

        var nameField  = CreateEditableField(fieldsContainer, "Название", scrim: null, multiline: false);
        var priceField = CreateEditableField(fieldsContainer, "Цена",      scrim: null, multiline: false);
        var descField  = CreateEditableField(fieldsContainer, "Описание",  scrim: null, multiline: false);

        var doneGo = NewChild(sheetGo, "DoneButton", out RectTransform doneRt);
        doneGo.AddComponent<Image>().color = Primary;
        var doneBtn = doneGo.AddComponent<Button>();
        SetAnchors(doneRt, new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(0, 0));
        doneRt.sizeDelta = Sv(-24, 52);
        doneRt.anchoredPosition = Sv(16, 24);
        var doneLabel = NewChild(doneGo, "Label", out RectTransform doneLabelRt);
        var doneTmp = AddStyledText(doneLabel, "Готово", Sz(17), FontWeight.Bold, Color.white);
        doneTmp.alignment = TextAlignmentOptions.Center;
        StretchFill(doneLabelRt);

        var deleteGo = NewChild(sheetGo, "DeleteButton", out RectTransform deleteRt);
        deleteGo.AddComponent<Image>().color = Danger;
        var deleteBtn = deleteGo.AddComponent<Button>();
        SetAnchors(deleteRt, new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(1, 0));
        deleteRt.sizeDelta = Sv(-24, 52);
        deleteRt.anchoredPosition = Sv(-16, 24);
        var deleteLabelGo = NewChild(deleteGo, "Label", out RectTransform deleteLabelRt);
        var deleteTmp = AddStyledText(deleteLabelGo, "Удалить", Sz(17), FontWeight.Bold, Color.white);
        deleteTmp.alignment = TextAlignmentOptions.Center;
        StretchFill(deleteLabelRt);

        var popupGo = NewChild(sheetContainer, "DeleteConfirmPopup", out RectTransform popupRt);
        StretchFill(popupRt);
        popupGo.AddComponent<Image>().color = new Color(0, 0, 0, 0.5f);
        popupGo.SetActive(false);

        var popupCard = NewChild(popupGo, "Content", out RectTransform popupCardRt);
        popupCard.AddComponent<Image>().color = Card;
        SetAnchors(popupCardRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        popupCardRt.sizeDelta = Sv(280, 160);

        var popupTextGo = NewChild(popupCard, "Text", out RectTransform popupTextRt);
        var popupTmp = AddStyledText(popupTextGo, "Удалить элемент?", Sz(16), FontWeight.Medium, Text);
        popupTmp.alignment = TextAlignmentOptions.Center;
        SetAnchors(popupTextRt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
        popupTextRt.sizeDelta = Sv(-24, 40);
        popupTextRt.anchoredPosition = Sv(0, -40);

        var yesGo = NewChild(popupCard, "Yes", out RectTransform yesRt);
        yesGo.AddComponent<Image>().color = Danger;
        var yesBtn = yesGo.AddComponent<Button>();
        SetAnchors(yesRt, new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(0, 0));
        yesRt.sizeDelta = Sv(-16, 44);
        yesRt.anchoredPosition = Sv(12, 12);
        var yesLabelGo = NewChild(yesGo, "Label", out RectTransform yesLabelRt);
        var yesTmp = AddStyledText(yesLabelGo, "Да", Sz(15), FontWeight.Bold, Color.white);
        yesTmp.alignment = TextAlignmentOptions.Center;
        StretchFill(yesLabelRt);

        var noGo = NewChild(popupCard, "No", out RectTransform noRt);
        noGo.AddComponent<Image>().color = Border;
        var noBtn = noGo.AddComponent<Button>();
        SetAnchors(noRt, new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(1, 0));
        noRt.sizeDelta = Sv(-16, 44);
        noRt.anchoredPosition = Sv(-12, 12);
        var noLabelGo = NewChild(noGo, "Label", out RectTransform noLabelRt);
        var noTmp = AddStyledText(noLabelGo, "Отмена", Sz(15), FontWeight.Bold, Text);
        noTmp.alignment = TextAlignmentOptions.Center;
        StretchFill(noLabelRt);

        var sheet = sheetContainer.AddComponent<ItemEditSheet>();
        var so = new SerializedObject(sheet);
        so.FindProperty("sheetRoot").objectReferenceValue = sheetRt;
        so.FindProperty("nameField").objectReferenceValue = nameField;
        so.FindProperty("priceField").objectReferenceValue = priceField;
        so.FindProperty("descField").objectReferenceValue = descField;
        so.FindProperty("doneButton").objectReferenceValue = doneBtn;
        so.FindProperty("deleteButton").objectReferenceValue = deleteBtn;
        so.FindProperty("deleteConfirmPopup").objectReferenceValue = popupGo;
        so.FindProperty("deleteConfirmYes").objectReferenceValue = yesBtn;
        so.FindProperty("deleteConfirmNo").objectReferenceValue = noBtn;
        so.FindProperty("scrimBehind").objectReferenceValue = scrimBehindGo;
        so.FindProperty("scrimBehindGroup").objectReferenceValue = scrimBehindCg;
        so.FindProperty("scrimBehindFinger").objectReferenceValue = scrimBehindFinger;
        so.ApplyModifiedPropertiesWithoutUndo();

        return sheet;
    }

    private static void AddSectionHeader(GameObject parent, string text)
    {
        var go = NewChild(parent, "SectionHeader_" + text, out RectTransform rt);
        var tmp = AddStyledText(go, text, Sz(13), FontWeight.Bold, TextMuted);
        tmp.characterSpacing = 0.5f;
        rt.sizeDelta = Sv(0, 20);
        var sh = go.AddComponent<SectionHeader>();
        var so = new SerializedObject(sh);
        so.FindProperty("labelText").objectReferenceValue = tmp;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Give section headers a 40-high slot so visual spacing above/below matches mockup.
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = Sz(40);
        le.minHeight = Sz(28);
    }

    // ============================================================
    // Utilities
    // ============================================================

    private static GameObject NewChild(GameObject parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    private static GameObject NewChild(Transform parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot = pivot;
    }

    private static void StretchFill(RectTransform rt, RectOffset padding = null)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        var p = padding ?? new RectOffset();
        rt.offsetMin = new Vector2(p.left, p.bottom);
        rt.offsetMax = new Vector2(-p.right, -p.top);
    }

    private static TextMeshProUGUI AddStyledText(GameObject go, string text, float fontSize, FontWeight weight, Color color)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontWeight = weight;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Left;
        return tmp;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }

    private static GameObject FindDescendant(Transform t, string name)
    {
        if (t.name == name) return t.gameObject;
        for (int i = 0; i < t.childCount; i++)
        {
            var found = FindDescendant(t.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    private static GameObject FindDescendantInChildren(Transform t, string name)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            if (c.name == name) return c.gameObject;
            var nested = FindDescendantInChildren(c, name);
            if (nested != null) return nested;
        }
        return null;
    }

    private static TMP_Dropdown StashBusinessTypeDropdown(Transform root)
    {
        var go = FindDescendant(root, "BusinessTypeDropdown");
        if (go == null)
        {
            Debug.LogWarning("[BotSettingsRebuilder] No BusinessTypeDropdown found in prefab. " +
                             "After rebuild, right-click the General tab in Unity and choose " +
                             "UI > Dropdown - TextMeshPro, rename it 'BusinessTypeDropdown', " +
                             "and drag it into BotSettings.BusinessTypeDropdown in the Inspector.");
            return null;
        }
        var dropdown = go.GetComponent<TMP_Dropdown>();
        if (dropdown == null || dropdown.template == null)
        {
            Debug.LogWarning("[BotSettingsRebuilder] Found BusinessTypeDropdown GameObject but it " +
                             "has no valid TMP_Dropdown template. Destroying it. Manually add a " +
                             "fresh Dropdown - TextMeshPro after the rebuild completes.");
            Object.DestroyImmediate(go);
            return null;
        }
        go.transform.SetParent(null, false); // detach so the top-level destroy loop skips it
        return dropdown;
    }

    private static void AttachDropdownToGeneralTab(TMP_Dropdown dropdown, GameObject generalTab,
                                                    int siblingIndex)
    {
        if (dropdown == null) return;
        dropdown.transform.SetParent(generalTab.transform, false);
        dropdown.transform.SetSiblingIndex(siblingIndex);
        var rt = dropdown.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = Sv(0, 64);
    }

    private static void AssignByDescendantIfNull(SerializedObject so, Transform root, string propName)
    {
        var prop = so.FindProperty(propName);
        if (prop == null) return;
        if (prop.objectReferenceValue != null) return;
        var go = FindDescendant(root, propName);
        if (go != null) prop.objectReferenceValue = go;
    }

    private static ImageWithRoundedCorners AddRoundedCorners(GameObject go, float radius)
    {
        var r = go.GetComponent<ImageWithRoundedCorners>();
        if (r == null) r = go.AddComponent<ImageWithRoundedCorners>();
        r.radius = radius;
        return r;
    }

    private static Shadow AddShadow(GameObject go)
    {
        var s = go.GetComponent<Shadow>();
        if (s == null) s = go.AddComponent<Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, 0.08f);
        s.effectDistance = new Vector2(0f, -1f);
        return s;
    }
}

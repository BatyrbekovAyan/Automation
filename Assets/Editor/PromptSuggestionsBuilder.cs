#if UNITY_EDITOR
using System;
using System.Reflection;
using Automation.BotSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ADDITIVE, idempotent surgery on BotSettings.prefab — adds the «ПОДСКАЗКИ»
/// section (chip cloud + «Ещё N ›») under the Промпт field and the catalog
/// sheet cloned from the UploadSourceSheet chrome.
///
/// Never confuse this with Tools/Rebuild Bot Settings Prefabs, which destroys
/// every top-level child and wipes a dozen builders' wiring. This tool only
/// deletes the three objects it creates itself, matched by name.
/// </summary>
public static class PromptSuggestionsBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string PlusSpritePath = "Assets/Images/New/plus.png";
    private const string HeaderGoName = "SuggestionsHeader";
    private const string CloudGoName = "SuggestionsCloud";
    private const string MoreGoName = "SuggestionsMoreButton";
    private const string SheetGoName = "PromptSuggestionsSheet";

    private static readonly Color Surface     = new Color(0.090f, 0.110f, 0.141f);
    private static readonly Color Border      = new Color(0.200f, 0.243f, 0.306f);
    private static readonly Color Hairline    = new Color(0.141f, 0.173f, 0.220f);
    private static readonly Color Background  = new Color(0.055f, 0.067f, 0.086f);
    private static readonly Color InkPrimary  = new Color(0.925f, 0.941f, 0.965f);
    private static readonly Color InkTertiary = new Color(0.475f, 0.525f, 0.604f);
    private static readonly Color AccentFill  = new Color(0.243f, 0.380f, 0.776f);
    private static readonly Color AccentText  = new Color(0.349f, 0.506f, 0.839f);
    private static readonly Color OnAccent    = Color.white;

    private static Type cachedRoundedType;

    [MenuItem("Tools/BotSettings/Build Prompt Suggestions")]
    public static void Build()
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[PromptSuggestions] Failed to load prefab at {PrefabPath}");
            return;
        }

        try
        {
            var settings = prefabRoot.GetComponent<BotSettings>();
            var promptContent = prefabRoot.transform.Find("Prompt/Content");
            if (settings == null || promptContent == null)
            {
                Debug.LogError("[PromptSuggestions] BotSettings component or Prompt/Content not found.");
                return;
            }

            DestroyIfPresent(promptContent, HeaderGoName);
            DestroyIfPresent(promptContent, CloudGoName);
            DestroyIfPresent(promptContent, MoreGoName);
            DestroyIfPresent(prefabRoot.transform, SheetGoName);

            BuildHeader(promptContent);
            var cloud = BuildCloud(promptContent);
            var sheet = BuildSheet(prefabRoot.transform);
            if (cloud == null || sheet == null) return;

            var so = new SerializedObject(settings);
            so.FindProperty("promptSuggestionsCloud").objectReferenceValue = cloud;
            so.FindProperty("promptSuggestionsSheet").objectReferenceValue = sheet;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log("[PromptSuggestions] Built header, cloud and sheet; wired both BotSettings refs.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void DestroyIfPresent(Transform parent, string childName)
    {
        var existing = parent.Find(childName);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
    }

    private static GameObject NewChild(Transform parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        rt = go.GetComponent<RectTransform>();
        return go;
    }

    private static TextMeshProUGUI AddText(
        GameObject host, string content, float size, Color color, TextAlignmentOptions align)
    {
        var tmp = host.AddComponent<TextMeshProUGUI>();
        tmp.text = content;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;   // never assume the default — it is usually wrong
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    // RoundedCorners lives in its OWN UPM assembly — Type.GetType(..., "Assembly-CSharp")
    // silently fails and the corners come out square. Scan loaded assemblies.
    private static Type ResolveRoundedType()
    {
        if (cachedRoundedType != null) return cachedRoundedType;
        const string fullName = "Nobi.UiRoundedCorners.ImageWithRoundedCorners";
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = asm.GetType(fullName);
            if (type != null) return cachedRoundedType = type;
        }
        return null;
    }

    private static void EnsureRounded(GameObject go, float radius)
    {
        var type = ResolveRoundedType();
        if (type == null)
        {
            Debug.LogWarning("[PromptSuggestions] ImageWithRoundedCorners not found — corners will be square.");
            return;
        }
        var component = go.GetComponent(type) ?? go.AddComponent(type);
        type.GetField("radius")?.SetValue(component, radius);
        // "image" is private on ImageWithRoundedCorners — default GetField binding
        // flags (public only) miss it silently, making this write a permanent no-op.
        type.GetField("image", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(component, go.GetComponent<Image>());
    }

    /// <summary>
    /// Binds a graphic to a semantic theme role: get-or-add <see cref="ThemedColor"/>,
    /// then write role/target/preserveAlpha via SerializedObject only — never
    /// <see cref="ThemedColor.Configure"/>, which repaints immediately and would
    /// rewrite the authored colour float. The runtime OnEnable / Theme.Changed
    /// handler does the actual painting. Get-or-add matters here: SheetRoot and
    /// ScrimBehind arrive from Instantiate(UploadSourceSheet) already carrying a
    /// ThemedColor, and the component is [DisallowMultipleComponent].
    /// </summary>
    private static void BindTheme(GameObject go, ThemeRole role, bool preserveAlpha = true)
    {
        var graphic = go.GetComponent<Graphic>();
        var themed = go.GetComponent<ThemedColor>();
        if (themed == null) themed = go.AddComponent<ThemedColor>();

        var so = new SerializedObject(themed);
        so.FindProperty("role").enumValueIndex = (int)role;
        so.FindProperty("target").objectReferenceValue = graphic;
        so.FindProperty("preserveAlpha").boolValue = preserveAlpha;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// A tick drawn as two rotated bars. The project ships no monochrome tick
    /// sprite, and the green PNGs in Assets/Images/Icons cannot be re-tinted
    /// per theme role. Returns the container to toggle with SetActive.
    /// Never themed inside this method — callers bind the two arms themselves,
    /// so the chip's own (never-themed) tick and the sheet row's (themed) tick
    /// can share one builder without either accidentally acquiring the other's
    /// binding.
    /// </summary>
    private static GameObject BuildTick(Transform parent, Color color, float size)
    {
        var root = NewChild(parent, "Tick", out var rootRt);
        rootRt.sizeDelta = new Vector2(size, size);

        var shortArm = NewChild(root.transform, "ArmShort", out var shortRt);
        var shortImage = shortArm.AddComponent<Image>();
        shortImage.color = color;
        shortImage.raycastTarget = false;
        shortRt.sizeDelta = new Vector2(size * 0.42f, size * 0.16f);
        shortRt.anchoredPosition = new Vector2(-size * 0.22f, -size * 0.12f);
        shortRt.localRotation = Quaternion.Euler(0, 0, 45f);

        var longArm = NewChild(root.transform, "ArmLong", out var longRt);
        var longImage = longArm.AddComponent<Image>();
        longImage.color = color;
        longImage.raycastTarget = false;
        longRt.sizeDelta = new Vector2(size * 0.72f, size * 0.16f);
        longRt.anchoredPosition = new Vector2(size * 0.08f, size * 0.04f);
        longRt.localRotation = Quaternion.Euler(0, 0, -45f);

        return root;
    }

    private static void BuildHeader(Transform promptContent)
    {
        var source = promptContent.Find("SectionHeader_ПРОМПТ");
        GameObject header;
        if (source != null)
        {
            header = UnityEngine.Object.Instantiate(source.gameObject, promptContent);
        }
        else
        {
            header = NewChild(promptContent, HeaderGoName, out var fallbackRt);
            fallbackRt.sizeDelta = new Vector2(0f, 50f);
            AddText(header, string.Empty, 30f, InkTertiary, TextAlignmentOptions.MidlineLeft);
        }

        header.name = HeaderGoName;
        header.transform.SetAsLastSibling();

        var text = header.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text == null) return;
        text.text = "ПОДСКАЗКИ";
        text.fontSize = 30f;
        text.color = InkTertiary;
        text.characterSpacing = 10f;

        BindTheme(header, ThemeRole.InkTertiary);
    }

    private static PromptSuggestionChip BuildChipTemplate(Transform parent)
    {
        var chip = NewChild(parent, "ChipTemplate", out var chipRt);
        chipRt.sizeDelta = new Vector2(400f, 108f);

        // Two stacked rounded rects, not uGUI's Outline effect — Outline
        // duplicates the quad four ways and reads as a blur, not a 3-unit ring.
        // Outer = the ring (Border), inner = the fill, inset by the ring width.
        // "Added" simply disables the outer, leaving a plain filled pill.
        var outline = chip.AddComponent<Image>();
        outline.color = Border;
        EnsureRounded(chip, 54f);

        var innerGo = NewChild(chip.transform, "Fill", out var innerRt);
        var background = innerGo.AddComponent<Image>();
        background.color = Surface;
        background.raycastTarget = false;
        Stretch(innerRt, left: 3f, right: 3f, top: 3f, bottom: 3f);
        EnsureRounded(innerGo, 51f);

        var plusGo = NewChild(chip.transform, "Plus", out var plusRt);
        var plus = plusGo.AddComponent<Image>();
        plus.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlusSpritePath);
        plus.color = AccentText;
        plus.raycastTarget = false;
        plusRt.anchorMin = plusRt.anchorMax = new Vector2(0f, 0.5f);
        plusRt.pivot = new Vector2(0f, 0.5f);
        plusRt.anchoredPosition = new Vector2(36f, 0f);
        plusRt.sizeDelta = new Vector2(42f, 42f);

        var tick = BuildTick(chip.transform, new Color(0.341f, 0.871f, 0.584f), 42f);
        var tickRt = tick.GetComponent<RectTransform>();
        tickRt.anchorMin = tickRt.anchorMax = new Vector2(0f, 0.5f);
        tickRt.pivot = new Vector2(0f, 0.5f);
        tickRt.anchoredPosition = new Vector2(36f, 0f);
        tick.SetActive(false);

        var labelGo = NewChild(chip.transform, "Label", out var labelRt);
        var label = AddText(labelGo, "Подсказка", 36f, InkPrimary, TextAlignmentOptions.MidlineLeft);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(labelRt, left: 36f + 42f + 18f, right: 36f);

        var button = chip.AddComponent<Button>();
        button.targetGraphic = outline;   // the outer image is the raycast target

        var component = chip.AddComponent<PromptSuggestionChip>();
        var so = new SerializedObject(component);
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("plusGlyph").objectReferenceValue = plus;
        so.FindProperty("tickGlyph").objectReferenceValue = tick;
        so.FindProperty("background").objectReferenceValue = background;
        so.FindProperty("outline").objectReferenceValue = outline;
        so.FindProperty("button").objectReferenceValue = button;
        so.ApplyModifiedPropertiesWithoutUndo();

        chip.SetActive(false);
        return component;
    }

    private static PromptSuggestionsCloud BuildCloud(Transform promptContent)
    {
        var cloud = NewChild(promptContent, CloudGoName, out var cloudRt);
        cloudRt.sizeDelta = new Vector2(0f, 108f);

        var flow = cloud.AddComponent<ChipFlowLayout>();
        var flowSo = new SerializedObject(flow);
        flowSo.FindProperty("spacingX").floatValue = 24f;
        flowSo.FindProperty("spacingY").floatValue = 24f;
        flowSo.FindProperty("rowHeight").floatValue = 108f;
        flowSo.ApplyModifiedPropertiesWithoutUndo();

        var chipTemplate = BuildChipTemplate(cloud.transform);

        var more = NewChild(promptContent, MoreGoName, out var moreRt);
        moreRt.sizeDelta = new Vector2(0f, 90f);
        var moreImage = more.AddComponent<Image>();
        moreImage.color = new Color(1f, 1f, 1f, 0f);   // invisible but raycastable
        var moreButton = more.AddComponent<Button>();
        moreButton.targetGraphic = moreImage;

        var moreLabelGo = NewChild(more.transform, "Label", out var moreLabelRt);
        var moreLabel = AddText(moreLabelGo, "Ещё 0 ›", 32f, AccentText, TextAlignmentOptions.MidlineRight);
        Stretch(moreLabelRt);
        BindTheme(moreLabelGo, ThemeRole.AccentText);

        var component = cloud.AddComponent<PromptSuggestionsCloud>();
        var so = new SerializedObject(component);
        so.FindProperty("chipsParent").objectReferenceValue = cloudRt;
        so.FindProperty("flowLayout").objectReferenceValue = flow;
        so.FindProperty("chipTemplate").objectReferenceValue = chipTemplate;
        so.FindProperty("moreButton").objectReferenceValue = moreButton;
        so.FindProperty("moreLabel").objectReferenceValue = moreLabel;
        so.ApplyModifiedPropertiesWithoutUndo();

        return component;
    }

    private static PromptSuggestionsSheet BuildSheet(Transform prefabRoot)
    {
        var source = prefabRoot.Find("UploadSourceSheet");
        if (source == null)
        {
            Debug.LogError("[PromptSuggestions] UploadSourceSheet not found — cannot clone sheet chrome.");
            return null;
        }

        var sheet = UnityEngine.Object.Instantiate(source.gameObject, prefabRoot);
        sheet.name = SheetGoName;
        UnityEngine.Object.DestroyImmediate(sheet.GetComponent<UploadSourceSheet>());

        var scrim = sheet.transform.Find("ScrimBehind");
        var sheetRoot = sheet.transform.Find("SheetRoot");
        if (scrim == null || sheetRoot == null)
        {
            Debug.LogError("[PromptSuggestions] Cloned sheet is missing ScrimBehind or SheetRoot.");
            return null;
        }

        foreach (var child in new[] { "Title", "FileButton", "GalleryButton", "CancelButton" })
            DestroyIfPresent(sheetRoot, child);

        var sheetRootRt = sheetRoot.GetComponent<RectTransform>();
        sheetRootRt.sizeDelta = new Vector2(sheetRootRt.sizeDelta.x, 1300f);
        var sheetBackground = sheetRoot.GetComponent<Image>();
        if (sheetBackground != null) sheetBackground.color = Background;
        EnsureRounded(sheetRoot.gameObject, 60f);
        // Overrides the Surface role this SheetRoot inherited by being cloned from
        // UploadSourceSheet — this full-height catalog sheet reads as its own
        // screen rather than an elevated card, so it binds Background instead.
        BindTheme(sheetRoot.gameObject, ThemeRole.Background);

        var grabber = NewChild(sheetRoot, "Grabber", out var grabberRt);
        grabber.AddComponent<Image>().color = Border;
        grabberRt.anchorMin = grabberRt.anchorMax = new Vector2(0.5f, 1f);
        grabberRt.pivot = new Vector2(0.5f, 1f);
        grabberRt.anchoredPosition = new Vector2(0f, -24f);
        grabberRt.sizeDelta = new Vector2(105f, 12f);
        EnsureRounded(grabber, 6f);
        BindTheme(grabber, ThemeRole.Border);

        var titleGo = NewChild(sheetRoot, "Title", out var titleRt);
        var title = AddText(titleGo, "Подсказки", 44f, InkPrimary, TextAlignmentOptions.MidlineLeft);
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.offsetMin = new Vector2(48f, 0f);
        titleRt.offsetMax = new Vector2(-48f, 0f);
        titleRt.anchoredPosition = new Vector2(0f, -66f);
        titleRt.sizeDelta = new Vector2(titleRt.sizeDelta.x, 60f);
        BindTheme(titleGo, ThemeRole.InkPrimary);

        var countGo = NewChild(sheetRoot, "SelectedCount", out var countRt);
        var countLabel = AddText(countGo, "выбрано 0", 32f, InkTertiary, TextAlignmentOptions.MidlineRight);
        countRt.anchorMin = new Vector2(0f, 1f);
        countRt.anchorMax = new Vector2(1f, 1f);
        countRt.pivot = new Vector2(0.5f, 1f);
        countRt.offsetMin = new Vector2(48f, 0f);
        countRt.offsetMax = new Vector2(-48f, 0f);
        countRt.anchoredPosition = new Vector2(0f, -66f);
        countRt.sizeDelta = new Vector2(countRt.sizeDelta.x, 60f);
        BindTheme(countGo, ThemeRole.InkTertiary);

        var categories = NewChild(sheetRoot, "Categories", out var categoriesRt);
        categoriesRt.anchorMin = new Vector2(0f, 1f);
        categoriesRt.anchorMax = new Vector2(1f, 1f);
        categoriesRt.pivot = new Vector2(0.5f, 1f);
        categoriesRt.offsetMin = new Vector2(48f, 0f);
        categoriesRt.offsetMax = new Vector2(-48f, 0f);
        categoriesRt.anchoredPosition = new Vector2(0f, -150f);
        categoriesRt.sizeDelta = new Vector2(categoriesRt.sizeDelta.x, 84f);
        var categoriesLayout = categories.AddComponent<HorizontalLayoutGroup>();
        categoriesLayout.spacing = 18f;
        categoriesLayout.childForceExpandWidth = false;
        categoriesLayout.childForceExpandHeight = false;
        categoriesLayout.childControlWidth = true;
        categoriesLayout.childControlHeight = true;

        var categoryTemplate = BuildCategoryTemplate(categories.transform);
        var rowTemplate = BuildRowTemplate(out var rowsParent, sheetRoot);
        var applyButton = BuildApplyButton(sheetRoot, out var applyLabel);

        var component = sheet.AddComponent<PromptSuggestionsSheet>();
        var so = new SerializedObject(component);
        so.FindProperty("sheetRoot").objectReferenceValue = sheetRootRt;
        so.FindProperty("scrimBehind").objectReferenceValue = scrim.gameObject;
        so.FindProperty("scrimBehindGroup").objectReferenceValue = scrim.GetComponent<CanvasGroup>();
        so.FindProperty("scrimBehindFinger").objectReferenceValue = scrim.GetComponent<DelayedFingerUpAction>();
        so.FindProperty("closeButton").objectReferenceValue = null;   // grabber is decorative; tap-outside closes
        so.FindProperty("rowsParent").objectReferenceValue = rowsParent;
        so.FindProperty("rowTemplate").objectReferenceValue = rowTemplate;
        so.FindProperty("categoriesParent").objectReferenceValue = categoriesRt;
        so.FindProperty("categoryTemplate").objectReferenceValue = categoryTemplate;
        so.FindProperty("selectedCountLabel").objectReferenceValue = countLabel;
        so.FindProperty("applyButton").objectReferenceValue = applyButton;
        so.FindProperty("applyLabel").objectReferenceValue = applyLabel;
        so.ApplyModifiedPropertiesWithoutUndo();

        sheet.SetActive(false);
        return component;
    }

    private static Button BuildCategoryTemplate(Transform parent)
    {
        var go = NewChild(parent, "CategoryTemplate", out var rt);
        rt.sizeDelta = new Vector2(220f, 84f);
        var image = go.AddComponent<Image>();
        image.color = Surface;
        EnsureRounded(go, 42f);
        BindTheme(go, ThemeRole.Surface);

        var labelGo = NewChild(go.transform, "Label", out var labelRt);
        var label = AddText(labelGo, "Категория", 32f, InkPrimary, TextAlignmentOptions.Midline);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        Stretch(labelRt, left: 30f, right: 30f);
        BindTheme(labelGo, ThemeRole.InkPrimary);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        go.SetActive(false);
        return button;
    }

    private static PromptSuggestionRowView BuildRowTemplate(out RectTransform rowsParent, Transform sheetRoot)
    {
        var scrollGo = NewChild(sheetRoot, "RowsScroll", out var scrollRt);
        scrollRt.anchorMin = new Vector2(0f, 0f);
        scrollRt.anchorMax = new Vector2(1f, 1f);
        scrollRt.offsetMin = new Vector2(48f, 200f);    // above the apply button
        scrollRt.offsetMax = new Vector2(-48f, -264f);  // below the category rail
        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;

        var viewportGo = NewChild(scrollGo.transform, "Viewport", out var viewportRt);
        Stretch(viewportRt);
        var viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.003f);   // Mask needs a Graphic
        viewportGo.AddComponent<Mask>().showMaskGraphic = false;

        var contentGo = NewChild(viewportGo.transform, "Content", out var contentRt);
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.sizeDelta = Vector2.zero;
        var contentLayout = contentGo.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 0f;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;
        contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewportRt;
        scroll.content = contentRt;
        rowsParent = contentRt;

        var row = NewChild(contentGo.transform, "RowTemplate", out var rowRt);
        rowRt.sizeDelta = new Vector2(0f, 150f);
        var rowImage = row.AddComponent<Image>();
        rowImage.color = new Color(1f, 1f, 1f, 0f);

        var separatorGo = NewChild(row.transform, "Separator", out var separatorRt);
        separatorGo.AddComponent<Image>().color = Hairline;
        separatorRt.anchorMin = new Vector2(0f, 0f);
        separatorRt.anchorMax = new Vector2(1f, 0f);
        separatorRt.pivot = new Vector2(0.5f, 0f);
        separatorRt.sizeDelta = new Vector2(0f, 2f);
        BindTheme(separatorGo, ThemeRole.Hairline);

        // The box outline is always visible; only the accent fill toggles, so
        // an unchecked row still shows a target to tap.
        var boxGo = NewChild(row.transform, "Checkbox", out var boxRt);
        boxGo.AddComponent<Image>().color = Border;
        boxRt.anchorMin = boxRt.anchorMax = new Vector2(0f, 0.5f);
        boxRt.pivot = new Vector2(0f, 0.5f);
        boxRt.anchoredPosition = new Vector2(0f, 0f);
        boxRt.sizeDelta = new Vector2(66f, 66f);
        EnsureRounded(boxGo, 20f);
        BindTheme(boxGo, ThemeRole.Border);

        var boxFillGo = NewChild(boxGo.transform, "Fill", out var boxFillRt);
        var boxFill = boxFillGo.AddComponent<Image>();
        boxFill.color = AccentFill;
        boxFill.raycastTarget = false;
        Stretch(boxFillRt, left: 3f, right: 3f, top: 3f, bottom: 3f);
        EnsureRounded(boxFillGo, 17f);
        BindTheme(boxFillGo, ThemeRole.AccentFill);

        var tick = BuildTick(boxGo.transform, OnAccent, 40f);
        var tickRt = tick.GetComponent<RectTransform>();
        tickRt.anchorMin = tickRt.anchorMax = new Vector2(0.5f, 0.5f);
        tickRt.anchoredPosition = Vector2.zero;
        // This row checkbox's tick IS themed (unlike the chip's) — both arms sit
        // on the AccentFill box, so their glyph colour is AccentOnFill.
        BindTheme(tick.transform.Find("ArmShort").gameObject, ThemeRole.AccentOnFill);
        BindTheme(tick.transform.Find("ArmLong").gameObject, ThemeRole.AccentOnFill);

        var labelGo = NewChild(row.transform, "Label", out var labelRt);
        var label = AddText(labelGo, "Текст подсказки", 38f, InkPrimary, TextAlignmentOptions.MidlineLeft);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        Stretch(labelRt, left: 66f + 30f, right: 0f, top: 18f, bottom: 18f);
        BindTheme(labelGo, ThemeRole.InkPrimary);

        var button = row.AddComponent<Button>();
        button.targetGraphic = rowImage;

        var component = row.AddComponent<PromptSuggestionRowView>();
        var so = new SerializedObject(component);
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("checkboxFill").objectReferenceValue = boxFill;
        so.FindProperty("checkboxTick").objectReferenceValue = tick;
        so.FindProperty("button").objectReferenceValue = button;
        so.ApplyModifiedPropertiesWithoutUndo();

        row.SetActive(false);
        return component;
    }

    private static Button BuildApplyButton(Transform sheetRoot, out TextMeshProUGUI applyLabel)
    {
        var go = NewChild(sheetRoot, "ApplyButton", out var rt);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.offsetMin = new Vector2(48f, 48f);
        rt.offsetMax = new Vector2(-48f, 48f + 132f);
        var image = go.AddComponent<Image>();
        image.color = AccentFill;
        EnsureRounded(go, 30f);
        BindTheme(go, ThemeRole.AccentFill);

        var labelGo = NewChild(go.transform, "Label", out var labelRt);
        applyLabel = AddText(labelGo, "Добавить 0", 38f, OnAccent, TextAlignmentOptions.Midline);
        Stretch(labelRt);
        BindTheme(labelGo, ThemeRole.AccentOnFill);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        return button;
    }
}
#endif

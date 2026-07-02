#if UNITY_EDITOR
using Automation.BotSettingsUI;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bakes the "Прайс-листы" uploaded-files section into BotSettings.prefab and
/// wires the serialized fields consumed by BotSettings.Files.cs.
///
/// Per tab (Product | Service), appended to the scroll Content after the
/// items list so it scrolls with it:
///
///   UploadedFilesSection            ← hidden at runtime while empty
///     SectionHeader_ПРАЙС-ЛИСТЫ
///     Rows                          ← rows spawned here at runtime
///       FileRowTemplate (inactive)  ← Badge/Label + Texts/Name + Texts/Meta + RemoveButton
///
/// Also builds DeleteFileConfirmPopup (PopupUI-compatible: transparent
/// backdrop + "Content" card) mirroring DeleteBotConfirmPopup.
///
/// Delete-and-rebuild — safe to re-run; serialized refs are rewired each run.
/// </summary>
public static class BotSettingsUploadedFilesBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string SectionName = "UploadedFilesSection";
    private const string PopupGoName = "DeleteFileConfirmPopup";

    private const float Scale = 2.5f; // matches BotSettingsRebuilder design units

    private static readonly Color Card = Hex("#FFFFFF");
    private static readonly Color Text = Hex("#1A1A2E");
    private static readonly Color TextMuted = Hex("#8E8E93");
    private static readonly Color Primary = Hex("#1B7CEB");
    private static readonly Color PrimaryLight = Hex("#E8F2FD");

    [MenuItem("Tools/BotSettings/Build Uploaded Files Section")]
    public static void Build()
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[BotSettings] Failed to load prefab at {PrefabPath}");
            return;
        }

        try
        {
            var settings = prefabRoot.GetComponent<BotSettings>();
            if (settings == null)
            {
                Debug.LogError("[BotSettings] BotSettings component not found on prefab root.");
                return;
            }

            var so = new SerializedObject(settings);

            BuildTabSection(prefabRoot, so, tabName: "Product",
                sectionProp: "uploadedProductFilesSection",
                parentProp: "uploadedProductFilesParent",
                templateProp: "uploadedProductFileRowTemplate");
            BuildTabSection(prefabRoot, so, tabName: "Service",
                sectionProp: "uploadedServiceFilesSection",
                parentProp: "uploadedServiceFilesParent",
                templateProp: "uploadedServiceFileRowTemplate");
            BuildConfirmPopup(prefabRoot, so);

            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            Debug.Log($"[BotSettings] Uploaded-files section baked into {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // ============================================================
    // Section (per tab)
    // ============================================================

    private static void BuildTabSection(GameObject prefabRoot, SerializedObject so,
        string tabName, string sectionProp, string parentProp, string templateProp)
    {
        var content = prefabRoot.transform.Find($"{tabName}/Viewport/Content");
        if (content == null)
        {
            Debug.LogWarning($"[BotSettings] '{tabName}/Viewport/Content' not found — skipping.");
            return;
        }

        var stale = content.Find(SectionName);
        if (stale != null) Object.DestroyImmediate(stale.gameObject);

        var section = NewChild(content, SectionName, out _);
        var sectionVlg = section.AddComponent<VerticalLayoutGroup>();
        sectionVlg.spacing = Sz(10);
        sectionVlg.childAlignment = TextAnchor.UpperLeft;
        sectionVlg.childControlWidth = true;
        sectionVlg.childControlHeight = true;
        sectionVlg.childForceExpandWidth = true;
        sectionVlg.childForceExpandHeight = false;
        var fitter = section.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        section.transform.SetAsFirstSibling(); // above the catalog section header + list

        AddSectionHeader(section, "ПРАЙС-ЛИСТЫ");

        var rows = NewChild(section.transform, "Rows", out var rowsRt);
        var rowsVlg = rows.AddComponent<VerticalLayoutGroup>();
        rowsVlg.spacing = Sz(10);
        rowsVlg.childAlignment = TextAnchor.UpperLeft;
        rowsVlg.childControlWidth = true;
        rowsVlg.childControlHeight = true;
        rowsVlg.childForceExpandWidth = true;
        rowsVlg.childForceExpandHeight = false;

        var template = BuildRowTemplate(rows.transform);

        so.FindProperty(sectionProp).objectReferenceValue = section;
        so.FindProperty(parentProp).objectReferenceValue = rowsRt;
        so.FindProperty(templateProp).objectReferenceValue = template;
    }

    private static void AddSectionHeader(GameObject parent, string text)
    {
        // Mirrors BotSettingsRebuilder.AddSectionHeader so this header sits
        // visually identical to "КАТАЛОГ ТОВАРОВ" above it.
        var go = NewChild(parent.transform, "SectionHeader_" + text, out _);
        var tmp = AddText(go, text, Sz(13), FontStyles.Bold, TextMuted, TextAlignmentOptions.MidlineLeft);
        tmp.characterSpacing = 0.5f;

        var sectionHeader = go.AddComponent<SectionHeader>();
        var headerSo = new SerializedObject(sectionHeader);
        var labelProp = headerSo.FindProperty("labelText");
        if (labelProp != null)
        {
            labelProp.objectReferenceValue = tmp;
            headerSo.ApplyModifiedPropertiesWithoutUndo();
        }

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = Sz(40);
        le.minHeight = Sz(28);
    }

    // ============================================================
    // Row template
    // ============================================================

    private static GameObject BuildRowTemplate(Transform rowsParent)
    {
        var row = NewChild(rowsParent, "FileRowTemplate", out _);
        var bg = row.AddComponent<Image>();
        bg.color = Card;
        AddRoundedCorners(row, Sz(11));

        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset((int)Sz(12), (int)Sz(6), (int)Sz(9), (int)Sz(9));
        hlg.spacing = Sz(11);
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        var rowLe = row.AddComponent<LayoutElement>();
        rowLe.preferredHeight = Sz(60);
        rowLe.minHeight = Sz(60);

        // Tap-to-retry surface for the failed-upload state. Disabled in the
        // normal/pending states so it never swallows the RemoveButton's taps.
        var rowButton = row.AddComponent<Button>();
        rowButton.transition = Selectable.Transition.None;
        rowButton.targetGraphic = bg;
        rowButton.interactable = false;

        BuildBadge(row.transform);
        BuildTexts(row.transform);
        BuildRemoveButton(row.transform);

        row.SetActive(false);
        return row;
    }

    private static void BuildBadge(Transform row)
    {
        var badge = NewChild(row, "Badge", out _);
        var img = badge.AddComponent<Image>();
        img.color = PrimaryLight;
        img.raycastTarget = false;
        AddRoundedCorners(badge, Sz(8));
        var le = badge.AddComponent<LayoutElement>();
        le.preferredWidth = Sz(37);
        le.preferredHeight = Sz(37);
        le.minWidth = Sz(37);
        le.minHeight = Sz(37);

        var label = NewChild(badge.transform, "Label", out var labelRt);
        Stretch(labelRt);
        AddText(label, "PDF", Sz(10.5f), FontStyles.Bold, Primary, TextAlignmentOptions.Center);
    }

    private static void BuildTexts(Transform row)
    {
        var texts = NewChild(row, "Texts", out _);
        var vlg = texts.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = Sz(2);
        vlg.childAlignment = TextAnchor.MiddleLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var le = texts.AddComponent<LayoutElement>();
        le.flexibleWidth = 1f;

        var name = NewChild(texts.transform, "Name", out _);
        var nameTmp = AddText(name, "Прайс-лист.pdf", Sz(15), FontStyles.Normal, Text, TextAlignmentOptions.MidlineLeft);
        nameTmp.textWrappingMode = TextWrappingModes.NoWrap;
        nameTmp.overflowMode = TextOverflowModes.Ellipsis;

        var meta = NewChild(texts.transform, "Meta", out _);
        AddText(meta, "240 КБ · 28 июн", Sz(11.5f), FontStyles.Normal, TextMuted, TextAlignmentOptions.MidlineLeft);
    }

    private static void BuildRemoveButton(Transform row)
    {
        var go = NewChild(row, "RemoveButton", out _);
        var hit = go.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f); // invisible, raycast-only hit area
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = Sz(48);
        le.preferredHeight = Sz(48);
        le.minWidth = Sz(48);
        le.minHeight = Sz(48);

        var button = go.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;

        // TMP glyph icons don't render in this project — draw the X as two
        // rotated Image bars instead.
        BuildXBar(go.transform, "X1", 45f);
        BuildXBar(go.transform, "X2", -45f);

        // Uploading state: three pulsing dots in the same trailing slot as the
        // X (slot stays constant → no layout shift between states). Inactive by
        // default; BotSettings.Files.cs toggles it for pending rows.
        BuildDots(go.transform);
    }

    private static void BuildDots(Transform removeButton)
    {
        var dots = NewChild(removeButton, "Dots", out var dotsRt);
        Stretch(dotsRt);

        var graphics = new Graphic[3];
        for (int i = 0; i < 3; i++)
        {
            var dot = NewChild(dots.transform, $"Dot{i}", out var rt);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Sz(3.5f), Sz(3.5f));
            rt.anchoredPosition = new Vector2((i - 1) * Sz(6), 0f);
            var img = dot.AddComponent<Image>();
            img.color = Primary;
            img.raycastTarget = false;
            AddRoundedCorners(dot, Sz(1.75f));
            graphics[i] = img;
        }

        // Reuse the app's existing "thinking" loading language — the same
        // self-animating dots as the reply-suggestions skeleton.
        var skeleton = dots.AddComponent<ThinkingDotsSkeleton>();
        var so = new SerializedObject(skeleton);
        var dotsProp = so.FindProperty("dots");
        dotsProp.arraySize = graphics.Length;
        for (int i = 0; i < graphics.Length; i++)
            dotsProp.GetArrayElementAtIndex(i).objectReferenceValue = graphics[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        dots.SetActive(false);
    }

    private static void BuildXBar(Transform parent, string name, float angle)
    {
        var bar = NewChild(parent, name, out var rt);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(Sz(17), Sz(2));
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        var img = bar.AddComponent<Image>();
        img.color = TextMuted;
        img.raycastTarget = false;
    }

    // ============================================================
    // Confirm popup (mirrors BotSettingsDeleteBotPopupBuilder)
    // ============================================================

    private static void BuildConfirmPopup(GameObject prefabRoot, SerializedObject so)
    {
        for (int i = prefabRoot.transform.childCount - 1; i >= 0; i--)
        {
            var child = prefabRoot.transform.GetChild(i);
            if (child.name == PopupGoName) Object.DestroyImmediate(child.gameObject);
        }

        var popup = NewChild(prefabRoot.transform, PopupGoName, out var prt);
        Stretch(prt);
        prt.SetAsLastSibling();
        var backdrop = popup.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0f); // PopupUI.Show fades it in
        popup.AddComponent<Button>();

        // Card must be named "Content" so PopupUI.Show finds it for the
        // fade/scale animation.
        var card = NewChild(popup.transform, "Content", out var crt);
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(960f, 660f);
        card.AddComponent<Image>().color = Card;
        AddRoundedCorners(card, 48f);
        card.AddComponent<EventAbsorber>();

        var title = NewChild(card.transform, "Title", out var trt);
        SetTopStretch(trt, y: -72f, height: 84f);
        AddText(title, "Удалить прайс-лист?", 54f, FontStyles.Bold, Text, TextAlignmentOptions.Center);

        var body = NewChild(card.transform, "Body", out var brt);
        SetTopStretch(brt, y: -204f, height: 220f);
        var bodyTmp = AddText(body,
            "Бот перестанет использовать этот файл в ответах. Это действие необратимо.",
            42f, FontStyles.Normal, new Color(0.39f, 0.39f, 0.4f), TextAlignmentOptions.Center);

        var cancel = BuildPopupButton(card.transform, "CancelButton", "Отмена", destructive: false, anchorX: 0.28f);
        var confirm = BuildPopupButton(card.transform, "ConfirmButton", "Удалить", destructive: true, anchorX: 0.72f);

        popup.SetActive(false);

        so.FindProperty("deleteFileConfirmPopup").objectReferenceValue = popup;
        so.FindProperty("deleteFileConfirmButton").objectReferenceValue = confirm;
        so.FindProperty("deleteFileCancelButton").objectReferenceValue = cancel;
        so.FindProperty("deleteFileConfirmBody").objectReferenceValue = bodyTmp;
    }

    private static Button BuildPopupButton(Transform card, string goName, string label, bool destructive, float anchorX)
    {
        var go = NewChild(card, goName, out var rt);
        rt.anchorMin = new Vector2(anchorX, 0f);
        rt.anchorMax = new Vector2(anchorX, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 60f);
        rt.sizeDelta = new Vector2(384f, 144f);

        var img = go.AddComponent<Image>();
        img.color = destructive ? new Color(0.92f, 0.27f, 0.27f) : new Color(0.94f, 0.94f, 0.95f);
        AddRoundedCorners(go, 36f);

        var labelGo = NewChild(go.transform, "Label", out var lrt);
        Stretch(lrt);
        AddText(labelGo, label, 45f, FontStyles.Bold,
            destructive ? Color.white : Text, TextAlignmentOptions.Center);

        var button = go.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = img;
        return button;
    }

    // ============================================================
    // Utilities
    // ============================================================

    private static float Sz(float designUnits) => designUnits * Scale;

    private static GameObject NewChild(Transform parent, string name, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);
        rt = (RectTransform)go.transform;
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetTopStretch(RectTransform rt, float y, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(-96f, height);
    }

    private static TextMeshProUGUI AddText(GameObject go, string text, float fontSize,
        FontStyles style, Color color, TextAlignmentOptions alignment)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void AddRoundedCorners(GameObject go, float radius)
    {
        var rounded = go.GetComponent<ImageWithRoundedCorners>();
        if (rounded == null) rounded = go.AddComponent<ImageWithRoundedCorners>();
        rounded.radius = radius;
        rounded.Validate();
        rounded.Refresh();
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var color);
        return color;
    }
}
#endif

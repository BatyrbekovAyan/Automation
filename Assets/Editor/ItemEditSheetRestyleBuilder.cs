using System;
using System.Reflection;
using Automation.BotSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Restyles BOTH item-edit sheets (ProductEditSheet / ServiceEditSheet) inside
/// BotSettings.prefab to the sketch-007 «B2» look: outside UPPERCASE labels,
/// bordered input wells on the sheet's ground colour, a ₸ suffix on the price
/// well, a full-width «Готово» and a text-only «Удалить» stacked beneath it.
///
/// ADDITIVE AND IDEMPOTENT — the counterpart of the destructive
/// Tools/Rebuild Bot Settings Prefabs, which must NEVER be run (it wipes the
/// wiring of a dozen builders). Nothing here is destroyed or re-created:
/// every existing GameObject is mutated in place, so the twelve serialized
/// references on ItemEditSheet (and the persistent onDismiss → Dismiss call on
/// DragZone) survive. The only new objects are, per field, a "Well" + its
/// "Fill", plus a "Suffix" on the price field — all created get-or-create by
/// name, so re-running changes nothing.
///
/// Targets are resolved through ItemEditSheet's own serialized references, not
/// by GameObject name, so a renamed object can never silently skip a sheet.
///
/// NOTE: Nobi's rounded-corner components are left to Validate()/Refresh()
/// themselves from their [ExecuteInEditMode] OnEnable — calling those here
/// would need Shader.Find, which is unreliable under -nographics batch mode.
///
/// Re-run this after Tools/Sheets/Wire Drag Dismiss: that wirer re-creates the
/// Grabber from scratch with a hardcoded colour and no ThemedColor binding.
/// </summary>
public static class ItemEditSheetRestyleBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";

    // ---- Layout, in 1080x1920 reference units (sketch CSS px x3) ----------
    private const float SheetHeight = 1149f;
    private const float SheetCornerRadius = 54f;    // 18px; independent corners are NOT doubled
    private const float DragZoneHeight = 156f;      // header block is 168 tall; 12 of clearance

    private const float TitleTop = -72f;            // 24 pad + 12 handle + 36 gap
    private const float TitleHeight = 60f;
    private const float TitleFontSize = 48f;

    private const float FieldsTop = -168f;
    private const float FieldsHeight = 603f;        // 165 + 30 + 165 + 30 + 213
    private const float SidePadding = 48f;          // 16px each side

    private const float LabelHeight = 42f;
    private const float LabelFontSize = 30f;
    private const float LabelSideInset = 6f;
    private const float LabelCharacterSpacing = 4f; // TMP: x fontSize x 0.01 = .04em

    // Nobi's radius field is 1:1 with the visual radius: Refresh sends
    // radius*2 and the shader halves it again (SDFUtils.cginc CalcAlpha:35).
    private const float WellRadius = 30f;           // 10px
    private const float WellRingWidth = 3f;         // 1px
    private const float WellPadX = 36f;             // 12px
    private const float WellPadY = 30f;             // 10px
    private const float SuffixWidth = 60f;
    private const float SuffixGap = 12f;
    private const float SuffixFontSize = 40f;

    private const float DoneY = 192f;
    private const float DoneHeight = 138f;          // 46px
    private const float DoneRadius = 36f;           // 12px
    private const float DoneFontSize = 45f;
    private const float DeleteY = 54f;
    private const float DeleteHeight = 114f;        // 38px
    private const float DeleteFontSize = 40f;

    // How much of the keyboard's height the sheet declines to lift by.
    // Derived, not tuned: «Готово» spans y 192..330, so a reduction of 192
    // would put its bottom edge exactly on the keyboard; 168 leaves a 24-unit
    // margin above it. «Удалить» (y 54..168) therefore ends up entirely behind
    // the keyboard while typing — deliberate. A destructive action sitting
    // directly above the keyboard's top row is an accidental-tap hazard, and
    // while the user is typing the only action that matters is «Готово».
    private const float LiftReduction = 168f;

    // Authored colours. They are only the value the graphic carries on disk —
    // ThemedColor / FieldWellFocusBorder repaint from the palette at runtime.
    private static readonly Color DarkHairline = new Color32(0x24, 0x2C, 0x38, 0xFF);
    private static readonly Color DarkBackground = new Color32(0x0E, 0x11, 0x16, 0xFF);
    private static readonly Color DarkInkTertiary = new Color32(0x79, 0x86, 0x9A, 0xFF);
    private static readonly Color DarkDestructive = new Color32(0xF2, 0x55, 0x5A, 0xFF);
    private static readonly Color DarkAccentOnFill = Color.white;

    private const string WellName = "Well";
    private const string FillName = "Fill";
    private const string SuffixName = "Suffix";
    private const string SuffixGlyph = "₸";

    private static Type cachedRoundedType;
    private static Type cachedIndependentRoundedType;

    // Reports through the Console rather than EditorUtility.DisplayDialog: a
    // modal blocks the Editor when the entry is driven over the mcp-unity
    // bridge, and the Console is already open next to the menu.
    [MenuItem("Tools/BotSettings/Restyle Item Edit Sheet")]
    public static void Restyle()
    {
        int sheets = Run();
        Debug.Log($"[ItemEditSheetRestyleBuilder] {sheets} sheet(s) updated in BotSettings.prefab. " +
                  "Smoke-test in Play mode: open a product and a service card, focus each field, delete an item.");
    }

    /// <summary>Batch entry: Tools/run-editor-builder.sh ItemEditSheetRestyleBuilder.BuildHeadless</summary>
    public static void BuildHeadless()
    {
        int sheets = Run();
        // Sentinel is the runner's source of truth — logged only after a clean
        // run AND a successful save, never from a partial pass.
        Debug.Log($"[ItemEditSheetRestyleBuilder] Restyled {sheets} sheet(s).");
        Debug.Log("[ItemEditSheetRestyleBuilder] Headless build + save complete");
    }

    private static int Run()
    {
        var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var sheets = contents.GetComponentsInChildren<ItemEditSheet>(true);
            if (sheets.Length == 0)
                throw new InvalidOperationException($"No ItemEditSheet found in {PrefabPath}.");

            foreach (var sheet in sheets) RestyleSheet(sheet);

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            return sheets.Length;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // ==================================================================
    // One sheet
    // ==================================================================

    private static void RestyleSheet(ItemEditSheet sheet)
    {
        var so = new SerializedObject(sheet);
        var sheetRoot = Resolve<RectTransform>(so, "sheetRoot", sheet);
        var nameField = Resolve<EditableField>(so, "nameField", sheet);
        var priceField = Resolve<EditableField>(so, "priceField", sheet);
        var descField = Resolve<EditableField>(so, "descField", sheet);
        var doneButton = Resolve<Button>(so, "doneButton", sheet);
        var deleteButton = Resolve<Button>(so, "deleteButton", sheet);

        bool isProduct = sheet.name.IndexOf("Product", StringComparison.OrdinalIgnoreCase) >= 0;

        RestyleChassis(sheetRoot, isProduct);
        RestyleFieldsContainer(sheetRoot);

        // Single-line wells for name/price, two-line for the description.
        RestyleField(nameField, "НАЗВАНИЕ", groupHeight: 165f, wellHeight: 114f, priceSuffix: false);
        RestyleField(priceField, "ЦЕНА", groupHeight: 165f, wellHeight: 114f, priceSuffix: true);
        RestyleField(descField, "ОПИСАНИЕ", groupHeight: 213f, wellHeight: 162f, priceSuffix: false);

        RestyleButtons(doneButton, deleteButton);

        // Unified deliberately: the two sheets shipped with 0 and 140, neither
        // derived from anything. The value now follows from where «Готово» sits.
        so.FindProperty("liftReduction").floatValue = LiftReduction;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void RestyleChassis(RectTransform sheetRoot, bool isProduct)
    {
        // Pivot/anchors are load-bearing: ItemEditSheet.Awake snapshots
        // hiddenAnchored = -sheetRoot.rect.height exactly once, and that is only
        // correct for a bottom-anchored rect with pivot.y = 0.
        sheetRoot.sizeDelta = new Vector2(sheetRoot.sizeDelta.x, SheetHeight);
        SetIndependentCornerRadius(sheetRoot.gameObject, SheetCornerRadius);

        var title = sheetRoot.Find("Title") as RectTransform;
        if (title != null)
        {
            SetTopStretch(title, TitleTop, TitleHeight, SidePadding);
            var tmp = title.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
            {
                tmp.text = isProduct ? "Товар" : "Услуга";
                tmp.fontSize = TitleFontSize;
                tmp.fontWeight = FontWeight.Bold;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.raycastTarget = false;
            }
        }

        // The pill's geometry already matches the sketch; only its colour owner
        // changes (InputBorder -> Border).
        var grabber = sheetRoot.Find("Grabber");
        if (grabber != null) BindTheme(grabber.gameObject, ThemeRole.Border);

        // Keep DragZone the LAST sibling: it must win the raycast over the
        // header, which is exactly how swipe-to-dismiss works.
        var dragZone = sheetRoot.Find("DragZone") as RectTransform;
        if (dragZone != null)
        {
            dragZone.sizeDelta = new Vector2(dragZone.sizeDelta.x, DragZoneHeight);
            dragZone.SetAsLastSibling();
        }
    }

    private static void RestyleFieldsContainer(RectTransform sheetRoot)
    {
        var fields = sheetRoot.Find("Fields") as RectTransform;
        if (fields == null) return;

        SetTopStretch(fields, FieldsTop, FieldsHeight, SidePadding);

        // childControlHeight is off, so the group reads each child's own
        // rect height — writing sizeDelta.y on the fields is the right lever.
        var layout = fields.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            layout.spacing = 30f;
            layout.padding = new RectOffset(0, 0, 0, 0);
        }
    }

    // ==================================================================
    // One field: label outside, bordered well, input filling the well
    // ==================================================================

    private static void RestyleField(
        EditableField field, string label, float groupHeight, float wellHeight, bool priceSuffix)
    {
        if (field == null) return;

        var fieldSo = new SerializedObject(field);
        var labelText = fieldSo.FindProperty("labelText")?.objectReferenceValue as TextMeshProUGUI;
        var input = fieldSo.FindProperty("input")?.objectReferenceValue as TMP_InputField;
        if (input == null)
            throw new InvalidOperationException($"'{field.name}' has no input wired — aborting.");

        var fieldRt = (RectTransform)field.transform;
        fieldRt.sizeDelta = new Vector2(fieldRt.sizeDelta.x, groupHeight);

        // The card fill disappears: the group now sits directly on the sheet.
        // The Image itself stays (ThemedColor preserves the authored alpha, and
        // the component is nobody's targetGraphic here).
        var fieldImage = field.GetComponent<Image>();
        if (fieldImage != null)
        {
            var c = fieldImage.color;
            fieldImage.color = new Color(c.r, c.g, c.b, 0f);
            fieldImage.raycastTarget = false;
        }
        var shadow = field.GetComponent<Shadow>();
        if (shadow != null) shadow.enabled = false;

        if (labelText != null)
        {
            var labelRt = (RectTransform)labelText.transform;
            SetTopStretch(labelRt, 0f, LabelHeight, LabelSideInset);
            labelText.text = label;
            labelText.fontSize = LabelFontSize;
            labelText.fontWeight = FontWeight.SemiBold;
            labelText.characterSpacing = LabelCharacterSpacing;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.raycastTarget = false;
        }

        var well = BuildWell(fieldRt, field, wellHeight);

        // The input covers the well exactly, so the whole well is the tap
        // target; the visual padding moves onto the text viewport.
        var inputRt = (RectTransform)input.transform;
        SetBottomStretch(inputRt, 0f, wellHeight, 0f);

        // Absolute draw order — label, well (behind), input, suffix (on top).
        // Deriving the well's index from the input's would flip the two on a
        // second run and bury the text under the well.
        int index = 0;
        if (labelText != null) ((RectTransform)labelText.transform).SetSiblingIndex(index++);
        well.SetSiblingIndex(index++);
        inputRt.SetSiblingIndex(index);

        // The sketch has no placeholders — with the label now sitting outside
        // the well, an empty field would otherwise read «НАЗВАНИЕ» above
        // «Название» inside. The Placeholder object itself stays (it is
        // TMP_InputField.placeholder); only its copy is cleared.
        if (input.placeholder is TMP_Text placeholder) placeholder.text = string.Empty;

        float rightInset = priceSuffix ? WellPadX + SuffixWidth + SuffixGap : WellPadX;
        var viewport = input.textViewport;
        if (viewport != null)
        {
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(WellPadX, WellPadY);
            viewport.offsetMax = new Vector2(-rightInset, -WellPadY);
        }

        if (priceSuffix) BuildSuffix(fieldRt, labelText, wellHeight);
        else DestroyIfPresent(fieldRt, SuffixName);
    }

    /// <summary>
    /// Ring + inset fill, the same two-stacked-rects trick the prompt chips use
    /// — uGUI's Outline duplicates the quad four ways and reads as a blur, not
    /// as a 1-unit border.
    /// </summary>
    private static RectTransform BuildWell(RectTransform fieldRt, EditableField field, float wellHeight)
    {
        var well = GetOrCreate(fieldRt, WellName);
        SetBottomStretch(well, 0f, wellHeight, 0f);

        var ring = well.GetComponent<Image>() ?? well.gameObject.AddComponent<Image>();
        ring.color = DarkHairline;
        ring.raycastTarget = false;
        SetCornerRadius(well.gameObject, WellRadius);

        var fill = GetOrCreate(well, FillName);
        Stretch(fill, WellRingWidth);
        var fillImage = fill.GetComponent<Image>() ?? fill.gameObject.AddComponent<Image>();
        fillImage.color = DarkBackground;
        fillImage.raycastTarget = false;
        SetCornerRadius(fill.gameObject, WellRadius - WellRingWidth);
        BindTheme(fill.gameObject, ThemeRole.Background);

        // The ring owns its colour itself — see FieldWellFocusBorder's summary.
        var border = well.GetComponent<FieldWellFocusBorder>()
                     ?? well.gameObject.AddComponent<FieldWellFocusBorder>();
        var borderSo = new SerializedObject(border);
        borderSo.FindProperty("field").objectReferenceValue = field;
        borderSo.FindProperty("ring").objectReferenceValue = ring;
        borderSo.ApplyModifiedPropertiesWithoutUndo();

        return well;
    }

    /// <summary>
    /// The ₸ is a sibling label, never part of the input's text: card.Price is
    /// a raw string that reaches PlayerPrefs and the n8n catalog payload as-is.
    /// </summary>
    private static void BuildSuffix(RectTransform fieldRt, TextMeshProUGUI fontSource, float wellHeight)
    {
        var suffix = GetOrCreate(fieldRt, SuffixName);
        suffix.anchorMin = suffix.anchorMax = new Vector2(1f, 0f);
        suffix.pivot = new Vector2(1f, 0f);
        suffix.sizeDelta = new Vector2(SuffixWidth, wellHeight - WellPadY * 2f);
        suffix.anchoredPosition = new Vector2(-WellPadX, WellPadY);
        suffix.SetAsLastSibling();   // drawn over the input's transparent fill

        var tmp = suffix.GetComponent<TextMeshProUGUI>() ?? suffix.gameObject.AddComponent<TextMeshProUGUI>();
        // Take the field label's font, not TMP's default: the ₸ glyph has to
        // come from the same SDF asset the rest of the sheet renders with.
        if (fontSource != null && fontSource.font != null) tmp.font = fontSource.font;
        tmp.text = SuffixGlyph;
        tmp.fontSize = SuffixFontSize;
        tmp.fontWeight = FontWeight.Regular;
        tmp.alignment = TextAlignmentOptions.MidlineRight;
        tmp.raycastTarget = false;
        tmp.color = DarkInkTertiary;
        BindTheme(suffix.gameObject, ThemeRole.InkTertiary);
    }

    // ==================================================================
    // Buttons
    // ==================================================================

    private static void RestyleButtons(Button done, Button delete)
    {
        if (done != null)
        {
            var rt = (RectTransform)done.transform;
            SetBottomStretch(rt, DoneY, DoneHeight, SidePadding);
            SetCornerRadius(done.gameObject, DoneRadius);
            StyleButtonLabel(done, DoneFontSize, DarkAccentOnFill, ThemeRole.AccentOnFill);
        }

        if (delete != null)
        {
            var rt = (RectTransform)delete.transform;
            SetBottomStretch(rt, DeleteY, DeleteHeight, SidePadding);

            // The fill goes invisible but the Image MUST stay enabled and
            // raycastable — it is the Button's targetGraphic and the only thing
            // that receives the tap. ColorTint on a fully transparent graphic
            // gives no feedback, so the transition is switched off explicitly.
            var image = delete.GetComponent<Image>();
            if (image != null)
            {
                var c = image.color;
                image.color = new Color(c.r, c.g, c.b, 0f);
                image.raycastTarget = true;
            }
            delete.transition = Selectable.Transition.None;

            StyleButtonLabel(delete, DeleteFontSize, DarkDestructive, ThemeRole.Destructive);
        }
    }

    private static void StyleButtonLabel(Button button, float fontSize, Color authored, ThemeRole role)
    {
        var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null) return;

        label.fontSize = fontSize;
        label.fontWeight = FontWeight.SemiBold;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        label.color = authored;
        BindTheme(label.gameObject, role);
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    private static T Resolve<T>(SerializedObject so, string property, ItemEditSheet sheet)
        where T : UnityEngine.Object
    {
        var value = so.FindProperty(property)?.objectReferenceValue as T;
        if (value == null)
            throw new InvalidOperationException(
                $"'{sheet.name}' has no '{property}' wired — refusing to restyle a half-wired sheet.");
        return value;
    }

    private static RectTransform GetOrCreate(RectTransform parent, string name)
    {
        var existing = parent.Find(name) as RectTransform;
        if (existing != null) return existing;

        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    private static void DestroyIfPresent(RectTransform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
    }

    private static void SetTopStretch(RectTransform rt, float top, float height, float sideInset)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(-sideInset * 2f, height);
        rt.anchoredPosition = new Vector2(0f, top);
    }

    private static void SetBottomStretch(RectTransform rt, float bottom, float height, float sideInset)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(-sideInset * 2f, height);
        rt.anchoredPosition = new Vector2(0f, bottom);
    }

    private static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    // RoundedCorners lives in its OWN UPM assembly — Type.GetType(..., "Assembly-CSharp")
    // silently fails and the corners come out square. Scan loaded assemblies.
    private static Type ResolveType(string fullName, ref Type cache)
    {
        if (cache != null) return cache;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = asm.GetType(fullName);
            if (type != null) return cache = type;
        }
        return null;
    }

    private static void SetCornerRadius(GameObject go, float radius)
    {
        var type = ResolveType("Nobi.UiRoundedCorners.ImageWithRoundedCorners", ref cachedRoundedType);
        if (type == null)
        {
            Debug.LogWarning("[ItemEditSheetRestyleBuilder] ImageWithRoundedCorners not found — corners stay square.");
            return;
        }

        var component = go.GetComponent(type) ?? go.AddComponent(type);
        type.GetField("radius")?.SetValue(component, radius);
        // "image" is private — default GetField flags (public only) miss it
        // silently, which would make the write a permanent no-op on new objects.
        type.GetField("image", BindingFlags.Instance | BindingFlags.NonPublic)?
            .SetValue(component, go.GetComponent<MaskableGraphic>());
    }

    private static void SetIndependentCornerRadius(GameObject go, float radius)
    {
        // Never swap this for ImageWithRoundedCorners: its OnEnable destroys the
        // independent component on sight, and a uniform radius would round the
        // sheet's bottom edge too.
        var type = ResolveType(
            "Nobi.UiRoundedCorners.ImageWithIndependentRoundedCorners", ref cachedIndependentRoundedType);
        if (type == null)
        {
            Debug.LogWarning("[ItemEditSheetRestyleBuilder] ImageWithIndependentRoundedCorners not found on SheetRoot.");
            return;
        }

        var component = go.GetComponent(type);
        if (component == null) return;   // do not add one: the sheet already ships with it
        type.GetField("r")?.SetValue(component, new Vector4(radius, radius, radius, radius));
    }

    /// <summary>
    /// Binds a graphic to a semantic theme role via SerializedObject only —
    /// never ThemedColor.Configure, which repaints immediately and would bake
    /// the active palette's colour into the prefab.
    /// </summary>
    private static void BindTheme(GameObject go, ThemeRole role, bool preserveAlpha = true)
    {
        var graphic = go.GetComponent<Graphic>();
        if (graphic == null) return;

        var themed = go.GetComponent<ThemedColor>() ?? go.AddComponent<ThemedColor>();
        var so = new SerializedObject(themed);
        so.FindProperty("role").enumValueIndex = (int)role;
        so.FindProperty("target").objectReferenceValue = graphic;
        so.FindProperty("preserveAlpha").boolValue = preserveAlpha;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}

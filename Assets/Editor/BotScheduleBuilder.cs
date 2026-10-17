#if UNITY_EDITOR
using Automation.BotSettingsUI;
using Nobi.UiRoundedCorners;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Part B «расписание бота» (spec 2026-10-14 §4 B2): ADDITIVE, idempotent surgery on BotSettings.prefab — adds the
/// «РАСПИСАНИЕ РАБОТЫ» section to the Основное tab, right above «Удалить бота»:
///
///   SectionHeader_РАСПИСАНИЕ  clone of the tab's first SectionHeader
///   ToggleRow_Расписание      clone of BotSettings.whatsappRow (the iOS toggle card), OFF, the neutral switch green
///   ScheduleHoursRow          «С» | «До» side by side — clones of BotSettings.BotNameField, so they inherit the keyboard
///                             contract BusinessContactFieldsTests pins (Default keypad, MultiLineSubmit, autocorrect off,
///                             DeferredDismissInputField, no scrim)
///   ScheduleDaysRow           seven DayChip pills Пн–Вс (the chip owns its colours — no ThemedColor on it)
///   ScheduleError             the one error line (Destructive, hidden until a value is wrong)
///   ScheduleSyncStatus        the delivery line (fix rounds 3–4, I-1: «Расписание ещё не дошло до сервера» / «Не удалось
///                             сохранить расписание — проверьте интернет» / «Сервер не принял расписание — попробуйте
///                             сохранить снова»), hidden; NO ThemedColor — BotSettings paints it by state (InkSecondary /
///                             Destructive), a ThemedColor would be a second owner
///   ScheduleHint              «Вне часов бот не отвечает сам — подсказки в «Вместе» работают»
///
/// plus FormKeyboardScroll on the Основное tab (the fields sit low enough for the keyboard to cover them) and the six
/// serialized refs on BotSettings. Every clone source is resolved through BotSettings' serialized refs, never a node
/// name; every object this tool creates is found again by its own name, and every value is compared before it is
/// written — a second run changes nothing and saves nothing. It deletes nothing. Never confuse it with the destructive
/// Tools/Rebuild Bot Settings Prefabs. All copy comes from ScheduleCopy.
/// </summary>
public static class BotScheduleBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";
    private const string Tag = "[BotScheduleBuilder]";

    private const string HeaderGoName = "SectionHeader_РАСПИСАНИЕ";
    private const string ToggleGoName = "ToggleRow_Расписание";
    private const string HoursGoName = "ScheduleHoursRow";
    private const string StartGoName = "Field_С";
    private const string EndGoName = "Field_До";
    private const string DaysGoName = "ScheduleDaysRow";
    private const string ErrorGoName = "ScheduleError";
    private const string SyncStatusGoName = "ScheduleSyncStatus";
    private const string HintGoName = "ScheduleHint";
    private const string DeleteGoName = "DeleteBotButton";

    // Canvas reference units (1080 wide, match width). The tab's list is 1080 minus its own horizontal padding.
    private const float ReferenceWidth = 1080f;
    private const float HoursGap = 30f;          // = the tab list's own spacing
    private const float ChipHeight = 112f;       // a touch target (≥ 112 u), a capsule at radius 55
    private const float ChipGap = 12f;
    private const float ChipRadius = 55f;        // just under half the height: a clean capsule
    private const float ChipRing = 3f;
    private const float ChipLabelSize = 32f;
    private const float NoteSize = 30f;
    private const float ErrorHeight = 40f;

    [MenuItem("Tools/BotSettings/Add Schedule Section")]
    public static void Build() => Run();

    /// <summary>Batch entry: Tools/run-editor-builder.sh BotScheduleBuilder.BuildHeadless</summary>
    public static void BuildHeadless()
    {
        if (!Run()) return;
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} Headless build + save complete");
    }

    private static bool Run()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"{Tag} Failed to load {PrefabPath}");
            return false;
        }
        try
        {
            var settings = root.GetComponent<BotSettings>();
            if (settings == null || settings.BotNameField == null || settings.whatsappRow == null)
            {
                Debug.LogError($"{Tag} BotSettings, BotNameField or whatsappRow (the clone sources) not wired; aborting.");
                return false;
            }
            var so = new SerializedObject(settings);
            var general = so.FindProperty("General").objectReferenceValue as GameObject;
            var content = settings.BotNameField.transform.parent as RectTransform;
            var scroll = general != null ? general.GetComponent<ScrollRect>() : null;
            if (content == null || scroll == null || scroll.content != content)
            {
                Debug.LogError($"{Tag} Unexpected Основное tab hierarchy (BotNameField is not in General's scroll content); aborting.");
                return false;
            }
            var deleteButton = so.FindProperty("deleteBotButton").objectReferenceValue as Button;
            var anchor = deleteButton != null ? deleteButton.transform : content.Find(DeleteGoName);
            if (anchor == null || anchor.parent != content)
            {
                Debug.LogError($"{Tag} «Удалить бота» is not in the Основное tab's list; aborting.");
                return false;
            }
            var sourceHeader = content.GetComponentInChildren<SectionHeader>(true);
            var fontSource = settings.BotNameField.GetComponentInChildren<TextMeshProUGUI>(true);
            if (sourceHeader == null || fontSource == null)
            {
                Debug.LogError($"{Tag} No SectionHeader / label to clone in the Основное tab; aborting.");
                return false;
            }

            float listWidth = ListWidth(content);
            bool modified = false;
            var header = EnsureHeader(content, sourceHeader, ref modified);
            var toggle = EnsureToggleRow(content, settings.whatsappRow, ref modified);
            var hours = EnsureRow(content, HoursGoName, settings.BotNameField.GetComponent<RectTransform>().sizeDelta.y, HoursGap, ref modified);
            var start = EnsureTimeField(hours, StartGoName, settings.BotNameField, ScheduleCopy.FromLabel, BotSchedule.Default.StartMin,
                                        ScheduleCopy.StartPlaceholder, ref modified);
            var end = EnsureTimeField(hours, EndGoName, settings.BotNameField, ScheduleCopy.ToLabel, BotSchedule.Default.EndMin,
                                      ScheduleCopy.EndPlaceholder, ref modified);
            var days = EnsureRow(content, DaysGoName, ChipHeight, ChipGap, ref modified);
            var chips = EnsureDayChips(days, fontSource, listWidth, ref modified);
            var error = EnsureNote(content, ErrorGoName, ScheduleCopy.Invalid, ThemeRole.Destructive, fontSource, active: false, ref modified);
            modified |= SetHeight((RectTransform)error.transform, ErrorHeight);
            var hint = EnsureNote(content, HintGoName, ScheduleCopy.Hint, ThemeRole.InkTertiary, fontSource, active: true, ref modified);
            modified |= SetHeight((RectTransform)hint.transform, MeasuredHeight(hint, listWidth, hint.text, "hint"));
            var status = EnsureSyncStatus(content, fontSource, ref modified);
            modified |= SetHeight((RectTransform)status.transform, SyncStatusHeight(hint, listWidth));

            modified |= PlaceBefore(anchor, header.transform, toggle.transform, hours, days, error.transform, status.transform, hint.transform);
            modified |= EnsureKeyboardScroll(general);
            modified |= StampRefs(so, toggle, start, end, chips, error, status);

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"{Tag} Prefab updated at {PrefabPath}");
            }
            else
            {
                Debug.Log($"{Tag} Nothing to do — already applied.");
            }
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── the pieces ────────────────────────────────────────────────────────────────────────────────────────

    private static SectionHeader EnsureHeader(RectTransform content, SectionHeader source, ref bool modified)
    {
        var header = FindOrClone(content, HeaderGoName, source, ref modified);
        if (header.Text != ScheduleCopy.SectionTitle)
        {
            header.Text = ScheduleCopy.SectionTitle;
            modified = true;
        }
        return header;
    }

    private static ToggleRow EnsureToggleRow(RectTransform content, ToggleRow source, ref bool modified)
    {
        var row = FindOrClone(content, ToggleGoName, source, ref modified);
        if (row.Label != ScheduleCopy.ToggleLabel)
        {
            row.Label = ScheduleCopy.ToggleLabel;
            modified = true;
        }
        if (row.Toggle.isOn)
        {
            row.Toggle.SetIsOnWithoutNotify(false);   // a schedule is off by default
            modified = true;
        }
        // The source is the WhatsApp row, whose ON track is the channel's identity green — this switch is not a channel.
        var rowSo = new SerializedObject(row);
        var onColor = rowSo.FindProperty("onColor");
        if (onColor.colorValue != Theme.Fixed.SwitchOnGreen)
        {
            onColor.colorValue = Theme.Fixed.SwitchOnGreen;
            rowSo.ApplyModifiedPropertiesWithoutUndo();
            modified = true;
        }
        return row;
    }

    private static RectTransform EnsureRow(RectTransform content, string name, float height, float spacing, ref bool modified)
    {
        var rt = content.Find(name) as RectTransform;
        if (rt == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.layer = content.gameObject.layer;
            rt = (RectTransform)go.transform;
            rt.SetParent(content, false);
            modified = true;
        }
        modified |= SetHeight(rt, height);
        var layout = rt.GetComponent<HorizontalLayoutGroup>();
        if (layout.spacing != spacing || layout.childAlignment != TextAnchor.UpperLeft || !layout.childControlWidth
            || layout.childControlHeight || !layout.childForceExpandWidth || layout.childForceExpandHeight
            || layout.padding.left != 0 || layout.padding.right != 0 || layout.padding.top != 0 || layout.padding.bottom != 0)
        {
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;      // the row splits its width; the cards keep their authored height
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(0, 0, 0, 0);
            modified = true;
        }
        return rt;
    }

    private static EditableField EnsureTimeField(RectTransform row, string name, EditableField source, string label, int minutes,
                                                 string placeholder, ref bool modified)
    {
        var field = FindOrClone(row, name, source, ref modified);
        if (field.Label != label)
        {
            field.Label = label;
            modified = true;
        }
        string value = ScheduleModel.FormatHhMm(minutes);   // the default window — what a fresh bot shows
        if (field.Value != value)
        {
            field.Value = value;
            modified = true;
        }
        if (field.InputField != null && field.InputField.placeholder is TMP_Text hint && hint.text != placeholder)
        {
            hint.text = placeholder;
            modified = true;
        }
        return field;
    }

    private static DayChip[] EnsureDayChips(RectTransform row, TextMeshProUGUI fontSource, float listWidth, ref bool modified)
    {
        float width = (listWidth - ChipGap * (ScheduleModel.DaysInWeek - 1)) / ScheduleModel.DaysInWeek;
        var chips = new DayChip[ScheduleModel.DaysInWeek];
        for (int day = 0; day < chips.Length; day++)
        {
            string name = $"DayChip_{ScheduleCopy.DayName(day)}";
            var existing = row.Find(name);
            var chip = existing != null ? existing.GetComponent<DayChip>() : null;
            if (chip == null) chip = CreateChip(row, name, ref modified);
            modified |= ConfigureChip(chip, day, width, fontSource);
            if (chip.transform.GetSiblingIndex() != day)
            {
                chip.transform.SetSiblingIndex(day);   // Пн first, as the mask's bits
                modified = true;
            }
            chips[day] = chip;
        }
        return chips;
    }

    private static DayChip CreateChip(RectTransform row, string name, ref bool modified)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ImageWithRoundedCorners), typeof(Button), typeof(DayChip));
        go.layer = row.gameObject.layer;
        go.transform.SetParent(row, false);
        var face = new GameObject("Face", typeof(RectTransform), typeof(Image), typeof(ImageWithRoundedCorners));
        face.layer = go.layer;
        face.transform.SetParent(go.transform, false);
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.layer = go.layer;
        label.transform.SetParent(go.transform, false);
        modified = true;
        return go.GetComponent<DayChip>();
    }

    // The authored state is the default one — every day selected — in the app's default (dark) theme; DayChip repaints
    // from Theme at runtime, so these colours only make the prefab read right in the editor.
    private static bool ConfigureChip(DayChip chip, int day, float width, TextMeshProUGUI fontSource)
    {
        bool modified = false;
        var rt = (RectTransform)chip.transform;
        if (!Mathf.Approximately(rt.sizeDelta.x, width) || !Mathf.Approximately(rt.sizeDelta.y, ChipHeight))
        {
            rt.sizeDelta = new Vector2(width, ChipHeight);
            modified = true;
        }

        var ring = chip.GetComponent<Image>();
        var faceRt = (RectTransform)chip.transform.Find("Face");
        var face = faceRt.GetComponent<Image>();
        var label = chip.transform.Find("Label").GetComponent<TextMeshProUGUI>();
        var fill = Theme.Dark.Resolve(ThemeRole.AccentFill);
        modified |= SetColor(ring, fill, raycast: true);
        modified |= SetColor(face, fill, raycast: false);
        modified |= SetRadius(chip.GetComponent<ImageWithRoundedCorners>(), ChipRadius);
        modified |= SetRadius(faceRt.GetComponent<ImageWithRoundedCorners>(), ChipRadius - ChipRing);
        modified |= Stretch(faceRt, ChipRing);
        modified |= Stretch((RectTransform)label.transform, 0f);
        modified |= ConfigureText(label, ScheduleCopy.DayName(day), ChipLabelSize, fontSource.fontWeight, TextAlignmentOptions.Center,
                                  Theme.Dark.Resolve(ThemeRole.AccentOnFill), fontSource, wrap: false);

        var button = chip.GetComponent<Button>();
        if (button.targetGraphic != ring || button.transition != Selectable.Transition.None)
        {
            button.targetGraphic = ring;                     // the ring takes the tap — recoloured, never disabled
            button.transition = Selectable.Transition.None;  // the chip's own colour flip is the feedback
            modified = true;
        }

        var chipSo = new SerializedObject(chip);
        modified |= SetRef(chipSo, "ring", ring);
        modified |= SetRef(chipSo, "face", face);
        modified |= SetRef(chipSo, "label", label);
        modified |= SetRef(chipSo, "button", button);
        var dayProp = chipSo.FindProperty("day");
        if (dayProp.intValue != day)
        {
            dayProp.intValue = day;
            modified = true;
        }
        chipSo.ApplyModifiedPropertiesWithoutUndo();
        return modified;
    }

    private static TextMeshProUGUI EnsureNote(RectTransform content, string name, string text, ThemeRole role, TextMeshProUGUI fontSource,
                                              bool active, ref bool modified)
    {
        var rt = content.Find(name) as RectTransform;
        if (rt == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(ThemedColor));
            go.layer = content.gameObject.layer;
            rt = (RectTransform)go.transform;
            rt.SetParent(content, false);
            go.SetActive(active);
            modified = true;
        }
        var tmp = rt.GetComponent<TextMeshProUGUI>();
        modified |= ConfigureText(tmp, text, NoteSize, FontWeight.Regular, TextAlignmentOptions.TopLeft, Theme.Dark.Resolve(role),
                                  fontSource, wrap: true);
        modified |= BindTheme(rt.GetComponent<ThemedColor>(), role, tmp);
        if (rt.gameObject.activeSelf != active)
        {
            rt.gameObject.SetActive(active);
            modified = true;
        }
        return tmp;
    }

    // The delivery line: a note WITHOUT a ThemedColor — its colour follows the delivery state and is painted by
    // BotSettings.ApplyScheduleSyncState (a ThemedColor would repaint a failed line grey on every theme change). Authored
    // hidden, with the pending copy in the app's default (dark) theme so the prefab reads right in the editor.
    private static TextMeshProUGUI EnsureSyncStatus(RectTransform content, TextMeshProUGUI fontSource, ref bool modified)
    {
        var rt = content.Find(SyncStatusGoName) as RectTransform;
        if (rt == null)
        {
            var go = new GameObject(SyncStatusGoName, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = content.gameObject.layer;
            rt = (RectTransform)go.transform;
            rt.SetParent(content, false);
            go.SetActive(false);
            modified = true;
        }
        var tmp = rt.GetComponent<TextMeshProUGUI>();
        modified |= ConfigureText(tmp, ScheduleCopy.SyncPending, NoteSize, FontWeight.Regular, TextAlignmentOptions.TopLeft,
                                  Theme.Dark.Resolve(ThemeRole.InkSecondary), fontSource, wrap: true);
        if (rt.gameObject.activeSelf)
        {
            rt.gameObject.SetActive(false);
            modified = true;
        }
        return tmp;
    }

    // One box for every state — the tallest of the copies, never under the error line's one-line box. Measured on the
    // hint: the same font, size and wrapping, and an ACTIVE label (the line itself is authored hidden).
    private static float SyncStatusHeight(TextMeshProUGUI hint, float width)
        => Mathf.Max(ErrorHeight, MeasuredHeight(hint, width, ScheduleCopy.SyncPending, "delivery line (pending)"),
                     MeasuredHeight(hint, width, ScheduleCopy.SyncFailed, "delivery line (failed)"),
                     MeasuredHeight(hint, width, ScheduleCopy.SyncRejected, "delivery line (rejected)"));

    // A note wraps in the list's width; its box is its measured height (the static SF Pro atlas measures headless). Measured
    // on a label inside the loaded prefab — no probe object, so a menu run never touches the open scene.
    private static float MeasuredHeight(TextMeshProUGUI label, float width, string text, string what)
    {
        float measured = label.GetPreferredValues(text, width, 0f).y;
        // At least one line; a never-laid-out TMP can report ~1/10 of the truth (ConfirmCardFitter's finding).
        bool sane = measured >= NoteSize && measured < 400f;
        Debug.Log($"{Tag} {what} measured {measured:0.##} u at width {width} ({(sane ? "used" : "fallback 2 lines")})");
        return sane ? Mathf.Ceil(measured) : Mathf.Ceil(NoteSize * 2.5f);
    }

    private static bool PlaceBefore(Transform anchor, params Transform[] block)
    {
        bool inPlace = true;
        for (int i = 0; i < block.Length; i++)
            if (block[i].GetSiblingIndex() != anchor.GetSiblingIndex() - block.Length + i) inPlace = false;
        if (inPlace) return false;
        foreach (var item in block)
        {
            int target = anchor.GetSiblingIndex();
            if (item.GetSiblingIndex() < target) target--;
            item.SetSiblingIndex(target);
        }
        return true;
    }

    private static bool EnsureKeyboardScroll(GameObject general)
    {
        if (general.GetComponent<FormKeyboardScroll>() != null) return false;
        general.AddComponent<FormKeyboardScroll>();
        return true;
    }

    private static bool StampRefs(SerializedObject so, ToggleRow toggle, EditableField start, EditableField end, DayChip[] chips,
                                  TextMeshProUGUI error, TextMeshProUGUI status)
    {
        bool modified = SetRef(so, "scheduleRow", toggle);
        modified |= SetRef(so, "scheduleStartField", start);
        modified |= SetRef(so, "scheduleEndField", end);
        modified |= SetRef(so, "scheduleError", error);
        modified |= SetRef(so, "scheduleSyncStatus", status);
        var array = so.FindProperty("scheduleDayChips");
        if (array.arraySize != chips.Length)
        {
            array.arraySize = chips.Length;
            modified = true;
        }
        for (int i = 0; i < chips.Length; i++)
        {
            var element = array.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue != chips[i])
            {
                element.objectReferenceValue = chips[i];
                modified = true;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return modified;
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────────

    private static T FindOrClone<T>(Transform parent, string name, T source, ref bool modified) where T : Component
    {
        var existing = parent.Find(name);
        if (existing != null) return existing.GetComponent<T>();
        var clone = Object.Instantiate(source, parent);
        clone.gameObject.name = name;
        modified = true;
        return clone;
    }

    private static float ListWidth(RectTransform content)
    {
        var layout = content.GetComponent<VerticalLayoutGroup>();
        return layout != null ? ReferenceWidth - layout.padding.left - layout.padding.right : ReferenceWidth;
    }

    private static bool SetHeight(RectTransform rt, float height)
    {
        if (Mathf.Approximately(rt.sizeDelta.y, height)) return false;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);   // the list controls the width, never the height
        return true;
    }

    private static bool Stretch(RectTransform rt, float inset)
    {
        var min = new Vector2(inset, inset);
        var max = new Vector2(-inset, -inset);
        if (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one && rt.offsetMin == min && rt.offsetMax == max) return false;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = min;
        rt.offsetMax = max;
        return true;
    }

    private static bool SetColor(Image image, Color color, bool raycast)
    {
        if (image.color == color && image.raycastTarget == raycast) return false;
        image.color = color;
        image.raycastTarget = raycast;
        return true;
    }

    // Both fields through one SerializedObject ("image" is private; Validate() would only set it on enable).
    private static bool SetRadius(ImageWithRoundedCorners corners, float radius)
    {
        var cornersSo = new SerializedObject(corners);
        var radiusProp = cornersSo.FindProperty("radius");
        var imageProp = cornersSo.FindProperty("image");
        var graphic = corners.GetComponent<Image>();
        if (Mathf.Approximately(radiusProp.floatValue, radius) && imageProp.objectReferenceValue == graphic) return false;
        radiusProp.floatValue = radius;
        imageProp.objectReferenceValue = graphic;
        cornersSo.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static bool ConfigureText(TextMeshProUGUI tmp, string text, float size, FontWeight weight, TextAlignmentOptions alignment,
                                      Color color, TextMeshProUGUI fontSource, bool wrap)
    {
        var wrapping = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        if (tmp.text == text && Mathf.Approximately(tmp.fontSize, size) && tmp.fontWeight == weight && tmp.alignment == alignment
            && tmp.color == color && tmp.font == fontSource.font && tmp.fontSharedMaterial == fontSource.fontSharedMaterial
            && tmp.textWrappingMode == wrapping && !tmp.raycastTarget && !tmp.enableAutoSizing)
            return false;
        tmp.font = fontSource.font;
        tmp.fontSharedMaterial = fontSource.fontSharedMaterial;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontWeight = weight;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.textWrappingMode = wrapping;
        tmp.enableAutoSizing = false;
        tmp.raycastTarget = false;
        return true;
    }

    // Get-or-add is done by the caller's typeof list; the role/target/alpha go through SerializedObject only — never
    // ThemedColor.Configure, which repaints at once (the PromptSuggestionsBuilder rule).
    private static bool BindTheme(ThemedColor themed, ThemeRole role, Graphic target)
    {
        var themedSo = new SerializedObject(themed);
        var roleProp = themedSo.FindProperty("role");
        var targetProp = themedSo.FindProperty("target");
        var alphaProp = themedSo.FindProperty("preserveAlpha");
        if (roleProp.enumValueIndex == (int)role && targetProp.objectReferenceValue == target && alphaProp.boolValue) return false;
        roleProp.enumValueIndex = (int)role;
        targetProp.objectReferenceValue = target;
        alphaProp.boolValue = true;
        themedSo.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static bool SetRef(SerializedObject so, string property, Object value)
    {
        var prop = so.FindProperty(property);
        if (prop.objectReferenceValue == value) return false;
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }
}
#endif

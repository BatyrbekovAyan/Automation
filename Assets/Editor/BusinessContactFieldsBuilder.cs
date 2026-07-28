#if UNITY_EDITOR
using Automation.BotSettingsUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ADDITIVE, idempotent surgery on BotSettings.prefab — adds the
/// «КОНТАКТЫ И ИНФОРМАЦИЯ» section (Телефон / Часы работы / Адрес /
/// Instagram / Email) to the Бизнес tab.
///
/// Deliberately NOT part of BotSettingsRebuilder: that tool destroys and
/// rebuilds every top-level child, which wipes the incremental wiring a
/// dozen other builders apply (SwipeBack, sticky add button, confirm
/// popups, uploaded-files section, scrollable text areas…). Re-running it
/// to add two fields broke the whole settings screen once already.
///
/// This tool instead:
///   • clones the existing BotNameField card 5× (so styling, TMP fonts,
///     RoundedCorners, shadow and the FocusScrim wiring come along for
///     free — Instantiate remaps subtree refs and preserves external ones)
///   • clones the existing SectionHeader for the new section title
///   • shrinks the description card so the tab isn't dominated by it
///   • makes the Бизнес tab scroll using the same no-reparent pattern as
///     BotSettingsScrollableTextAreaBuilder (ScrollRect + RectMask2D on
///     the tab root, which doubles as the viewport) — nothing is
///     reparented, nothing is destroyed
///   • stamps the five serialized refs on BotSettings
///
/// Safe to re-run: every step checks its own end state first.
/// </summary>
public static class BusinessContactFieldsBuilder
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";

    // The description card is 800 tall in the shipped prefab — more than
    // half the ~1510 tab viewport. It scrolls internally (ScrollableTextArea),
    // so a shorter window loses no capability and leaves the contact cards
    // reachable.
    private const float DescriptionHeight = 360f;

    private const string ContactSectionTitle = "КОНТАКТЫ И ИНФОРМАЦИЯ";
    private const string ContactSectionGoName = "SectionHeader_КОНТАКТЫ";

    private readonly struct ContactField
    {
        public readonly string Property;
        public readonly string Label;
        public readonly string GoName;
        public readonly string Placeholder;

        public ContactField(string property, string label, string goName, string placeholder)
        {
            Property = property;
            Label = label;
            GoName = goName;
            Placeholder = placeholder;
        }
    }

    // Placeholders are concrete example values, not a repeat of the label —
    // they teach the expected format (KZ phone shape, opening-hours phrasing,
    // city-first address). Cloning carries the source card's placeholder
    // over, so each one must be set explicitly or all five read «Ассистент».
    private static readonly ContactField[] Contacts =
    {
        new ContactField("PhoneField",     "Телефон",     "Field_Телефон",     "+7 707 123 45 67"),
        new ContactField("HoursField",     "Часы работы", "Field_ЧасыРаботы",  "Пн–Сб 09:00–19:00"),
        new ContactField("AddressField",   "Адрес",       "Field_Адрес",       "г. Алматы, ул. Толе би 285"),
        new ContactField("InstagramField", "Instagram",   "Field_Instagram",   "@my_shop"),
        new ContactField("EmailField",     "Email",       "Field_Email",       "info@company.kz"),
    };

    [MenuItem("Tools/BotSettings/Add Business Contact Fields")]
    public static void Build()
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"[BusinessContactFields] Failed to load prefab at {PrefabPath}");
            return;
        }

        try
        {
            var settings = prefabRoot.GetComponent<BotSettings>();
            if (settings == null)
            {
                Debug.LogError("[BusinessContactFields] BotSettings component not found on prefab root.");
                return;
            }
            if (settings.BusinessField == null)
            {
                Debug.LogError("[BusinessContactFields] BusinessField not wired; aborting.");
                return;
            }
            if (settings.BotNameField == null)
            {
                Debug.LogError("[BusinessContactFields] BotNameField (clone source) not wired; aborting.");
                return;
            }

            var descriptionRt = (RectTransform)settings.BusinessField.transform;
            var content = descriptionRt.parent as RectTransform;
            var tab = content != null ? content.parent as RectTransform : null;
            if (content == null || tab == null)
            {
                Debug.LogError("[BusinessContactFields] Unexpected Бизнес tab hierarchy; aborting.");
                return;
            }

            var modified = false;
            modified |= ShrinkDescription(descriptionRt);
            modified |= MakeTabScrollable(tab, content);
            modified |= AddContactSection(settings, content);
            modified |= UnifyKeyboardConfig(prefabRoot);
            modified |= DetachScrimEditInline(prefabRoot, tab);

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log($"[BusinessContactFields] Prefab updated at {PrefabPath}");
            }
            else
            {
                Debug.Log("[BusinessContactFields] Nothing to do — already applied.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool ShrinkDescription(RectTransform descriptionRt)
    {
        var size = descriptionRt.sizeDelta;
        if (Mathf.Approximately(size.y, DescriptionHeight)) return false;

        descriptionRt.sizeDelta = new Vector2(size.x, DescriptionHeight);
        Debug.Log($"[BusinessContactFields] Description card {size.y} → {DescriptionHeight}.");
        return true;
    }

    // Same no-reparent pattern BotSettingsScrollableTextAreaBuilder uses on
    // the description card: the tab root IS the viewport (clipped by
    // RectMask2D), Content is the scroll content and sizes itself via
    // ContentSizeFitter driven by the existing VerticalLayoutGroup.
    private static bool MakeTabScrollable(RectTransform tab, RectTransform content)
    {
        var modified = false;

        var contentMin = new Vector2(0f, 1f);
        var contentMax = new Vector2(1f, 1f);
        var contentPivot = new Vector2(0.5f, 1f);
        if (content.anchorMin != contentMin || content.anchorMax != contentMax || content.pivot != contentPivot)
        {
            content.anchorMin = contentMin;
            content.anchorMax = contentMax;
            content.pivot = contentPivot;
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, content.sizeDelta.y);
            modified = true;
        }

        var fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            modified = true;
        }
        if (fitter.verticalFit != ContentSizeFitter.FitMode.PreferredSize)
        {
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            modified = true;
        }
        if (fitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained)
        {
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            modified = true;
        }

        var scroll = tab.GetComponent<ScrollRect>();
        if (scroll == null)
        {
            scroll = tab.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 30f;
            modified = true;
        }
        if (scroll.viewport != tab || scroll.content != content)
        {
            scroll.viewport = tab;
            scroll.content = content;
            modified = true;
        }

        if (tab.GetComponent<RectMask2D>() == null)
        {
            tab.gameObject.AddComponent<RectMask2D>();
            modified = true;
        }

        return modified;
    }

    private static bool AddContactSection(BotSettings settings, RectTransform content)
    {
        var so = new SerializedObject(settings);
        var modified = false;

        // Section header — clone the existing one so typography/spacing match.
        var sourceHeader = settings.BusinessField.transform.parent.GetComponentInChildren<SectionHeader>();
        if (sourceHeader == null)
        {
            Debug.LogError("[BusinessContactFields] No SectionHeader to clone; aborting.");
            return false;
        }
        var existingHeader = content.Find(ContactSectionGoName);
        if (existingHeader == null)
        {
            var header = Object.Instantiate(sourceHeader, content);
            header.gameObject.name = ContactSectionGoName;
            header.Text = ContactSectionTitle;
            header.transform.SetAsLastSibling();
            modified = true;
        }
        else
        {
            var header = existingHeader.GetComponent<SectionHeader>();
            if (header != null && header.Text != ContactSectionTitle)
            {
                header.Text = ContactSectionTitle;
                modified = true;
            }
        }

        // Contact cards — clone the canonical single-line field. Labels,
        // placeholders and keyboards are re-applied on every run (not just at
        // creation) so this tool stays the single source of truth for them.
        foreach (var contact in Contacts)
        {
            var prop = so.FindProperty(contact.Property);
            if (prop == null)
            {
                Debug.LogError($"[BusinessContactFields] BotSettings has no '{contact.Property}' field. " +
                               "Add the serialized fields to BotSettings.cs first.");
                return modified;
            }

            var existing = content.Find(contact.GoName);
            EditableField field;
            if (existing != null)
            {
                field = existing.GetComponent<EditableField>();
            }
            else
            {
                field = Object.Instantiate(settings.BotNameField, content);
                field.gameObject.name = contact.GoName;
                field.transform.SetAsLastSibling();
                modified = true;
            }

            if (field == null)
            {
                Debug.LogError($"[BusinessContactFields] Could not create {contact.GoName}; aborting.");
                return modified;
            }

            if (field.Label != contact.Label)
            {
                field.Label = contact.Label;
                modified = true;
            }
            if (!string.IsNullOrEmpty(field.Value))
            {
                field.Value = string.Empty;
                modified = true;
            }

            modified |= ApplyPlaceholder(field, contact.Placeholder);

            if (prop.objectReferenceValue != field)
            {
                prop.objectReferenceValue = field;
                modified = true;
            }
        }

        if (modified)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[BusinessContactFields] Applied {Contacts.Length} contact fields + section header.");
        }
        return modified;
    }

    // The cloned card carries the SOURCE field's placeholder ("Ассистент"),
    // which is why every contact card read the same hint before this ran.
    private static bool ApplyPlaceholder(EditableField field, string placeholder)
    {
        var input = field.InputField;
        var target = input != null ? input.placeholder as TMP_Text : null;
        if (target == null)
        {
            Debug.LogWarning($"[BusinessContactFields] {field.name} has no TMP placeholder to set.");
            return false;
        }
        if (target.text == placeholder) return false;

        target.text = placeholder;
        return true;
    }

    // ONE keyboard configuration for every input — total products-sheet
    // parity. ANY per-field difference restarts the IME session on a focus
    // switch, and the restart redelivers pending composition into the newly
    // focused field (the cross-field text duplication):
    //   • mixed LINE types swap the native input view type → restart;
    //   • mixed KEYBOARD/content types swap the keypad layout → restart,
    //     even while the keyboard visibly stays up. This is why the PhonePad
    //     and EmailAddress keypads had to go — the sheet is clean precisely
    //     because all its fields share one Standard/Default keyboard.
    // Single-line-looking fields use MultiLineSubmit (Enter still submits);
    // real textareas keep MultiLineNewline.
    private static bool UnifyKeyboardConfig(GameObject prefabRoot)
    {
        var modified = false;
        foreach (var input in prefabRoot.GetComponentsInChildren<TMP_InputField>(true))
        {
            if (input.contentType != TMP_InputField.ContentType.Standard
                && input.contentType != TMP_InputField.ContentType.Custom)
            {
                input.contentType = TMP_InputField.ContentType.Standard;
                modified = true;
            }
            // AUTOCORRECT OFF (InputType.Standard). TMP's Standard contentType
            // silently enables iOS autocorrection, and iOS runs the predictive
            // session on the ONE hidden native text field every Unity input
            // shares — on focus switches it can replay/commit the previous
            // field's content into the newly focused one (device repro:
            // rapid cross-taps duplicate text between fields). Assigning
            // inputType flips contentType to Custom, which keeps the other
            // traits.
            if (input.inputType != TMP_InputField.InputType.Standard)
            {
                input.inputType = TMP_InputField.InputType.Standard;
                modified = true;
            }
            if (input.keyboardType != TouchScreenKeyboardType.Default)
            {
                input.keyboardType = TouchScreenKeyboardType.Default;
                modified = true;
            }
            if (input.lineType == TMP_InputField.LineType.SingleLine)
            {
                input.lineType = TMP_InputField.LineType.MultiLineSubmit;
                modified = true;
            }
        }
        return modified;
    }

    // Products-sheet parity: fields edit INLINE — no scrim, no raise. The
    // scrim's modal design forced a full keyboard dismiss/reopen on every
    // field switch, and that IME session restart is the window where the
    // shared native buffer erases/copies text across fields; three rounds of
    // guards lost to it, while the scrimless sheet never exhibits it. The
    // FocusScrim component stays in the prefab (dormant, zero consumers);
    // FormKeyboardScroll on the tab takes over the scroll-above-keyboard
    // behaviour for covered fields.
    private static bool DetachScrimEditInline(GameObject prefabRoot, RectTransform tab)
    {
        var modified = false;

        foreach (var field in prefabRoot.GetComponentsInChildren<EditableField>(true))
        {
            var so = new SerializedObject(field);
            var scrimProp = so.FindProperty("scrim");
            if (scrimProp != null && scrimProp.objectReferenceValue != null)
            {
                scrimProp.objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
                modified = true;
            }
        }

        if (tab.GetComponent<FormKeyboardScroll>() == null)
        {
            tab.gameObject.AddComponent<FormKeyboardScroll>();
            modified = true;
        }

        return modified;
    }

}
#endif

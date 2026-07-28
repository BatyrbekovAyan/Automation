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

        public ContactField(string property, string label, string goName)
        {
            Property = property;
            Label = label;
            GoName = goName;
        }
    }

    private static readonly ContactField[] Contacts =
    {
        new ContactField("PhoneField",     "Телефон",     "Field_Телефон"),
        new ContactField("HoursField",     "Часы работы", "Field_ЧасыРаботы"),
        new ContactField("AddressField",   "Адрес",       "Field_Адрес"),
        new ContactField("InstagramField", "Instagram",   "Field_Instagram"),
        new ContactField("EmailField",     "Email",       "Field_Email"),
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
        if (AllContactsWired(so))
        {
            return false;
        }

        // Section header — clone the existing one so typography/spacing match.
        var sourceHeader = settings.BusinessField.transform.parent.GetComponentInChildren<SectionHeader>();
        if (sourceHeader == null)
        {
            Debug.LogError("[BusinessContactFields] No SectionHeader to clone; aborting.");
            return false;
        }
        if (content.Find(ContactSectionGoName) == null)
        {
            var header = Object.Instantiate(sourceHeader, content);
            header.gameObject.name = ContactSectionGoName;
            header.Text = ContactSectionTitle;
            header.transform.SetAsLastSibling();
        }

        // Contact cards — clone the canonical single-line field.
        foreach (var contact in Contacts)
        {
            var prop = so.FindProperty(contact.Property);
            if (prop == null)
            {
                Debug.LogError($"[BusinessContactFields] BotSettings has no '{contact.Property}' field. " +
                               "Add the serialized fields to BotSettings.cs first.");
                return false;
            }

            var existing = content.Find(contact.GoName);
            EditableField field = existing != null
                ? existing.GetComponent<EditableField>()
                : Object.Instantiate(settings.BotNameField, content);

            if (field == null)
            {
                Debug.LogError($"[BusinessContactFields] Could not create {contact.GoName}; aborting.");
                return false;
            }

            field.gameObject.name = contact.GoName;
            field.Label = contact.Label;
            field.Value = string.Empty;
            field.transform.SetAsLastSibling();
            ApplyKeyboard(field, contact.Property);

            prop.objectReferenceValue = field;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"[BusinessContactFields] Added {Contacts.Length} contact fields + section header.");
        return true;
    }

    private static bool AllContactsWired(SerializedObject so)
    {
        foreach (var contact in Contacts)
        {
            var prop = so.FindProperty(contact.Property);
            if (prop == null || prop.objectReferenceValue == null) return false;
        }
        return true;
    }

    // TMP's contentType setter overwrites inputType/keyboardType/validation,
    // so contentType must be assigned BEFORE keyboardType or the keypad
    // choice is silently reverted.
    private static void ApplyKeyboard(EditableField field, string property)
    {
        var input = field.InputField;
        if (input == null) return;

        switch (property)
        {
            case "PhoneField":
                input.contentType = TMP_InputField.ContentType.Standard;
                input.keyboardType = TouchScreenKeyboardType.PhonePad;
                break;
            case "EmailField":
                input.contentType = TMP_InputField.ContentType.EmailAddress;
                input.keyboardType = TouchScreenKeyboardType.EmailAddress;
                break;
            default:
                input.contentType = TMP_InputField.ContentType.Standard;
                input.keyboardType = TouchScreenKeyboardType.Default;
                break;
        }
        input.lineType = TMP_InputField.LineType.SingleLine;
    }
}
#endif

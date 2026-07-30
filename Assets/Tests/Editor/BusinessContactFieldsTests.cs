using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Automation.BotSettingsUI;

/// <summary>
/// Covers the composed n8n "Business" payload and guards the prefab wiring
/// that the Tools/BotSettings/Add Business Contact Fields builder produces.
/// The prefab guard is the check that would have caught the silent
/// null-ref stamping of the earlier (full-rebuild) attempt.
/// </summary>
public class BusinessContactFieldsTests
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";

    [Test]
    public void DescriptionOnly_NoContactBlock()
    {
        var result = Manager.ComposeBusinessKnowledge("Магазин", "", "", "", "", "");
        Assert.AreEqual("About Business:\nМагазин", result);
        StringAssert.DoesNotContain("Контакты:", result);
    }

    [Test]
    public void AllFields_LabeledBlockInOrder()
    {
        var result = Manager.ComposeBusinessKnowledge(
            "Магазин", "+7700", "9-19", "Алматы", "@shop", "a@b.kz");
        var expected =
            "About Business:\nМагазин\n\n" +
            "Контакты:\n" +
            "Телефон: +7700\n" +
            "Часы работы: 9-19\n" +
            "Адрес: Алматы\n" +
            "Instagram: @shop\n" +
            "Email: a@b.kz";
        Assert.AreEqual(expected, result);
    }

    [Test]
    public void PartialFields_OnlyNonEmptyLines()
    {
        var result = Manager.ComposeBusinessKnowledge(
            "Магазин", "+7700", "", "", "", "a@b.kz");
        var expected =
            "About Business:\nМагазин\n\n" +
            "Контакты:\n" +
            "Телефон: +7700\n" +
            "Email: a@b.kz";
        Assert.AreEqual(expected, result);
    }

    [Test]
    public void ContactsAllEmpty_HeaderAndDescriptionOnly()
    {
        var result = Manager.ComposeBusinessKnowledge("", "", "", "", "", "");
        Assert.AreEqual("About Business:\n", result);
    }

    [Test]
    public void WhitespaceOnlyContact_IsSkipped()
    {
        var result = Manager.ComposeBusinessKnowledge("Магазин", "   ", "", "", "", "");
        StringAssert.DoesNotContain("Контакты:", result);
    }

    [Test]
    public void ContactKeysAndFieldOrder_StayAligned()
    {
        Assert.AreEqual(
            new[] { "Phone", "Hours", "Address", "Instagram", "Email" },
            BotSettings.ContactKeys,
            "Contact key order feeds PlayerPrefs suffixes and must not drift.");
    }

    [Test]
    public void BotSettingsPrefab_HasAllContactFieldsWired()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "BotSettings.prefab not found");

        var settings = prefab.GetComponent<BotSettings>();
        Assert.IsNotNull(settings, "BotSettings component missing on prefab");
        Assert.IsNotNull(settings.BusinessField, "BusinessField not wired");

        var contacts = settings.ContactFields;
        Assert.AreEqual(BotSettings.ContactKeys.Length, contacts.Length);
        for (int i = 0; i < contacts.Length; i++)
        {
            Assert.IsNotNull(contacts[i],
                $"Contact field '{BotSettings.ContactKeys[i]}' not wired on the prefab. " +
                "Run Tools/BotSettings/Add Business Contact Fields.");
        }
    }

    // Every bot-settings input must be DeferredDismissInputField: the
    // FocusScrim field-to-field handoff relies on its smooth-switch branch to
    // keep the OS keyboard up (no dip) and to null the deselected field's
    // soft-keyboard reference (no cross-field text bleed). A rebuild or
    // builder step that recreates an input as stock TMP_InputField silently
    // reintroduces both device bugs.
    [Test]
    public void BotSettingsPrefab_AllInputsAreDeferredDismiss()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab);

        foreach (var input in prefab.GetComponentsInChildren<TMPro.TMP_InputField>(true))
        {
            Assert.IsInstanceOf<DeferredDismissInputField>(input,
                $"'{input.transform.parent.name}/{input.name}' is a stock TMP_InputField — " +
                "keyboard smooth-switch needs DeferredDismissInputField.");
        }
    }

    // Products-sheet parity: fields edit inline, no scrim. The scrim's modal
    // dismiss/reopen cycle restarted the IME session on every field switch —
    // the window where text erased/copied across fields. Re-wiring any field
    // to a scrim reintroduces that cycle.
    [Test]
    public void BotSettingsPrefab_FieldsEditInline_NoScrim()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab);

        foreach (var field in prefab.GetComponentsInChildren<EditableField>(true))
        {
            var scrimRef = new UnityEditor.SerializedObject(field).FindProperty("scrim");
            Assert.IsNotNull(scrimRef, "EditableField no longer has a 'scrim' field?");
            Assert.IsNull(scrimRef.objectReferenceValue,
                $"'{field.name}' is wired to a FocusScrim — inline editing (sheet parity) bans the scrim.");
        }

        var businessTab = prefab.GetComponent<BotSettings>().BusinessField
            .transform.parent.parent;
        Assert.IsNotNull(businessTab.GetComponent<FormKeyboardScroll>(),
            "Business tab lost FormKeyboardScroll — covered fields would sit under the keyboard.");
    }

    // Mixed line types make the OS swap the native input view type on focus
    // switches — a full IME session restart that both dips the keyboard and
    // corrupts text across fields. The products sheet fixed this by unifying
    // on MultiLineSubmit; the full rebuild silently wiped that fix
    // (re-applied 2026-07-28). SingleLine is banned prefab-wide.
    [Test]
    public void BotSettingsPrefab_UniformKeyboardConfig()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab);

        var settings = prefab.GetComponent<BotSettings>();
        var phoneInput = settings.PhoneField != null ? settings.PhoneField.InputField : null;
        var emailInput = settings.EmailField != null ? settings.EmailField.InputField : null;

        foreach (var input in prefab.GetComponentsInChildren<TMPro.TMP_InputField>(true))
        {
            var where = $"'{input.transform.parent.name}/{input.name}'";
            Assert.AreNotEqual(TMPro.TMP_InputField.LineType.SingleLine, input.lineType,
                $"{where} is SingleLine — mixed line types restart the IME on switch.");
            Assert.AreEqual(TMPro.TMP_InputField.InputType.Standard, input.inputType,
                $"{where} has autocorrection enabled — iOS runs the predictive session " +
                "on the shared hidden text field and replays content across focus " +
                "switches (text duplication).");

            // Exactly two deliberate keypad exceptions (device-verified with
            // the rapid-switch repro); everything else stays Default.
            var expectedKeyboard = input == phoneInput ? TouchScreenKeyboardType.PhonePad
                : input == emailInput ? TouchScreenKeyboardType.EmailAddress
                : TouchScreenKeyboardType.Default;
            Assert.AreEqual(expectedKeyboard, input.keyboardType,
                $"{where} has an unexpected keyboardType — the keypad map is " +
                "Телефон=PhonePad, Email=EmailAddress, everything else Default.");
        }
    }

    // Cloning a card copies the source's placeholder, so without an explicit
    // pass every contact field showed the bot-name hint («Ассистент»).
    [Test]
    public void ContactFields_HaveDistinctPlaceholders()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var settings = prefab.GetComponent<BotSettings>();
        var contacts = settings.ContactFields;

        var seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < contacts.Length; i++)
        {
            Assert.IsNotNull(contacts[i], $"{BotSettings.ContactKeys[i]} not wired");
            var placeholder = contacts[i].InputField.placeholder as TMPro.TMP_Text;
            Assert.IsNotNull(placeholder, $"{BotSettings.ContactKeys[i]} has no placeholder");

            var text = placeholder.text;
            Assert.IsNotEmpty(text, $"{BotSettings.ContactKeys[i]} placeholder is empty");
            Assert.AreNotEqual("Ассистент", text,
                $"{BotSettings.ContactKeys[i]} still carries the cloned bot-name placeholder.");
            Assert.IsTrue(seen.Add(text),
                $"Placeholder '{text}' is used by more than one contact field.");
        }
    }

    // The earlier attempt broke the settings screen by running the FULL
    // rebuild, which destroys every top-level child and drops the wiring a
    // dozen other builders apply. This pins the pieces that regressed.
    [Test]
    public void BotSettingsPrefab_KeepsExistingWiring()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab);

        var settings = prefab.GetComponent<BotSettings>();
        Assert.IsNotNull(settings.BotNameField, "BotNameField lost");
        Assert.IsNotNull(settings.PromptField, "PromptField lost");
        Assert.IsNotNull(settings.WhatsappNumberField, "WhatsappNumberField lost");
        Assert.IsNotNull(settings.ProductsParent, "ProductsParent lost");
        Assert.IsNotNull(settings.ServicesParent, "ServicesParent lost");

        Assert.IsNotNull(prefab.GetComponentInChildren<SwipeToBackBotSettings>(true),
            "SwipeToBackBotSettings lost — the symptom of the full-rebuild regression.");
        Assert.IsNotNull(settings.BusinessField.GetComponent<ScrollableTextArea>(),
            "Description ScrollableTextArea lost.");
    }
}

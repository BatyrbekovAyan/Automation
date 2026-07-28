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

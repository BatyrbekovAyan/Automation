using NUnit.Framework;

public class BusinessKnowledgeComposeTests
{
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
    public void BotSettingsPrefab_HasAllContactFieldRefs()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(
            "Assets/Prefabs/BotSettings.prefab");
        Assert.IsNotNull(prefab, "BotSettings.prefab not found");

        var settings = prefab.GetComponent<BotSettings>();
        Assert.IsNotNull(settings, "BotSettings component missing on prefab");
        Assert.IsNotNull(settings.BusinessField,  "BusinessField not wired");
        Assert.IsNotNull(settings.PhoneField,     "PhoneField not wired");
        Assert.IsNotNull(settings.HoursField,     "HoursField not wired");
        Assert.IsNotNull(settings.AddressField,   "AddressField not wired");
        Assert.IsNotNull(settings.InstagramField, "InstagramField not wired");
        Assert.IsNotNull(settings.EmailField,     "EmailField not wired");
    }
}

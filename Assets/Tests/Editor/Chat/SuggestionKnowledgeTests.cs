using System.Collections.Generic;
using NUnit.Framework;

/// <summary>S1/S6 (audit stage 5): the knowledge contract carries descriptions and contacts as fields, and its version moves with every fact.</summary>
public class SuggestionKnowledgeTests
{
    private static List<SuggestionKnowledgeItem> Items() => new()
    {
        new SuggestionKnowledgeItem { kind = "product", name = "Фильтр масляный 90915-YZZE1", price = "3200 тг", description = "Оригинал Toyota, цена без установки" },
        new SuggestionKnowledgeItem { kind = "service", name = "Замена масла", price = "5000", description = "" },
        new SuggestionKnowledgeItem { kind = "product", name = "   ", price = "1", description = "skipped" },
        null,
    };

    [Test]
    public void Compose_KeepsDescriptions_AndContactsAsFields()
    {
        var d = SuggestionKnowledge.Compose(Items(), new SuggestionKnowledgeContacts { phone = "+7 777", hours = "10–20" }, "Магазин запчастей", "Asia/Almaty", 2);
        Assert.AreEqual(2, d.items.Count, "blank names are skipped");
        Assert.AreEqual("Оригинал Toyota, цена без установки", d.items[0].description);
        Assert.AreEqual("service", d.items[1].kind);
        Assert.AreEqual("", d.items[1].description, "an absent description stays empty — never invented");
        Assert.AreEqual("+7 777", d.contacts.phone);
        Assert.AreEqual("Asia/Almaty", d.timezone);
        Assert.AreEqual("bot", d.tzSource);
        Assert.AreEqual(2, d.documents);
        Assert.AreEqual(16, d.version.Length);
    }

    [Test]
    public void Compose_LegacyBotWithoutTimezone_SaysSo()
    {
        var d = SuggestionKnowledge.Compose(Items(), null, "", "  ", 0);
        Assert.IsNull(d.timezone);
        Assert.AreEqual("none", d.tzSource, "the server defaults the zone and hedges the time — never the traveling phone's clock");
    }

    [Test]
    public void Version_ChangesWithAnyFact_AndIsStable()
    {
        var a = SuggestionKnowledge.Compose(Items(), new SuggestionKnowledgeContacts { phone = "1" }, "desc", "Asia/Almaty", 0);
        var b = SuggestionKnowledge.Compose(Items(), new SuggestionKnowledgeContacts { phone = "1" }, "desc", "Asia/Almaty", 0);
        Assert.AreEqual(a.version, b.version, "same facts → same version");
        var priced = Items(); priced[0].price = "3300 тг";
        Assert.AreNotEqual(a.version, SuggestionKnowledge.Compose(priced, new SuggestionKnowledgeContacts { phone = "1" }, "desc", "Asia/Almaty", 0).version, "a price edit must miss the cache");
        Assert.AreNotEqual(a.version, SuggestionKnowledge.Compose(Items(), new SuggestionKnowledgeContacts { phone = "2" }, "desc", "Asia/Almaty", 0).version, "a contact edit must miss the cache");
        Assert.AreNotEqual(SuggestionKnowledge.Version(a), SuggestionKnowledge.Version(a, "будь краток"), "the owner prompt is part of the version");
    }

    [Test]
    public void Fnv1a64_KnownVector()
    {
        Assert.AreEqual("cbf29ce484222325", SuggestionKnowledge.Fnv1a64(""));
        Assert.AreEqual("af63dc4c8601ec8c", SuggestionKnowledge.Fnv1a64("a"));
    }
}

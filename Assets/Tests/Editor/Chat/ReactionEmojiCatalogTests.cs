using System.Collections.Generic;
using NUnit.Framework;

public class ReactionEmojiCatalogTests
{
    [Test]
    public void All_HasReasonableCount()
        => Assert.GreaterOrEqual(ReactionEmojiCatalog.All.Length, 200);

    [Test]
    public void All_ContainsQuickSix()
    {
        foreach (var e in new[] { "👍", "❤️", "😂", "😮", "😢", "🙏" })
            CollectionAssert.Contains(ReactionEmojiCatalog.All, e);
    }

    [Test]
    public void All_NoNullOrEmpty()
    {
        foreach (var e in ReactionEmojiCatalog.All)
            Assert.IsFalse(string.IsNullOrEmpty(e));
    }

    [Test]
    public void All_NoDuplicates()
    {
        var seen = new HashSet<string>();
        foreach (var e in ReactionEmojiCatalog.All)
            Assert.IsTrue(seen.Add(e), $"Duplicate emoji in catalog: {e}");
    }

    [Test]
    public void Categories_AllNonEmpty()
    {
        Assert.Greater(ReactionEmojiCatalog.Categories.Length, 0);
        foreach (var c in ReactionEmojiCatalog.Categories)
        {
            Assert.IsFalse(string.IsNullOrEmpty(c.Name), "Category with empty name");
            Assert.IsNotNull(c.Emojis);
            Assert.Greater(c.Emojis.Length, 0, $"Category '{c.Name}' has no emoji");
        }
    }

    [Test]
    public void All_EqualsFlattenedCategories()
    {
        int sum = 0;
        foreach (var c in ReactionEmojiCatalog.Categories) sum += c.Emojis.Length;
        Assert.AreEqual(sum, ReactionEmojiCatalog.All.Length);
    }
}

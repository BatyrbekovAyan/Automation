using System.Collections.Generic;
using NUnit.Framework;

public class ReactionEmojiCatalogTests
{
    [Test]
    public void All_HasReasonableCount()
        => Assert.GreaterOrEqual(ReactionEmojiCatalog.All.Length, 48);

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
}

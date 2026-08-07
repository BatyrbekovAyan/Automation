using System.Collections.Generic;
using System.Linq;
using Automation.BotSettingsUI;
using NUnit.Framework;
using UnityEditor;

public class PromptSuggestionCatalogTests
{
    private const string BusinessTypesAssetPath = "Assets/Data/BusinessTypes.asset";

    private static HashSet<string> BusinessTypeIds()
    {
        var asset = AssetDatabase.LoadAssetAtPath<BusinessTypesSO>(BusinessTypesAssetPath);
        Assert.IsNotNull(asset, $"BusinessTypes asset missing at {BusinessTypesAssetPath}");
        return new HashSet<string>(asset.All.Select(e => e.id));
    }

    [Test]
    public void EveryEntry_HasUniqueIdAndNonEmptyCopy()
    {
        var seen = new HashSet<string>();
        foreach (var entry in PromptSuggestionCatalog.All)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Id), "empty Id");
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Text), $"{entry.Id}: empty Text");
            Assert.IsFalse(string.IsNullOrWhiteSpace(entry.ShortLabel), $"{entry.Id}: empty ShortLabel");
            Assert.IsTrue(seen.Add(entry.Id), $"duplicate Id {entry.Id}");
        }
    }

    [Test]
    public void EveryShortLabel_FitsTheChip()
    {
        foreach (var entry in PromptSuggestionCatalog.All)
            Assert.LessOrEqual(entry.ShortLabel.Length, 22,
                $"{entry.Id}: ShortLabel «{entry.ShortLabel}» is too long for a pill");
    }

    [Test]
    public void EveryVerticalId_ExistsInBusinessTypesAsset()
    {
        var ids = BusinessTypeIds();
        foreach (var entry in PromptSuggestionCatalog.All)
        {
            if (string.IsNullOrEmpty(entry.VerticalId)) continue;
            Assert.IsTrue(ids.Contains(entry.VerticalId),
                $"{entry.Id}: unknown VerticalId «{entry.VerticalId}»");
        }
    }

    [Test]
    public void FeaturedFlag_IsCoreOnly_AndPlentiful()
    {
        var featured = PromptSuggestionCatalog.All.Where(e => e.Featured).ToList();
        Assert.AreEqual(10, featured.Count, "the catalog's documented shape is exactly 10 Featured core entries");
        foreach (var entry in featured)
            Assert.IsEmpty(entry.VerticalId, $"{entry.Id}: Featured must be core-only");
    }

    [Test]
    public void ForVertical_PutsVerticalEntriesFirst()
    {
        var list = PromptSuggestionCatalog.ForVertical("auto_parts");
        Assert.AreEqual(32, list.Count);
        for (var i = 0; i < 5; i++)
            Assert.AreEqual("auto_parts", list[i].VerticalId, $"index {i} is not a vertical entry");
        for (var i = 5; i < list.Count; i++)
            Assert.IsEmpty(list[i].VerticalId, $"index {i} is not a core entry");
    }

    [Test]
    public void ForVertical_UnknownOrEmptyId_ReturnsCoreOnly()
    {
        Assert.AreEqual(27, PromptSuggestionCatalog.ForVertical("").Count);
        // «car_service» is a pre-vertical legacy id still stored on old bots.
        Assert.AreEqual(27, PromptSuggestionCatalog.ForVertical("car_service").Count);
    }

    [Test]
    public void CloudCandidates_AreVerticalFirst_CappedAndDistinct()
    {
        var cloud = PromptSuggestionCatalog.CloudCandidates("flowers");
        Assert.AreEqual(8, cloud.Count);
        Assert.AreEqual(5, cloud.Count(e => e.VerticalId == "flowers"));
        for (var i = 0; i < 5; i++) Assert.AreEqual("flowers", cloud[i].VerticalId);
        for (var i = 5; i < cloud.Count; i++) Assert.IsTrue(cloud[i].Featured);
        Assert.AreEqual(8, cloud.Select(e => e.Id).Distinct().Count());

        var coreOnly = PromptSuggestionCatalog.CloudCandidates("");
        Assert.AreEqual(8, coreOnly.Count);
        Assert.IsTrue(coreOnly.All(e => e.Featured));
    }
}

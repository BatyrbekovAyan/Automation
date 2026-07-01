using NUnit.Framework;
using UnityEngine;

// Contract tests for the per-bot, per-type (product/service) uploaded price-list
// store. Persistence is PlayerPrefs; keys follow the Product/Service list
// convention (count = plural + "Number", items = singular prefix + index).
public class UploadedFilesStoreTests
{
    private const string BotA = "TESTBOT_ufs_A";
    private const string BotB = "TESTBOT_ufs_B";
    private const string Product = "product";
    private const string Service = "service";

    [SetUp]
    public void SetUp() => ClearAll();

    [TearDown]
    public void TearDown() => ClearAll();

    private static void ClearAll()
    {
        foreach (var bot in new[] { BotA, BotB })
            foreach (var type in new[] { Product, Service })
                UploadedFilesStore.Clear(bot, type);
    }

    private static UploadedFileEntry Entry(string id, string name, long size = 100, long date = 1000) =>
        new UploadedFileEntry { Id = id, Name = name, Size = size, DateUnixMs = date };

    [Test]
    public void Load_ReturnsEmpty_WhenNothingStored()
    {
        Assert.AreEqual(0, UploadedFilesStore.Load(BotA, Product).Count);
    }

    [Test]
    public void Add_ThenLoad_ReturnsStoredEntryFields()
    {
        UploadedFilesStore.Add(BotA, Product, Entry("id-1", "price.pdf", 240, 1719843600000));

        var list = UploadedFilesStore.Load(BotA, Product);
        Assert.AreEqual(1, list.Count);
        Assert.AreEqual("id-1", list[0].Id);
        Assert.AreEqual("price.pdf", list[0].Name);
        Assert.AreEqual(240, list[0].Size);
        Assert.AreEqual(1719843600000, list[0].DateUnixMs);
    }

    [Test]
    public void Add_Multiple_PreservesInsertionOrder()
    {
        UploadedFilesStore.Add(BotA, Product, Entry("a", "a.pdf"));
        UploadedFilesStore.Add(BotA, Product, Entry("b", "b.xlsx"));
        UploadedFilesStore.Add(BotA, Product, Entry("c", "c.csv"));

        var ids = UploadedFilesStore.Load(BotA, Product).ConvertAll(e => e.Id);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, ids);
    }

    [Test]
    public void Remove_ById_DropsOnlyThatEntry_AndReindexes()
    {
        UploadedFilesStore.Add(BotA, Product, Entry("a", "a.pdf"));
        UploadedFilesStore.Add(BotA, Product, Entry("b", "b.xlsx"));
        UploadedFilesStore.Add(BotA, Product, Entry("c", "c.csv"));

        bool removed = UploadedFilesStore.Remove(BotA, Product, "b");

        Assert.IsTrue(removed);
        var ids = UploadedFilesStore.Load(BotA, Product).ConvertAll(e => e.Id);
        CollectionAssert.AreEqual(new[] { "a", "c" }, ids);
    }

    [Test]
    public void Remove_LeavesNoOrphanTailKeys()
    {
        UploadedFilesStore.Add(BotA, Product, Entry("a", "a.pdf"));
        UploadedFilesStore.Add(BotA, Product, Entry("b", "b.xlsx"));

        UploadedFilesStore.Remove(BotA, Product, "a"); // count 2 -> 1

        Assert.IsFalse(PlayerPrefs.HasKey(BotA + "ProductFile1"), "orphan id key left behind");
        Assert.IsFalse(PlayerPrefs.HasKey(BotA + "ProductFile1Name"), "orphan name key left behind");
        Assert.AreEqual(1, UploadedFilesStore.Load(BotA, Product).Count);
    }

    [Test]
    public void Remove_ReturnsFalse_WhenIdNotFound()
    {
        UploadedFilesStore.Add(BotA, Product, Entry("a", "a.pdf"));

        Assert.IsFalse(UploadedFilesStore.Remove(BotA, Product, "nope"));
        Assert.AreEqual(1, UploadedFilesStore.Load(BotA, Product).Count);
    }

    [Test]
    public void Clear_RemovesAllEntriesAndCountKey()
    {
        UploadedFilesStore.Add(BotA, Product, Entry("a", "a.pdf"));
        UploadedFilesStore.Add(BotA, Product, Entry("b", "b.xlsx"));

        UploadedFilesStore.Clear(BotA, Product);

        Assert.AreEqual(0, UploadedFilesStore.Load(BotA, Product).Count);
        Assert.IsFalse(PlayerPrefs.HasKey(BotA + "ProductFilesNumber"));
    }

    [Test]
    public void Stores_AreIsolated_ByType()
    {
        UploadedFilesStore.Add(BotA, Product, Entry("p", "p.pdf"));

        Assert.AreEqual(1, UploadedFilesStore.Load(BotA, Product).Count);
        Assert.AreEqual(0, UploadedFilesStore.Load(BotA, Service).Count);
    }

    [Test]
    public void Stores_AreIsolated_ByBot()
    {
        UploadedFilesStore.Add(BotA, Product, Entry("p", "p.pdf"));

        Assert.AreEqual(0, UploadedFilesStore.Load(BotB, Product).Count);
    }
}

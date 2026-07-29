using NUnit.Framework;
using UnityEngine;

// Contract tests for the pending-price-list-upload ledger backing next-launch
// orphan cleanup.
//
// An upload writes its fileId here immediately before the POST and clears it
// once the file is recorded locally. If the process dies in between — a
// swipe-kill, or the OS reclaiming a backgrounded app while the coroutine is
// frozen mid-request — n8n still finishes ingesting, and this entry is the only
// on-device trace of the fileId those RAG chunks carry.
//
// Keys are global PlayerPrefs (a sweep at launch has no open bot), so each test
// snapshots whatever real state the editor had and restores it after.
public class PendingUploadLedgerTests
{
    private const string CountKey = "PendingUploadsNumber";
    private const int MaxSnapshot = 32;

    private readonly string[] savedIds = new string[MaxSnapshot];
    private readonly string[] savedBots = new string[MaxSnapshot];
    private readonly string[] savedTypes = new string[MaxSnapshot];
    private int savedCount;

    [SetUp]
    public void SetUp()
    {
        savedCount = PlayerPrefs.GetInt(CountKey, 0);
        for (int i = 0; i < savedCount && i < MaxSnapshot; i++)
        {
            savedIds[i] = PlayerPrefs.GetString($"PendingUpload{i}", "");
            savedBots[i] = PlayerPrefs.GetString($"PendingUpload{i}Bot", "");
            savedTypes[i] = PlayerPrefs.GetString($"PendingUpload{i}Type", "");
        }
        PendingUploadLedger.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        PendingUploadLedger.Clear();
        for (int i = 0; i < savedCount && i < MaxSnapshot; i++)
        {
            PlayerPrefs.SetString($"PendingUpload{i}", savedIds[i]);
            PlayerPrefs.SetString($"PendingUpload{i}Bot", savedBots[i]);
            PlayerPrefs.SetString($"PendingUpload{i}Type", savedTypes[i]);
        }
        if (savedCount > 0) PlayerPrefs.SetInt(CountKey, savedCount);
    }

    [Test]
    public void LoadAll_EmptyOnFreshState()
    {
        Assert.AreEqual(0, PendingUploadLedger.LoadAll().Count);
    }

    [Test]
    public void Add_ThenLoadAll_ReturnsAllThreeFields()
    {
        PendingUploadLedger.Add("file-1", "Bot0", "product");

        var all = PendingUploadLedger.LoadAll();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("file-1", all[0].FileId);
        Assert.AreEqual("Bot0", all[0].BotName);
        Assert.AreEqual("product", all[0].ContentType);
    }

    // A multi-file pick starts one job per file, so unlike the profile ledger
    // this one is a list, not a single slot.
    [Test]
    public void Add_KeepsEveryConcurrentUpload()
    {
        PendingUploadLedger.Add("file-1", "Bot0", "product");
        PendingUploadLedger.Add("file-2", "Bot0", "product");
        PendingUploadLedger.Add("file-3", "Bot0", "service");

        var ids = PendingUploadLedger.LoadAll().ConvertAll(e => e.FileId);
        CollectionAssert.AreEquivalent(new[] { "file-1", "file-2", "file-3" }, ids);
    }

    [Test]
    public void Add_SameFileIdTwice_DoesNotDuplicate()
    {
        PendingUploadLedger.Add("file-1", "Bot0", "product");
        PendingUploadLedger.Add("file-1", "Bot0", "product");

        Assert.AreEqual(1, PendingUploadLedger.LoadAll().Count);
    }

    [Test]
    public void Add_IgnoresBlankFileId()
    {
        PendingUploadLedger.Add("", "Bot0", "product");
        PendingUploadLedger.Add(null, "Bot0", "product");

        Assert.AreEqual(0, PendingUploadLedger.LoadAll().Count);
    }

    [Test]
    public void Add_IgnoresBlankBotName()
    {
        PendingUploadLedger.Add("file-1", "", "product");

        Assert.AreEqual(0, PendingUploadLedger.LoadAll().Count);
    }

    [Test]
    public void Remove_DropsOnlyThatEntry()
    {
        PendingUploadLedger.Add("file-1", "Bot0", "product");
        PendingUploadLedger.Add("file-2", "Bot0", "product");

        Assert.IsTrue(PendingUploadLedger.Remove("file-1"));

        var ids = PendingUploadLedger.LoadAll().ConvertAll(e => e.FileId);
        CollectionAssert.AreEqual(new[] { "file-2" }, ids);
    }

    [Test]
    public void Remove_UnknownFileId_ReturnsFalse()
    {
        PendingUploadLedger.Add("file-1", "Bot0", "product");

        Assert.IsFalse(PendingUploadLedger.Remove("nope"));
        Assert.AreEqual(1, PendingUploadLedger.LoadAll().Count);
    }

    // A shrink must not leave orphan tail keys that a later Add would read back
    // as a phantom entry — the same contiguous-rewrite discipline as
    // UploadedFilesStore.Persist.
    [Test]
    public void Remove_ThenAdd_DoesNotResurrectStaleTailKeys()
    {
        PendingUploadLedger.Add("file-1", "Bot0", "product");
        PendingUploadLedger.Add("file-2", "Bot0", "product");
        PendingUploadLedger.Remove("file-1");
        PendingUploadLedger.Remove("file-2");

        PendingUploadLedger.Add("file-3", "Bot1", "service");

        var all = PendingUploadLedger.LoadAll();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("file-3", all[0].FileId);
        Assert.AreEqual("Bot1", all[0].BotName);
        Assert.AreEqual("service", all[0].ContentType);
    }

    [Test]
    public void Clear_RemovesEverything()
    {
        PendingUploadLedger.Add("file-1", "Bot0", "product");
        PendingUploadLedger.Add("file-2", "Bot1", "service");

        PendingUploadLedger.Clear();

        Assert.AreEqual(0, PendingUploadLedger.LoadAll().Count);
    }

    ////////////////////////////// SWEEP RECONCILE //////////////////////////////
    // The sweep deletes RAG chunks, so being wrong here means silently making a
    // bot forget a price list the user can still see listed. An entry is only
    // an orphan if the file was never recorded locally.

    private const string SweepBot = "TESTBOT_pul_sweep";

    [Test]
    public void IsOrphan_True_WhenTheFileWasNeverRecorded()
    {
        UploadedFilesStore.Clear(SweepBot, "product");

        var entry = new PendingUploadEntry { FileId = "file-1", BotName = SweepBot, ContentType = "product" };
        Assert.IsTrue(PendingUploadLedger.IsOrphan(entry));
    }

    // The kill can also land AFTER the store write but before the ledger clear.
    // Sweeping then would delete the chunks of a file the app lists — so a
    // recorded fileId is never an orphan, whatever the ledger still says.
    [Test]
    public void IsOrphan_False_WhenTheStoreAlreadyRecordedThatFileId()
    {
        UploadedFilesStore.Clear(SweepBot, "product");
        UploadedFilesStore.Add(SweepBot, "product",
            new UploadedFileEntry { Id = "file-1", Name = "прайс.pdf", Size = 10, DateUnixMs = 1 });

        try
        {
            var entry = new PendingUploadEntry { FileId = "file-1", BotName = SweepBot, ContentType = "product" };
            Assert.IsFalse(PendingUploadLedger.IsOrphan(entry));
        }
        finally
        {
            UploadedFilesStore.Clear(SweepBot, "product");
        }
    }

    // Same fileId under the other tab must not count as recorded.
    [Test]
    public void IsOrphan_True_WhenOnlyTheOtherContentTypeRecordedIt()
    {
        UploadedFilesStore.Clear(SweepBot, "product");
        UploadedFilesStore.Clear(SweepBot, "service");
        UploadedFilesStore.Add(SweepBot, "service",
            new UploadedFileEntry { Id = "file-1", Name = "прайс.pdf", Size = 10, DateUnixMs = 1 });

        try
        {
            var entry = new PendingUploadEntry { FileId = "file-1", BotName = SweepBot, ContentType = "product" };
            Assert.IsTrue(PendingUploadLedger.IsOrphan(entry));
        }
        finally
        {
            UploadedFilesStore.Clear(SweepBot, "service");
        }
    }
}

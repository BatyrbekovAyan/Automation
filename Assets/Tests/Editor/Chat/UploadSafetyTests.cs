using System.Collections.Generic;
using NUnit.Framework;

// The three decisions that keep an upload from destroying data it should not.
// Each was extracted from inside a coroutine specifically so it can be asserted
// here rather than reasoned about — an audit found all three wrong.
public class UploadSweepPolicyTests
{
    // A sweep fires DeleteFile for a fileId with no local record. If it lands
    // while n8n is still ingesting, the chunk delete matches nothing while the
    // parallel Storage branch still removes the archived original — so settling
    // on "HTTP 200" throws away the archive AND leaves the chunks orphaned.
    [Test]
    public void ZeroChunksDeleted_IsNotSettled_WhileAttemptsRemain()
    {
        Assert.IsFalse(UploadSweepPolicy.ShouldSettle(requestSucceeded: true, deletedChunks: 0, attempts: 1));
    }

    [Test]
    public void ChunksDeleted_SettlesImmediately()
    {
        Assert.IsTrue(UploadSweepPolicy.ShouldSettle(requestSucceeded: true, deletedChunks: 3, attempts: 1));
    }

    // A deterministically-failed upload legitimately holds a fileId with zero
    // chunks forever. Without a cap its ledger entry would be immortal and the
    // app would re-issue a pointless delete on every single launch.
    [Test]
    public void ZeroChunks_SettlesOnceAttemptsAreExhausted()
    {
        Assert.IsTrue(UploadSweepPolicy.ShouldSettle(requestSucceeded: true, deletedChunks: 0,
                                                     attempts: UploadSweepPolicy.MaxAttempts));
    }

    [Test]
    public void FailedRequest_NeverSettles_RegardlessOfAttempts()
    {
        Assert.IsFalse(UploadSweepPolicy.ShouldSettle(requestSucceeded: false, deletedChunks: 0,
                                                      attempts: UploadSweepPolicy.MaxAttempts + 5));
    }

    // An unparseable body must behave like zero, not like success — otherwise a
    // malformed response silently discards the archive.
    [Test]
    public void UnknownChunkCount_IsTreatedAsZero()
    {
        Assert.IsFalse(UploadSweepPolicy.ShouldSettle(requestSucceeded: true, deletedChunks: -1, attempts: 1));
        Assert.IsTrue(UploadSweepPolicy.ShouldSettle(requestSucceeded: true, deletedChunks: -1,
                                                     attempts: UploadSweepPolicy.MaxAttempts));
    }
}

public class DeleteFileResponseTests
{
    [Test]
    public void ParsesDeletedChunks()
    {
        Assert.AreEqual(3, DeleteFileResponse.ParseDeletedChunks(
            "{\"success\":true,\"fileId\":\"5f1fb8a6\",\"deletedChunks\":3}"));
    }

    [Test]
    public void ParsesZero()
    {
        Assert.AreEqual(0, DeleteFileResponse.ParseDeletedChunks(
            "{\"success\":true,\"fileId\":\"5f1fb8a6\",\"deletedChunks\":0}"));
    }

    [Test]
    public void UnknownWhenBodyIsMissingOrUnparseable()
    {
        Assert.AreEqual(-1, DeleteFileResponse.ParseDeletedChunks(null));
        Assert.AreEqual(-1, DeleteFileResponse.ParseDeletedChunks(""));
        Assert.AreEqual(-1, DeleteFileResponse.ParseDeletedChunks("not json at all"));
        Assert.AreEqual(-1, DeleteFileResponse.ParseDeletedChunks("{\"success\":true}"));
    }
}

public class UploadNameSetTests
{
    private static UploadedFileEntry Stored(string name) =>
        new UploadedFileEntry { Id = System.Guid.NewGuid().ToString(), Name = name };

    private static UploadJob InFlight(string name) =>
        new UploadJob { FileName = name, BotName = "Bot0", ContentType = "product" };

    // The bug this closes: gallery names are stamped to the minute and were
    // de-duped only against COMPLETED uploads, so a second photo picked while
    // the first was still uploading got an identical name — and the second
    // completion then deleted the first's chunks and archive.
    [Test]
    public void IncludesNamesOfUploadsStillInFlight()
    {
        var taken = UploadNameSet.TakenNames(
            new List<UploadedFileEntry>(),
            new List<UploadJob> { InFlight("Фото 29.07.2026 14:22.jpg") });

        Assert.IsTrue(taken.Contains("Фото 29.07.2026 14:22.jpg"));
    }

    [Test]
    public void UnionsStoredAndInFlight()
    {
        var taken = UploadNameSet.TakenNames(
            new List<UploadedFileEntry> { Stored("прайс.pdf") },
            new List<UploadJob> { InFlight("фото.jpg") });

        Assert.AreEqual(2, taken.Count);
        Assert.IsTrue(taken.Contains("прайс.pdf"));
        Assert.IsTrue(taken.Contains("фото.jpg"));
    }

    [Test]
    public void DeduplicatesAcrossSources()
    {
        var taken = UploadNameSet.TakenNames(
            new List<UploadedFileEntry> { Stored("прайс.pdf") },
            new List<UploadJob> { InFlight("прайс.pdf") });

        Assert.AreEqual(1, taken.Count);
    }

    [Test]
    public void ToleratesNullsAndBlanks()
    {
        var taken = UploadNameSet.TakenNames(null, null);
        Assert.AreEqual(0, taken.Count);

        taken = UploadNameSet.TakenNames(
            new List<UploadedFileEntry> { Stored("") },
            new List<UploadJob> { InFlight(null) });
        Assert.AreEqual(0, taken.Count);
    }

    // The end-to-end point of the set: GalleryPhotoNamer must bump off a name
    // that only exists as an in-flight job.
    [Test]
    public void GalleryNamer_BumpsOffAnInFlightName()
    {
        var taken = UploadNameSet.TakenNames(
            new List<UploadedFileEntry>(),
            new List<UploadJob> { InFlight("Фото 29.07.2026 14:22.jpg") });

        string next = GalleryPhotoNamer.DisplayName(
            new System.DateTime(2026, 7, 29, 14, 22, 0), indexInBatch: 0, batchSize: 1, takenNames: taken);

        Assert.AreNotEqual("Фото 29.07.2026 14:22.jpg", next);
        StringAssert.EndsWith(".jpg", next);
    }
}

using System.IO;
using NUnit.Framework;

// Pins the disk surface of «Удалить все данные»: which paths are wiped and
// which root-level files count as sticker downloads. DeleteDiskData itself is
// exercised against a temp directory so no real app data is touched.
public class LocalDataWipeTests
{
    [Test]
    public void Targets_CoverAllKnownUserDataLocations()
    {
        var targets = LocalDataWipe.DiskWipeTargets("/data");
        CollectionAssert.AreEquivalent(
            new[]
            {
                Path.Combine("/data", "BotCache"),
                Path.Combine("/data", "response.txt"),
                Path.Combine("/data", "link_metadata.json"),
                Path.Combine("/data", "all_chats_cache.json"),
            },
            targets);
    }

    [Test]
    public void StickerGlob_MatchesWappiDownloads()
    {
        Assert.IsTrue(LocalDataWipe.IsStickerFile("sticker_ABC123.webp"));
        Assert.IsTrue(LocalDataWipe.IsStickerFile("sticker_x.WEBP"));
    }

    [Test]
    public void StickerGlob_RejectsNonStickers()
    {
        Assert.IsFalse(LocalDataWipe.IsStickerFile("response.txt"));
        Assert.IsFalse(LocalDataWipe.IsStickerFile("sticker_.png"));
        Assert.IsFalse(LocalDataWipe.IsStickerFile("Sticker_x.webp")); // case-sensitive prefix, matches writer
        Assert.IsFalse(LocalDataWipe.IsStickerFile(null));
        Assert.IsFalse(LocalDataWipe.IsStickerFile(""));
    }

    [Test]
    public void DeleteDiskData_RemovesTargetsAndStickers_LeavesOthers()
    {
        string root = Path.Combine(Path.GetTempPath(), "wipe_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "BotCache", "Bot0", "media"));
        File.WriteAllText(Path.Combine(root, "BotCache", "Bot0", "chats.json"), "{}");
        File.WriteAllText(Path.Combine(root, "response.txt"), "raw");
        File.WriteAllText(Path.Combine(root, "link_metadata.json"), "{}");
        File.WriteAllText(Path.Combine(root, "sticker_msg1.webp"), "x");
        File.WriteAllText(Path.Combine(root, "keep_me.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root, "emoji_patch"));

        try
        {
            LocalDataWipe.DeleteDiskData(root);

            Assert.IsFalse(Directory.Exists(Path.Combine(root, "BotCache")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "response.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "link_metadata.json")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "sticker_msg1.webp")));
            // Non-user-data neighbours survive.
            Assert.IsTrue(File.Exists(Path.Combine(root, "keep_me.json")));
            Assert.IsTrue(Directory.Exists(Path.Combine(root, "emoji_patch")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void DeleteDiskData_MissingTargets_DoesNotThrow()
    {
        string root = Path.Combine(Path.GetTempPath(), "wipe_test_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            Assert.DoesNotThrow(() => LocalDataWipe.DeleteDiskData(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

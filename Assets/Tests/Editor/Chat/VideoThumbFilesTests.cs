using System.IO;
using NUnit.Framework;

public class VideoThumbFilesTests
{
    private string dir;

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "VideoThumbFilesTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private string FinalPath() => Path.Combine(dir, "ABCDEF.jpg");

    [Test]
    public void TempPathFor_IsUniquePerAttempt()
    {
        string final = FinalPath();
        Assert.AreNotEqual(VideoThumbFiles.TempPathFor(final, 1), VideoThumbFiles.TempPathFor(final, 2));
    }

    [Test]
    public void TempPathFor_StaysInSameDirectory()
    {
        string temp = VideoThumbFiles.TempPathFor(FinalPath(), 7);
        Assert.AreEqual(dir, Path.GetDirectoryName(temp));
    }

    [Test]
    public void Commit_MovesTempToFinal()
    {
        string final = FinalPath();
        string temp = VideoThumbFiles.TempPathFor(final, 1);
        File.WriteAllBytes(temp, new byte[] { 1, 2, 3 });

        Assert.IsTrue(VideoThumbFiles.Commit(temp, final));
        Assert.IsFalse(File.Exists(temp));
        Assert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(final));
    }

    [Test]
    public void Commit_ReplacesExistingFinal()
    {
        string final = FinalPath();
        File.WriteAllBytes(final, new byte[] { 9, 9 });   // stale frame already cached
        string temp = VideoThumbFiles.TempPathFor(final, 2);
        File.WriteAllBytes(temp, new byte[] { 1, 2, 3 });

        Assert.IsTrue(VideoThumbFiles.Commit(temp, final));
        Assert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(final));
    }

    [Test]
    public void Commit_MissingTemp_ReturnsFalseAndLeavesFinalUntouched()
    {
        string final = FinalPath();
        File.WriteAllBytes(final, new byte[] { 9, 9 });
        string temp = VideoThumbFiles.TempPathFor(final, 3);

        Assert.IsFalse(VideoThumbFiles.Commit(temp, final));
        Assert.AreEqual(new byte[] { 9, 9 }, File.ReadAllBytes(final));
    }

    [Test]
    public void Commit_NullOrEmptyPaths_ReturnsFalse()
    {
        Assert.IsFalse(VideoThumbFiles.Commit(null, FinalPath()));
        Assert.IsFalse(VideoThumbFiles.Commit("", FinalPath()));
        Assert.IsFalse(VideoThumbFiles.Commit(FinalPath(), null));
    }

    [Test]
    public void Discard_RemovesTemp()
    {
        string temp = VideoThumbFiles.TempPathFor(FinalPath(), 4);
        File.WriteAllBytes(temp, new byte[] { 1 });

        VideoThumbFiles.Discard(temp);
        Assert.IsFalse(File.Exists(temp));
    }

    [Test]
    public void Discard_MissingOrNullTemp_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => VideoThumbFiles.Discard(VideoThumbFiles.TempPathFor(FinalPath(), 5)));
        Assert.DoesNotThrow(() => VideoThumbFiles.Discard(null));
    }
}

using System;
using System.IO;
using System.Text;
using NUnit.Framework;

public class Base64EncoderTests
{
    private static string TempPath(string tag) =>
        Path.Combine(Path.GetTempPath(), $"b64enc_{tag}_{Guid.NewGuid():N}.bin");

    [Test]
    public void EncodeFileAsync_KnownBytes_MatchesConvertToBase64()
    {
        string path = TempPath("known");
        byte[] bytes = Encoding.UTF8.GetBytes("hello wappi media");
        File.WriteAllBytes(path, bytes);
        try
        {
            string result = Base64Encoder.EncodeFileAsync(path).GetAwaiter().GetResult();
            Assert.AreEqual(Convert.ToBase64String(bytes), result);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void EncodeFileAsync_EmptyFile_ReturnsEmptyString()
    {
        string path = TempPath("empty");
        File.WriteAllBytes(path, Array.Empty<byte>());
        try
        {
            string result = Base64Encoder.EncodeFileAsync(path).GetAwaiter().GetResult();
            Assert.AreEqual("", result);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void EncodeFileAsync_LargeFile_MatchesConvertToBase64()
    {
        string path = TempPath("large");
        var bytes = new byte[3 * 1024 * 1024];
        new System.Random(42).NextBytes(bytes);
        File.WriteAllBytes(path, bytes);
        try
        {
            string result = Base64Encoder.EncodeFileAsync(path).GetAwaiter().GetResult();
            Assert.AreEqual(Convert.ToBase64String(bytes), result);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void EncodeFileAsync_MissingFile_TaskFaults()
    {
        string path = TempPath("missing"); // never created
        var task = Base64Encoder.EncodeFileAsync(path);
        Assert.Throws<AggregateException>(() => task.Wait());
        Assert.IsTrue(task.IsFaulted);
    }
}

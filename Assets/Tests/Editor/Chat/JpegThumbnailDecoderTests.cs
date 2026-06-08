using System;
using System.Text;
using NUnit.Framework;

public class JpegThumbnailDecoderTests
{
    // A short, known payload we can re-encode with various server-style decorations
    // and assert round-trips back to the original bytes.
    private static readonly byte[] Sample = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

    private static string Std => Convert.ToBase64String(Sample);

    [Test]
    public void PlainBase64_DecodesToOriginalBytes()
    {
        Assert.IsTrue(JpegThumbnailDecoder.TryDecodeBase64(Std, out byte[] bytes));
        Assert.AreEqual(Sample, bytes);
    }

    [Test]
    public void DataUriPrefix_IsStripped()
    {
        string withPrefix = "data:image/jpeg;base64," + Std;
        Assert.IsTrue(JpegThumbnailDecoder.TryDecodeBase64(withPrefix, out byte[] bytes));
        Assert.AreEqual(Sample, bytes);
    }

    [Test]
    public void EmbeddedWhitespaceAndNewlines_AreIgnored()
    {
        string b64 = Std;
        // Inject line breaks/spaces the way some servers wrap base64 payloads.
        string wrapped = " " + b64.Substring(0, 2) + "\r\n" + b64.Substring(2) + "\t ";
        Assert.IsTrue(JpegThumbnailDecoder.TryDecodeBase64(wrapped, out byte[] bytes));
        Assert.AreEqual(Sample, bytes);
    }

    [Test]
    public void UrlSafeAlphabet_IsNormalized()
    {
        // Pick bytes whose standard base64 contains '+' and '/', then swap to URL-safe.
        byte[] payload = { 0xFB, 0xFF, 0xBF, 0x3E, 0x3F };
        string urlSafe = Convert.ToBase64String(payload).Replace('+', '-').Replace('/', '_');
        Assert.IsTrue(JpegThumbnailDecoder.TryDecodeBase64(urlSafe, out byte[] bytes));
        Assert.AreEqual(payload, bytes);
    }

    [Test]
    public void MissingPadding_IsRestored()
    {
        string unpadded = Std.TrimEnd('=');
        Assert.IsTrue(JpegThumbnailDecoder.TryDecodeBase64(unpadded, out byte[] bytes));
        Assert.AreEqual(Sample, bytes);
    }

    [Test]
    public void NullOrEmpty_ReturnsFalse()
    {
        Assert.IsFalse(JpegThumbnailDecoder.TryDecodeBase64(null, out byte[] a));
        Assert.IsNull(a);
        Assert.IsFalse(JpegThumbnailDecoder.TryDecodeBase64("", out byte[] b));
        Assert.IsNull(b);
    }

    [Test]
    public void WhitespaceOnly_ReturnsFalse()
    {
        Assert.IsFalse(JpegThumbnailDecoder.TryDecodeBase64("   \r\n\t", out byte[] bytes));
        Assert.IsNull(bytes);
    }

    [Test]
    public void DataUriPrefixWithNoPayload_ReturnsFalse()
    {
        Assert.IsFalse(JpegThumbnailDecoder.TryDecodeBase64("data:image/jpeg;base64,", out byte[] bytes));
        Assert.IsNull(bytes);
    }

    [Test]
    public void GarbageInput_ReturnsFalse()
    {
        // '@' and '#' are outside the base64 alphabet (and length % 4 == 1 anyway).
        Assert.IsFalse(JpegThumbnailDecoder.TryDecodeBase64("@@@@@#", out byte[] bytes));
        Assert.IsNull(bytes);
    }

    [Test]
    public void LengthRemainderOfOne_ReturnsFalse()
    {
        // 5 chars from the valid alphabet => length % 4 == 1, which can never be a
        // valid base64 length — reject without throwing or feeding garbage to decode.
        Assert.IsFalse(JpegThumbnailDecoder.TryDecodeBase64("QUJDR", out byte[] bytes));
        Assert.IsNull(bytes);
    }
}

using System;
using NUnit.Framework;

public class WebPSignatureTests
{
    // Minimal valid WebP container header: "RIFF" + 4-byte size + "WEBP".
    private static byte[] WebPHeader(int trailing = 0)
    {
        byte[] header =
        {
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x00, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P'
        };
        if (trailing <= 0) return header;

        byte[] withBody = new byte[header.Length + trailing];
        Array.Copy(header, withBody, header.Length);
        return withBody;
    }

    [Test]
    public void RiffWebpWithBody_IsWebP()
    {
        Assert.IsTrue(WebPSignature.IsWebP(WebPHeader(trailing: 8)));
    }

    [Test]
    public void ExactTwelveByteHeader_IsWebP()
    {
        Assert.IsTrue(WebPSignature.IsWebP(WebPHeader()));
    }

    [Test]
    public void JpegBytes_AreNotWebP()
    {
        // JPEG SOI + JFIF — the exact kind of foreign photo that used to render as a sticker.
        byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
        Assert.IsFalse(WebPSignature.IsWebP(jpeg));
    }

    [Test]
    public void PngBytes_AreNotWebP()
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
        Assert.IsFalse(WebPSignature.IsWebP(png));
    }

    [Test]
    public void RiffButWrongFormType_IsNotWebP()
    {
        // A RIFF/WAVE container shares the RIFF magic but carries the wrong form type.
        byte[] wave =
        {
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x00, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'A', (byte)'V', (byte)'E'
        };
        Assert.IsFalse(WebPSignature.IsWebP(wave));
    }

    [Test]
    public void TooShort_IsNotWebP()
    {
        byte[] shortBytes = { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x00, 0x00 };
        Assert.IsFalse(WebPSignature.IsWebP(shortBytes));
    }

    [Test]
    public void Null_IsNotWebP()
    {
        Assert.IsFalse(WebPSignature.IsWebP(null));
    }

    [Test]
    public void Empty_IsNotWebP()
    {
        Assert.IsFalse(WebPSignature.IsWebP(new byte[0]));
    }
}

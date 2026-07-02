using System.IO;
using NUnit.Framework;
using UnityEngine;

public class ImageUploadPreprocessorTests
{
    private static string TempPng(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        var pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (i % 7 == 0) ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
        texture.SetPixels32(pixels);
        texture.Apply();
        string path = Path.Combine(Path.GetTempPath(), $"imgprep_{System.Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        return path;
    }

    [Test]
    public void ToJpegPayload_ValidPng_ReturnsDecodableJpeg()
    {
        string path = TempPng(320, 240);
        try
        {
            byte[] jpeg = ImageUploadPreprocessor.ToJpegPayload(path);
            Assert.IsNotNull(jpeg);
            Assert.Greater(jpeg.Length, 100);
            Assert.AreEqual(0xFF, jpeg[0]); // JPEG SOI marker
            Assert.AreEqual(0xD8, jpeg[1]);
            var decoded = new Texture2D(2, 2);
            Assert.IsTrue(decoded.LoadImage(jpeg));
            Assert.AreEqual(320, decoded.width);
            Assert.AreEqual(240, decoded.height);
            Object.DestroyImmediate(decoded);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void ToJpegPayload_MissingFile_ReturnsNull()
    {
        Assert.IsNull(ImageUploadPreprocessor.ToJpegPayload(
            Path.Combine(Path.GetTempPath(), "does_not_exist_12345.jpg")));
    }

    [Test]
    public void ToJpegPayload_CorruptBytes_ReturnsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), $"imgprep_corrupt_{System.Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        try { Assert.IsNull(ImageUploadPreprocessor.ToJpegPayload(path)); }
        finally { File.Delete(path); }
    }
}

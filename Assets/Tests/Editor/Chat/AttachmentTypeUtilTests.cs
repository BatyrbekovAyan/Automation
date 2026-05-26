using NUnit.Framework;

public class AttachmentTypeUtilTests
{
    [TestCase("/path/photo.jpg",  true)]
    [TestCase("/path/photo.JPEG", true)]
    [TestCase("/path/photo.png",  true)]
    [TestCase("/path/photo.gif",  true)]
    [TestCase("/path/photo.webp", true)]
    [TestCase("/path/photo.heic", true)]
    [TestCase("/path/clip.mp4",   false)]
    [TestCase("/path/clip.MOV",   false)]
    [TestCase("/path/file.pdf",   false)]
    [TestCase("",                 false)]
    [TestCase(null,               false)]
    public void IsImageExtension_PathSuffix_ReturnsExpected(string path, bool expected)
    {
        Assert.AreEqual(expected, AttachmentTypeUtil.IsImageExtension(path));
    }

    [TestCase("/p/a.jpg",  "image/jpeg")]
    [TestCase("/p/a.jpeg", "image/jpeg")]
    [TestCase("/p/a.PNG",  "image/png")]
    [TestCase("/p/a.gif",  "image/gif")]
    [TestCase("/p/a.webp", "image/webp")]
    [TestCase("/p/a.heic", "image/heic")]
    [TestCase("/p/a.mp4",  "video/mp4")]
    [TestCase("/p/a.mov",  "video/quicktime")]
    [TestCase("/p/a.pdf",  "application/pdf")]
    [TestCase("/p/a.doc",  "application/msword")]
    [TestCase("/p/a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [TestCase("/p/a.xls",  "application/vnd.ms-excel")]
    [TestCase("/p/a.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [TestCase("/p/a.txt",  "text/plain")]
    [TestCase("/p/a.zip",  "application/zip")]
    public void MimeFromExtension_Known_ReturnsMappedMime(string path, string expected)
    {
        Assert.AreEqual(expected, AttachmentTypeUtil.MimeFromExtension(path));
    }

    [TestCase("/p/a.xyz")]
    [TestCase("/p/no-extension")]
    [TestCase("")]
    [TestCase(null)]
    public void MimeFromExtension_Unknown_ReturnsNull(string path)
    {
        Assert.IsNull(AttachmentTypeUtil.MimeFromExtension(path));
    }

    [TestCase("/p/a.jpg", AttachmentKind.GalleryImage)]
    [TestCase("/p/a.png", AttachmentKind.GalleryImage)]
    [TestCase("/p/a.mp4", AttachmentKind.GalleryVideo)]
    [TestCase("/p/a.mov", AttachmentKind.GalleryVideo)]
    [TestCase("/p/a",     AttachmentKind.GalleryVideo)]
    public void GalleryKindFromPath_ImageVsVideo(string path, AttachmentKind expected)
    {
        Assert.AreEqual(expected, AttachmentTypeUtil.GalleryKindFromPath(path));
    }
}

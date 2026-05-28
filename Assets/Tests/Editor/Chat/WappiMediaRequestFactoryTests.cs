using NUnit.Framework;

public class WappiMediaRequestFactoryTests
{
    private const string Img = "https://wappi.pro/api/sync/message/img/send?profile_id=PID";
    private const string Vid = "https://wappi.pro/api/sync/message/video/send?profile_id=PID";
    private const string Doc = "https://wappi.pro/api/sync/message/document/send?profile_id=PID";

    [Test]
    public void EndpointFor_Image_UsesImgSend()
    {
        Assert.AreEqual(Img, WappiMediaRequestFactory.EndpointFor(AttachmentKind.Photo, "PID"));
        Assert.AreEqual(Img, WappiMediaRequestFactory.EndpointFor(AttachmentKind.GalleryImage, "PID"));
    }

    [Test]
    public void EndpointFor_Video_UsesVideoSend() =>
        Assert.AreEqual(Vid, WappiMediaRequestFactory.EndpointFor(AttachmentKind.GalleryVideo, "PID"));

    [Test]
    public void EndpointFor_Document_UsesDocumentSend() =>
        Assert.AreEqual(Doc, WappiMediaRequestFactory.EndpointFor(AttachmentKind.Document, "PID"));

    [Test]
    public void NormalizeRecipient_StripsCUs() =>
        Assert.AreEqual("79995579399", WappiMediaRequestFactory.NormalizeRecipient("79995579399@c.us"));

    [Test]
    public void NormalizeRecipient_KeepsGroupAndBare()
    {
        Assert.AreEqual("120363@g.us", WappiMediaRequestFactory.NormalizeRecipient("120363@g.us"));
        Assert.AreEqual("79995579399", WappiMediaRequestFactory.NormalizeRecipient("79995579399"));
    }

    [Test]
    public void BuildBody_Document_IncludesFileName()
    {
        string body = WappiMediaRequestFactory.BuildBody(
            AttachmentKind.Document, "79995579399@c.us", "cap", "report.pdf", "QkFTRTY0");
        StringAssert.Contains("\"recipient\":\"79995579399\"", body);
        StringAssert.Contains("\"caption\":\"cap\"", body);
        StringAssert.Contains("\"file_name\":\"report.pdf\"", body);
        StringAssert.Contains("\"b64_file\":\"QkFTRTY0\"", body);
    }

    [Test]
    public void BuildBody_Image_OmitsFileName()
    {
        string body = WappiMediaRequestFactory.BuildBody(
            AttachmentKind.GalleryImage, "79995579399@c.us", "", null, "QkFTRTY0");
        StringAssert.DoesNotContain("file_name", body);
        StringAssert.Contains("\"caption\":\"\"", body);
        StringAssert.Contains("\"b64_file\":\"QkFTRTY0\"", body);
    }

    [Test]
    public void BuildBody_DocumentMissingName_FallsBackToFile()
    {
        string body = WappiMediaRequestFactory.BuildBody(
            AttachmentKind.Document, "x@c.us", "", "", "Qg==");
        StringAssert.Contains("\"file_name\":\"file\"", body);
    }
}

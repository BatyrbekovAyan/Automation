using System.Text;
using NUnit.Framework;

// Contract tests for the client-side price-list payload preparation: the
// extension routing, the on-device conversion to the two formats the n8n
// Upload File workflow ingests (text/plain and PDF, plus JPEG for photos),
// and the deterministic-vs-retryable failure verdicts.
//
// Extracted out of the upload coroutine so the composed payload — the exact
// bytes, name and MIME that go on the wire — is assertable without a network
// round trip. Routing reads the extension from the picker's file PATH while
// the payload name comes from the display name: gallery picks hand back
// pickedMediaN.jpg temp copies under a synthesized display name.
public class UploadPayloadBuilderTests
{
    private const string Product = "product";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Test]
    public void Pdf_PassesBytesThroughUnconverted()
    {
        byte[] bytes = Utf8("%PDF-1.4 fake");

        UploadPayload payload = UploadPayloadBuilder.Build(bytes, "меню.pdf", "/tmp/меню.pdf", Product);

        Assert.IsTrue(payload.Ok);
        CollectionAssert.AreEqual(bytes, payload.Bytes);
        Assert.AreEqual("меню.pdf", payload.Name);
        Assert.AreEqual("application/pdf", payload.Mime);
    }

    // Mobile pickers filter by MIME/UTI, not by name — "MENU.PDF" is pickable
    // and an ordinal ".pdf" compare would fall through to the unsupported
    // branch and post a form with no file part.
    [Test]
    public void Pdf_UppercaseExtension_StillRoutes()
    {
        UploadPayload payload = UploadPayloadBuilder.Build(Utf8("%PDF"), "MENU.PDF", "/tmp/MENU.PDF", Product);

        Assert.IsTrue(payload.Ok);
        Assert.AreEqual("application/pdf", payload.Mime);
    }

    [Test]
    public void Txt_DecodesWindows1251_ToUtf8Payload()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] cp1251 = Encoding.GetEncoding(1251).GetBytes("Яблоки 500 тг");

        UploadPayload payload = UploadPayloadBuilder.Build(cp1251, "прайс.txt", "/tmp/прайс.txt", Product);

        Assert.IsTrue(payload.Ok);
        Assert.AreEqual("Яблоки 500 тг", Encoding.UTF8.GetString(payload.Bytes));
        Assert.AreEqual("text/plain", payload.Mime);
        Assert.AreEqual("прайс.txt", payload.Name);
    }

    [Test]
    public void Csv_ConvertsToText_AndSuffixesNameWithTxt()
    {
        byte[] csv = Utf8("Название;Цена\nЯблоки;500\n");

        UploadPayload payload = UploadPayloadBuilder.Build(csv, "прайс.csv", "/tmp/прайс.csv", Product);

        Assert.IsTrue(payload.Ok);
        Assert.AreEqual("прайс.csv.txt", payload.Name);
        Assert.AreEqual("text/plain", payload.Mime);
        StringAssert.Contains("Яблоки", Encoding.UTF8.GetString(payload.Bytes));
    }

    [Test]
    public void Xml_ConvertsToText_AndReplacesExtension()
    {
        byte[] xml = Utf8("<?xml version=\"1.0\" encoding=\"utf-8\"?><list><item>Яблоки</item></list>");

        UploadPayload payload = UploadPayloadBuilder.Build(xml, "прайс.xml", "/tmp/прайс.xml", Product);

        Assert.IsTrue(payload.Ok);
        Assert.AreEqual("прайс.txt", payload.Name);
        Assert.AreEqual("text/plain", payload.Mime);
    }

    [Test]
    public void UnsupportedExtension_FailsWithFormatReason_AndNoRetry()
    {
        UploadPayload payload = UploadPayloadBuilder.Build(Utf8("PK"), "прайс.zip", "/tmp/прайс.zip", Product);

        Assert.IsFalse(payload.Ok);
        Assert.AreEqual(UploadFailureText.UnsupportedFormat(".zip"), payload.FailReasonRu);
        Assert.IsNull(payload.Bytes);
    }

    // .doc is the unsupported format users actually hit (old Word, 1C exports)
    // — it gets the concrete fix, not just the verdict.
    [Test]
    public void LegacyDoc_FailsWithItsOwnGuidance()
    {
        UploadPayload payload = UploadPayloadBuilder.Build(Utf8("\xD0\xCF"), "прайс.doc", "/tmp/прайс.doc", Product);

        Assert.IsFalse(payload.Ok);
        Assert.AreEqual(UploadFailureText.UnsupportedFormat(".doc"), payload.FailReasonRu);
        StringAssert.Contains(".doc", payload.FailReason);
    }

    [Test]
    public void EmptyConversion_FailsAsEmptyFile()
    {
        UploadPayload payload = UploadPayloadBuilder.Build(Utf8("   \n  "), "пусто.txt", "/tmp/пусто.txt", Product);

        Assert.IsFalse(payload.Ok);
        Assert.AreEqual(UploadFailureText.EmptyFile, payload.FailReasonRu);
    }

    [Test]
    public void ConverterThrow_FailsAsUnreadable()
    {
        byte[] notXml = Utf8("this is definitely not xml <<<>>>");

        UploadPayload payload = UploadPayloadBuilder.Build(notXml, "битый.xml", "/tmp/битый.xml", Product);

        Assert.IsFalse(payload.Ok);
        Assert.AreEqual(UploadFailureText.Unreadable, payload.FailReasonRu);
    }

    [Test]
    public void MissingImageFile_FailsAsUndecodablePhoto()
    {
        UploadPayload payload = UploadPayloadBuilder.Build(
            Utf8("not really an image"), "Прайс 1.jpg", "/tmp/definitely-not-here-9f3a.jpg", Product);

        Assert.IsFalse(payload.Ok);
        Assert.AreEqual(UploadFailureText.PhotoUndecodable, payload.FailReasonRu);
    }

    // The workflow's Switch routes on the payload NAME, so a synthesized
    // gallery name must still end in .jpg even when the temp copy did not.
    [Test]
    public void ImageName_AlwaysEndsInJpg()
    {
        Assert.AreEqual("Прайс 1.jpg", UploadPayloadBuilder.JpegPayloadName("Прайс 1.jpg"));
        Assert.AreEqual("Прайс 1.png.jpg", UploadPayloadBuilder.JpegPayloadName("Прайс 1.png"));
        Assert.AreEqual("SNAP.JPG", UploadPayloadBuilder.JpegPayloadName("SNAP.JPG"));
    }

    [Test]
    public void NullFileData_FailsAsUnreadable()
    {
        UploadPayload payload = UploadPayloadBuilder.Build(null, "прайс.txt", "/tmp/прайс.txt", Product);

        Assert.IsFalse(payload.Ok);
        Assert.AreEqual(UploadFailureText.Unreadable, payload.FailReasonRu);
    }
}

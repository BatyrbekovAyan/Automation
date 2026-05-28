using NUnit.Framework;

public class AttachmentDisplayFormatTests
{
    // ── HumanReadableBytes ────────────────────────────────────────

    [TestCase(512L,         "<1 KB")]
    [TestCase(1023L,        "<1 KB")]
    [TestCase(1024L,        "1 KB")]
    [TestCase(1500L,        "1 KB")]
    [TestCase(10240L,       "10 KB")]
    [TestCase(1048576L,     "1.0 MB")]
    [TestCase(1500000L,     "1.4 MB")]
    [TestCase(15728640L,    "15.0 MB")]
    [TestCase(1073741824L,  "1.0 GB")]
    [TestCase(1610612736L,  "1.5 GB")]
    public void HumanReadableBytes_Returns_Expected(long bytes, string expected)
    {
        Assert.AreEqual(expected, AttachmentDisplayFormat.HumanReadableBytes(bytes));
    }

    [TestCase(0L,           "<1 KB")]
    [TestCase(-1L,          "<1 KB")]
    [TestCase(long.MinValue, "<1 KB")]
    public void HumanReadableBytes_NegativeOrZero_ReturnsLessThanOneKb(long bytes, string expected)
    {
        Assert.AreEqual(expected, AttachmentDisplayFormat.HumanReadableBytes(bytes));
    }

    // ── ShortMime ─────────────────────────────────────────────────

    [TestCase(null,                                                                                  "")]
    [TestCase("",                                                                                    "")]
    [TestCase("no-slash",                                                                            "")]
    [TestCase("application/",                                                                        "")]
    [TestCase("application/pdf",                                                                     "PDF")]
    [TestCase("image/jpeg",                                                                          "JPEG")]
    [TestCase("image/png",                                                                           "PNG")]
    [TestCase("video/mp4",                                                                           "MP4")]
    [TestCase("video/quicktime",                                                                     "QUICKTIME")]
    [TestCase("text/plain",                                                                          "PLAIN")]
    [TestCase("application/zip",                                                                     "ZIP")]
    [TestCase("application/msword",                                                                  "MSWORD")]
    [TestCase("application/vnd.openxmlformats-officedocument.wordprocessingml.document",             "DOCX")]
    [TestCase("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",                   "XLSX")]
    public void ShortMime_Returns_Expected(string mime, string expected)
    {
        Assert.AreEqual(expected, AttachmentDisplayFormat.ShortMime(mime));
    }
}

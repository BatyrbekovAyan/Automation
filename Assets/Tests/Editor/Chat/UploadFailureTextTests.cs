using NUnit.Framework;

// User-facing Russian reasons for the failed price-list upload row.
// Deterministic failures must name the actual problem (not the generic
// tap-to-retry hint) so the user can fix the file instead of retrying
// an upload that can only fail the same way again.
public class UploadFailureTextTests
{
    [Test]
    public void UnsupportedFormat_Doc_TellsUserToResaveAsDocxOrPdf()
    {
        Assert.AreEqual(
            "Формат .doc не поддерживается — сохраните как .docx или PDF",
            UploadFailureText.UnsupportedFormat(".doc"));
    }

    [Test]
    public void UnsupportedFormat_Doc_IsCaseInsensitive()
    {
        // UploadFile lowercases extensions, but the mapping must not depend on it.
        Assert.AreEqual(
            UploadFailureText.UnsupportedFormat(".doc"),
            UploadFailureText.UnsupportedFormat(".DOC"));
    }

    [Test]
    public void UnsupportedFormat_OtherExtension_NamesTheExtension()
    {
        Assert.AreEqual("Формат .pages не поддерживается",
                        UploadFailureText.UnsupportedFormat(".pages"));
    }

    [Test]
    public void UnsupportedFormat_MissingExtension_FallsBackToGenericText()
    {
        Assert.AreEqual("Формат файла не поддерживается",
                        UploadFailureText.UnsupportedFormat(""));
        Assert.AreEqual("Формат файла не поддерживается",
                        UploadFailureText.UnsupportedFormat(null));
    }

    [Test]
    public void DeterministicReasons_NeverContainTheRetryHint()
    {
        // Deterministic rows suppress retry — their text must not invite a tap.
        string[] reasons =
        {
            UploadFailureText.UnsupportedFormat(".doc"),
            UploadFailureText.UnsupportedFormat(".pages"),
            UploadFailureText.EmptyFile,
            UploadFailureText.Unreadable,
        };
        foreach (string reason in reasons)
            StringAssert.DoesNotContain("повторить", reason);
    }

    [Test]
    public void PhotoUndecodable_IsRussianAndNonEmpty()
    {
        StringAssert.Contains("фото", UploadFailureText.PhotoUndecodable.ToLower());
    }

    [Test]
    public void NoPriceDataOnPhoto_IsRussianAndNonEmpty()
    {
        StringAssert.Contains("не видно цен", UploadFailureText.NoPriceDataOnPhoto);
    }

    [Test]
    public void ReasonForHttpResponse_422NoPriceData_MapsToPhotoReason()
    {
        string reason = UploadFailureText.ReasonForHttpResponse(422, "{\"success\":false,\"error\":\"no_price_data\"}");
        Assert.AreEqual(UploadFailureText.NoPriceDataOnPhoto, reason);
    }

    [Test]
    public void ReasonForHttpResponse_OtherCodes_ReturnsNull()
    {
        Assert.IsNull(UploadFailureText.ReasonForHttpResponse(500, "boom"));
        Assert.IsNull(UploadFailureText.ReasonForHttpResponse(422, "{\"error\":\"something_else\"}"));
    }
}

using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

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

    /// <summary>
    /// R3 (2026-09-27, staging on prod's n8n 2.36): when the Upload File workflow FAILS before its Respond node,
    /// n8n answers 200 with an EMPTY body — and UploadCenter recorded that as an uploaded file (a corrupt PDF
    /// showed as uploaded while nothing was ingested). A 200 counts only with the workflow's own success body.
    /// </summary>
    [Test]
    public void UploadCenter_RecordsAnUploadOnlyAfterTheSuccessBodyCheck()
    {
        string src = Regex.Replace(File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/UploadCenter.cs")), @"//[^\n]*", "");
        int check = src.IndexOf("UploadFailureText.IsAcceptedResponse(", System.StringComparison.Ordinal);
        int record = src.IndexOf("UploadedFilesStore.Add(", System.StringComparison.Ordinal);
        Assert.IsTrue(check >= 0 && record > check, "UploadCenter must check the response body (IsAcceptedResponse) before recording the upload");
    }

    [Test]
    public void ReasonForHttpResponse_422UnreadableFile_IsTheDeterministicUnreadableReason()
    {
        Assert.AreEqual(UploadFailureText.Unreadable,
            UploadFailureText.ReasonForHttpResponse(422, "{\"success\":false,\"error\":\"unreadable_file\"}"));
    }

    [TestCase("{\"success\":true,\"fileId\":\"f-1\"}", "f-1", true)]
    [TestCase("", "f-1", false)]                                          // n8n's 200 when the workflow failed
    [TestCase(null, "f-1", false)]
    [TestCase("{\"success\":true,\"fileId\":\"other\"}", "f-1", false)]  // not this upload's answer
    [TestCase("{\"success\":false,\"error\":\"x\"}", "f-1", false)]
    [TestCase("{\"success\":\"true\",\"fileId\":\"f-1\"}", "f-1", false)]
    [TestCase("{\"message\":\"Workflow executed successfully but no data was returned\"}", "f-1", false)]
    [TestCase("not json", "f-1", false)]
    public void IsAcceptedResponse_OnlyTheWorkflowsSuccessBodyForThisFile(string body, string fileId, bool expected)
    {
        Assert.AreEqual(expected, UploadFailureText.IsAcceptedResponse(body, fileId));
    }

    [Test]
    public void ReasonForHttpResponse_OtherCodes_ReturnsNull()
    {
        Assert.IsNull(UploadFailureText.ReasonForHttpResponse(500, "boom"));
        Assert.IsNull(UploadFailureText.ReasonForHttpResponse(422, "{\"error\":\"something_else\"}"));
    }
}

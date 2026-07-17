using NUnit.Framework;

// Covers MediaDownloadFailure — the pure classifier + capped log formatter behind the
// three silent failure exits of ChatManager.DownloadMediaRoutine (D11 instrumentation).
//
// The classifier names WHY a message/media/download failed (network/timeout, HTTP error,
// empty 2xx body, or a JSON parse throw) so the device pass (08-16) can show the owner an
// actionable line. The Snippet cap is the T-08-15-01 information-disclosure mitigation:
// the response body is NEVER logged in full — first 256 chars, single-line, null-safe.
public class MediaDownloadFailureTests
{
    // ---- Classify ---------------------------------------------------------------

    [Test]
    public void Classify_TransportFailNoStatus_NetworkOrTimeout()
    {
        // result != Success AND responseCode 0 == the connection never reached HTTP
        // (DNS/socket drop or the 30s timeout firing) — the retryable transient kind.
        Assert.AreEqual(MediaDownloadFailureKind.NetworkOrTimeout,
            MediaDownloadFailure.Classify(false, 0, false, false));
    }

    [Test]
    public void Classify_TransportFail4xx_HttpError()
    {
        // A non-2xx status is an HTTP error (permanent for 4xx; the caller decides
        // retryability by status band).
        Assert.AreEqual(MediaDownloadFailureKind.HttpError,
            MediaDownloadFailure.Classify(false, 404, false, false));
    }

    [Test]
    public void Classify_TransportFail5xx_HttpError()
    {
        // 5xx is still classified HttpError; the retry decision (>= 500) lives at the
        // call site, not in the classifier.
        Assert.AreEqual(MediaDownloadFailureKind.HttpError,
            MediaDownloadFailure.Classify(false, 503, false, false));
    }

    [Test]
    public void Classify_SuccessEmptyBody_NoLinkInResponse()
    {
        // A 200 whose body carried neither file_link nor file_b64 — the empty-body exit
        // (exit 2). Likely the server-side cause the owner suspects for missing videos.
        Assert.AreEqual(MediaDownloadFailureKind.NoLinkInResponse,
            MediaDownloadFailure.Classify(true, 200, false, false));
    }

    // ---- Snippet (T-08-15-01: capped, single-line, null-safe) -------------------

    [Test]
    public void Snippet_Null_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, MediaDownloadFailure.Snippet(null));
    }

    [Test]
    public void Snippet_ShortBody_ReturnedVerbatim()
    {
        Assert.AreEqual("{\"status\":\"error\"}",
            MediaDownloadFailure.Snippet("{\"status\":\"error\"}"));
    }

    [Test]
    public void Snippet_LongBody_CappedAtMaxSnippet()
    {
        string huge = new string('x', 4000);
        string snippet = MediaDownloadFailure.Snippet(huge);
        Assert.AreEqual(MediaDownloadFailure.MaxSnippet, snippet.Length,
            "Snippet must never exceed MaxSnippet chars — no full-payload logging.");
        Assert.AreEqual(256, MediaDownloadFailure.MaxSnippet);
    }

    [Test]
    public void Snippet_StripsNewlinesAndTabs_SingleLine()
    {
        string snippet = MediaDownloadFailure.Snippet("line1\nline2\r\nline3\tcol");
        StringAssert.DoesNotContain("\n", snippet);
        StringAssert.DoesNotContain("\r", snippet);
        StringAssert.DoesNotContain("\t", snippet);
        // Content is preserved (only the control chars become spaces).
        StringAssert.Contains("line1", snippet);
        StringAssert.Contains("line3", snippet);
    }

    // ---- FormatLog --------------------------------------------------------------

    [Test]
    public void FormatLog_ContainsIdHttpAndKind()
    {
        string log = MediaDownloadFailure.FormatLog(
            "MSG42", 404, MediaDownloadFailureKind.HttpError, "body-snip");
        StringAssert.Contains("id=MSG42", log);
        StringAssert.Contains("http=404", log);
        StringAssert.Contains("HttpError", log);
        StringAssert.Contains("body-snip", log);
    }

    [Test]
    public void FormatLog_BodyPortionNeverExceedsMaxSnippet()
    {
        // The body embedded in the log is always the already-capped Snippet, so a huge
        // response can never leak in full through FormatLog.
        string snippet = MediaDownloadFailure.Snippet(new string('a', 4000));
        string log = MediaDownloadFailure.FormatLog(
            "MSG1", 200, MediaDownloadFailureKind.NoLinkInResponse, snippet);
        StringAssert.Contains(snippet, log);
        Assert.LessOrEqual(snippet.Length, MediaDownloadFailure.MaxSnippet);
    }
}

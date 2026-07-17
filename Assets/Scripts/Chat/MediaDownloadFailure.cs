using System.Text;

/// <summary>
/// The named reason a <c>message/media/download</c> attempt failed. Drives both the
/// device-visible diagnostic line and the retry decision at the call site.
/// </summary>
public enum MediaDownloadFailureKind
{
    /// <summary>Transport never reached HTTP — socket/DNS drop or the request timed out (responseCode 0). Transient.</summary>
    NetworkOrTimeout,

    /// <summary>A non-2xx HTTP status. 4xx is permanent; 5xx is transient (decided by the caller, not here).</summary>
    HttpError,

    /// <summary>A well-formed 2xx body that carried neither <c>file_link</c> nor <c>file_b64</c>. Permanent — the likely server-side cause.</summary>
    NoLinkInResponse,

    /// <summary>The 2xx body could not be parsed as JSON (the download coroutine's parse throw). Permanent.</summary>
    ParseError
}

/// <summary>
/// Pure (UnityEngine-free) classifier + capped log formatter for the three silent failure
/// exits of <c>ChatManager.DownloadMediaRoutine</c> (D11).
///
/// Before this seam a failed media download called <c>onFailure</c> with NO diagnostics, so a
/// device failure gave the owner nothing to act on. <see cref="Classify"/> names the reason and
/// <see cref="FormatLog"/> emits one compact logcat line — but <see cref="Snippet"/> caps the
/// response body at <see cref="MaxSnippet"/> chars, single-line, so a media payload can never
/// leak in full (T-08-15-01). This must NOT grow into the pre-existing full-payload
/// <c>response.txt</c> dumps (IN-03): no file write, no full body — ever.
///
/// Plain static class (no MonoBehaviour, no UnityEngine) so it stays EditMode-unit-testable
/// alongside the other pure chat seams (ChatRowSwipePolicy, ChatIdFormat, ...).
/// </summary>
public static class MediaDownloadFailure
{
    /// <summary>Hard ceiling on how much of a response body is ever logged. Never the full payload.</summary>
    public const int MaxSnippet = 256;

    /// <summary>
    /// Classify a media-download failure from the request outcome.
    /// <list type="bullet">
    /// <item><c>!resultIsSuccess &amp;&amp; httpStatus == 0</c> ⇒ <see cref="MediaDownloadFailureKind.NetworkOrTimeout"/></item>
    /// <item><c>!resultIsSuccess</c> (any non-zero status) ⇒ <see cref="MediaDownloadFailureKind.HttpError"/></item>
    /// <item>success but neither <paramref name="hasFileLink"/> nor <paramref name="hasFileB64"/> ⇒ <see cref="MediaDownloadFailureKind.NoLinkInResponse"/></item>
    /// </list>
    /// A successful response that DOES carry a reference is not a failure and never reaches this
    /// classifier; a JSON parse throw is classified <see cref="MediaDownloadFailureKind.ParseError"/>
    /// directly by the caller's catch (there is no request-outcome that distinguishes it here).
    /// </summary>
    public static MediaDownloadFailureKind Classify(bool resultIsSuccess, long httpStatus, bool hasFileLink, bool hasFileB64)
    {
        if (!resultIsSuccess)
        {
            return httpStatus == 0
                ? MediaDownloadFailureKind.NetworkOrTimeout
                : MediaDownloadFailureKind.HttpError;
        }

        // Transport succeeded. On a 2xx the only failure the caller classifies is an empty
        // body — neither file_link nor file_b64. A 2xx that DOES carry a reference is a
        // success and is never passed here; hasFileLink/hasFileB64 are part of the documented
        // contract (and let the test assert exactly this empty-body case).
        _ = hasFileLink;
        _ = hasFileB64;
        return MediaDownloadFailureKind.NoLinkInResponse;
    }

    /// <summary>
    /// First <see cref="MaxSnippet"/> chars of <paramref name="body"/>, collapsed to a single
    /// line (CR/LF/tab ⇒ space) and null-safe. NEVER the full body — this is the
    /// information-disclosure cap (T-08-15-01).
    /// </summary>
    public static string Snippet(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;

        int limit = body.Length < MaxSnippet ? body.Length : MaxSnippet;
        var sb = new StringBuilder(limit);
        for (int i = 0; i < limit; i++)
        {
            char c = body[i];
            sb.Append(c == '\n' || c == '\r' || c == '\t' ? ' ' : c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// One compact, device-visible line, e.g.
    /// <c>[MediaDownload] FAIL id=ABC http=404 kind=HttpError body=...</c>. The
    /// <paramref name="bodySnippet"/> must already be a <see cref="Snippet"/> so the body can
    /// never exceed <see cref="MaxSnippet"/> chars.
    /// </summary>
    public static string FormatLog(string messageId, long httpStatus, MediaDownloadFailureKind kind, string bodySnippet)
        => $"[MediaDownload] FAIL id={messageId} http={httpStatus} kind={kind} body={bodySnippet}";
}

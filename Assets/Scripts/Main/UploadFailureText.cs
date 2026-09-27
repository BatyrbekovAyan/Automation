// User-facing (Russian) texts for the failed price-list upload row.
// Deterministic failures — wrong format, empty file, broken file — get a
// specific reason and NO retry affordance: retrying can only fail the same
// way, so the row tells the user what to fix instead. Network failures keep
// the generic tap-to-retry hint. Pure static so the mapping is unit-testable.
public static class UploadFailureText
{
    // Transient (network) failure — the row stays tappable and retries.
    public const string TapToRetry = "Не загрузилось · нажмите, чтобы повторить";

    // Converter produced no text — nothing to ingest, retry is pointless.
    public const string EmptyFile = "Файл пуст или не содержит текста";

    // Converter threw — the file itself can't be parsed.
    public const string Unreadable = "Не удалось обработать файл — возможно, он повреждён";

    // Image decode/downscale/re-encode failed on-device — nothing to upload.
    public const string PhotoUndecodable =
        "Не удалось прочитать фото — попробуйте другой снимок.";

    // Workflow's vision branch ran but found no prices on the photo — retrying
    // the same file can only fail the same way, so no retry affordance.
    public const string NoPriceDataOnPhoto =
        "На фото не видно цен — попробуйте более чёткий снимок.";

    public static string UnsupportedFormat(string extension)
    {
        // .doc is the one unsupported format users actually hit (old Word,
        // 1C exports) — give the concrete fix, not just the verdict.
        if (string.Equals(extension, ".doc", System.StringComparison.OrdinalIgnoreCase))
            return "Формат .doc не поддерживается — сохраните как .docx или PDF";

        return string.IsNullOrEmpty(extension)
            ? "Формат файла не поддерживается"
            : $"Формат {extension} не поддерживается";
    }

    // Deterministic server verdicts that retrying the same file cannot fix.
    // Returns null when the response isn't one of them (caller keeps the
    // generic retryable failure path).
    public static string ReasonForHttpResponse(long responseCode, string responseBody)
    {
        if (responseCode == 422 && responseBody != null && responseBody.Contains("no_price_data"))
            return NoPriceDataOnPhoto;
        // The workflow could not read the file (a broken PDF) — the same bytes fail the same way.
        if (responseCode == 422 && responseBody != null && responseBody.Contains("unreadable_file"))
            return Unreadable;
        return null;
    }

    /// <summary>
    /// A 2xx counts as an upload only with the workflow's own success body naming THIS fileId. When the Upload
    /// File workflow fails before its Respond node, n8n answers 200 with an EMPTY body (R3, 2026-09-27) — treating
    /// that as success recorded a file nothing had ingested.
    /// </summary>
    public static bool IsAcceptedResponse(string responseBody, string fileId)
    {
        if (string.IsNullOrEmpty(responseBody)) return false;
        try
        {
            var jo = Newtonsoft.Json.Linq.JObject.Parse(responseBody);
            var ok = jo["success"];
            if (ok == null || ok.Type != Newtonsoft.Json.Linq.JTokenType.Boolean || !(bool)ok) return false;
            string echoed = jo.Value<string>("fileId");
            return string.IsNullOrEmpty(fileId) || echoed == fileId;
        }
        catch
        {
            return false;
        }
    }
}

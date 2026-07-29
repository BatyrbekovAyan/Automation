using System.IO;
using System.Text;

/// <summary>
/// What actually goes on the wire for one price-list upload: the converted
/// bytes, the name the workflow routes on, and the MIME. A failure carries
/// both a dev-facing reason (error log) and a user-facing Russian one (the
/// failed row).
/// </summary>
public struct UploadPayload
{
    public byte[] Bytes;
    public string Name;
    public string Mime;
    public string FailReason;    // dev-facing, null on success
    public string FailReasonRu;  // user-facing, shown in the row

    public bool Ok => FailReason == null;

    public static UploadPayload Fail(string reason, string reasonRu) =>
        new UploadPayload { FailReason = reason, FailReasonRu = reasonRu };
}

/// <summary>
/// Client-side preparation of a picked price list. Every non-PDF, non-photo
/// format converts to plain text ON-DEVICE because the n8n Upload File
/// workflow only ingests text/plain, PDF and JPEG — and because the local
/// converters emit RAG-ready, entity-labeled text that generic server-side
/// extraction can't (n8n's Extract From File can't parse docx at all).
///
/// Pure and side-effect free apart from the native image decode, so the
/// composed payload is unit-testable without a network round trip.
/// </summary>
public static class UploadPayloadBuilder
{
    /// <param name="fileData">Raw bytes read from <paramref name="filePath"/>.</param>
    /// <param name="fileName">Display name — what the payload is named, and what the workflow's Switch reads.</param>
    /// <param name="filePath">Picker path. Routing reads the extension from HERE: gallery picks hand
    /// back pickedMediaN.jpg temp copies under a synthesized display name.</param>
    /// <param name="contentType">"product" or "service" — the entity label the table converters emit.</param>
    public static UploadPayload Build(byte[] fileData, string fileName, string filePath, string contentType)
    {
        // Lowercased: mobile pickers filter by MIME/UTI, not by name, so a
        // "MENU.PDF" is perfectly pickable — and an ordinal Equals(".pdf")
        // would match no branch and post the form with no file attached.
        string extension = Path.GetExtension(filePath ?? "").ToLowerInvariant();

        bool isPhoto = extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic";

        // Photos read from the path natively (HEIC included), so they are the
        // one branch that does not need the bytes.
        if (fileData == null && !isPhoto)
            return UploadPayload.Fail("file bytes unavailable", UploadFailureText.Unreadable);

        try
        {
            if (isPhoto)
                return BuildPhoto(fileName, filePath);

            if (extension == ".pdf")
                return new UploadPayload { Bytes = fileData, Name = fileName, Mime = "application/pdf" };

            string convertedText = ConvertToText(fileData, fileName, extension, contentType, out string textName);
            if (textName == null)
            {
                // Android pickers can ignore the MIME filter — without this
                // guard the form would post with no file part at all.
                string reason = extension == ".doc"
                    ? "'.doc' (Word 97-2003) is not supported — ask the user to re-save as .docx or PDF"
                    : $"unsupported file type '{extension}'";
                return UploadPayload.Fail(reason, UploadFailureText.UnsupportedFormat(extension));
            }

            if (string.IsNullOrWhiteSpace(convertedText))
                return UploadPayload.Fail("converted to empty text (nothing to ingest)", UploadFailureText.EmptyFile);

            return new UploadPayload
            {
                Bytes = Encoding.UTF8.GetBytes(convertedText),
                Name = textName,
                Mime = "text/plain"
            };
        }
        catch (System.Exception exception)
        {
            return UploadPayload.Fail($"conversion failed: {exception.Message}", UploadFailureText.Unreadable);
        }
    }

    // Photos of menus/price boards: decode, downscale, re-encode JPEG; the
    // workflow's vision branch extracts the text.
    private static UploadPayload BuildPhoto(string fileName, string filePath)
    {
        byte[] jpeg = ImageUploadPreprocessor.ToJpegPayload(filePath);
        if (jpeg == null)
        {
            return UploadPayload.Fail(
                "image decode/downscale/re-encode failed (undecodable, missing, or degenerate)",
                UploadFailureText.PhotoUndecodable);
        }

        return new UploadPayload { Bytes = jpeg, Name = JpegPayloadName(fileName), Mime = "image/jpeg" };
    }

    /// <summary>
    /// The workflow's Switch routes on the payload NAME, so a re-encoded photo
    /// must end in .jpg even when the picked file did not.
    /// </summary>
    public static string JpegPayloadName(string fileName) =>
        fileName != null && fileName.EndsWith(".jpg", System.StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".jpg";

    /// <summary>
    /// Runs the format's on-device converter. <paramref name="payloadName"/> comes
    /// back null for formats we don't support, which is the caller's signal to fail
    /// the row rather than post an empty form.
    /// </summary>
    private static string ConvertToText(byte[] fileData, string fileName, string extension,
                                        string contentType, out string payloadName)
    {
        switch (extension)
        {
            case ".txt":
                // Old-Notepad/1C TXT is often windows-1251 or UTF-16 — the
                // workflow assumes UTF-8, so those used to ingest as mojibake.
                payloadName = fileName;
                return TextEncodingSniffer.Decode(fileData);

            case ".rtf":
                payloadName = fileName + ".txt";
                return RtfToTextConverter.Convert(fileData);

            case ".xml":
                // Byte overload honors the prolog's declared encoding
                // (1C/CommerceML exports are commonly windows-1251).
                payloadName = Path.ChangeExtension(fileName, ".txt");
                return XmlToTextConverter.ConvertXmlToText(fileData);

            case ".csv":
            case ".tsv":
            case ".xls":
            case ".xlsx":
            case ".xlsm":
                payloadName = fileName + ".txt";
                return TableToTextConverter.Convert(fileData, fileName, contentType);

            case ".html":
            case ".htm":
                payloadName = fileName + ".txt";
                return HtmlTableToTextConverter.Convert(fileData, contentType);

            case ".docx":
                payloadName = fileName + ".txt";
                return DocxToTextConverter.Convert(fileData);

            default:
                payloadName = null;
                return null;
        }
    }
}

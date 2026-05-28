using System.Globalization;

public static class AttachmentDisplayFormat
{
    private const long KB = 1024L;
    private const long MB = KB * 1024L;
    private const long GB = MB * 1024L;

    public static string HumanReadableBytes(long bytes)
    {
        if (bytes < KB) return "<1 KB";
        if (bytes < MB) return $"{bytes / KB} KB";
        if (bytes < GB) return ((double)bytes / MB).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return ((double)bytes / GB).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }

    public static string ShortMime(string mime)
    {
        if (string.IsNullOrEmpty(mime)) return "";
        int slash = mime.LastIndexOf('/');
        if (slash < 0 || slash == mime.Length - 1) return "";

        string suffix = mime.Substring(slash + 1);

        // Compatibility overrides for the Office Open XML long-form MIMEs.
        if (suffix.Equals("vnd.openxmlformats-officedocument.wordprocessingml.document",
                          System.StringComparison.OrdinalIgnoreCase)) return "DOCX";
        if (suffix.Equals("vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                          System.StringComparison.OrdinalIgnoreCase)) return "XLSX";

        return suffix.ToUpperInvariant();
    }
}

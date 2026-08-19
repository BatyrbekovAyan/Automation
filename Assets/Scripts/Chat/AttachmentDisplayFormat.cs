using System.Globalization;

public static class AttachmentDisplayFormat
{
    private const long KB = 1024L;
    private const long MB = KB * 1024L;
    private const long GB = MB * 1024L;

    public static string HumanReadableBytes(long bytes)
    {
        // RU units — the app ships Russian-only. InvariantCulture stays on the
        // NUMBER so the decimal separator can never follow the device locale.
        if (bytes < KB) return "<1 КБ";
        if (bytes < MB) return $"{bytes / KB} КБ";
        if (bytes < GB) return string.Format(CultureInfo.InvariantCulture, "{0:0.0} МБ", (double)bytes / MB);
        return string.Format(CultureInfo.InvariantCulture, "{0:0.0} ГБ", (double)bytes / GB);
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

using System;
using System.Globalization;

/// <summary>
/// Formats byte counts for the Privacy page's cache rows (Russian units,
/// comma decimal separator). Pure and deterministic for EditMode tests.
/// Rounding contract: ГБ shows one optional decimal («1,3 ГБ», «2 ГБ»),
/// МБ/КБ round to whole numbers, anything non-positive reads «0 МБ» and
/// anything under a kilobyte reads «1 КБ» so a non-empty cache never
/// displays as zero.
/// </summary>
public static class CacheSizeFormatter
{
    private const long Kilobyte = 1024;
    private const long Megabyte = Kilobyte * 1024;
    private const long Gigabyte = Megabyte * 1024;

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 МБ";

        if (bytes >= Gigabyte)
        {
            string gb = ((double)bytes / Gigabyte)
                .ToString("0.#", CultureInfo.InvariantCulture)
                .Replace('.', ',');
            return $"{gb} ГБ";
        }

        if (bytes >= Megabyte)
            return $"{(long)Math.Round((double)bytes / Megabyte)} МБ";

        long kilobytes = Math.Max(1L, (long)Math.Round((double)bytes / Kilobyte));
        return $"{kilobytes} КБ";
    }
}

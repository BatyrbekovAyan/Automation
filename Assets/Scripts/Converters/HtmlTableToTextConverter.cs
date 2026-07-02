using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Extracts price-list text from HTML: real .html/.htm exports and the
/// classic 1C/legacy "fake .xls" (an HTML table or SpreadsheetML 2003
/// document saved with an .xls extension). Rows are collected DOCUMENT-wide
/// (per-&lt;table&gt; matching truncated nested layout tables and silently
/// dropped rows) and rendered by TableToTextConverter.RowsToText, so header
/// selection, title rows and colspan'd headers behave exactly like the
/// CSV/Excel paths. Anything the row parsers can't shape falls back to
/// tag-stripped plain text — never an empty result for a non-empty document.
/// </summary>
public static class HtmlTableToTextConverter
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;
    private static readonly Regex CommentRegex = new(@"<!--.*?-->", RegexOptions.Singleline);
    private static readonly Regex ScriptStyleRegex = new(@"<(script|style)[^>]*>.*?</\1\s*>", Opts);
    // "(\s[^>]*)?" (attrs empty or whitespace-led) keeps <thead>/<track> from
    // matching as <th>/<tr> starts — the group stays capture #1 in all four.
    private static readonly Regex RowRegex = new(@"<tr(\s[^>]*)?>(.*?)</tr\s*>", Opts);
    private static readonly Regex CellRegex = new(@"<t[dh](\s[^>]*)?>(.*?)</t[dh]\s*>", Opts);
    // SpreadsheetML 2003 — Excel-XML price lists exported by 1C/PHP web backends, usually named .xls
    private static readonly Regex SsRowRegex = new(@"<row(\s[^>]*)?>(.*?)</row\s*>", Opts);
    private static readonly Regex SsCellRegex = new(@"<data(\s[^>]*)?>(.*?)</data\s*>", Opts);
    private static readonly Regex ColspanRegex = new(@"colspan\s*=\s*[""']?(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Singleline);

    public static string Convert(byte[] htmlBytes, string entity) =>
        Convert(TextEncodingSniffer.Decode(htmlBytes), entity);

    public static string Convert(string html, string entity)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        // Comments first: commented-out rows ("old price kept just in case")
        // must never become headers or live data.
        html = ScriptStyleRegex.Replace(CommentRegex.Replace(html, " "), " ");

        var rows = ExtractRows(html, RowRegex, CellRegex, expandColspan: true);
        if (rows.Count == 0)
            rows = ExtractRows(html, SsRowRegex, SsCellRegex, expandColspan: false);

        string result = rows.Count > 0
            ? TableToTextConverter.RowsToText(rows, entity).Trim()
            : string.Empty;

        // Spec-legal HTML with omitted </td>/</tr>, exotic markup: plain text
        // with the prices in it still beats hard-failing the upload.
        return string.IsNullOrWhiteSpace(result) ? StripToPlainText(html) : result;
    }

    private static List<string[]> ExtractRows(string html, Regex rowRegex, Regex cellRegex, bool expandColspan)
    {
        var rows = new List<string[]>();
        foreach (Match row in rowRegex.Matches(html))
        {
            var cells = new List<string>();
            foreach (Match cell in cellRegex.Matches(row.Groups[2].Value))
            {
                cells.Add(CleanFragment(cell.Groups[2].Value));
                if (!expandColspan) continue;
                // Pad colspan'd cells so values stay under their real headers.
                var span = ColspanRegex.Match(cell.Groups[1].Value);
                if (span.Success && int.TryParse(span.Groups[1].Value, out int n))
                    for (int k = 1; k < n && k < 50; k++) cells.Add(string.Empty);
            }
            if (cells.Count > 0) rows.Add(cells.ToArray());
        }
        return rows;
    }

    private static string CleanFragment(string html)
    {
        string text = DecodeEntities(TagRegex.Replace(html, " "));
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string StripToPlainText(string html)
    {
        html = Regex.Replace(html, @"<(br|/p|/div|/li|/h[1-6]|/tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        string text = DecodeEntities(TagRegex.Replace(html, " "));
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" *\n[ \n]*", "\n");
        return text.Trim();
    }

    private static string DecodeEntities(string text)
    {
        if (text.IndexOf('&') < 0) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '&') { sb.Append(text[i]); continue; }
            int end = text.IndexOf(';', i + 1);
            if (end < 0 || end - i > 10) { sb.Append('&'); continue; }
            string name = text.Substring(i + 1, end - i - 1);
            string decoded = name switch
            {
                "nbsp" => " ",
                "amp" => "&",
                "lt" => "<",
                "gt" => ">",
                "quot" => "\"",
                "apos" or "#39" => "'",
                "laquo" => "«",
                "raquo" => "»",
                "mdash" => "—",
                "ndash" => "–",
                _ => NumericEntity(name),
            };
            if (decoded != null) { sb.Append(decoded); i = end; }
            else sb.Append('&');
        }
        return sb.ToString();
    }

    private static string NumericEntity(string name)
    {
        if (name.Length < 2 || name[0] != '#') return null;
        string digits = name.Substring(1);
        bool isHex = digits[0] == 'x' || digits[0] == 'X';
        int code;
        bool ok = isHex
            ? int.TryParse(digits.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)
            : int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
        bool valid = ok && code > 0 && code <= 0x10FFFF && (code < 0xD800 || code > 0xDFFF);
        return valid ? char.ConvertFromUtf32(code) : null;
    }
}

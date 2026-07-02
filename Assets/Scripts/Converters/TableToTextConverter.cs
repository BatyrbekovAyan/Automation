using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Data;
using System.Linq;
using ExcelDataReader;

public static class TableToTextConverter
{
    static TableToTextConverter()
    {
        // ОБЯЗАТЕЛЬНО для XLS
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string Convert(
        byte[] fileBytes,
        string fileName,
        string entity // <-- ВАЖНО: entity задаёшь ЯВНО
    )
    {
        entity = entity.ToLower().Trim();

        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".csv" or ".tsv" => CsvToText(fileBytes, entity),
            ".xls" or ".xlsx" or ".xlsm" => SpreadsheetToText(fileBytes, entity),
            _ => throw new NotSupportedException($"Unsupported extension: {ext}")
        };
    }

    // ================= shared row emission =================

    /// <summary>
    /// Renders parsed table rows as RAG-ready text. Headers are the first row
    /// matching the MODAL multi-cell width — real 1C/Excel exports often put
    /// title/company rows above the header row, and taking row[0] blindly
    /// mislabeled every product. Rows before the headers and single-value rows
    /// (section headers like "Напитки") become plain context lines; data cells
    /// beyond the header count are kept label-less instead of silently dropped
    /// (colspan'd headers, ragged exports).
    /// </summary>
    public static string RowsToText(List<string[]> rows, string entity)
    {
        entity = entity.ToLower().Trim();
        var sb = new StringBuilder();
        int index = 1;
        AppendRows(sb, rows, entity, ref index);
        return sb.ToString();
    }

    private static void AppendRows(StringBuilder sb, List<string[]> rows, string entity, ref int index)
    {
        int modalWidth = ModalWidth(rows);
        string[] headers = null;

        foreach (var row in rows)
        {
            int nonEmpty = row.Count(c => !string.IsNullOrEmpty(c));
            if (nonEmpty == 0) continue;

            if (headers == null && nonEmpty >= 2 && row.Length == modalWidth)
            {
                headers = row;
                continue;
            }

            if (headers == null || nonEmpty == 1)
            {
                sb.AppendLine(string.Join(" ", row.Where(c => !string.IsNullOrEmpty(c))));
                continue;
            }

            sb.Append($"{entity}[{index}]: ");
            for (int c = 0; c < row.Length; c++)
            {
                if (string.IsNullOrEmpty(row[c])) continue;
                if (c < headers.Length && !string.IsNullOrEmpty(headers[c]))
                    sb.Append($"{headers[c]}: {row[c]}; ");
                else
                    sb.Append($"{row[c]}; ");
            }
            sb.AppendLine();
            index++;
        }
    }

    private static int ModalWidth(List<string[]> rows)
    {
        var multiCell = rows.Where(r => r.Length >= 2 && r.Any(c => !string.IsNullOrEmpty(c))).ToList();
        if (multiCell.Count == 0) return 0;
        return multiCell
            .GroupBy(r => r.Length)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => multiCell.FindIndex(r => r.Length == g.Key)) // tie → width seen first (headers come before data)
            .First().Key;
    }

    // ================= CSV / TSV =================
    private static string CsvToText(byte[] bytes, string entity) =>
        // Encoding is sniffed (ru-locale Excel exports CSV as windows-1251),
        // and so is the delimiter (it also uses ';' — ',' is the ru decimal
        // separator). Tabs make .tsv fall out of the same path.
        DelimitedToText(TextEncodingSniffer.Decode(bytes), entity);

    private static string DelimitedToText(string text, string entity)
    {
        text = text.Replace("\0", "");
        char delimiter = SniffDelimiter(text);
        return RowsToText(ParseDelimited(text, delimiter), entity);
    }

    private static readonly char[] DelimiterCandidates = { ';', '\t', ',' };

    private static char SniffDelimiter(string text)
    {
        // Quote-aware per-line counts over the first content lines. Scored by
        // how many lines agree on one repeat count — delimiter-free title
        // lines simply don't vote (anchoring on line 1 mis-picked ',' for a
        // titled ';'-CSV). Ties go to the earlier candidate (';' — the
        // ru-locale Excel default).
        var perLine = CountCandidatesPerLine(text, maxLines: 6);

        int best = DelimiterCandidates.Length - 1; // default ','
        int bestScore = -1;
        for (int d = 0; d < DelimiterCandidates.Length; d++)
        {
            var nonZero = perLine.Select(l => l[d]).Where(c => c > 0).ToList();
            if (nonZero.Count == 0) continue;
            int modal = nonZero.GroupBy(c => c)
                .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
                .First().Key;
            int agreeing = nonZero.Count(c => c == modal);
            int score = agreeing * 1000 + modal;
            if (score > bestScore) { best = d; bestScore = score; }
        }
        return DelimiterCandidates[best];
    }

    private static List<int[]> CountCandidatesPerLine(string text, int maxLines)
    {
        var perLine = new List<int[]>();
        var counts = new int[DelimiterCandidates.Length];
        bool inQuotes = false, lineHasContent = false;

        void EndLine()
        {
            if (lineHasContent) perLine.Add(counts);
            counts = new int[DelimiterCandidates.Length];
            lineHasContent = false;
        }

        foreach (char ch in text)
        {
            if (ch == '"') { inQuotes = !inQuotes; lineHasContent = true; continue; }
            if ((ch == '\n' || ch == '\r') && !inQuotes)
            {
                EndLine();
                if (perLine.Count >= maxLines) return perLine;
                continue;
            }
            if (ch == '\t' || !char.IsWhiteSpace(ch)) lineHasContent = true;
            if (!inQuotes)
            {
                for (int d = 0; d < DelimiterCandidates.Length; d++)
                    if (ch == DelimiterCandidates[d]) counts[d]++;
            }
        }
        EndLine();
        return perLine;
    }

    private static List<string[]> ParseDelimited(string text, char delimiter)
    {
        // RFC-4180-style: quoted cells may contain the delimiter, newlines and
        // "" escapes — naive Split() shredded real Excel exports on all three.
        // Bare CR is a row terminator too (Excel for Mac "CSV (Macintosh)").
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;

        void EndCell()
        {
            row.Add(NormalizeCell(cell.ToString()));
            cell.Clear();
        }

        void EndRow()
        {
            EndCell();
            if (row.Any(c => !string.IsNullOrEmpty(c)))
                rows.Add(row.ToArray());
            row.Clear();
        }

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cell.Append(ch);
            }
            else if (ch == '"' && cell.Length == 0) inQuotes = true;
            else if (ch == delimiter) EndCell();
            else if (ch == '\n') EndRow();
            else if (ch == '\r')
            {
                if (i + 1 >= text.Length || text[i + 1] != '\n') EndRow(); // CRLF ends via the '\n'
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) EndRow();

        return rows;
    }

    private static string NormalizeCell(string cell) =>
        Regex.Replace(cell, @"\s+", " ").Trim();

    // ================= XLS / XLSX / XLSM =================
    private static string SpreadsheetToText(byte[] bytes, string entity)
    {
        if (IsOle2(bytes) || IsZip(bytes))
            return ExcelToText(bytes, entity);

        // Classic 1C/legacy trick: a file NAMED .xls that is actually an HTML
        // table, a SpreadsheetML 2003 document, or plain delimited text.
        // ExcelDataReader chokes on all of those.
        string text = TextEncodingSniffer.Decode(bytes);
        return text.TrimStart().StartsWith("<")
            ? HtmlTableToTextConverter.Convert(text, entity)
            : DelimitedToText(text, entity);
    }

    private static bool IsOle2(byte[] b) => // real .xls (BIFF inside OLE2)
        b.Length >= 4 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0;

    private static bool IsZip(byte[] b) => // real .xlsx/.xlsm (OOXML zip)
        b.Length >= 2 && b[0] == 0x50 && b[1] == 0x4B;

    private static string ExcelToText(byte[] bytes, string entity)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var dataSet = reader.AsDataSet();
        var sb = new StringBuilder();
        int index = 1; // continuous across sheets

        foreach (DataTable table in dataSet.Tables)
        {
            var rows = new List<string[]>();
            foreach (DataRow dataRow in table.Rows)
            {
                var cells = dataRow.ItemArray
                    .Select(v => v?.ToString()?.Trim() ?? string.Empty)
                    .ToArray();
                if (cells.Any(c => !string.IsNullOrEmpty(c)))
                    rows.Add(cells);
            }
            AppendRows(sb, rows, entity, ref index);
        }

        return sb.ToString();
    }
}

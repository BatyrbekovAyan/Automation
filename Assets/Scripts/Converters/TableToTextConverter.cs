using System;
using System.IO;
using System.Text;
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
            ".csv"  => CsvToText(fileBytes, entity),
            ".xls"  => ExcelToText(fileBytes, entity),
            ".xlsx" => ExcelToText(fileBytes, entity),
            _ => throw new NotSupportedException($"Unsupported extension: {ext}")
        };
    }

    // ================= CSV =================
    private static string CsvToText(byte[] bytes, string entity)
    {
        var text = Encoding.UTF8.GetString(bytes)
            .Replace("\0", "")
            .Replace("\r", "")
            .Trim();

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) return string.Empty;

        var headers = lines[0]
            .Split(',')
            .Select(h => h.Trim())
            .ToArray();

        var sb = new StringBuilder();
        int index = 1;

        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i]
                .Split(',')
                .Select(v => v.Trim())
                .ToArray();

            sb.Append($"{entity}[{index}]: ");

            for (int c = 0; c < headers.Length && c < values.Length; c++)
            {
                if (!string.IsNullOrEmpty(values[c]))
                    sb.Append($"{headers[c]}: {values[c]}; ");
            }

            sb.AppendLine();
            index++;
        }

        return sb.ToString();
    }

    // ================= XLS / XLSX =================
    private static string ExcelToText(byte[] bytes, string contentType)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var dataSet = reader.AsDataSet();
        var sb = new StringBuilder();
        int index = 1;

        foreach (DataTable table in dataSet.Tables)
        {
            if (table.Rows.Count < 2) continue;

            var headers = table.Rows[0].ItemArray
                .Select(h => h?.ToString()?.Trim())
                .ToArray();

            for (int r = 1; r < table.Rows.Count; r++)
            {
                sb.Append($"{contentType}[{index}]: ");

                for (int c = 0; c < headers.Length; c++)
                {
                    var value = table.Rows[r][c]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(value))
                        sb.Append($"{headers[c]}: {value}; ");
                }

                sb.AppendLine();
                index++;
            }
        }

        return sb.ToString();
    }
}
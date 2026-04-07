using System;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

public static class DocxToTextConverter
{
    public static string Convert(byte[] docxBytes)
    {
        if (docxBytes == null || docxBytes.Length == 0)
            throw new ArgumentException("DOCX bytes are empty");

        var sb = new StringBuilder();

        using var stream = new MemoryStream(docxBytes);
        using var wordDoc = WordprocessingDocument.Open(stream, false);

        var body = wordDoc.MainDocumentPart?.Document?.Body;
        if (body == null)
            return string.Empty;

        foreach (var element in body.Elements())
        {
            switch (element)
            {
                case Paragraph paragraph:
                    AppendParagraph(sb, paragraph);
                    break;

                case Table table:
                    AppendTable(sb, table);
                    break;
            }
        }

        return sb.ToString().Trim();
    }

    // --------------------
    // Paragraphs
    // --------------------
    private static void AppendParagraph(StringBuilder sb, Paragraph paragraph)
    {
        var text = paragraph.InnerText?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        sb.AppendLine(text);
        sb.AppendLine();
    }

    // --------------------
    // Tables
    // --------------------
    private static void AppendTable(StringBuilder sb, Table table)
    {
        sb.AppendLine("[Table]");

        int rowIndex = 1;
        foreach (var row in table.Elements<TableRow>())
        {
            sb.Append($"Row {rowIndex}: ");

            var_toggle:
            bool firstCell = true;

            foreach (var cell in row.Elements<TableCell>())
            {
                var cellText = cell.InnerText?.Trim();
                if (string.IsNullOrEmpty(cellText))
                    continue;

                if (!firstCell)
                    sb.Append("; ");

                sb.Append(cellText);
                firstCell = false;
            }

            sb.AppendLine(";");
            rowIndex++;
        }

        sb.AppendLine();
    }
}
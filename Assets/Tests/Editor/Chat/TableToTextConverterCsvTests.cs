using System.Text;
using NUnit.Framework;

public class TableToTextConverterCsvTests
{
    [OneTimeSetUp]
    public void RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static string ConvertCsv(string text, string fileName = "прайс.csv", Encoding encoding = null)
    {
        encoding ??= Encoding.UTF8;
        return TableToTextConverter.Convert(encoding.GetBytes(text), fileName, "Product");
    }

    [Test]
    public void Convert_RuLocaleCsv_SemicolonAndCp1251()
    {
        // The default export of ru-locale Excel: ';' delimiter, windows-1251.
        string result = ConvertCsv("Название;Цена\nЧай;5000\nКофе;7000",
            encoding: Encoding.GetEncoding(1251));
        StringAssert.Contains("product[1]: Название: Чай; Цена: 5000;", result);
        StringAssert.Contains("product[2]: Название: Кофе; Цена: 7000;", result);
    }

    [Test]
    public void Convert_QuotedFieldWithDelimiterAndNewline_StaysOneCell()
    {
        string result = ConvertCsv("Name,Price\n\"Tea, black\npremium\",5000");
        StringAssert.Contains("Name: Tea, black premium;", result);
        StringAssert.Contains("Price: 5000;", result);
        Assert.IsFalse(result.Contains("product[2]"), "quoted newline must not split the row");
    }

    [Test]
    public void Convert_EscapedQuotes_Unescaped()
    {
        string result = ConvertCsv("Name,Note\nTea,\"Say \"\"hi\"\"\"");
        StringAssert.Contains("Note: Say \"hi\";", result);
    }

    [Test]
    public void Convert_TsvExtension_ParsesTabs()
    {
        string result = TableToTextConverter.Convert(
            Encoding.UTF8.GetBytes("Name\tPrice\nTea\t5000"), "прайс.tsv", "Product");
        StringAssert.Contains("Name: Tea; Price: 5000;", result);
    }

    [Test]
    public void Convert_CommasInsideCells_SemicolonStillWins()
    {
        string result = ConvertCsv("Товар, вид;Цена\nЧай, черный;5000");
        StringAssert.Contains("Товар, вид: Чай, черный;", result);
        StringAssert.Contains("Цена: 5000;", result);
    }

    [Test]
    public void Convert_Utf8BomCsv_BomDoesNotPolluteFirstHeader()
    {
        byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF };
        bytes = System.Linq.Enumerable.ToArray(
            System.Linq.Enumerable.Concat(bytes, Encoding.UTF8.GetBytes("Name,Price\nTea,5000")));
        string result = TableToTextConverter.Convert(bytes, "x.csv", "Product");
        StringAssert.Contains("product[1]: Name: Tea;", result);
    }

    [Test]
    public void Convert_SingleLineCsv_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, ConvertCsv("Name,Price"));
    }

    [Test]
    public void Convert_FakeXlsThatIsHtml_RoutedToHtmlTableParser()
    {
        // Classic 1C export: an HTML table saved with an .xls extension.
        string html = "<html><body><table><tr><td>Название</td><td>Цена</td></tr>" +
                      "<tr><td>Чай</td><td>5000</td></tr></table></body></html>";
        string result = TableToTextConverter.Convert(
            Encoding.GetEncoding(1251).GetBytes(html), "прайс.xls", "Product");
        StringAssert.Contains("Название: Чай;", result);
        StringAssert.Contains("Цена: 5000;", result);
    }

    [Test]
    public void Convert_FakeXlsThatIsDelimitedText_RoutedToCsvParser()
    {
        string result = TableToTextConverter.Convert(
            Encoding.UTF8.GetBytes("Name;Price\nTea;5000"), "export.xls", "Product");
        StringAssert.Contains("Name: Tea; Price: 5000;", result);
    }

    [Test]
    public void Convert_XlsmExtension_AcceptedByExtensionSwitch()
    {
        // Not a real workbook — routing is what's under test: xlsm must not
        // throw NotSupportedException, and non-zip content falls to text.
        string result = TableToTextConverter.Convert(
            Encoding.UTF8.GetBytes("Name;Price\nTea;5000"), "макрос.xlsm", "Product");
        StringAssert.Contains("Name: Tea; Price: 5000;", result);
    }

    [Test]
    public void Convert_CrOnlyLineEndings_MacExcelCsv_Parses()
    {
        // Excel for Mac "CSV (Macintosh)" emits bare CR line endings.
        string result = ConvertCsv("Название;Цена\rЧай;5000\rКофе;7000");
        StringAssert.Contains("product[1]: Название: Чай; Цена: 5000;", result);
        StringAssert.Contains("product[2]: Название: Кофе; Цена: 7000;", result);
    }

    [Test]
    public void Convert_TitleLineAboveHeader_DelimiterAndHeadersStillRight()
    {
        // A delimiter-free title line must neither hijack delimiter sniffing
        // (defaulting to ',') nor become the header row.
        string result = ConvertCsv("ООО Ромашка Прайс-лист\nНазвание;Цена\nЧай;5000\nКофе;7000");
        StringAssert.Contains("ООО Ромашка Прайс-лист", result);
        StringAssert.Contains("product[1]: Название: Чай; Цена: 5000;", result);
    }

    [Test]
    public void Convert_ExtraCellsBeyondHeaders_KeptLabelless()
    {
        string result = ConvertCsv("Name;Price\nTea;5000;хит сезона");
        StringAssert.Contains("Name: Tea; Price: 5000; хит сезона;", result);
    }

    [Test]
    public void Convert_SectionHeaderRowsInsideData_BecomePlainLines()
    {
        string result = ConvertCsv("Название;Цена\nНапитки;\nЧай;5000");
        StringAssert.Contains("Напитки", result);
        Assert.IsFalse(result.Contains("Название: Напитки"), "single-value row is a section header, not a product");
        StringAssert.Contains("product[1]: Название: Чай; Цена: 5000;", result);
    }
}

using System.Text;
using NUnit.Framework;

public class HtmlTableToTextConverterTests
{
    [OneTimeSetUp]
    public void RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Test]
    public void Convert_BasicTable_EmitsEntityRows()
    {
        string html = "<table><tr><td>Name</td><td>Price</td></tr>" +
                      "<tr><td>Tea</td><td>5000</td></tr><tr><td>Coffee</td><td>7000</td></tr></table>";
        string result = HtmlTableToTextConverter.Convert(html, "Product");
        StringAssert.Contains("product[1]: Name: Tea; Price: 5000;", result);
        StringAssert.Contains("product[2]: Name: Coffee; Price: 7000;", result);
    }

    [Test]
    public void Convert_UppercaseTagsWithAttributes_StillParsed()
    {
        string html = "<TABLE border=\"1\"><TR><TH>Name</TH><TH>Price</TH></TR>" +
                      "<TR><TD align=\"left\">Tea</TD><TD>5000</TD></TR></TABLE>";
        string result = HtmlTableToTextConverter.Convert(html, "Product");
        StringAssert.Contains("Name: Tea; Price: 5000;", result);
    }

    [Test]
    public void Convert_SingleCellTitleRows_KeptAsPlainLines()
    {
        string html = "<table><tr><td colspan=\"2\">Прайс-лист 2026</td></tr>" +
                      "<tr><td>Название</td><td>Цена</td></tr>" +
                      "<tr><td>Чай</td><td>5000</td></tr></table>";
        string result = HtmlTableToTextConverter.Convert(html, "Product");
        StringAssert.Contains("Прайс-лист 2026", result);
        StringAssert.Contains("product[1]: Название: Чай; Цена: 5000;", result);
    }

    [Test]
    public void Convert_HtmlEntities_Decoded()
    {
        string html = "<table><tr><td>Name</td><td>Price</td></tr>" +
                      "<tr><td>&#1063;&#1072;&#1081;</td><td>5&nbsp;000&amp;up</td></tr></table>";
        string result = HtmlTableToTextConverter.Convert(html, "Product");
        StringAssert.Contains("Name: Чай;", result);
        StringAssert.Contains("Price: 5 000&up;", result);
    }

    [Test]
    public void Convert_NestedMarkupInsideCells_Stripped()
    {
        string html = "<table><tr><td>Name</td><td>Price</td></tr>" +
                      "<tr><td><b>Tea</b> <span class=\"x\">black</span></td><td><i>5000</i></td></tr></table>";
        string result = HtmlTableToTextConverter.Convert(html, "Product");
        StringAssert.Contains("Name: Tea black;", result);
        StringAssert.Contains("Price: 5000;", result);
    }

    [Test]
    public void Convert_NoTable_FallsBackToStrippedText()
    {
        string html = "<html><head><style>p{color:red}</style><script>var x=1;</script></head>" +
                      "<body><p>Прайс</p><p>Чай — 5000 тг</p></body></html>";
        string result = HtmlTableToTextConverter.Convert(html, "Product");
        StringAssert.Contains("Прайс", result);
        StringAssert.Contains("Чай — 5000 тг", result);
        Assert.IsFalse(result.Contains("var x"), "script content must be stripped");
        Assert.IsFalse(result.Contains("color:red"), "style content must be stripped");
        Assert.IsFalse(result.Contains("<"), "tags must be stripped");
    }

    [Test]
    public void Convert_Cp1251Bytes_DecodedViaSniffer()
    {
        string html = "<table><tr><td>Название</td><td>Цена</td></tr>" +
                      "<tr><td>Чай</td><td>5000</td></tr></table>";
        string result = HtmlTableToTextConverter.Convert(
            Encoding.GetEncoding(1251).GetBytes(html), "Product");
        StringAssert.Contains("Название: Чай;", result);
    }

    [Test]
    public void Convert_EmptyInput_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, HtmlTableToTextConverter.Convert("", "Product"));
        Assert.AreEqual(string.Empty, HtmlTableToTextConverter.Convert("   ", "Product"));
    }

    [Test]
    public void Convert_CommentedOutRow_NeverBecomesHeadersOrData()
    {
        // "Old price kept commented out" is common in hand-edited CMS pages.
        string html = "<table><!-- <tr><td>старая</td><td>999</td></tr> -->" +
                      "<tr><td>Name</td><td>Price</td></tr><tr><td>Tea</td><td>5000</td></tr></table>";
        string result = HtmlTableToTextConverter.Convert(html, "Product");
        Assert.IsFalse(result.Contains("старая"), "commented row leaked as data");
        Assert.IsFalse(result.Contains("999"), "commented value leaked");
        StringAssert.Contains("product[1]: Name: Tea; Price: 5000;", result);
    }

    [Test]
    public void Convert_NestedLayoutTable_NoRowsLost()
    {
        string html = "<table><tr><td>Name</td><td>Price</td></tr>" +
                      "<table><tr><td>Tea</td><td>5000</td></tr></table>" +
                      "<tr><td>Coffee</td><td>7000</td></tr></table>";
        string result = HtmlTableToTextConverter.Convert(html, "Product");
        StringAssert.Contains("Name: Tea; Price: 5000;", result);
        StringAssert.Contains("Name: Coffee; Price: 7000;", result);
    }

    [Test]
    public void Convert_ColspanHeader_ValuesStayUnderRealHeaders()
    {
        string html = "<table><tr><th colspan=\"2\">Товар</th><th>Цена</th></tr>" +
                      "<tr><td>Чай</td><td>черный</td><td>5000</td></tr></table>";
        string result = HtmlTableToTextConverter.Convert(html, "Product");
        StringAssert.Contains("Товар: Чай;", result);
        StringAssert.Contains("черный;", result);
        StringAssert.Contains("Цена: 5000;", result);
    }

    [Test]
    public void Convert_SpreadsheetMl2003_ParsedViaRowDataRegexes()
    {
        // Excel-XML "fake .xls" from 1C/PHP web exports: <Row>/<Cell>/<Data>.
        string xml = "<?xml version=\"1.0\"?><Workbook><Worksheet ss:Name=\"Лист1\"><Table>" +
                     "<Row><Cell><Data ss:Type=\"String\">Name</Data></Cell><Cell><Data ss:Type=\"String\">Price</Data></Cell></Row>" +
                     "<Row><Cell><Data ss:Type=\"String\">Tea</Data></Cell><Cell><Data ss:Type=\"Number\">5000</Data></Cell></Row>" +
                     "</Table></Worksheet></Workbook>";
        string result = TableToTextConverter.Convert(Encoding.UTF8.GetBytes(xml), "экспорт.xls", "Product");
        StringAssert.Contains("product[1]: Name: Tea; Price: 5000;", result);
    }

    [Test]
    public void Convert_OmittedClosingTags_FallsBackToPlainTextNotEmpty()
    {
        // Spec-legal HTML may omit </td>/</tr>; plain text still beats failing.
        string html = "<table><tr><td>Чай<td>5000<tr><td>Кофе<td>7000</table>";
        string result = HtmlTableToTextConverter.Convert(html, "Product");
        StringAssert.Contains("Чай", result);
        StringAssert.Contains("5000", result);
        StringAssert.Contains("Кофе", result);
        Assert.IsFalse(result.Contains("<"), "tags must be stripped in the fallback");
    }
}

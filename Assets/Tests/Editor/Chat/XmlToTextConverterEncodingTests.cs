using System.Text;
using NUnit.Framework;

public class XmlToTextConverterEncodingTests
{
    [OneTimeSetUp]
    public void RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Test]
    public void ConvertBytes_Windows1251DeclaredXml_DecodedPerProlog()
    {
        // The 1C/CommerceML shape: prolog declares windows-1251, body is Cyrillic.
        string xml = "<?xml version=\"1.0\" encoding=\"windows-1251\"?>" +
                     "<товары><товар><название>Чай черный</название><цена>5000</цена></товар></товары>";
        byte[] bytes = Encoding.GetEncoding(1251).GetBytes(xml);

        string result = XmlToTextConverter.ConvertXmlToText(bytes);

        StringAssert.Contains("Чай черный", result);
        StringAssert.Contains("5000", result);
    }

    [Test]
    public void ConvertBytes_Utf8Xml_StillWorks()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                     "<items><item><name>Tea</name><price>5000</price></item></items>";
        string result = XmlToTextConverter.ConvertXmlToText(Encoding.UTF8.GetBytes(xml));

        StringAssert.Contains("Tea", result);
        StringAssert.Contains("5000", result);
    }

    [Test]
    public void ConvertBytes_MatchesStringOverloadForUtf8()
    {
        string xml = "<items><item><name>Tea</name></item></items>";
        Assert.AreEqual(
            XmlToTextConverter.ConvertXmlToText(xml),
            XmlToTextConverter.ConvertXmlToText(Encoding.UTF8.GetBytes(xml)));
    }

    [Test]
    public void ConvertBytes_AttributeCarriedData_Emitted()
    {
        // Kaspi price XML carries sku/model as attributes, not elements.
        string xml = "<offers><offer sku=\"ABC-1\" price=\"5000\"><name>Чай</name></offer></offers>";
        string result = XmlToTextConverter.ConvertXmlToText(Encoding.UTF8.GetBytes(xml));
        StringAssert.Contains("sku: ABC-1;", result);
        StringAssert.Contains("price: 5000;", result);
        StringAssert.Contains("Чай", result);
    }

    [Test]
    public void ConvertBytes_CdataWrappedText_Emitted()
    {
        string xml = "<items><item><name><![CDATA[Чай «Премиум»]]></name></item></items>";
        string result = XmlToTextConverter.ConvertXmlToText(Encoding.UTF8.GetBytes(xml));
        StringAssert.Contains("Чай «Премиум»", result);
    }
}

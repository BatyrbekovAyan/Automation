using System.Linq;
using System.Text;
using NUnit.Framework;

public class TextEncodingSnifferTests
{
    [OneTimeSetUp]
    public void RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Test]
    public void Decode_Cp1251Cyrillic_DecodesCorrectly()
    {
        string original = "Привет, Цена: 5000 тг";
        byte[] bytes = Encoding.GetEncoding(1251).GetBytes(original);
        Assert.AreEqual(original, TextEncodingSniffer.Decode(bytes));
    }

    [Test]
    public void Decode_Utf8CyrillicWithoutBom_DecodesAsUtf8()
    {
        string original = "Прайс-лист — чай 5000₸";
        Assert.AreEqual(original, TextEncodingSniffer.Decode(Encoding.UTF8.GetBytes(original)));
    }

    [Test]
    public void Decode_Utf8Bom_StrippedFromOutput()
    {
        byte[] bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Чай")).ToArray();
        Assert.AreEqual("Чай", TextEncodingSniffer.Decode(bytes));
    }

    [Test]
    public void Decode_Utf16LeBom_DecodesCorrectly()
    {
        // Old Notepad's "Unicode" save format.
        byte[] bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Цена 5000")).ToArray();
        Assert.AreEqual("Цена 5000", TextEncodingSniffer.Decode(bytes));
    }

    [Test]
    public void Decode_Utf16BeBom_DecodesCorrectly()
    {
        byte[] bytes = Encoding.BigEndianUnicode.GetPreamble()
            .Concat(Encoding.BigEndianUnicode.GetBytes("Цена")).ToArray();
        Assert.AreEqual("Цена", TextEncodingSniffer.Decode(bytes));
    }

    [Test]
    public void Decode_PlainAscii_Unchanged()
    {
        Assert.AreEqual("Name,Price", TextEncodingSniffer.Decode(Encoding.ASCII.GetBytes("Name,Price")));
    }

    [Test]
    public void Decode_EmptyOrNull_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, TextEncodingSniffer.Decode(null));
        Assert.AreEqual(string.Empty, TextEncodingSniffer.Decode(new byte[0]));
    }

    [Test]
    public void IsValidUtf8_RejectsCp1251CyrillicSequences()
    {
        // "П " in cp1251: 0xCF is a UTF-8 lead byte demanding a continuation, 0x20 is not one.
        Assert.IsFalse(TextEncodingSniffer.IsValidUtf8(new byte[] { 0xCF, 0x20 }));
        Assert.IsFalse(TextEncodingSniffer.IsValidUtf8(Encoding.GetEncoding(1251).GetBytes("Прайс 2026")));
    }

    [Test]
    public void IsValidUtf8_AcceptsFourByteSequences()
    {
        Assert.IsTrue(TextEncodingSniffer.IsValidUtf8(Encoding.UTF8.GetBytes("Чай 🍵 5000")));
    }

    [Test]
    public void IsValidUtf8_RejectsTruncatedSequence()
    {
        byte[] full = Encoding.UTF8.GetBytes("Ч");
        Assert.IsFalse(TextEncodingSniffer.IsValidUtf8(new[] { full[0] })); // lead byte, no continuation
    }

    [Test]
    public void Decode_BomlessUtf16Le_DetectedViaNulBytes()
    {
        // Cyrillic UTF-16 bytes are all <= 0x7F, so the UTF-8 check alone
        // would "pass" it and produce control-char garbage.
        string original = "Товар;Цена\n5000";
        Assert.AreEqual(original, TextEncodingSniffer.Decode(Encoding.Unicode.GetBytes(original)));
    }

    [Test]
    public void Decode_BomlessUtf16Be_DetectedViaNulBytes()
    {
        string original = "Товар;Цена 5000";
        Assert.AreEqual(original, TextEncodingSniffer.Decode(Encoding.BigEndianUnicode.GetBytes(original)));
    }
}

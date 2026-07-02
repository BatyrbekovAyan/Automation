using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

public class RtfToTextConverterTests
{
    [OneTimeSetUp]
    public void RegisterCodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Test]
    public void Convert_PlainAsciiWithFormatting_StripsControlWords()
    {
        string rtf = @"{\rtf1\ansi{\fonttbl{\f0 Helvetica;}}\f0\fs24 Hello \b World\b0\par Second line\par}";
        string result = RtfToTextConverter.Convert(rtf);
        Assert.AreEqual("Hello World\nSecond line", result);
    }

    [Test]
    public void Convert_Cp1251HexEscapes_DecodesCyrillic()
    {
        // "Привет" as cp1251 \'hh escapes — the shape Word emits for Russian price lists.
        string rtf = @"{\rtf1\ansi\ansicpg1251{\fonttbl{\f0 Arial;}}\f0 \'cf\'f0\'e8\'e2\'e5\'f2\par}";
        string result = RtfToTextConverter.Convert(rtf);
        Assert.AreEqual("Привет", result);
    }

    [Test]
    public void Convert_UnicodeEscapes_SkipsFallbackCharacter()
    {
        // "Привет" as \uN escapes with \uc1 '?' fallbacks that must not leak into output.
        string rtf = "{\\rtf1\\ansi\\uc1 \\u1055?\\u1088?\\u1080?\\u1074?\\u1077?\\u1090?}";
        string result = RtfToTextConverter.Convert(rtf);
        Assert.AreEqual("Привет", result);
    }

    [Test]
    public void Convert_WordTable_CellsBecomeTabsRowsBecomeLines()
    {
        string rtf = @"{\rtf1\ansi\trowd\cellx1000\cellx2000 Item\cell 100\cell\row\trowd Item2\cell 200\cell\row}";
        string result = RtfToTextConverter.Convert(rtf);
        string[] lines = result.Split('\n');
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual("Item\t100", lines[0].TrimEnd());
        Assert.AreEqual("Item2\t200", lines[1].TrimEnd());
    }

    [Test]
    public void Convert_NonContentDestinations_AreSkipped()
    {
        string rtf = @"{\rtf1{\fonttbl{\f0 Courier;}}{\colortbl;\red255\green0\blue0;}{\*\generator Riched20 10.0;}{\pict\wmetafile8 0102abcd}Visible}";
        string result = RtfToTextConverter.Convert(rtf);
        Assert.AreEqual("Visible", result);
    }

    [Test]
    public void Convert_EscapedBracesAndBackslash_KeptLiteral()
    {
        string rtf = @"{\rtf1 a\{b\}c\\d}";
        string result = RtfToTextConverter.Convert(rtf);
        Assert.AreEqual(@"a{b}c\d", result);
    }

    [Test]
    public void Convert_SymbolControlWords_MapToCharacters()
    {
        string rtf = @"{\rtf1 5\~000 \endash 10}";
        string result = RtfToTextConverter.Convert(rtf);
        Assert.AreEqual("5 000 –10", result);
    }

    [Test]
    public void Convert_NonRtfInput_ReturnedAsIs()
    {
        Assert.AreEqual("just plain text", RtfToTextConverter.Convert("just plain text"));
    }

    [Test]
    public void Convert_EmptyOrNullBytes_ReturnsEmpty()
    {
        Assert.AreEqual(string.Empty, RtfToTextConverter.Convert((byte[])null));
        Assert.AreEqual(string.Empty, RtfToTextConverter.Convert(System.Array.Empty<byte>()));
    }

    [Test]
    public void Convert_Cp1251Bytes_RoundTripsThroughByteOverload()
    {
        string rtf = @"{\rtf1\ansi\ansicpg1251 \'d6\'e5\'ed\'e0: 5000\par}"; // "Цена: 5000"
        byte[] bytes = Encoding.ASCII.GetBytes(rtf);
        string result = RtfToTextConverter.Convert(bytes);
        Assert.AreEqual("Цена: 5000", result);
    }

    [Test]
    public void Convert_ExcessiveBlankLines_CollapsedToOne()
    {
        string rtf = @"{\rtf1 First\par\par\par\par Second}";
        string result = RtfToTextConverter.Convert(rtf);
        Assert.AreEqual("First\n\nSecond", result);
    }

    [Test]
    public void Convert_NonRtfBytesMisnamedRtf_DecodedViaSniffer()
    {
        // A cp1251 plain-text file renamed .rtf must not come out as Latin-1 mojibake.
        byte[] bytes = Encoding.GetEncoding(1251).GetBytes("Прайс: чай 5000");
        Assert.AreEqual("Прайс: чай 5000", RtfToTextConverter.Convert(bytes));
    }

    [Test]
    public void Convert_RawUnescapedAnsiBytes_DecodedWithDocumentCodepage()
    {
        // Some RTF writers emit raw 8-bit ANSI bytes instead of \'hh escapes.
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("{\\rtf1\\ansi\\ansicpg1251 "));
        bytes.AddRange(Encoding.GetEncoding(1251).GetBytes("Цена 5000"));
        bytes.Add((byte)'}');
        Assert.AreEqual("Цена 5000", RtfToTextConverter.Convert(bytes.ToArray()));
    }
}

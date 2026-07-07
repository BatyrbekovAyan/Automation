using NUnit.Framework;

public class CacheSizeFormatterTests
{
    private const long KB = 1024;
    private const long MB = KB * 1024;
    private const long GB = MB * 1024;

    [Test]
    public void Zero_ReadsZeroMegabytes() => Assert.AreEqual("0 МБ", CacheSizeFormatter.FormatBytes(0));

    [Test]
    public void Negative_ReadsZeroMegabytes() => Assert.AreEqual("0 МБ", CacheSizeFormatter.FormatBytes(-5));

    [Test]
    public void UnderOneKilobyte_NeverReadsZero()
    {
        // A non-empty cache must not display as empty.
        Assert.AreEqual("1 КБ", CacheSizeFormatter.FormatBytes(3));
    }

    [Test]
    public void Kilobytes_RoundToWhole() => Assert.AreEqual("512 КБ", CacheSizeFormatter.FormatBytes(512 * KB));

    [Test]
    public void Megabytes_RoundToWhole() => Assert.AreEqual("128 МБ", CacheSizeFormatter.FormatBytes(128 * MB));

    [Test]
    public void MegabytesRoundUp_AtHalfBoundary()
    {
        Assert.AreEqual("2 МБ", CacheSizeFormatter.FormatBytes(MB + MB / 2));
    }

    [Test]
    public void Gigabytes_OneDecimal_RussianComma()
    {
        Assert.AreEqual("1,3 ГБ", CacheSizeFormatter.FormatBytes((long)(1.3 * GB)));
    }

    [Test]
    public void WholeGigabytes_DropTrailingDecimal()
    {
        Assert.AreEqual("2 ГБ", CacheSizeFormatter.FormatBytes(2 * GB));
    }
}

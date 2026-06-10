using NUnit.Framework;

public class MediaUrlIdentityTests
{
    // Realistic Wappi/s3 tail: uuid filename with extension.
    private const string Uuid = "0c1f9a52-7e34-4b6e-9d2c-5a8f13e7b4a1.webp";

    [Test]
    public void SameFile_TrueAcrossHostsForSameUuidTail()
    {
        // The exact production shape: recovery file_link vs the later hosted s3 URL.
        string fileLink = $"https://wappi.pro/files/{Uuid}";
        string s3 = $"https://s3.eu-central-1.amazonaws.com/wappi-media/store/{Uuid}?X-Amz-Signature=abc123";

        Assert.IsTrue(MediaUrlIdentity.SameFile(fileLink, s3));
    }

    [Test]
    public void SameFile_FalseForDifferentUuids()
    {
        string a = $"https://wappi.pro/files/{Uuid}";
        string b = "https://wappi.pro/files/ffffffff-0000-1111-2222-333333333333.webp";

        Assert.IsFalse(MediaUrlIdentity.SameFile(a, b));
    }

    [Test]
    public void SameFile_FalseForShortGenericTails()
    {
        // Two unrelated endpoint URLs sharing a generic last segment must never alias.
        string a = "https://wappi.pro/api/sync/message/media/download";
        string b = "https://other.host/v2/media/download";

        Assert.IsFalse(MediaUrlIdentity.SameFile(a, b));
    }

    [Test]
    public void SameFile_FalseForNullOrEmpty()
    {
        string url = $"https://wappi.pro/files/{Uuid}";

        Assert.IsFalse(MediaUrlIdentity.SameFile(null, url));
        Assert.IsFalse(MediaUrlIdentity.SameFile(url, null));
        Assert.IsFalse(MediaUrlIdentity.SameFile("", url));
        Assert.IsFalse(MediaUrlIdentity.SameFile(null, null));
    }

    [Test]
    public void SameFile_FalseForNonHttpSchemes()
    {
        // base64:// and thumb:// carry no stable path identity.
        Assert.IsFalse(MediaUrlIdentity.SameFile("base64://AAAA", "base64://AAAA"));
        Assert.IsFalse(MediaUrlIdentity.SameFile($"thumb://{Uuid}", $"thumb://{Uuid}"));
        Assert.IsFalse(MediaUrlIdentity.SameFile($"thumb://{Uuid}", $"https://wappi.pro/files/{Uuid}"));
    }

    [Test]
    public void SameFile_FalseForHostOnlyUrls()
    {
        // A long hostname is not a file tail, even when it matches on both sides.
        string hostOnly = "https://very-long-hostname-here.example.com";

        Assert.IsFalse(MediaUrlIdentity.SameFile(hostOnly, hostOnly));
    }

    [Test]
    public void SameFile_TrueWhenOnlyQueryDiffers()
    {
        // Rotated s3 signatures over the same stored file.
        string a = $"https://s3.host/store/{Uuid}?X-Amz-Signature=old";
        string b = $"https://s3.host/store/{Uuid}?X-Amz-Signature=new";

        Assert.IsTrue(MediaUrlIdentity.SameFile(a, b));
    }

    [Test]
    public void SameFile_IgnoresTrailingSlash()
    {
        string a = $"https://wappi.pro/files/{Uuid}/";
        string b = $"https://s3.host/store/{Uuid}";

        Assert.IsTrue(MediaUrlIdentity.SameFile(a, b));
    }
}

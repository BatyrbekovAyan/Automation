using NUnit.Framework;

public class N8nBaseUrlTests
{
    private const string CloudDefault = "https://bagkz.app.n8n.cloud";

    [Test]
    public void EmptyConfig_FallsBackToCloud()
    {
        Assert.AreEqual(CloudDefault, Manager.ResolveN8nBaseUrl(""));
    }

    [Test]
    public void NullConfig_FallsBackToCloud()
    {
        Assert.AreEqual(CloudDefault, Manager.ResolveN8nBaseUrl(null));
    }

    [Test]
    public void WhitespaceConfig_FallsBackToCloud()
    {
        Assert.AreEqual(CloudDefault, Manager.ResolveN8nBaseUrl("   "));
    }

    [Test]
    public void ConfiguredValue_IsUsedVerbatim()
    {
        Assert.AreEqual("https://abc.trycloudflare.com",
            Manager.ResolveN8nBaseUrl("https://abc.trycloudflare.com"));
    }

    [Test]
    public void TrailingSlash_IsTrimmed()
    {
        Assert.AreEqual("https://abc.trycloudflare.com",
            Manager.ResolveN8nBaseUrl("https://abc.trycloudflare.com/"));
    }
}

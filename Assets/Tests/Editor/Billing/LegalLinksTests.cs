using NUnit.Framework;

/// <summary>
/// Pins the paywall legal-links seam (store submission pack). The URLs are empty until
/// the owner's domain lands; the shape tests keep whatever value arrives honest.
/// </summary>
public class LegalLinksTests
{
    [Test]
    public void Labels_are_pinned_ru()
    {
        Assert.AreEqual("Условия использования", LegalLinks.TermsLabel);
        Assert.AreEqual("Политика конфиденциальности", LegalLinks.PrivacyLabel);
        Assert.AreEqual("·", LegalLinks.Separator);
    }

    [Test]
    public void Urls_are_empty_or_https_without_spaces()
    {
        foreach (var url in new[] { LegalLinks.TermsUrl, LegalLinks.PrivacyUrl })
        {
            if (string.IsNullOrEmpty(url)) continue;
            StringAssert.StartsWith("https://", url);
            Assert.IsFalse(url.Contains(" "), $"URL carries a space: '{url}'");
        }
    }

    [Test]
    public void Urls_are_filled_together_and_HasUrls_reflects_it()
    {
        // A half-filled pair would render one dead link — exactly what HasUrls exists
        // to prevent, so the pair is required to change in one edit.
        Assert.AreEqual(string.IsNullOrEmpty(LegalLinks.TermsUrl),
                        string.IsNullOrEmpty(LegalLinks.PrivacyUrl),
                        "TermsUrl and PrivacyUrl must be filled together");
        Assert.AreEqual(
            !string.IsNullOrEmpty(LegalLinks.TermsUrl) && !string.IsNullOrEmpty(LegalLinks.PrivacyUrl),
            LegalLinks.HasUrls);
    }
}

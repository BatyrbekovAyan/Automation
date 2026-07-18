using NUnit.Framework;

// Covers SuccessCtaSelector — the pure success-panel primary-CTA target selector.
// Files already uploaded (settings re-auth case) ⇒ «Открыть чаты»; otherwise the
// primary CTA drives the owner to «Загрузить прайс-лист».
public class SuccessCtaSelectorTests
{
    [Test]
    public void Choose_NoFiles_UploadPriceList()
        => Assert.AreEqual(SuccessCta.UploadPriceList, SuccessCtaSelector.Choose(hasUploadedFiles: false),
            "No price-list files yet ⇒ primary CTA is «Загрузить прайс-лист».");

    [Test]
    public void Choose_HasFiles_OpenChats()
        => Assert.AreEqual(SuccessCta.OpenChats, SuccessCtaSelector.Choose(hasUploadedFiles: true),
            "Files already exist (settings re-auth) ⇒ primary CTA is «Открыть чаты».");
}

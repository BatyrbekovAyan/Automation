/// <summary>
/// The paywall's legal links (store submission pack, spec 2026-08-27). Apple Guideline
/// 3.1.2 requires functional Privacy Policy + Terms of Use links next to the purchase
/// point; these constants are the ONE place the hosted URLs live.
///
/// Both URLs stay empty until the owner's domain is known — PaywallController hides the
/// whole LegalRow while <see cref="HasUrls"/> is false, so the app never renders a dead
/// link. Filling them (together, then re-running Tools/Billing/Add Paywall Legal Row so
/// the scene seed matches) is a submission blocker tracked in
/// docs/store/submission-checklist.md.
/// </summary>
public static class LegalLinks
{
    public const string TermsUrl = "";
    public const string PrivacyUrl = "";

    public const string TermsLabel = "Условия использования";
    public const string PrivacyLabel = "Политика конфиденциальности";
    public const string Separator = "·";

    public static bool HasUrls =>
        !string.IsNullOrEmpty(TermsUrl) && !string.IsNullOrEmpty(PrivacyUrl);
}

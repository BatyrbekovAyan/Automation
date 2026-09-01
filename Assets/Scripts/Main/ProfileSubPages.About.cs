using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Profile → О приложении + the Лицензии child page. Version comes live from
// Application.version; the product name is a single constant (final store name
// «Choose Reply», owner's call 2026-08-27 — matches ProjectSettings.productName).
public partial class ProfileSubPages
{
    public const string ProductName = "Choose Reply";

    [Header("About page")]
    [SerializeField] private TextMeshProUGUI aboutVersionLabel;
    [SerializeField] private Button licensesButton;
    [SerializeField] private Button privacyPolicyButton;
    [SerializeField] private Button termsOfUseButton;
    [SerializeField] private TextMeshProUGUI licensesText;

    private const string LicensesBody =
        "Это приложение использует открытые компоненты:\n\n" +
        "• DOTween — Demigiant, DOTween License\n" +
        "• NativeFilePicker — Süleyman Yasir Kula, MIT\n" +
        "• NativeGallery — Süleyman Yasir Kula, MIT\n" +
        "• NativeCamera — Süleyman Yasir Kula, MIT\n" +
        "• NativeShare — Süleyman Yasir Kula, MIT\n" +
        "• Unity UI Rounded Corners — Nobi, MIT\n" +
        "• unity.webp — netpyoung, MIT\n" +
        "• Json.NET (Newtonsoft.Json) — MIT\n" +
        "• NuGetForUnity — GlitchEnzo, MIT\n" +
        "• Liberation Sans — Red Hat, SIL OFL 1.1\n" +
        "• Twemoji graphics — Twitter/X, CC-BY 4.0 (creativecommons.org/licenses/by/4.0)\n\n" +
        "Полные тексты лицензий доступны на страницах проектов.";

    private void WireAbout()
    {
        if (licensesButton != null)
            licensesButton.onClick.AddListener(() => Open(Page.Licenses));

        // Apple 5.1.1(i) / Play User Data: the privacy policy must be easily reachable
        // IN-APP — a link living only inside the paywall was a weak answer to both.
        // Same LegalLinks URLs the paywall row opens.
        if (privacyPolicyButton != null)
            privacyPolicyButton.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(LegalLinks.PrivacyUrl)) Application.OpenURL(LegalLinks.PrivacyUrl);
            });
        if (termsOfUseButton != null)
            termsOfUseButton.onClick.AddListener(() =>
            {
                if (!string.IsNullOrEmpty(LegalLinks.TermsUrl)) Application.OpenURL(LegalLinks.TermsUrl);
            });
    }

    private void RefreshAbout()
    {
        if (aboutVersionLabel != null)
            aboutVersionLabel.text = $"Версия {Application.version}";
    }

    private void RefreshLicenses()
    {
        if (licensesText != null) licensesText.text = LicensesBody;
    }
}

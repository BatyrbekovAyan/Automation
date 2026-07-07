using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Profile → О приложении + the Лицензии child page. Version comes live from
// Application.version; the product name is a single constant (working title
// per Q1 — rename here before release if the product gets a real name).
public partial class ProfileSubPages
{
    public const string ProductName = "Automation";

    [Header("About page")]
    [SerializeField] private TextMeshProUGUI aboutVersionLabel;
    [SerializeField] private Button licensesButton;
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
        "• Twemoji graphics — Twitter/X, CC-BY 4.0\n\n" +
        "Полные тексты лицензий доступны на страницах проектов.";

    private void WireAbout()
    {
        if (licensesButton != null)
            licensesButton.onClick.AddListener(() => Open(Page.Licenses));
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

using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the Profile screen (tab index 4).
/// Persists user name and email in PlayerPrefs.
/// Follows the singleton pattern used throughout this project.
/// </summary>
public class ProfilePage : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────
    public static ProfilePage Instance;

    // ── PlayerPrefs keys ───────────────────────────────────────────────────
    private const string KeyName  = "ProfileName";
    private const string KeyEmail = "ProfileEmail";

    private const string DefaultName  = "Иван Петров";
    private const string DefaultEmail = "ivan.petrov@email.com";

    // ── Profile card ───────────────────────────────────────────────────────
    [Header("Profile Card")]
    [SerializeField] private TextMeshProUGUI avatarInitialText;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI emailText;
    [SerializeField] private Button          editButton;

    // ── Settings rows ──────────────────────────────────────────────────────
    [Header("Settings Rows")]
    [SerializeField] private Button accountButton;
    [SerializeField] private Button notificationsButton;
    [SerializeField] private Button privacyButton;
    [SerializeField] private Button supportButton;
    [SerializeField] private Button aboutButton;

    // ── Logout ─────────────────────────────────────────────────────────────
    [Header("Logout")]
    [SerializeField] private Button logoutButton;

    // ── Edit popup ─────────────────────────────────────────────────────────
    [Header("Edit Popup")]
    [SerializeField] private GameObject      editPopup;
    [SerializeField] private TMP_InputField  editNameInput;
    [SerializeField] private TMP_InputField  editEmailInput;
    [SerializeField] private Button          editSaveButton;
    [SerializeField] private Button          editCancelButton;

    // ── Logout confirm popup ───────────────────────────────────────────────
    [Header("Logout Confirm Popup")]
    [SerializeField] private GameObject logoutPopup;
    [SerializeField] private Button     logoutConfirmButton;
    [SerializeField] private Button     logoutCancelButton;

    // ── Navigation ─────────────────────────────────────────────────────────
    [Header("Navigation")]
    [SerializeField] private BottomTabManager bottomTabManager;

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Seed defaults on first launch
        if (!PlayerPrefs.HasKey(KeyName))  PlayerPrefs.SetString(KeyName,  DefaultName);
        if (!PlayerPrefs.HasKey(KeyEmail)) PlayerPrefs.SetString(KeyEmail, DefaultEmail);

        WireButtons();
        RefreshProfileCard();
    }

    private void OnEnable()
    {
        // Refresh whenever the tab becomes visible
        RefreshProfileCard();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Re-reads PlayerPrefs and updates the profile card UI.</summary>
    public void RefreshProfileCard()
    {
        if (nameText == null) return; // called before Start on first frame

        string savedName  = PlayerPrefs.GetString(KeyName,  DefaultName);
        string savedEmail = PlayerPrefs.GetString(KeyEmail, DefaultEmail);

        nameText.text  = savedName;
        emailText.text = savedEmail;

        // Avatar shows first letter of the name
        avatarInitialText.text = savedName.Length > 0
            ? savedName[0].ToString().ToUpper()
            : "?";
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private void WireButtons()
    {
        if (editButton       != null) editButton.onClick.AddListener(OpenEditPopup);
        if (editSaveButton   != null) editSaveButton.onClick.AddListener(SaveProfile);
        if (editCancelButton != null) editCancelButton.onClick.AddListener(CloseEditPopup);

        if (logoutButton        != null) logoutButton.onClick.AddListener(OpenLogoutConfirm);
        if (logoutConfirmButton != null) logoutConfirmButton.onClick.AddListener(ConfirmLogout);
        if (logoutCancelButton  != null) logoutCancelButton.onClick.AddListener(CloseLogoutConfirm);

        // Settings stubs — replace with real navigation when screens exist
        if (accountButton       != null) accountButton.onClick.AddListener(OnAccount);
        if (notificationsButton != null) notificationsButton.onClick.AddListener(OnNotifications);
        if (privacyButton       != null) privacyButton.onClick.AddListener(OnPrivacy);
        if (supportButton       != null) supportButton.onClick.AddListener(OnSupport);
        if (aboutButton         != null) aboutButton.onClick.AddListener(OnAbout);
    }

    // ── Edit popup ─────────────────────────────────────────────────────────

    private void OpenEditPopup()
    {
        if (editPopup == null) return;

        editNameInput.text  = PlayerPrefs.GetString(KeyName,  DefaultName);
        editEmailInput.text = PlayerPrefs.GetString(KeyEmail, DefaultEmail);

        editPopup.SetActive(true);
        AnimatePopupIn(editPopup.transform);
    }

    private void CloseEditPopup()
    {
        if (editPopup == null) return;
        AnimatePopupOut(editPopup.transform, () => editPopup.SetActive(false));
    }

    private void SaveProfile()
    {
        string newName  = editNameInput.text.Trim();
        string newEmail = editEmailInput.text.Trim();

        if (!string.IsNullOrEmpty(newName))  PlayerPrefs.SetString(KeyName,  newName);
        if (!string.IsNullOrEmpty(newEmail)) PlayerPrefs.SetString(KeyEmail, newEmail);
        PlayerPrefs.Save();

        RefreshProfileCard();
        CloseEditPopup();
    }

    // ── Logout popup ───────────────────────────────────────────────────────

    private void OpenLogoutConfirm()
    {
        if (logoutPopup == null) return;
        logoutPopup.SetActive(true);
        AnimatePopupIn(logoutPopup.transform);
    }

    private void CloseLogoutConfirm()
    {
        if (logoutPopup == null) return;
        AnimatePopupOut(logoutPopup.transform, () => logoutPopup.SetActive(false));
    }

    private void ConfirmLogout()
    {
        // Clear all bot PlayerPrefs entries
        int botCount = PlayerPrefs.GetInt("ids", 0);
        for (int i = 0; i < botCount; i++)
        {
            string prefix = "Bot" + i;
            string[] suffixes =
            {
                "Name", "isOn", "Status", "Active", "isOnWhatsapp", "isOnTelegram",
                "BusinessType", "WhatsappNumber", "TelegramNumber", "Business", "Prompt",
                "WhatsappWorkflowId", "WhatsappProfileId", "TelegramWorkflowId", "TelegramProfileId",
                "ProductsNumber", "ServicesNumber"
            };
            foreach (string s in suffixes) PlayerPrefs.DeleteKey(prefix + s);
        }
        PlayerPrefs.DeleteKey("ids");
        PlayerPrefs.Save();

        CloseLogoutConfirm();

        // Navigate to the first tab (WhatsApp)
        if (bottomTabManager != null)
            bottomTabManager.SwitchTab(0);
    }

    // ── Settings stubs ─────────────────────────────────────────────────────

    private void OnAccount()       => Debug.Log("[ProfilePage] Account tapped — stub");
    private void OnNotifications() => Debug.Log("[ProfilePage] Notifications tapped — stub");
    private void OnPrivacy()       => Debug.Log("[ProfilePage] Privacy tapped — stub");
    private void OnSupport()       => Debug.Log("[ProfilePage] Support tapped — stub");
    private void OnAbout()         => Debug.Log("[ProfilePage] About tapped — stub");

    // ── DOTween helpers ────────────────────────────────────────────────────

    private static void AnimatePopupIn(Transform t)
    {
        t.localScale = Vector3.one * 0.88f;
        t.DOScale(Vector3.one, 0.22f).SetEase(Ease.OutBack);
    }

    private static void AnimatePopupOut(Transform t, TweenCallback onComplete)
    {
        t.DOScale(Vector3.one * 0.88f, 0.16f)
         .SetEase(Ease.InBack)
         .OnComplete(onComplete);
    }

    private void OnDestroy()
    {
        DOTween.Kill(transform);
    }
}

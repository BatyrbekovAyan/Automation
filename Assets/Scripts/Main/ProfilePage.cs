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
    [SerializeField] private TMP_InputField  editN8nUrlInput;   // dev-only n8n URL override
    [SerializeField] private TMP_InputField  editN8nApiKeyInput; // dev-only n8n API key override
    [SerializeField] private RectTransform   editPopupCard;     // grown by one row when the dev field shows
    [SerializeField] private RectTransform   editButtonRow;     // nudged down when the dev field shows

    // Scene defaults of the edit-popup card/buttons, captured once so the dev-only
    // layout shift can be applied relative to them (prod layout stays untouched).
    private float _baseEditCardHeight;
    private float _baseEditButtonY;
    private bool  _editLayoutCaptured;
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
        // Row buttons that OPEN popups stay on Button.onClick — they're not
        // dismiss/confirm actions, just navigation triggers.
        if (editButton   != null) editButton.onClick.AddListener(OpenEditPopup);
        if (logoutButton != null) logoutButton.onClick.AddListener(OpenLogoutConfirm);

        // Popup dismiss/confirm actions: route via PopupUI so they fire on
        // true finger release (filters spurious iOS PointerUp on keyboard
        // dismiss) and never on finger-down.
        if (editSaveButton      != null) PopupUI.WireFingerUp(editSaveButton,      SaveProfile);
        if (editCancelButton    != null) PopupUI.WireFingerUp(editCancelButton,    CloseEditPopup);
        if (logoutConfirmButton != null) PopupUI.WireFingerUp(logoutConfirmButton, ConfirmLogout);
        if (logoutCancelButton  != null) PopupUI.WireFingerUp(logoutCancelButton,  CloseLogoutConfirm);

        // Overlay backdrop tap → close. EventAbsorber on the card prevents
        // taps on card background (between/around inputs/buttons) from
        // bubbling up to the overlay's dismiss handler.
        WireOverlayDismiss(editPopup,   CloseEditPopup);
        WireOverlayDismiss(logoutPopup, CloseLogoutConfirm);

        // Settings stubs — replace with real navigation when screens exist
        if (accountButton       != null) accountButton.onClick.AddListener(OnAccount);
        if (notificationsButton != null) notificationsButton.onClick.AddListener(OnNotifications);
        if (privacyButton       != null) privacyButton.onClick.AddListener(OnPrivacy);
        if (supportButton       != null) supportButton.onClick.AddListener(OnSupport);
        if (aboutButton         != null) aboutButton.onClick.AddListener(OnAbout);
    }

    private static void WireOverlayDismiss(GameObject popup, System.Action onCancel)
    {
        if (popup == null) return;

        // Backdrop tap → dismiss.
        PopupUI.WireFingerUp(popup, onCancel);

        var card = popup.transform.Find("Card");
        if (card == null) return;

        // Card-bg taps must NOT bubble to the overlay dismiss handler.
        PopupUI.AbsorbEvents(card);

        // ✕ close button (if present in the card) → dismiss on finger-up.
        // Tries Card/CloseButton first, then any descendant named "CloseButton".
        var closeBtn = card.Find("CloseButton")?.GetComponent<Button>();
        if (closeBtn == null)
        {
            foreach (var t in card.GetComponentsInChildren<Transform>(includeInactive: true))
            {
                if (t.name != "CloseButton") continue;
                closeBtn = t.GetComponent<Button>();
                if (closeBtn != null) break;
            }
        }
        if (closeBtn != null) PopupUI.WireFingerUp(closeBtn, onCancel);
    }

    // ── Edit popup ─────────────────────────────────────────────────────────

    private void OpenEditPopup()
    {
        if (editPopup == null) return;

        editNameInput.text  = PlayerPrefs.GetString(KeyName,  DefaultName);
        editEmailInput.text = PlayerPrefs.GetString(KeyEmail, DefaultEmail);

        if (editN8nUrlInput != null || editN8nApiKeyInput != null)
        {
            if (!_editLayoutCaptured)
            {
                if (editPopupCard != null) _baseEditCardHeight = editPopupCard.sizeDelta.y;
                if (editButtonRow != null) _baseEditButtonY    = editButtonRow.anchoredPosition.y;
                _editLayoutCaptured = true;
            }

            bool dev = Debug.isDebugBuild;
            const float rowHeight = 150f;
            int devRows = 0;

            if (editN8nUrlInput != null)
            {
                editN8nUrlInput.gameObject.SetActive(dev);
                editN8nUrlInput.text = PlayerPrefs.GetString(Manager.DevN8nBaseUrlKey, "");
                if (dev) devRows++;
            }
            if (editN8nApiKeyInput != null)
            {
                editN8nApiKeyInput.gameObject.SetActive(dev);
                editN8nApiKeyInput.text = PlayerPrefs.GetString(Manager.DevN8nApiKeyKey, "");
                if (dev) devRows++;
            }

            float shift = rowHeight * devRows;
            if (editPopupCard != null)
            {
                var size = editPopupCard.sizeDelta;
                size.y = _baseEditCardHeight + shift;
                editPopupCard.sizeDelta = size;
            }
            if (editButtonRow != null)
            {
                var pos = editButtonRow.anchoredPosition;
                pos.y = _baseEditButtonY - shift;
                editButtonRow.anchoredPosition = pos;
            }
        }

        // Activate name field after the open tween completes — calling
        // ActivateInputField during the scale tween would let the keyboard
        // slide-up steal main-thread time and cause stutter.
        PopupUI.Show(editPopup, onCardSettled: () =>
        {
            if (editNameInput != null) editNameInput.ActivateInputField();
        });
    }

    private void CloseEditPopup() => PopupUI.Hide(editPopup);

    private void SaveProfile()
    {
        string newName  = editNameInput.text.Trim();
        string newEmail = editEmailInput.text.Trim();

        if (!string.IsNullOrEmpty(newName))  PlayerPrefs.SetString(KeyName,  newName);
        if (!string.IsNullOrEmpty(newEmail)) PlayerPrefs.SetString(KeyEmail, newEmail);
        if (editN8nUrlInput != null) PlayerPrefs.SetString(Manager.DevN8nBaseUrlKey, editN8nUrlInput.text.Trim());
        if (editN8nApiKeyInput != null) PlayerPrefs.SetString(Manager.DevN8nApiKeyKey, editN8nApiKeyInput.text.Trim());
        PlayerPrefs.Save();

        RefreshProfileCard();
        CloseEditPopup();
    }

    // ── Logout popup ───────────────────────────────────────────────────────

    private void OpenLogoutConfirm() => PopupUI.Show(logoutPopup);

    private void CloseLogoutConfirm() => PopupUI.Hide(logoutPopup);

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
}

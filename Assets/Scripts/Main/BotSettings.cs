using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Automation.BotSettingsUI;

public partial class BotSettings : MonoBehaviour
{
    #region Serialized — Tabs
    [SerializeField] private GameObject General;
    [SerializeField] private GameObject Business;
    [SerializeField] private GameObject Product;
    [SerializeField] private GameObject Service;
    [SerializeField] private GameObject Prompt;
    [SerializeField] private Button GeneralTabButton;
    [SerializeField] private Button BusinessTabButton;
    [SerializeField] private Button ProductTabButton;
    [SerializeField] private Button ServiceTabButton;
    [SerializeField] private Button PromptTabButton;
    [SerializeField] private RectTransform headerGroup;
    [SerializeField] private RectTransform tabBarGroup;
    [SerializeField] private FocusScrim mainScrim;
    #endregion

    [System.Serializable]
    public struct TabVisual
    {
        public TextMeshProUGUI label;
        public GameObject underline;
    }

    #region Serialized — Tab visuals
    [SerializeField] private TabVisual[] tabVisuals;
    [SerializeField] private Color tabActiveColor = new Color(0.106f, 0.486f, 0.922f); // #1B7CEB
    [SerializeField] private Color tabInactiveColor = new Color(0.557f, 0.557f, 0.576f); // #8E8E93
    #endregion

    #region Serialized — General tab
    [SerializeField] public EditableField BotNameField;
    public TMP_Dropdown BusinessTypeDropdown;
    [SerializeField] public ToggleRow whatsappRow;
    [SerializeField] public ToggleRow telegramRow;
    public Toggle WhatsappToggle => whatsappRow != null ? whatsappRow.Toggle : null;
    public Toggle TelegramToggle => telegramRow != null ? telegramRow.Toggle : null;
    [SerializeField] public EditableField WhatsappNumberField;
    [SerializeField] public EditableField TelegramNumberField;
    #endregion

    #region Serialized — Business / Prompt
    [SerializeField] public EditableTextArea BusinessField;
    [SerializeField] public EditableTextArea PromptField;
    #endregion

    #region Serialized — Products / Services
    [SerializeField] public GameObject ProductPrefab;
    [SerializeField] public GameObject ServicePrefab;
    [SerializeField] public RectTransform ProductsParent;
    [SerializeField] public RectTransform ServicesParent;
    [SerializeField] private AddItemButton addProductButton;
    [SerializeField] private AddItemButton addServiceButton;
    [SerializeField] private ItemEditSheet productEditSheet;
    [SerializeField] private ItemEditSheet serviceEditSheet;
    public AddItemButton AddProductButton => addProductButton;
    public AddItemButton AddServiceButton => addServiceButton;
    #endregion

    #region Serialized — Auth (names preserved for Manager + auth partial)
    [SerializeField] public GameObject WhatsappAuthorization;
    [SerializeField] public GameObject WhatsappQRPanel;
    [SerializeField] public GameObject WhatsappCodePanel;
    [SerializeField] public GameObject TelegramAuthorization;
    [SerializeField] public GameObject TelegramQRPanel;
    [SerializeField] public GameObject TelegramCodePanel;
    [SerializeField] private TextMeshProUGUI TelegramPhoneTitle;
    [SerializeField] private TextMeshProUGUI TelegramPhoneBody;
    [SerializeField] public GameObject Saved;
    [SerializeField] private GameObject ConfirmChangeWhatsappNumberPopup;
    [SerializeField] private GameObject ConfirmChangeTelegramNumberPopup;
    [SerializeField] private GameObject WhatsappCodeTimer;
    [SerializeField] private GameObject TelegramCodeTimer;
    [SerializeField] public Button WhatsappAuthorizationBackButton;
    [SerializeField] private Button WhatsappAuthorizationDoneBotton;
    [SerializeField] private Button OpenWhatsappQRPanelButton;
    [SerializeField] private Button OpenWhatsappCodePanelButton;
    [SerializeField] private Button CloseWhatsappQRPanelButton;
    [SerializeField] private Button CloseWhatsappCodePanelButton;
    [SerializeField] private Button GetWhatsappCodeButton;
    [SerializeField] public Button TelegramAuthorizationBackButton;
    [SerializeField] private Button TelegramAuthorizationDoneBotton;
    [SerializeField] private Button OpenTelegramQRPanelButton;
    [SerializeField] private Button OpenTelegramCodePanelButton;
    [SerializeField] private Button CloseTelegramQRPanelButton;
    [SerializeField] private Button CloseTelegramCodePanelButton;
    [SerializeField] private Button GetTelegramCodeButton;
    [SerializeField] private Button SendTelegramCodeButton;
    [SerializeField] private Button ConfirmChangeWhatsappNumberButton;
    [SerializeField] private Button CancelChangeWhatsappNumberButton;
    [SerializeField] private Button ConfirmChangeTelegramNumberButton;
    [SerializeField] private Button CancelChangeTelegramNumberButton;
    [SerializeField] private Button UploadPriceListButton;
    public TMP_InputField WhatsappNumberInput;
    public TMP_InputField TelegramNumberInput;
    public TMP_InputField TelegramCodeInput;
    [SerializeField] private RawImage WhatsappQRCodeImage;
    [SerializeField] private RawImage TelegramQRCodeImage;
    private string telegramPhoneTitleInitial;
    private string telegramPhoneBodyInitial;
    #endregion

    // Needed for PickMediaFile / UploadFile logic that references file extension fields
    private string pdf;
    private string txt;
    private string rtf;
    private string xml;
    private string csv;
    private string xls;
    private string xlsx;
    private string docx;

    public static BotSettings Instance;

    void Start()
    {
        if (TelegramPhoneTitle != null) telegramPhoneTitleInitial = TelegramPhoneTitle.text;
        if (TelegramPhoneBody != null) telegramPhoneBodyInitial = TelegramPhoneBody.text;

        WireTabs();
        WireFields();
        WireProductsAndServices();
        WireAuthButtons();
    }

    public void OnEnable()
    {
        StartCoroutine(CheckWhatsappUnauthorizationOutsideApp());
        StartCoroutine(CheckTelegramUnauthorizationOutsideApp());
    }

    public void OnDisable() => OpenGeneralTab();

    //////////////////////////////////////// TABS ////////////////////////////////////////

    public void OpenGeneralTab()  => SetActiveTab(general: true);
    public void OpenBusinessTab() => SetActiveTab(business: true);
    public void OpenProductTab()  => SetActiveTab(product: true);
    public void OpenServiceTab()  => SetActiveTab(service: true);
    public void OpenPromptTab()   => SetActiveTab(prompt: true);

    private void SetActiveTab(bool general = false, bool business = false,
                              bool product = false, bool service = false,
                              bool prompt = false)
    {
        General.SetActive(general);
        Business.SetActive(business);
        Product.SetActive(product);
        Service.SetActive(service);
        Prompt.SetActive(prompt);

        if (tabVisuals == null || tabVisuals.Length < 5) return;
        var active = new[] { general, business, product, service, prompt };
        for (int i = 0; i < tabVisuals.Length; i++)
        {
            if (tabVisuals[i].underline != null)
                tabVisuals[i].underline.SetActive(active[i]);
            if (tabVisuals[i].label != null)
                tabVisuals[i].label.color = active[i] ? tabActiveColor : tabInactiveColor;
        }
    }

    private void WireTabs()
    {
        if (GeneralTabButton != null)  GeneralTabButton.onClick.AddListener(OpenGeneralTab);
        if (BusinessTabButton != null) BusinessTabButton.onClick.AddListener(OpenBusinessTab);
        if (ProductTabButton != null)  ProductTabButton.onClick.AddListener(OpenProductTab);
        if (ServiceTabButton != null)  ServiceTabButton.onClick.AddListener(OpenServiceTab);
        if (PromptTabButton != null)   PromptTabButton.onClick.AddListener(OpenPromptTab);
    }

    //////////////////////////////////////// FIELDS ////////////////////////////////////////

    private void WireFields()
    {
        if (BotNameField != null)
            BotNameField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        if (WhatsappNumberField != null)
            WhatsappNumberField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        if (TelegramNumberField != null)
            TelegramNumberField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());

        if (BusinessField != null)
        {
            BusinessField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
            BusinessField.OnFullScreenFocusRequested.AddListener(HideHeaderAndTabs);
            BusinessField.OnFullScreenFocusReleased.AddListener(RestoreHeaderAndTabs);
        }
        if (PromptField != null)
        {
            PromptField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
            PromptField.OnFullScreenFocusRequested.AddListener(HideHeaderAndTabs);
            PromptField.OnFullScreenFocusReleased.AddListener(RestoreHeaderAndTabs);
        }

        if (BusinessTypeDropdown != null)
            BusinessTypeDropdown.onValueChanged.AddListener(_ => Manager.Instance.EnableSave());

        if (WhatsappToggle != null)
            WhatsappToggle.onValueChanged.AddListener(WhatsappChannelToggleChanged);
        if (TelegramToggle != null)
            TelegramToggle.onValueChanged.AddListener(TelegramChannelToggleChanged);
    }

    private void HideHeaderAndTabs()
    {
        if (headerGroup != null) headerGroup.gameObject.SetActive(false);
        if (tabBarGroup != null) tabBarGroup.gameObject.SetActive(false);
    }

    private void RestoreHeaderAndTabs()
    {
        if (headerGroup != null) headerGroup.gameObject.SetActive(true);
        if (tabBarGroup != null) tabBarGroup.gameObject.SetActive(true);
    }

    //////////////////////////////////////// PRODUCTS / SERVICES ////////////////////////////////////////

    private void WireProductsAndServices()
    {
        if (addProductButton != null) addProductButton.OnTap.AddListener(AddProduct);
        if (addServiceButton != null) addServiceButton.OnTap.AddListener(AddService);

        if (productEditSheet != null)
        {
            productEditSheet.OnAnyCommitted += () => Manager.Instance.EnableSave();
            productEditSheet.OnProductDeleted += DeleteProductCard;
        }
        if (serviceEditSheet != null)
        {
            serviceEditSheet.OnAnyCommitted += () => Manager.Instance.EnableSave();
            serviceEditSheet.OnServiceDeleted += DeleteServiceCard;
        }

        WireExistingProductCards();
        WireExistingServiceCards();
    }

    private void WireExistingProductCards()
    {
        for (int i = 0; i < ProductsParent.childCount; i++)
        {
            var card = ProductsParent.GetChild(i).GetComponent<ProductCardView>();
            if (card != null) BindProductCard(card);
        }
    }

    private void WireExistingServiceCards()
    {
        for (int i = 0; i < ServicesParent.childCount; i++)
        {
            var card = ServicesParent.GetChild(i).GetComponent<ServiceCardView>();
            if (card != null) BindServiceCard(card);
        }
    }

    private void BindProductCard(ProductCardView card)
    {
        card.OnEditRequested += c => productEditSheet.Show(c);
    }

    private void BindServiceCard(ServiceCardView card)
    {
        card.OnEditRequested += c => serviceEditSheet.Show(c);
    }

    public void AddProduct()
    {
        var go = Instantiate(ProductPrefab,
                             ProductPrefab.transform.position,
                             ProductPrefab.transform.rotation,
                             ProductsParent);
        if (addProductButton != null)
            addProductButton.transform.parent.SetAsLastSibling();

        var card = go.GetComponent<ProductCardView>();
        if (card != null) BindProductCard(card);

        var anim = go.GetComponent<Animation>();
        if (anim != null) anim.Play();

        Manager.Instance.EnableSave();
    }

    public void AddService()
    {
        var go = Instantiate(ServicePrefab,
                             ServicePrefab.transform.position,
                             ServicePrefab.transform.rotation,
                             ServicesParent);
        if (addServiceButton != null)
            addServiceButton.transform.parent.SetAsLastSibling();

        var card = go.GetComponent<ServiceCardView>();
        if (card != null) BindServiceCard(card);

        var anim = go.GetComponent<Animation>();
        if (anim != null) anim.Play();

        Manager.Instance.EnableSave();
    }

    private void DeleteProductCard(ProductCardView card)
    {
        Destroy(card.gameObject);
        Manager.Instance.EnableSave();
    }

    private void DeleteServiceCard(ServiceCardView card)
    {
        Destroy(card.gameObject);
        Manager.Instance.EnableSave();
    }

    //////////////////////////////////////// AUTH WIRING ////////////////////////////////////////

    private void WireAuthButtons()
    {
        if (WhatsappAuthorizationBackButton != null)
            WhatsappAuthorizationBackButton.onClick.AddListener(WhatsappAuthorizationBack);
        if (WhatsappAuthorizationDoneBotton != null)
            WhatsappAuthorizationDoneBotton.onClick.AddListener(WhatsappAuthorizationDone);
        if (OpenWhatsappQRPanelButton != null)
            OpenWhatsappQRPanelButton.onClick.AddListener(() => StartCoroutine(OpenWhatsappQRPanel()));
        if (OpenWhatsappCodePanelButton != null)
            OpenWhatsappCodePanelButton.onClick.AddListener(OpenWhatsappCodePanel);
        if (CloseWhatsappQRPanelButton != null)
            CloseWhatsappQRPanelButton.onClick.AddListener(CloseWhatsappQRPanel);
        if (CloseWhatsappCodePanelButton != null)
            CloseWhatsappCodePanelButton.onClick.AddListener(CloseWhatsappCodePanel);
        if (GetWhatsappCodeButton != null)
            GetWhatsappCodeButton.onClick.AddListener(() => StartCoroutine(GetWhatsappCode()));

        if (TelegramAuthorizationBackButton != null)
            TelegramAuthorizationBackButton.onClick.AddListener(TelegramAuthorizationBack);
        if (TelegramAuthorizationDoneBotton != null)
            TelegramAuthorizationDoneBotton.onClick.AddListener(TelegramAuthorizationDone);
        if (OpenTelegramQRPanelButton != null)
            OpenTelegramQRPanelButton.onClick.AddListener(() => StartCoroutine(OpenTelegramQRPanel()));
        if (OpenTelegramCodePanelButton != null)
            OpenTelegramCodePanelButton.onClick.AddListener(OpenTelegramCodePanel);
        if (CloseTelegramQRPanelButton != null)
            CloseTelegramQRPanelButton.onClick.AddListener(CloseTelegramQRPanel);
        if (CloseTelegramCodePanelButton != null)
            CloseTelegramCodePanelButton.onClick.AddListener(CloseTelegramCodePanel);
        if (GetTelegramCodeButton != null)
            GetTelegramCodeButton.onClick.AddListener(() => StartCoroutine(GetTelegramCode()));
        if (SendTelegramCodeButton != null)
            SendTelegramCodeButton.onClick.AddListener(() => StartCoroutine(SendTelegramCode()));

        if (WhatsappNumberInput != null)
            WhatsappNumberInput.onValueChanged.AddListener(WhatsappNumberInputChanged);
        if (TelegramNumberInput != null)
            TelegramNumberInput.onValueChanged.AddListener(TelegramNumberInputChanged);
        if (TelegramCodeInput != null)
            TelegramCodeInput.onValueChanged.AddListener(TelegramCodeInputChanged);

        if (ConfirmChangeWhatsappNumberButton != null)
            PopupUI.WireFingerUp(ConfirmChangeWhatsappNumberButton, ConfirmChangeWhatsappNumber);
        if (CancelChangeWhatsappNumberButton != null)
            PopupUI.WireFingerUp(CancelChangeWhatsappNumberButton, CancelChangeWhatsappNumber);
        if (ConfirmChangeTelegramNumberButton != null)
            PopupUI.WireFingerUp(ConfirmChangeTelegramNumberButton, ConfirmChangeTelegramNumber);
        if (CancelChangeTelegramNumberButton != null)
            PopupUI.WireFingerUp(CancelChangeTelegramNumberButton, CancelChangeTelegramNumber);

        if (UploadPriceListButton != null)
            UploadPriceListButton.onClick.AddListener(UploadPriceList);
    }
}

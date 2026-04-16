using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text;
using System;
using System.IO;
using Automation.BotSettingsUI;

public class Manager : MonoBehaviour
{
    #region
    // [SerializeField] private GameObject MainPage;
    [SerializeField] private GameObject WhatsappAuth;
    [SerializeField] private GameObject TelegramAuth;
    // [SerializeField] private GameObject Confirmation;
    [SerializeField] private GameObject BotsPage;
    [SerializeField] private GameObject BotsParent;
    [SerializeField] private GameObject BotPrefab;
    [SerializeField] private GameObject BotSettings;
    [SerializeField] private GameObject BotSettingsParent;
    [SerializeField] private GameObject ProductPrefab;
    [SerializeField] private GameObject ServicePrefab;
    [SerializeField] private GameObject WhatsappQRPanel;
    [SerializeField] private GameObject WhatsappCodePanel;
    [SerializeField] private GameObject TelegramQRPanel;
    [SerializeField] private GameObject TelegramCodePanel;
    [SerializeField] private TextMeshProUGUI TelegramPhoneTitle;
    [SerializeField] private TextMeshProUGUI TelegramPhoneBody;
    [SerializeField] private GameObject WhatsappCodeTimer;
    [SerializeField] private GameObject TelegramCodeTimer;
    // Status messages are shown inline in button text — no separate GOs needed
    [SerializeField] public GameObject LoadingPanel;

    [SerializeField] private GameObject WhatsappAuthSuccessPanel;
    [SerializeField] private Button WhatsappAuthBackButton;
    [SerializeField] private GameObject TelegramAuthSuccessPanel;
    [SerializeField] private Button TelegramAuthBackButton;
    [SerializeField] private Button GetWhatsappCodeButton;
    [SerializeField] private Button GetTelegramCodeButton;
    [SerializeField] private Button SendTelegramCodeButton;
    [SerializeField] private Button ChangeWhatsappNumberButton;
    [SerializeField] private Button ChangeTelegramNumberButton;
    [SerializeField] private Button SaveButton;

    [SerializeField] private TMP_InputField WhatsappNumberInput;
    [SerializeField] private TMP_InputField TelegramNumberInput;
    [SerializeField] private TMP_InputField TelegramCodeInput;

    [SerializeField] private RawImage WhatsappQRCodeImage;
    [SerializeField] private RawImage TelegramQRCodeImage;
    [SerializeField] private GameObject WhatsappQRStatusText;
    [SerializeField] private GameObject TelegramQRStatusText;
    [SerializeField] private RectTransform BusinessTypesParent;
    [SerializeField] private GameObject BusinessTypeButtonTemplate;
    [SerializeField] private BusinessTypesSO businessTypes;

    [Header("Add Bot Form")]
    [SerializeField] private GameObject AddBotFormPage;
    [SerializeField] private TextMeshProUGUI platformValueText;
    [SerializeField] private GameObject platformWhatsappGroup;
    [SerializeField] private GameObject platformTelegramGroup;
    [SerializeField] private GameObject platformPlusSeparator;
    [SerializeField] private TextMeshProUGUI botNameValueText;
    [SerializeField] private TextMeshProUGUI businessTypeValueText;
    [SerializeField] private TextMeshProUGUI descriptionValueText;
    [SerializeField] private Button createBotFormButton;
    [SerializeField] private GameObject platformSelectorPanel;
    [SerializeField] private GameObject botNameInputPanel;
    [SerializeField] private GameObject businessSelectorPanel;
    [SerializeField] private GameObject descriptionInputPanel;
    [SerializeField] private TMP_InputField botNamePopupInput;
    [SerializeField] private TMP_InputField descriptionPopupInput;
    [SerializeField] private Button platformRowButton;
    [SerializeField] private Button botNameRowButton;
    [SerializeField] private Button businessTypeRowButton;
    [SerializeField] private Button descriptionRowButton;
    [SerializeField] private Button whatsappOptionButton;
    [SerializeField] private Button telegramOptionButton;
    [SerializeField] private Button bothOptionButton;

    private readonly System.Collections.Generic.List<Button> businessTypeButtons = new();
    private string selectedBusinessId = "";
    private int id;
    private Color businessButtonDefaultColor;

    private bool CreateWhatsappWorkflowFromEditSuccess;
    private bool EditWhatsappWorkflowSaved;
    private bool EnableWhatsappWorkflowSaved;
    private bool CreateTelegramWorkflowFromEditSuccess;
    private bool EditTelegramWorkflowSaved;
    private bool EnableTelegramWorkflowSaved;

    private string whatsappProfileId = "-1";
    private string telegramProfileId = "-1";

    private int selectedPlatform; // 0=none, 1=WhatsApp, 2=Telegram, 3=Both
    private string formBotName = "";
    private string formDescription = "";
    private bool businessTypeSelected;
    private bool whatsappAuthCompleted;
    private bool telegramAuthCompleted;
    private bool isCreatingBot;
    private Coroutine _whatsappStatusCoroutine;
    private Coroutine _telegramStatusCoroutine;
    private string telegramPhoneTitleInitial;
    private string telegramPhoneBodyInitial;

    public static string wappiAuthToken => Secrets.Data.wappiAuthToken;
    public static string n8nAPIKey => Secrets.Data.n8nAPIKey;

    private string apiUrl => Secrets.Data.greenApi.apiUrl;
    private string idInstance => Secrets.Data.greenApi.idInstance;
    private string apiTokenInstance => Secrets.Data.greenApi.apiTokenInstance;

    public static GameObject BotSettingsParentStatic;
    public static Manager Instance;

    public static BotSettings openBotSettings;
    public static GameObject openBot;

    private string pdf;
    private string txt;
    private string rtf;
    private string xml;
    private string csv;
    private string xls;
    private string xlsx;
    private string doc;
    private string docx;
    private string video;

    // private GreenApiAvatarFetcher greenApiAvatarFetcher;
    // public GameObject GreenApi;
    // public Image profileImage;
    #endregion


    public void Start()
    {
        Application.targetFrameRate = 60;

        PopulateBusinessTypes();

        id = PlayerPrefs.GetInt("ids", 0);

        BotSettingsParentStatic = BotSettingsParent;
        StartCoroutine(LoadBots());

        LoadingPanel.SetActive(false);

        CreateWhatsappWorkflowFromEditSuccess = false;
        EditWhatsappWorkflowSaved = false;
        EnableWhatsappWorkflowSaved = false;
        CreateTelegramWorkflowFromEditSuccess = false;
        EditTelegramWorkflowSaved = false;
        EnableTelegramWorkflowSaved = false;

        Instance = this;

        if (TelegramPhoneTitle != null) telegramPhoneTitleInitial = TelegramPhoneTitle.text;
        if (TelegramPhoneBody != null) telegramPhoneBodyInitial = TelegramPhoneBody.text;

        // Add Bot Form — row buttons
        if (platformRowButton != null) platformRowButton.onClick.AddListener(OpenPlatformSelector);
        if (botNameRowButton != null) botNameRowButton.onClick.AddListener(OpenBotNameInput);
        if (businessTypeRowButton != null) businessTypeRowButton.onClick.AddListener(OpenBusinessSelector);
        if (descriptionRowButton != null) descriptionRowButton.onClick.AddListener(OpenDescriptionInput);

        // Add Bot Form — platform selector (dismisses popup → finger-up)
        if (whatsappOptionButton != null) PopupUI.WireFingerUp(whatsappOptionButton, () => SelectPlatform(1));
        if (telegramOptionButton != null) PopupUI.WireFingerUp(telegramOptionButton, () => SelectPlatform(2));
        if (bothOptionButton != null) PopupUI.WireFingerUp(bothOptionButton, () => SelectPlatform(3));

        // Add Bot Form — create button
        if (createBotFormButton != null)
        {
            createBotFormButton.onClick.AddListener(() => StartCoroutine(CreateBotFromForm()));
            createBotFormButton.interactable = false;
        }

        // Auth panels — WhatsApp
        if (WhatsappAuthBackButton != null) WhatsappAuthBackButton.onClick.AddListener(CancelBotCreation);
        if (GetWhatsappCodeButton != null) GetWhatsappCodeButton.onClick.AddListener(() => StartCoroutine(GetWhatsappCode()));
        if (ChangeWhatsappNumberButton != null) ChangeWhatsappNumberButton.onClick.AddListener(ChangeWhatsappNumber);

        // Auth panels — Telegram
        if (TelegramAuthBackButton != null) TelegramAuthBackButton.onClick.AddListener(CancelBotCreation);

        if (GetTelegramCodeButton != null) GetTelegramCodeButton.onClick.AddListener(() => StartCoroutine(GetTelegramCode()));
        if (SendTelegramCodeButton != null) SendTelegramCodeButton.onClick.AddListener(() => StartCoroutine(SendTelegramCode()));
        if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.onClick.AddListener(ChangeTelegramNumber);

        // Auth input fields
        if (WhatsappNumberInput != null) WhatsappNumberInput.onValueChanged.AddListener(WhatsappNumberInputChanged);
        if (TelegramNumberInput != null) TelegramNumberInput.onValueChanged.AddListener(TelegramNumberInputChanged);
        if (TelegramCodeInput != null) TelegramCodeInput.onValueChanged.AddListener(TelegramCodeInputChanged);

        // Initialize popups as hidden
        if (platformSelectorPanel != null) platformSelectorPanel.SetActive(false);
        if (botNameInputPanel != null) botNameInputPanel.SetActive(false);
        if (businessSelectorPanel != null) businessSelectorPanel.SetActive(false);
        if (descriptionInputPanel != null) descriptionInputPanel.SetActive(false);

        // Wire overlay/close/confirm/cancel dismiss paths for every Add Bot
        // popup via PopupUI helpers. EventAbsorber on each card prevents taps
        // on the card background (non-button areas) from bubbling up to the
        // overlay's dismiss handler.
        WirePopupDismiss(platformSelectorPanel, confirm: null,           cancel: ClosePlatformSelector);
        WirePopupDismiss(botNameInputPanel,     confirm: ConfirmBotName, cancel: CloseBotNameInput);
        WirePopupDismiss(businessSelectorPanel, confirm: null,           cancel: CloseBusinessSelector);
        WirePopupDismiss(descriptionInputPanel, confirm: ConfirmDescription, cancel: CloseDescriptionInput);
    }

    // Standard dismiss wiring for an Add Bot popup: overlay-tap, ✕, Confirm,
    // and Cancel all fire on real finger release; card background absorbs
    // taps so they don't bubble to the overlay's dismiss handler.
    // `confirm` may be null for popups without an OK button (platform /
    // business selectors, where individual options close the popup).
    private static void WirePopupDismiss(GameObject panel, Action confirm, Action cancel)
    {
        if (panel == null || cancel == null) return;

        var overlayBtn = panel.GetComponent<Button>();
        if (overlayBtn != null) PopupUI.WireFingerUp(overlayBtn, cancel);

        var closeBtn = panel.transform.Find("Content/CloseButton")?.GetComponent<Button>();
        if (closeBtn != null) PopupUI.WireFingerUp(closeBtn, cancel);

        var confirmBtn = panel.transform.Find("Content/Buttons/ConfirmButton")?.GetComponent<Button>();
        if (confirmBtn != null && confirm != null) PopupUI.WireFingerUp(confirmBtn, confirm);

        var cancelBtn = panel.transform.Find("Content/Buttons/CancelButton")?.GetComponent<Button>();
        if (cancelBtn != null) PopupUI.WireFingerUp(cancelBtn, cancel);

        var card = panel.transform.Find("Content");
        if (card != null) PopupUI.AbsorbEvents(card);
    }


    public IEnumerator LoadBots()
    {
        yield return new WaitForEndOfFrame();

        for (int i = 0; i < id; i++)
        {
            if (PlayerPrefs.HasKey("Bot" + i.ToString() + "Name"))
            {
                GameObject recreatedBot = Instantiate(BotPrefab, BotPrefab.transform.position, BotPrefab.transform.rotation, BotsParent.transform);

                recreatedBot.name = "Bot" + i.ToString();

                var recreatedBotComp = recreatedBot.GetComponent<Bot>();
                if (recreatedBotComp.BotName != null)
                    recreatedBotComp.BotName.text = PlayerPrefs.GetString(recreatedBot.name + "Name", "");
                if (recreatedBotComp.BotDesc != null)
                    recreatedBotComp.BotDesc.text = PlayerPrefs.GetString(recreatedBot.name + "Business", "");
                if (recreatedBotComp.ActivationSwitch != null)
                    recreatedBotComp.ActivationSwitch.isOn = PlayerPrefs.GetInt(recreatedBot.name + "isOn", 1) == 1;
                if (recreatedBotComp.Status != null)
                    recreatedBotComp.Status.text = PlayerPrefs.GetString(recreatedBot.name + "Status", "");
                recreatedBot.GetComponent<Bot>().active = PlayerPrefs.GetInt(recreatedBot.name + "Active", 0) == 1;
                recreatedBot.GetComponent<Bot>().whatsappProfileId = PlayerPrefs.GetString(recreatedBot.name + "WhatsappProfileId", "-1");
                recreatedBot.GetComponent<Bot>().telegramProfileId = PlayerPrefs.GetString(recreatedBot.name + "TelegramProfileId", "-1");
                recreatedBot.GetComponent<Bot>().whatsappWorkflowId = PlayerPrefs.GetString(recreatedBot.name + "WhatsappWorkflowId", "-1");
                recreatedBot.GetComponent<Bot>().telegramWorkflowId = PlayerPrefs.GetString(recreatedBot.name + "TelegramWorkflowId", "-1");
                // Apply the icon now — Bot.Awake fires before the rename above,
                // so it sees the prefab name and no PlayerPrefs entry. Refresh
                // explicitly now that the bot has its final name.
                recreatedBotComp.RefreshBusinessIcon();

                BotSettings recreatedBotSettings = Instantiate(BotSettings, new Vector3(BotSettings.transform.position.x + Screen.width / 2, BotSettings.transform.position.y + Screen.height / 2, 0), BotSettings.transform.rotation, BotSettingsParent.transform).GetComponent<BotSettings>();

                recreatedBotSettings.BotNameField.Value = PlayerPrefs.GetString(recreatedBot.name + "Name", "");
                recreatedBotSettings.WhatsappToggle.isOn = PlayerPrefs.GetInt(recreatedBot.name + "isOnWhatsapp", 1) == 1;
                recreatedBotSettings.TelegramToggle.isOn = PlayerPrefs.GetInt(recreatedBot.name + "isOnTelegram", 1) == 1;
                PopulateBusinessDropdown(recreatedBotSettings.BusinessTypeDropdown);
                {
                    var savedId = PlayerPrefs.GetString(recreatedBot.name + "BusinessType", "");
                    recreatedBotSettings.BusinessTypeDropdown.value = Mathf.Max(0, businessTypes.IndexOf(savedId));
                }
                recreatedBotSettings.WhatsappNumberField.Value = PlayerPrefs.GetString(recreatedBot.name + "WhatsappNumber", "");
                recreatedBotSettings.TelegramNumberField.Value = PlayerPrefs.GetString(recreatedBot.name + "TelegramNumber", "");

                recreatedBotSettings.WhatsappNumberField.gameObject.SetActive(!recreatedBotSettings.WhatsappNumberField.Value.Equals(""));
                recreatedBotSettings.TelegramNumberField.gameObject.SetActive(!recreatedBotSettings.TelegramNumberField.Value.Equals(""));

                recreatedBotSettings.BusinessField.Value = PlayerPrefs.GetString(recreatedBot.name + "Business", "");
                recreatedBotSettings.PromptField.Value = PlayerPrefs.GetString(recreatedBot.name + "Prompt", "");

                int ProductsNumber = PlayerPrefs.GetInt(recreatedBot.name + "ProductsNumber", 0);
                for (int p = 0; p < ProductsNumber; p++)
                {
                    ProductCardView recreatedProduct = Instantiate(ProductPrefab, ProductPrefab.transform.position, ProductPrefab.transform.rotation, recreatedBotSettings.AddProductButton.transform.parent.parent).GetComponent<ProductCardView>();

                    recreatedProduct.Name = PlayerPrefs.GetString(recreatedBot.name + "Product" + p, "");
                    recreatedProduct.Price = PlayerPrefs.GetString(recreatedBot.name + "Product" + p + "Price", "");
                    recreatedProduct.Description = PlayerPrefs.GetString(recreatedBot.name + "Product" + p + "Description", "");
                }

                recreatedBotSettings.AddProductButton.transform.parent.SetAsLastSibling();


                int ServicesNumber = PlayerPrefs.GetInt(recreatedBot.name + "ServicesNumber", 0);
                for (int s = 0; s < ServicesNumber; s++)
                {
                    ServiceCardView recreatedService = Instantiate(ServicePrefab, ServicePrefab.transform.position, ServicePrefab.transform.rotation, recreatedBotSettings.AddServiceButton.transform.parent.parent).GetComponent<ServiceCardView>();

                    recreatedService.Name = PlayerPrefs.GetString(recreatedBot.name + "Service" + s, "");
                    recreatedService.Price = PlayerPrefs.GetString(recreatedBot.name + "Service" + s + "Price", "");
                    recreatedService.Description = PlayerPrefs.GetString(recreatedBot.name + "Service" + s + "Description", "");
                }

                recreatedBotSettings.AddServiceButton.transform.parent.SetAsLastSibling();
            }
        }

        if (PlayerPrefs.GetInt("lastCreatedWhatsappProfileIdSaved", 1) == 0 && !PlayerPrefs.GetString("lastCreatedWhatsappProfileId", "-1").Equals("-1"))
        {
            // StartCoroutine(DeleteWhatsappProfile(PlayerPrefs.GetString("lastCreatedWhatsappProfileId", "-1"), true));
        }

        if (PlayerPrefs.GetInt("lastCreatedTelegramProfileIdSaved", 1) == 0 && !PlayerPrefs.GetString("lastCreatedTelegramProfileId", "-1").Equals("-1"))
        {
            // StartCoroutine(DeleteTelegramProfile(PlayerPrefs.GetString("lastCreatedTelegramProfileId", "-1"), true));
        }
    }

    public void SaveSettings()
    {
        var newName = openBotSettings.BotNameField.Value;
        PlayerPrefs.SetString(openBot.name + "Name", newName);
        var openBotComp = openBot.GetComponent<Bot>();
        if (openBotComp.BotName != null) openBotComp.BotName.text = newName;
        // Refresh the card's description from the about-business text.
        if (openBotComp.BotDesc != null)
            openBotComp.BotDesc.text = openBotSettings.BusinessField.Value;

        {
            var dd = openBotSettings.BusinessTypeDropdown;
            if (businessTypes.TryGetByIndex(dd.value, out var bt))
                PlayerPrefs.SetString(openBot.name + "BusinessType", bt.id);
        }
        openBot.GetComponent<Bot>()?.RefreshBusinessIcon();
        //PlayerPrefs.SetInt(openBot.name + "isOnWhatsapp", openBotSettings.WhatsappToggle.isOn ? 1 : 0);
        //PlayerPrefs.SetInt(openBot.name + "isOnTelegram", openBotSettings.TelegramToggle.isOn ? 1 : 0);

        PlayerPrefs.SetString(openBot.name + "WhatsappNumber", openBotSettings.WhatsappNumberField.Value);
        PlayerPrefs.SetString(openBot.name + "TelegramNumber", openBotSettings.TelegramNumberField.Value);

        openBotSettings.WhatsappNumberField.gameObject.SetActive(!openBotSettings.WhatsappNumberField.Value.Equals(""));
        openBotSettings.TelegramNumberField.gameObject.SetActive(!openBotSettings.TelegramNumberField.Value.Equals(""));


        PlayerPrefs.SetString(openBot.name + "Business", openBotSettings.BusinessField.Value);
        openBotSettings.BusinessInput.text = "";

        PlayerPrefs.SetString(openBot.name + "Prompt", openBotSettings.PromptField.Value);
        openBotSettings.PromptInput.text = "";


        for (int i = 0; i < openBotSettings.ProductsParent.transform.childCount - 1; i++)
        {
            Transform product = openBotSettings.ProductsParent.transform.GetChild(i);

            product.GetComponent<ProductCardView>().Name.Trim();
            if (!product.GetComponent<ProductCardView>().Name.Equals(""))
            {
                PlayerPrefs.SetString(openBot.name + "Product" + i, product.GetComponent<ProductCardView>().Name);
                PlayerPrefs.SetString(openBot.name + "Product" + i + "Price", product.GetComponent<ProductCardView>().Price);
                PlayerPrefs.SetString(openBot.name + "Product" + i + "Description", product.GetComponent<ProductCardView>().Description);
            }
        }

        //delete not used keyes
        if (PlayerPrefs.GetInt(openBot.name + "ProductsNumber", 0) > openBotSettings.ProductsParent.transform.childCount - 1)
        {
            for (int p = openBotSettings.ProductsParent.transform.childCount - 1; p < PlayerPrefs.GetInt(openBot.name + "ProductsNumber", 0); p++)
            {
                if (PlayerPrefs.HasKey(openBot.name + "Product" + p))
                {
                    PlayerPrefs.DeleteKey(openBot.name + "Product" + p);
                }

                if (PlayerPrefs.HasKey(openBot.name + "Product" + p + "Price"))
                {
                    PlayerPrefs.DeleteKey(openBot.name + "Product" + p + "Price");
                }

                if (PlayerPrefs.HasKey(openBot.name + "Product" + p + "Description"))
                {
                    PlayerPrefs.DeleteKey(openBot.name + "Product" + p + "Description");
                }
            }
        }

        PlayerPrefs.SetInt(openBot.name + "ProductsNumber", openBotSettings.ProductsParent.transform.childCount - 1);


        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount - 1; i++)
        {
            Transform service = openBotSettings.ServicesParent.transform.GetChild(i);

            service.GetComponent<ServiceCardView>().Name.Trim();
            if (!service.GetComponent<ServiceCardView>().Name.Equals(""))
            {
                PlayerPrefs.SetString(openBot.name + "Service" + i, service.GetComponent<ServiceCardView>().Name);
                PlayerPrefs.SetString(openBot.name + "Service" + i + "Price", service.GetComponent<ServiceCardView>().Price);
                PlayerPrefs.SetString(openBot.name + "Service" + i + "Description", service.GetComponent<ServiceCardView>().Description);
            }
        }

        //delete not used keyes
        if (PlayerPrefs.GetInt(openBot.name + "ServicesNumber", 0) > openBotSettings.ServicesParent.transform.childCount - 1)
        {
            for (int s = openBotSettings.ServicesParent.transform.childCount - 1; s < PlayerPrefs.GetInt(openBot.name + "ServicesNumber", 0); s++)
            {
                if (PlayerPrefs.HasKey(openBot.name + "Service" + s))
                {
                    PlayerPrefs.DeleteKey(openBot.name + "Service" + s);
                }

                if (PlayerPrefs.HasKey(openBot.name + "Service" + s + "Price"))
                {
                    PlayerPrefs.DeleteKey(openBot.name + "Service" + s + "Price");
                }

                if (PlayerPrefs.HasKey(openBot.name + "Service" + s + "Description"))
                {
                    PlayerPrefs.DeleteKey(openBot.name + "Service" + s + "Description");
                }
            }
        }

        PlayerPrefs.SetInt(openBot.name + "ServicesNumber", openBotSettings.ServicesParent.transform.childCount - 1);


        PlayerPrefs.Save(); // Ensure changes are written to disk
    }

    public void CloseSettings()
    {
        openBotSettings.BotNameField.Value = PlayerPrefs.GetString(openBot.name + "Name", "");
        openBotSettings.WhatsappToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(openBot.name + "isOnWhatsapp", 1) == 1);
        openBotSettings.TelegramToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(openBot.name + "isOnTelegram", 1) == 1);
        {
            var savedId = PlayerPrefs.GetString(openBot.name + "BusinessType", "");
            openBotSettings.BusinessTypeDropdown.value = Mathf.Max(0, businessTypes.IndexOf(savedId));
        }
        openBotSettings.WhatsappNumberField.Value = PlayerPrefs.GetString(openBot.name + "WhatsappNumber", "");
        openBotSettings.TelegramNumberField.Value = PlayerPrefs.GetString(openBot.name + "TelegramNumber", "");

        openBotSettings.WhatsappNumberField.gameObject.SetActive(!openBotSettings.WhatsappNumberField.Value.Equals(""));
        openBotSettings.TelegramNumberField.gameObject.SetActive(!openBotSettings.TelegramNumberField.Value.Equals(""));

        openBotSettings.BusinessField.Value = PlayerPrefs.GetString(openBot.name + "Business", "");
        openBotSettings.PromptField.Value = PlayerPrefs.GetString(openBot.name + "Prompt", "");


        for (int p = 0; p < openBotSettings.ProductsParent.transform.childCount - 1; p++)
        {
            Destroy(openBotSettings.ProductsParent.transform.GetChild(p).gameObject);
        }

        int ProductsNumber = PlayerPrefs.GetInt(openBot.name + "ProductsNumber", 0);
        for (int p = 0; p < ProductsNumber; p++)
        {
            ProductCardView recreatedProduct = Instantiate(ProductPrefab, ProductPrefab.transform.position, ProductPrefab.transform.rotation, openBotSettings.AddProductButton.transform.parent.parent).GetComponent<ProductCardView>();

            recreatedProduct.Name = PlayerPrefs.GetString(openBot.name + "Product" + p, "");
            recreatedProduct.Price = PlayerPrefs.GetString(openBot.name + "Product" + p + "Price", "");
            recreatedProduct.Description = PlayerPrefs.GetString(openBot.name + "Product" + p + "Description", "");
        }

        openBotSettings.AddProductButton.transform.parent.SetAsLastSibling();


        for (int s = 0; s < openBotSettings.ServicesParent.transform.childCount - 1; s++)
        {
            Destroy(openBotSettings.ServicesParent.transform.GetChild(s).gameObject);
        }

        int ServicesNumber = PlayerPrefs.GetInt(openBot.name + "ServicesNumber", 0);
        for (int s = 0; s < ServicesNumber; s++)
        {
            ServiceCardView recreatedService = Instantiate(ServicePrefab, ServicePrefab.transform.position, ServicePrefab.transform.rotation, openBotSettings.AddServiceButton.transform.parent.parent).GetComponent<ServiceCardView>();

            recreatedService.Name = PlayerPrefs.GetString(openBot.name + "Service" + s, "");
            recreatedService.Price = PlayerPrefs.GetString(openBot.name + "Service" + s + "Price", "");
            recreatedService.Description = PlayerPrefs.GetString(openBot.name + "Service" + s + "Description", "");
        }

        openBotSettings.AddServiceButton.transform.parent.SetAsLastSibling();
    }

    public void EnableSave()
    {
        bool settingsChanged = false;

        if (!openBotSettings.BotNameField.Value.Equals(PlayerPrefs.GetString(openBot.name + "Name", "")) ||
            openBotSettings.WhatsappToggle.isOn != (PlayerPrefs.GetInt(openBot.name + "isOnWhatsapp", 1) == 1) ||
            openBotSettings.TelegramToggle.isOn != (PlayerPrefs.GetInt(openBot.name + "isOnTelegram", 1) == 1) ||
            (businessTypes.TryGetByIndex(openBotSettings.BusinessTypeDropdown.value, out var dirtyBt)
                ? dirtyBt.id : "")
                != PlayerPrefs.GetString(openBot.name + "BusinessType", "") ||
            !openBotSettings.WhatsappNumberField.Value.Equals(PlayerPrefs.GetString(openBot.name + "WhatsappNumber", "")) ||
            !openBotSettings.TelegramNumberField.Value.Equals(PlayerPrefs.GetString(openBot.name + "TelegramNumber", "")) ||
            !openBotSettings.BusinessField.Value.Equals(PlayerPrefs.GetString(openBot.name + "Business", "")) ||
            !openBotSettings.PromptField.Value.Equals(PlayerPrefs.GetString(openBot.name + "Prompt", "")))
        {
            settingsChanged = true;
        }


        if (settingsChanged)
        {
            SaveButton.interactable = true;
        }
        else
        {
            SaveButton.interactable = false;
        }

        StartCoroutine(CheckProductsOrServicesChanged());
    }

    public IEnumerator CheckProductsOrServicesChanged()
    {
        yield return new WaitForEndOfFrame();

        if (openBotSettings.ProductsParent.transform.childCount - 1 != PlayerPrefs.GetInt(openBot.name + "ProductsNumber", 0) ||
            openBotSettings.ServicesParent.transform.childCount - 1 != PlayerPrefs.GetInt(openBot.name + "ServicesNumber", 0))
        {
            SaveButton.interactable = true;
        }

        else if (openBotSettings.ProductsParent.transform.childCount - 1 == PlayerPrefs.GetInt(openBot.name + "ProductsNumber", 0))
        {
            for (int p = 0; p < openBotSettings.ProductsParent.transform.childCount - 1; p++)
            {
                if (!openBotSettings.ProductsParent.transform.GetChild(p).GetComponent<ProductCardView>().Name.Equals(PlayerPrefs.GetString(openBot.name + "Product" + p, "")) ||
                    !openBotSettings.ProductsParent.transform.GetChild(p).GetComponent<ProductCardView>().Price.Equals(PlayerPrefs.GetString(openBot.name + "Product" + p + "Price", "")) ||
                    !openBotSettings.ProductsParent.transform.GetChild(p).GetComponent<ProductCardView>().Description.Equals(PlayerPrefs.GetString(openBot.name + "Product" + p + "Description", "")))
                {
                    SaveButton.interactable = true;
                }
            }
        }

        else if (openBotSettings.ServicesParent.transform.childCount - 1 == PlayerPrefs.GetInt(openBot.name + "ServicesNumber", 0))
        {
            for (int s = 0; s < openBotSettings.ServicesParent.transform.childCount - 1; s++)
            {
                if (!openBotSettings.ServicesParent.transform.GetChild(s).GetComponent<ServiceCardView>().Name.Equals(PlayerPrefs.GetString(openBot.name + "Service" + s, "")) ||
                    !openBotSettings.ServicesParent.transform.GetChild(s).GetComponent<ServiceCardView>().Price.Equals(PlayerPrefs.GetString(openBot.name + "Service" + s + "Price", "")) ||
                    !openBotSettings.ServicesParent.transform.GetChild(s).GetComponent<ServiceCardView>().Description.Equals(PlayerPrefs.GetString(openBot.name + "Service" + s + "Description", "")))
                {
                    SaveButton.interactable = true;
                }
            }
        }
    }


    //////////////////////////////////////////////////////////CREATE BOT//////////////////////////////////////////////////////////

    // ── Add Bot Form — Popup Controllers ──

    public void OpenPlatformSelector() => PopupUI.Show(platformSelectorPanel);

    public void ClosePlatformSelector() => PopupUI.Hide(platformSelectorPanel);

    public void SelectPlatform(int mode)
    {
        selectedPlatform = mode;

        bool showWa = mode == 1 || mode == 3;
        bool showTg = mode == 2 || mode == 3;

        if (platformWhatsappGroup != null) platformWhatsappGroup.SetActive(showWa);
        if (platformTelegramGroup != null) platformTelegramGroup.SetActive(showTg);
        if (platformPlusSeparator != null) platformPlusSeparator.SetActive(mode == 3);

        // Hide placeholder ValueText — each group carries its own colored label
        if (platformValueText != null) platformValueText.gameObject.SetActive(false);

        // Force immediate layout rebuild — nested CSF + HLG chains don't resolve on
        // the same frame a previously-inactive child becomes active, so the first
        // selection would show the icon/label overlapping until the next change.
        RectTransform platformIconContainer = null;
        if (platformWhatsappGroup != null)
            platformIconContainer = platformWhatsappGroup.transform.parent as RectTransform;
        else if (platformTelegramGroup != null)
            platformIconContainer = platformTelegramGroup.transform.parent as RectTransform;
        if (platformIconContainer != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(platformIconContainer);
            if (platformIconContainer.parent is RectTransform rowRt)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rowRt);
        }

        ClosePlatformSelector();
        ValidateCreateForm();
    }

    public void OpenBotNameInput()
    {
        PopupUI.Show(botNameInputPanel, onCardSettled: () =>
        {
            // Activate the input field after the card settles — the native
            // keyboard slide-up otherwise competes with the open tween.
            if (botNamePopupInput != null) botNamePopupInput.ActivateInputField();
        });

        if (botNamePopupInput != null)
            botNamePopupInput.text = formBotName;
    }

    public void CloseBotNameInput() => PopupUI.Hide(botNameInputPanel);

    public void ConfirmBotName()
    {
        if (botNamePopupInput != null && !string.IsNullOrEmpty(botNamePopupInput.text))
        {
            formBotName = botNamePopupInput.text.Trim();
            botNameValueText.text = formBotName;
            botNameValueText.color = new Color32(28, 28, 30, 255); // --text-primary
        }

        CloseBotNameInput();
        ValidateCreateForm();
    }

    public void OpenBusinessSelector() => PopupUI.Show(businessSelectorPanel);

    public void CloseBusinessSelector() => PopupUI.Hide(businessSelectorPanel);

    private void PopulateBusinessTypes()
    {
        if (BusinessTypesParent == null || BusinessTypeButtonTemplate == null || businessTypes == null)
        {
            Debug.LogError("[Manager] PopulateBusinessTypes: missing serialized refs (BusinessTypesParent, BusinessTypeButtonTemplate, or businessTypes).");
            return;
        }

        // Destroy any previously-instantiated buttons (everything except the template).
        for (int i = BusinessTypesParent.childCount - 1; i >= 0; i--)
        {
            var child = BusinessTypesParent.GetChild(i).gameObject;
            if (child == BusinessTypeButtonTemplate) continue;
            DestroyImmediate(child);
        }

        BusinessTypeButtonTemplate.SetActive(false);
        businessTypeButtons.Clear();

        foreach (var entry in businessTypes.All)
        {
            var go = Instantiate(BusinessTypeButtonTemplate, BusinessTypesParent);
            go.SetActive(true);
            go.name = entry.id;

            var label = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = entry.displayName;

            var btn = go.GetComponent<Button>();
            var capturedId = entry.id;
            PopupUI.WireFingerUp(btn, () => ChooseBusiness(capturedId));
            businessTypeButtons.Add(btn);
        }

        if (businessTypes.Count > 0)
        {
            selectedBusinessId = businessTypes.All[0].id;
            if (businessTypeButtons.Count > 0)
                businessButtonDefaultColor = businessTypeButtons[0].GetComponent<Image>().color;
        }
        else
        {
            selectedBusinessId = "";
        }
    }

    private void PopulateBusinessDropdown(TMP_Dropdown dd)
    {
        if (dd == null || businessTypes == null) return;

        dd.options.Clear();
        foreach (var entry in businessTypes.All)
            dd.options.Add(new TMP_Dropdown.OptionData(entry.displayName));
        dd.RefreshShownValue();
    }

    public void ChooseBusiness(string id)
    {
        if (businessTypes == null || !businessTypes.TryGetById(id, out var entry)) return;
        selectedBusinessId = id;
        businessTypeSelected = true;

        for (int i = 0; i < businessTypeButtons.Count; i++)
        {
            var btn = businessTypeButtons[i];
            var img = btn.GetComponent<Image>();
            img.color = (btn.gameObject.name == id) ? Color.green : businessButtonDefaultColor;
        }

        if (businessTypeValueText != null)
        {
            businessTypeValueText.text = entry.displayName;
            businessTypeValueText.color = new Color32(28, 28, 30, 255);
        }

        CloseBusinessSelector();
        ValidateCreateForm();
    }

    public void OpenDescriptionInput()
    {
        PopupUI.Show(descriptionInputPanel, onCardSettled: () =>
        {
            if (descriptionPopupInput != null) descriptionPopupInput.ActivateInputField();
        });

        if (descriptionPopupInput != null)
            descriptionPopupInput.text = formDescription;
    }

    public void CloseDescriptionInput() => PopupUI.Hide(descriptionInputPanel);

    public void ConfirmDescription()
    {
        if (descriptionPopupInput != null)
        {
            formDescription = descriptionPopupInput.text.Trim();
            if (!string.IsNullOrEmpty(formDescription))
            {
                descriptionValueText.text = formDescription;
                descriptionValueText.color = new Color32(28, 28, 30, 255);
            }
            else
            {
                descriptionValueText.text = "Необязательно";
                descriptionValueText.color = new Color32(199, 199, 204, 255); // --text-tertiary
            }
        }

        CloseDescriptionInput();
    }

    // ── Form Validation ──

    private void ValidateCreateForm()
    {
        bool isValid = selectedPlatform > 0
                    && !string.IsNullOrEmpty(formBotName)
                    && businessTypeSelected;

        if (createBotFormButton != null)
        {
            createBotFormButton.interactable = isValid;
        }
    }

    // ── Bot Creation Flow ──

    private IEnumerator CreateBotFromForm()
    {
        isCreatingBot = true;
        whatsappAuthCompleted = false;
        telegramAuthCompleted = false;
        whatsappProfileId = "-1";
        telegramProfileId = "-1";
        createBotFormButton.interactable = false;

        bool useWhatsapp = selectedPlatform == 1 || selectedPlatform == 3;
        bool useTelegram = selectedPlatform == 2 || selectedPlatform == 3;

        // Step 1: Create WhatsApp profile and authenticate
        if (useWhatsapp)
        {
            yield return StartCoroutine(CreateWhatsappProfile(formBotName, true));
            if (!isCreatingBot) yield break;

            ShowWhatsappAuth();
            PlayerPrefs.SetString("WhatsappCooldownFinishTime", "-1");
            WhatsappCodeTimer.SetActive(false);

            while (!whatsappAuthCompleted)
            {
                if (!isCreatingBot) yield break;
                yield return new WaitForSeconds(0.5f);
            }
        }

        // Step 2: Create Telegram profile and authenticate
        if (useTelegram)
        {
            yield return StartCoroutine(CreateTelegramProfile(formBotName, true));
            if (!isCreatingBot) yield break;

            ShowTelegramAuth();
            LoadingPanel.SetActive(false);
            PlayerPrefs.SetString("TelegramCooldownFinishTime", "-1");
            TelegramCodeTimer.SetActive(false);

            while (!telegramAuthCompleted)
            {
                if (!isCreatingBot) yield break;
                yield return new WaitForSeconds(0.5f);
            }
        }

        // Step 3: Instantiate bot
        GameObject newBot = Instantiate(BotPrefab, BotPrefab.transform.position, BotPrefab.transform.rotation, BotsParent.transform);
        newBot.name = "Bot" + id.ToString();

        var newBotComp = newBot.GetComponent<Bot>();
        if (newBotComp.BotName != null) newBotComp.BotName.text = formBotName;
        // The Add Bot form captures an optional description in formDescription.
        // Propagate it so the card renders it immediately (matches the LoadBots
        // path which reads PlayerPrefs "Business").
        if (newBotComp.BotDesc != null) newBotComp.BotDesc.text = formDescription;
        if (newBotComp.ActivationSwitch != null) newBotComp.ActivationSwitch.isOn = true;
        if (newBotComp.Status != null) newBotComp.Status.text = "Connecting..";
        newBot.GetComponent<Bot>().active = false;
        newBot.GetComponent<Bot>().EditButton.interactable = false;
        newBot.GetComponent<Bot>().ActivationSwitch.interactable = false;
        newBot.GetComponent<Bot>().whatsappProfileId = whatsappProfileId;
        newBot.GetComponent<Bot>().telegramProfileId = telegramProfileId;

        BotSettings newBotSettings = Instantiate(BotSettings, new Vector3(BotSettings.transform.position.x + Screen.width / 2, BotSettings.transform.position.y + Screen.height / 2, 0), BotSettings.transform.rotation, BotSettingsParentStatic.transform).GetComponent<BotSettings>();

        newBotSettings.BotNameField.Value = formBotName;
        newBotSettings.WhatsappToggle.isOn = useWhatsapp;
        newBotSettings.TelegramToggle.isOn = useTelegram;
        PopulateBusinessDropdown(newBotSettings.BusinessTypeDropdown);
        newBotSettings.BusinessTypeDropdown.value = Mathf.Max(0, businessTypes.IndexOf(selectedBusinessId));
        newBotSettings.BusinessField.Value = formDescription;
        newBotSettings.WhatsappNumberField.Value = useWhatsapp ? WhatsappNumberInput.text : "";
        newBotSettings.TelegramNumberField.Value = useTelegram ? TelegramNumberInput.text : "";
        newBotSettings.WhatsappNumberField.gameObject.SetActive(useWhatsapp && !string.IsNullOrEmpty(WhatsappNumberInput.text));
        newBotSettings.TelegramNumberField.gameObject.SetActive(useTelegram && !string.IsNullOrEmpty(TelegramNumberInput.text));

        // Step 4: Create workflows
        if (useWhatsapp)
        {
            StartCoroutine(CreateWhatsappWorkflowFromStart(newBot));
        }
        else
        {
            newBot.GetComponent<Bot>().whatsappWorkflowId = "-1";
            PlayerPrefs.SetString(newBot.name + "WhatsappWorkflowId", "-1");
        }

        if (useTelegram)
        {
            StartCoroutine(CreateTelegramWorkflowFromStart(newBot));
        }
        else
        {
            newBot.GetComponent<Bot>().telegramWorkflowId = "-1";
            PlayerPrefs.SetString(newBot.name + "TelegramWorkflowId", "-1");
        }

        // Step 5: Save to PlayerPrefs
        PlayerPrefs.SetString(newBot.name + "Name", formBotName);
        PlayerPrefs.SetInt(newBot.name + "isOn", 1);
        PlayerPrefs.SetString(newBot.name + "Status", "Connecting..");
        PlayerPrefs.SetInt(newBot.name + "Active", 0);
        PlayerPrefs.SetInt(newBot.name + "isOnWhatsapp", useWhatsapp ? 1 : 0);
        PlayerPrefs.SetInt(newBot.name + "isOnTelegram", useTelegram ? 1 : 0);
        PlayerPrefs.SetString(newBot.name + "BusinessType", selectedBusinessId);
        // Bot.Awake() fires during Instantiate before the rename + PlayerPrefs
        // write above, so the icon was never applied. Apply it explicitly now.
        newBot.GetComponent<Bot>()?.RefreshBusinessIcon();
        PlayerPrefs.SetString(newBot.name + "Business", formDescription);
        PlayerPrefs.SetString(newBot.name + "WhatsappNumber", useWhatsapp ? WhatsappNumberInput.text : "");
        PlayerPrefs.SetString(newBot.name + "TelegramNumber", useTelegram ? TelegramNumberInput.text : "");

        PlayerPrefs.SetInt("ids", ++id);
        PlayerPrefs.Save();

        // Step 6: Reset form
        ResetAddBotForm();
        isCreatingBot = false;
    }

    private void CancelBotCreation()
    {
        isCreatingBot = false;
        WhatsappAuth.SetActive(false);
        TelegramAuth.SetActive(false);

        // Clear cooldowns — profiles are deleted on back, so timers are invalid
        PlayerPrefs.SetString("WhatsappCooldownFinishTime", "-1");
        PlayerPrefs.SetString("TelegramCooldownFinishTime", "-1");

        if (!whatsappProfileId.Equals("-1"))
        {
            StartCoroutine(DeleteWhatsappProfile(whatsappProfileId, true));
        }
        if (!telegramProfileId.Equals("-1"))
        {
            StartCoroutine(DeleteTelegramProfile(telegramProfileId, true));
        }

        ValidateCreateForm();
    }

    private void ResetAddBotForm()
    {
        selectedPlatform = 0;
        formBotName = "";
        formDescription = "";
        businessTypeSelected = false;
        selectedBusinessId = businessTypes.Count > 0 ? businessTypes.All[0].id : "";

        if (platformValueText != null)
        {
            platformValueText.text = "Выберите";
            platformValueText.color = new Color32(142, 142, 147, 255);
            platformValueText.gameObject.SetActive(true);
        }
        if (platformWhatsappGroup != null) platformWhatsappGroup.SetActive(false);
        if (platformTelegramGroup != null) platformTelegramGroup.SetActive(false);
        if (platformPlusSeparator != null) platformPlusSeparator.SetActive(false);
        if (botNameValueText != null)
        {
            botNameValueText.text = "Введите имя";
            botNameValueText.color = new Color32(142, 142, 147, 255);
        }
        if (businessTypeValueText != null)
        {
            businessTypeValueText.text = "Выберите тип";
            businessTypeValueText.color = new Color32(142, 142, 147, 255);
        }
        if (descriptionValueText != null)
        {
            descriptionValueText.text = "Необязательно";
            descriptionValueText.color = new Color32(199, 199, 204, 255);
        }

        if (WhatsappNumberInput != null) WhatsappNumberInput.text = "";
        if (TelegramNumberInput != null) TelegramNumberInput.text = "";

        foreach (var btn in businessTypeButtons)
        {
            btn.GetComponent<Image>().color = businessButtonDefaultColor;
        }

        if (createBotFormButton != null) createBotFormButton.interactable = false;
    }


    //////////////////////////////////////////////////////////AUTHORIZATION//////////////////////////////////////////////////////////

    public void RebuildWhatsappAuthLayout() => ForceRebuildLayout(WhatsappAuth);
    public void RebuildTelegramAuthLayout() => ForceRebuildLayout(TelegramAuth);

    private static void SetButtonText(Button btn, string text)
    {
        if (btn == null) return;
        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) tmp.text = text;
    }

    private void ForceRebuildLayout(GameObject authPage)
    {
        Canvas.ForceUpdateCanvases();
        var scrollRect = authPage.GetComponentInChildren<ScrollRect>();
        if (scrollRect == null) return;

        // Rebuild innermost ContentSizeFitters first (panels), then root content
        var fitters = scrollRect.content.GetComponentsInChildren<ContentSizeFitter>();
        for (int i = fitters.Length - 1; i >= 0; i--)
        {
            if (fitters[i].transform is RectTransform childRt)
                LayoutRebuilder.ForceRebuildLayoutImmediate(childRt);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
        Canvas.ForceUpdateCanvases();
    }

    private IEnumerator SmoothScrollToBottom(GameObject authPage, float duration = 0.4f)
    {
        var scrollRect = authPage.GetComponentInChildren<ScrollRect>();
        if (scrollRect == null) yield break;

        float start = scrollRect.normalizedPosition.y;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            scrollRect.normalizedPosition = new Vector2(0f, Mathf.Lerp(start, 0f, t));
            yield return null;
        }

        scrollRect.normalizedPosition = Vector2.zero;
    }

    private IEnumerator ShowAuthSuccess(GameObject authPage, GameObject successPanel)
    {
        if (successPanel != null)
        {
            // Block all interaction (back button, etc.) during the success animation
            var cg = authPage.GetComponent<CanvasGroup>();
            if (cg == null) cg = authPage.AddComponent<CanvasGroup>();
            cg.interactable = false;
            cg.blocksRaycasts = true;

            // Scroll to top so the QR container (with checkmark) is visible
            var scrollRect = authPage.GetComponentInChildren<ScrollRect>();
            if (scrollRect != null)
                scrollRect.normalizedPosition = Vector2.one;

            successPanel.SetActive(true);
            yield return new WaitForSeconds(2f);
            successPanel.SetActive(false);

            cg.interactable = true;
        }

        if (isCreatingBot)
        {
            bool moreAuthSteps = selectedPlatform == 3 && authPage != TelegramAuth;
            if (moreAuthSteps)
            {
                // Cover transition to next auth page
                LoadingPanel.SetActive(true);
            }
            else
            {
                // Final auth — switch to bots tab before hiding
                var tabManager = FindObjectOfType<BottomTabManager>();
                if (tabManager != null)
                    tabManager.SwitchTab(3);
            }
        }

        authPage.SetActive(false);
    }

    private void ShowWhatsappAuth()
    {
        // Set up all child states BEFORE activating root to prevent layout jitter
        WhatsappQRPanel.SetActive(true);
        WhatsappQRCodeImage.texture = null;
        WhatsappQRStatusText.GetComponent<TextMeshProUGUI>().text = "Загрузка...";
        WhatsappQRStatusText.SetActive(true);

        WhatsappCodePanel.SetActive(true);
        WhatsappNumberInput.gameObject.SetActive(true);
        GetWhatsappCodeButton.gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(false);
        WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(false);
        if (ChangeWhatsappNumberButton != null) ChangeWhatsappNumberButton.gameObject.SetActive(false);
        WhatsappCodePanel.transform.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
        SetButtonText(GetWhatsappCodeButton, "Получить код");

        // Restore timer/button state from persisted cooldown
        string waCooldown = PlayerPrefs.GetString("WhatsappCooldownFinishTime", "-1");
        if (!waCooldown.Equals("-1") && DateTime.TryParse(waCooldown, out var waCooldownEnd) && waCooldownEnd > DateTime.Now)
        {
            WhatsappCodeTimer.SetActive(true);
            GetWhatsappCodeButton.interactable = false;
        }
        else
        {
            if (!waCooldown.Equals("-1"))
                PlayerPrefs.SetString("WhatsappCooldownFinishTime", "-1");
            WhatsappCodeTimer.SetActive(false);
            GetWhatsappCodeButton.interactable = WhatsappNumberInput.text.Length >= 10;
        }

        if (WhatsappAuthSuccessPanel != null) WhatsappAuthSuccessPanel.SetActive(false);

        // Activate root LAST — everything appears in its final state
        WhatsappAuth.SetActive(true);
        StartCoroutine(OpenWhatsappQRPanel());
    }

    private void ShowTelegramAuth()
    {
        // Set up all child states BEFORE activating root to prevent layout jitter
        TelegramQRPanel.SetActive(true);
        TelegramQRCodeImage.texture = null;
        TelegramQRStatusText.GetComponent<TextMeshProUGUI>().text = "Загрузка...";
        TelegramQRStatusText.SetActive(true);

        TelegramCodePanel.SetActive(true);
        TelegramNumberInput.gameObject.SetActive(true);
        GetTelegramCodeButton.gameObject.SetActive(true);
        TelegramCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        TelegramCodeInput.gameObject.SetActive(false);
        SendTelegramCodeButton.gameObject.SetActive(false);
        if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.gameObject.SetActive(false);
        TelegramCodeInput.text = "";
        SetButtonText(GetTelegramCodeButton, "Получить код");
        SetTelegramCodeEntryTexts(false);

        // Restore timer/button state from persisted cooldown
        string tgCooldown = PlayerPrefs.GetString("TelegramCooldownFinishTime", "-1");
        if (!tgCooldown.Equals("-1") && DateTime.TryParse(tgCooldown, out var tgCooldownEnd) && tgCooldownEnd > DateTime.Now)
        {
            TelegramCodeTimer.SetActive(true);
            GetTelegramCodeButton.interactable = false;
        }
        else
        {
            if (!tgCooldown.Equals("-1"))
                PlayerPrefs.SetString("TelegramCooldownFinishTime", "-1");
            TelegramCodeTimer.SetActive(false);
            GetTelegramCodeButton.interactable = TelegramNumberInput.text.Length >= 10;
        }

        if (TelegramAuthSuccessPanel != null) TelegramAuthSuccessPanel.SetActive(false);

        // Activate root LAST — everything appears in its final state
        TelegramAuth.SetActive(true);
        StartCoroutine(OpenTelegramQRPanel());
    }

    private IEnumerator OpenWhatsappQRPanel()
    {
        WhatsappQRStatusText.GetComponent<TextMeshProUGUI>().text = "Загрузка...";
        WhatsappQRStatusText.SetActive(true);

        // Wait for Wappi to finish provisioning the newly created profile
        yield return new WaitForSeconds(3f);
        if (!WhatsappQRPanel.activeSelf) yield break;

        string lastError = "Server Unavailable.\nTry Again Later";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!WhatsappQRPanel.activeSelf) yield break;

            using (var www = UnityWebRequest.Get($"https://wappi.pro/api/sync/qr/get?profile_id={whatsappProfileId}"))
            {
                www.SetRequestHeader("Authorization", wappiAuthToken);
                www.timeout = 30;
                yield return www.SendWebRequest();

                if (!WhatsappQRPanel.activeSelf) yield break;

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string response = www.downloadHandler.text;

                    if (response.Contains("data:image/png;base64,") && response.Contains("\",\"task_id\":"))
                    {
                        int startIndex = response.IndexOf("data:image/png;base64,") + 22;
                        int endIndex = response.IndexOf("\",\"task_id\":");
                        int length = endIndex - startIndex;

                        byte[] imageBytes = Convert.FromBase64String(response.Substring(startIndex, length));
                        Texture2D texture = new(2, 2);

                        if (texture.LoadImage(imageBytes))
                            WhatsappQRCodeImage.texture = texture;

                        WhatsappQRStatusText.SetActive(false);
                        ForceRebuildLayout(WhatsappAuth);

                        if (_whatsappStatusCoroutine != null) StopCoroutine(_whatsappStatusCoroutine);
                        _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
                        yield break;
                    }
                }
                else if (www.result == UnityWebRequest.Result.ConnectionError)
                {
                    lastError = "Check internet connection.";
                }
                else if (www.result == UnityWebRequest.Result.ProtocolError && www.downloadHandler != null)
                {
                    string errResp = www.downloadHandler.text;
                    if (errResp.Contains("\"detail\":") && errResp.Contains("\",\"status\":"))
                    {
                        int si = errResp.IndexOf("\"detail\":") + 10;
                        int ei = errResp.IndexOf("\",\"status\":");
                        lastError = errResp.Substring(si, ei - si) + ".";
                    }
                }
            }

            if (attempt < 4)
                yield return new WaitForSeconds(3f);
        }

        WhatsappQRStatusText.GetComponent<TextMeshProUGUI>().text = lastError;
        WhatsappQRStatusText.SetActive(true);
        ForceRebuildLayout(WhatsappAuth);
    }
    public void CloseWhatsappQRPanel()
    {
        WhatsappQRPanel.SetActive(false);

        WhatsappQRCodeImage.texture = null;

        WhatsappQRStatusText.SetActive(false);
        WhatsappQRStatusText.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\nTry Again Later";
    }

    public void OpenWhatsappCodePanel()
    {
        WhatsappCodePanel.SetActive(true);

        WhatsappNumberInput.caretPosition = WhatsappNumberInput.text.Length;
        WhatsappNumberInput.ActivateInputField();

        if (WhatsappNumberInput.text.Length >= 10 && !WhatsappCodeTimer.activeSelf)
        {
            GetWhatsappCodeButton.interactable = true;
        }
    }

    public void WhatsappNumberInputChanged(string newText)
    {
        if (string.IsNullOrEmpty(newText) || newText.Length < 10 || WhatsappCodeTimer.activeSelf)
        {
            GetWhatsappCodeButton.interactable = false;
        }
        else
        {
            GetWhatsappCodeButton.interactable = true;
        }
    }

    private IEnumerator GetWhatsappCode()
    {
        LoadingPanel.SetActive(true);
        GetWhatsappCodeButton.interactable = false;

        string originalWaBtnText = GetWhatsappCodeButton.GetComponentInChildren<TextMeshProUGUI>().text;
        SetButtonText(GetWhatsappCodeButton, "Getting..");

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/auth/code?profile_id={whatsappProfileId}&phone=7{WhatsappNumberInput.text}");

        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            string errorMsg = "Server Unavailable";
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                errorMsg = "Check internet connection";
            }
            else if (www.result == UnityWebRequest.Result.ProtocolError && www.downloadHandler != null)
            {
                string response = www.downloadHandler.text;
                if (response.Contains("\"detail\":") && response.Contains("\",\"uuid\":"))
                {
                    int startIndex = response.IndexOf("\"detail\":") + 10;
                    int endIndex = response.IndexOf("\",\"uuid\":");
                    errorMsg = response.Substring(startIndex, endIndex - startIndex);
                }
            }

            SetButtonText(GetWhatsappCodeButton, errorMsg);
            yield return new WaitForSeconds(2f);
            SetButtonText(GetWhatsappCodeButton, originalWaBtnText);

            if (WhatsappNumberInput.text.Length >= 10)
                GetWhatsappCodeButton.interactable = true;

            LoadingPanel.SetActive(false);
        }
        else
        {
            SetButtonText(GetWhatsappCodeButton, originalWaBtnText);
            WhatsappCodeTimer.SetActive(true);

            WhatsappNumberInput.gameObject.SetActive(false);
            WhatsappCodePanel.transform.GetChild(3).gameObject.SetActive(false);
            WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(true);
            WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(true);
            SetButtonText(GetWhatsappCodeButton, "Получить другой код");
            if (ChangeWhatsappNumberButton != null) ChangeWhatsappNumberButton.gameObject.SetActive(true);

            string response = www.downloadHandler.text;

            if (response.Contains("\"code\":\""))
            {
                int startIndex = response.IndexOf("\"code\":\"") + 8;

                WhatsappCodePanel.transform.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = response.Substring(startIndex, 9);

                if (_whatsappStatusCoroutine != null) StopCoroutine(_whatsappStatusCoroutine);
                _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
            }

            ForceRebuildLayout(WhatsappAuth);
            // Snap scroll to bottom before revealing — prevents code panel jumping over QR section
            var waScrollRect = WhatsappAuth.GetComponentInChildren<ScrollRect>();
            if (waScrollRect != null) waScrollRect.normalizedPosition = Vector2.zero;
            LoadingPanel.SetActive(false);
        }
    }

    public void CloseWhatsappCodePanel()
    {
        WhatsappCodePanel.SetActive(false);

        WhatsappNumberInput.gameObject.SetActive(true);
        GetWhatsappCodeButton.gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(false);
        WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(false);

        if (ChangeWhatsappNumberButton != null) ChangeWhatsappNumberButton.gameObject.SetActive(false);

        WhatsappCodePanel.transform.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
        ForceRebuildLayout(WhatsappAuth);
    }

    public void ChangeWhatsappNumber()
    {
        WhatsappNumberInput.gameObject.SetActive(true);
        GetWhatsappCodeButton.gameObject.SetActive(true);
        SetButtonText(GetWhatsappCodeButton, "Получить код");
        WhatsappCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(false);
        WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(false);

        if (ChangeWhatsappNumberButton != null) ChangeWhatsappNumberButton.gameObject.SetActive(false);

        WhatsappCodePanel.transform.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = "";

        if (WhatsappNumberInput.text.Length >= 10 && !WhatsappCodeTimer.activeSelf)
            GetWhatsappCodeButton.interactable = true;

        ForceRebuildLayout(WhatsappAuth);
    }

    private IEnumerator GetWhatsappProfileStatus()
    {
        bool authorized = false;

        while (!authorized)
        {
            using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/get/status?profile_id={whatsappProfileId}");

            www.SetRequestHeader("Authorization", wappiAuthToken);

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string response = www.downloadHandler.text;

                if (response.Contains("\"authorized\":"))
                {
                    int startIndex = response.IndexOf("\"authorized\":") + 13;
                    int endIndex = response.IndexOf(",\"authorized_at\":");
                    int lenght = endIndex - startIndex;

                    if (response.Substring(startIndex, lenght).Equals("true"))
                    {
                        authorized = true;

                        if (response.Contains("\"phone\":") && response.Contains("\",\"platform\":"))
                        {
                            startIndex = response.IndexOf("\"phone\":") + 9;
                            endIndex = response.IndexOf("\",\"platform\":");
                            lenght = endIndex - startIndex;

                            WhatsappNumberInput.text = response.Substring(startIndex, lenght);
                        }

                        // Show checkmark inside QR box, then navigate
                        yield return StartCoroutine(ShowAuthSuccess(WhatsappAuth, WhatsappAuthSuccessPanel));
                        whatsappAuthCompleted = true;
                    }
                }
            }

            yield return new WaitForSeconds(5f);
        }
    }

    private IEnumerator OpenTelegramQRPanel()
    {
        TelegramQRStatusText.GetComponent<TextMeshProUGUI>().text = "Загрузка...";
        TelegramQRStatusText.SetActive(true);

        // Wait for Wappi to finish provisioning the newly created profile
        yield return new WaitForSeconds(3f);
        if (!TelegramQRPanel.activeSelf) yield break;

        string lastError = "Server Unavailable.\nTry Again Later";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!TelegramQRPanel.activeSelf) yield break;

            using (var www = UnityWebRequest.Get($"https://wappi.pro/tapi/sync/auth/qr?profile_id={telegramProfileId}"))
            {
                www.SetRequestHeader("Authorization", wappiAuthToken);
                www.timeout = 30;
                yield return www.SendWebRequest();

                if (!TelegramQRPanel.activeSelf) yield break;

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string response = www.downloadHandler.text;

                    if (response.Contains("\"detail\":\"") && response.Contains("\",\"uuid\":"))
                    {
                        int startIndex = response.IndexOf("\"detail\":\"") + 10;
                        int endIndex = response.IndexOf("\",\"uuid\":");
                        int length = endIndex - startIndex;

                        byte[] imageBytes = Convert.FromBase64String(response.Substring(startIndex, length));
                        Texture2D texture = new(2, 2);

                        if (texture.LoadImage(imageBytes))
                            TelegramQRCodeImage.texture = texture;

                        TelegramQRStatusText.SetActive(false);
                        ForceRebuildLayout(TelegramAuth);

                        if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
                        _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());
                        yield break;
                    }
                }
                else if (www.result == UnityWebRequest.Result.ConnectionError)
                {
                    lastError = "Check internet connection.";
                }
                else if (www.result == UnityWebRequest.Result.ProtocolError && www.downloadHandler != null)
                {
                    string errResp = www.downloadHandler.text;
                    if (errResp.Contains("\"detail\":") && errResp.Contains("\",\"status\":"))
                    {
                        int si = errResp.IndexOf("\"detail\":") + 10;
                        int ei = errResp.IndexOf("\",\"status\":");
                        lastError = errResp.Substring(si, ei - si) + ".";
                    }
                }
            }

            if (attempt < 4)
                yield return new WaitForSeconds(3f);
        }

        TelegramQRStatusText.GetComponent<TextMeshProUGUI>().text = lastError;
        TelegramQRStatusText.SetActive(true);
        ForceRebuildLayout(TelegramAuth);
    }

    public void CloseTelegramQRPanel()
    {
        TelegramQRPanel.SetActive(false);

        TelegramQRCodeImage.texture = null;

        TelegramQRStatusText.SetActive(false);
        TelegramQRStatusText.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\nTry Again Later";
    }

    private void SetTelegramCodeEntryTexts(bool codeMode)
    {
        if (TelegramPhoneTitle != null)
            TelegramPhoneTitle.text = codeMode ? "Введите код" : telegramPhoneTitleInitial;
        if (TelegramPhoneBody != null)
            TelegramPhoneBody.text = codeMode ? "Откройте Telegram и введите\nполученный код подтверждения" : telegramPhoneBodyInitial;
    }

    public void OpenTelegramCodePanel()
    {
        TelegramCodePanel.SetActive(true);

        TelegramNumberInput.caretPosition = TelegramNumberInput.text.Length;
        TelegramNumberInput.ActivateInputField();

        if (!PlayerPrefs.GetString("TelegramCooldownFinishTime", "-1").Equals("-1"))
        {
            TelegramCodeTimer.SetActive(true);
        }

        if (TelegramNumberInput.text.Length >= 10 && !TelegramCodeTimer.activeSelf)
        {
            GetTelegramCodeButton.interactable = true;
        }
    }

    public void TelegramNumberInputChanged(string newText)
    {
        if (string.IsNullOrEmpty(newText) || newText.Length < 10 || TelegramCodeTimer.activeSelf)
        {
            GetTelegramCodeButton.interactable = false;
        }
        else
        {
            GetTelegramCodeButton.interactable = true;
        }
    }

    private IEnumerator GetTelegramCode()
    {
        LoadingPanel.SetActive(true);
        GetTelegramCodeButton.interactable = false;

        string originalTgBtnText = GetTelegramCodeButton.GetComponentInChildren<TextMeshProUGUI>().text;
        SetButtonText(GetTelegramCodeButton, "Sending..");

        string jsonBody = "{\"phone\":\"7" + TelegramNumberInput.text + "\"}";

        using UnityWebRequest www = new($"https://wappi.pro/tapi/sync/auth/phone?profile_id={telegramProfileId}", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            string errorMsg = "Server Unavailable";
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                errorMsg = "Check internet connection";
            }
            else if (www.result == UnityWebRequest.Result.ProtocolError && www.downloadHandler != null)
            {
                string response = www.downloadHandler.text;
                if (response.Contains("\"detail\":") && response.Contains("\",\"status\":"))
                {
                    int startIndex = response.IndexOf("\"detail\":") + 10;
                    int endIndex = response.IndexOf("\",\"status\":");
                    errorMsg = response.Substring(startIndex, endIndex - startIndex);
                }
            }

            SetButtonText(GetTelegramCodeButton, errorMsg);
            yield return new WaitForSeconds(2f);
            SetButtonText(GetTelegramCodeButton, originalTgBtnText);

            if (TelegramNumberInput.text.Length >= 10)
                GetTelegramCodeButton.interactable = true;

            LoadingPanel.SetActive(false);
        }
        else
        {
            TelegramCodeTimer.SetActive(true);

            TelegramNumberInput.gameObject.SetActive(false);
            TelegramCodePanel.transform.GetChild(3).gameObject.SetActive(false);
            TelegramCodeInput.gameObject.SetActive(true);
            SetTelegramCodeEntryTexts(true);
            SendTelegramCodeButton.gameObject.SetActive(true);
            if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.gameObject.SetActive(true);

            // Rebuild layout and snap scroll immediately after toggling elements,
            // before any yield — prevents code panel jumping over QR section
            ForceRebuildLayout(TelegramAuth);
            var tgScrollRect = TelegramAuth.GetComponentInChildren<ScrollRect>();
            if (tgScrollRect != null) tgScrollRect.normalizedPosition = Vector2.zero;
            LoadingPanel.SetActive(false);

            // Show "Sent" confirmation briefly, then set persistent "another code" text
            string response = www.downloadHandler.text;
            if (response.Contains("\"status\":\""))
            {
                int startIndex = response.IndexOf("\"status\":\"") + 10;
                if (response.Substring(startIndex, 4).Equals("done"))
                {
                    SetButtonText(GetTelegramCodeButton, "Sent");
                    yield return new WaitForSeconds(2f);
                }
            }
            SetButtonText(GetTelegramCodeButton, "Получить другой код");
        }
    }
    
    public void TelegramCodeInputChanged(string newText)
    {
        if (string.IsNullOrEmpty(newText) || newText.Length < 5)
        {
            SendTelegramCodeButton.interactable = false;
        }
        else
        {
            SendTelegramCodeButton.interactable = true;
        }
    }

    private IEnumerator SendTelegramCode()
    {
        LoadingPanel.SetActive(true);
        SendTelegramCodeButton.interactable = false;
        SetButtonText(SendTelegramCodeButton, "Authorizing..");

        string jsonBody = "{\"auth_code\":\"" + TelegramCodeInput.text + "\"}";

        using UnityWebRequest www = new($"https://wappi.pro/tapi/sync/auth/code?profile_id={telegramProfileId}", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            string errorMsg = "Server Unavailable";
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                errorMsg = "Check internet connection";
            }
            else if (www.result == UnityWebRequest.Result.ProtocolError && www.downloadHandler != null)
            {
                string response = www.downloadHandler.text;
                if (response.Contains("\"detail\":") && response.Contains("\",\"uuid\":"))
                {
                    int startIndex = response.IndexOf("\"detail\":") + 10;
                    int endIndex = response.IndexOf("\",\"uuid\":");
                    errorMsg = response.Substring(startIndex, endIndex - startIndex);
                }
            }

            SetButtonText(SendTelegramCodeButton, errorMsg);
            yield return new WaitForSeconds(2f);
            SetButtonText(SendTelegramCodeButton, "Подтвердить код");

            if (TelegramCodeInput.text.Length >= 5)
                SendTelegramCodeButton.interactable = true;
        }
        else
        {
            string response = www.downloadHandler.text;

            if (response.Contains("\"detail\":\""))
            {
                int startIndex = response.IndexOf("\"detail\":\"") + 10;

                if (response.Substring(startIndex, 12).Equals("auth_success"))
                {
                    SetButtonText(SendTelegramCodeButton, "Authorization Complete");

                    if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
                    _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());

                    yield return new WaitForSeconds(2f);
                }
                else
                {
                    SetButtonText(SendTelegramCodeButton, "Authorization Failed");
                    yield return new WaitForSeconds(2f);
                }
            }
            else
            {
                SetButtonText(SendTelegramCodeButton, "Authorization Failed");
                yield return new WaitForSeconds(2f);
            }

            SetButtonText(SendTelegramCodeButton, "Подтвердить код");
        }

        LoadingPanel.SetActive(false);
    }
    public void CloseTelegramCodePanel()
    {
        TelegramCodePanel.SetActive(false);

        TelegramNumberInput.gameObject.SetActive(true);
        GetTelegramCodeButton.gameObject.SetActive(true);
        TelegramCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        TelegramCodeInput.gameObject.SetActive(false);
        SendTelegramCodeButton.gameObject.SetActive(false);
        SetTelegramCodeEntryTexts(false);

        if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.gameObject.SetActive(false);

        TelegramCodeInput.text = "";
        ForceRebuildLayout(TelegramAuth);
    }

    public void ChangeTelegramNumber()
    {
        TelegramNumberInput.gameObject.SetActive(true);
        GetTelegramCodeButton.gameObject.SetActive(true);
        SetButtonText(GetTelegramCodeButton, "Получить код");
        TelegramCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        TelegramCodeInput.gameObject.SetActive(false);
        SendTelegramCodeButton.gameObject.SetActive(false);
        SetTelegramCodeEntryTexts(false);

        if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.gameObject.SetActive(false);

        TelegramCodeInput.text = "";

        if (TelegramNumberInput.text.Length >= 10 && !TelegramCodeTimer.activeSelf)
            GetTelegramCodeButton.interactable = true;

        ForceRebuildLayout(TelegramAuth);
    }

    private IEnumerator GetTelegramProfileStatus()
    {
        bool authorized = false;

        while (!authorized)
        {
            using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/tapi/sync/get/status?profile_id={telegramProfileId}");

            www.SetRequestHeader("Authorization", wappiAuthToken);

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string response = www.downloadHandler.text;

                if (response.Contains("\"authorized\":"))
                {
                    int startIndex = response.IndexOf("\"authorized\":") + 13;
                    int endIndex = response.IndexOf(",\"authorized_at\":");
                    int lenght = endIndex - startIndex;

                    if (response.Substring(startIndex, lenght).Equals("true"))
                    {
                        authorized = true;

                        if (response.Contains("\"phone\":") && response.Contains("\",\"platform\":"))
                        {
                            startIndex = response.IndexOf("\"phone\":") + 9;
                            endIndex = response.IndexOf("\",\"platform\":");
                            lenght = endIndex - startIndex;

                            TelegramNumberInput.text = response.Substring(startIndex, lenght);
                        }

                        // Show checkmark inside QR box, then navigate
                        yield return StartCoroutine(ShowAuthSuccess(TelegramAuth, TelegramAuthSuccessPanel));
                        telegramAuthCompleted = true;
                    }
                }
            }

            yield return new WaitForSeconds(5f);
        }
    }

    //////////////////////////////////////////////////////////SENT FROM OTHER CLASSES//////////////////////////////////////////////////////////

    public void GetCreateWhatsappProfile(string whatsappProfileId)
    {
        StartCoroutine(CreateWhatsappProfile(whatsappProfileId, false));
    }

    public void GetCreateTelegramProfile(string telegramProfileId)
    {
        StartCoroutine(CreateTelegramProfile(telegramProfileId, false));
    }

    public void GetDeleteWhatsappProfile(string whatsappProfileId)
    {
        StartCoroutine(DeleteWhatsappProfile(whatsappProfileId, false));
    }

    public void GetDeleteTelegramProfile(string telegramProfileId)
    {
        StartCoroutine(DeleteTelegramProfile(telegramProfileId, false));
    }


    public void GetCreateWhatsappWorkflow()
    {
        StartCoroutine(CreateWhatsappWorkflowFromEdit());
    }

    public void GetCreateTelegramWorkflow()
    {
        StartCoroutine(CreateTelegramWorkflowFromEdit());
    }

    public void GetEnableWhatsappWorkflow(string whatsappWorkflowId, bool enabled)
    {
        StartCoroutine(EnableWhatsappWorkflow(whatsappWorkflowId, enabled));
    }

    public void GetEnableTelegramWorkflow(string telegramWorkflowId, bool enabled)
    {
        StartCoroutine(EnableTelegramWorkflow(telegramWorkflowId, enabled));
    }

    public void GetDeleteWhatsappWorkflow(string whatsappWorkflowId)
    {
        StartCoroutine(DeleteWhatsappWorkflow(whatsappWorkflowId, false));
    }

    public void GetDeleteTelegramWorkflow(string telegramWorkflowId)
    {
        StartCoroutine(DeleteTelegramWorkflow(telegramWorkflowId, false));
    }


    public void GetSaveSettings(string whatsappWorkflowId, string telegramWorkflowId)
    {
        StartCoroutine(SaveWorkflows(whatsappWorkflowId, telegramWorkflowId));

        SaveSettings();
    }

    public void DeleteProfilesAndWorkflows(string whatsappProfileId, string telegramProfileId, string whatsappWorkflowId, string telegramWorkflowId)
    {
        StartCoroutine(DeleteWhatsappProfile(whatsappProfileId, true));
        StartCoroutine(DeleteTelegramProfile(telegramProfileId, true));

        StartCoroutine(DeleteWhatsappWorkflow(whatsappWorkflowId, true));
        StartCoroutine(DeleteTelegramWorkflow(telegramWorkflowId, true));
    }


    //////////////////////////////////////////////////////////SEND PROFILE REQUESTS//////////////////////////////////////////////////////////

    private IEnumerator CreateWhatsappProfile(string name, bool localId)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        using UnityWebRequest www = UnityWebRequest.Post($"https://wappi.pro/api/profile/add?name={name}", form);

        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {

        }
        else
        {
            string response = www.downloadHandler.text;

            if (response.Contains("\"profile_id\":") && response.Contains("\",\"status\":"))
            {
                int startIndex = response.IndexOf("\"profile_id\":") + 14;
                int endIndex = response.IndexOf("\",\"status\":");
                int lenght = endIndex - startIndex;

                if (localId)
                {
                    whatsappProfileId = response.Substring(startIndex, lenght);
                }
                else
                {
                    openBot.GetComponent<Bot>().whatsappProfileId = response.Substring(startIndex, lenght);
                }

                PlayerPrefs.SetString("lastCreatedWhatsappProfileId", response.Substring(startIndex, lenght));
                PlayerPrefs.SetInt("lastCreatedWhatsappProfileIdSaved", 0);
            }
        }

        LoadingPanel.SetActive(false);
    }

    private IEnumerator DeleteWhatsappProfile(string whatsappProfileId, bool localId)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        using UnityWebRequest www = UnityWebRequest.Post($"https://wappi.pro/api/profile/delete?profile_id={whatsappProfileId}", form);

        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            if (localId)
            {
                this.whatsappProfileId = "-1";
            }
            else
            {
                openBot.GetComponent<Bot>().whatsappProfileId = "-1";
                PlayerPrefs.SetString(openBot.name + "WhatsappProfileId", "-1");
            }

            PlayerPrefs.SetString("lastCreatedWhatsappProfileId", "-1");
            PlayerPrefs.SetInt("lastCreatedWhatsappProfileIdSaved", 1);
        }

        LoadingPanel.SetActive(false);
    }


    private IEnumerator CreateTelegramProfile(string name, bool localId)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        using UnityWebRequest www = UnityWebRequest.Post($"https://wappi.pro/tapi/profile/add?name={name}", form);

        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {

        }
        else
        {
            string response = www.downloadHandler.text;

            if (response.Contains("\"profile_id\":") && response.Contains("\",\"status\":"))
            {
                int startIndex = response.IndexOf("\"profile_id\":") + 14;
                int endIndex = response.IndexOf("\",\"status\":");
                int lenght = endIndex - startIndex;

                if (localId)
                {
                    telegramProfileId = response.Substring(startIndex, lenght);
                }
                else
                {
                    openBot.GetComponent<Bot>().telegramProfileId = response.Substring(startIndex, lenght);
                }

                PlayerPrefs.SetString("lastCreatedTelegramProfileId", response.Substring(startIndex, lenght));
                PlayerPrefs.SetInt("lastCreatedTelegramProfileIdSaved", 0);
            }
        }

        LoadingPanel.SetActive(false);
    }

    private IEnumerator DeleteTelegramProfile(string telegramProfileId, bool localId)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        using UnityWebRequest www = UnityWebRequest.Post($"https://wappi.pro/tapi/profile/delete?profile_id={telegramProfileId}", form);

        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            if (localId)
            {
                this.telegramProfileId = "-1";
            }
            else
            {
                openBot.GetComponent<Bot>().telegramProfileId = "-1";
                PlayerPrefs.SetString(openBot.name + "TelegramProfileId", "-1");
            }

            PlayerPrefs.SetString("lastCreatedTelegramProfileId", "-1");
            PlayerPrefs.SetInt("lastCreatedTelegramProfileIdSaved", 1);
        }

        LoadingPanel.SetActive(false);
    }


    //////////////////////////////////////////////////////////SEND WORKFLOW REQUESTS//////////////////////////////////////////////////////////

    private IEnumerator CreateWhatsappWorkflowFromStart(GameObject bot)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        form.AddField("Name", bot.GetComponent<Bot>().BotName != null ? bot.GetComponent<Bot>().BotName.text : "");
        form.AddField("BusinessType", businessTypes.TryGetById(selectedBusinessId, out var bt1) ? bt1.displayName : "");
        form.AddField("WhatsappProfileId", whatsappProfileId);

        form.AddField("Business", "");
        form.AddField("Prompt", "");
        form.AddField("ProductsList", "");
        form.AddField("ServicesList", "");

        using UnityWebRequest www = UnityWebRequest.Post("https://bagkz.app.n8n.cloud/webhook/CreateWhatsappWorkflow", form);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        // if (www.result != UnityWebRequest.Result.Success)
        {
            StartCoroutine(DeleteWhatsappProfile(whatsappProfileId, true));
        }
        else
        {
            bot.GetComponent<Bot>().active = true;
            PlayerPrefs.SetInt(bot.name + "Active", 1);
            bot.GetComponent<Bot>().Status.GetComponent<TextMeshProUGUI>().text = "Active";
            bot.GetComponent<Bot>().Status.GetComponent<TextMeshProUGUI>().color = new(0, 1, 0);

            bot.GetComponent<Bot>().EditButton.interactable = true;
            bot.GetComponent<Bot>().ActivationSwitch.interactable = true;


            string response = www.downloadHandler.text;

            if (response.Contains("\"id\":"))
            {
                int startIndex = response.IndexOf("\"id\":") + 6;
                int length = response.Length - 9;

                bot.GetComponent<Bot>().whatsappWorkflowId = response.Substring(startIndex, length);
                // PlayerPrefs.SetString(bot.name + "WhatsappWorkflowId", bot.GetComponent<Bot>().whatsappWorkflowId);
                PlayerPrefs.SetString(bot.name + "WhatsappProfileId", bot.GetComponent<Bot>().whatsappProfileId);
                PlayerPrefs.SetInt("lastCreatedWhatsappProfileIdSaved", 1);
            }
        }

        whatsappProfileId = "-1";

        LoadingPanel.SetActive(false);
    }

    private IEnumerator CreateWhatsappWorkflowFromEdit()
    {
        LoadingPanel.SetActive(true);

        openBotSettings.Saved.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "Saving..";
        openBotSettings.Saved.SetActive(true);


        string productsList = "";

        for (int i = 0; i < openBotSettings.ProductsParent.transform.childCount - 1; i++)
        {
            ProductCardView product = openBotSettings.ProductsParent.transform.GetChild(i).GetComponent<ProductCardView>();

            product.Name.Trim();
            if (!product.Name.Equals(""))
            {
                productsList += $"Product{i + 1}: {product.Name}" +
                    $"\nProduct{i + 1} Price: {product.Price}" +
                    $"\nProduct{i + 1} Description: {product.Description}"
                    + (i == openBotSettings.ProductsParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        string servicesList = "";

        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount - 1; i++)
        {
            ServiceCardView service = openBotSettings.ServicesParent.transform.GetChild(i).GetComponent<ServiceCardView>();

            service.Name.Trim();
            if (!service.Name.Equals(""))
            {
                servicesList += $"Service{i + 1}: {service.Name}" +
                    $"\nService{i + 1} Price: {service.Price}" +
                    $"\nService{i + 1} Description: {service.Description}"
                    + (i == openBotSettings.ServicesParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        WWWForm form = new();

        form.AddField("Name", openBotSettings.BotNameField.Value);
        form.AddField("BusinessType", openBotSettings.BusinessTypeDropdown.options[openBotSettings.BusinessTypeDropdown.value].text);
        form.AddField("WhatsappProfileId", openBot.GetComponent<Bot>().whatsappProfileId);

        form.AddField("Business", "About Business:\n" + openBotSettings.BusinessField.Value);
        form.AddField("Prompt", openBotSettings.PromptField.Value);
        form.AddField("ProductsList", "Products:\n" + productsList);
        form.AddField("ServicesList", "Services:\n" + servicesList);

        using UnityWebRequest www = UnityWebRequest.Post("https://bagkz.app.n8n.cloud/webhook/CreateWhatsappWorkflow", form);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            string response = www.downloadHandler.text;

            if (response.Contains("\"id\":"))
            {
                int startIndex = response.IndexOf("\"id\":") + 6;
                int length = response.Length - 9;

                openBot.GetComponent<Bot>().whatsappWorkflowId = response.Substring(startIndex, length);
                PlayerPrefs.SetString(openBot.name + "WhatsappWorkflowId", openBot.GetComponent<Bot>().whatsappWorkflowId);
                PlayerPrefs.SetInt("lastCreatedWhatsappProfileIdSaved", 1);

                CreateWhatsappWorkflowFromEditSuccess = true;
                Saved();
            }
        }

        LoadingPanel.SetActive(false);
    }

    private IEnumerator CreateTelegramWorkflowFromStart(GameObject bot)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        form.AddField("Name", bot.GetComponent<Bot>().BotName != null ? bot.GetComponent<Bot>().BotName.text : "");
        form.AddField("BusinessType", businessTypes.TryGetById(selectedBusinessId, out var bt2) ? bt2.displayName : "");
        form.AddField("TelegramProfileId", telegramProfileId);

        form.AddField("Business", "");
        form.AddField("Prompt", "");
        form.AddField("ProductsList", "");
        form.AddField("ServicesList", "");

        using UnityWebRequest www = UnityWebRequest.Post("https://bagkz.app.n8n.cloud/webhook/CreateTelegramWorkflow", form);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        // if (www.result != UnityWebRequest.Result.Success)
        {
            StartCoroutine(DeleteTelegramProfile(telegramProfileId, true));
        }
        else
        {
            bot.GetComponent<Bot>().active = true;
            PlayerPrefs.SetInt(bot.name + "Active", 1);
            bot.GetComponent<Bot>().Status.GetComponent<TextMeshProUGUI>().text = "Active";
            bot.GetComponent<Bot>().Status.GetComponent<TextMeshProUGUI>().color = new(0, 1, 0);

            bot.GetComponent<Bot>().EditButton.interactable = true;
            bot.GetComponent<Bot>().ActivationSwitch.interactable = true;


            string response = www.downloadHandler.text;

            if (response.Contains("\"id\":"))
            {
                int startIndex = response.IndexOf("\"id\":") + 6;
                int length = response.Length - 9;

                bot.GetComponent<Bot>().telegramWorkflowId = response.Substring(startIndex, length);
                PlayerPrefs.SetString(bot.name + "TelegramWorkflowId", bot.GetComponent<Bot>().telegramWorkflowId);
                PlayerPrefs.SetString(bot.name + "TelegramProfileId", bot.GetComponent<Bot>().telegramProfileId);
                PlayerPrefs.SetInt("lastCreatedTelegramProfileIdSaved", 1);
            }
        }

        telegramProfileId = "-1";

        LoadingPanel.SetActive(false);
    }

    private IEnumerator CreateTelegramWorkflowFromEdit()
    {
        LoadingPanel.SetActive(true);

        openBotSettings.Saved.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "Saving..";
        openBotSettings.Saved.SetActive(true);


        string productsList = "";

        for (int i = 0; i < openBotSettings.ProductsParent.transform.childCount - 1; i++)
        {
            ProductCardView product = openBotSettings.ProductsParent.transform.GetChild(i).GetComponent<ProductCardView>();

            product.Name.Trim();
            if (!product.Name.Equals(""))
            {
                productsList += $"Product{i + 1}: {product.Name}" +
                    $"\nProduct{i + 1} Price: {product.Price}" +
                    $"\nProduct{i + 1} Description: {product.Description}"
                    + (i == openBotSettings.ProductsParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        string servicesList = "";

        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount - 1; i++)
        {
            ServiceCardView service = openBotSettings.ServicesParent.transform.GetChild(i).GetComponent<ServiceCardView>();

            service.Name.Trim();
            if (!service.Name.Equals(""))
            {
                servicesList += $"Service{i + 1}: {service.Name}" +
                    $"\nService{i + 1} Price: {service.Price}" +
                    $"\nService{i + 1} Description: {service.Description}"
                    + (i == openBotSettings.ServicesParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        WWWForm form = new();

        form.AddField("Name", openBotSettings.BotNameField.Value);
        form.AddField("BusinessType", openBotSettings.BusinessTypeDropdown.options[openBotSettings.BusinessTypeDropdown.value].text);
        form.AddField("TelegramProfileId", openBot.GetComponent<Bot>().telegramProfileId);

        form.AddField("Business", "About Business:\n" + openBotSettings.BusinessField.Value);
        form.AddField("Prompt", openBotSettings.PromptField.Value);
        form.AddField("ProductsList", "Products:\n" + productsList);
        form.AddField("ServicesList", "Services:\n" + servicesList);

        using UnityWebRequest www = UnityWebRequest.Post("https://bagkz.app.n8n.cloud/webhook/CreateTelegramWorkflow", form);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            string response = www.downloadHandler.text;

            if (response.Contains("\"id\":"))
            {
                int startIndex = response.IndexOf("\"id\":") + 6;
                int length = response.Length - 9;

                openBot.GetComponent<Bot>().telegramWorkflowId = response.Substring(startIndex, length);
                PlayerPrefs.SetString(openBot.name + "TelegramWorkflowId", openBot.GetComponent<Bot>().telegramWorkflowId);
                PlayerPrefs.SetInt("lastCreatedTelegramProfileIdSaved", 1);

                CreateTelegramWorkflowFromEditSuccess = true;
                Saved();
            }
        }

        LoadingPanel.SetActive(false);
    }

    private IEnumerator EnableWhatsappWorkflow(string id, bool enabled)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        using UnityWebRequest www = UnityWebRequest.Post($"https://bagkz.app.n8n.cloud/api/v1/workflows/{id}/" + (enabled ? "activate" : "deactivate"), form);

        www.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            EnableWhatsappWorkflowSaved = true;
            Saved();
        }

        LoadingPanel.SetActive(false);
    }

    private IEnumerator EnableTelegramWorkflow(string id, bool enabled)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        using UnityWebRequest www = UnityWebRequest.Post($"https://bagkz.app.n8n.cloud/api/v1/workflows/{id}/" + (enabled ? "activate" : "deactivate"), form);

        www.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            EnableTelegramWorkflowSaved = true;
            Saved();
        }

        LoadingPanel.SetActive(false);
    }

    private IEnumerator DeleteWhatsappWorkflow(string whatsappWorkflowId, bool deletingBot)
    {
        using UnityWebRequest whatsappRequest = UnityWebRequest.Delete($"https://bagkz.app.n8n.cloud/api/v1/workflows/{whatsappWorkflowId}");

        whatsappRequest.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);

        yield return whatsappRequest.SendWebRequest();

        if (!deletingBot)
        {
            if (whatsappRequest.result == UnityWebRequest.Result.Success)
            {
                openBot.GetComponent<Bot>().whatsappWorkflowId = "-1";
                PlayerPrefs.SetString(openBot.name + "WhatsappWorkflowId", "-1");
            }
        }
    }

    private IEnumerator DeleteTelegramWorkflow(string telegramWorkflowId, bool deletingBot)
    {
        using UnityWebRequest telegramRequest = UnityWebRequest.Delete($"https://bagkz.app.n8n.cloud/api/v1/workflows/{telegramWorkflowId}");

        telegramRequest.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);

        yield return telegramRequest.SendWebRequest();

        if (!deletingBot)
        {
            if (telegramRequest.result == UnityWebRequest.Result.Success)
            {
                openBot.GetComponent<Bot>().telegramWorkflowId = "-1";
                PlayerPrefs.SetString(openBot.name + "TelegramWorkflowId", "-1");
            }
        }
    }

    private IEnumerator SaveWorkflows(string whatsappWorkflowId, string telegramWorkflowId)
    {
        LoadingPanel.SetActive(true);

        openBotSettings.Saved.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "Saving..";
        openBotSettings.Saved.SetActive(true);


        string productsList = "";

        for (int i = 0; i < openBotSettings.ProductsParent.transform.childCount - 1; i++)
        {
            ProductCardView product = openBotSettings.ProductsParent.transform.GetChild(i).GetComponent<ProductCardView>();

            product.Name.Trim();
            if (!product.Name.Equals(""))
            {
                productsList += $"Product{i + 1}: {product.Name}" +
                    $"\nProduct{i + 1} Price: {product.Price}" +
                    $"\nProduct{i + 1} Description: {product.Description}"
                    + (i == openBotSettings.ProductsParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        string servicesList = "";

        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount - 1; i++)
        {
            ServiceCardView service = openBotSettings.ServicesParent.transform.GetChild(i).GetComponent<ServiceCardView>();

            service.Name.Trim();
            if (!service.Name.Equals(""))
            {
                servicesList += $"Service{i + 1}: {service.Name}" +
                    $"\nService{i + 1} Price: {service.Price}" +
                    $"\nService{i + 1} Description: {service.Description}"
                    + (i == openBotSettings.ServicesParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        WWWForm form = new();

        form.AddField("WhatsappWorkflowId", whatsappWorkflowId);
        form.AddField("TelegramWorkflowId", telegramWorkflowId);
        form.AddField("Name", openBotSettings.BotNameField.Value);
        form.AddField("BusinessType", openBotSettings.BusinessTypeDropdown.options[openBotSettings.BusinessTypeDropdown.value].text);
        form.AddField("Business", openBotSettings.BusinessField.Value);
        form.AddField("Prompt", openBotSettings.PromptField.Value);
        form.AddField("ProductsList", productsList);
        form.AddField("ServicesList", servicesList);


        EnableWhatsappWorkflowSaved = false;
        EditWhatsappWorkflowSaved = false;
        EnableTelegramWorkflowSaved = false;
        EditTelegramWorkflowSaved = false;


        if (openBot.GetComponent<Bot>().whatsappWorkflowId.Equals("-1"))
        {
            EnableWhatsappWorkflowSaved = true;
            EditWhatsappWorkflowSaved = true;
        }
        else
        {
            if (PlayerPrefs.GetInt(openBot.name + "isOnWhatsapp", 0) != (openBotSettings.WhatsappToggle.isOn ? 1 : 0))
            {
                StartCoroutine(EnableWhatsappWorkflow(openBot.GetComponent<Bot>().whatsappWorkflowId, openBotSettings.WhatsappToggle.isOn));
            }
            else
            {
                EnableWhatsappWorkflowSaved = true;
            }

            PlayerPrefs.SetInt(openBot.name + "isOnWhatsapp", openBotSettings.WhatsappToggle.isOn ? 1 : 0);


            using UnityWebRequest editWhatsappRequest = UnityWebRequest.Post("https://bagkz.app.n8n.cloud/webhook/EditWhatsappWorkflow", form);

            editWhatsappRequest.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);

            yield return editWhatsappRequest.SendWebRequest();

            if (editWhatsappRequest.result == UnityWebRequest.Result.Success)
            {
                EditWhatsappWorkflowSaved = true;
            }
        }


        if (openBot.GetComponent<Bot>().telegramWorkflowId.Equals("-1"))
        {
            EnableTelegramWorkflowSaved = true;
            EditTelegramWorkflowSaved = true;
        }
        else
        {
            if (PlayerPrefs.GetInt(openBot.name + "isOnTelegram", 0) != (openBotSettings.TelegramToggle.isOn ? 1 : 0))
            {
                StartCoroutine(EnableTelegramWorkflow(openBot.GetComponent<Bot>().telegramWorkflowId, openBotSettings.TelegramToggle.isOn));
            }
            else
            {
                EnableTelegramWorkflowSaved = true;
            }

            PlayerPrefs.SetInt(openBot.name + "isOnTelegram", openBotSettings.TelegramToggle.isOn ? 1 : 0);


            using UnityWebRequest editTelegramRequest = UnityWebRequest.Post("https://bagkz.app.n8n.cloud/webhook/EditTelegramWorkflow", form);

            editTelegramRequest.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);

            yield return editTelegramRequest.SendWebRequest();

            if (editTelegramRequest.result == UnityWebRequest.Result.Success)
            {
                EditTelegramWorkflowSaved = true;
            }
        }


        Saved();
    }

    private void Saved()
    {
        if (EditWhatsappWorkflowSaved && EnableWhatsappWorkflowSaved && EditTelegramWorkflowSaved && EnableTelegramWorkflowSaved ||
            CreateWhatsappWorkflowFromEditSuccess || CreateTelegramWorkflowFromEditSuccess)
        {
            LoadingPanel.SetActive(false);

            StartCoroutine(ShowSavedPanel());

            if (CreateWhatsappWorkflowFromEditSuccess)
            {
                CreateWhatsappWorkflowFromEditSuccess = false;
            }

            if (CreateTelegramWorkflowFromEditSuccess)
            {
                CreateTelegramWorkflowFromEditSuccess = false;
            }

            if (EditWhatsappWorkflowSaved && EnableWhatsappWorkflowSaved && EditTelegramWorkflowSaved && EnableTelegramWorkflowSaved)
            {
                EditWhatsappWorkflowSaved = false;
                EnableWhatsappWorkflowSaved = false;
                EditTelegramWorkflowSaved = false;
                EnableTelegramWorkflowSaved = false;
            }
        }
    }

    private IEnumerator ShowSavedPanel()
    {
        openBotSettings.Saved.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "Saved";

        yield return new WaitForSeconds(2f);

        openBotSettings.Saved.SetActive(false);
    }


    //////////////////////////////////////////////////////////SEND TO TELEGRAM REQUESTS//////////////////////////////////////////////////////////

    private IEnumerator SendToTelegram(string message)
    {
        if (message != "")
        {
            string url = "https://api.telegram.org/bot8435792686:AAHSSN6RblmlO-4Do2UDPJlLRNwR6zCgnpI/sendMessage";
            WWWForm form = new();
            form.AddField("chat_id", "1038376805");
            form.AddField("text", message);

            using UnityWebRequest www = UnityWebRequest.Post(url, form);
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Ошибка отправки: " + www.error);
            }
            else
            {
                Debug.Log("Сообщение отправлено!");
            }
        }
    }

    public void OnPickFileButtonPressed()
    {
        pdf = NativeFilePicker.ConvertExtensionToFileType("pdf"); // Returns "application/pdf" on Android and "com.adobe.pdf" on iOS
        txt = NativeFilePicker.ConvertExtensionToFileType("txt");

        NativeFilePicker.PickFile((path) =>
        {
            if (path == null)
            {
                Debug.Log("Operation cancelled");
            }
            else
            {
                Debug.Log("Picked file path: " + path);
                // Start the upload process using the file path
                //StartCoroutine(UploadFile(path));
            }
        }, null); // 'null' allows all file types
    }


    private void PickFile()
    {
        pdf = NativeFilePicker.ConvertExtensionToFileType("pdf"); // Returns "application/pdf" on Android and "com.adobe.pdf" on iOS
        txt = NativeFilePicker.ConvertExtensionToFileType("txt");
        //video = NativeFilePicker.ConvertExtensionToFileType("image");
        rtf = NativeFilePicker.ConvertExtensionToFileType("rtf");
        xml = NativeFilePicker.ConvertExtensionToFileType("xml");
        csv = NativeFilePicker.ConvertExtensionToFileType("csv");
        xls = NativeFilePicker.ConvertExtensionToFileType("xls");
        xlsx = NativeFilePicker.ConvertExtensionToFileType("xlsx");
        doc = "com.microsoft.word.doc";
        docx = "org.openxmlformats.wordprocessingml.document";


        //if (Input.mousePosition.x < Screen.width / 3)
        //{
        PickMediaFile();
        //}
        //else if (Input.mousePosition.x < Screen.width * 2 / 3)
        //{
        //    PickPDFFile();
        //}
        //else
        //{
        //    PickCreateAllTXTFile();
        //}
    }

    private void PickMediaFile()
    {
        #if UNITY_ANDROID
		// Use MIMEs on Android
        string[] fileTypes = new string[] { pdf, txt, rtf, xml, csv, xls, xlsx, doc, docx };
        #else
        // Use UTIs on iOS
        string[] fileTypes = new string[] { pdf, txt, rtf, xml, csv, xls, xlsx, doc, docx };
        //string[] fileTypes = new string[] { "public.image", "public.movie", pdf, txt, rtf };

#endif

        // Pick image(s) and/or video(s)
        NativeFilePicker.PickMultipleFiles((paths) =>
        {
            if (paths == null)
            {
            }
            else
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    Debug.Log("Picked file: " + paths[i]);
                    StartCoroutine(UploadFile(paths[i]));
                }
            }
        }, fileTypes);
    }

    private void PickPDFFile()
    {
        // Pick a PDF file
        NativeFilePicker.PickFile((path) =>
        {
            if (path == null)
                Debug.Log("Operation cancelled");
            else
                Debug.Log("Picked file: " + path);
        }, new string[] { video });
    }

    private void PickCreateAllTXTFile()
    {
        //    // Create a dummy text file
        //    string filePath = Path.Combine(Application.temporaryCachePath, "test.txt");
        //    File.WriteAllText(filePath, "Hello world!");

        //    // Export the file
        //    NativeFilePicker.ExportFile(filePath, (success) => Debug.Log("File exported: " + success));
        // Pick a PDF file
        NativeFilePicker.PickFile((path) =>
        {
            if (path == null)
                Debug.Log("Operation cancelled");
            else
                Debug.Log("Picked file: " + path);
        }, new string[] { pdf, txt, video });
    }

    private IEnumerator UploadFile(string filePath)
    {
        //string originalName = "MyImportantDocument.pdf";
        //string path = Path.Combine(filePath, originalName);

        string uploadUrl = "https://bagkz.app.n8n.cloud/webhook-test/UploadFile";

        byte[] fileData = File.ReadAllBytes(filePath);
        string fileName = Path.GetFileName(filePath);

        WWWForm form = new();

        //form.AddField("Name", fileName);
        //form.AddBinaryData("data", fileData, fileName, "application/pdf"); // Added mimeType here
        form.AddBinaryData("data", fileData, fileName); // Added mimeType here


        using UnityWebRequest www = UnityWebRequest.Post(uploadUrl, form);

        yield return www.SendWebRequest();


        if (www.result != UnityWebRequest.Result.Success)
        {
        }
        else
        {
        }
    }

    //Gallery
    private void PickGalleryFile()
    {
        //if (Input.mousePosition.x < Screen.width / 3)
        //{
        //    // Take a screenshot and save it to Gallery/Photos
        //    StartCoroutine(TakeScreenshotAndSave());
        //}
        //else
        //{
            // Don't attempt to pick media from Gallery/Photos if
            // another media pick operation is already in progress
            if (NativeGallery.IsMediaPickerBusy())
                return;

            PickImageOrVideo();

            //if (Input.mousePosition.x < Screen.width * 2 / 3)
            //{
            //    // Pick a PNG image from Gallery/Photos
            //    // If the selected image's width and/or height is greater than 512px, down-scale the image
            //    PickImage(512);
            //}
            //else
            //{
            //    // Pick a video from Gallery/Photos
            //    PickVideo();
            //}
        //}
    }

    // Example code doesn't use this function but it is here for reference
    private void PickImageOrVideo()
    {
        if (NativeGallery.CanSelectMultipleMediaTypesFromGallery())
        {
            NativeGallery.GetMixedMediaFromGallery((path) =>
            {
                Debug.Log("Media path: " + path);
                if (path != null)
                {
                    // Determine if user has picked an image, video or neither of these
                    switch (NativeGallery.GetMediaTypeOfFile(path))
                    {
                        case NativeGallery.MediaType.Image: Debug.Log("Picked image"); break;
                        case NativeGallery.MediaType.Video: Debug.Log("Picked video"); break;
                        default: Debug.Log("Probably picked something else"); break;
                    }
                }
            }, NativeGallery.MediaType.Image | NativeGallery.MediaType.Video, "Select an image or video");
        }
    }




    private IEnumerator TakeScreenshotAndSave()
    {
        yield return new WaitForEndOfFrame();

        Texture2D ss = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        ss.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        ss.Apply();

        // Save the screenshot to Gallery/Photos
        NativeGallery.SaveImageToGallery(ss, "GalleryTest", "Image.png", (success, path) => Debug.Log("Media save result: " + success + " " + path));

        // To avoid memory leaks
        Destroy(ss);
    }

    private void PickImage(int maxSize)
    {
        NativeGallery.GetImageFromGallery((path) =>
        {
            Debug.Log("Image path: " + path);
            if (path != null)
            {
                // Create Texture from selected image
                Texture2D texture = NativeGallery.LoadImageAtPath(path, maxSize);
                if (texture == null)
                {
                    Debug.Log("Couldn't load texture from " + path);
                    return;
                }

                // Assign texture to a temporary quad and destroy it after 5 seconds
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 2.5f;
                quad.transform.forward = Camera.main.transform.forward;
                quad.transform.localScale = new Vector3(1f, texture.height / (float)texture.width, 1f);

                Material material = quad.GetComponent<Renderer>().material;
                if (!material.shader.isSupported) // happens when Standard shader is not included in the build
                    material.shader = Shader.Find("Legacy Shaders/Diffuse");

                material.mainTexture = texture;

                Destroy(quad, 5f);

                // If a procedural texture is not destroyed manually, 
                // it will only be freed after a scene change
                Destroy(texture, 5f);
            }
        });
    }

    private void PickVideo()
    {
        NativeGallery.GetVideoFromGallery((path) =>
        {
            Debug.Log("Video path: " + path);
            if (path != null)
            {
                // Play the selected video
                Handheld.PlayFullScreenMovie("file://" + path);
            }
        }, "Select a video");
    }

     private IEnumerator GetWhatsappMesseges()
     {
         LoadingPanel.SetActive(true);
         WWWForm form = new();
     
         // using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/messages/all/get?profile_id=03c9cb54-8e39");
         // using UnityWebRequest www = UnityWebRequest.Post($"https://wappi.pro/api/sync/chats/get?profile_id=03c9cb54-8e39", form);
         // using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/chats/filter?profile_id=ecd897e3-d1c8");
         // using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/messages/get?profile_id=ecd897e3-d1c8&chat_id=77026998844@c.us");
         
         // using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/message/media/download?profile_id=cf87cc87-94ff&message_id=3ABD17EC4D6379CAB94F");
         using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/contact/info?profile_id=af80627e-6d9d&user_id=77472714618@c.us");


         www.SetRequestHeader("Authorization", wappiAuthToken);
     
         yield return www.SendWebRequest();
     
     
         if (www.result != UnityWebRequest.Result.Success)
         {
             
         }
         else
         {
             var text = www.downloadHandler.text;
             System.IO.File.WriteAllText(
                 Application.persistentDataPath + "/response.txt",
                 text
             );
             Debug.Log("Saved to: " + Application.persistentDataPath);
             
             
             string response = www.downloadHandler.text;
             
             print(response);
         }
     
         LoadingPanel.SetActive(false);
     }
}
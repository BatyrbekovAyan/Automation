using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text;
using System;
using System.IO;

public class Manager : MonoBehaviour
{
    #region
    [SerializeField] private GameObject MainPage;
    [SerializeField] private GameObject WhatsappAuth;
    [SerializeField] private GameObject TelegramAuth;
    [SerializeField] private GameObject Confirmation;
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
    [SerializeField] private GameObject WhatsappCodeTimer;
    [SerializeField] private GameObject TelegramCodeTimer;
    [SerializeField] private GameObject WhatsappCodeSendingMessage;
    [SerializeField] private GameObject TelegramCodeSendingMessage;
    [SerializeField] public GameObject LoadingPanel;

    [SerializeField] private Button WhatsappAuthContinueButton;
    [SerializeField] private Button WhatsappAuthBackButton;
    [SerializeField] private Button TelegramAuthContinueButton;
    [SerializeField] private Button TelegramAuthBackButton;
    [SerializeField] private Button OpenWhatsappQRPanelButton;
    [SerializeField] private Button OpenWhatsappCodePanelButton;
    [SerializeField] private Button CloseWhatsappQRPanelButton;
    [SerializeField] private Button CloseWhatsappCodePanelButton;
    [SerializeField] private Button GetWhatsappCodeButton;
    [SerializeField] private Button OpenTelegramQRPanelButton;
    [SerializeField] private Button OpenTelegramCodePanelButton;
    [SerializeField] private Button CloseTelegramQRPanelButton;
    [SerializeField] private Button CloseTelegramCodePanelButton;
    [SerializeField] private Button GetTelegramCodeButton;
    [SerializeField] private Button SendTelegramCodeButton;
    [SerializeField] private Button SaveButton;

    [SerializeField] private TMP_InputField WhatsappNumberInput;
    [SerializeField] private TMP_InputField TelegramNumberInput;
    [SerializeField] private TMP_InputField TelegramCodeInput;

    [SerializeField] private RawImage WhatsappQRCodeImage;
    [SerializeField] private RawImage TelegramQRCodeImage;
    [SerializeField] private List<Button> BusinessTypesList = new();

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

    private GameObject businessType;
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

    public static string wappiAuthToken => Secrets.Data.wappiAuthToken;
    public static string n8nAPIKey => Secrets.Data.n8nAPIKey;

    private string apiUrl => Secrets.Data.greenApi.apiUrl;
    private string idInstance => Secrets.Data.greenApi.idInstance;
    private string apiTokenInstance => Secrets.Data.greenApi.apiTokenInstance;

    public static GameObject BotSettingsParentStatic;
    public static Manager Instance;

    public static BotSettings openBotSettings;
    public static GameObject openBot;

    [SerializeField] private Button ChatsButton;
    [SerializeField] private Button SettingsButton;
    [SerializeField] private GameObject ChatsPanel;

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

        businessType = BusinessTypesList[0].gameObject;
        businessButtonDefaultColor = businessType.GetComponent<Image>().color;

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

        // Add Bot Form — row buttons
        if (platformRowButton != null) platformRowButton.onClick.AddListener(OpenPlatformSelector);
        if (botNameRowButton != null) botNameRowButton.onClick.AddListener(OpenBotNameInput);
        if (businessTypeRowButton != null) businessTypeRowButton.onClick.AddListener(OpenBusinessSelector);
        if (descriptionRowButton != null) descriptionRowButton.onClick.AddListener(OpenDescriptionInput);

        // Add Bot Form — platform selector
        if (whatsappOptionButton != null) whatsappOptionButton.onClick.AddListener(() => SelectPlatform(1));
        if (telegramOptionButton != null) telegramOptionButton.onClick.AddListener(() => SelectPlatform(2));
        if (bothOptionButton != null) bothOptionButton.onClick.AddListener(() => SelectPlatform(3));

        // Add Bot Form — create button
        if (createBotFormButton != null)
        {
            createBotFormButton.onClick.AddListener(() => StartCoroutine(CreateBotFromForm()));
            createBotFormButton.interactable = false;
        }

        // Auth panels — WhatsApp
        if (WhatsappAuthContinueButton != null)
        {
            WhatsappAuthContinueButton.onClick.AddListener(() =>
            {
                whatsappAuthCompleted = true;
                WhatsappAuth.SetActive(false);
            });
        }
        if (WhatsappAuthBackButton != null) WhatsappAuthBackButton.onClick.AddListener(CancelBotCreation);

        if (OpenWhatsappQRPanelButton != null) OpenWhatsappQRPanelButton.onClick.AddListener(() => StartCoroutine(OpenWhatsappQRPanel()));
        if (OpenWhatsappCodePanelButton != null) OpenWhatsappCodePanelButton.onClick.AddListener(OpenWhatsappCodePanel);
        if (CloseWhatsappQRPanelButton != null) CloseWhatsappQRPanelButton.onClick.AddListener(CloseWhatsappQRPanel);
        if (CloseWhatsappCodePanelButton != null) CloseWhatsappCodePanelButton.onClick.AddListener(CloseWhatsappCodePanel);
        if (GetWhatsappCodeButton != null) GetWhatsappCodeButton.onClick.AddListener(() => StartCoroutine(GetWhatsappCode()));

        // Auth panels — Telegram
        if (TelegramAuthContinueButton != null)
        {
            TelegramAuthContinueButton.onClick.AddListener(() =>
            {
                telegramAuthCompleted = true;
                TelegramAuth.SetActive(false);
            });
        }
        if (TelegramAuthBackButton != null) TelegramAuthBackButton.onClick.AddListener(CancelBotCreation);

        if (OpenTelegramQRPanelButton != null) OpenTelegramQRPanelButton.onClick.AddListener(() => StartCoroutine(OpenTelegramQRPanel()));
        if (OpenTelegramCodePanelButton != null) OpenTelegramCodePanelButton.onClick.AddListener(OpenTelegramCodePanel);
        if (CloseTelegramQRPanelButton != null) CloseTelegramQRPanelButton.onClick.AddListener(CloseTelegramQRPanel);
        if (CloseTelegramCodePanelButton != null) CloseTelegramCodePanelButton.onClick.AddListener(CloseTelegramCodePanel);
        if (GetTelegramCodeButton != null) GetTelegramCodeButton.onClick.AddListener(() => StartCoroutine(GetTelegramCode()));
        if (SendTelegramCodeButton != null) SendTelegramCodeButton.onClick.AddListener(() => StartCoroutine(SendTelegramCode()));

        // Auth input fields
        if (WhatsappNumberInput != null) WhatsappNumberInput.onValueChanged.AddListener(WhatsappNumberInputChanged);
        if (TelegramNumberInput != null) TelegramNumberInput.onValueChanged.AddListener(TelegramNumberInputChanged);
        if (TelegramCodeInput != null) TelegramCodeInput.onValueChanged.AddListener(TelegramCodeInputChanged);

        // Business type buttons
        foreach (Button business in BusinessTypesList)
        {
            business.onClick.AddListener(() => ChooseBusiness(business));
        }

        // Other
        if (ChatsButton != null) ChatsButton.onClick.AddListener(OpenChatsPanel);
        if (SettingsButton != null) SettingsButton.onClick.AddListener(() => StartCoroutine(GetWhatsappMesseges()));

        // Initialize popups as hidden
        if (platformSelectorPanel != null) platformSelectorPanel.SetActive(false);
        if (botNameInputPanel != null) botNameInputPanel.SetActive(false);
        if (businessSelectorPanel != null) businessSelectorPanel.SetActive(false);
        if (descriptionInputPanel != null) descriptionInputPanel.SetActive(false);

        // Wire overlay background tap + close button (✕) to dismiss popups
        if (platformSelectorPanel != null)
        {
            Button overlayBtn = platformSelectorPanel.GetComponent<Button>();
            if (overlayBtn != null) overlayBtn.onClick.AddListener(ClosePlatformSelector);
            Button closeBtn = platformSelectorPanel.transform.Find("Content/CloseButton")?.GetComponent<Button>();
            if (closeBtn != null) closeBtn.onClick.AddListener(ClosePlatformSelector);
        }
        if (botNameInputPanel != null)
        {
            Button overlayBtn = botNameInputPanel.GetComponent<Button>();
            if (overlayBtn != null) overlayBtn.onClick.AddListener(CloseBotNameInput);
            Button closeBtn = botNameInputPanel.transform.Find("Content/CloseButton")?.GetComponent<Button>();
            if (closeBtn != null) closeBtn.onClick.AddListener(CloseBotNameInput);
        }
        if (businessSelectorPanel != null)
        {
            Button overlayBtn = businessSelectorPanel.GetComponent<Button>();
            if (overlayBtn != null) overlayBtn.onClick.AddListener(CloseBusinessSelector);
            Button closeBtn = businessSelectorPanel.transform.Find("Content/CloseButton")?.GetComponent<Button>();
            if (closeBtn != null) closeBtn.onClick.AddListener(CloseBusinessSelector);
        }
        if (descriptionInputPanel != null)
        {
            Button overlayBtn = descriptionInputPanel.GetComponent<Button>();
            if (overlayBtn != null) overlayBtn.onClick.AddListener(CloseDescriptionInput);
            Button closeBtn = descriptionInputPanel.transform.Find("Content/CloseButton")?.GetComponent<Button>();
            if (closeBtn != null) closeBtn.onClick.AddListener(CloseDescriptionInput);
        }

        // Wire popup confirm/cancel buttons by name
        if (botNameInputPanel != null)
        {
            Button confirmName = botNameInputPanel.transform.Find("Content/ConfirmButton")?.GetComponent<Button>();
            Button cancelName = botNameInputPanel.transform.Find("Content/CancelButton")?.GetComponent<Button>();
            if (confirmName != null) confirmName.onClick.AddListener(ConfirmBotName);
            if (cancelName != null) cancelName.onClick.AddListener(CloseBotNameInput);
        }
        if (descriptionInputPanel != null)
        {
            Button confirmDesc = descriptionInputPanel.transform.Find("Content/ConfirmButton")?.GetComponent<Button>();
            Button cancelDesc = descriptionInputPanel.transform.Find("Content/CancelButton")?.GetComponent<Button>();
            if (confirmDesc != null) confirmDesc.onClick.AddListener(ConfirmDescription);
            if (cancelDesc != null) cancelDesc.onClick.AddListener(CloseDescriptionInput);
        }
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

                recreatedBot.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Name", "");
                recreatedBot.transform.GetChild(1).GetComponent<Toggle>().isOn = PlayerPrefs.GetInt(recreatedBot.name + "isOn", 1) == 1;
                recreatedBot.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Status", "");
                recreatedBot.GetComponent<Bot>().active = PlayerPrefs.GetInt(recreatedBot.name + "Active", 0) == 1;
                recreatedBot.GetComponent<Bot>().whatsappProfileId = PlayerPrefs.GetString(recreatedBot.name + "WhatsappProfileId", "-1");
                recreatedBot.GetComponent<Bot>().telegramProfileId = PlayerPrefs.GetString(recreatedBot.name + "TelegramProfileId", "-1");
                recreatedBot.GetComponent<Bot>().whatsappWorkflowId = PlayerPrefs.GetString(recreatedBot.name + "WhatsappWorkflowId", "-1");
                recreatedBot.GetComponent<Bot>().telegramWorkflowId = PlayerPrefs.GetString(recreatedBot.name + "TelegramWorkflowId", "-1");

                BotSettings recreatedBotSettings = Instantiate(BotSettings, new Vector3(BotSettings.transform.position.x + Screen.width / 2, BotSettings.transform.position.y + Screen.height / 2, 0), BotSettings.transform.rotation, BotSettingsParent.transform).GetComponent<BotSettings>();

                recreatedBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Name", "");
                recreatedBotSettings.WhatsappToggle.isOn = PlayerPrefs.GetInt(recreatedBot.name + "isOnWhatsapp", 1) == 1;
                recreatedBotSettings.TelegramToggle.isOn = PlayerPrefs.GetInt(recreatedBot.name + "isOnTelegram", 1) == 1;
                recreatedBotSettings.BusinessTypeDropdown.value = PlayerPrefs.GetInt(recreatedBot.name + "BusinessType", 0);
                recreatedBotSettings.WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "WhatsappNumber", "");
                recreatedBotSettings.TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "TelegramNumber", "");

                recreatedBotSettings.WhatsappNumberButton.transform.parent.gameObject.SetActive(!recreatedBotSettings.WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""));
                recreatedBotSettings.TelegramNumberButton.transform.parent.gameObject.SetActive(!recreatedBotSettings.TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""));

                recreatedBotSettings.BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Business", "");
                recreatedBotSettings.PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Prompt", "");

                int ProductsNumber = PlayerPrefs.GetInt(recreatedBot.name + "ProductsNumber", 0);
                for (int p = 0; p < ProductsNumber; p++)
                {
                    Product recreatedProduct = Instantiate(ProductPrefab, ProductPrefab.transform.position, ProductPrefab.transform.rotation, recreatedBotSettings.AddProductButton.transform.parent.parent).GetComponent<Product>();

                    recreatedProduct.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Product" + p, "");
                    recreatedProduct.PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Product" + p + "Price", "");
                    recreatedProduct.DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Product" + p + "Description", "");
                }

                recreatedBotSettings.AddProductButton.transform.parent.SetAsLastSibling();


                int ServicesNumber = PlayerPrefs.GetInt(recreatedBot.name + "ServicesNumber", 0);
                for (int s = 0; s < ServicesNumber; s++)
                {
                    Service recreatedService = Instantiate(ServicePrefab, ServicePrefab.transform.position, ServicePrefab.transform.rotation, recreatedBotSettings.AddServiceButton.transform.parent.parent).GetComponent<Service>();

                    recreatedService.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Service" + s, "");
                    recreatedService.PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Service" + s + "Price", "");
                    recreatedService.DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(recreatedBot.name + "Service" + s + "Description", "");
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
            StartCoroutine(DeleteTelegramProfile(PlayerPrefs.GetString("lastCreatedTelegramProfileId", "-1"), true));
        }
    }

    public void SaveSettings()
    {
        PlayerPrefs.SetString(openBot.name + "Name", openBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        openBot.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = openBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text;

        PlayerPrefs.SetInt(openBot.name + "BusinessType", openBotSettings.BusinessTypeDropdown.value);
        //PlayerPrefs.SetInt(openBot.name + "isOnWhatsapp", openBotSettings.WhatsappToggle.isOn ? 1 : 0);
        //PlayerPrefs.SetInt(openBot.name + "isOnTelegram", openBotSettings.TelegramToggle.isOn ? 1 : 0);

        PlayerPrefs.SetString(openBot.name + "WhatsappNumber", openBotSettings.WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        PlayerPrefs.SetString(openBot.name + "TelegramNumber", openBotSettings.TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);

        openBotSettings.WhatsappNumberButton.transform.parent.gameObject.SetActive(!openBotSettings.WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""));
        openBotSettings.TelegramNumberButton.transform.parent.gameObject.SetActive(!openBotSettings.TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""));


        PlayerPrefs.SetString(openBot.name + "Business", openBotSettings.BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        openBotSettings.BusinessInput.text = "";

        PlayerPrefs.SetString(openBot.name + "Prompt", openBotSettings.PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        openBotSettings.PromptInput.text = "";


        for (int i = 0; i < openBotSettings.ProductsParent.transform.childCount - 1; i++)
        {
            Transform product = openBotSettings.ProductsParent.transform.GetChild(i);

            product.GetComponent<Product>().ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Trim();
            if (!product.GetComponent<Product>().ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""))
            {
                PlayerPrefs.SetString(openBot.name + "Product" + i, product.GetComponent<Product>().ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
                PlayerPrefs.SetString(openBot.name + "Product" + i + "Price", product.GetComponent<Product>().PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
                PlayerPrefs.SetString(openBot.name + "Product" + i + "Description", product.GetComponent<Product>().DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
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

            service.GetComponent<Service>().ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Trim();
            if (!service.GetComponent<Service>().ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""))
            {
                PlayerPrefs.SetString(openBot.name + "Service" + i, service.GetComponent<Service>().ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
                PlayerPrefs.SetString(openBot.name + "Service" + i + "Price", service.GetComponent<Service>().PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
                PlayerPrefs.SetString(openBot.name + "Service" + i + "Description", service.GetComponent<Service>().DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
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
        openBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "Name", "");
        openBotSettings.WhatsappToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(openBot.name + "isOnWhatsapp", 1) == 1);
        openBotSettings.TelegramToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(openBot.name + "isOnTelegram", 1) == 1);
        openBotSettings.BusinessTypeDropdown.value = PlayerPrefs.GetInt(openBot.name + "BusinessType", 0);
        openBotSettings.WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "WhatsappNumber", "");
        openBotSettings.TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "TelegramNumber", "");

        openBotSettings.WhatsappNumberButton.transform.parent.gameObject.SetActive(!openBotSettings.WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""));
        openBotSettings.TelegramNumberButton.transform.parent.gameObject.SetActive(!openBotSettings.TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""));

        openBotSettings.BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "Business", "");
        openBotSettings.PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "Prompt", "");


        for (int p = 0; p < openBotSettings.ProductsParent.transform.childCount - 1; p++)
        {
            Destroy(openBotSettings.ProductsParent.transform.GetChild(p).gameObject);
        }

        int ProductsNumber = PlayerPrefs.GetInt(openBot.name + "ProductsNumber", 0);
        for (int p = 0; p < ProductsNumber; p++)
        {
            Product recreatedProduct = Instantiate(ProductPrefab, ProductPrefab.transform.position, ProductPrefab.transform.rotation, openBotSettings.AddProductButton.transform.parent.parent).GetComponent<Product>();

            recreatedProduct.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "Product" + p, "");
            recreatedProduct.PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "Product" + p + "Price", "");
            recreatedProduct.DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "Product" + p + "Description", "");
        }

        openBotSettings.AddProductButton.transform.parent.SetAsLastSibling();


        for (int s = 0; s < openBotSettings.ServicesParent.transform.childCount - 1; s++)
        {
            Destroy(openBotSettings.ServicesParent.transform.GetChild(s).gameObject);
        }

        int ServicesNumber = PlayerPrefs.GetInt(openBot.name + "ServicesNumber", 0);
        for (int s = 0; s < ServicesNumber; s++)
        {
            Service recreatedService = Instantiate(ServicePrefab, ServicePrefab.transform.position, ServicePrefab.transform.rotation, openBotSettings.AddServiceButton.transform.parent.parent).GetComponent<Service>();

            recreatedService.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "Service" + s, "");
            recreatedService.PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "Service" + s + "Price", "");
            recreatedService.DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PlayerPrefs.GetString(openBot.name + "Service" + s + "Description", "");
        }

        openBotSettings.AddServiceButton.transform.parent.SetAsLastSibling();
    }

    public void EnableSave()
    {
        bool settingsChanged = false;

        if (!openBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "Name", "")) ||
            openBotSettings.WhatsappToggle.isOn != (PlayerPrefs.GetInt(openBot.name + "isOnWhatsapp", 1) == 1) ||
            openBotSettings.TelegramToggle.isOn != (PlayerPrefs.GetInt(openBot.name + "isOnTelegram", 1) == 1) ||
            openBotSettings.BusinessTypeDropdown.value != PlayerPrefs.GetInt(openBot.name + "BusinessType", 0) ||
            !openBotSettings.WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "WhatsappNumber", "")) ||
            !openBotSettings.TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "TelegramNumber", "")) ||
            !openBotSettings.BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "Business", "")) ||
            !openBotSettings.PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "Prompt", "")))
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
                if (!openBotSettings.ProductsParent.transform.GetChild(p).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "Product" + p, "")) ||
                    !openBotSettings.ProductsParent.transform.GetChild(p).GetChild(2).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "Product" + p + "Price", "")) ||
                    !openBotSettings.ProductsParent.transform.GetChild(p).GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "Product" + p + "Description", "")))
                {
                    SaveButton.interactable = true;
                }
            }
        }

        else if (openBotSettings.ServicesParent.transform.childCount - 1 == PlayerPrefs.GetInt(openBot.name + "ServicesNumber", 0))
        {
            for (int s = 0; s < openBotSettings.ServicesParent.transform.childCount - 1; s++)
            {
                if (!openBotSettings.ServicesParent.transform.GetChild(s).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "Service" + s, "")) ||
                    !openBotSettings.ServicesParent.transform.GetChild(s).GetChild(2).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "Service" + s + "Price", "")) ||
                    !openBotSettings.ServicesParent.transform.GetChild(s).GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PlayerPrefs.GetString(openBot.name + "Service" + s + "Description", "")))
                {
                    SaveButton.interactable = true;
                }
            }
        }
    }


    //////////////////////////////////////////////////////////CREATE BOT//////////////////////////////////////////////////////////

    public void OpenMyBots()
    {
        MainPage.SetActive(false);
        BotsPage.SetActive(true);
    }

    // ── Add Bot Form — Popup Controllers ──

    public void OpenPlatformSelector()
    {
        platformSelectorPanel.SetActive(true);
    }

    public void ClosePlatformSelector()
    {
        platformSelectorPanel.SetActive(false);
    }

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
        botNameInputPanel.SetActive(true);
        if (botNamePopupInput != null)
        {
            botNamePopupInput.text = formBotName;
            botNamePopupInput.ActivateInputField();
        }
    }

    public void CloseBotNameInput()
    {
        botNameInputPanel.SetActive(false);
    }

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

    public void OpenBusinessSelector()
    {
        businessSelectorPanel.SetActive(true);
    }

    public void CloseBusinessSelector()
    {
        businessSelectorPanel.SetActive(false);
    }

    public void ChooseBusiness(Button chosenBusiness)
    {
        businessType = chosenBusiness.gameObject;
        businessTypeSelected = true;

        foreach (Button business in BusinessTypesList)
        {
            business.gameObject.GetComponent<Image>().color = businessButtonDefaultColor;
        }
        chosenBusiness.gameObject.GetComponent<Image>().color = Color.green;

        if (businessTypeValueText != null)
        {
            businessTypeValueText.text = chosenBusiness.gameObject.name;
            businessTypeValueText.color = new Color32(28, 28, 30, 255);
        }

        CloseBusinessSelector();
        ValidateCreateForm();
    }

    public void OpenDescriptionInput()
    {
        descriptionInputPanel.SetActive(true);
        if (descriptionPopupInput != null)
        {
            descriptionPopupInput.text = formDescription;
            descriptionPopupInput.ActivateInputField();
        }
    }

    public void CloseDescriptionInput()
    {
        descriptionInputPanel.SetActive(false);
    }

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

            WhatsappAuth.SetActive(true);
            WhatsappAuthContinueButton.interactable = false;
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

            TelegramAuth.SetActive(true);
            TelegramAuthContinueButton.interactable = false;
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

        newBot.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = formBotName;
        newBot.transform.GetChild(1).GetComponent<Toggle>().isOn = true;
        newBot.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = "Connecting..";
        newBot.GetComponent<Bot>().active = false;
        newBot.GetComponent<Bot>().EditButton.interactable = false;
        newBot.GetComponent<Bot>().ActivationSwitch.interactable = false;
        newBot.GetComponent<Bot>().whatsappProfileId = whatsappProfileId;
        newBot.GetComponent<Bot>().telegramProfileId = telegramProfileId;

        BotSettings newBotSettings = Instantiate(BotSettings, new Vector3(BotSettings.transform.position.x + Screen.width / 2, BotSettings.transform.position.y + Screen.height / 2, 0), BotSettings.transform.rotation, BotSettingsParentStatic.transform).GetComponent<BotSettings>();

        newBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = formBotName;
        newBotSettings.WhatsappToggle.isOn = useWhatsapp;
        newBotSettings.TelegramToggle.isOn = useTelegram;
        newBotSettings.BusinessTypeDropdown.value = businessType.transform.GetSiblingIndex();
        newBotSettings.WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = useWhatsapp ? WhatsappNumberInput.text : "";
        newBotSettings.TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = useTelegram ? TelegramNumberInput.text : "";
        newBotSettings.WhatsappNumberButton.transform.parent.gameObject.SetActive(useWhatsapp && !string.IsNullOrEmpty(WhatsappNumberInput.text));
        newBotSettings.TelegramNumberButton.transform.parent.gameObject.SetActive(useTelegram && !string.IsNullOrEmpty(TelegramNumberInput.text));

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
        PlayerPrefs.SetInt(newBot.name + "BusinessType", businessType.transform.GetSiblingIndex());
        PlayerPrefs.SetString(newBot.name + "WhatsappNumber", useWhatsapp ? WhatsappNumberInput.text : "");
        PlayerPrefs.SetString(newBot.name + "TelegramNumber", useTelegram ? TelegramNumberInput.text : "");

        PlayerPrefs.SetInt("ids", ++id);
        PlayerPrefs.Save();

        // Step 6: Reset form and navigate to bots tab
        ResetAddBotForm();
        isCreatingBot = false;

        BottomTabManager tabManager = FindObjectOfType<BottomTabManager>();
        if (tabManager != null)
        {
            tabManager.SwitchTab(3);
        }
    }

    private void CancelBotCreation()
    {
        isCreatingBot = false;
        WhatsappAuth.SetActive(false);
        TelegramAuth.SetActive(false);

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
        businessType = BusinessTypesList[0].gameObject;

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

        foreach (Button business in BusinessTypesList)
        {
            business.gameObject.GetComponent<Image>().color = businessButtonDefaultColor;
        }

        if (createBotFormButton != null) createBotFormButton.interactable = false;
    }


    //////////////////////////////////////////////////////////AUTHORIZATION//////////////////////////////////////////////////////////

    private IEnumerator OpenWhatsappQRPanel()
    {
        LoadingPanel.SetActive(true);
        WhatsappQRPanel.SetActive(true);

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/qr/get?profile_id={whatsappProfileId}");

        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                WhatsappQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Check internet connection.";
            }
            else if (www.result == UnityWebRequest.Result.ProtocolError)
            {
                if (www.downloadHandler != null)
                {
                    string response = www.downloadHandler.text;

                    if (response.Contains("\"detail\":") && response.Contains("\",\"status\":"))
                    {
                        int startIndex = response.IndexOf("\"detail\":") + 10;
                        int endIndex = response.IndexOf("\",\"status\":");
                        int length = endIndex - startIndex;

                        WhatsappQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = response.Substring(startIndex, length) + ".";
                    }
                    else
                    {
                        WhatsappQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                    }
                }
                else
                {
                    WhatsappQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                }
            }
            else
            {
                WhatsappQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
            }

            WhatsappQRPanel.transform.GetChild(3).gameObject.SetActive(true);
        }
        else
        {
            string response = www.downloadHandler.text;

            if (response.Contains("data:image/png;base64,") && response.Contains("\",\"task_id\":"))
            {
                int startIndex = response.IndexOf("data:image/png;base64,") + 22;
                int endIndex = response.IndexOf("\",\"task_id\":");
                int length = endIndex - startIndex;

                response = response.Substring(startIndex, length);
                byte[] imageBytes = Convert.FromBase64String(response);

                Texture2D texture = new(2, 2);

                if (texture.LoadImage(imageBytes))
                {
                    WhatsappQRCodeImage.texture = texture;
                }

                if (_whatsappStatusCoroutine != null) StopCoroutine(_whatsappStatusCoroutine);
                _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
            }
        }

        LoadingPanel.SetActive(false);
    }
    private IEnumerator OpenWhatsappQRPanel1()
    {
        LoadingPanel.SetActive(true);
        WhatsappQRPanel.SetActive(true);
        // Reset the QR image and status text for a clean state
        WhatsappQRCodeImage.texture = null; 
        WhatsappQRPanel.transform.GetChild(3).gameObject.SetActive(false);

        string url = $"{apiUrl}/waInstance{idInstance}/qr/{apiTokenInstance}";
        using UnityWebRequest www = UnityWebRequest.Get(url);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            WhatsappQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
            WhatsappQRPanel.transform.GetChild(3).gameObject.SetActive(true);
        }
        else
        {
            string response = www.downloadHandler.text;
            // print(response); // Useful for debugging

            // First, check the "type" field to decide how to handle the response
            if (response.Contains("\"type\":\"alreadyLogged\""))
            {
                Debug.Log("Instance is already authorized. Skipping QR code generation.");
                // Optionally update UI to say "Already Connected" here
                
                // Proceed directly to status checking to finalize the UI state
                if (_whatsappStatusCoroutine != null) StopCoroutine(_whatsappStatusCoroutine);
                _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
            }
            else if (response.Contains("\"type\":\"qrCode\"") && response.Contains("\"message\":\""))
            {
                // Extract the Base-64 string from the "message" field
                int startIndex = response.IndexOf("\"message\":\"") + 11;
                int endIndex = response.IndexOf("\"", startIndex);
                string base64Image = response.Substring(startIndex, endIndex - startIndex);

                try
                {
                    // Convert the raw Base-64 string directly to a texture
                    byte[] imageBytes = Convert.FromBase64String(base64Image);
                    Texture2D texture = new Texture2D(2, 2);

                    // LoadImage will auto-resize the texture dimensions
                    if (texture.LoadImage(imageBytes))
                    {
                        WhatsappQRCodeImage.texture = texture;
                        // Start polling for status changes after successfully displaying the QR code
                        if (_whatsappStatusCoroutine != null) StopCoroutine(_whatsappStatusCoroutine);
                _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
                    }
                    else
                    {
                         Debug.LogError("Failed to load texture from base64 data.");
                         WhatsappQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Error loading QR Code.";
                         WhatsappQRPanel.transform.GetChild(3).gameObject.SetActive(true);
                    }
                }
                catch (FormatException e)
                {
                    Debug.LogError($"Invalid Base-64 string from API: {e.Message}");
                    WhatsappQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Data Error. Try again.";
                    WhatsappQRPanel.transform.GetChild(3).gameObject.SetActive(true);
                }
            }
            else
            {
                // Handle unexpected response formats
                Debug.LogWarning($"Received unexpected response format: {response}");
                WhatsappQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Unexpected response from server.";
                WhatsappQRPanel.transform.GetChild(3).gameObject.SetActive(true);
            }
        }

        LoadingPanel.SetActive(false);
    }    
    public void CloseWhatsappQRPanel()
    {
        WhatsappQRPanel.SetActive(false);

        WhatsappQRCodeImage.texture = null;

        WhatsappQRPanel.transform.GetChild(3).gameObject.SetActive(false);
        WhatsappQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
    }

    public void OpenWhatsappCodePanel()
    {
        WhatsappCodePanel.SetActive(true);

        WhatsappNumberInput.caretPosition = WhatsappNumberInput.text.Length;
        WhatsappNumberInput.ActivateInputField();

        if (WhatsappNumberInput.text.Length >= 11 && !WhatsappCodeTimer.activeSelf)
        {
            GetWhatsappCodeButton.interactable = true;
        }
    }

    public void WhatsappNumberInputChanged(string newText)
    {
        if (string.IsNullOrEmpty(newText) || newText.Length < 11 || WhatsappCodeTimer.activeSelf)
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

        WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Getting..";
        WhatsappCodeSendingMessage.SetActive(true);


        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/auth/code?profile_id={whatsappProfileId}&phone={WhatsappNumberInput.text}");

        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Check internet connection.";
            }
            else if (www.result == UnityWebRequest.Result.ProtocolError)
            {
                if (www.downloadHandler != null)
                {
                    string response = www.downloadHandler.text;

                    if (response.Contains("\"detail\":") && response.Contains("\",\"uuid\":"))
                    {
                        int startIndex = response.IndexOf("\"detail\":") + 10;
                        int endIndex = response.IndexOf("\",\"uuid\":");
                        int length = endIndex - startIndex;

                        WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = response.Substring(startIndex, length) + ".";
                    }
                    else
                    {
                        WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                    }
                }
                else
                {
                    WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                }
            }
            else
            {
                WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
            }


            if (WhatsappNumberInput.text.Length >= 11)
            {
                GetWhatsappCodeButton.interactable = true;
            }
        }
        else
        {
            WhatsappCodeSendingMessage.SetActive(false);
            WhatsappCodeTimer.SetActive(true);

            WhatsappNumberInput.gameObject.SetActive(false);
            WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(false);
            WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(true);
            WhatsappCodePanel.transform.GetChild(6).gameObject.SetActive(true);

            string response = www.downloadHandler.text;

            if (response.Contains("\"code\":\""))
            {
                int startIndex = response.IndexOf("\"code\":\"") + 8;

                WhatsappCodePanel.transform.GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = response.Substring(startIndex, 9);

                if (_whatsappStatusCoroutine != null) StopCoroutine(_whatsappStatusCoroutine);
                _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
            }
        }

        LoadingPanel.SetActive(false);
    }
    private IEnumerator GetWhatsappCode1()
    {
        LoadingPanel.SetActive(true);
        GetWhatsappCodeButton.interactable = false;

        WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Getting..";
        WhatsappCodeSendingMessage.SetActive(true);

        string url = $"{apiUrl}/waInstance{idInstance}/getAuthorizationCode/{apiTokenInstance}";
        
        // Green-API requires a clean integer-like string for the phone number
        string cleanPhone = WhatsappNumberInput.text.Replace("+", "").Replace(" ", "");
        string jsonBody = $"{{\"phoneNumber\": {cleanPhone}}}";

        using UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
            if (WhatsappNumberInput.text.Length >= 11) GetWhatsappCodeButton.interactable = true;
        }
        else
        {
            string response = www.downloadHandler.text;
            
            // Remove spaces from the response string to make checking the JSON keys foolproof
            string cleanResponse = response.Replace(" ", "");

            if (cleanResponse.Contains("\"status\":true"))
            {
                // The request was successful, proceed to UI changes and code extraction
                WhatsappCodeSendingMessage.SetActive(false);
                WhatsappCodeTimer.SetActive(true);

                WhatsappNumberInput.gameObject.SetActive(false);
                WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(false);
                WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(true);
                WhatsappCodePanel.transform.GetChild(6).gameObject.SetActive(true);

                if (response.Contains("\"code\":\""))
                {
                    int startIndex = response.IndexOf("\"code\":\"") + 8;
                    int endIndex = response.IndexOf("\"", startIndex);
                    string authCode = response.Substring(startIndex, endIndex - startIndex);

                    WhatsappCodePanel.transform.GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = authCode;

                    if (_whatsappStatusCoroutine != null) StopCoroutine(_whatsappStatusCoroutine);
                _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
                }
            }
            else if (cleanResponse.Contains("\"status\":false"))
            {
                Debug.LogWarning($"API rejected the code request. Response: {response}");
                
                // Explicitly checking for the empty code string shown in your console
                if (cleanResponse.Contains("\"code\":\"\""))
                {
                    WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Already Authorized!";
                    
                    // Jump straight to the status checker to finalize the UI
                    if (_whatsappStatusCoroutine != null) StopCoroutine(_whatsappStatusCoroutine);
                _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
                }
                else
                {
                    WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Error generating code. Check number.";
                    if (WhatsappNumberInput.text.Length >= 11) GetWhatsappCodeButton.interactable = true;
                }
            }
            else
            {
                Debug.LogError($"Unexpected JSON structure: {response}");
                WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Unexpected server error.";
                if (WhatsappNumberInput.text.Length >= 11) GetWhatsappCodeButton.interactable = true;
            }
        }

        LoadingPanel.SetActive(false);
    }
    
    public void CloseWhatsappCodePanel()
    {
        WhatsappCodePanel.SetActive(false);

        WhatsappNumberInput.gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(false);
        WhatsappCodePanel.transform.GetChild(6).gameObject.SetActive(false);

        WhatsappCodeSendingMessage.SetActive(false);

        WhatsappCodePanel.transform.GetChild(5).GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
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
                        WhatsappAuthContinueButton.interactable = true;
                        whatsappAuthCompleted = true;
                        if (isCreatingBot)
                        {
                            WhatsappAuth.SetActive(false);
                        }

                        CloseWhatsappQRPanel();
                        CloseWhatsappCodePanel();

                        if (response.Contains("\"phone\":") && response.Contains("\",\"platform\":"))
                        {
                            startIndex = response.IndexOf("\"phone\":") + 9;
                            endIndex = response.IndexOf("\",\"platform\":");
                            lenght = endIndex - startIndex;

                            WhatsappNumberInput.text = response.Substring(startIndex, lenght);
                        }
                    }
                }
            }

            yield return new WaitForSeconds(5f);
        }
    }
    private IEnumerator GetWhatsappProfileStatus1()
    {
        bool authorized = false;
        string url = $"{apiUrl}/waInstance{idInstance}/getStateInstance/{apiTokenInstance}";

        while (!authorized)
        {
            using UnityWebRequest www = UnityWebRequest.Get(url);
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string response = www.downloadHandler.text;

                // Green-API returns {"stateInstance": "authorized"}
                if (response.Contains("\"stateInstance\":\"authorized\""))
                {
                    authorized = true;
                    WhatsappAuthContinueButton.interactable = true;

                    CloseWhatsappQRPanel();
                    CloseWhatsappCodePanel();
                    
                    // Note: Green-API getStateInstance doesn't return the phone number directly in the same call.
                    // You might need to call /getSettings/ to fetch the assigned phone number if needed.
                }
            }

            yield return new WaitForSeconds(5f);
        }
    }

    private IEnumerator OpenTelegramQRPanel()
    {
        LoadingPanel.SetActive(true);
        TelegramQRPanel.SetActive(true);

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/tapi/sync/auth/qr?profile_id={telegramProfileId}");

        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                TelegramQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Check internet connection.";
            }
            else if (www.result == UnityWebRequest.Result.ProtocolError)
            {
                if (www.downloadHandler != null)
                {
                    string response = www.downloadHandler.text;

                    if (response.Contains("\"detail\":") && response.Contains("\",\"status\":"))
                    {
                        int startIndex = response.IndexOf("\"detail\":") + 10;
                        int endIndex = response.IndexOf("\",\"status\":");
                        int length = endIndex - startIndex;

                        TelegramQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = response.Substring(startIndex, length) + ".";
                    }
                    else
                    {
                        TelegramQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                    }
                }
                else
                {
                    TelegramQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                }
            }
            else
            {
                TelegramQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
            }

            TelegramQRPanel.transform.GetChild(3).gameObject.SetActive(true);
        }
        else
        {
            string response = www.downloadHandler.text;

            if (response.Contains("\"detail\":\"") && response.Contains("\",\"uuid\":"))
            {
                int startIndex = response.IndexOf("\"detail\":\"") + 10;
                int endIndex = response.IndexOf("\",\"uuid\":");
                int length = endIndex - startIndex;

                response = response.Substring(startIndex, length);

                byte[] imageBytes = Convert.FromBase64String(response);

                Texture2D texture = new(2, 2);

                if (texture.LoadImage(imageBytes))
                {
                    TelegramQRCodeImage.texture = texture;
                }

                if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
                _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());
            }
        }

        LoadingPanel.SetActive(false);
    }
    private IEnumerator OpenTelegramQRPanel1()
    {
        LoadingPanel.SetActive(true);
        TelegramQRPanel.SetActive(true);
        TelegramQRCodeImage.texture = null;
        TelegramQRPanel.transform.GetChild(3).gameObject.SetActive(false);

        // Swap idInstance and apiTokenInstance to your Telegram-specific variables here
        string url = $"{apiUrl}/waInstance{idInstance}/qr/{apiTokenInstance}";
        using UnityWebRequest www = UnityWebRequest.Get(url);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            TelegramQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
            TelegramQRPanel.transform.GetChild(3).gameObject.SetActive(true);
        }
        else
        {
            string response = www.downloadHandler.text;
            if (response.Contains("\"type\":\"already_registered\""))
            {
                Debug.Log("Telegram Instance already authorized.");
                if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
                _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());
            }
            else if (response.Contains("\"type\":\"qrCode\"") && response.Contains("\"message\":\""))
            {
                int startIndex = response.IndexOf("\"message\":\"") + 11;
                int endIndex = response.IndexOf("\"", startIndex);
                string base64Image = response.Substring(startIndex, endIndex - startIndex);

                if (base64Image.StartsWith("data:image/png;base64,"))
                {
                    base64Image = base64Image.Substring(22);
                }

                try
                {
                    byte[] imageBytes = Convert.FromBase64String(base64Image);
                    Texture2D texture = new Texture2D(2, 2);

                    if (texture.LoadImage(imageBytes))
                    {
                        TelegramQRCodeImage.texture = texture;
                        if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
                _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());
                    }
                }
                catch (FormatException e)
                {
                    Debug.LogError($"Invalid Base-64 string: {e.Message}");
                    TelegramQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Data Error. Try again.";
                    TelegramQRPanel.transform.GetChild(3).gameObject.SetActive(true);
                }
            }
        }

        LoadingPanel.SetActive(false);
    }

    public void CloseTelegramQRPanel()
    {
        TelegramQRPanel.SetActive(false);

        TelegramQRCodeImage.texture = null;

        TelegramQRPanel.transform.GetChild(3).gameObject.SetActive(false);
        TelegramQRPanel.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
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

        if (TelegramNumberInput.text.Length >= 11 && !TelegramCodeTimer.activeSelf)
        {
            GetTelegramCodeButton.interactable = true;
        }
    }

    public void TelegramNumberInputChanged(string newText)
    {
        if (string.IsNullOrEmpty(newText) || newText.Length < 11 || TelegramCodeTimer.activeSelf)
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

        TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Sending..";
        TelegramCodeSendingMessage.SetActive(true);


        string jsonBody = "{\"phone\":\"" + TelegramNumberInput.text + "\"}";

        using UnityWebRequest www = new($"https://wappi.pro/tapi/sync/auth/phone?profile_id={telegramProfileId}", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Check internet connection.";
            }
            else if (www.result == UnityWebRequest.Result.ProtocolError)
            {
                if (www.downloadHandler != null)
                {
                    string response = www.downloadHandler.text;

                    if (response.Contains("\"detail\":") && response.Contains("\",\"status\":"))
                    {
                        int startIndex = response.IndexOf("\"detail\":") + 10;
                        int endIndex = response.IndexOf("\",\"status\":");
                        int length = endIndex - startIndex;

                        TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = response.Substring(startIndex, length) + ".";
                    }
                    else
                    {
                        TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                    }
                }
                else
                {
                    TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                }
            }
            else
            {
                TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
            }


            if (TelegramNumberInput.text.Length >= 11)
            {
                GetTelegramCodeButton.interactable = true;
            }
        }
        else
        {
            TelegramCodeSendingMessage.SetActive(false);
            PlayerPrefs.SetString("TelegramCooldownFinishTime", DateTime.Now.AddSeconds(30).ToString());

            TelegramNumberInput.gameObject.SetActive(false);
            GetTelegramCodeButton.gameObject.SetActive(false);
            TelegramCodePanel.transform.GetChild(4).gameObject.SetActive(false);
            TelegramCodeInput.gameObject.SetActive(true);
            SendTelegramCodeButton.gameObject.SetActive(true);
            TelegramCodePanel.transform.GetChild(7).gameObject.SetActive(true);


            string response = www.downloadHandler.text;

            if (response.Contains("\"status\":\""))
            {
                int startIndex = response.IndexOf("\"status\":\"") + 10;

                if (response.Substring(startIndex, 4).Equals("done"))
                {
                    TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Sent";
                    TelegramCodeSendingMessage.SetActive(true);

                    yield return new WaitForSeconds(2f);
                    TelegramCodeSendingMessage.SetActive(false);
                }
            }
        }

        LoadingPanel.SetActive(false);
    }
    private IEnumerator GetTelegramCode1()
    {
        LoadingPanel.SetActive(true);
        GetTelegramCodeButton.interactable = false;

        TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Sending..";
        TelegramCodeSendingMessage.SetActive(true);

        string url = $"{apiUrl}/waInstance{idInstance}/startAuthorization/{apiTokenInstance}";
        string cleanPhone = TelegramNumberInput.text.Replace("+", "").Replace(" ", "");
        string jsonBody = $"{{\"phoneNumber\": {cleanPhone}}}";

        using UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            if (!string.IsNullOrEmpty(www.downloadHandler.text))
            {
                string response = www.downloadHandler.text;

                print(www.downloadHandler.text);

                if (response.Contains("\"status\":false"))
                {
                    TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Already Authorized!";
                    if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
                _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());
                }
            }
            else
            {
                TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                if (TelegramNumberInput.text.Length >= 11) GetTelegramCodeButton.interactable = true;
            }
        }
        else
        {
            string response = www.downloadHandler.text;
            print(response);

            string cleanResponse = response.Replace(" ", "");

            if (cleanResponse.Contains("\"status\":true"))
            {
                // The code was successfully sent to the user's Telegram app
                TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Sent";
                PlayerPrefs.SetString("TelegramCooldownFinishTime", DateTime.Now.AddSeconds(30).ToString());

                yield return new WaitForSeconds(2f);
                
                // Switch UI to let the user input the 5-digit code they received
                TelegramCodeSendingMessage.SetActive(false);
                TelegramNumberInput.gameObject.SetActive(false);
                GetTelegramCodeButton.gameObject.SetActive(false);
                TelegramCodePanel.transform.GetChild(4).gameObject.SetActive(false);
                TelegramCodeInput.gameObject.SetActive(true);
                SendTelegramCodeButton.gameObject.SetActive(true);
                TelegramCodePanel.transform.GetChild(7).gameObject.SetActive(true);
            }
            else
            {
                TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Error sending code.";
                if (TelegramNumberInput.text.Length >= 11) GetTelegramCodeButton.interactable = true;
            }
        }

        LoadingPanel.SetActive(false);
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

        TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Authorizing..";
        TelegramCodeSendingMessage.SetActive(true);
        
        
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
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Check internet connection.";
            }
            else if (www.result == UnityWebRequest.Result.ProtocolError)
            {
                if (www.downloadHandler != null)
                {
                    string response = www.downloadHandler.text;

                    if (response.Contains("\"detail\":") && response.Contains("\",\"uuid\":"))
                    {
                        int startIndex = response.IndexOf("\"detail\":") + 10;
                        int endIndex = response.IndexOf("\",\"uuid\":");
                        int length = endIndex - startIndex;

                        TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = response.Substring(startIndex, length) + ".";
                    }
                    else
                    {
                        print(response);
                        TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                    }
                }
                else
                {
                    TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
                }
            }
            else
            {
                TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
            }


            if (TelegramCodeInput.text.Length >= 5)
            {
                SendTelegramCodeButton.interactable = true;
            }
        }
        else
        {
            string response = www.downloadHandler.text;
            
            if (response.Contains("\"detail\":\""))
            {
                int startIndex = response.IndexOf("\"detail\":\"") + 10;

                if (response.Substring(startIndex, 12).Equals("auth_success"))
                {
                    TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Authorization Complete";

                    if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
                _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());

                    yield return new WaitForSeconds(2f);
                    TelegramCodeSendingMessage.SetActive(false);
                }
                else
                {
                    TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Authorization Failed";

                    yield return new WaitForSeconds(2f);
                    TelegramCodeSendingMessage.SetActive(false);
                }
            }
            else
            {
                TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Authorization Failed";

                yield return new WaitForSeconds(2f);
                TelegramCodeSendingMessage.SetActive(false);
            }
        }

        LoadingPanel.SetActive(false);
    }
    private IEnumerator SendTelegramCode1()
    {
        LoadingPanel.SetActive(true);
        SendTelegramCodeButton.interactable = false;

        TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Authorizing..";
        TelegramCodeSendingMessage.SetActive(true);
        
        // Green-API typically expects this in an endpoint like /setAuthorizationCode/
        // Check your specific Telegram API docs for the exact route if it differs from WA
        string url = $"{apiUrl}/waInstance{idInstance}/sendAuthorizationCode/{apiTokenInstance}";
        string jsonBody = $"{{\"code\": \"{TelegramCodeInput.text}\"}}";

        using UnityWebRequest www = new UnityWebRequest(url, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Server Unavailable.\n\nTry Again Later";
            if (TelegramCodeInput.text.Length >= 5) SendTelegramCodeButton.interactable = true;
        }
        else
        {
            string response = www.downloadHandler.text;
            string cleanResponse = response.Replace(" ", "");

            // Assuming Green-API returns {"status": true} on successful code verification
            if (cleanResponse.Contains("\"status\":true"))
            {
                TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Authorization Complete";
                if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
                _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());
                
                yield return new WaitForSeconds(2f);
                TelegramCodeSendingMessage.SetActive(false);
            }
            else
            {
                TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Invalid Code";
                yield return new WaitForSeconds(2f);
                TelegramCodeSendingMessage.SetActive(false);
                if (TelegramCodeInput.text.Length >= 5) SendTelegramCodeButton.interactable = true;
            }
        }

        LoadingPanel.SetActive(false);
    }
    public void CloseTelegramCodePanel()
    {
        TelegramCodePanel.SetActive(false);

        TelegramNumberInput.gameObject.SetActive(true);
        GetTelegramCodeButton.gameObject.SetActive(true);
        TelegramCodePanel.transform.GetChild(4).gameObject.SetActive(true);
        TelegramCodeInput.gameObject.SetActive(false);
        SendTelegramCodeButton.gameObject.SetActive(false);
        TelegramCodePanel.transform.GetChild(7).gameObject.SetActive(false);

        TelegramCodeSendingMessage.SetActive(false);

        TelegramCodeInput.text = "";
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
                        TelegramAuthContinueButton.interactable = true;
                        telegramAuthCompleted = true;
                        if (isCreatingBot)
                        {
                            TelegramAuth.SetActive(false);
                        }

                        CloseTelegramQRPanel();
                        CloseTelegramCodePanel();

                        if (response.Contains("\"phone\":") && response.Contains("\",\"platform\":"))
                        {
                            startIndex = response.IndexOf("\"phone\":") + 9;
                            endIndex = response.IndexOf("\",\"platform\":");
                            lenght = endIndex - startIndex;

                            TelegramNumberInput.text = response.Substring(startIndex, lenght);
                        }
                    }
                }
            }

            yield return new WaitForSeconds(5f);
        }
    }
    private IEnumerator GetTelegramProfileStatus1()
    {
        bool authorized = false;
        string url = $"{apiUrl}/waInstance{idInstance}/getStateInstance/{apiTokenInstance}";

        while (!authorized)
        {
            using UnityWebRequest www = UnityWebRequest.Get(url);
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string response = www.downloadHandler.text;

                if (response.Contains("\"stateInstance\":\"authorized\""))
                {
                    authorized = true;
                    TelegramAuthContinueButton.interactable = true;

                    CloseTelegramQRPanel();
                    CloseTelegramCodePanel();
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

        form.AddField("Name", bot.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        form.AddField("BusinessType", businessType.name);
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
            if (Confirmation != null)
            {
                Confirmation.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "ERROR";
                Confirmation.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "Something went wrong.\n\n Please try later!";

                if (www.result == UnityWebRequest.Result.ConnectionError)
                {
                    Confirmation.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "ERROR";
                    Confirmation.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "Network error: Please check your internet connection and try again.";
                }
            }

            StartCoroutine(DeleteWhatsappProfile(whatsappProfileId, true));
        }
        else
        {
            if (Confirmation != null)
            {
                Confirmation.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "Congartulations!";
                Confirmation.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "New bot is created! \n\nIt is already active. \n\nYou can check your bots status in My Bots tab.";
            }

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
            Product product = openBotSettings.ProductsParent.transform.GetChild(i).GetComponent<Product>();

            product.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Trim();
            if (!product.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""))
            {
                productsList += $"Product{i + 1}: {product.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nProduct{i + 1} Price: {product.PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nProduct{i + 1} Description: {product.DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}"
                    + (i == openBotSettings.ProductsParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        string servicesList = "";

        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount - 1; i++)
        {
            Service service = openBotSettings.ServicesParent.transform.GetChild(i).GetComponent<Service>();

            service.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Trim();
            if (!service.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""))
            {
                servicesList += $"Service{i + 1}: {service.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nService{i + 1} Price: {service.PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nService{i + 1} Description: {service.DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}"
                    + (i == openBotSettings.ServicesParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        WWWForm form = new();

        form.AddField("Name", openBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        form.AddField("BusinessType", openBotSettings.BusinessTypeDropdown.options[openBotSettings.BusinessTypeDropdown.value].text);
        form.AddField("WhatsappProfileId", openBot.GetComponent<Bot>().whatsappProfileId);

        form.AddField("Business", "About Business:\n" + openBotSettings.BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        form.AddField("Prompt", openBotSettings.PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
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

        form.AddField("Name", bot.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        form.AddField("BusinessType", businessType.name);
        form.AddField("TelegramProfileId", telegramProfileId);

        form.AddField("Business", "");
        form.AddField("Prompt", "");
        form.AddField("ProductsList", "");
        form.AddField("ServicesList", "");

        using UnityWebRequest www = UnityWebRequest.Post("https://bagkz.app.n8n.cloud/webhook/CreateTelegramWorkflow", form);
        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            if (Confirmation != null)
            {
                Confirmation.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "ERROR";
                Confirmation.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "Something went wrong.\n\n Please try later!";

                if (www.result == UnityWebRequest.Result.ConnectionError)
                {
                    Confirmation.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "ERROR";
                    Confirmation.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "Network error: Please check your internet connection and try again.";
                }
            }

            StartCoroutine(DeleteTelegramProfile(telegramProfileId, true));
        }
        else
        {
            if (Confirmation != null)
            {
                Confirmation.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "Congartulations!";
                Confirmation.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "New bot is created! \n\nIt is already active. \n\nYou can check your bots status in My Bots tab.";
            }

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
            Product product = openBotSettings.ProductsParent.transform.GetChild(i).GetComponent<Product>();

            product.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Trim();
            if (!product.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""))
            {
                productsList += $"Product{i + 1}: {product.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nProduct{i + 1} Price: {product.PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nProduct{i + 1} Description: {product.DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}"
                    + (i == openBotSettings.ProductsParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        string servicesList = "";

        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount - 1; i++)
        {
            Service service = openBotSettings.ServicesParent.transform.GetChild(i).GetComponent<Service>();

            service.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Trim();
            if (!service.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""))
            {
                servicesList += $"Service{i + 1}: {service.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nService{i + 1} Price: {service.PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nService{i + 1} Description: {service.DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}"
                    + (i == openBotSettings.ServicesParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        WWWForm form = new();

        form.AddField("Name", openBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        form.AddField("BusinessType", openBotSettings.BusinessTypeDropdown.options[openBotSettings.BusinessTypeDropdown.value].text);
        form.AddField("TelegramProfileId", openBot.GetComponent<Bot>().telegramProfileId);

        form.AddField("Business", "About Business:\n" + openBotSettings.BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        form.AddField("Prompt", openBotSettings.PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
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
            Product product = openBotSettings.ProductsParent.transform.GetChild(i).GetComponent<Product>();

            product.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Trim();
            if (!product.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""))
            {
                productsList += $"Product{i + 1}: {product.ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nProduct{i + 1} Price: {product.PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nProduct{i + 1} Description: {product.DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}"
                    + (i == openBotSettings.ProductsParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        string servicesList = "";

        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount - 1; i++)
        {
            Service service = openBotSettings.ServicesParent.transform.GetChild(i).GetComponent<Service>();

            service.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Trim();
            if (!service.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(""))
            {
                servicesList += $"Service{i + 1}: {service.ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nService{i + 1} Price: {service.PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}" +
                    $"\nService{i + 1} Description: {service.DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text}"
                    + (i == openBotSettings.ServicesParent.transform.childCount - 2 ? "" : $"\n\n");
            }
        }


        WWWForm form = new();

        form.AddField("WhatsappWorkflowId", whatsappWorkflowId);
        form.AddField("TelegramWorkflowId", telegramWorkflowId);
        form.AddField("Name", openBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        form.AddField("BusinessType", openBotSettings.BusinessTypeDropdown.options[openBotSettings.BusinessTypeDropdown.value].text);
        form.AddField("Business", openBotSettings.BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        form.AddField("Prompt", openBotSettings.PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
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
                SettingsButton.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text += "Operation cancelled";
            }
            else
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    SettingsButton.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text += "paths " + paths[i];

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
            SettingsButton.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "error";
        }
        else
        {
            SettingsButton.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = www.downloadHandler.text;
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


    private void OpenChatsPanel()
    {
        ChatsPanel.SetActive(true);
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
             if (www.result == UnityWebRequest.Result.ConnectionError)
             {
                 SettingsButton.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "ConnectionError";
     
             }
             else if (www.result == UnityWebRequest.Result.ProtocolError)
             {
                 SettingsButton.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "ProtocolError";
             }
             else
             {
                 SettingsButton.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = "Error";
             }
             
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
             
             SettingsButton.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = response;
     
             print(response);
         }
     
         LoadingPanel.SetActive(false);
     }
}
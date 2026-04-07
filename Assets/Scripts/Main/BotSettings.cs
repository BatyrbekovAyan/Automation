using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using UnityEngine.Networking;
using System.Text;
using System;
using System.IO;
using System.Xml;

public class BotSettings : MonoBehaviour
{
    #region
    [SerializeField] private GameObject General;
    [SerializeField] private GameObject Business;
    [SerializeField] private GameObject Product;
    [SerializeField] private GameObject Service;
    [SerializeField] private GameObject Prompt;
    [SerializeField] private GameObject ProductPrefab;
    [SerializeField] private GameObject ServicePrefab;
    [SerializeField] public GameObject ProductsParent;
    [SerializeField] public GameObject ServicesParent;
    [SerializeField] public GameObject WhatsappAuthorization;
    [SerializeField] public GameObject WhatsappQRPanel;
    [SerializeField] public GameObject WhatsappCodePanel;
    [SerializeField] public GameObject TelegramAuthorization;
    [SerializeField] public GameObject TelegramQRPanel;
    [SerializeField] public GameObject TelegramCodePanel;
    [SerializeField] public GameObject Saved;
    [SerializeField] private GameObject ConfirmChangeWhatsappNumberPopup;
    [SerializeField] private GameObject ConfirmChangeTelegramNumberPopup;
    [SerializeField] private GameObject WhatsappCodeTimer;
    [SerializeField] private GameObject TelegramCodeTimer;
    [SerializeField] private GameObject WhatsappCodeSendingMessage;
    [SerializeField] private GameObject TelegramCodeSendingMessage;

    [SerializeField] private Button GeneralTabButton;
    [SerializeField] private Button BusinessTabButton;
    [SerializeField] private Button ProductTabButton;
    [SerializeField] private Button ServiceTabButton;
    [SerializeField] private Button PromptTabButton;
    public Button BotNameButton;
    public Button WhatsappNumberButton;
    public Button TelegramNumberButton;
    [SerializeField] private Button InputBackgroundButton;
    public Button PromptInputButton;
    public Button PromptInputTemplatesButton;
    public Button PromptInputDoneButton;
    public Button BusinessInputButton;
    public Button BusinessInputTemplatesButton;
    public Button BusinessInputDoneButton;
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

    public Button AddProductButton;
    public Button AddServiceButton;

    [SerializeField] private TMP_InputField BotNameInput;
    public TMP_InputField WhatsappNumberInput;
    public TMP_InputField TelegramNumberInput;
    public TMP_InputField TelegramCodeInput;
    public TMP_InputField BusinessInput;
    public TMP_InputField PromptInput;

    public TMP_Dropdown BusinessTypeDropdown;

    public Toggle WhatsappToggle;
    public Toggle TelegramToggle;

    [SerializeField] private RawImage WhatsappQRCodeImage;
    [SerializeField] private RawImage TelegramQRCodeImage;

    private string pdf;
    private string txt;
    private string rtf;
    private string xml;
    private string csv;
    private string xls;
    private string xlsx;
    private string docx;

    public static BotSettings Instance;
    #endregion


    void Start()
    {
        if (GeneralTabButton != null)
        {
            GeneralTabButton.onClick.AddListener(OpenGeneralTab);
        }

        if (BusinessTabButton != null)
        {
            BusinessTabButton.onClick.AddListener(OpenBusinessTab);
        }

        if (ProductTabButton != null)
        {
            ProductTabButton.onClick.AddListener(OpenProductTab);
        }

        if (ServiceTabButton != null)
        {
            ServiceTabButton.onClick.AddListener(OpenServiceTab);
        }

        if (PromptTabButton != null)
        {
            PromptTabButton.onClick.AddListener(OpenPromptTab);
        }


        if (BotNameButton != null)
        {
            BotNameButton.onClick.AddListener(OpenBotNameInput);
        }

        if (BusinessTypeDropdown != null)
        {
            BusinessTypeDropdown.onValueChanged.AddListener(ChooseBusinessType);
        }

        if (WhatsappToggle != null)
        {
            WhatsappToggle.onValueChanged.AddListener(WhatsappChannelToggleChanged);
        }

        if (TelegramToggle != null)
        {
            TelegramToggle.onValueChanged.AddListener(TelegramChannelToggleChanged);
        }

        if (WhatsappNumberButton != null)
        {
            WhatsappNumberButton.onClick.AddListener(OpenConfirmChangeWhatsappNumberPopup);
        }

        if (TelegramNumberButton != null)
        {
            TelegramNumberButton.onClick.AddListener(OpenConfirmChangeTelegramNumberPopup);
        }

        if (ConfirmChangeWhatsappNumberButton != null)
        {
            ConfirmChangeWhatsappNumberButton.onClick.AddListener(ConfirmChangeWhatsappNumber);
        }

        if (CancelChangeWhatsappNumberButton != null)
        {
            CancelChangeWhatsappNumberButton.onClick.AddListener(CancelChangeWhatsappNumber);
        }

        if (ConfirmChangeTelegramNumberButton != null)
        {
            ConfirmChangeTelegramNumberButton.onClick.AddListener(ConfirmChangeTelegramNumber);
        }

        if (CancelChangeTelegramNumberButton != null)
        {
            CancelChangeTelegramNumberButton.onClick.AddListener(CancelChangeTelegramNumber);
        }


        if (BusinessInputButton != null)
        {
            BusinessInputButton.onClick.AddListener(OpenBusinessInput);
        }

        if (BusinessInputDoneButton != null)
        {
            BusinessInputDoneButton.onClick.AddListener(FinishEditingBusiness);
        }

        if (PromptInputButton != null)
        {
            PromptInputButton.onClick.AddListener(OpenPromptInput);
        }

        if (PromptInputDoneButton != null)
        {
            PromptInputDoneButton.onClick.AddListener(FinishEditingPrompt);
        }

        if (InputBackgroundButton != null)
        {
            InputBackgroundButton.onClick.AddListener(CloseInputBackground);
        }


        if (BotNameInput != null)
        {
            BotNameInput.onEndEdit.AddListener(delegate { CloseInputBackground(); });
        }

        if (WhatsappNumberInput != null)
        {
            WhatsappNumberInput.onValueChanged.AddListener(WhatsappNumberInputChanged);
        }

        if (WhatsappNumberInput != null)
        {
            WhatsappNumberInput.onEndEdit.AddListener(delegate { CloseInputBackground(); });
        }

        if (TelegramNumberInput != null)
        {
            TelegramNumberInput.onValueChanged.AddListener(TelegramNumberInputChanged);
        }

        if (TelegramNumberInput != null)
        {
            TelegramNumberInput.onEndEdit.AddListener(delegate { CloseInputBackground(); });
        }

        if (TelegramCodeInput != null)
        {
            TelegramCodeInput.onValueChanged.AddListener(TelegramCodeInputChanged);
        }

        if (AddProductButton != null)
        {
            AddProductButton.onClick.AddListener(AddProduct);
        }

        if (AddServiceButton != null)
        {
            AddServiceButton.onClick.AddListener(AddService);
        }

        if (UploadPriceListButton != null)
        {
            UploadPriceListButton.onClick.AddListener(UploadPriceList);
        }

        if (WhatsappAuthorizationBackButton != null)
        {
            WhatsappAuthorizationBackButton.onClick.AddListener(WhatsappAuthorizationBack);
        }

        if (WhatsappAuthorizationDoneBotton != null)
        {
            WhatsappAuthorizationDoneBotton.onClick.AddListener(WhatsappAuthorizationDone);
        }

        if (OpenWhatsappQRPanelButton != null)
        {
            OpenWhatsappQRPanelButton.onClick.AddListener(() => StartCoroutine(OpenWhatsappQRPanel()));
        }

        if (OpenWhatsappCodePanelButton != null)
        {
            OpenWhatsappCodePanelButton.onClick.AddListener(OpenWhatsappCodePanel);
        }

        if (CloseWhatsappQRPanelButton != null)
        {
            CloseWhatsappQRPanelButton.onClick.AddListener(CloseWhatsappQRPanel);
        }

        if (CloseWhatsappCodePanelButton != null)
        {
            CloseWhatsappCodePanelButton.onClick.AddListener(CloseWhatsappCodePanel);
        }

        if (GetWhatsappCodeButton != null)
        {
            GetWhatsappCodeButton.onClick.AddListener(() => StartCoroutine(GetWhatsappCode()));
        }


        if (TelegramAuthorizationBackButton != null)
        {
            TelegramAuthorizationBackButton.onClick.AddListener(TelegramAuthorizationBack);
        }

        if (TelegramAuthorizationDoneBotton != null)
        {
            TelegramAuthorizationDoneBotton.onClick.AddListener(TelegramAuthorizationDone);
        }

        if (OpenTelegramQRPanelButton != null)
        {
            OpenTelegramQRPanelButton.onClick.AddListener(() => StartCoroutine(OpenTelegramQRPanel()));
        }

        if (OpenTelegramCodePanelButton != null)
        {
            OpenTelegramCodePanelButton.onClick.AddListener(OpenTelegramCodePanel);
        }

        if (CloseTelegramQRPanelButton != null)
        {
            CloseTelegramQRPanelButton.onClick.AddListener(CloseTelegramQRPanel);
        }

        if (CloseTelegramCodePanelButton != null)
        {
            CloseTelegramCodePanelButton.onClick.AddListener(CloseTelegramCodePanel);
        }

        if (GetTelegramCodeButton != null)
        {
            GetTelegramCodeButton.onClick.AddListener(() => StartCoroutine(GetTelegramCode()));
        }

        if (SendTelegramCodeButton != null)
        {
            SendTelegramCodeButton.onClick.AddListener(() => StartCoroutine(SendTelegramCode()));
        }
    }


    public void OnEnable()
    {
        StartCoroutine(CheckWhatsappUnauthorizationOutsideApp());
        StartCoroutine(CheckTelegramUnauthorizationOutsideApp());
    }

    public void OnDisable()
    {
        OpenGeneralTab();
    }


    //////////////////////////////////////////////////////////SWITCH TABS//////////////////////////////////////////////////////////

    public void OpenGeneralTab()
    {
        General.SetActive(true);
        Business.SetActive(false);
        Product.SetActive(false);
        Service.SetActive(false);
        Prompt.SetActive(false);
    }

    public void OpenBusinessTab()
    {
        General.SetActive(false);
        Business.SetActive(true);
        Product.SetActive(false);
        Service.SetActive(false);
        Prompt.SetActive(false);
    }

    public void OpenProductTab()
    {
        General.SetActive(false);
        Business.SetActive(false);
        Product.SetActive(true);
        Service.SetActive(false);
        Prompt.SetActive(false);
    }

    public void OpenServiceTab()
    {
        General.SetActive(false);
        Business.SetActive(false);
        Product.SetActive(false);
        Service.SetActive(true);
        Prompt.SetActive(false);
    }

    public void OpenPromptTab()
    {
        General.SetActive(false);
        Business.SetActive(false);
        Product.SetActive(false);
        Service.SetActive(false);
        Prompt.SetActive(true);
    }


    //////////////////////////////////////////////////////////CHANGE SETTINGS//////////////////////////////////////////////////////////

    public void OpenBotNameInput()
    {
        BotNameInput.text = BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text;
        BotNameInput.gameObject.SetActive(true);
        BotNameButton.gameObject.SetActive(false);

        BotNameButton.transform.parent.GetChild(0).GetComponent<Canvas>().overrideSorting = true;

        StartCoroutine(HandleKeyboardAppearing(BotNameInput));
    }

    public void ChooseBusinessType(int selectedIndex)
    {
        Manager.Instance.EnableSave();
    }

    public void WhatsappChannelToggleChanged(bool isOn)
    {
        if (isOn)
        {
            StartCoroutine(CheckWhatsappAuthorization());
        }
        else
        {
            Manager.Instance.EnableSave();
        }
    }

    public void TelegramChannelToggleChanged(bool isOn)
    {
        if (isOn)
        {
            StartCoroutine(CheckTelegramAuthorization());
        }
        else
        {
            Manager.Instance.EnableSave();
        }
    }

    public void OpenConfirmChangeWhatsappNumberPopup()
    {
        ConfirmChangeWhatsappNumberPopup.SetActive(true);
    }

    public void ConfirmChangeWhatsappNumber()
    {
        StartCoroutine(UnauthorizeWhatsapp());

        ConfirmChangeWhatsappNumberPopup.SetActive(false);
        OpenWhatsappAuthorization(true);
    }

    public void CancelChangeWhatsappNumber()
    {
        ConfirmChangeWhatsappNumberPopup.SetActive(false);
    }

    public void OpenConfirmChangeTelegramNumberPopup()
    {
        ConfirmChangeTelegramNumberPopup.SetActive(true);
    }

    public void ConfirmChangeTelegramNumber()
    {
        StartCoroutine(UnauthorizeTelegram());

        ConfirmChangeTelegramNumberPopup.SetActive(false);
        OpenTelegramAuthorization(true);
    }

    public void CancelChangeTelegramNumber()
    {
        ConfirmChangeTelegramNumberPopup.SetActive(false);
    }

    public void OpenBusinessInput()
    {
        BusinessInput.text = BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text;

        BusinessInputButton.transform.parent.parent.parent.gameObject.SetActive(false);
        BusinessInput.gameObject.SetActive(true);
        BusinessInputTemplatesButton.gameObject.SetActive(false);
        BusinessInputDoneButton.gameObject.SetActive(true);

        BusinessInputButton.interactable = false;

        StartCoroutine(HandleKeyboardAppearing(BusinessInput));
    }

    public void FinishEditingBusiness()
    {
        if (!BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(BusinessInput.text))
        {
            BusinessInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = BusinessInput.text;
            Manager.Instance.EnableSave();
        }

        BusinessInput.text = "";

        BusinessInputButton.transform.parent.parent.parent.gameObject.SetActive(true);
        BusinessInput.gameObject.SetActive(false);
        BusinessInputTemplatesButton.gameObject.SetActive(true);
        BusinessInputDoneButton.gameObject.SetActive(false);

        InputBackgroundButton.gameObject.SetActive(false);

        HandleKeyboardDisappearing();
    }

    public void OpenPromptInput()
    {
        PromptInput.text = PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text;

        PromptInputButton.transform.parent.parent.parent.gameObject.SetActive(false);
        PromptInput.gameObject.SetActive(true);
        PromptInputTemplatesButton.gameObject.SetActive(false);
        PromptInputDoneButton.gameObject.SetActive(true);

        PromptInputButton.interactable = false;

        StartCoroutine(HandleKeyboardAppearing(PromptInput));
    }

    public void FinishEditingPrompt()
    {
        if (!PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PromptInput.text))
        {
            PromptInputButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PromptInput.text;
            Manager.Instance.EnableSave();
        }

        PromptInput.text = "";

        PromptInputButton.transform.parent.parent.parent.gameObject.SetActive(true);
        PromptInput.gameObject.SetActive(false);
        PromptInputTemplatesButton.gameObject.SetActive(true);
        PromptInputDoneButton.gameObject.SetActive(false);

        InputBackgroundButton.gameObject.SetActive(false);

        HandleKeyboardDisappearing();
    }

    public void AddProduct()
    {
        GameObject newProduct = Instantiate(ProductPrefab, ProductPrefab.transform.position, ProductPrefab.transform.rotation, AddProductButton.transform.parent.parent);

        AddProductButton.transform.parent.SetAsLastSibling();

        if (ProductsParent.transform.childCount > 3)
        {
            ProductsParent.transform.GetComponent<RectTransform>().DOAnchorPosY(ProductsParent.transform.localPosition.y
                + ProductsParent.GetComponent<GridLayoutGroup>().cellSize.y + ProductsParent.GetComponent<GridLayoutGroup>().spacing.y, .3f);
        }

        newProduct.GetComponent<Animation>().Play();

        Manager.Instance.EnableSave();
    }

    public void AddService()
    {
        GameObject newService = Instantiate(ServicePrefab, ServicePrefab.transform.position, ServicePrefab.transform.rotation, AddServiceButton.transform.parent.parent);

        AddServiceButton.transform.parent.SetAsLastSibling();

        if (ServicesParent.transform.childCount > 3)
        {
            ServicesParent.transform.GetComponent<RectTransform>().DOAnchorPosY(ServicesParent.transform.localPosition.y
                + ServicesParent.GetComponent<GridLayoutGroup>().cellSize.y + ServicesParent.GetComponent<GridLayoutGroup>().spacing.y, .3f);
        }

        newService.GetComponent<Animation>().Play();

        Manager.Instance.EnableSave();
    }

    public void CloseInputBackground()
    {
        if (General.activeSelf)
        {
            if (BotNameInput.gameObject.activeSelf)
            {
                BotNameButton.transform.parent.GetChild(0).GetComponent<Canvas>().overrideSorting = false;

                BotNameButton.gameObject.SetActive(true);
                BotNameInput.gameObject.SetActive(false);

                BotNameInput.text.Trim();
                if (!BotNameInput.text.Equals(""))
                {
                    if (!BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(BotNameInput.text))
                    {
                        BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = BotNameInput.text;
                        Manager.Instance.EnableSave();
                    }

                    BotNameInput.text = "";
                }
            }
        }

        else if (Business.activeSelf)
        {
            BusinessInput.DeactivateInputField();
            BusinessInput.text = "";

            BusinessInputButton.transform.parent.parent.parent.gameObject.SetActive(true);
            BusinessInput.gameObject.SetActive(false);
            BusinessInputTemplatesButton.gameObject.SetActive(true);
            BusinessInputDoneButton.gameObject.SetActive(false);

            HandleKeyboardDisappearing();
        }

        else if (Prompt.activeSelf)
        {
            PromptInput.DeactivateInputField();
            PromptInput.text = "";

            PromptInputButton.transform.parent.parent.parent.gameObject.SetActive(true);
            PromptInput.gameObject.SetActive(false);
            PromptInputTemplatesButton.gameObject.SetActive(true);
            PromptInputDoneButton.gameObject.SetActive(false);

            HandleKeyboardDisappearing();
        }

        else if (Product.activeSelf)
        {
            for (int i = 0; i < ProductsParent.transform.childCount - 1; i++)
            {
                if (ProductsParent.transform.GetChild(i).GetComponent<Canvas>().overrideSorting)
                {
                    ProductsParent.transform.GetChild(i).GetComponent<Canvas>().overrideSorting = false;
                    StartCoroutine(SetProductsParentPosition(i));

                    if (ProductsParent.transform.GetChild(i).GetChild(1).gameObject.activeSelf)
                    {
                        if (!ProductsParent.transform.GetChild(i).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(
                            ProductsParent.transform.GetChild(i).GetChild(1).GetComponent<TMP_InputField>().text)
                            && !ProductsParent.transform.GetChild(i).GetChild(1).GetComponent<TMP_InputField>().text.Equals(""))
                        {
                            ProductsParent.transform.GetChild(i).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text = ProductsParent.transform.GetChild(i).GetChild(1).GetComponent<TMP_InputField>().text;
                            Manager.Instance.EnableSave();
                        }

                        ProductsParent.transform.GetChild(i).GetChild(1).gameObject.SetActive(false);
                        ProductsParent.transform.GetChild(i).GetChild(1).GetComponent<TMP_InputField>().text = "";
                    }

                    else if (ProductsParent.transform.GetChild(i).GetChild(3).gameObject.activeSelf)
                    {
                        if (!ProductsParent.transform.GetChild(i).GetChild(2).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(
                            ProductsParent.transform.GetChild(i).GetChild(3).GetComponent<TMP_InputField>().text))
                        {
                            ProductsParent.transform.GetChild(i).GetChild(2).GetChild(0).GetComponent<TextMeshProUGUI>().text = ProductsParent.transform.GetChild(i).GetChild(3).GetComponent<TMP_InputField>().text;
                            Manager.Instance.EnableSave();
                        }

                        ProductsParent.transform.GetChild(i).GetChild(3).gameObject.SetActive(false);
                        ProductsParent.transform.GetChild(i).GetChild(3).GetComponent<TMP_InputField>().text = "";
                    }

                    else if (ProductsParent.transform.GetChild(i).GetChild(5).gameObject.activeSelf)
                    {
                        if (!ProductsParent.transform.GetChild(i).GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(
                            ProductsParent.transform.GetChild(i).GetChild(5).GetComponent<TMP_InputField>().text))
                        {
                            ProductsParent.transform.GetChild(i).GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = ProductsParent.transform.GetChild(i).GetChild(5).GetComponent<TMP_InputField>().text;
                            Manager.Instance.EnableSave();
                        }

                        ProductsParent.transform.GetChild(i).GetChild(5).gameObject.SetActive(false);
                        ProductsParent.transform.GetChild(i).GetChild(5).GetComponent<TMP_InputField>().text = "";
                    }
                }

                else
                {
                    ProductsParent.transform.GetChild(i).gameObject.SetActive(true);
                }
            }
        }

        else if (Service.activeSelf)
        {
            for (int i = 0; i < ServicesParent.transform.childCount - 1; i++)
            {
                if (ServicesParent.transform.GetChild(i).GetComponent<Canvas>().overrideSorting)
                {
                    ServicesParent.transform.GetChild(i).GetComponent<Canvas>().overrideSorting = false;
                    StartCoroutine(SetServicesParentPosition(i));

                    if (ServicesParent.transform.GetChild(i).GetChild(1).gameObject.activeSelf)
                    {
                        if (!ServicesParent.transform.GetChild(i).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(
                            ServicesParent.transform.GetChild(i).GetChild(1).GetComponent<TMP_InputField>().text)
                            && !ServicesParent.transform.GetChild(i).GetChild(1).GetComponent<TMP_InputField>().text.Equals(""))
                        {
                            ServicesParent.transform.GetChild(i).GetChild(0).GetChild(0).GetComponent<TextMeshProUGUI>().text = ServicesParent.transform.GetChild(i).GetChild(1).GetComponent<TMP_InputField>().text;
                            Manager.Instance.EnableSave();
                        }

                        ServicesParent.transform.GetChild(i).GetChild(1).gameObject.SetActive(false);
                        ServicesParent.transform.GetChild(i).GetChild(1).GetComponent<TMP_InputField>().text = "";
                    }

                    else if (ServicesParent.transform.GetChild(i).GetChild(3).gameObject.activeSelf)
                    {
                        if (!ServicesParent.transform.GetChild(i).GetChild(2).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(
                            ServicesParent.transform.GetChild(i).GetChild(3).GetComponent<TMP_InputField>().text))
                        {
                            ServicesParent.transform.GetChild(i).GetChild(2).GetChild(0).GetComponent<TextMeshProUGUI>().text = ServicesParent.transform.GetChild(i).GetChild(3).GetComponent<TMP_InputField>().text;
                            Manager.Instance.EnableSave();
                        }

                        ServicesParent.transform.GetChild(i).GetChild(3).gameObject.SetActive(false);
                        ServicesParent.transform.GetChild(i).GetChild(3).GetComponent<TMP_InputField>().text = "";
                    }

                    else if (ServicesParent.transform.GetChild(i).GetChild(5).gameObject.activeSelf)
                    {
                        if (!ServicesParent.transform.GetChild(i).GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(
                            ServicesParent.transform.GetChild(i).GetChild(5).GetComponent<TMP_InputField>().text))
                        {
                            ServicesParent.transform.GetChild(i).GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = ServicesParent.transform.GetChild(i).GetChild(5).GetComponent<TMP_InputField>().text;
                            Manager.Instance.EnableSave();
                        }

                        ServicesParent.transform.GetChild(i).GetChild(5).gameObject.SetActive(false);
                        ServicesParent.transform.GetChild(i).GetChild(5).GetComponent<TMP_InputField>().text = "";
                    }
                }

                else
                {
                    ServicesParent.transform.GetChild(i).gameObject.SetActive(true);
                }
            }
        }


        InputBackgroundButton.gameObject.SetActive(false);
    }


    //////////////////////////////////////////////////////////HELPING METHODS//////////////////////////////////////////////////////////

    public IEnumerator HandleKeyboardAppearing(TMP_InputField inputField)
    {
        yield return new WaitForEndOfFrame();

        inputField.caretPosition = inputField.text.Length;
        inputField.ActivateInputField();

        if (General.activeSelf)
        {
            InputBackgroundButton.gameObject.SetActive(true);
        }

        else if (Business.activeSelf || Prompt.activeSelf)
        {
            GeneralTabButton.transform.parent.gameObject.SetActive(false);
            transform.parent.parent.GetChild(0).gameObject.SetActive(false);
            transform.parent.parent.GetChild(1).gameObject.SetActive(false);
            transform.parent.parent.GetChild(2).gameObject.SetActive(false);


            yield return new WaitForSeconds(0.1f);
            yield return new WaitWhile(() => !TouchScreenKeyboard.visible);

            General.transform.parent.GetComponent<RectTransform>().DOAnchorPosY(TouchScreenKeyboard.area.height / 2 - PromptInputDoneButton.gameObject.GetComponent<RectTransform>().rect.height / 2, .3f);

            InputBackgroundButton.gameObject.SetActive(true);
        }

        else if (Product.activeSelf || Service.activeSelf)
        {
            InputBackgroundButton.gameObject.SetActive(true);
        }
    }

    public void HandleKeyboardDisappearing()
    {
        General.transform.parent.GetComponent<RectTransform>().DOAnchorPosY(0, .3f).OnComplete(() => {
            GeneralTabButton.transform.parent.gameObject.SetActive(true);
            transform.parent.parent.GetChild(0).gameObject.SetActive(true);
            transform.parent.parent.GetChild(1).gameObject.SetActive(true);
            transform.parent.parent.GetChild(2).gameObject.SetActive(true);

            if (Business.activeSelf)
            {
                BusinessInputButton.interactable = true;
            }
            else if (Prompt.activeSelf)
            {
                PromptInputButton.interactable = true;
            }
        });
    }

    private IEnumerator SetProductsParentPosition(int index)
    {
        yield return new WaitForEndOfFrame();

        ProductsParent.transform.localPosition = ProductsParent.transform.GetChild(index).GetComponent<Product>().ProductsParentPosition;
    }

    private IEnumerator SetServicesParentPosition(int index)
    {
        yield return new WaitForEndOfFrame();

        ServicesParent.transform.localPosition = ServicesParent.transform.GetChild(index).GetComponent<Service>().ServicesParentPosition;
    }

    private void OpenAuthorization(bool open)
    {
        General.transform.parent.gameObject.SetActive(!open);
        GeneralTabButton.transform.parent.gameObject.SetActive(!open);

        transform.parent.parent.GetChild(0).gameObject.SetActive(!open);
        transform.parent.parent.GetChild(1).gameObject.SetActive(!open);
        transform.parent.parent.GetChild(2).gameObject.SetActive(!open);
    }


    //////////////////////////////////////////////////////////WHATSAPP AUTHORIZATION//////////////////////////////////////////////////////////

    private void OpenWhatsappAuthorization(bool open)
    {
        OpenAuthorization(open);
        WhatsappAuthorization.SetActive(open);
    }

    public void WhatsappAuthorizationBack()
    {
        WhatsappToggle.SetIsOnWithoutNotify(false);
        WhatsappAuthorizationDoneBotton.interactable = false;
        WhatsappNumberInput.text = "";

        WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
        WhatsappNumberButton.transform.parent.gameObject.SetActive(false);
        PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", "");
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 0);

        Manager.Instance.EnableSave();

        Manager.Instance.GetDeleteWhatsappProfile(Manager.openBot.GetComponent<Bot>().whatsappProfileId);

        OpenWhatsappAuthorization(false);
    }

    public void WhatsappAuthorizationDone()
    {
        WhatsappAuthorizationDoneBotton.interactable = false;

        WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = WhatsappNumberInput.text;
        WhatsappNumberButton.transform.parent.gameObject.SetActive(true);
        PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 1);
        PlayerPrefs.SetString(Manager.openBot.name + "WhatsappProfileId", Manager.openBot.GetComponent<Bot>().whatsappProfileId);

        OpenWhatsappAuthorization(false);

        Manager.Instance.GetCreateWhatsappWorkflow();
    }

    private IEnumerator OpenWhatsappQRPanel()
    {
        Manager.Instance.LoadingPanel.SetActive(true);
        WhatsappQRPanel.SetActive(true);

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/qr/get?profile_id={Manager.openBot.GetComponent<Bot>().whatsappProfileId}");

        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

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

                StartCoroutine(GetWhatsappProfileStatus());
            }
        }

        Manager.Instance.LoadingPanel.SetActive(false);
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
        Manager.Instance.LoadingPanel.SetActive(true);
        GetWhatsappCodeButton.interactable = false;

        WhatsappCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Getting..";
        WhatsappCodeSendingMessage.SetActive(true);


        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/auth/code?profile_id={Manager.openBot.GetComponent<Bot>().whatsappProfileId}&phone={WhatsappNumberInput.text}");

        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

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

                StartCoroutine(GetWhatsappProfileStatus());
            }
        }

        Manager.Instance.LoadingPanel.SetActive(false);
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
            using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/get/status?profile_id={Manager.openBot.GetComponent<Bot>().whatsappProfileId}");

            www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

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
                        WhatsappAuthorizationDoneBotton.interactable = true;

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

    private IEnumerator CheckWhatsappAuthorization()
    {
        if (Manager.openBot.GetComponent<Bot>().whatsappProfileId.Equals("-1"))
        {
            OpenWhatsappAuthorization(true);

            PlayerPrefs.SetString("WhatsappCooldownFinishTime", "-1");
            WhatsappCodeTimer.SetActive(false);

            Manager.Instance.GetCreateWhatsappProfile(BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);

            yield break;
        }


        Manager.Instance.LoadingPanel.SetActive(true);

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/get/status?profile_id={Manager.openBot.GetComponent<Bot>().whatsappProfileId}");

        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

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
                    if (response.Contains("\"phone\":") && response.Contains("\",\"platform\":"))
                    {
                        startIndex = response.IndexOf("\"phone\":") + 9;
                        endIndex = response.IndexOf("\",\"platform\":");
                        lenght = endIndex - startIndex;

                        WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = response.Substring(startIndex, lenght);
                        Manager.Instance.EnableSave();
                    }
                }
                else
                {
                    OpenWhatsappAuthorization(true);
                }
            }
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }

    private IEnumerator CheckWhatsappUnauthorizationOutsideApp()
    {
        Manager.Instance.LoadingPanel.SetActive(true);

        yield return new WaitForEndOfFrame();

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/get/status?profile_id={Manager.openBot.GetComponent<Bot>().whatsappProfileId}");

        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            string response = www.downloadHandler.text;

            if (response.Contains("\"authorized\":"))
            {
                int startIndex = response.IndexOf("\"authorized\":") + 13;
                int endIndex = response.IndexOf(",\"authorized_at\":");
                int lenght = endIndex - startIndex;

                if (!response.Substring(startIndex, lenght).Equals("true") && !Manager.openBot.GetComponent<Bot>().whatsappProfileId.Equals("-1"))
                {
                    WhatsappToggle.SetIsOnWithoutNotify(false);

                    WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
                    WhatsappNumberButton.transform.parent.gameObject.SetActive(false);

                    PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", "");
                    PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 0);

                    Manager.Instance.GetDeleteWhatsappProfile(Manager.openBot.GetComponent<Bot>().whatsappProfileId);
                }
            }
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }

    private IEnumerator UnauthorizeWhatsapp()
    {
        Manager.Instance.LoadingPanel.SetActive(true);

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/profile/logout?profile_id={Manager.openBot.GetComponent<Bot>().whatsappProfileId}");

        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Manager.Instance.GetDeleteWhatsappWorkflow(Manager.openBot.GetComponent<Bot>().whatsappWorkflowId);

            WhatsappNumberButton.transform.parent.gameObject.SetActive(false);
            WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
            WhatsappNumberInput.text = "";

            PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", "");
            PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 0);
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }


    //////////////////////////////////////////////////////////TELEGRAM AUTHORIZATION//////////////////////////////////////////////////////////

    private void OpenTelegramAuthorization(bool open)
    {
        OpenAuthorization(open);
        TelegramAuthorization.SetActive(open);
    }

    public void TelegramAuthorizationBack()
    {
        TelegramToggle.SetIsOnWithoutNotify(false);
        TelegramAuthorizationDoneBotton.interactable = false;
        TelegramNumberInput.text = "";

        TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
        TelegramNumberButton.transform.parent.gameObject.SetActive(false);
        PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", "");
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 0);

        Manager.Instance.EnableSave();

        Manager.Instance.GetDeleteTelegramProfile(Manager.openBot.GetComponent<Bot>().telegramProfileId);

        OpenTelegramAuthorization(false);
    }

    public void TelegramAuthorizationDone()
    {
        TelegramAuthorizationDoneBotton.interactable = false;

        TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = TelegramNumberInput.text;
        TelegramNumberButton.transform.parent.gameObject.SetActive(true);
        PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 1);
        PlayerPrefs.SetString(Manager.openBot.name + "TelegramProfileId", Manager.openBot.GetComponent<Bot>().telegramProfileId);

        OpenTelegramAuthorization(false);

        Manager.Instance.GetCreateTelegramWorkflow();
    }

    private IEnumerator OpenTelegramQRPanel()
    {
        Manager.Instance.LoadingPanel.SetActive(true);
        TelegramQRPanel.SetActive(true);

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/tapi/sync/auth/qr?profile_id={Manager.openBot.GetComponent<Bot>().telegramProfileId}");

        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

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

                StartCoroutine(GetTelegramProfileStatus());
            }
        }

        Manager.Instance.LoadingPanel.SetActive(false);
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
        Manager.Instance.LoadingPanel.SetActive(true);
        GetTelegramCodeButton.interactable = false;

        TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Sending..";
        TelegramCodeSendingMessage.SetActive(true);


        string jsonBody = "{\"phone\":\"" + TelegramNumberInput.text + "\"}";

        using UnityWebRequest www = new($"https://wappi.pro/tapi/sync/auth/phone?profile_id={Manager.openBot.GetComponent<Bot>().telegramProfileId}", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

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

        Manager.Instance.LoadingPanel.SetActive(false);
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
        Manager.Instance.LoadingPanel.SetActive(true);
        SendTelegramCodeButton.interactable = false;

        TelegramCodeSendingMessage.GetComponent<TextMeshProUGUI>().text = "Authorizing..";
        TelegramCodeSendingMessage.SetActive(true);


        string jsonBody = "{\"auth_code\":\"" + TelegramCodeInput.text + "\"}";

        using UnityWebRequest www = new($"https://wappi.pro/tapi/sync/auth/code?profile_id={Manager.openBot.GetComponent<Bot>().telegramProfileId}", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

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

                    StartCoroutine(GetTelegramProfileStatus());

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

        Manager.Instance.LoadingPanel.SetActive(false);
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
            using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/tapi/sync/get/status?profile_id={Manager.openBot.GetComponent<Bot>().telegramProfileId}");

            www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

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
                        TelegramAuthorizationDoneBotton.interactable = true;

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

    private IEnumerator CheckTelegramAuthorization()
    {
        if (Manager.openBot.GetComponent<Bot>().telegramProfileId.Equals("-1"))
        {
            OpenTelegramAuthorization(true);

            PlayerPrefs.SetString("TelegramCooldownFinishTime", "-1");
            TelegramCodeTimer.SetActive(false);

            Manager.Instance.GetCreateTelegramProfile(BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text);

            yield break;
        }


        Manager.Instance.LoadingPanel.SetActive(true);

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/tapi/sync/get/status?profile_id={Manager.openBot.GetComponent<Bot>().telegramProfileId}");

        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

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
                    if (response.Contains("\"phone\":") && response.Contains("\",\"platform\":"))
                    {
                        startIndex = response.IndexOf("\"phone\":") + 9;
                        endIndex = response.IndexOf("\",\"platform\":");
                        lenght = endIndex - startIndex;

                        TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = response.Substring(startIndex, lenght);
                        Manager.Instance.EnableSave();
                    }
                }
                else
                {
                    OpenTelegramAuthorization(true);
                }
            }
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }

    private IEnumerator CheckTelegramUnauthorizationOutsideApp()
    {
        Manager.Instance.LoadingPanel.SetActive(true);

        yield return new WaitForEndOfFrame();

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/tapi/sync/get/status?profile_id={Manager.openBot.GetComponent<Bot>().telegramProfileId}");

        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            string response = www.downloadHandler.text;

            if (response.Contains("\"authorized\":"))
            {
                int startIndex = response.IndexOf("\"authorized\":") + 13;
                int endIndex = response.IndexOf(",\"authorized_at\":");
                int lenght = endIndex - startIndex;

                if (!response.Substring(startIndex, lenght).Equals("true") && !Manager.openBot.GetComponent<Bot>().telegramProfileId.Equals("-1"))
                {
                    TelegramToggle.SetIsOnWithoutNotify(false);

                    TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
                    TelegramNumberButton.transform.parent.gameObject.SetActive(false);

                    PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", "");
                    PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 0);

                    Manager.Instance.GetDeleteTelegramProfile(Manager.openBot.GetComponent<Bot>().telegramProfileId);
                }
            }
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }

    private IEnumerator UnauthorizeTelegram()
    {
        Manager.Instance.LoadingPanel.SetActive(true);

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/tapi/profile/logout?profile_id={Manager.openBot.GetComponent<Bot>().telegramProfileId}");

        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            Manager.Instance.GetDeleteTelegramWorkflow(Manager.openBot.GetComponent<Bot>().telegramWorkflowId);

            TelegramNumberButton.transform.parent.gameObject.SetActive(false);
            TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
            TelegramNumberInput.text = "";

            PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", "");
            PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 0);
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }






    private void UploadPriceList()
    {
        pdf = NativeFilePicker.ConvertExtensionToFileType("pdf"); // Returns "application/pdf" on Android and "com.adobe.pdf" on iOS
        txt = NativeFilePicker.ConvertExtensionToFileType("txt");
        //video = NativeFilePicker.ConvertExtensionToFileType("image");
        rtf = NativeFilePicker.ConvertExtensionToFileType("rtf");
        xml = NativeFilePicker.ConvertExtensionToFileType("xml");
        csv = NativeFilePicker.ConvertExtensionToFileType("csv");
        xls = NativeFilePicker.ConvertExtensionToFileType("xls");
        xlsx = NativeFilePicker.ConvertExtensionToFileType("xlsx");
        docx = "org.openxmlformats.wordprocessingml.document";

        PickMediaFile();
    }

    private void PickMediaFile()
    {
#if UNITY_ANDROID
			// Use MIMEs on Android
            string[] fileTypes = new string[] { pdf, txt, rtf, xml, csv, xls, xlsx, docx };
#else
        // Use UTIs on iOS
        string[] fileTypes = new string[] { pdf, txt, rtf, xml, csv, xls, xlsx, docx };
#endif
        // Pick image(s) and/or video(s)
        NativeFilePicker.PickMultipleFiles((paths) =>
        {
            if (paths == null)
                Debug.Log("Operation cancelled");
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

    private IEnumerator UploadFile(string filePath)
    {
        WWWForm form = new();

        form.AddField("whatsappWorkflowId", Manager.openBot.GetComponent<Bot>().whatsappWorkflowId);
        form.AddField("telegramWorkflowId", Manager.openBot.GetComponent<Bot>().telegramWorkflowId);
        form.AddField("contentType", "product");

        byte[] fileData = File.ReadAllBytes(filePath);
        string fileName = Path.GetFileName(filePath);
        string fileExtension = Path.GetExtension(filePath);
        UploadPriceListButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = fileExtension;

        if (fileExtension.Equals(".pdf"))
        {
            form.AddBinaryData("data", fileData, fileName, "application/pdf");
        }
        else if (fileExtension.Equals(".txt"))
        {
            form.AddBinaryData("data", fileData, fileName, "text/plain");
        }
        else if (fileExtension.Equals(".rtf"))
        {
            form.AddBinaryData("data", fileData, fileName, "application/rtf");
        }
        else if (fileExtension.Equals(".xml"))
        {
            string xmlString = Encoding.UTF8.GetString(fileData);
            string xmlText = XmlToTextConverter.ConvertXmlToText(xmlString);
            byte[] textBytes = Encoding.UTF8.GetBytes(xmlText);
            string txtFileName = Path.ChangeExtension(fileName, ".txt");
            
            form.AddBinaryData("data", textBytes, txtFileName, "text/plain");
        }
        else if (fileExtension.Equals(".csv") || fileExtension.Equals(".xls") || fileExtension.Equals(".xlsx"))
        {
            string contentType = "product";

            string text = TableToTextConverter.Convert(fileData, fileName, contentType);

            form.AddBinaryData("data", Encoding.UTF8.GetBytes(text), fileName + ".txt", "text/plain");
        }

        else if (fileExtension.Equals(".docx"))
        {
            string text = DocxToTextConverter.Convert(fileData);

            form.AddBinaryData("data", Encoding.UTF8.GetBytes(text), fileName + ".txt", "text/plain");
        }


        using UnityWebRequest www = UnityWebRequest.Post("https://bagkz.app.n8n.cloud/webhook-test/UploadFile", form);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {

        }
        else
        {
            UploadPriceListButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = www.downloadHandler.text;
        }
    }
}
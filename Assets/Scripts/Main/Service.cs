using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Service : MonoBehaviour
{
    public Button ServiceButton;
    public Button PriceButton;
    public Button DescriptionButton;
    public Button DeleteServiceButton;

    [SerializeField] private GameObject DeletePopup;

    [SerializeField] private Button DeleteConfirmButton;
    [SerializeField] private Button DeleteCancelButton;

    public TMP_InputField ServiceInput;
    public TMP_InputField PriceInput;
    public TMP_InputField DescriptionInput;

    public Vector3 ServicesParentPosition;


    void Start()
    {
        ServicesParentPosition = transform.parent.localPosition;

        if (ServiceButton != null)
        {
            ServiceButton.onClick.AddListener(() => OpenInput(ServiceInput, ServiceButton));
        }

        if (PriceButton != null)
        {
            PriceButton.onClick.AddListener(() => OpenInput(PriceInput, PriceButton));
        }

        if (DescriptionButton != null)
        {
            DescriptionButton.onClick.AddListener(() => OpenInput(DescriptionInput, DescriptionButton));
        }

        if (DeleteServiceButton != null)
        {
            DeleteServiceButton.onClick.AddListener(OpenDeletePopup);
        }

        if (DeleteConfirmButton != null)
        {
            DeleteConfirmButton.onClick.AddListener(DeleteService);
        }

        if (DeleteCancelButton != null)
        {
            DeleteCancelButton.onClick.AddListener(DeleteCancel);
        }

        if (ServiceInput != null)
        {
            ServiceInput.onEndEdit.AddListener(delegate { Manager.openBotSettings.CloseInputBackground(); });
        }

        if (PriceInput != null)
        {
            PriceInput.onEndEdit.AddListener(delegate { Manager.openBotSettings.CloseInputBackground(); });
        }

        if (DescriptionInput != null)
        {
            DescriptionInput.onEndEdit.AddListener(delegate { Manager.openBotSettings.CloseInputBackground(); });
        }
    }


    private void OpenInput(TMP_InputField inputField, Button button)
    {
        if (ServiceInput.gameObject.activeSelf)
        {
            if (!ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(ServiceInput.text) && !ServiceInput.text.Equals(""))
            {
                ServiceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = ServiceInput.text;
                Manager.Instance.EnableSave();

                ServiceInput.text = "";
            }
        }

        else if (PriceInput.gameObject.activeSelf)
        {
            if (!PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(PriceInput.text))
            {
                PriceButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = PriceInput.text;
                Manager.Instance.EnableSave();

                PriceInput.text = "";
            }
        }

        else if (DescriptionInput.gameObject.activeSelf)
        {
            if (!DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(DescriptionInput.text))
            {
                DescriptionButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = DescriptionInput.text;
                Manager.Instance.EnableSave();

                DescriptionInput.text = "";
            }
        }


        inputField.text = button.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text;

        ServiceInput.gameObject.SetActive(ServiceInput == inputField);
        PriceInput.gameObject.SetActive(PriceInput == inputField);
        DescriptionInput.gameObject.SetActive(DescriptionInput == inputField);


        GetComponent<Canvas>().overrideSorting = true;
        GetComponent<Canvas>().sortingOrder = 2;


        ServicesParentPosition = transform.parent.localPosition;
        transform.parent.localPosition = new Vector3(transform.parent.localPosition.x, 0, transform.parent.localPosition.z);

        for (int s = 0; s < transform.parent.childCount - 1; s++)
        {
            if (transform.parent.GetChild(s) != transform)
            {
                transform.parent.GetChild(s).gameObject.SetActive(false);
            }
        }

        StartCoroutine(Manager.openBotSettings.HandleKeyboardAppearing(inputField));
    }

    public void DeleteService()
    {
        if (ServiceInput.gameObject.activeSelf || PriceInput.gameObject.activeSelf || DescriptionInput.gameObject.activeSelf)
        {
            Manager.openBotSettings.transform.GetChild(2).gameObject.SetActive(false);
            Manager.openBotSettings.HandleKeyboardDisappearing();
        }

        Manager.Instance.EnableSave();

        Destroy(gameObject);
    }

    private void OpenDeletePopup()
    {
        DeletePopup.SetActive(true);
    }

    private void DeleteCancel()
    {
        DeletePopup.SetActive(false);
    }
}

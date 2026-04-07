using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Product : MonoBehaviour
{
    public Button ProductButton;
    public Button PriceButton;
    public Button DescriptionButton;
    public Button DeleteProductButton;

    [SerializeField] private GameObject DeletePopup;

    [SerializeField] private Button DeleteConfirmButton;
    [SerializeField] private Button DeleteCancelButton;

    public TMP_InputField ProductInput;
    public TMP_InputField PriceInput;
    public TMP_InputField DescriptionInput;

    public Vector3 ProductsParentPosition;

    void Start()
    {
        ProductsParentPosition = transform.parent.localPosition;

        if (ProductButton != null)
        {
            ProductButton.onClick.AddListener(() => OpenInput(ProductInput, ProductButton));
        }

        if (PriceButton != null)
        {
            PriceButton.onClick.AddListener(() => OpenInput(PriceInput, PriceButton));
        }

        if (DescriptionButton != null)
        {
            DescriptionButton.onClick.AddListener(() => OpenInput(DescriptionInput, DescriptionButton));
        }

        if (DeleteProductButton != null)
        {
            DeleteProductButton.onClick.AddListener(OpenDeletePopup);
        }

        if (DeleteConfirmButton != null)
        {
            DeleteConfirmButton.onClick.AddListener(DeleteProduct);
        }

        if (DeleteCancelButton != null)
        {
            DeleteCancelButton.onClick.AddListener(DeleteCancel);
        }

        if (ProductInput != null)
        {
            ProductInput.onEndEdit.AddListener(delegate { Manager.openBotSettings.CloseInputBackground(); });
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
        if (ProductInput.gameObject.activeSelf)
        {
            if (!ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text.Equals(ProductInput.text) && !ProductInput.text.Equals(""))
            {
                ProductButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = ProductInput.text;
                Manager.Instance.EnableSave();

                ProductInput.text = "";
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

        ProductInput.gameObject.SetActive(ProductInput == inputField);
        PriceInput.gameObject.SetActive(PriceInput == inputField);
        DescriptionInput.gameObject.SetActive(DescriptionInput == inputField);


        GetComponent<Canvas>().overrideSorting = true;
        GetComponent<Canvas>().sortingOrder = 2;


        ProductsParentPosition = transform.parent.localPosition;
        transform.parent.localPosition = new Vector3(transform.parent.localPosition.x, 0, transform.parent.localPosition.z);

        for (int p = 0; p < transform.parent.childCount - 1; p++)
        {
            if (transform.parent.GetChild(p) != transform)
            {
                transform.parent.GetChild(p).gameObject.SetActive(false);
            }
        }

        StartCoroutine(Manager.openBotSettings.HandleKeyboardAppearing(inputField));
    }

    public void DeleteProduct()
    {
        if (ProductInput.gameObject.activeSelf || PriceInput.gameObject.activeSelf || DescriptionInput.gameObject.activeSelf)
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

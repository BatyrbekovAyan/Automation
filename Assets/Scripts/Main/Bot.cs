using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;
using TMPro;

public class Bot : MonoBehaviour
{
    [SerializeField] public TextMeshProUGUI Status;
    [SerializeField] public Button EditButton;
    [SerializeField] private Button DeleteButton;
    [SerializeField] public Toggle ActivationSwitch;
    [SerializeField] private GameObject DeletePopup;

    [SerializeField] private Button DeleteConfirmButton;
    [SerializeField] private Button DeleteCancelButton;

    [SerializeField] private Color backgroundActiveColor;
    [SerializeField] private Color handleActiveColor;


    public bool active = false;

    public string whatsappProfileId;
    public string telegramProfileId;

    public string whatsappWorkflowId;
    public string telegramWorkflowId;

    private RectTransform switchHandle;
    private Image switchBackgroundImage, switchHandleImage;
    private Color backgroundDefaultColor, handleDefaultColor;
    private Vector2 switchHandlePosition;

    private Color green = new(0, 1, 0);
    private Color red = new(1, 0, 0);
    private Color blue = new(0, 0.6980392f, 1);


    private void Awake ()
    {
        StartCoroutine(SetSwitches());


        ActivationSwitch.onValueChanged.AddListener(EnableBot);

        if (EditButton != null)
        {
            EditButton.onClick.AddListener(OpenSettings);
        }

        if (DeleteButton != null)
        {
            DeleteButton.onClick.AddListener(OpenDeletePopup);
        }

        // Delete confirm popup: fire on real finger release via PopupUI.
        if (DeleteConfirmButton != null)
            PopupUI.WireFingerUp(DeleteConfirmButton, DeleteBot);
        if (DeleteCancelButton != null)
            PopupUI.WireFingerUp(DeleteCancelButton, DeleteCancel);
    }


    private void OpenSettings()
    {
        BotsPage.Instance.gameObject.SetActive(false);
        Manager.BotSettingsParentStatic.transform.parent.gameObject.SetActive(true);

        if (Manager.BotSettingsParentStatic.transform.childCount != 0)
        {
            foreach (Transform botSettings in Manager.BotSettingsParentStatic.transform)
            {
                if (botSettings.GetSiblingIndex() == transform.GetSiblingIndex())
                {
                    botSettings.gameObject.SetActive(true);
                    Manager.openBot = gameObject;
                    Manager.openBotSettings = botSettings.gameObject.GetComponent<BotSettings>();
                }
                else
                {
                    botSettings.gameObject.SetActive(false);
                }
            }
        }
    }

    private void DeleteBot()
    {
        if (PlayerPrefs.HasKey(transform.name + "Name"))
        {
            PlayerPrefs.DeleteKey(transform.name + "Name");
            PlayerPrefs.DeleteKey(transform.name + "isOn");
            PlayerPrefs.DeleteKey(transform.name + "Status");
            PlayerPrefs.DeleteKey(transform.name + "Active");
            PlayerPrefs.DeleteKey(transform.name + "isOnWhatsapp");
            PlayerPrefs.DeleteKey(transform.name + "isOnTelegram");
            PlayerPrefs.DeleteKey(transform.name + "BusinessType");
            PlayerPrefs.DeleteKey(transform.name + "WhatsappNumber");
            PlayerPrefs.DeleteKey(transform.name + "TelegramNumber");
            PlayerPrefs.DeleteKey(transform.name + "Business");
            PlayerPrefs.DeleteKey(transform.name + "Prompt");
            PlayerPrefs.DeleteKey(transform.name + "WhatsappWorkflowId");
            PlayerPrefs.DeleteKey(transform.name + "WhatsappProfileId");
            PlayerPrefs.DeleteKey(transform.name + "TelegramWorkflowId");
            PlayerPrefs.DeleteKey(transform.name + "TelegramProfileId");

            if (PlayerPrefs.GetInt(transform.name + "ProductsNumber", 0) > 0)
            {
                for (int p = 0; p < PlayerPrefs.GetInt(transform.name + "ProductsNumber", 0); p++)
                {
                    if (PlayerPrefs.HasKey(transform.name + "Product" + p))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Product" + p);
                    }

                    if (PlayerPrefs.HasKey(transform.name + "Product" + p + "Price"))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Product" + p + "Price");
                    }

                    if (PlayerPrefs.HasKey(transform.name + "Product" + p + "Description"))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Product" + p + "Description");
                    }
                }
            }

            PlayerPrefs.DeleteKey(transform.name + "ProductsNumber");

            if (PlayerPrefs.GetInt(transform.name + "ServicesNumber", 0) > 0)
            {
                for (int s = 0; s < PlayerPrefs.GetInt(transform.name + "ServicesNumber", 0); s++)
                {
                    if (PlayerPrefs.HasKey(transform.name + "Service" + s))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Service" + s);
                    }

                    if (PlayerPrefs.HasKey(transform.name + "Service" + s + "Price"))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Service" + s + "Price");
                    }

                    if (PlayerPrefs.HasKey(transform.name + "Service" + s + "Description"))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Service" + s + "Description");
                    }
                }

            }

            PlayerPrefs.DeleteKey(transform.name + "ServicesNumber");
        }

        Manager.Instance.DeleteProfilesAndWorkflows(whatsappProfileId, telegramProfileId, whatsappWorkflowId, telegramWorkflowId);

        Destroy(Manager.BotSettingsParentStatic.transform.GetChild(transform.GetSiblingIndex()).gameObject);
        Destroy(gameObject);
    }

    private void OpenDeletePopup() => PopupUI.Show(DeletePopup);

    private void DeleteCancel() => PopupUI.Hide(DeletePopup);

    private void EnableBot (bool enabled)
    {
        switchHandle.DOAnchorPos (enabled ? switchHandlePosition * -1 : switchHandlePosition, .4f).SetEase (Ease.InOutBack);
        switchBackgroundImage.DOColor (enabled ? backgroundActiveColor : backgroundDefaultColor, .6f);
        switchHandleImage.DOColor (enabled ? handleActiveColor : handleDefaultColor, .4f);

        PlayerPrefs.SetInt(transform.name, enabled ? 1 : 0);
        Status.text = enabled ? active ? "Active" : "Connecting.." : "Not Active";
        Status.color = enabled ? active ? green : blue : red;

        gameObject.SetActive(!BotsPage.onlyActiveBotsVisible);

        Manager.Instance.GetEnableWhatsappWorkflow(whatsappWorkflowId, enabled);
        Manager.Instance.GetEnableTelegramWorkflow(telegramWorkflowId, enabled);
    }

    private IEnumerator SetSwitches()
    {
        yield return new WaitForEndOfFrame();

        switchHandle = ActivationSwitch.transform.GetChild(0).GetChild(0).GetComponent<RectTransform>();

        switchHandle.localPosition = new Vector2(-30 * ActivationSwitch.transform.GetChild(0).GetComponent<RectTransform>().rect.width / 160, switchHandle.localPosition.y);

        switchHandlePosition = switchHandle.anchoredPosition;

        switchBackgroundImage = switchHandle.parent.GetComponent<Image>();
        switchHandleImage = switchHandle.GetComponent<Image>();

        backgroundDefaultColor = switchBackgroundImage.color;
        handleDefaultColor = switchHandleImage.color;

        if (PlayerPrefs.GetInt(transform.name, 1) == 1)
        {
            ActivationSwitch.isOn = true;

            switchHandle.DOAnchorPos(switchHandlePosition * -1, .4f).SetEase(Ease.InOutBack);
            switchBackgroundImage.DOColor(backgroundActiveColor, .6f);
            switchHandleImage.DOColor(handleActiveColor, .4f);

            if (active)
            {
                Status.text = "Active";
                Status.color = green;
            }
            else
            {
                Status.text = "Connecting..";
                Status.color = blue;
            }
        }
        else
        {
            ActivationSwitch.isOn = false;
            Status.text = "Not Active";
            Status.color = red;
        }
    }

    private void OnDestroy ()
    {
        ActivationSwitch.onValueChanged.RemoveListener (EnableBot);
    }
}

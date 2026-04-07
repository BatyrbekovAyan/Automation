using UnityEngine;
using UnityEngine.UI;

public class SettingsPage : MonoBehaviour
{
    [SerializeField] private GameObject BotsPage;

    [SerializeField] private Button BackButton;
    [SerializeField] public Button SaveButton;

    void Start()
    {
        if (BackButton != null)
        {
            BackButton.onClick.AddListener(BackToBotsList);
        }

        if (SaveButton != null)
        {
            SaveButton.onClick.AddListener(SaveSettings);
        }
    }


    public void BackToBotsList()
    {
        BotsPage.SetActive(true);
        gameObject.SetActive(false);

        SaveButton.interactable = false;

        Manager.Instance.CloseSettings();
    }

    public void SaveSettings()
    {
        SaveButton.interactable = false;

        Manager.Instance.GetSaveSettings(Manager.openBot.GetComponent<Bot>().whatsappWorkflowId, Manager.openBot.GetComponent<Bot>().telegramWorkflowId);
    }
}

using UnityEngine;
using UnityEngine.UI;

public class BotsPage : MonoBehaviour
{
    [SerializeField] private GameObject MainPage;
    [SerializeField] private GameObject BotsParent;
    [SerializeField] private GameObject Chanel;

    [SerializeField] private Button MainPageButton;
    [SerializeField] private Button AllBotsButton;
    [SerializeField] private Button ActiveBotsButton;
    [SerializeField] private Button NewBotButton;

    public static BotsPage Instance; 

    public static bool onlyActiveBotsVisible = false;

    void Start()
    {
        Instance = this;

        if (MainPageButton != null)
        {
            MainPageButton.onClick.AddListener(OpenMainPage);
        }

        if (AllBotsButton != null)
        {
            AllBotsButton.onClick.AddListener(OpenAllBots);
        }

        if (ActiveBotsButton != null)
        {
            ActiveBotsButton.onClick.AddListener(OpenActiveBots);
        }

        if (NewBotButton != null)
        {
            NewBotButton.onClick.AddListener(CreateBot);
        }
    }

    public void OpenMainPage()
    {
        gameObject.SetActive(false);
        MainPage.SetActive(true);
    }

    public void OpenAllBots()
    {
        onlyActiveBotsVisible = false;

        if (BotsParent.transform.childCount != 0)
        {
            foreach (Transform bot in BotsParent.transform)
            {
                bot.gameObject.SetActive(true);
            }
        }
    }

    public void OpenActiveBots()
    {
        onlyActiveBotsVisible = true;

        if (BotsParent.transform.childCount != 0)
        {
            foreach (Transform bot in BotsParent.transform)
            {
                if (!bot.GetChild(1).GetComponent<Toggle>().isOn)
                {
                    bot.gameObject.SetActive(false);
                }
            }
        }
    }

    public void CreateBot()
    {
        gameObject.SetActive(false);
        Chanel.SetActive(true);
    }
}

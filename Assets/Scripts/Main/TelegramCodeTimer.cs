using UnityEngine;
using System;
using TMPro;
using System.Collections;
using UnityEngine.UI;

public class TelegramCodeTimer : MonoBehaviour
{
    [SerializeField] private Button GetTelegramCodeButton;
    [SerializeField] private TMP_InputField TelegramNumberInput;

    private int cooldown = 30;
    private DateTime cooldownFinishTime;


    private void OnEnable()
    {
        //cooldown = 30;

        if (PlayerPrefs.GetString("TelegramCooldownFinishTime", "-1").Equals("-1"))
        {
            cooldownFinishTime = DateTime.Now.AddSeconds(cooldown);
            PlayerPrefs.SetString("TelegramCooldownFinishTime", cooldownFinishTime.ToString());
        }
        else
        {
            cooldownFinishTime = StringToDate(PlayerPrefs.GetString("TelegramCooldownFinishTime", "-1"));
        }

        StartCoroutine(StartCountdown());
    }

    private IEnumerator StartCountdown()
    {
        while (DateTime.Now < cooldownFinishTime)
        {
            UpdateTimerText();
            yield return new WaitForSecondsRealtime (1f);
        }

        if (TelegramNumberInput.text.Length >= 11)
        {
            GetTelegramCodeButton.interactable = true;
        }

        PlayerPrefs.SetString("TelegramCooldownFinishTime", "-1");

        gameObject.SetActive(false);

        yield return null;
    }

    public void UpdateTimerText()
    {
        TimeSpan time = cooldownFinishTime - DateTime.Now;

        string timeValue;

        if (PlayerPrefs.GetInt("Locale", 0) == 0)
        {
            if (time.Minutes > 0)
            {
                timeValue = string.Format("{0:D1}", time.Minutes) + "m " + string.Format("{0:D1}", time.Seconds) + "s";
            }
            else
            {
                timeValue = string.Format("{0:D1}", time.Seconds) + "s";
            }
        }
        else
        {
            if (time.Minutes > 0)
            {
                timeValue = string.Format("{0:D1}", time.Minutes) + ":" + string.Format("{0:D1}", time.Seconds);
            }
            else
            {
                timeValue = string.Format("{0:D1}", time.Seconds);
            }
        }


        GetComponent<TextMeshProUGUI>().text = timeValue;
    }

    private DateTime StringToDate(string dateTime)
    {
        if (string.IsNullOrEmpty(dateTime) || dateTime.Equals("-1"))
        {
            return DateTime.Now;
        }
        else
        {
            return DateTime.Parse(dateTime);
        }
    }
}
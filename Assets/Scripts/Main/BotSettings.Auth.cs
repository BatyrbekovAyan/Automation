using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;
using System;
using System.IO;
using Automation.BotSettingsUI;

public partial class BotSettings
{
    // All auth methods, moved verbatim from the old BotSettings.cs.
    // Only re-wire: *.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text
    // patterns on BotNameButton/WhatsappNumberButton/TelegramNumberButton
    // become .Value accesses on the corresponding EditableField. Logic
    // unchanged.

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

    public void OpenConfirmChangeWhatsappNumberPopup() => PopupUI.Show(ConfirmChangeWhatsappNumberPopup);

    public void ConfirmChangeWhatsappNumber()
    {
        StartCoroutine(UnauthorizeWhatsapp());
        PopupUI.Hide(ConfirmChangeWhatsappNumberPopup);
        OpenWhatsappAuthorization(true);
    }

    public void CancelChangeWhatsappNumber() => PopupUI.Hide(ConfirmChangeWhatsappNumberPopup);

    public void OpenConfirmChangeTelegramNumberPopup() => PopupUI.Show(ConfirmChangeTelegramNumberPopup);

    public void ConfirmChangeTelegramNumber()
    {
        StartCoroutine(UnauthorizeTelegram());
        PopupUI.Hide(ConfirmChangeTelegramNumberPopup);
        OpenTelegramAuthorization(true);
    }

    public void CancelChangeTelegramNumber() => PopupUI.Hide(ConfirmChangeTelegramNumberPopup);

    private void OpenAuthorization(bool open)
    {
        General.transform.parent.gameObject.SetActive(!open);
        GeneralTabButton.transform.parent.gameObject.SetActive(!open);

        transform.parent.parent.GetChild(0).gameObject.SetActive(!open);
        transform.parent.parent.GetChild(1).gameObject.SetActive(!open);
        transform.parent.parent.GetChild(2).gameObject.SetActive(!open);
    }


    //////////////////////////////////////////////////////////WHATSAPP AUTHORIZATION//////////////////////////////////////////////////////////

    private static void SetButtonText(Button btn, string text)
    {
        if (btn == null) return;
        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) tmp.text = text;
    }

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

        WhatsappNumberField.Value = "";
        WhatsappNumberField.gameObject.SetActive(false);
        PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", "");
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 0);

        Manager.Instance.EnableSave();

        Manager.Instance.GetDeleteWhatsappProfile(Manager.openBot.GetComponent<Bot>().whatsappProfileId);

        OpenWhatsappAuthorization(false);
    }

    public void WhatsappAuthorizationDone()
    {
        WhatsappAuthorizationDoneBotton.interactable = false;

        WhatsappNumberField.Value = WhatsappNumberInput.text;
        WhatsappNumberField.gameObject.SetActive(true);
        PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", WhatsappNumberField.Value);
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
        SetButtonText(GetWhatsappCodeButton, "Получить код");

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

        string originalBtnText = GetWhatsappCodeButton.GetComponentInChildren<TextMeshProUGUI>().text;
        SetButtonText(GetWhatsappCodeButton, "Getting..");


        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/auth/code?profile_id={Manager.openBot.GetComponent<Bot>().whatsappProfileId}&phone={WhatsappNumberInput.text}");

        www.SetRequestHeader("Authorization", Manager.wappiAuthToken);

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
            SetButtonText(GetWhatsappCodeButton, originalBtnText);

            if (WhatsappNumberInput.text.Length >= 11)
            {
                GetWhatsappCodeButton.interactable = true;
            }
        }
        else
        {
            SetButtonText(GetWhatsappCodeButton, "Получить другой код");
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
        SetButtonText(GetWhatsappCodeButton, "Получить код");

        WhatsappNumberInput.gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(false);
        WhatsappCodePanel.transform.GetChild(6).gameObject.SetActive(false);

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

            Manager.Instance.GetCreateWhatsappProfile(BotNameField.Value);

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

                        WhatsappNumberField.Value = response.Substring(startIndex, lenght);
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

                    WhatsappNumberField.Value = "";
                    WhatsappNumberField.gameObject.SetActive(false);

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

            WhatsappNumberField.gameObject.SetActive(false);
            WhatsappNumberField.Value = "";
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

        TelegramNumberField.Value = "";
        TelegramNumberField.gameObject.SetActive(false);
        PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", "");
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 0);

        Manager.Instance.EnableSave();

        Manager.Instance.GetDeleteTelegramProfile(Manager.openBot.GetComponent<Bot>().telegramProfileId);

        OpenTelegramAuthorization(false);
    }

    public void TelegramAuthorizationDone()
    {
        TelegramAuthorizationDoneBotton.interactable = false;

        TelegramNumberField.Value = TelegramNumberInput.text;
        TelegramNumberField.gameObject.SetActive(true);
        PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", TelegramNumberField.Value);
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
        SetButtonText(GetTelegramCodeButton, "Получить код");

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

        string originalBtnText = GetTelegramCodeButton.GetComponentInChildren<TextMeshProUGUI>().text;
        SetButtonText(GetTelegramCodeButton, "Sending..");


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
            SetButtonText(GetTelegramCodeButton, originalBtnText);

            if (TelegramNumberInput.text.Length >= 11)
            {
                GetTelegramCodeButton.interactable = true;
            }
        }
        else
        {
            SetButtonText(GetTelegramCodeButton, "Sent");
            PlayerPrefs.SetString("TelegramCooldownFinishTime", DateTime.Now.AddSeconds(30).ToString());

            TelegramNumberInput.gameObject.SetActive(false);
            TelegramCodeInput.gameObject.SetActive(true);
            SendTelegramCodeButton.gameObject.SetActive(true);
            TelegramCodePanel.transform.GetChild(6).gameObject.SetActive(true);
            SetTelegramCodeEntryTexts(true);


            string response = www.downloadHandler.text;

            if (response.Contains("\"status\":\""))
            {
                int startIndex = response.IndexOf("\"status\":\"") + 10;

                if (response.Substring(startIndex, 4).Equals("done"))
                {
                    yield return new WaitForSeconds(2f);
                }
            }

            SetButtonText(GetTelegramCodeButton, "Получить другой код");
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

        SetButtonText(SendTelegramCodeButton, "Authorizing..");


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
                    SetButtonText(SendTelegramCodeButton, "Authorization Complete");

                    StartCoroutine(GetTelegramProfileStatus());

                    yield return new WaitForSeconds(2f);
                    SetButtonText(SendTelegramCodeButton, "Подтвердить код");
                }
                else
                {
                    SetButtonText(SendTelegramCodeButton, "Authorization Failed");

                    yield return new WaitForSeconds(2f);
                    SetButtonText(SendTelegramCodeButton, "Подтвердить код");
                }
            }
            else
            {
                SetButtonText(SendTelegramCodeButton, "Authorization Failed");

                yield return new WaitForSeconds(2f);
                SetButtonText(SendTelegramCodeButton, "Подтвердить код");
            }
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }

    public void CloseTelegramCodePanel()
    {
        TelegramCodePanel.SetActive(false);
        SetButtonText(GetTelegramCodeButton, "Получить код");

        TelegramNumberInput.gameObject.SetActive(true);
        TelegramCodeInput.gameObject.SetActive(false);
        SendTelegramCodeButton.gameObject.SetActive(false);
        TelegramCodePanel.transform.GetChild(6).gameObject.SetActive(false);
        SetTelegramCodeEntryTexts(false);

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

            Manager.Instance.GetCreateTelegramProfile(BotNameField.Value);

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

                        TelegramNumberField.Value = response.Substring(startIndex, lenght);
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

                    TelegramNumberField.Value = "";
                    TelegramNumberField.gameObject.SetActive(false);

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

            TelegramNumberField.gameObject.SetActive(false);
            TelegramNumberField.Value = "";
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

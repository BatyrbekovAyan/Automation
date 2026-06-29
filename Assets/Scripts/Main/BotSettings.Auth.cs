using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Text;
using System.IO;
using Automation.BotSettingsUI;

public partial class BotSettings
{
    // Auth is now fully delegated to Manager's shared auth page via
    // ShowWhatsappAuthFromSettings / ShowTelegramAuthFromSettings. The old
    // in-prefab QR / code panels, number inputs, code timers, and
    // done/back buttons were deleted together with the
    // WhatsappAuthorization / TelegramAuthorization GameObjects.

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
        ShowWhatsappAuthFromSettings(Manager.openBot.GetComponent<Bot>().whatsappProfileId);
    }

    public void CancelChangeWhatsappNumber() => PopupUI.Hide(ConfirmChangeWhatsappNumberPopup);

    public void OpenConfirmChangeTelegramNumberPopup() => PopupUI.Show(ConfirmChangeTelegramNumberPopup);

    public void ConfirmChangeTelegramNumber()
    {
        StartCoroutine(UnauthorizeTelegram());
        PopupUI.Hide(ConfirmChangeTelegramNumberPopup);
        ShowTelegramAuthFromSettings(Manager.openBot.GetComponent<Bot>().telegramProfileId);
    }

    public void CancelChangeTelegramNumber() => PopupUI.Hide(ConfirmChangeTelegramNumberPopup);


    //////////////////////////////////////////////////////////WHATSAPP AUTHORIZATION//////////////////////////////////////////////////////////

    private IEnumerator CheckWhatsappAuthorization()
    {
        var bot = Manager.openBot.GetComponent<Bot>();

        if (bot.whatsappProfileId.Equals("-1"))
        {
            // Fresh auth path: provision a new Wappi profile for this bot, then
            // show Manager's shared auth page using the newly assigned id.
            Manager.Instance.GetCreateWhatsappProfile(BotNameField.Value);

            float elapsed = 0f;
            while (bot.whatsappProfileId.Equals("-1") && elapsed < 10f)
            {
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }

            if (bot.whatsappProfileId.Equals("-1"))
            {
                if (whatsappRow != null) whatsappRow.SetIsOnQuiet(false);
                yield break;
            }

            ShowWhatsappAuthFromSettings(bot.whatsappProfileId);
            yield break;
        }


        Manager.Instance.LoadingPanel.SetActive(true);

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/get/status?profile_id={bot.whatsappProfileId}");

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
                    ShowWhatsappAuthFromSettings(bot.whatsappProfileId);
                }
            }
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }

    private void ShowWhatsappAuthFromSettings(string profileId)
    {
        Manager.Instance.OpenWhatsappAuthFromSettings(
            profileId: profileId,
            onDone: OnWhatsappAuthFromSettingsDone,
            onBack: OnWhatsappAuthFromSettingsBack);
    }

    private void OnWhatsappAuthFromSettingsDone()
    {
        WhatsappNumberField.Value = Manager.Instance.LastAuthedWhatsappNumber;
        WhatsappNumberField.gameObject.SetActive(!string.IsNullOrEmpty(WhatsappNumberField.Value));

        PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", WhatsappNumberField.Value);
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 1);
        PlayerPrefs.SetString(Manager.openBot.name + "WhatsappProfileId", Manager.openBot.GetComponent<Bot>().whatsappProfileId);

        Manager.Instance.GetCreateWhatsappWorkflow();
    }

    private void OnWhatsappAuthFromSettingsBack()
    {
        // whatsappRow.SetIsOnQuiet updates isOn AND moves the thumb/retints
        // the track in one call; plain Toggle.SetIsOnWithoutNotify skips the
        // ToggleRow animation listener, leaving the control looking "on".
        if (whatsappRow != null) whatsappRow.SetIsOnQuiet(false);

        if (WhatsappNumberField != null)
        {
            WhatsappNumberField.Value = "";
            WhatsappNumberField.gameObject.SetActive(false);
        }

        PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", "");
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 0);

        Manager.Instance.EnableSave();
        Manager.Instance.GetDeleteWhatsappProfile(Manager.openBot.GetComponent<Bot>().whatsappProfileId);
    }

    private IEnumerator CheckWhatsappUnauthorizationOutsideApp()
    {
        // Silent background probe fired from OnEnable. No LoadingPanel — it
        // would overlay the slide-in animation. User-triggered logout
        // (UnauthorizeWhatsapp) still shows LoadingPanel because that is a
        // foreground action the user expects to see.
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
                    if (whatsappRow != null) whatsappRow.SetIsOnQuiet(false);

                    WhatsappNumberField.Value = "";
                    WhatsappNumberField.gameObject.SetActive(false);

                    PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", "");
                    PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 0);

                    Manager.Instance.GetDeleteWhatsappProfile(Manager.openBot.GetComponent<Bot>().whatsappProfileId);
                }
            }
        }
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

            PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", "");
            PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 0);
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }


    //////////////////////////////////////////////////////////TELEGRAM AUTHORIZATION//////////////////////////////////////////////////////////

    private IEnumerator CheckTelegramAuthorization()
    {
        var bot = Manager.openBot.GetComponent<Bot>();

        if (bot.telegramProfileId.Equals("-1"))
        {
            Manager.Instance.GetCreateTelegramProfile(BotNameField.Value);

            float elapsed = 0f;
            while (bot.telegramProfileId.Equals("-1") && elapsed < 10f)
            {
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }

            if (bot.telegramProfileId.Equals("-1"))
            {
                if (telegramRow != null) telegramRow.SetIsOnQuiet(false);
                yield break;
            }

            ShowTelegramAuthFromSettings(bot.telegramProfileId);
            yield break;
        }


        Manager.Instance.LoadingPanel.SetActive(true);

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/tapi/sync/get/status?profile_id={bot.telegramProfileId}");

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
                    ShowTelegramAuthFromSettings(bot.telegramProfileId);
                }
            }
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }

    private void ShowTelegramAuthFromSettings(string profileId)
    {
        Manager.Instance.OpenTelegramAuthFromSettings(
            profileId: profileId,
            onDone: OnTelegramAuthFromSettingsDone,
            onBack: OnTelegramAuthFromSettingsBack);
    }

    private void OnTelegramAuthFromSettingsDone()
    {
        TelegramNumberField.Value = Manager.Instance.LastAuthedTelegramNumber;
        TelegramNumberField.gameObject.SetActive(!string.IsNullOrEmpty(TelegramNumberField.Value));

        PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", TelegramNumberField.Value);
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 1);
        PlayerPrefs.SetString(Manager.openBot.name + "TelegramProfileId", Manager.openBot.GetComponent<Bot>().telegramProfileId);

        Manager.Instance.GetCreateTelegramWorkflow();
    }

    private void OnTelegramAuthFromSettingsBack()
    {
        if (telegramRow != null) telegramRow.SetIsOnQuiet(false);

        if (TelegramNumberField != null)
        {
            TelegramNumberField.Value = "";
            TelegramNumberField.gameObject.SetActive(false);
        }

        PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", "");
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 0);

        Manager.Instance.EnableSave();
        Manager.Instance.GetDeleteTelegramProfile(Manager.openBot.GetComponent<Bot>().telegramProfileId);
    }

    private IEnumerator CheckTelegramUnauthorizationOutsideApp()
    {
        // Silent background probe fired from OnEnable. No LoadingPanel — it
        // would overlay the slide-in animation. User-triggered logout
        // (UnauthorizeTelegram) still shows LoadingPanel because that is a
        // foreground action the user expects to see.
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
                    if (telegramRow != null) telegramRow.SetIsOnQuiet(false);

                    TelegramNumberField.Value = "";
                    TelegramNumberField.gameObject.SetActive(false);

                    PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", "");
                    PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 0);

                    Manager.Instance.GetDeleteTelegramProfile(Manager.openBot.GetComponent<Bot>().telegramProfileId);
                }
            }
        }
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

            PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", "");
            PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 0);
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }




    private void UploadPriceList()
    {
        InitializeFilePickerTypes();
        PickMediaFile("product", UploadProductsPriceListButton);
    }

    private void UploadServiceList()
    {
        InitializeFilePickerTypes();
        PickMediaFile("service", UploadServicesPriceListButton);
    }

    private void InitializeFilePickerTypes()
    {
        pdf = NativeFilePicker.ConvertExtensionToFileType("pdf"); // Returns "application/pdf" on Android and "com.adobe.pdf" on iOS
        txt = NativeFilePicker.ConvertExtensionToFileType("txt");
        rtf = NativeFilePicker.ConvertExtensionToFileType("rtf");
        xml = NativeFilePicker.ConvertExtensionToFileType("xml");
        csv = NativeFilePicker.ConvertExtensionToFileType("csv");
        xls = NativeFilePicker.ConvertExtensionToFileType("xls");
        xlsx = NativeFilePicker.ConvertExtensionToFileType("xlsx");
        docx = "org.openxmlformats.wordprocessingml.document";
    }

    private void PickMediaFile(string contentType, Button targetButton)
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
                    StartCoroutine(UploadFile(paths[i], contentType, targetButton));
                }
            }
        }, fileTypes);
    }

    private IEnumerator UploadFile(string filePath, string contentType, Button targetButton)
    {
        WWWForm form = new();

        form.AddField("whatsappWorkflowId", Manager.openBot.GetComponent<Bot>().whatsappWorkflowId);
        form.AddField("telegramWorkflowId", Manager.openBot.GetComponent<Bot>().telegramWorkflowId);
        form.AddField("contentType", contentType);

        byte[] fileData = File.ReadAllBytes(filePath);
        string fileName = Path.GetFileName(filePath);
        string fileExtension = Path.GetExtension(filePath);
        targetButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = fileExtension;

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
            string text = TableToTextConverter.Convert(fileData, fileName, contentType);

            form.AddBinaryData("data", Encoding.UTF8.GetBytes(text), fileName + ".txt", "text/plain");
        }

        else if (fileExtension.Equals(".docx"))
        {
            string text = DocxToTextConverter.Convert(fileData);

            form.AddBinaryData("data", Encoding.UTF8.GetBytes(text), fileName + ".txt", "text/plain");
        }


        using UnityWebRequest www = UnityWebRequest.Post($"{Manager.n8nBaseUrl}/webhook/UploadFile", form);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {

        }
        else
        {
            targetButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = www.downloadHandler.text;
        }
    }
}

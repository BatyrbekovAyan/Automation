using System.Collections;
using System.Collections.Generic;
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




    // Tapping «Загрузить прайс-лист» no longer opens the picker directly — it
    // opens the source sheet («Файл» / «Фото из галереи») so photos of price
    // boards reach the iPhone Photos library, not just the document picker.
    private void UploadPriceList()
    {
        ShowUploadSourceSheet("product", UploadProductsPriceListButton);
    }

    private void UploadServiceList()
    {
        ShowUploadSourceSheet("service", UploadServicesPriceListButton);
    }

    private void ShowUploadSourceSheet(string contentType, Button targetButton)
    {
        pendingUploadContentType = contentType;
        pendingUploadButton = targetButton;

        // No sheet baked into this prefab yet — degrade gracefully to the old
        // direct-picker behaviour so uploads still work.
        if (uploadSourceSheet == null)
        {
            InitializeFilePickerTypes();
            PickMediaFile(contentType, targetButton);
            return;
        }

        uploadSourceSheet.Show();
    }

    // Wired to the sheet's «Файл» button (via UploadSourceSheet.OnFilePressed).
    // Runs the existing document picker, now including image types.
    public void OnUploadSourceFilePressed()
    {
        if (uploadSourceSheet != null) uploadSourceSheet.Hide();
        InitializeFilePickerTypes();
        PickMediaFile(pendingUploadContentType, pendingUploadButton);
    }

    // Wired to the sheet's «Фото из галереи» button (via
    // UploadSourceSheet.OnGalleryPressed). Multi-selects photos and reuses the
    // same UploadFile coroutine — the image branch there decodes/downscales and
    // the workflow's vision branch extracts the prices.
    public void OnUploadSourceGalleryPressed()
    {
        if (uploadSourceSheet != null) uploadSourceSheet.Hide();

        // Snapshot the pending context: the callback runs asynchronously and a
        // later tap could overwrite the fields before it fires.
        string contentType = pendingUploadContentType;
        Button targetButton = pendingUploadButton;

        NativeGallery.GetImagesFromGallery(paths =>
        {
            if (paths == null) return; // cancelled

            // Synthesize display names: the temp copies iOS hands back are all
            // named pickedMediaN.jpg (reused across pick sessions), which reads
            // as duplicates in the list and cross-fires the replace-by-name
            // flow — a later photo would silently replace an earlier one's
            // knowledge (see GalleryPhotoNamer).
            Bot openBot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
            var takenNames = new HashSet<string>();
            if (openBot != null)
            {
                foreach (UploadedFileEntry entry in UploadedFilesStore.Load(openBot.name, contentType))
                    takenNames.Add(entry.Name);
            }

            int index = 0;
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                string displayName = GalleryPhotoNamer.DisplayName(System.DateTime.Now, index, paths.Length, takenNames);
                takenNames.Add(displayName);
                index++;
                StartCoroutine(UploadFile(path, contentType, targetButton, displayName));
            }
        }, "Выберите фото прайс-листа");
    }

    private void InitializeFilePickerTypes()
    {
        pdf = NativeFilePicker.ConvertExtensionToFileType("pdf"); // Returns "application/pdf" on Android and "com.adobe.pdf" on iOS
        txt = NativeFilePicker.ConvertExtensionToFileType("txt");
        rtf = NativeFilePicker.ConvertExtensionToFileType("rtf");
        xml = NativeFilePicker.ConvertExtensionToFileType("xml");
        csv = NativeFilePicker.ConvertExtensionToFileType("csv");
        tsv = NativeFilePicker.ConvertExtensionToFileType("tsv");
        xls = NativeFilePicker.ConvertExtensionToFileType("xls");
        xlsx = NativeFilePicker.ConvertExtensionToFileType("xlsx");
        xlsm = NativeFilePicker.ConvertExtensionToFileType("xlsm");
        docx = "org.openxmlformats.wordprocessingml.document";
        doc = NativeFilePicker.ConvertExtensionToFileType("doc"); // application/msword / com.microsoft.word.doc
        html = NativeFilePicker.ConvertExtensionToFileType("html"); // text/html / public.html also cover .htm
        jpg = NativeFilePicker.ConvertExtensionToFileType("jpg"); // also covers .jpeg
        png = NativeFilePicker.ConvertExtensionToFileType("png");
        webp = NativeFilePicker.ConvertExtensionToFileType("webp");
        heic = NativeFilePicker.ConvertExtensionToFileType("heic");
    }

    private void PickMediaFile(string contentType, Button targetButton)
    {
#if UNITY_ANDROID
				// Use MIMEs on Android
            string[] fileTypes = new string[] { pdf, txt, rtf, xml, csv, tsv, xls, xlsx, xlsm, docx, doc, html, jpg, png, webp, heic };
#else
        // Use UTIs on iOS
        string[] fileTypes = new string[] { pdf, txt, rtf, xml, csv, tsv, xls, xlsx, xlsm, docx, doc, html, jpg, png, webp, heic };
#endif
        // Older Androids have no MIME registered for tsv/xlsm — drop nulls so
        // the picker intent doesn't choke on them.
        fileTypes = System.Array.FindAll(fileTypes, type => !string.IsNullOrEmpty(type));
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

    private IEnumerator UploadFile(string filePath, string contentType, Button targetButton, string displayNameOverride = null)
    {
        Bot openBot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
        if (openBot == null)
        {
            Debug.LogError("[UploadFile] No open bot (Manager.openBot or its Bot component is null) — aborting upload.");
            yield break;
        }

        // Gallery picks pass a synthesized display name: iOS temp copies are
        // all named pickedMediaN.jpg (reused every session), which both looks
        // broken in the list and cross-matches the replace-by-name flow.
        string fileName = displayNameOverride ?? Path.GetFileName(filePath);
        // Lowercased: mobile pickers filter by MIME/UTI, not by name, so a
        // "MENU.PDF" is perfectly pickable — and an ordinal Equals(".pdf")
        // would match no branch and post the form with no file attached.
        string fileExtension = Path.GetExtension(filePath).ToLowerInvariant();

        // A same-named upload replaces the existing file's knowledge — ask
        // before uploading anything. Cancel = no upload, the old file stays.
        if (UploadedFilesStore.FindByName(openBot.name, contentType, fileName).Count > 0)
        {
            bool replaceConfirmed = false;
            yield return RequestReplaceFileDecision(fileName, decision => replaceConfirmed = decision);
            if (!replaceConfirmed) yield break;
        }

        // Optimistic feedback: the row appears in «Прайс-листы» immediately, in
        // an uploading state — the n8n webhook takes a few seconds (extraction +
        // embedding) and a silent gap reads as "did my tap even register?".
        GameObject pendingRow = AddPendingFileRow(contentType, fileName);
        System.Action retryUpload = () => StartCoroutine(UploadFile(filePath, contentType, targetButton, displayNameOverride));

        byte[] fileData = ReadFileOrNull(filePath);
        if (fileData == null)
        {
            MarkPendingRowFailed(pendingRow, contentType, retryUpload);
            yield break;
        }

        WWWForm form = new();

        form.AddField("whatsappWorkflowId", openBot.whatsappWorkflowId);
        form.AddField("telegramWorkflowId", openBot.telegramWorkflowId);
        form.AddField("contentType", contentType);

        // Mint a stable per-file id up front and send it with the upload. The n8n
        // UploadFile workflow stamps this onto every RAG chunk (metadata.fileId), so
        // the per-file delete (X) can later remove exactly this file's chunks.
        string fileId = System.Guid.NewGuid().ToString();
        form.AddField("fileId", fileId);

        // Every non-PDF format converts to plain text ON-DEVICE (the workflow
        // only ingests text/plain + PDF). Converted text is validated below:
        // an empty conversion or a converter throw fails the row honestly
        // instead of uploading zero knowledge or hanging the coroutine.
        byte[] payloadBytes = null;
        string payloadName = null;
        string payloadMime = null;
        string convertedText = null;
        string failReason = null;   // dev-facing, goes to the error log
        string failReasonRu = null; // user-facing, shown in the row (deterministic failures)

        try
        {
            if (fileExtension.Equals(".pdf"))
            {
                payloadBytes = fileData;
                payloadName = fileName;
                payloadMime = "application/pdf";
            }
            else if (fileExtension.Equals(".txt"))
            {
                // Old-Notepad/1C TXT is often windows-1251 or UTF-16 — the
                // workflow assumes UTF-8, so those used to ingest as mojibake.
                convertedText = TextEncodingSniffer.Decode(fileData);
                payloadName = fileName;
            }
            else if (fileExtension.Equals(".rtf"))
            {
                convertedText = RtfToTextConverter.Convert(fileData);
                payloadName = fileName + ".txt";
            }
            else if (fileExtension.Equals(".xml"))
            {
                // Byte overload honors the prolog's declared encoding
                // (1C/CommerceML exports are commonly windows-1251).
                convertedText = XmlToTextConverter.ConvertXmlToText(fileData);
                payloadName = Path.ChangeExtension(fileName, ".txt");
            }
            else if (fileExtension.Equals(".csv") || fileExtension.Equals(".tsv")
                || fileExtension.Equals(".xls") || fileExtension.Equals(".xlsx") || fileExtension.Equals(".xlsm"))
            {
                convertedText = TableToTextConverter.Convert(fileData, fileName, contentType);
                payloadName = fileName + ".txt";
            }
            else if (fileExtension.Equals(".html") || fileExtension.Equals(".htm"))
            {
                convertedText = HtmlTableToTextConverter.Convert(fileData, contentType);
                payloadName = fileName + ".txt";
            }
            else if (fileExtension.Equals(".docx"))
            {
                convertedText = DocxToTextConverter.Convert(fileData);
                payloadName = fileName + ".txt";
            }
            else if (fileExtension.Equals(".jpg") || fileExtension.Equals(".jpeg") || fileExtension.Equals(".png")
                || fileExtension.Equals(".webp") || fileExtension.Equals(".heic"))
            {
                // Photos of menus/price boards: decode (HEIC included on device),
                // downscale, re-encode JPEG; the workflow's vision branch extracts text.
                payloadBytes = ImageUploadPreprocessor.ToJpegPayload(filePath);
                if (payloadBytes == null)
                {
                    failReason = "image decode/downscale/re-encode failed (undecodable, missing, or degenerate)";
                    failReasonRu = UploadFailureText.PhotoUndecodable;
                }
                else
                {
                    // Route on the NAME's final extension (synthesized gallery
                    // names already end in .jpg regardless of the temp file's
                    // real extension) — the workflow's Switch reads the name.
                    payloadName = fileName.EndsWith(".jpg", System.StringComparison.OrdinalIgnoreCase)
                        ? fileName
                        : fileName + ".jpg";
                    payloadMime = "image/jpeg";
                }
            }
            else
            {
                // Android pickers can ignore the MIME filter — without this
                // guard the form would post with no file part at all.
                failReason = fileExtension.Equals(".doc")
                    ? "'.doc' (Word 97-2003) is not supported — ask the user to re-save as .docx or PDF"
                    : $"unsupported file type '{fileExtension}'";
                failReasonRu = UploadFailureText.UnsupportedFormat(fileExtension);
            }

            if (failReason == null && payloadBytes == null)
            {
                if (string.IsNullOrWhiteSpace(convertedText))
                {
                    failReason = "converted to empty text (nothing to ingest)";
                    failReasonRu = UploadFailureText.EmptyFile;
                }
                else
                {
                    payloadBytes = Encoding.UTF8.GetBytes(convertedText);
                    payloadMime = "text/plain";
                }
            }
        }
        catch (System.Exception exception)
        {
            failReason = $"conversion failed: {exception.Message}";
            failReasonRu = UploadFailureText.Unreadable;
        }

        if (failReason != null)
        {
            // Deterministic: the same file will fail the same way, so the row
            // shows WHY (in Russian) and offers no retry — only the ✕.
            Debug.LogError($"[UploadFile] '{fileName}': {failReason} — upload aborted.");
            MarkPendingRowFailed(pendingRow, contentType, retry: null, failReasonRu);
            yield break;
        }

        form.AddBinaryData("data", payloadBytes, payloadName, payloadMime);

        using UnityWebRequest www = UnityWebRequest.Post($"{Manager.n8nBaseUrl}/webhook/UploadFile", form);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            // Deterministic server verdicts (e.g. a photo with no visible prices)
            // retrying the same file cannot fix — surface the specific reason and
            // suppress retry, same as the client-side deterministic failures above.
            string deterministicReason = UploadFailureText.ReasonForHttpResponse(www.responseCode, www.downloadHandler?.text);
            if (deterministicReason != null)
            {
                Debug.LogError($"[UploadFile] '{fileName}': {deterministicReason} ({www.responseCode})");
                MarkPendingRowFailed(pendingRow, contentType, retry: null, deterministicReason);
                yield break;
            }

            Debug.LogError($"[UploadFile] Upload failed ({www.responseCode} {www.result}): {www.error}\n{www.downloadHandler?.text}");
            MarkPendingRowFailed(pendingRow, contentType, retryUpload);
        }
        else
        {
            // Re-uploading a same-named file REPLACES it: delete the superseded
            // upload's RAG chunks (by old fileId), or stale and current prices would
            // coexist in the vector store and retrieval could quote either. The new
            // chunks are already inserted under a fresh fileId, so this is safe.
            foreach (UploadedFileEntry stale in UploadedFilesStore.FindByName(openBot.name, contentType, fileName))
            {
                if (stale.Id == fileId) continue; // never target the fresh upload
                StartCoroutine(DeleteReplacedFileRoutine(openBot.name, contentType, stale.Id));
            }

            // Remember the upload on-device so the file survives closing/reopening the bot,
            // and so the per-file delete (X) can target this fileId in the RAG store.
            UploadedFilesStore.Add(openBot.name, contentType, new UploadedFileEntry
            {
                Id = fileId,
                Name = fileName,
                Size = fileData.Length,
                DateUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // The pending row settles into the real stored row (with a small
            // pop) — the list is the upload confirmation, the button label
            // stays a constant call-to-action.
            CompletePendingFileRow(pendingRow, contentType);
        }
    }

    private static byte[] ReadFileOrNull(string filePath)
    {
        try
        {
            return File.ReadAllBytes(filePath);
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"[UploadFile] Could not read '{filePath}': {exception.Message}");
            return null;
        }
    }
}

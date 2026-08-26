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

    // BOTH directions must recompute the Save verdict. Only the OFF branch used
    // to, so turning a channel back ON — which returns the row to its persisted
    // value and must DIM Save again — left the button showing the pre-toggle
    // state, and turning ON a channel that was saved OFF left Save dim on a
    // genuine change (the toggle then silently reverted on back). The
    // authorization check is a separate, network-dependent concern: it may
    // fail, time out, or take neither branch, so the dirty check must never
    // depend on it.
    public void WhatsappChannelToggleChanged(bool isOn)
    {
        Manager.Instance.EnableSave();
        if (isOn) StartCoroutine(CheckWhatsappAuthorization());
    }

    public void TelegramChannelToggleChanged(bool isOn)
    {
        Manager.Instance.EnableSave();
        if (isOn) StartCoroutine(CheckTelegramAuthorization());
    }

    // Forces a channel row to a state without re-entering the auth flow, then
    // recomputes Save. ToggleRow.SetIsOnQuiet goes through
    // Toggle.SetIsOnWithoutNotify, so the toggle listener — and with it the
    // dirty check — never runs. Most call sites also rewrite the bot's
    // PlayerPrefs right before calling this; the profile-creation-timeout
    // bail-outs don't, and there the recompute correctly re-lights Save when
    // the snapped-off row no longer matches a saved ON.
    private static void SetChannelRowQuiet(ToggleRow row, bool isOn)
    {
        if (row != null) row.SetIsOnQuiet(isOn);
        Manager.Instance.EnableSave();
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

        if (bot.whatsappProfileId.Equals(Bot.UnauthedProfileSentinel))
        {
            // Fresh path: a new Wappi profile is about to be provisioned, consuming a
            // channel slot. Re-auth of an EXISTING profile (the branch below) reuses
            // its slot and is deliberately NOT gated.
            if (!EntitlementGate.CanConnectChannel(EntitlementGate.ConnectedChannelCount()))
            {
                EntitlementGate.RequestPaywall(PaywallTrigger.ChannelLimit);
                SetChannelRowQuiet(whatsappRow, false);
                yield break;
            }

            // Fresh auth path: provision a new Wappi profile for this bot, then
            // show Manager's shared auth page using the newly assigned id.
            Manager.Instance.GetCreateWhatsappProfile(BotNameField.Value);

            float elapsed = 0f;
            while (bot.whatsappProfileId.Equals(Bot.UnauthedProfileSentinel) && elapsed < 10f)
            {
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }

            if (bot.whatsappProfileId.Equals(Bot.UnauthedProfileSentinel))
            {
                SetChannelRowQuiet(whatsappRow, false);
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

            // WR-03: parse through the throw-safe, whitespace/order-agnostic WappiStatusParser
            // (already adopted by the Telegram twin below). The old scan derived a length from an
            // UNGUARDED IndexOf, so any body carrying "authorized": without the exact compact
            // ",\"authorized_at\":" token threw a negative-length Substring — killing this
            // coroutine before LoadingPanel.SetActive(false) and stranding the full-screen overlay.
            //
            // Two intended deltas vs the old scan (both fail-safer, see 11-REVIEW-FIX.md):
            //  • a present-but-non-boolean "authorized" (e.g. null) now takes NEITHER branch,
            //    where the old slice fell into the re-auth branch;
            //  • the phone is read whenever the body carries one, instead of requiring the
            //    adjacent ",\"platform\":" token the WhatsApp body may not even contain.
            // The write is dirty-checked so a background status probe can never light Save on
            // its own (EnableSave compares against PlayerPrefs, and the server may format the
            // number differently from what the wizard stored).
            if (WappiStatusParser.TryGetAuthorized(response, out bool isAuthorized))
            {
                if (isAuthorized)
                {
                    // Task 15a: the server just confirmed this bot has an authorized WhatsApp
                    // channel, so the trial clock must be running. The FRESH auth path starts it
                    // in Manager's poller; this branch covers the profile that was ALREADY
                    // authorized when the row was toggled on — including an install whose
                    // channels were connected before the ledger existed, which would otherwise
                    // never show the countdown. Idempotent: a no-op once the clock has started.
                    TrialLedger.StartIfNeeded();

                    if (WappiStatusParser.TryGetPhone(response, out string phone)
                        && WhatsappNumberField.Value != phone)
                    {
                        WhatsappNumberField.Value = phone;
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

        // A successful auth persists the channel itself, so the screen is back
        // in agreement with disk on these fields — recompute rather than leave
        // whatever verdict the pre-auth toggle flip produced.
        Manager.Instance.EnableSave();
        Manager.Instance.GetCreateWhatsappWorkflow();
    }

    private void OnWhatsappAuthFromSettingsBack()
    {
        if (WhatsappNumberField != null)
        {
            WhatsappNumberField.Value = "";
            WhatsappNumberField.gameObject.SetActive(false);
        }

        PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", "");
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 0);

        // SetChannelRowQuiet updates isOn AND moves the thumb/retints the track
        // in one call (plain Toggle.SetIsOnWithoutNotify skips the ToggleRow
        // animation listener, leaving the control looking "on"), then
        // recomputes Save against the prefs written just above.
        SetChannelRowQuiet(whatsappRow, false);
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

            // WR-03 (same defect class as CheckWhatsappAuthorization — this THIRD site was not in
            // the review). Note the deliberate shape: this path is DESTRUCTIVE (it deletes the
            // Wappi profile and clears the saved number), so it must fire ONLY on a definitively
            // parsed authorized:false. A missing/unparseable body leaves the profile untouched —
            // identical to the old Contains-guard, and the reason this is not folded into a single
            // `!TryGetAuthorized(...) || !isAuthorized` condition.
            if (WappiStatusParser.TryGetAuthorized(response, out bool isAuthorized))
            {
                if (!isAuthorized && !Manager.openBot.GetComponent<Bot>().whatsappProfileId.Equals(Bot.UnauthedProfileSentinel))
                {
                    WhatsappNumberField.Value = "";
                    WhatsappNumberField.gameObject.SetActive(false);

                    PlayerPrefs.SetString(Manager.openBot.name + "WhatsappNumber", "");
                    PlayerPrefs.SetInt(Manager.openBot.name + "isOnWhatsapp", 0);

                    // This probe lands ~a network round-trip after the screen
                    // opened and rewrites both the row and the prefs behind the
                    // user's back, so the Save verdict has to be recomputed —
                    // otherwise a toggle the user had just flipped stayed lit
                    // even though the probe brought screen and prefs back into
                    // agreement.
                    SetChannelRowQuiet(whatsappRow, false);
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

            // Logout rewrote the number field and both prefs — recompute Save.
            Manager.Instance.EnableSave();
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }


    //////////////////////////////////////////////////////////TELEGRAM AUTHORIZATION//////////////////////////////////////////////////////////

    private IEnumerator CheckTelegramAuthorization()
    {
        var bot = Manager.openBot.GetComponent<Bot>();

        if (bot.telegramProfileId.Equals(Bot.UnauthedProfileSentinel))
        {
            // Fresh path — see the WhatsApp twin above (re-auth of an existing profile
            // reuses its slot and is deliberately NOT gated).
            if (!EntitlementGate.CanConnectChannel(EntitlementGate.ConnectedChannelCount()))
            {
                EntitlementGate.RequestPaywall(PaywallTrigger.ChannelLimit);
                SetChannelRowQuiet(telegramRow, false);
                yield break;
            }

            Manager.Instance.GetCreateTelegramProfile(BotNameField.Value);

            float elapsed = 0f;
            while (bot.telegramProfileId.Equals(Bot.UnauthedProfileSentinel) && elapsed < 10f)
            {
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }

            if (bot.telegramProfileId.Equals(Bot.UnauthedProfileSentinel))
            {
                SetChannelRowQuiet(telegramRow, false);
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

            // tapi get/status is pretty-printed with two "phone" keys — parse via the
            // whitespace/order-agnostic WappiStatusParser instead of substring scanning.
            if (WappiStatusParser.TryGetAuthorized(response, out bool isAuthorized))
            {
                if (isAuthorized)
                {
                    // Telegram twin of the WhatsApp branch above (Task 15a): an already-authorized
                    // channel confirmed on toggle-on also starts the trial clock, idempotently.
                    TrialLedger.StartIfNeeded();

                    if (WappiStatusParser.TryGetPhone(response, out string phone))
                    {
                        TelegramNumberField.Value = phone;
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

        // Recompute Save — see the WhatsApp twin above.
        Manager.Instance.EnableSave();
        Manager.Instance.GetCreateTelegramWorkflow();
    }

    private void OnTelegramAuthFromSettingsBack()
    {
        if (TelegramNumberField != null)
        {
            TelegramNumberField.Value = "";
            TelegramNumberField.gameObject.SetActive(false);
        }

        PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", "");
        PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 0);

        SetChannelRowQuiet(telegramRow, false);
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

            // tapi get/status is pretty-printed — the old substring parse THREW here (its
            // ",\"authorized_at\":" guard never matches the pretty ",\n  \"authorized_at\":",
            // so Substring got a negative length), silently breaking outside-app de-auth
            // detection. Robust, throw-safe parse via WappiStatusParser; semantics preserved:
            // act only when the profile reports NOT authorized and still has a real id.
            //
            // The extra isOnTelegram==1 gate is DELIBERATELY stricter than the WhatsApp twin
            // (CheckWhatsappUnauthorizationOutsideApp): this Telegram branch only just went live
            // — the old pretty-body parse always threw before it could run, so this destructive
            // GetDeleteTelegramProfile path has NO field history. Requiring the bot to be a known
            // Telegram bot means a transient / mid-pairing authorized:false (a reconnecting or
            // abandoned-pairing profile whose id was never reset to "-1") cannot silently delete
            // a live profile. The WhatsApp twin stays byte-identical (proven safe on compact
            // api/sync JSON) and deliberately does NOT get this extra gate.
            if (WappiStatusParser.TryGetAuthorized(response, out bool isAuthorized)
                && !isAuthorized
                && PlayerPrefs.GetInt(Manager.openBot.name + "isOnTelegram", 0) == 1
                && !Manager.openBot.GetComponent<Bot>().telegramProfileId.Equals(Bot.UnauthedProfileSentinel))
            {
                TelegramNumberField.Value = "";
                TelegramNumberField.gameObject.SetActive(false);

                PlayerPrefs.SetString(Manager.openBot.name + "TelegramNumber", "");
                PlayerPrefs.SetInt(Manager.openBot.name + "isOnTelegram", 0);

                // Recompute Save — see the WhatsApp twin above.
                SetChannelRowQuiet(telegramRow, false);
                Manager.Instance.GetDeleteTelegramProfile(Manager.openBot.GetComponent<Bot>().telegramProfileId);
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

            // Recompute Save — see the WhatsApp twin above.
            Manager.Instance.EnableSave();
        }

        Manager.Instance.LoadingPanel.SetActive(false);
    }




    // Tapping «Загрузить прайс-лист» no longer opens the picker directly — it
    // opens the source sheet («Файл» / «Фото из галереи») so photos of price
    // boards reach the iPhone Photos library, not just the document picker.
    private void UploadPriceList()
    {
        ShowUploadSourceSheet("product");
    }

    private void UploadServiceList()
    {
        ShowUploadSourceSheet("service");
    }

    private void ShowUploadSourceSheet(string contentType)
    {
        pendingUploadContentType = contentType;

        // No sheet baked into this prefab yet — degrade gracefully to the old
        // direct-picker behaviour so uploads still work.
        if (uploadSourceSheet == null)
        {
            InitializeFilePickerTypes();
            PickMediaFile(contentType);
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
        PickMediaFile(pendingUploadContentType);
    }

    // Wired to the sheet's «Фото из галереи» button (via
    // UploadSourceSheet.OnGalleryPressed). Multi-selects photos and reuses the
    // same upload path — UploadPayloadBuilder decodes/downscales each photo and
    // the workflow's vision branch extracts the prices.
    public void OnUploadSourceGalleryPressed()
    {
        if (uploadSourceSheet != null) uploadSourceSheet.Hide();

        // Snapshot the pending context: the callback runs asynchronously and a
        // later tap could overwrite the field before it fires.
        string contentType = pendingUploadContentType;

        NativeGallery.GetImagesFromGallery(paths =>
        {
            if (paths == null) return; // cancelled

            // Synthesize display names: the temp copies iOS hands back are all
            // named pickedMediaN.jpg (reused across pick sessions), which reads
            // as duplicates in the list and cross-fires the replace-by-name
            // flow — a later photo would silently replace an earlier one's
            // knowledge (see GalleryPhotoNamer).
            Bot openBot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
            // Stored AND in-flight: the store is only written on completion, so
            // seeding from it alone let a photo picked while another was still
            // uploading collide with it (both stamped to the same minute).
            var takenNames = openBot != null
                ? UploadNameSet.TakenNames(
                      UploadedFilesStore.Load(openBot.name, contentType),
                      UploadCenter.Existing != null
                          ? UploadCenter.Existing.Jobs.JobsFor(openBot.name, contentType)
                          : null)
                : new HashSet<string>();

            int index = 0;
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                string displayName = GalleryPhotoNamer.DisplayName(System.DateTime.Now, index, paths.Length, takenNames);
                takenNames.Add(displayName);
                index++;
                StartCoroutine(BeginUpload(path, contentType, displayName));
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

    private void PickMediaFile(string contentType)
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
                    StartCoroutine(BeginUpload(paths[i], contentType));
                }
            }
        }, fileTypes);
    }

    // Asks the one question that needs the user (replace an existing file?),
    // then hands the upload to UploadCenter. The transfer itself deliberately
    // does NOT run here: this MonoBehaviour's coroutines are killed the moment
    // the settings screen is deactivated, which used to abandon the upload
    // mid-request while n8n finished ingesting it anyway.
    private IEnumerator BeginUpload(string filePath, string contentType, string displayNameOverride = null)
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

        // A same-named upload replaces the existing file's knowledge — ask
        // before uploading anything. Cancel = no upload, the old file stays.
        // The answer travels with the job: it is the ONLY thing that authorises
        // deleting the old file's chunks once this upload lands, and a retry
        // re-enters the upload below this point.
        bool replaceConfirmed = false;
        if (UploadedFilesStore.FindByName(openBot.name, contentType, fileName).Count > 0)
        {
            yield return RequestReplaceFileDecision(fileName, decision => replaceConfirmed = decision);
            if (!replaceConfirmed) yield break;
        }

        UploadCenter.Instance?.StartUpload(openBot.name, contentType, filePath, fileName,
                                           displayNameOverride, replaceConfirmed);
    }

}

using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Text;
using System;
using System.IO;
using Automation.BotSettingsUI;
using DG.Tweening;
using Newtonsoft.Json;

public partial class Manager : MonoBehaviour
{
    #region
    // [SerializeField] private GameObject MainPage;
    [SerializeField] private GameObject WhatsappAuth;
    [SerializeField] private GameObject TelegramAuth;
    // [SerializeField] private GameObject Confirmation;
    [SerializeField] private GameObject BotsPage;
    [SerializeField] private GameObject BotsParent;

    /// <summary>Read-only access to the bots root transform. Used by ChatManager to enumerate bots.</summary>
    public Transform BotsRoot => BotsParent != null ? BotsParent.transform : null;

    /// <summary>
    /// Returns the Bot whose GameObject name matches botName, or null if not found.
    /// Bot names are "Bot0", "Bot1", etc. — they are persistent identifiers used for
    /// PlayerPrefs and per-bot cache directories.
    /// </summary>
    public Bot FindBotByName(string botName)
    {
        if (BotsParent == null || string.IsNullOrEmpty(botName)) return null;
        Transform t = BotsParent.transform.Find(botName);
        return t != null ? t.GetComponent<Bot>() : null;
    }

    [SerializeField] private GameObject BotPrefab;
    [SerializeField] private GameObject BotSettings;
    [SerializeField] private GameObject BotSettingsParent;
    [SerializeField] private GameObject ProductPrefab;
    [SerializeField] private GameObject ServicePrefab;
    [SerializeField] private GameObject WhatsappQRPanel;
    [SerializeField] private GameObject WhatsappCodePanel;
    [SerializeField] private GameObject TelegramQRPanel;
    [SerializeField] private GameObject TelegramCodePanel;
    [SerializeField] private TextMeshProUGUI TelegramPhoneTitle;
    [SerializeField] private TextMeshProUGUI TelegramPhoneBody;
    [SerializeField] private GameObject WhatsappCodeTimer;
    [SerializeField] private GameObject TelegramCodeTimer;
    // Status messages are shown inline in button text — no separate GOs needed
    [SerializeField] public GameObject LoadingPanel;

    [SerializeField] private GameObject WhatsappAuthSuccessPanel;
    [SerializeField] private Button WhatsappAuthBackButton;
    [SerializeField] private GameObject TelegramAuthSuccessPanel;
    [SerializeField] private Button TelegramAuthBackButton;

    // Interactive «Бот подключён!» success moment — ONE field set on a NEW standalone
    // full-screen overlay (SuccessOverlay). D2 relocation (owner decision 2026-07-18): the
    // overlay is a Canvas-level sibling of the ScreenContainer rendered ABOVE the auth pages,
    // so the celebration reads clean instead of stacked over the still-visible code UI. Because
    // the overlay is a single shared hierarchy, one label/button set serves BOTH channels —
    // replacing the ten per-channel wa*/tg* fields. All null-guarded: stamped by
    // OnboardingAuthBlocksBuilder, so they are null in-scene until that builder runs.
    [Header("Onboarding success moment — standalone overlay (stamped by OnboardingAuthBlocksBuilder)")]
    [SerializeField] private GameObject SuccessOverlay;                       // standalone full-screen overlay root
    [SerializeField] private TMPro.TextMeshProUGUI successTitleLabel;         // «Бот подключён!»
    [SerializeField] private TMPro.TextMeshProUGUI successBodyLabel;          // price-list body
    [SerializeField] private UnityEngine.UI.Button successPrimaryButton;      // «Загрузить прайс-лист» / «Открыть чаты»
    [SerializeField] private TMPro.TextMeshProUGUI successPrimaryLabel;
    [SerializeField] private UnityEngine.UI.Button successLaterButton;        // «Позже»
    [SerializeField] private Button GetWhatsappCodeButton;
    [SerializeField] private Button GetTelegramCodeButton;
    [SerializeField] private Button SendTelegramCodeButton;
    [SerializeField] private Button ChangeWhatsappNumberButton;
    [SerializeField] private Button ChangeTelegramNumberButton;
    [SerializeField] private Button SaveButton;

    [SerializeField] private TMP_InputField WhatsappNumberInput;
    [SerializeField] private TMP_InputField TelegramNumberInput;
    [SerializeField] private TMP_InputField TelegramCodeInput;

    [SerializeField] private RawImage WhatsappQRCodeImage;
    [SerializeField] private RawImage TelegramQRCodeImage;
    [SerializeField] private GameObject WhatsappQRStatusText;
    [SerializeField] private GameObject TelegramQRStatusText;
    [SerializeField] private RectTransform BusinessTypesParent;
    [SerializeField] private GameObject BusinessTypeButtonTemplate;
    [SerializeField] private BusinessTypesSO businessTypes;

    [Header("Add Bot Form")]
    [SerializeField] private GameObject AddBotFormPage;
    [Tooltip("Back chevron in the Add-Bot overlay header (wired by NavRestructureBuilder).")]
    [SerializeField] private Button addBotBackButton;
    [Tooltip("Left-edge swipe strip on the Add-Bot overlay (wired by NavRestructureBuilder).")]
    [SerializeField] private SwipeToBackPanel addBotSwipeBack;
    [Tooltip("Bots-page empty-state CTA (wired by NavRestructureBuilder).")]
    [SerializeField] private Button botsEmptyStateCta;
    [SerializeField] private TextMeshProUGUI platformValueText;
    [SerializeField] private GameObject platformWhatsappGroup;
    [SerializeField] private GameObject platformTelegramGroup;
    [SerializeField] private GameObject platformPlusSeparator;
    [SerializeField] private TextMeshProUGUI botNameValueText;
    [SerializeField] private TextMeshProUGUI businessTypeValueText;
    [SerializeField] private TextMeshProUGUI descriptionValueText;
    [SerializeField] private Button createBotFormButton;
    [SerializeField] private GameObject platformSelectorPanel;
    [SerializeField] private GameObject botNameInputPanel;
    [SerializeField] private GameObject businessSelectorPanel;
    [SerializeField] private GameObject descriptionInputPanel;
    [SerializeField] private TMP_InputField botNamePopupInput;
    [SerializeField] private TMP_InputField descriptionPopupInput;
    [SerializeField] private Button platformRowButton;
    [SerializeField] private Button botNameRowButton;
    [SerializeField] private Button businessTypeRowButton;
    [SerializeField] private Button descriptionRowButton;
    [SerializeField] private Button whatsappOptionButton;
    [SerializeField] private Button telegramOptionButton;
    [SerializeField] private Button bothOptionButton;

    private readonly System.Collections.Generic.List<Button> businessTypeButtons = new();
    private string selectedBusinessId = "";
    private int id;
    private Color businessButtonDefaultColor;

    // Platform brand colors — must match the PlatformRow label colors in Main.unity
    private static readonly Color CreateButtonDefaultColor = new Color32(0x00, 0x7A, 0xFF, 0xFF); // iOS blue
    private static readonly Color WhatsappBrandColor = new Color32(0x25, 0xD3, 0x66, 0xFF);
    private static readonly Color TelegramBrandColor = new Color32(0x2A, 0xAB, 0xEE, 0xFF);

    private bool CreateWhatsappWorkflowFromEditSuccess;
    private bool EditWhatsappWorkflowSaved;
    private bool EnableWhatsappWorkflowSaved;
    private bool CreateTelegramWorkflowFromEditSuccess;
    private bool EditTelegramWorkflowSaved;
    private bool EnableTelegramWorkflowSaved;

    // True when any sub-request of the in-flight save failed. Read by Saved()
    // to pick the pill's final text. Reset to false at every "Saving.." show so
    // it is scoped to the current save and can't leak in from a prior failure
    // (e.g. a failed bot-card activation toggle, which shares the Enable*
    // coroutines but never shows the pill).
    private bool _saveHadError;

    // Pill states (Russian, matching the rest of the app's UI language).
    // SavingText is the in-progress text shown at each save entry point.
    private const string SavingText = "Сохранение..";
    private const string SavedText = "Сохранено";
    private const string SaveFailedText = "Не удалось сохранить";
    private const string UnknownBusinessTypeOption = "Тип не выбран";

    private string whatsappProfileId = "-1";
    private string telegramProfileId = "-1";

    private int selectedPlatform; // 0=none, 1=WhatsApp, 2=Telegram, 3=Both
    private string formBotName = "";
    private string formDescription = "";
    private bool businessTypeSelected;
    private bool whatsappAuthCompleted;
    private bool telegramAuthCompleted;
    private bool isCreatingBot;
    private Coroutine _whatsappStatusCoroutine;
    private Coroutine _telegramStatusCoroutine;
    private Coroutine _whatsappQrCoroutine;
    // Telegram twin of _whatsappQrCoroutine — without a stored handle the tapi QR loop could
    // not be stopped on back-press, so it kept polling auth/qr against a profile the back
    // handler was concurrently deleting (the IN-04 symptom, Telegram half).
    private Coroutine _telegramQrCoroutine;
    // Re-entrancy latch for the success moment. SuccessOverlay.activeSelf is NOT sufficient:
    // the overlay only goes active AFTER the in-box checkmark dwell, leaving that window
    // unguarded. Set once the moment commits, cleared in CloseSuccessAndOverlay.
    private bool _successMomentRunning;
    // False only while the success moment's in-box checkmark is actually dwelling.
    // ShowAuthSuccess's settings-re-auth branch waits on this before returning, because the
    // completion signal its caller raises kicks off the workflow POST — whose LoadingPanel
    // cover is drawn above the auth pages and would hide the checkmark. Starts true so a
    // moment that never reaches the dwell (early return, or no in-box panel to show) is not
    // waited on. See ShowInteractiveSuccessMoment.
    private bool _inBoxCheckDone = true;
    // True once a pairing code was issued for the current WhatsApp profile.
    // WhatsApp refuses a repeat code for the same profile for ~2 minutes, so
    // the next code request silently swaps in a fresh profile instead.
    private bool _whatsappCodeIssued;
    private string telegramPhoneTitleInitial;
    private string telegramPhoneBodyInitial;
    // True while the Telegram code-entry panel is repurposed as a cloud-password (2FA)
    // prompt: TelegramCodeInputChanged relaxes its gate and the submit button posts
    // auth/2fa instead of auth/code. Reset whenever the panel is closed / number changed.
    private bool _telegram2faMode;

    public static string wappiAuthToken => Secrets.Data.wappiAuthToken;
    public static string n8nAPIKey => Secrets.Data.n8nAPIKey;
    public static string telegramBotToken => Secrets.Data.telegramBotToken;
    public static string supportChatId => Secrets.Data.supportChatId;
    public const string DevN8nBaseUrlKey = "DevN8nBaseUrl";

    public static string n8nBaseUrl =>
        ResolveN8nBaseUrl(PlayerPrefs.GetString(DevN8nBaseUrlKey, ""), Secrets.Data.n8nBaseUrl);

    public static string ResolveN8nBaseUrl(string configured) => ResolveN8nBaseUrl(null, configured);

    public static string ResolveN8nBaseUrl(string overrideUrl, string configured)
    {
        if (!string.IsNullOrWhiteSpace(overrideUrl)) return overrideUrl.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().TrimEnd('/');
        return "https://bagkz.app.n8n.cloud";
    }

    // Robustly extract the workflow "id" from an n8n create response. Tolerates the
    // full workflow object, a trimmed {"id":"..."}, whitespace, and malformed bodies
    // (returns null instead of throwing or slicing the wrong substring).
    public static string ExtractWorkflowId(string responseJson)
    {
        if (string.IsNullOrEmpty(responseJson)) return null;
        try
        {
            return JsonConvert.DeserializeObject<WorkflowIdResponse>(responseJson)?.id;
        }
        catch
        {
            return null;
        }
    }

    private class WorkflowIdResponse
    {
        public string id;
    }

    private string apiUrl => Secrets.Data.greenApi.apiUrl;
    private string idInstance => Secrets.Data.greenApi.idInstance;
    private string apiTokenInstance => Secrets.Data.greenApi.apiTokenInstance;

    public static GameObject BotSettingsParentStatic;
    public static Manager Instance;

    public static BotSettings openBotSettings;
    public static GameObject openBot;

    private string pdf;
    private string txt;
    private string rtf;
    private string xml;
    private string csv;
    private string xls;
    private string xlsx;
    private string doc;
    private string docx;
    private string video;

    // private GreenApiAvatarFetcher greenApiAvatarFetcher;
    // public GameObject GreenApi;
    // public Image profileImage;
    #endregion


    public void Awake()
    {
        // Warm the secrets cache before anything makes an API call. On Android the
        // StreamingAssets file lives inside the APK and can only be read asynchronously
        // (UnityWebRequest), so we kick the load off here — the earliest lifecycle hook —
        // and let it populate the cache before any user-driven request or the next-frame
        // chat sync fires. iOS/Editor/desktop read the file directly in the same frame.
        // See Secrets.cs.
        StartCoroutine(PreloadSecretsThenInitBilling());
    }

    // BillingService.Initialize() reads Secrets.Data.revenueCat, so it must run AFTER Preload
    // actually completes — chained as a continuation (yield return, not a parallel
    // StartCoroutine) so it never races Android's async APK read (Preload is synchronous inline
    // on Editor/iOS/desktop but a real yield on Android). try/catch: an uncaught throw here must
    // not brick the rest of Start() behind LoadingPanel — billing degrades to
    // FakeBillingBackend/None-tier rather than the app getting stuck on the loading cover.
    private IEnumerator PreloadSecretsThenInitBilling()
    {
        yield return Secrets.Preload();

        try
        {
            BillingService.Initialize();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Manager] BillingService.Initialize() threw — continuing without billing: {e}");
        }

        // Boot-time usage snapshot fetch (Task 11) — best-effort, one shot. Must never brick
        // boot behind LoadingPanel, so the StartCoroutine call itself (which runs
        // UsageClient.FetchRoutine()'s body synchronously up to its first yield) is guarded
        // the same way Initialize() is above. The other trigger is BotsPage.OnEnable (fires
        // whenever the Bots tab becomes visible); a screen-open-time fetch for the usage UI
        // itself lands with Task 14c.
        try
        {
            StartCoroutine(UsageClient.FetchRoutine());
        }
        catch (Exception e)
        {
            Debug.LogError($"[Manager] UsageClient.FetchRoutine() threw — continuing without a usage snapshot: {e}");
        }

        // Trial backfill (Task 15b): installs that authed a channel BEFORE Task 15a's
        // auth-time StartIfNeeded shipped carry no TrialStartedUtc at all, so their trial
        // never starts and — since IsExpired is `HasStarted && DaysLeft() <= 0` — never
        // expires either. Owner-approved default: start their clock at this launch.
        //
        // Ordering, both halves load-bearing: it must run AFTER BillingService.Initialize()
        // (same coroutine, so the tier the paywall reads below is already resolving) and
        // BEFORE the expiry-paywall check, so that check never sees a half-written ledger.
        // The bot list it counts is already populated by now: LoadBots() is started in
        // Start() and yields WaitForEndOfFrame, which resumes at the END of frame 1 and
        // instantiates every bot synchronously, while this coroutine's own body cannot
        // resume before frame 2 (`yield return` suspends for a frame even when the nested
        // enumerator completes without yielding — Secrets.Preload() on iOS/Editor does).
        // Same try/catch discipline as every step above.
        try
        {
            if (LaunchTrialBackfill.ShouldBackfill(TrialLedger.HasStarted,
                                                   EntitlementGate.ConnectedChannelCount()))
            {
                TrialLedger.StartIfNeeded();
                Debug.Log("[Manager] trial backfill: pre-ledger install with a connected channel — clock started now.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Manager] trial backfill threw — continuing without starting the clock: {e}");
        }

        // Trial-expiry paywall (Task 15a): the «чек ценности» variant, once per launch, and only
        // AFTER BillingService.Initialize() — the whole decision hangs on EntitlementsKnown, which
        // is false until Initialize() has run at all. Same try/catch discipline as the two steps
        // above: a paywall failure must never brick boot behind LoadingPanel.
        try
        {
            TryShowLaunchExpiryPaywall();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Manager] launch expiry paywall check threw — continuing: {e}");
        }
    }

    // ── Launch-time trial-expiry paywall (Task 15a, Part B) ──────────────────
    //
    // Instance fields, not statics: a launch is one Manager, and a static would survive the
    // Editor's domain-reload-free play-mode enter and make the second Play session think the
    // paywall had already been shown.
    private bool _launchExpiryPaywallShown;
    private bool _launchExpiryAwaitingEntitlements;

    /// <summary>
    /// Evaluates <see cref="LaunchPaywallPolicy.ShouldShowExpiry"/> now if the entitlement set is
    /// already resolved; otherwise subscribes ONCE to the first
    /// <see cref="BillingService.OnEntitlementChanged"/> and evaluates there.
    ///
    /// The wait is the point: on a keyed device the first CustomerInfo round-trip is still in
    /// flight at this moment, and <see cref="EntitlementGate.CurrentTier"/> answers Trial grace
    /// for that whole window — evaluating once and giving up would silently skip the paywall for
    /// every real customer whose trial had actually expired.
    /// </summary>
    private void TryShowLaunchExpiryPaywall()
    {
        if (BillingService.EntitlementsKnown)
        {
            EvaluateLaunchExpiryPaywall();
            return;
        }

        if (_launchExpiryAwaitingEntitlements) return;
        _launchExpiryAwaitingEntitlements = true;
        BillingService.OnEntitlementChanged += OnFirstEntitlementResolved;
    }

    private void OnFirstEntitlementResolved(PlanTier _)
    {
        // One shot: unsubscribe before evaluating, so a purchase later in the session can never
        // re-open this paywall (Open/Restore own that flow) even if the guard field were reset.
        BillingService.OnEntitlementChanged -= OnFirstEntitlementResolved;
        _launchExpiryAwaitingEntitlements = false;

        // Same try/catch discipline as the synchronous twin in PreloadSecretsThenInitBilling — and
        // it matters MORE here: this runs inside BillingService.OnEntitlementChanged's multicast
        // invoke, itself inside RevenueCat's GetCustomerInfo callback. An escaping throw (the whole
        // paywall open — Render, DOTween, scene lookups — hangs off this call) would abort the
        // REST of the multicast, silently starving whichever handlers happen to sit after ours.
        try
        {
            EvaluateLaunchExpiryPaywall();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Manager] deferred launch expiry paywall check threw — continuing: {e}");
        }
    }

    private void EvaluateLaunchExpiryPaywall()
    {
        if (!LaunchPaywallPolicy.ShouldShowExpiry(
                EntitlementGate.CurrentTier,
                BillingService.EntitlementsKnown,
                TrialLedger.IsExpired,
                _launchExpiryPaywallShown))
            return;

        _launchExpiryPaywallShown = true;
        EntitlementGate.RequestPaywall(PaywallTrigger.TrialExpired);
    }

    public void Start()
    {
        Application.targetFrameRate = 60;

        PopulateBusinessTypes();

        id = PlayerPrefs.GetInt("ids", 0);

        BotSettingsParentStatic = BotSettingsParent;
        StartCoroutine(LoadBots());

        LoadingPanel.SetActive(false);

        CreateWhatsappWorkflowFromEditSuccess = false;
        EditWhatsappWorkflowSaved = false;
        EnableWhatsappWorkflowSaved = false;
        CreateTelegramWorkflowFromEditSuccess = false;
        EditTelegramWorkflowSaved = false;
        EnableTelegramWorkflowSaved = false;

        Instance = this;

        if (TelegramPhoneTitle != null) telegramPhoneTitleInitial = TelegramPhoneTitle.text;
        if (TelegramPhoneBody != null) telegramPhoneBodyInitial = TelegramPhoneBody.text;

        // Add Bot Form — row buttons
        if (platformRowButton != null) platformRowButton.onClick.AddListener(OpenPlatformSelector);
        if (botNameRowButton != null) botNameRowButton.onClick.AddListener(OpenBotNameInput);
        if (businessTypeRowButton != null) businessTypeRowButton.onClick.AddListener(OpenBusinessSelector);
        if (descriptionRowButton != null) descriptionRowButton.onClick.AddListener(OpenDescriptionInput);

        // Add Bot Form — platform selector (dismisses popup → finger-up)
        if (whatsappOptionButton != null) PopupUI.WireFingerUp(whatsappOptionButton, () => SelectPlatform(1));
        if (telegramOptionButton != null) PopupUI.WireFingerUp(telegramOptionButton, () => SelectPlatform(2));
        if (bothOptionButton != null) PopupUI.WireFingerUp(bothOptionButton, () => SelectPlatform(3));

        // Add Bot Form — create button
        if (createBotFormButton != null)
        {
            createBotFormButton.onClick.AddListener(() => StartCoroutine(CreateBotFromForm()));
            createBotFormButton.interactable = false;
        }

        // Add-Bot overlay chrome + Bots empty-state CTA (objects created by NavRestructureBuilder)
        if (addBotBackButton != null) addBotBackButton.onClick.AddListener(CloseAddBotForm);
        if (addBotSwipeBack != null) addBotSwipeBack.OnCommitted = CloseAddBotForm;
        if (botsEmptyStateCta != null) botsEmptyStateCta.onClick.AddListener(() => global::BotsPage.Instance?.StartNewBot());

        // Auth panels — WhatsApp
        if (WhatsappAuthBackButton != null) WhatsappAuthBackButton.onClick.AddListener(CancelBotCreation);
        if (GetWhatsappCodeButton != null) GetWhatsappCodeButton.onClick.AddListener(() => StartCoroutine(GetWhatsappCode()));
        if (ChangeWhatsappNumberButton != null) ChangeWhatsappNumberButton.onClick.AddListener(ChangeWhatsappNumber);

        // Auth panels — Telegram
        if (TelegramAuthBackButton != null) TelegramAuthBackButton.onClick.AddListener(CancelBotCreation);

        if (GetTelegramCodeButton != null) GetTelegramCodeButton.onClick.AddListener(() => StartCoroutine(GetTelegramCode()));
        if (SendTelegramCodeButton != null) SendTelegramCodeButton.onClick.AddListener(() => StartCoroutine(SendTelegramCode()));
        if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.onClick.AddListener(ChangeTelegramNumber);

        // Auth input fields
        if (WhatsappNumberInput != null) WhatsappNumberInput.onValueChanged.AddListener(WhatsappNumberInputChanged);
        if (TelegramNumberInput != null) TelegramNumberInput.onValueChanged.AddListener(TelegramNumberInputChanged);
        if (TelegramCodeInput != null) TelegramCodeInput.onValueChanged.AddListener(TelegramCodeInputChanged);

        // Initialize popups as hidden
        if (platformSelectorPanel != null) platformSelectorPanel.SetActive(false);
        if (botNameInputPanel != null) botNameInputPanel.SetActive(false);
        if (businessSelectorPanel != null) businessSelectorPanel.SetActive(false);
        if (descriptionInputPanel != null) descriptionInputPanel.SetActive(false);

        // Wire overlay/close/confirm/cancel dismiss paths for every Add Bot
        // popup via PopupUI helpers. EventAbsorber on each card prevents taps
        // on the card background (non-button areas) from bubbling up to the
        // overlay's dismiss handler.
        WirePopupDismiss(platformSelectorPanel, confirm: null,           cancel: ClosePlatformSelector);
        WirePopupDismiss(botNameInputPanel,     confirm: ConfirmBotName, cancel: CloseBotNameInput);
        WirePopupDismiss(businessSelectorPanel, confirm: null,           cancel: CloseBusinessSelector);
        WirePopupDismiss(descriptionInputPanel, confirm: ConfirmDescription, cancel: CloseDescriptionInput);
    }

    // Standard dismiss wiring for an Add Bot popup: overlay-tap, ✕, Confirm,
    // and Cancel all fire on real finger release; card background absorbs
    // taps so they don't bubble to the overlay's dismiss handler.
    // `confirm` may be null for popups without an OK button (platform /
    // business selectors, where individual options close the popup).
    private static void WirePopupDismiss(GameObject panel, Action confirm, Action cancel)
    {
        if (panel == null || cancel == null) return;

        var overlayBtn = panel.GetComponent<Button>();
        if (overlayBtn != null) PopupUI.WireFingerUp(overlayBtn, cancel);

        var closeBtn = panel.transform.Find("Content/CloseButton")?.GetComponent<Button>();
        if (closeBtn != null) PopupUI.WireFingerUp(closeBtn, cancel);

        var confirmBtn = panel.transform.Find("Content/Buttons/ConfirmButton")?.GetComponent<Button>();
        if (confirmBtn != null && confirm != null) PopupUI.WireFingerUp(confirmBtn, confirm);

        var cancelBtn = panel.transform.Find("Content/Buttons/CancelButton")?.GetComponent<Button>();
        if (cancelBtn != null) PopupUI.WireFingerUp(cancelBtn, cancel);

        var card = panel.transform.Find("Content");
        if (card != null) PopupUI.AbsorbEvents(card);
    }


    public IEnumerator LoadBots()
    {
        // One-shot migration: compact pre-existing phantom-blank Product/Service
        // slot keys before LoadBots reads them. Idempotent on clean data.
        MigrateBotPersistence();

        yield return new WaitForEndOfFrame();

        for (int i = 0; i < id; i++)
        {
            if (PlayerPrefs.HasKey("Bot" + i.ToString() + "Name"))
            {
                GameObject recreatedBot = Instantiate(BotPrefab, BotPrefab.transform.position, BotPrefab.transform.rotation, BotsParent.transform);

                recreatedBot.name = "Bot" + i.ToString();

                var recreatedBotComp = recreatedBot.GetComponent<Bot>();
                if (recreatedBotComp.BotName != null)
                    recreatedBotComp.BotName.text = PlayerPrefs.GetString(recreatedBot.name + "Name", "");
                // C2 card: the subline (business type + channel icons) is owned by
                // Bot.RefreshCardSubline — it derives itself one frame after the
                // rename above, so no BotDesc/activation writes are needed here.
                if (recreatedBotComp.Status != null)
                    recreatedBotComp.Status.text = PlayerPrefs.GetString(recreatedBot.name + "Status", "");
                recreatedBot.GetComponent<Bot>().active = PlayerPrefs.GetInt(recreatedBot.name + "Active", 0) == 1;
                recreatedBot.GetComponent<Bot>().whatsappProfileId = PlayerPrefs.GetString(recreatedBot.name + "WhatsappProfileId", Bot.UnauthedProfileSentinel);
                recreatedBot.GetComponent<Bot>().telegramProfileId = PlayerPrefs.GetString(recreatedBot.name + "TelegramProfileId", Bot.UnauthedProfileSentinel);
                recreatedBot.GetComponent<Bot>().whatsappWorkflowId = PlayerPrefs.GetString(recreatedBot.name + "WhatsappWorkflowId", Bot.UnauthedProfileSentinel);
                recreatedBot.GetComponent<Bot>().telegramWorkflowId = PlayerPrefs.GetString(recreatedBot.name + "TelegramWorkflowId", Bot.UnauthedProfileSentinel);
                // Apply the icon now — Bot.Awake fires before the rename above,
                // so it sees the prefab name and no PlayerPrefs entry. Refresh
                // explicitly now that the bot has its final name.
                recreatedBotComp.RefreshBusinessIcon();

                BotSettings recreatedBotSettings = InstantiateBotSettingsFlush(BotSettingsParent.transform);

                recreatedBotSettings.BotNameField.Value = PlayerPrefs.GetString(recreatedBot.name + "Name", "");
                recreatedBotSettings.SyncHeaderTitle();
                recreatedBotSettings.WhatsappToggle.isOn = PlayerPrefs.GetInt(recreatedBot.name + "isOnWhatsapp", 1) == 1;
                recreatedBotSettings.TelegramToggle.isOn = PlayerPrefs.GetInt(recreatedBot.name + "isOnTelegram", 1) == 1;
                PopulateBusinessDropdown(recreatedBotSettings.BusinessTypeDropdown);
                ApplyBusinessTypeToDropdown(recreatedBotSettings.BusinessTypeDropdown,
                    PlayerPrefs.GetString(recreatedBot.name + "BusinessType", ""));
                recreatedBotSettings.WhatsappNumberField.Value = PlayerPrefs.GetString(recreatedBot.name + "WhatsappNumber", "");
                recreatedBotSettings.TelegramNumberField.Value = PlausibleTelegramNumber(PlayerPrefs.GetString(recreatedBot.name + "TelegramNumber", ""));

                recreatedBotSettings.WhatsappNumberField.gameObject.SetActive(!recreatedBotSettings.WhatsappNumberField.Value.Equals(""));
                recreatedBotSettings.TelegramNumberField.gameObject.SetActive(!recreatedBotSettings.TelegramNumberField.Value.Equals(""));

                recreatedBotSettings.BusinessField.Value = PlayerPrefs.GetString(recreatedBot.name + "Business", "");
                recreatedBotSettings.PromptField.Value = PlayerPrefs.GetString(recreatedBot.name + "Prompt", "");
                LoadContactFields(recreatedBotSettings, recreatedBot.name);

                int ProductsNumber = PlayerPrefs.GetInt(recreatedBot.name + "ProductsNumber", 0);
                for (int p = 0; p < ProductsNumber; p++)
                {
                    ProductCardView recreatedProduct = Instantiate(ProductPrefab, ProductPrefab.transform.position, ProductPrefab.transform.rotation, recreatedBotSettings.ProductsParent).GetComponent<ProductCardView>();
                    recreatedBotSettings.RegisterProductCard(recreatedProduct);

                    recreatedProduct.Name = PlayerPrefs.GetString(recreatedBot.name + "Product" + p, "");
                    recreatedProduct.Price = PlayerPrefs.GetString(recreatedBot.name + "Product" + p + "Price", "");
                    recreatedProduct.Description = PlayerPrefs.GetString(recreatedBot.name + "Product" + p + "Description", "");
                }

                int ServicesNumber = PlayerPrefs.GetInt(recreatedBot.name + "ServicesNumber", 0);
                for (int s = 0; s < ServicesNumber; s++)
                {
                    ServiceCardView recreatedService = Instantiate(ServicePrefab, ServicePrefab.transform.position, ServicePrefab.transform.rotation, recreatedBotSettings.ServicesParent).GetComponent<ServiceCardView>();
                    recreatedBotSettings.RegisterServiceCard(recreatedService);

                    recreatedService.Name = PlayerPrefs.GetString(recreatedBot.name + "Service" + s, "");
                    recreatedService.Price = PlayerPrefs.GetString(recreatedBot.name + "Service" + s + "Price", "");
                    recreatedService.Description = PlayerPrefs.GetString(recreatedBot.name + "Service" + s + "Description", "");
                }
            }
        }

        // Existing-user auto-flag: users who already have bots must never see the
        // first-run carousel — set OnboardingSeen so a later delete-all-bots in-session
        // can't resurface it. Use the LIVE post-load bot count
        // (BotsParent.transform.childCount, the container the loop instantiates into),
        // NOT the loop bound `id`: `id` is a monotonic bot-creation counter that is never
        // decremented, so a user who created then fully deleted their bot(s) would be
        // wrongly auto-flagged and permanently lose the carousel despite having ZERO bots
        // (violates ONB-01).
        bool hasBotsNow = BotsParent != null && BotsParent.transform.childCount > 0;
        if (OnboardingGate.ShouldAutoFlagSeen(hasBots: hasBotsNow,
                seen: PlayerPrefs.GetInt(OnboardingKeys.Seen, 0) == 1))
        {
            PlayerPrefs.SetInt(OnboardingKeys.Seen, 1);
            PlayerPrefs.Save();
        }

        // Sweep profiles orphaned by an app death mid-wizard/mid-re-auth that
        // the quit-time settle (OnApplicationQuit) couldn't reach — on mobile
        // most kills run no code at all, so this launch-time pass stays the
        // safety net.
        if (PendingProfileLedger.TryGetPendingWhatsapp(out string orphanedWhatsappProfileId))
        {
            StartCoroutine(DeleteWhatsappProfile(orphanedWhatsappProfileId, true));
        }

        if (PendingProfileLedger.TryGetPendingTelegram(out string orphanedTelegramProfileId))
        {
            StartCoroutine(DeleteTelegramProfile(orphanedTelegramProfileId, true));
        }

        SweepPendingUploads();
    }

    // Same safety net, one layer down: an upload whose process died between the
    // POST and the local record leaves n8n holding RAG chunks under a fileId
    // nothing on-device references — the bot would answer from a price list the
    // app can neither list nor delete, and index it again on the next upload.
    //
    // Deletes, never resumes: the job's file path is the picker's temp copy, and
    // iOS reuses pickedMediaN.jpg across sessions, so re-reading it here could
    // upload an entirely different file.
    private void SweepPendingUploads()
    {
        foreach (PendingUploadEntry entry in PendingUploadLedger.LoadAll())
        {
            // Recorded after all (the kill landed between the store write and
            // the ledger clear) — settle it, deleting would make the bot forget
            // a file the user can still see.
            if (!PendingUploadLedger.IsOrphan(entry))
            {
                PendingUploadLedger.Remove(entry.FileId);
                continue;
            }

            string fileId = entry.FileId;
            // Counted before the call so a kill mid-sweep cannot reset the tally.
            int attempts = PendingUploadLedger.RecordAttempt(fileId);
            Debug.Log($"[PendingUploads] Sweeping orphaned upload {fileId} " +
                      $"({entry.BotName}/{entry.ContentType}), attempt {attempts}.");

            // Settling on a bare HTTP 200 would be wrong: a sweep that lands
            // while n8n is still ingesting deletes zero chunks, yet the webhook's
            // parallel branch still removes the archived original — so the entry
            // survives for another launch instead. See UploadSweepPolicy.
            UploadCenter.Instance?.DeleteFile(entry.BotName, entry.ContentType, fileId,
                (deleted, deletedChunks) =>
                {
                    if (UploadSweepPolicy.ShouldSettle(deleted, deletedChunks, attempts))
                        PendingUploadLedger.Remove(fileId);
                });
        }
    }

    // ── App lifecycle: orphaned-profile cleanup ──
    // A profile in the pending ledger is an orphan the moment the app dies.
    // Backgrounding must NOT settle it — the pairing-code flow sends the user
    // out of the app to type the code into WhatsApp/Telegram on this same
    // phone — so deletion only runs on a real quit. OnApplicationQuit fires on
    // Editor play-stop, desktop quit and Android clean finishes; iOS never
    // raises it (and swipe-kills run no code anywhere) — those paths rely on
    // the Start() sweep instead.

    private void OnApplicationPause(bool pauseStatus)
    {
        // Flush PlayerPrefs (ledger included) — after backgrounding the OS may
        // kill the process without any further callback.
        if (pauseStatus) PlayerPrefs.Save();
    }

    private void OnApplicationQuit()
    {
        SettlePendingProfilesBeforeQuit();
    }

    private void OnDestroy()
    {
        // BillingService.OnEntitlementChanged is a STATIC event: a Manager destroyed while still
        // waiting for the first CustomerInfo would otherwise leave a delegate pointing at a dead
        // MonoBehaviour (Task 15a). Unsubscribing an unsubscribed handler is a no-op.
        BillingService.OnEntitlementChanged -= OnFirstEntitlementResolved;
    }

    // Coroutines are dead on the quit path, so this blocks the main thread
    // (hard 2s cap) while the native transport pushes the delete requests out.
    // Confirmed deletions clear the ledger so the next-launch sweep won't
    // re-fire; anything unconfirmed stays pending for that sweep.
    private void SettlePendingProfilesBeforeQuit()
    {
        bool hasWhatsappOrphan = PendingProfileLedger.TryGetPendingWhatsapp(out string pendingWhatsappId);
        bool hasTelegramOrphan = PendingProfileLedger.TryGetPendingTelegram(out string pendingTelegramId);
        if (!hasWhatsappOrphan && !hasTelegramOrphan) return;

        UnityWebRequest whatsappDelete = null;
        UnityWebRequest telegramDelete = null;

        if (hasWhatsappOrphan)
        {
            whatsappDelete = UnityWebRequest.Post($"https://wappi.pro/api/profile/delete?profile_id={pendingWhatsappId}", new WWWForm());
            whatsappDelete.SetRequestHeader("Authorization", wappiAuthToken);
            whatsappDelete.SendWebRequest();
        }
        if (hasTelegramOrphan)
        {
            telegramDelete = UnityWebRequest.Post($"https://wappi.pro/tapi/profile/delete?profile_id={pendingTelegramId}", new WWWForm());
            telegramDelete.SetRequestHeader("Authorization", wappiAuthToken);
            telegramDelete.SendWebRequest();
        }

        var quitBudget = System.Diagnostics.Stopwatch.StartNew();
        while (quitBudget.ElapsedMilliseconds < 2000 &&
               ((whatsappDelete != null && !whatsappDelete.isDone) ||
                (telegramDelete != null && !telegramDelete.isDone)))
        {
            System.Threading.Thread.Sleep(25);
        }

        SettleQuitDelete(whatsappDelete, pendingWhatsappId, PendingProfileLedger.ClearWhatsappIfMatches);
        SettleQuitDelete(telegramDelete, pendingTelegramId, PendingProfileLedger.ClearTelegramIfMatches);
    }

    private static void SettleQuitDelete(UnityWebRequest request, string profileId, System.Action<string> clearLedger)
    {
        if (request == null) return;

        if (request.isDone && request.result == UnityWebRequest.Result.Success)
        {
            clearLedger(profileId);
            Debug.Log($"[QuitCleanup] Deleted orphaned profile {profileId}");
        }
        else
        {
            Debug.LogWarning($"[QuitCleanup] Orphaned profile {profileId} not confirmed deleted ({request.result}) — next-launch sweep will retry");
        }

        // Dispose aborts anything still in flight — acceptable, the process is
        // exiting anyway and the ledger keeps the entry for the sweep.
        request.Dispose();
    }

    // Instantiate BotSettings under a parent and force its RectTransform to
    // fill the parent flush. The old code passed a Vector3 world position
    // offset by (Screen.width/2, Screen.height/2) which Unity converted into
    // RectTransform offsets, producing the ~104px downward shift on every
    // clone. Using worldPositionStays:false keeps the prefab's local values.
    private BotSettings InstantiateBotSettingsFlush(Transform parent)
    {
        var go = Instantiate(BotSettings, parent, worldPositionStays: false);
        var rt = go.transform as RectTransform;
        if (rt != null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchoredPosition3D = Vector3.zero;
            rt.localScale = Vector3.one;
        }
        return go.GetComponent<BotSettings>();
    }

    // NOTE: CountNonEmptyProductCards / CountNonEmptyServiceCards lived here.
    // They existed only to decouple the saved *count* from childCount while
    // the slot *keys* were still written at the child index — a mismatch that
    // dropped a product whenever a blank card preceded it. SaveSettings now
    // writes contiguous slots and the running slot counter IS the count, so
    // the helpers had nothing left to compute.

    // Compacts a saved bot's product slot keys: walks PlayerPrefs entries
    // 0..oldCount-1, collects non-empty slots (Name key non-empty per
    // string.IsNullOrEmpty), writes them back at contiguous indices 0..N-1,
    // deletes orphans at [N..oldCount-1], and rewrites the saved
    // ProductsNumber to N. No-op if the data is already clean. Pure data
    // operation — does not touch the live UI / scene.
    private static void CompactSavedProducts(string botKey)
    {
        int oldCount = PlayerPrefs.GetInt(botKey + "ProductsNumber", 0);
        if (oldCount <= 0) return;

        var liveNames = new System.Collections.Generic.List<string>(oldCount);
        var livePrices = new System.Collections.Generic.List<string>(oldCount);
        var liveDescriptions = new System.Collections.Generic.List<string>(oldCount);

        for (int p = 0; p < oldCount; p++)
        {
            var name = PlayerPrefs.GetString(botKey + "Product" + p, "");
            if (string.IsNullOrEmpty(name)) continue;
            liveNames.Add(name);
            livePrices.Add(PlayerPrefs.GetString(botKey + "Product" + p + "Price", ""));
            liveDescriptions.Add(PlayerPrefs.GetString(botKey + "Product" + p + "Description", ""));
        }

        if (liveNames.Count == oldCount) return; // already compact

        for (int p = 0; p < liveNames.Count; p++)
        {
            PlayerPrefs.SetString(botKey + "Product" + p, liveNames[p]);
            PlayerPrefs.SetString(botKey + "Product" + p + "Price", livePrices[p]);
            PlayerPrefs.SetString(botKey + "Product" + p + "Description", liveDescriptions[p]);
        }

        for (int p = liveNames.Count; p < oldCount; p++)
        {
            PlayerPrefs.DeleteKey(botKey + "Product" + p);
            PlayerPrefs.DeleteKey(botKey + "Product" + p + "Price");
            PlayerPrefs.DeleteKey(botKey + "Product" + p + "Description");
        }

        PlayerPrefs.SetInt(botKey + "ProductsNumber", liveNames.Count);
    }

    // Service-side mirror of CompactSavedProducts. Two helpers (rather than
    // a generic) because product and service slot keys differ in name
    // ("Product" vs "Service") and live-list helper signatures differ; the
    // duplication is mechanical and isolated.
    private static void CompactSavedServices(string botKey)
    {
        int oldCount = PlayerPrefs.GetInt(botKey + "ServicesNumber", 0);
        if (oldCount <= 0) return;

        var liveNames = new System.Collections.Generic.List<string>(oldCount);
        var livePrices = new System.Collections.Generic.List<string>(oldCount);
        var liveDescriptions = new System.Collections.Generic.List<string>(oldCount);

        for (int s = 0; s < oldCount; s++)
        {
            var name = PlayerPrefs.GetString(botKey + "Service" + s, "");
            if (string.IsNullOrEmpty(name)) continue;
            liveNames.Add(name);
            livePrices.Add(PlayerPrefs.GetString(botKey + "Service" + s + "Price", ""));
            liveDescriptions.Add(PlayerPrefs.GetString(botKey + "Service" + s + "Description", ""));
        }

        if (liveNames.Count == oldCount) return; // already compact

        for (int s = 0; s < liveNames.Count; s++)
        {
            PlayerPrefs.SetString(botKey + "Service" + s, liveNames[s]);
            PlayerPrefs.SetString(botKey + "Service" + s + "Price", livePrices[s]);
            PlayerPrefs.SetString(botKey + "Service" + s + "Description", liveDescriptions[s]);
        }

        for (int s = liveNames.Count; s < oldCount; s++)
        {
            PlayerPrefs.DeleteKey(botKey + "Service" + s);
            PlayerPrefs.DeleteKey(botKey + "Service" + s + "Price");
            PlayerPrefs.DeleteKey(botKey + "Service" + s + "Description");
        }

        PlayerPrefs.SetInt(botKey + "ServicesNumber", liveNames.Count);
    }

    // One-shot migration that runs at the top of LoadBots(). Walks every
    // saved bot (using the same enumeration LoadBots itself uses) and
    // compacts each bot's products and services slot keys. Idempotent
    // (compacting clean data is a no-op) and synchronous (PlayerPrefs is
    // synchronous). Closes the gap left by Fix C: SaveSettings's saved-
    // count is correct going forward, but pre-existing data with a mid-
    // list phantom slot would still re-create a blank card on next
    // LoadBots without compaction.
    private void MigrateBotPersistence()
    {
        for (int i = 0; i < id; i++)
        {
            string botKey = "Bot" + i.ToString();
            if (!PlayerPrefs.HasKey(botKey + "Name")) continue;
            CompactSavedProducts(botKey);
            CompactSavedServices(botKey);
        }
    }

    public void SaveSettings()
    {
        var newName = openBotSettings.BotNameField.Value;
        PlayerPrefs.SetString(openBot.name + "Name", newName);
        openBotSettings.SyncHeaderTitle();
        var openBotComp = openBot.GetComponent<Bot>();
        if (openBotComp.BotName != null) openBotComp.BotName.text = newName;

        {
            var dd = openBotSettings.BusinessTypeDropdown;
            if (businessTypes.TryGetByIndex(dd.value, out var bt))
                PlayerPrefs.SetString(openBot.name + "BusinessType", bt.id);
        }
        openBot.GetComponent<Bot>()?.RefreshBusinessIcon();
        // The suggestion cloud is vertical-keyed off the id written above — a
        // saved business-type change must re-bind it, or the user sits in front
        // of the old vertical's chips until the next settings open.
        openBotSettings.RefreshPromptSuggestions();

        // Both channel toggles are persisted HERE, synchronously, before this
        // method's trailing EnableSave(). They used to be written inside
        // SaveWorkflows instead, which broke the Save button two ways:
        //   • isOnTelegram was written only AFTER that coroutine's first
        //     network round-trip, so the dirty re-check below still read the
        //     pre-save value and re-lit the button the save had just cleaned —
        //     the user had to press Save a second time to make it dim;
        //   • both writes sat inside the "channel has a real n8n workflow id"
        //     branch, so for a channel whose id is ""/"-1" the toggle change
        //     was never persisted at all and was silently dropped on close.
        // SaveWorkflows still needs the PRE-save values to decide whether a
        // workflow must be (de)activated — GetSaveSettings snapshots them and
        // passes them in, so this write cannot race that decision.
        if (openBotSettings.WhatsappToggle != null)
            PlayerPrefs.SetInt(openBot.name + "isOnWhatsapp", openBotSettings.WhatsappToggle.isOn ? 1 : 0);
        if (openBotSettings.TelegramToggle != null)
            PlayerPrefs.SetInt(openBot.name + "isOnTelegram", openBotSettings.TelegramToggle.isOn ? 1 : 0);

        // C2 card: the subline shows the business TYPE + the channel icons, so it
        // must be re-derived AFTER both the BusinessType write above and the two
        // channel toggles just persisted — reading them earlier repaints the card
        // from pre-save values (invisible today only because BotsPage is inactive
        // while settings are open and Bot.OnEnable refreshes again on return).
        openBotComp.RefreshCardSubline();

        PlayerPrefs.SetString(openBot.name + "WhatsappNumber", openBotSettings.WhatsappNumberField.Value);
        PlayerPrefs.SetString(openBot.name + "TelegramNumber", openBotSettings.TelegramNumberField.Value);

        openBotSettings.WhatsappNumberField.gameObject.SetActive(!openBotSettings.WhatsappNumberField.Value.Equals(""));
        openBotSettings.TelegramNumberField.gameObject.SetActive(!openBotSettings.TelegramNumberField.Value.Equals(""));


        PlayerPrefs.SetString(openBot.name + "Business", openBotSettings.BusinessField.Value);

        PlayerPrefs.SetString(openBot.name + "Prompt", openBotSettings.PromptField.Value);

        var contactFieldsToSave = openBotSettings.ContactFields;
        for (int c = 0; c < contactFieldsToSave.Length; c++)
        {
            if (contactFieldsToSave[c] == null) continue;
            PlayerPrefs.SetString(openBot.name + global::BotSettings.ContactKeys[c], contactFieldsToSave[c].Value);
        }


        // Both lists go through BotSettingsListSlots, which is also what the
        // dirty check reads — one definition of "which rows get saved and
        // where", so a saved screen always evaluates back to clean. Slots are
        // written CONTIGUOUSLY: the old loop wrote each row at its CHILD index
        // while the stored count only counted non-empty rows, so one blank card
        // in the list put the real row at a slot the next load never read (the
        // row vanished) and pinned the Save button lit.
        BotSettingsListSlots.Persist(openBot.name, "Product", "ProductsNumber",
            BotSettingsListSlots.Persistable(ReadProductCards(openBotSettings.ProductsParent)));

        BotSettingsListSlots.Persist(openBot.name, "Service", "ServicesNumber",
            BotSettingsListSlots.Persistable(ReadServiceCards(openBotSettings.ServicesParent)));


        PlayerPrefs.Save(); // Ensure changes are written to disk

        // Prefs now reflect current UI; re-run the dirty check to flip save off.
        EnableSave();
    }

    public void CloseSettings()
    {
        openBotSettings.BotNameField.Value = PlayerPrefs.GetString(openBot.name + "Name", "");
        openBotSettings.SyncHeaderTitle();
        openBotSettings.WhatsappToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(openBot.name + "isOnWhatsapp", 1) == 1);
        openBotSettings.TelegramToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(openBot.name + "isOnTelegram", 1) == 1);
        ApplyBusinessTypeToDropdown(openBotSettings.BusinessTypeDropdown,
            PlayerPrefs.GetString(openBot.name + "BusinessType", ""));
        openBotSettings.WhatsappNumberField.Value = PlayerPrefs.GetString(openBot.name + "WhatsappNumber", "");
        openBotSettings.TelegramNumberField.Value = PlausibleTelegramNumber(PlayerPrefs.GetString(openBot.name + "TelegramNumber", ""));

        openBotSettings.WhatsappNumberField.gameObject.SetActive(!openBotSettings.WhatsappNumberField.Value.Equals(""));
        openBotSettings.TelegramNumberField.gameObject.SetActive(!openBotSettings.TelegramNumberField.Value.Equals(""));

        openBotSettings.BusinessField.Value = PlayerPrefs.GetString(openBot.name + "Business", "");
        openBotSettings.PromptField.Value = PlayerPrefs.GetString(openBot.name + "Prompt", "");
        openBotSettings.RefreshPromptSuggestions();
        LoadContactFields(openBotSettings, openBot.name);


        for (int p = 0; p < openBotSettings.ProductsParent.transform.childCount; p++)
        {
            Destroy(openBotSettings.ProductsParent.transform.GetChild(p).gameObject);
        }

        int ProductsNumber = PlayerPrefs.GetInt(openBot.name + "ProductsNumber", 0);
        for (int p = 0; p < ProductsNumber; p++)
        {
            ProductCardView recreatedProduct = Instantiate(ProductPrefab, ProductPrefab.transform.position, ProductPrefab.transform.rotation, openBotSettings.ProductsParent).GetComponent<ProductCardView>();
            openBotSettings.RegisterProductCard(recreatedProduct);

            recreatedProduct.Name = PlayerPrefs.GetString(openBot.name + "Product" + p, "");
            recreatedProduct.Price = PlayerPrefs.GetString(openBot.name + "Product" + p + "Price", "");
            recreatedProduct.Description = PlayerPrefs.GetString(openBot.name + "Product" + p + "Description", "");
        }

        for (int s = 0; s < openBotSettings.ServicesParent.transform.childCount; s++)
        {
            Destroy(openBotSettings.ServicesParent.transform.GetChild(s).gameObject);
        }

        int ServicesNumber = PlayerPrefs.GetInt(openBot.name + "ServicesNumber", 0);
        for (int s = 0; s < ServicesNumber; s++)
        {
            ServiceCardView recreatedService = Instantiate(ServicePrefab, ServicePrefab.transform.position, ServicePrefab.transform.rotation, openBotSettings.ServicesParent).GetComponent<ServiceCardView>();
            openBotSettings.RegisterServiceCard(recreatedService);

            recreatedService.Name = PlayerPrefs.GetString(openBot.name + "Service" + s, "");
            recreatedService.Price = PlayerPrefs.GetString(openBot.name + "Service" + s + "Price", "");
            recreatedService.Description = PlayerPrefs.GetString(openBot.name + "Service" + s + "Description", "");
        }
    }

    // Self-heals a stale pre-fix status blob persisted in {bot}TelegramNumber: an
    // implausible stored value (old raw-JSON substring) collapses to empty so the field
    // hides via SetActive(!Value.Equals("")) and the dirty-check stays quiet. The correct
    // number repopulates on the next tapi get/status via WappiStatusParser. Telegram-only —
    // the WhatsApp number field is deliberately left untouched.
    private static string PlausibleTelegramNumber(string stored) =>
        WappiStatusParser.IsPlausiblePhone(stored) ? stored : "";

    // Builds the free-text business knowledge sent to n8n as the "Business" field:
    // the description (existing format) plus a labeled contact block. Empty contact
    // lines — and the whole block if no contact is set — are omitted. Pure/static
    // so it is unit-testable without a Manager instance.
    public static string ComposeBusinessKnowledge(
        string description, string phone, string hours, string address, string instagram, string email)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("About Business:\n").Append(description ?? "");

        var contactLines = new System.Collections.Generic.List<string>();
        void AddContact(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                contactLines.Add($"{label}: {value.Trim()}");
        }
        AddContact("Телефон", phone);
        AddContact("Часы работы", hours);
        AddContact("Адрес", address);
        AddContact("Instagram", instagram);
        AddContact("Email", email);

        if (contactLines.Count > 0)
            builder.Append("\n\nКонтакты:\n").Append(string.Join("\n", contactLines));

        return builder.ToString();
    }

    // Convenience overload: pulls the six values off a BotSettings instance.
    // Null-tolerant per field so a prefab that predates the contact cards
    // (or a bot opened before the builder ran) still composes its description.
    public static string ComposeBusinessKnowledge(global::BotSettings settings)
    {
        var contacts = settings.ContactFields;
        string Read(int index) =>
            contacts[index] != null ? contacts[index].Value : "";

        return ComposeBusinessKnowledge(
            settings.BusinessField.Value,
            Read(0), Read(1), Read(2), Read(3), Read(4));
    }

    // Fills the contact cards from PlayerPrefs. Shared by the bot-recreate
    // path and the revert-on-close path.
    private static void LoadContactFields(global::BotSettings settings, string botName)
    {
        var contacts = settings.ContactFields;
        for (int c = 0; c < contacts.Length; c++)
        {
            if (contacts[c] == null) continue;
            contacts[c].Value = PlayerPrefs.GetString(botName + global::BotSettings.ContactKeys[c], "");
        }
    }

    // Recomputes the Save button's interactable state from scratch: dirty when
    // ANY value on the screen differs from what is persisted, clean the moment
    // every value is back at its saved state. Call this after ANY mutation of
    // the screen or of the bot's PlayerPrefs — including the ones that bypass
    // the UI listeners (Toggle.SetIsOnWithoutNotify, direct PlayerPrefs writes
    // from the auth flows), which is exactly where the button used to go stale.
    public void EnableSave()
    {
        // Auth probes and the save flow can land after the screen closed;
        // a missing open bot is normal here, not an error.
        if (openBot == null || openBotSettings == null) return;

        SetBotSettingsSaveInteractable(IsBotSettingsDirty());

        // A card destroyed this frame still counts in childCount until the end
        // of the frame, so run the SAME verdict again once the hierarchy has
        // settled. Only one re-check is ever in flight.
        if (_dirtyRecheck != null) StopCoroutine(_dirtyRecheck);
        _dirtyRecheck = StartCoroutine(RecheckBotSettingsDirtyAfterFrame());
    }

    private Coroutine _dirtyRecheck;

    private IEnumerator RecheckBotSettingsDirtyAfterFrame()
    {
        yield return new WaitForEndOfFrame();
        _dirtyRecheck = null;
        if (openBot == null || openBotSettings == null) yield break;
        SetBotSettingsSaveInteractable(IsBotSettingsDirty());
    }

    public bool IsBotSettingsDirty() =>
        BotSettingsDirtyPolicy.IsDirty(BuildEditedSettingsSnapshot(),
                                       ReadSavedSettings(openBot.name));

    // The live screen. Values are normalized to what SaveSettings would
    // actually persist (trimmed list names, non-empty rows only) so "saved,
    // nothing left to write" always evaluates to clean.
    private BotSettingsSnapshot BuildEditedSettingsSnapshot()
    {
        var snapshot = new BotSettingsSnapshot
        {
            Name = openBotSettings.BotNameField != null ? openBotSettings.BotNameField.Value : "",
            WhatsappOn = openBotSettings.WhatsappToggle != null && openBotSettings.WhatsappToggle.isOn,
            TelegramOn = openBotSettings.TelegramToggle != null && openBotSettings.TelegramToggle.isOn,
            WhatsappNumber = openBotSettings.WhatsappNumberField != null ? openBotSettings.WhatsappNumberField.Value : "",
            TelegramNumber = openBotSettings.TelegramNumberField != null ? openBotSettings.TelegramNumberField.Value : "",
            Business = openBotSettings.BusinessField != null ? openBotSettings.BusinessField.Value : "",
            Prompt = openBotSettings.PromptField != null ? openBotSettings.PromptField.Value : "",
        };

        // Left null when the dropdown resolves to no known type — the
        // placeholder a legacy stored id selects. BotSettingsDirtyPolicy
        // skips a null, matching SaveSettings, which keeps the stored id.
        if (openBotSettings.BusinessTypeDropdown != null
            && businessTypes != null
            && businessTypes.TryGetByIndex(openBotSettings.BusinessTypeDropdown.value, out var editedBt))
        {
            snapshot.BusinessTypeId = editedBt.id;
        }

        var contactFields = openBotSettings.ContactFields;
        snapshot.Contacts = new string[contactFields.Length];
        for (int c = 0; c < contactFields.Length; c++)
            snapshot.Contacts[c] = contactFields[c] != null ? contactFields[c].Value : null;

        // Same filter the save path uses, so a blank card the user added and
        // abandoned reads as clean — it is never written, so there is nothing
        // to save.
        snapshot.Products = BotSettingsListSlots.Persistable(ReadProductCards(openBotSettings.ProductsParent));
        snapshot.Services = BotSettingsListSlots.Persistable(ReadServiceCards(openBotSettings.ServicesParent));

        return snapshot;
    }

    // Raw card data, in list order. ProductCardView and ServiceCardView share
    // no base type exposing Name, hence the two near-identical readers.
    private static List<BotSettingsListItem> ReadProductCards(RectTransform parent)
    {
        var rows = new List<BotSettingsListItem>();
        if (parent == null) return rows;
        for (int i = 0; i < parent.childCount; i++)
        {
            var card = parent.GetChild(i).GetComponent<ProductCardView>();
            if (card != null) rows.Add(new BotSettingsListItem(card.Name, card.Price, card.Description));
        }
        return rows;
    }

    private static List<BotSettingsListItem> ReadServiceCards(RectTransform parent)
    {
        var rows = new List<BotSettingsListItem>();
        if (parent == null) return rows;
        for (int i = 0; i < parent.childCount; i++)
        {
            var card = parent.GetChild(i).GetComponent<ServiceCardView>();
            if (card != null) rows.Add(new BotSettingsListItem(card.Name, card.Price, card.Description));
        }
        return rows;
    }

    // What is on disk for this bot. Defaults mirror the load path
    // (CloseSettings / LoadBots) so a freshly opened, untouched screen is clean.
    // Public so tests can assert the real saved-side read rather than a
    // hand-built stand-in that could drift from it.
    public static BotSettingsSnapshot ReadSavedSettings(string botName)
    {
        var snapshot = new BotSettingsSnapshot
        {
            Name = PlayerPrefs.GetString(botName + "Name", ""),
            BusinessTypeId = PlayerPrefs.GetString(botName + "BusinessType", ""),
            WhatsappOn = PlayerPrefs.GetInt(botName + "isOnWhatsapp", 1) == 1,
            TelegramOn = PlayerPrefs.GetInt(botName + "isOnTelegram", 1) == 1,
            WhatsappNumber = PlayerPrefs.GetString(botName + "WhatsappNumber", ""),
            TelegramNumber = PlausibleTelegramNumber(PlayerPrefs.GetString(botName + "TelegramNumber", "")),
            Business = PlayerPrefs.GetString(botName + "Business", ""),
            Prompt = PlayerPrefs.GetString(botName + "Prompt", ""),
        };

        snapshot.Contacts = new string[global::BotSettings.ContactKeys.Length];
        for (int c = 0; c < snapshot.Contacts.Length; c++)
            snapshot.Contacts[c] = PlayerPrefs.GetString(botName + global::BotSettings.ContactKeys[c], "");

        snapshot.Products = BotSettingsListSlots.Read(botName, "Product", "ProductsNumber");
        snapshot.Services = BotSettingsListSlots.Read(botName, "Service", "ServicesNumber");

        return snapshot;
    }

    private void SetBotSettingsSaveInteractable(bool interactable)
    {
        if (openBotSettings != null && openBotSettings.SaveButton != null)
            openBotSettings.SaveButton.interactable = interactable;
        if (SaveButton != null)
            SaveButton.interactable = interactable;
    }

    //////////////////////////////////////////////////////////
    // BOT SETTINGS — SHARED AUTH PANELS
    //
    // The Add-Bot flow's WhatsappAuth / TelegramAuth panels are reused from
    // Bot Settings so there is a single redesigned auth UI to maintain. These
    // entry points point the panels at an existing bot's profile id, rewire
    // the Back button to a settings-mode callback, and wait for the existing
    // whatsappAuthCompleted / telegramAuthCompleted signal to fire onDone.
    //////////////////////////////////////////////////////////

    private bool _authFromSettings;
    private System.Action _settingsAuthOnDone;
    private System.Action _settingsAuthOnBack;
    private Coroutine _settingsAuthWaiter;

    public void OpenWhatsappAuthFromSettings(string profileId, System.Action onDone, System.Action onBack)
    {
        BeginSettingsAuth(onDone, onBack);
        whatsappProfileId = profileId;
        whatsappAuthCompleted = false;

        if (WhatsappAuthBackButton != null)
        {
            WhatsappAuthBackButton.onClick.RemoveAllListeners();
            WhatsappAuthBackButton.onClick.AddListener(OnSettingsAuthBackPressed);
        }

        ShowWhatsappAuth();

        if (_settingsAuthWaiter != null) StopCoroutine(_settingsAuthWaiter);
        _settingsAuthWaiter = StartCoroutine(WaitForSettingsAuthCompletion(whatsapp: true));
    }

    public void OpenTelegramAuthFromSettings(string profileId, System.Action onDone, System.Action onBack)
    {
        BeginSettingsAuth(onDone, onBack);
        telegramProfileId = profileId;
        telegramAuthCompleted = false;

        if (TelegramAuthBackButton != null)
        {
            TelegramAuthBackButton.onClick.RemoveAllListeners();
            TelegramAuthBackButton.onClick.AddListener(OnSettingsAuthBackPressed);
        }

        ShowTelegramAuth();

        if (_settingsAuthWaiter != null) StopCoroutine(_settingsAuthWaiter);
        _settingsAuthWaiter = StartCoroutine(WaitForSettingsAuthCompletion(whatsapp: false));
    }

    // Phone number the user typed during the shared auth flow. Settings-mode
    // onDone callbacks read this to populate the bot's WhatsappNumberField.
    public string LastAuthedWhatsappNumber => WhatsappNumberInput != null ? WhatsappNumberInput.text : "";
    public string LastAuthedTelegramNumber => TelegramNumberInput != null ? TelegramNumberInput.text : "";
    public string LastAuthedWhatsappProfileId => whatsappProfileId;
    public string LastAuthedTelegramProfileId => telegramProfileId;

    private void BeginSettingsAuth(System.Action onDone, System.Action onBack)
    {
        _authFromSettings = true;
        _settingsAuthOnDone = onDone;
        _settingsAuthOnBack = onBack;
    }

    private IEnumerator WaitForSettingsAuthCompletion(bool whatsapp)
    {
        while (_authFromSettings)
        {
            if (whatsapp && whatsappAuthCompleted) break;
            if (!whatsapp && telegramAuthCompleted) break;
            yield return null;
        }

        if (!_authFromSettings) yield break;

        var done = _settingsAuthOnDone;
        EndSettingsAuth();
        done?.Invoke();
    }

    private void OnSettingsAuthBackPressed()
    {
        // IN-04: mirror CancelBotCreation and stop the QR loop too. OpenWhatsappQRPanel's
        // early-exit checks WhatsappQRPanel.activeSelf, which stays TRUE when only the parent
        // WhatsappAuth is deactivated — so without this the loop kept polling qr/get (up to
        // 5 × 3s) against a profile OnWhatsappAuthFromSettingsBack was concurrently deleting.
        if (_whatsappQrCoroutine != null) { StopCoroutine(_whatsappQrCoroutine); _whatsappQrCoroutine = null; }
        if (_telegramQrCoroutine != null) { StopCoroutine(_telegramQrCoroutine); _telegramQrCoroutine = null; }
        if (_whatsappStatusCoroutine != null) { StopCoroutine(_whatsappStatusCoroutine); _whatsappStatusCoroutine = null; }
        if (_telegramStatusCoroutine != null) { StopCoroutine(_telegramStatusCoroutine); _telegramStatusCoroutine = null; }
        if (WhatsappAuth != null) WhatsappAuth.SetActive(false);
        if (TelegramAuth != null) TelegramAuth.SetActive(false);

        var back = _settingsAuthOnBack;
        EndSettingsAuth();
        back?.Invoke();
    }

    private void EndSettingsAuth()
    {
        _authFromSettings = false;
        _settingsAuthOnDone = null;
        _settingsAuthOnBack = null;
        if (_settingsAuthWaiter != null) { StopCoroutine(_settingsAuthWaiter); _settingsAuthWaiter = null; }

        // Restore the default Add-Bot flow back-button wiring.
        if (WhatsappAuthBackButton != null)
        {
            WhatsappAuthBackButton.onClick.RemoveAllListeners();
            WhatsappAuthBackButton.onClick.AddListener(CancelBotCreation);
        }
        if (TelegramAuthBackButton != null)
        {
            TelegramAuthBackButton.onClick.RemoveAllListeners();
            TelegramAuthBackButton.onClick.AddListener(CancelBotCreation);
        }
    }

    // NOTE: the old CheckProductsOrServicesChanged lived here. It was an
    // if/else-if chain whose second branch (`products.childCount == saved`) was
    // tautologically true once the first branch's count-mismatch test failed,
    // so the third branch — the only place a service card's content was ever
    // compared — was unreachable dead code: editing an existing service's
    // name/price/description could not light Save and the edit was lost on
    // back. It also only ever set the button ON, never OFF, and compared raw
    // childCount against the non-empty saved count, so one blank card pinned
    // Save lit for the rest of the session. Both lists are now part of the
    // single two-way verdict in BotSettingsDirtyPolicy.


    //////////////////////////////////////////////////////////CREATE BOT//////////////////////////////////////////////////////////

    // ── Add Bot Form — Popup Controllers ──

    public void OpenPlatformSelector() => PopupUI.Show(platformSelectorPanel);

    public void ClosePlatformSelector() => PopupUI.Hide(platformSelectorPanel);

    public void SelectPlatform(int mode)
    {
        selectedPlatform = mode;

        bool showWa = mode == 1 || mode == 3;
        bool showTg = mode == 2 || mode == 3;

        if (platformWhatsappGroup != null) platformWhatsappGroup.SetActive(showWa);
        if (platformTelegramGroup != null) platformTelegramGroup.SetActive(showTg);
        if (platformPlusSeparator != null) platformPlusSeparator.SetActive(mode == 3);

        // Hide placeholder ValueText — each group carries its own colored label
        if (platformValueText != null) platformValueText.gameObject.SetActive(false);

        // Force immediate layout rebuild — nested CSF + HLG chains don't resolve on
        // the same frame a previously-inactive child becomes active, so the first
        // selection would show the icon/label overlapping until the next change.
        RectTransform platformIconContainer = null;
        if (platformWhatsappGroup != null)
            platformIconContainer = platformWhatsappGroup.transform.parent as RectTransform;
        else if (platformTelegramGroup != null)
            platformIconContainer = platformTelegramGroup.transform.parent as RectTransform;
        if (platformIconContainer != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(platformIconContainer);
            if (platformIconContainer.parent is RectTransform rowRt)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rowRt);
        }

        ClosePlatformSelector();
        UpdateCreateButtonColor(mode);
        ValidateCreateForm();
    }

    // Tint the create button to the chosen platform's brand color.
    // Button uses ColorTint transition, so the disabled state still greys
    // this base color out via the tint multiplier.
    private void UpdateCreateButtonColor(int mode, bool instant = false)
    {
        if (createBotFormButton == null || createBotFormButton.image == null) return;

        Color target = mode switch
        {
            1 => WhatsappBrandColor,
            2 => TelegramBrandColor,
            _ => CreateButtonDefaultColor, // 0=none and 3=Both keep the default accent
        };

        createBotFormButton.image.DOKill();
        if (instant) createBotFormButton.image.color = target;
        else createBotFormButton.image.DOColor(target, 0.2f);
    }

    public void OpenBotNameInput()
    {
        PopupUI.Show(botNameInputPanel, onCardSettled: () =>
        {
            // Activate the input field after the card settles — the native
            // keyboard slide-up otherwise competes with the open tween.
            if (botNamePopupInput != null) botNamePopupInput.ActivateInputField();
        });

        if (botNamePopupInput != null)
            botNamePopupInput.text = formBotName;
    }

    public void CloseBotNameInput() => PopupUI.Hide(botNameInputPanel);

    public void ConfirmBotName()
    {
        if (botNamePopupInput != null && !string.IsNullOrEmpty(botNamePopupInput.text))
        {
            formBotName = botNamePopupInput.text.Trim();
            botNameValueText.text = formBotName;
            botNameValueText.color = Theme.Color(ThemeRole.InkPrimary); // --text-primary
        }

        CloseBotNameInput();
        ValidateCreateForm();
    }

    public void OpenBusinessSelector() => PopupUI.Show(businessSelectorPanel);

    public void CloseBusinessSelector() => PopupUI.Hide(businessSelectorPanel);

    private void PopulateBusinessTypes()
    {
        if (BusinessTypesParent == null || BusinessTypeButtonTemplate == null || businessTypes == null)
        {
            Debug.LogError("[Manager] PopulateBusinessTypes: missing serialized refs (BusinessTypesParent, BusinessTypeButtonTemplate, or businessTypes).");
            return;
        }

        // Destroy any previously-instantiated buttons (everything except the template).
        for (int i = BusinessTypesParent.childCount - 1; i >= 0; i--)
        {
            var child = BusinessTypesParent.GetChild(i).gameObject;
            if (child == BusinessTypeButtonTemplate) continue;
            DestroyImmediate(child);
        }

        BusinessTypeButtonTemplate.SetActive(false);
        businessTypeButtons.Clear();

        // Resolved BEFORE the loop so every clone can be painted as it is made.
        // The tiles are state-stamped (selected vs default) and so carry no
        // ThemedColor binding; without this they kept the template's light fill
        // until the user selected something.
        businessButtonDefaultColor = Theme.Color(ThemeRole.Background);

        foreach (var entry in businessTypes.All)
        {
            var go = Instantiate(BusinessTypeButtonTemplate, BusinessTypesParent);
            go.SetActive(true);
            go.name = entry.id;

            var tileImage = go.GetComponent<Image>();
            if (tileImage != null) tileImage.color = businessButtonDefaultColor;

            var label = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) label.text = entry.displayName;

            // Icon squircle built into the template by BusinessTileIconBuilder.
            var iconBg = go.transform.Find("IconBg");
            if (iconBg != null)
            {
                var badgeImage = iconBg.GetComponent<Image>();
                if (badgeImage != null) badgeImage.color = entry.tileColor;

                var iconTransform = iconBg.Find("Icon");
                var iconImage = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
                if (iconImage != null) iconImage.sprite = entry.sprite;

                iconBg.gameObject.SetActive(entry.sprite != null);
            }

            var btn = go.GetComponent<Button>();
            var capturedId = entry.id;
            PopupUI.WireFingerUp(btn, () => ChooseBusiness(capturedId));
            businessTypeButtons.Add(btn);
        }

        selectedBusinessId = businessTypes.Count > 0 ? businessTypes.All[0].id : "";
    }

    private void PopulateBusinessDropdown(TMP_Dropdown dd)
    {
        if (dd == null || businessTypes == null) return;

        dd.options.Clear();
        foreach (var entry in businessTypes.All)
            dd.options.Add(new TMP_Dropdown.OptionData(entry.displayName));
        dd.RefreshShownValue();
    }

    // Selects the saved business type in the settings dropdown. A saved id that is
    // missing or no longer offered (legacy vertical like "dentist") must NOT fall back
    // to entry 0 — that would silently migrate the bot to auto_parts on its next save
    // and overwrite its workflow prompt head. Instead a placeholder option is shown
    // past the real entries, where TryGetByIndex fails: saving keeps the stored id and
    // the webhooks get BusinessTypeId="" (workflows then preserve the existing head).
    private void ApplyBusinessTypeToDropdown(TMP_Dropdown dd, string savedId)
    {
        if (dd == null || businessTypes == null) return;

        while (dd.options.Count > businessTypes.Count)
            dd.options.RemoveAt(dd.options.Count - 1);

        int index = businessTypes.IndexOf(savedId);
        if (index < 0)
        {
            dd.options.Add(new TMP_Dropdown.OptionData(UnknownBusinessTypeOption));
            index = dd.options.Count - 1;
        }
        dd.value = index;
        dd.RefreshShownValue();
    }

    // BusinessType/BusinessTypeId form fields for the settings-driven webhook payloads.
    // With the dropdown on the unknown-type placeholder, the id goes empty so the n8n
    // workflows keep the bot's existing prompt head, and the display name falls back to
    // the stored legacy id so the "Business Type:" header line stays meaningful.
    private void AddBusinessTypeFields(WWWForm form)
    {
        bool known = businessTypes.TryGetByIndex(openBotSettings.BusinessTypeDropdown.value, out var entry);
        form.AddField("BusinessType", known ? entry.displayName : PlayerPrefs.GetString(openBot.name + "BusinessType", ""));
        form.AddField("BusinessTypeId", known ? entry.id : "");
    }

    public void ChooseBusiness(string id)
    {
        if (businessTypes == null || !businessTypes.TryGetById(id, out var entry)) return;
        selectedBusinessId = id;
        businessTypeSelected = true;

        for (int i = 0; i < businessTypeButtons.Count; i++)
        {
            var btn = businessTypeButtons[i];
            var img = btn.GetComponent<Image>();
            // Selection used a raw Color.green, which is neither a theme colour
            // nor the app's own green — it read as placeholder art. AccentSoft is
            // the same "selected chip" treatment the rest of the app uses.
            img.color = (btn.gameObject.name == id)
                ? Theme.Color(ThemeRole.AccentSoft)
                : businessButtonDefaultColor;
        }

        if (businessTypeValueText != null)
        {
            businessTypeValueText.text = entry.displayName;
            businessTypeValueText.color = Theme.Color(ThemeRole.InkPrimary);
        }

        CloseBusinessSelector();
        ValidateCreateForm();
    }

    public void OpenDescriptionInput()
    {
        PopupUI.Show(descriptionInputPanel, onCardSettled: () =>
        {
            if (descriptionPopupInput != null) descriptionPopupInput.ActivateInputField();
        });

        if (descriptionPopupInput != null)
            descriptionPopupInput.text = formDescription;
    }

    public void CloseDescriptionInput() => PopupUI.Hide(descriptionInputPanel);

    public void ConfirmDescription()
    {
        if (descriptionPopupInput != null)
        {
            formDescription = descriptionPopupInput.text.Trim();
            if (!string.IsNullOrEmpty(formDescription))
            {
                descriptionValueText.text = formDescription;
                descriptionValueText.color = Theme.Color(ThemeRole.InkPrimary);
            }
            else
            {
                descriptionValueText.text = "Необязательно";
                descriptionValueText.color = Theme.Color(ThemeRole.InkTertiary); // --text-tertiary
            }
        }

        CloseDescriptionInput();
    }

    // ── Form Validation ──

    private void ValidateCreateForm()
    {
        bool isValid = selectedPlatform > 0
                    && !string.IsNullOrEmpty(formBotName)
                    && businessTypeSelected;

        if (createBotFormButton != null)
        {
            createBotFormButton.interactable = isValid;
        }
    }

    // ── Bot Creation Flow ──

    private IEnumerator CreateBotFromForm()
    {
        isCreatingBot = true;
        whatsappAuthCompleted = false;
        telegramAuthCompleted = false;
        whatsappProfileId = "-1";
        telegramProfileId = "-1";
        createBotFormButton.interactable = false;

        bool useWhatsapp = selectedPlatform == 1 || selectedPlatform == 3;
        bool useTelegram = selectedPlatform == 2 || selectedPlatform == 3;

        // Pre-flight gate — checked ONCE here, before ANY profile creation/pairing begins,
        // covering both legs at once. This bot has no Bot component yet (Step 3 instantiates
        // it), so BotsPage's gate (checked once, at wizard entry) and BotSettings.Auth's gate
        // (operates on an already-instantiated Manager.openBot) never see this wizard's own
        // fresh-profile creation below — it needs its own check.
        //
        // Deliberately NOT per-leg: a per-leg check let platform «Оба» pass Step 1 (room for
        // one more channel), walk the user through a full WhatsApp pairing (QR/code, the
        // remote wait), and only then discover Step 2 doesn't fit — CancelBotCreation would
        // then silently delete the just-authorized profile the user just spent real effort
        // on. Checking total demand up front means a block costs nothing: no QR shown, no
        // pairing started. whatsappProfileId/telegramProfileId are still "-1" here (just
        // reset above), so CancelBotCreation's delete-profile branches are both skipped —
        // this call is a pure UI/coroutine reset, no network calls.
        int channelDemand = (useWhatsapp ? 1 : 0) + (useTelegram ? 1 : 0);
        int existingBotsForGate = BotsParent != null ? BotsParent.transform.childCount : 0;
        bool botOk = EntitlementGate.CanCreateBot(existingBotsForGate);
        bool channelsOk = EntitlementGate.CanConnectChannels(EntitlementGate.ConnectedChannelCount(), channelDemand);
        if (!botOk || !channelsOk)
        {
            EntitlementGate.RequestPaywall(!botOk ? PaywallTrigger.BotLimit : PaywallTrigger.ChannelLimit);
            CancelBotCreation();
            yield break;
        }

        // Step 1: Create WhatsApp profile and authenticate
        if (useWhatsapp)
        {
            yield return StartCoroutine(CreateWhatsappProfile(formBotName, true));
            if (!isCreatingBot) yield break;

            ShowWhatsappAuth();
            PlayerPrefs.SetString("WhatsappCooldownFinishTime", "-1");
            WhatsappCodeTimer.SetActive(false);

            while (!whatsappAuthCompleted)
            {
                if (!isCreatingBot) yield break;
                yield return new WaitForSeconds(0.5f);
            }
            // WR-02: the loop exits on whatsappAuthCompleted WITHOUT re-testing isCreatingBot, so a
            // back-press landing in the auth-completion → resume gap (up to ~0.5s) would run
            // CancelBotCreation — deleting the just-authorized profile — and still fall through to
            // Step 3, persisting a bot card pointing at a deleted profile. Re-check before continuing.
            if (!isCreatingBot) yield break;
        }

        // Step 2: Create Telegram profile and authenticate
        if (useTelegram)
        {
            yield return StartCoroutine(CreateTelegramProfile(formBotName, true));
            if (!isCreatingBot) yield break;

            ShowTelegramAuth();
            LoadingPanel.SetActive(false);
            PlayerPrefs.SetString("TelegramCooldownFinishTime", "-1");
            TelegramCodeTimer.SetActive(false);

            while (!telegramAuthCompleted)
            {
                if (!isCreatingBot) yield break;
                yield return new WaitForSeconds(0.5f);
            }
            // WR-02 (Telegram leg — same completion → resume gap as the WhatsApp loop above).
            if (!isCreatingBot) yield break;
        }

        // Step 3: Instantiate bot
        GameObject newBot = Instantiate(BotPrefab, BotPrefab.transform.position, BotPrefab.transform.rotation, BotsParent.transform);
        newBot.name = "Bot" + id.ToString();

        // The wizard forced the Bots tab, so Screen_Bots never re-enables and its
        // OnEnable empty-state refresh won't re-fire — hide it now that a card exists.
        global::BotsPage.Instance?.RefreshEmptyState();

        var newBotComp = newBot.GetComponent<Bot>();
        if (newBotComp.BotName != null) newBotComp.BotName.text = formBotName;
        // C2 card: the subline derives itself (business type + channel icons) one
        // frame after the rename — no BotDesc/master-key writes needed here.
        if (newBotComp.Status != null) newBotComp.Status.text = "Connecting..";
        newBot.GetComponent<Bot>().active = false;
        newBot.GetComponent<Bot>().EditButton.interactable = false;
        newBotComp.SetActivationInteractable(false);
        newBot.GetComponent<Bot>().whatsappProfileId = whatsappProfileId;
        newBot.GetComponent<Bot>().telegramProfileId = telegramProfileId;

        BotSettings newBotSettings = InstantiateBotSettingsFlush(BotSettingsParentStatic.transform);

        newBotSettings.BotNameField.Value = formBotName;
        newBotSettings.SyncHeaderTitle();
        newBotSettings.WhatsappToggle.isOn = useWhatsapp;
        newBotSettings.TelegramToggle.isOn = useTelegram;
        PopulateBusinessDropdown(newBotSettings.BusinessTypeDropdown);
        ApplyBusinessTypeToDropdown(newBotSettings.BusinessTypeDropdown, selectedBusinessId);
        newBotSettings.BusinessField.Value = formDescription;
        newBotSettings.WhatsappNumberField.Value = useWhatsapp ? WhatsappNumberInput.text : "";
        newBotSettings.TelegramNumberField.Value = useTelegram ? TelegramNumberInput.text : "";
        newBotSettings.WhatsappNumberField.gameObject.SetActive(useWhatsapp && !string.IsNullOrEmpty(WhatsappNumberInput.text));
        newBotSettings.TelegramNumberField.gameObject.SetActive(useTelegram && !string.IsNullOrEmpty(TelegramNumberInput.text));

        // Step 4: Create workflows
        if (useWhatsapp)
        {
            StartCoroutine(CreateWhatsappWorkflowFromStart(newBot));
        }
        else
        {
            newBot.GetComponent<Bot>().whatsappWorkflowId = "-1";
            PlayerPrefs.SetString(newBot.name + "WhatsappWorkflowId", "-1");
        }

        if (useTelegram)
        {
            StartCoroutine(CreateTelegramWorkflowFromStart(newBot));
        }
        else
        {
            newBot.GetComponent<Bot>().telegramWorkflowId = "-1";
            PlayerPrefs.SetString(newBot.name + "TelegramWorkflowId", "-1");
        }

        // Step 5: Save to PlayerPrefs
        PlayerPrefs.SetString(newBot.name + "Name", formBotName);
        PlayerPrefs.SetInt(newBot.name + "isOn", 1);
        PlayerPrefs.SetString(newBot.name + "Status", "Connecting..");
        PlayerPrefs.SetInt(newBot.name + "Active", 0);
        PlayerPrefs.SetInt(newBot.name + "isOnWhatsapp", useWhatsapp ? 1 : 0);
        PlayerPrefs.SetInt(newBot.name + "isOnTelegram", useTelegram ? 1 : 0);
        PlayerPrefs.SetString(newBot.name + "BusinessType", selectedBusinessId);
        // Bot.Awake() fires during Instantiate before the rename + PlayerPrefs
        // write above, so the icon was never applied. Apply it explicitly now.
        newBot.GetComponent<Bot>()?.RefreshBusinessIcon();
        PlayerPrefs.SetString(newBot.name + "Business", formDescription);
        PlayerPrefs.SetString(newBot.name + "WhatsappNumber", useWhatsapp ? WhatsappNumberInput.text : "");
        PlayerPrefs.SetString(newBot.name + "TelegramNumber", useTelegram ? TelegramNumberInput.text : "");

        PlayerPrefs.SetInt("ids", ++id);
        PlayerPrefs.Save();

        // Start the fixed post-creation sync window for the WhatsApp tab. Anchored
        // here (after auth completed earlier in the wizard) so it lines up with when
        // Wappi actually begins importing chats for the new profile.
        if (useWhatsapp)
        {
            long syncUntil = System.DateTimeOffset.UtcNow
                .AddSeconds(ChatManager.WhatsAppSyncWindowSeconds)
                .ToUnixTimeMilliseconds();
            PlayerPrefs.SetString(newBot.name + "WhatsappSyncUntil", syncUntil.ToString());
            PlayerPrefs.Save();
        }

        // Same fixed window for a just-created Telegram bot (08-19 D13a): stamps the
        // per-channel sibling key so the shared SyncingState cover (spinner + ~5-min
        // progress slider) shows over the chats list on Telegram exactly like WhatsApp.
        if (useTelegram)
        {
            long telegramSyncUntil = System.DateTimeOffset.UtcNow
                .AddSeconds(ChatManager.WhatsAppSyncWindowSeconds)
                .ToUnixTimeMilliseconds();
            PlayerPrefs.SetString(newBot.name + "TelegramSyncUntil", telegramSyncUntil.ToString());
            PlayerPrefs.Save();
        }

        // Make the just-created bot the active one so the chat UI resolves to it
        // (syncing screen or chat list) the moment the user opens the WhatsApp tab.
        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.SetActiveBot(newBot.name);
        }

        // Step 6: Reset form
        ResetAddBotForm();
        isCreatingBot = false;

        // D3: refresh the checklist underneath the success overlay so it already shows the
        // correct «N из 4» (rows 1-2 checked) the moment the overlay dismisses onto Bots —
        // the AddBotPanel is an overlay on the still-active Bots page, so OnEnable never
        // re-fires. Fire-and-forget, null-guarded.
        FirstStepsCard.Instance?.RefreshFromFacts();

        // Interactive success moment — final auth, bot now exists. D2: the moment renders on a
        // standalone full-screen overlay (channel-agnostic), so it no longer takes a channel arg.
        // This is the ONE creation-flow site — ShowAuthSuccess never re-fires it (its else branch
        // is gated on !isCreatingBot), so a "both" creation shows the moment exactly once.
        yield return StartCoroutine(ShowInteractiveSuccessMoment(newBotComp));
    }

    /// <summary>
    /// Public close for the Add-Bot overlay (back chevron / swipe). Cancels an
    /// in-flight wizard (deletes any half-created Wappi profiles) exactly like the
    /// auth back buttons, then slides the panel away.
    /// </summary>
    public void CloseAddBotForm()
    {
        if (isCreatingBot) CancelBotCreation();
        AddBotPanel.Instance?.Close();

        // D1 belt-and-suspenders for the wizard back-out: refresh so the card hides if the
        // back-out left zero bots, even if RefreshEmptyState does not re-run this frame.
        FirstStepsCard.Instance?.RefreshFromFacts();
    }

    private void CancelBotCreation()
    {
        isCreatingBot = false;

        // Stop auth polling before deleting the profiles — the status loops only
        // exit on authorized:true, so they'd poll the deleted profiles forever.
        if (_whatsappQrCoroutine != null) { StopCoroutine(_whatsappQrCoroutine); _whatsappQrCoroutine = null; }
        if (_telegramQrCoroutine != null) { StopCoroutine(_telegramQrCoroutine); _telegramQrCoroutine = null; }
        if (_whatsappStatusCoroutine != null) { StopCoroutine(_whatsappStatusCoroutine); _whatsappStatusCoroutine = null; }
        if (_telegramStatusCoroutine != null) { StopCoroutine(_telegramStatusCoroutine); _telegramStatusCoroutine = null; }

        WhatsappAuth.SetActive(false);
        TelegramAuth.SetActive(false);

        // Clear cooldowns — profiles are deleted on back, so timers are invalid
        PlayerPrefs.SetString("WhatsappCooldownFinishTime", "-1");
        PlayerPrefs.SetString("TelegramCooldownFinishTime", "-1");

        if (!whatsappProfileId.Equals("-1"))
        {
            StartCoroutine(DeleteWhatsappProfile(whatsappProfileId, true));
        }
        if (!telegramProfileId.Equals("-1"))
        {
            StartCoroutine(DeleteTelegramProfile(telegramProfileId, true));
        }

        ValidateCreateForm();
    }

    private void ResetAddBotForm()
    {
        selectedPlatform = 0;
        formBotName = "";
        formDescription = "";
        businessTypeSelected = false;
        selectedBusinessId = businessTypes.Count > 0 ? businessTypes.All[0].id : "";

        if (platformValueText != null)
        {
            platformValueText.text = "Выберите";
            platformValueText.color = Theme.Color(ThemeRole.InkTertiary);
            platformValueText.gameObject.SetActive(true);
        }
        if (platformWhatsappGroup != null) platformWhatsappGroup.SetActive(false);
        if (platformTelegramGroup != null) platformTelegramGroup.SetActive(false);
        if (platformPlusSeparator != null) platformPlusSeparator.SetActive(false);
        if (botNameValueText != null)
        {
            botNameValueText.text = "Введите имя";
            botNameValueText.color = Theme.Color(ThemeRole.InkTertiary);
        }
        if (businessTypeValueText != null)
        {
            businessTypeValueText.text = "Выберите тип";
            businessTypeValueText.color = Theme.Color(ThemeRole.InkTertiary);
        }
        if (descriptionValueText != null)
        {
            descriptionValueText.text = "Необязательно";
            descriptionValueText.color = Theme.Color(ThemeRole.InkTertiary);
        }

        if (WhatsappNumberInput != null) WhatsappNumberInput.text = "";
        if (TelegramNumberInput != null) TelegramNumberInput.text = "";

        foreach (var btn in businessTypeButtons)
        {
            btn.GetComponent<Image>().color = businessButtonDefaultColor;
        }

        if (createBotFormButton != null) createBotFormButton.interactable = false;
        UpdateCreateButtonColor(0, instant: true);
    }


    //////////////////////////////////////////////////////////AUTHORIZATION//////////////////////////////////////////////////////////

    public void RebuildWhatsappAuthLayout() => ForceRebuildLayout(WhatsappAuth);
    public void RebuildTelegramAuthLayout() => ForceRebuildLayout(TelegramAuth);

    private static void SetButtonText(Button btn, string text)
    {
        if (btn == null) return;
        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null) tmp.text = text;
    }

    private void ForceRebuildLayout(GameObject authPage)
    {
        Canvas.ForceUpdateCanvases();
        var scrollRect = authPage.GetComponentInChildren<ScrollRect>();
        if (scrollRect == null) return;

        // Rebuild innermost ContentSizeFitters first (panels), then root content
        var fitters = scrollRect.content.GetComponentsInChildren<ContentSizeFitter>();
        for (int i = fitters.Length - 1; i >= 0; i--)
        {
            if (fitters[i].transform is RectTransform childRt)
                LayoutRebuilder.ForceRebuildLayoutImmediate(childRt);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
        Canvas.ForceUpdateCanvases();
    }

    private IEnumerator SmoothScrollToBottom(GameObject authPage, float duration = 0.4f)
    {
        var scrollRect = authPage.GetComponentInChildren<ScrollRect>();
        if (scrollRect == null) yield break;

        float start = scrollRect.normalizedPosition.y;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            scrollRect.normalizedPosition = new Vector2(0f, Mathf.Lerp(start, 0f, t));
            yield return null;
        }

        scrollRect.normalizedPosition = Vector2.zero;
    }

    private IEnumerator ShowAuthSuccess(GameObject authPage, GameObject successPanel)
    {
        // Compute the intermediate-step flag FIRST. The WhatsApp leg of a "both" creation is
        // the ONLY case that keeps the old transient 2s success check + LoadingPanel cover +
        // authPage deactivation (it covers the transition to the Telegram auth page). The
        // final creating-bot auth AND settings re-auth hand off to the interactive success
        // moment, which owns the panel show/dismiss AND the authPage deactivation — so they
        // must NOT flash the 2s panel and must leave authPage ACTIVE (the panel is nested
        // inside authPage; deactivating it now would hide the moment).
        bool moreAuthSteps = isCreatingBot && selectedPlatform == 3 && authPage != TelegramAuth;

        if (moreAuthSteps)
        {
            if (successPanel != null)
            {
                // Block all interaction (back button, etc.) during the success animation
                var cg = authPage.GetComponent<CanvasGroup>();
                if (cg == null) cg = authPage.AddComponent<CanvasGroup>();
                cg.interactable = false;
                cg.blocksRaycasts = true;

                // Scroll to top so the QR container (with checkmark) is visible
                var scrollRect = authPage.GetComponentInChildren<ScrollRect>();
                if (scrollRect != null)
                    scrollRect.normalizedPosition = Vector2.one;

                successPanel.SetActive(true);
                yield return new WaitForSeconds(2f);
                successPanel.SetActive(false);

                cg.interactable = true;
            }

            // Cover transition to the next auth page (Telegram), then deactivate this page.
            LoadingPanel.SetActive(true);
            authPage.SetActive(false);
        }
        else if (!isCreatingBot && Manager.openBot != null)
        {
            // D16 (08-REVIEW IN-01): a bot that already has WhatsApp gets NO Telegram sync cover when
            // Telegram is authed later, because {bot}TelegramSyncUntil is stamped ONLY in the creation
            // wizard (CreateBotFromForm). Stamp the same 300s per-channel window here on a late Telegram
            // auth so the shared SyncingState cover fires when the user opens the newly-authed Telegram
            // channel — mirrors the wizard stamp at Manager.cs:1490-1497 and reuses the already-tested
            // {bot}TelegramSyncUntil key (ChatManager.SyncUntilSuffixFor(Telegram); Bot.DeleteBot clears it).
            //
            // COVER PARITY (D17, round-5 owner scope-override — SUPERSEDES the 08-28 Telegram-only parity
            // decision): stamp on late auth of EITHER channel so the post-creation sync cover fires for both
            // WhatsApp and Telegram every time a channel is added late (owner-approved, like D14). Each stamp is
            // channel-gated by authPage and writes only its own per-channel {bot}...SyncUntil key.
            if (authPage == TelegramAuth)
            {
                long telegramSyncUntil = System.DateTimeOffset.UtcNow
                    .AddSeconds(ChatManager.WhatsAppSyncWindowSeconds)
                    .ToUnixTimeMilliseconds();
                PlayerPrefs.SetString(Manager.openBot.name + "TelegramSyncUntil", telegramSyncUntil.ToString());
                PlayerPrefs.Save();
            }
            else if (authPage == WhatsappAuth)
            {
                long whatsappSyncUntil = System.DateTimeOffset.UtcNow
                    .AddSeconds(ChatManager.WhatsAppSyncWindowSeconds)
                    .ToUnixTimeMilliseconds();
                PlayerPrefs.SetString(Manager.openBot.name + "WhatsappSyncUntil", whatsappSyncUntil.ToString());
                PlayerPrefs.Save();
            }

            // D3: a channel just authed on an existing bot → refresh the checklist so its
            // «Подключить …» row flips to done immediately (fire-and-forget, null-guarded).
            FirstStepsCard.Instance?.RefreshFromFacts();

            // Settings re-auth: the Manager.openBot bot already exists → interactive moment
            // with the files-exist fallback («Открыть чаты»). D2: the moment renders on the
            // standalone overlay (channel-agnostic) and deactivates both auth hierarchies itself,
            // so no channel arg and no authPage hand-off. Gated on !isCreatingBot so the creation
            // flow (which fires the moment from CreateBotFromForm) never double-fires.
            StartCoroutine(ShowInteractiveSuccessMoment(Manager.openBot.GetComponent<Bot>()));

            // …then HOLD until that moment's in-box checkmark has had its full dwell. Returning
            // immediately let our caller raise whatsappAuthCompleted/telegramAuthCompleted, which
            // runs the settings onDone → Create*WorkflowFromEdit → LoadingPanel.SetActive(true)
            // a frame or two into the 1.2s dwell; the cover is a Canvas child ABOVE the auth
            // pages, so the checkmark was hidden almost immediately. The wizard never had this
            // problem because it fires the moment AFTER its workflow POST is already in flight.
            //
            // Deliberately fixed by DELAYING the signal rather than by moving the moment to fire
            // after onDone (the wizard's literal ordering): that alternative leaves the whole
            // multi-second workflow POST running with no full-screen cover, and
            // Create*WorkflowFromEdit dereferences the mutable static Manager.openBot AFTER its
            // yield — a back-press out of BotSettings (SettleClosedInstant nulls openBot) would
            // then NRE past the workflow-id write and PendingProfileLedger.MarkWhatsappClaimed(),
            // stranding a dead channel and letting the quit/launch sweep delete the profile the
            // user just connected. Holding here keeps the cover up for the POST exactly as before.
            //
            // Bounded: the flag starts true and is cleared only for the duration of a dwell that
            // actually runs, so this normally costs SuccessInBoxCheckSeconds and nothing when
            // there is no checkmark to show. The cap is a backstop — a moment killed mid-dwell
            // must not hang the auth hand-off (no workflow would ever be created).
            float inBoxWaited = 0f;
            while (!_inBoxCheckDone && inBoxWaited < SuccessInBoxCheckSeconds + 1f)
            {
                inBoxWaited += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        // else: final creating-bot auth — do nothing here. CreateBotFromForm fires the
        // interactive moment after the bot card exists; authPage stays active for it.

        yield break;
    }

    // Interactive «Бот подключён!» moment — the FINAL success beat after auth completes and
    // the bot exists. D2 (owner decision 2026-07-18): the moment now lives on a STANDALONE
    // full-screen overlay (SuccessOverlay), a Canvas-level sibling of the ScreenContainer that
    // renders ABOVE the auth pages — so NOTHING of the code-entry UI shows beneath it. The old
    // authPage-reactivation hack is gone: the overlay is a single shared hierarchy, so one field
    // set serves both channels (channel-agnostic — no useTelegram arg). Replaces the fixed 2s
    // auto-dismiss with a wait-for-user overlay whose primary CTA deep-links into the just-authed
    // bot's «Прайс-листы» tab (fallback «Открыть чаты» when files already exist). Fired from
    // exactly two sites (CreateBotFromForm after creation; ShowAuthSuccess's else branch for
    // settings re-auth) — never from BotSettings (this is a private Manager member).
    //
    // The in-box checkmark dwell below must run with NO loading cover over it. The wizard gets
    // that for free (it fires this moment after its workflow POST is already in flight, so the
    // cover is up before we lower it and nothing raises it again). Settings re-auth instead
    // holds its auth-completion signal until _inBoxCheckDone flips back — see ShowAuthSuccess.
    // Dwell for the in-box QR/code checkmark shown before the standalone success page.
    private const float SuccessInBoxCheckSeconds = 1.2f;

    private IEnumerator ShowInteractiveSuccessMoment(Bot bot)
    {
        // Nothing is dwelling until the checkmark branch below actually starts. Reset up front
        // (this runs synchronously inside StartCoroutine) so ShowAuthSuccess's settings branch,
        // which waits on this flag right after starting us, never waits on a stale value or on a
        // moment that returns early.
        _inBoxCheckDone = true;

        if (bot == null) yield break;
        if (SuccessOverlay == null) yield break;

        // IN-03: re-entrancy guard. The wait-for-dismiss loop below spins on a `dismissed`
        // closure captured by the button listeners; a second invocation would RemoveAllListeners
        // and rewire them to a NEW closure, so the first coroutine's flag could never flip and it
        // would yield forever (leaked coroutine). Unreachable today — the opaque overlay blocks
        // input and both fire sites are gated — so this is defensive.
        //
        // Latched on a FIELD, not SuccessOverlay.activeSelf: the overlay only goes active after
        // the in-box checkmark dwell below, so an activeSelf test would leave that ~1.2s window
        // unguarded — exactly when a second invocation could still rewire the buttons.
        if (_successMomentRunning) yield break;
        _successMomentRunning = true;

        // Restore the pre-D2 in-box checkmark: before opening the standalone success page,
        // flash the just-authed channel's nested success sheet (the original QR/code-box
        // checkmark — 11-08 kept these panels, removed only their injected CTA) for a beat,
        // exactly as it appeared before we split out a separate success page. The active
        // auth page is the one that just authed; both are still active here (the creation
        // final-auth and settings re-auth paths leave authPage active for this moment).
        GameObject activeAuth =
            (WhatsappAuth != null && WhatsappAuth.activeSelf) ? WhatsappAuth :
            (TelegramAuth != null && TelegramAuth.activeSelf) ? TelegramAuth : null;
        GameObject inBoxCheck =
            activeAuth == WhatsappAuth ? WhatsappAuthSuccessPanel :
            activeAuth == TelegramAuth ? TelegramAuthSuccessPanel : null;
        if (activeAuth != null && inBoxCheck != null)
        {
            // Settings re-auth holds its auth-completion signal until this clears again, so the
            // workflow POST that signal kicks off cannot raise the cover mid-dwell.
            _inBoxCheckDone = false;
            // The loading cover is drawn above the screens — hide it so the in-box checkmark is
            // actually visible for its full dwell instead of being instantly covered (the
            // standalone overlay shown right after replaces it, so nothing flickers).
            if (LoadingPanel != null) LoadingPanel.SetActive(false);
            // LoadingPanel used to block input here; keep the auth page uninteractable for the
            // dwell (the create-workflow webhook is still in flight — a stray back/tap must not
            // land) — mirrors the moreAuthSteps guard. Restore interactable before moving on so
            // a later re-auth on the same page isn't left stuck.
            var authCg = activeAuth.GetComponent<CanvasGroup>();
            if (authCg == null) authCg = activeAuth.AddComponent<CanvasGroup>();
            authCg.interactable = false;
            authCg.blocksRaycasts = true;
            // Scroll the QR/code container to top so the check is on-screen (mirrors the
            // surviving moreAuthSteps branch). This toggles only the success sheet + scroll —
            // the auth CODE flow (GetChild states, requests) is never touched.
            var sr = activeAuth.GetComponentInChildren<ScrollRect>();
            if (sr != null) sr.normalizedPosition = Vector2.one;
            inBoxCheck.SetActive(true);
            yield return new WaitForSeconds(SuccessInBoxCheckSeconds);
            inBoxCheck.SetActive(false);
            authCg.interactable = true;
            // Dwell served — release ShowAuthSuccess's settings-re-auth hold.
            _inBoxCheckDone = true;
        }

        // D2: the overlay is standalone (Canvas-level, above the auth pages) — NO authPage
        // reactivation. Deactivate BOTH auth hierarchies defensively (pre-phase behaviour) so
        // nothing of the code-entry UI shows beneath the overlay.
        if (WhatsappAuth != null) WhatsappAuth.SetActive(false);
        if (TelegramAuth != null) TelegramAuth.SetActive(false);

        // Files-exist fact (both content types) → CTA target.
        bool hasFiles = UploadedFilesStore.Load(bot.name, "product").Count > 0
                     || UploadedFilesStore.Load(bot.name, "service").Count > 0;
        var cta = SuccessCtaSelector.Choose(hasFiles);

        if (successTitleLabel != null) successTitleLabel.text = "Бот подключён!";
        // IN-02: branch the BODY on the CTA too. It used to always urge «загрузите прайс-лист»
        // even when the files-exist path had already switched the button to «Открыть чаты» —
        // the body asked for an action the button no longer offered.
        if (successBodyLabel != null) successBodyLabel.text = cta == SuccessCta.UploadPriceList
            ? "Осталось научить бота вашим ценам — загрузите прайс-лист, и он будет отвечать по вашим товарам"
            : "Бот уже знает ваши цены — откройте чаты и посмотрите, как он отвечает";
        if (successPrimaryLabel != null) successPrimaryLabel.text =
            cta == SuccessCta.UploadPriceList ? "Загрузить прайс-лист" : "Открыть чаты";

        // Wire buttons fresh each show (clear old listeners to avoid stacking).
        bool dismissed = false;
        if (successPrimaryButton != null)
        {
            successPrimaryButton.onClick.RemoveAllListeners();
            successPrimaryButton.onClick.AddListener(() =>
            {
                dismissed = true;
                CloseSuccessAndOverlay();
                if (cta == SuccessCta.UploadPriceList) bot.OpenSettingsAtProductTab();
                else BottomTabManager.Instance?.SwitchTab(BottomTabManager.WhatsAppTabIndex);   // IN-08
            });
        }
        if (successLaterButton != null)
        {
            successLaterButton.onClick.RemoveAllListeners();
            successLaterButton.onClick.AddListener(() =>
            {
                dismissed = true;
                CloseSuccessAndOverlay();   // normal post-auth destination = Bots tab
            });
        }

        SuccessOverlay.SetActive(true);
        // No fixed auto-dismiss — WAIT for the user (replaces the old WaitForSeconds(2f)).
        while (!dismissed) yield return null;
    }

    // Hide the standalone success overlay, close the Add-Bot overlay if open, land on Bots.
    // D2: parameterless — the authPage-deferral hack died with the relocation (auth pages are
    // deactivated up front in ShowInteractiveSuccessMoment).
    private void CloseSuccessAndOverlay()
    {
        if (SuccessOverlay != null) SuccessOverlay.SetActive(false);
        _successMomentRunning = false;   // IN-03: release the re-entrancy latch
        AddBotPanel.Instance?.CloseImmediate();
        BottomTabManager.Instance?.SwitchTab(BottomTabManager.BotsTabIndex);   // IN-08
        // IN-05 follow-up: the checklist's entrance cascade was consumed while the card sat
        // behind this overlay (RefreshFromFacts runs right after bot creation), so the user
        // never saw it. Replay it now that the Bots page is actually on screen.
        FirstStepsCard.Instance?.ReplayEntrance();
    }

    private void ShowWhatsappAuth()
    {
        // Set up all child states BEFORE activating root to prevent layout jitter
        WhatsappQRPanel.SetActive(true);
        WhatsappQRCodeImage.texture = null;
        WhatsappQRStatusText.GetComponent<TextMeshProUGUI>().text = "Загрузка...";
        WhatsappQRStatusText.SetActive(true);

        WhatsappCodePanel.SetActive(true);
        WhatsappNumberInput.gameObject.SetActive(true);
        GetWhatsappCodeButton.gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(false);
        WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(false);
        if (ChangeWhatsappNumberButton != null) ChangeWhatsappNumberButton.gameObject.SetActive(false);
        WhatsappCodePanel.transform.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
        SetButtonText(GetWhatsappCodeButton, "Получить код");

        // Restore timer/button state from persisted cooldown
        string waCooldown = PlayerPrefs.GetString("WhatsappCooldownFinishTime", "-1");
        if (!waCooldown.Equals("-1") && DateTime.TryParse(waCooldown, out var waCooldownEnd) && waCooldownEnd > DateTime.Now)
        {
            WhatsappCodeTimer.SetActive(true);
            GetWhatsappCodeButton.interactable = false;
        }
        else
        {
            if (!waCooldown.Equals("-1"))
                PlayerPrefs.SetString("WhatsappCooldownFinishTime", "-1");
            WhatsappCodeTimer.SetActive(false);
            GetWhatsappCodeButton.interactable = WhatsappNumberInput.text.Length >= 10;
        }

        if (WhatsappAuthSuccessPanel != null) WhatsappAuthSuccessPanel.SetActive(false);

        _whatsappCodeIssued = false;

        // Activate root LAST — everything appears in its final state
        WhatsappAuth.SetActive(true);
        if (_whatsappQrCoroutine != null) StopCoroutine(_whatsappQrCoroutine);
        _whatsappQrCoroutine = StartCoroutine(OpenWhatsappQRPanel());
    }

    private void ShowTelegramAuth()
    {
        // Set up all child states BEFORE activating root to prevent layout jitter
        TelegramQRPanel.SetActive(true);
        TelegramQRCodeImage.texture = null;
        TelegramQRStatusText.GetComponent<TextMeshProUGUI>().text = "Загрузка...";
        TelegramQRStatusText.SetActive(true);

        TelegramCodePanel.SetActive(true);
        TelegramNumberInput.gameObject.SetActive(true);
        GetTelegramCodeButton.gameObject.SetActive(true);
        TelegramCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        TelegramCodeInput.gameObject.SetActive(false);
        SendTelegramCodeButton.gameObject.SetActive(false);
        if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.gameObject.SetActive(false);
        _telegram2faMode = false;
        TelegramCodeInput.text = "";
        SetButtonText(GetTelegramCodeButton, "Получить код");
        SetTelegramCodeEntryTexts(false);

        // Restore timer/button state from persisted cooldown
        string tgCooldown = PlayerPrefs.GetString("TelegramCooldownFinishTime", "-1");
        if (!tgCooldown.Equals("-1") && DateTime.TryParse(tgCooldown, out var tgCooldownEnd) && tgCooldownEnd > DateTime.Now)
        {
            TelegramCodeTimer.SetActive(true);
            GetTelegramCodeButton.interactable = false;
        }
        else
        {
            if (!tgCooldown.Equals("-1"))
                PlayerPrefs.SetString("TelegramCooldownFinishTime", "-1");
            TelegramCodeTimer.SetActive(false);
            GetTelegramCodeButton.interactable = TelegramNumberInput.text.Length >= 10;
        }

        if (TelegramAuthSuccessPanel != null) TelegramAuthSuccessPanel.SetActive(false);

        // Activate root LAST — everything appears in its final state
        TelegramAuth.SetActive(true);
        if (_telegramQrCoroutine != null) StopCoroutine(_telegramQrCoroutine);
        _telegramQrCoroutine = StartCoroutine(OpenTelegramQRPanel());
    }

    private IEnumerator OpenWhatsappQRPanel()
    {
        // Clear any stale QR before showing the loading label. On a resend the
        // deleted profile's QR is still on screen; without this the "Загрузка..."
        // text renders on top of the old QR instead of a clean box.
        WhatsappQRCodeImage.texture = null;
        WhatsappQRStatusText.GetComponent<TextMeshProUGUI>().text = "Загрузка...";
        WhatsappQRStatusText.SetActive(true);

        // Wait for Wappi to finish provisioning the newly created profile
        yield return new WaitForSeconds(3f);
        if (!WhatsappQRPanel.activeSelf) yield break;

        string lastError = "Server Unavailable.\nTry Again Later";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!WhatsappQRPanel.activeSelf) yield break;

            using (var www = UnityWebRequest.Get($"https://wappi.pro/api/sync/qr/get?profile_id={whatsappProfileId}"))
            {
                www.SetRequestHeader("Authorization", wappiAuthToken);
                www.timeout = 30;
                yield return www.SendWebRequest();

                if (!WhatsappQRPanel.activeSelf) yield break;

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string response = www.downloadHandler.text;

                    // Throw-safe extract + decode (WA qr/get returns the PNG as a data URI under
                    // "qrCode"). The old slice was bounded by a literal ",\"task_id\":" token —
                    // absent from the documented response shape, so a body without it went
                    // negative-length — and Convert.FromBase64String ran UNGUARDED on the success
                    // path, throwing FormatException on any malformed payload. Either throw killed
                    // this coroutine, leaving the QR spinner up forever.
                    if (WappiStatusParser.TryGetQrPng(response, "qrCode", out byte[] imageBytes))
                    {
                        Texture2D texture = new(2, 2);

                        if (texture.LoadImage(imageBytes))
                            WhatsappQRCodeImage.texture = texture;

                        WhatsappQRStatusText.SetActive(false);
                        ForceRebuildLayout(WhatsappAuth);

                        if (_whatsappStatusCoroutine != null) StopCoroutine(_whatsappStatusCoroutine);
                        _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
                        yield break;
                    }
                }
                else if (www.result == UnityWebRequest.Result.ConnectionError)
                {
                    lastError = "Check internet connection.";
                }
                else if (www.result == UnityWebRequest.Result.ProtocolError && www.downloadHandler != null)
                {
                    string errResp = www.downloadHandler.text;
                    // Bounds-checked, never-throws detail read (the helper this file already uses
                    // for the tapi auth flow). The old slice required a literal ",\"status\":" to
                    // FOLLOW the value, so a body bounded differently — or with the keys reordered —
                    // computed a negative length and threw ON AN ERROR PATH, turning a readable
                    // message into a dead coroutine. Not found ⇒ keep the existing fallback text.
                    string errDetail = TelegramAuthResponseParser.ExtractDetail(errResp);
                    if (!string.IsNullOrEmpty(errDetail)) lastError = errDetail + ".";
                }
            }

            if (attempt < 4)
                yield return new WaitForSeconds(3f);
        }

        WhatsappQRStatusText.GetComponent<TextMeshProUGUI>().text = lastError;
        WhatsappQRStatusText.SetActive(true);
        ForceRebuildLayout(WhatsappAuth);
    }
    public void CloseWhatsappQRPanel()
    {
        WhatsappQRPanel.SetActive(false);

        WhatsappQRCodeImage.texture = null;

        WhatsappQRStatusText.SetActive(false);
        WhatsappQRStatusText.GetComponent<TextMeshProUGUI>().text = "Сервер недоступен.\nПопробуйте позже";
    }

    public void OpenWhatsappCodePanel()
    {
        WhatsappCodePanel.SetActive(true);

        WhatsappNumberInput.caretPosition = WhatsappNumberInput.text.Length;
        WhatsappNumberInput.ActivateInputField();

        if (WhatsappNumberInput.text.Length >= 10 && !WhatsappCodeTimer.activeSelf)
        {
            GetWhatsappCodeButton.interactable = true;
        }
    }

    public void WhatsappNumberInputChanged(string newText)
    {
        if (string.IsNullOrEmpty(newText) || newText.Length < 10 || WhatsappCodeTimer.activeSelf)
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
        LoadingPanel.SetActive(true);
        GetWhatsappCodeButton.interactable = false;

        string originalWaBtnText = GetWhatsappCodeButton.GetComponentInChildren<TextMeshProUGUI>().text;
        SetButtonText(GetWhatsappCodeButton, "Getting..");

        // A profile that already issued a code is inside WhatsApp's ~2min
        // repeat-code cooldown. Instead of making the user wait it out, swap
        // in a fresh profile behind the loader and request the code on it.
        if (_whatsappCodeIssued || whatsappProfileId.Equals("-1"))
        {
            if (_whatsappStatusCoroutine != null) { StopCoroutine(_whatsappStatusCoroutine); _whatsappStatusCoroutine = null; }

            bool alreadyAuthorized = false;
            if (!whatsappProfileId.Equals("-1"))
                yield return StartCoroutine(CheckWhatsappAuthorized(ok => alreadyAuthorized = ok));

            if (alreadyAuthorized)
            {
                // The previous code actually worked — keep the profile and let
                // the status poll flip the success UI momentarily.
                _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
                SetButtonText(GetWhatsappCodeButton, originalWaBtnText);
                LoadingPanel.SetActive(false);
                yield break;
            }

            yield return StartCoroutine(RecreateWhatsappProfileForNewCode());

            if (whatsappProfileId.Equals("-1"))
            {
                yield return StartCoroutine(FlashWhatsappCodeError("Server Unavailable", originalWaBtnText));
                yield break;
            }
        }

        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/auth/code?profile_id={whatsappProfileId}&phone=7{WhatsappNumberInput.text}");

        www.SetRequestHeader("Authorization", wappiAuthToken);

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
                // Bounds-checked, never-throws detail read — see the QR twin above. Bound-agnostic,
                // so it no longer matters whether the body follows the value with "uuid" or "status".
                string errDetail = TelegramAuthResponseParser.ExtractDetail(response);
                if (!string.IsNullOrEmpty(errDetail)) errorMsg = errDetail;
            }

            yield return StartCoroutine(FlashWhatsappCodeError(errorMsg, originalWaBtnText));
        }
        else
        {
            _whatsappCodeIssued = true;

            SetButtonText(GetWhatsappCodeButton, originalWaBtnText);
            WhatsappCodeTimer.SetActive(true);

            WhatsappNumberInput.gameObject.SetActive(false);
            WhatsappCodePanel.transform.GetChild(3).gameObject.SetActive(false);
            WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(true);
            WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(true);
            SetButtonText(GetWhatsappCodeButton, "Получить другой код");
            if (ChangeWhatsappNumberButton != null) ChangeWhatsappNumberButton.gameObject.SetActive(true);

            string response = www.downloadHandler.text;

            // Same defect class as WR-03 (see 11-REVIEW-FIX.md): the old read was a hard-coded
            // Substring(startIndex, 9) assuming the canonical "XXXX-XXXX" pairing code. A shorter
            // code — or any truncated body — threw ArgumentOutOfRangeException here, killing this
            // coroutine before LoadingPanel.SetActive(false) below and leaving the user with a
            // stranded full-screen overlay AND a disabled request button. Bounded, throw-safe read;
            // an unparseable body now just leaves the code label untouched.
            if (WappiStatusParser.TryGetCode(response, out string pairingCode))
            {
                WhatsappCodePanel.transform.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = pairingCode;

                if (_whatsappStatusCoroutine != null) StopCoroutine(_whatsappStatusCoroutine);
                _whatsappStatusCoroutine = StartCoroutine(GetWhatsappProfileStatus());
            }

            ForceRebuildLayout(WhatsappAuth);
            // Snap scroll to bottom before revealing — prevents code panel jumping over QR section
            var waScrollRect = WhatsappAuth.GetComponentInChildren<ScrollRect>();
            if (waScrollRect != null) waScrollRect.normalizedPosition = Vector2.zero;
            LoadingPanel.SetActive(false);
        }
    }

    private IEnumerator FlashWhatsappCodeError(string errorMsg, string originalText)
    {
        SetButtonText(GetWhatsappCodeButton, errorMsg);
        yield return new WaitForSeconds(2f);
        SetButtonText(GetWhatsappCodeButton, originalText);

        if (WhatsappNumberInput.text.Length >= 10)
            GetWhatsappCodeButton.interactable = true;

        LoadingPanel.SetActive(false);
    }

    private IEnumerator CheckWhatsappAuthorized(System.Action<bool> callback)
    {
        using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/get/status?profile_id={whatsappProfileId}");

        www.SetRequestHeader("Authorization", wappiAuthToken);
        www.timeout = 30;

        yield return www.SendWebRequest();

        bool authorized = false;

        if (www.result == UnityWebRequest.Result.Success)
        {
            string response = www.downloadHandler.text;
            // WR-03 (4th site — length-guarded, so it never threw, but migrated anyway because
            // "cannot throw" is the wrong bar HERE): this bool is the pre-delete guard for the
            // resend-code path, whose whole job is to stop a just-authorized profile from being
            // deleted + recreated. The hand-rolled scan needed the EXACT compact
            // ",\"authorized_at\":" token, so a pretty-printed or key-reordered body silently read
            // as NOT authorized — and that false negative destroys a live pairing (the same
            // pretty-print shape that broke the tapi twin and prompted this parser). Unparseable
            // still yields false, identical to the old guard's miss behaviour.
            authorized = WappiStatusParser.TryGetAuthorized(response, out bool parsed) && parsed;
        }

        callback?.Invoke(authorized);
    }

    // Deletes the current (cooldown-poisoned) Wappi profile and provisions a
    // replacement under the same name, so a fresh pairing code can be issued
    // immediately. Leaves whatsappProfileId at "-1" if provisioning failed.
    private IEnumerator RecreateWhatsappProfileForNewCode()
    {
        if (!whatsappProfileId.Equals("-1"))
        {
            yield return StartCoroutine(DeleteWhatsappProfile(whatsappProfileId, true));
            LoadingPanel.SetActive(true);

            // Delete failed — keep the old profile; the code request will
            // surface Wappi's cooldown error exactly as before.
            if (!whatsappProfileId.Equals("-1")) yield break;
        }

        string profileName = _authFromSettings && openBot != null
            ? PlayerPrefs.GetString(openBot.name + "Name", "")
            : formBotName;
        if (string.IsNullOrEmpty(profileName)) profileName = "Bot";

        for (int attempt = 0; attempt < 3 && whatsappProfileId.Equals("-1"); attempt++)
        {
            if (attempt > 0) yield return new WaitForSeconds(2f);
            yield return StartCoroutine(CreateWhatsappProfile(profileName, true));
            LoadingPanel.SetActive(true);
        }

        if (_authFromSettings && openBot != null)
        {
            // The done-handler and workflow creation read the BOT's id, and an
            // abandoned auth must not leave PlayerPrefs pointing at the deleted
            // old profile — mirror the swap (or the failure) immediately.
            openBot.GetComponent<Bot>().whatsappProfileId = whatsappProfileId;
            PlayerPrefs.SetString(openBot.name + "WhatsappProfileId", whatsappProfileId);
            if (!whatsappProfileId.Equals("-1"))
                PendingProfileLedger.MarkWhatsappClaimed();
            PlayerPrefs.Save();
        }

        if (whatsappProfileId.Equals("-1")) yield break;

        _whatsappCodeIssued = false;

        // Re-point the QR section at the replacement profile; its built-in 3s
        // delay doubles as the provisioning wait mirrored below.
        if (_whatsappQrCoroutine != null) StopCoroutine(_whatsappQrCoroutine);
        _whatsappQrCoroutine = StartCoroutine(OpenWhatsappQRPanel());

        yield return new WaitForSeconds(3f);
    }

    public void CloseWhatsappCodePanel()
    {
        WhatsappCodePanel.SetActive(false);

        WhatsappNumberInput.gameObject.SetActive(true);
        GetWhatsappCodeButton.gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(false);
        WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(false);

        if (ChangeWhatsappNumberButton != null) ChangeWhatsappNumberButton.gameObject.SetActive(false);

        WhatsappCodePanel.transform.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = "";
        ForceRebuildLayout(WhatsappAuth);
    }

    public void ChangeWhatsappNumber()
    {
        WhatsappNumberInput.gameObject.SetActive(true);
        GetWhatsappCodeButton.gameObject.SetActive(true);
        SetButtonText(GetWhatsappCodeButton, "Получить код");
        WhatsappCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        WhatsappCodePanel.transform.GetChild(4).gameObject.SetActive(false);
        WhatsappCodePanel.transform.GetChild(5).gameObject.SetActive(false);

        if (ChangeWhatsappNumberButton != null) ChangeWhatsappNumberButton.gameObject.SetActive(false);

        WhatsappCodePanel.transform.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text = "";

        if (WhatsappNumberInput.text.Length >= 10 && !WhatsappCodeTimer.activeSelf)
            GetWhatsappCodeButton.interactable = true;

        ForceRebuildLayout(WhatsappAuth);
    }

    private IEnumerator GetWhatsappProfileStatus()
    {
        bool authorized = false;

        while (!authorized)
        {
            // Auth screen is gone (wizard cancelled / settings back) — nothing to advance
            if (!WhatsappAuth.activeInHierarchy) yield break;

            using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/api/sync/get/status?profile_id={whatsappProfileId}");

            www.SetRequestHeader("Authorization", wappiAuthToken);

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string response = www.downloadHandler.text;

                // WR-03: throw-safe parse (mirrors the Telegram twin in GetTelegramProfileStatus).
                // The old unguarded IndexOf/Substring could throw a negative-length Substring and
                // kill this POLLING coroutine outright — after which a successful QR/code auth was
                // never detected and the wizard hung indefinitely. Unparseable body ⇒ not
                // authorized ⇒ keep polling, exactly as before.
                if (WappiStatusParser.TryGetAuthorized(response, out bool isAuthorized) && isAuthorized)
                {
                    authorized = true;

                    // THE trial clock starts here (spec §3, Task 15a): the first channel this
                    // account ever gets authorized. This poller is the single confirmation point
                    // for WhatsApp — the wizard and the Bot-Settings re-auth flow both run it
                    // (OpenWhatsappAuthFromSettings only re-points the shared panel), and it is
                    // reached ONLY on a server-confirmed authorized:true. Idempotent, so every
                    // later auth is a no-op; it deliberately does NOT fire on wizard open or on
                    // bot creation, neither of which authorizes anything.
                    TrialLedger.StartIfNeeded();

                    if (WappiStatusParser.TryGetPhone(response, out string phone))
                    {
                        WhatsappNumberInput.text = phone;
                    }

                    // Show checkmark inside QR box, then navigate
                    yield return StartCoroutine(ShowAuthSuccess(WhatsappAuth, WhatsappAuthSuccessPanel));
                    whatsappAuthCompleted = true;
                }
            }

            yield return new WaitForSeconds(5f);
        }
    }

    private IEnumerator OpenTelegramQRPanel()
    {
        TelegramQRStatusText.GetComponent<TextMeshProUGUI>().text = "Загрузка...";
        TelegramQRStatusText.SetActive(true);

        // Wait for Wappi to finish provisioning the newly created profile
        yield return new WaitForSeconds(3f);
        if (!TelegramQRPanel.activeSelf) yield break;

        string lastError = "Server Unavailable.\nTry Again Later";

        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (!TelegramQRPanel.activeSelf) yield break;

            using (var www = UnityWebRequest.Get($"https://wappi.pro/tapi/sync/auth/qr?profile_id={telegramProfileId}"))
            {
                www.SetRequestHeader("Authorization", wappiAuthToken);
                www.timeout = 30;
                yield return www.SendWebRequest();

                if (!TelegramQRPanel.activeSelf) yield break;

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string response = www.downloadHandler.text;

                    // A cloud password diverts the QR flow to detail:"2fa". Handle it BEFORE
                    // the base64 branch (which would otherwise decode "2fa" into a broken texture).
                    if (TelegramAuthResponseParser.IsTwoFactor(TelegramAuthResponseParser.ExtractDetail(response)))
                    {
                        ShowTelegram2faFromQr();
                        yield break;
                    }

                    // Throw-safe extract + decode (tapi auth/qr returns RAW base64 under "detail").
                    // The 2fa divert is handled above; any other non-base64 "detail" (an error
                    // string, auth_success) now fails the decode gracefully and the retry loop
                    // continues, instead of FormatException killing this coroutine with the
                    // QR spinner still on screen.
                    if (WappiStatusParser.TryGetQrPng(response, "detail", out byte[] imageBytes))
                    {
                        Texture2D texture = new(2, 2);

                        if (texture.LoadImage(imageBytes))
                            TelegramQRCodeImage.texture = texture;

                        TelegramQRStatusText.SetActive(false);
                        ForceRebuildLayout(TelegramAuth);

                        if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
                        _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());
                        yield break;
                    }
                }
                else if (www.result == UnityWebRequest.Result.ConnectionError)
                {
                    lastError = "Check internet connection.";
                }
                else if (www.result == UnityWebRequest.Result.ProtocolError && www.downloadHandler != null)
                {
                    string errResp = www.downloadHandler.text;
                    // Bounds-checked, never-throws detail read (the helper this file already uses
                    // for the tapi auth flow). The old slice required a literal ",\"status\":" to
                    // FOLLOW the value, so a body bounded differently — or with the keys reordered —
                    // computed a negative length and threw ON AN ERROR PATH, turning a readable
                    // message into a dead coroutine. Not found ⇒ keep the existing fallback text.
                    string errDetail = TelegramAuthResponseParser.ExtractDetail(errResp);
                    if (!string.IsNullOrEmpty(errDetail)) lastError = errDetail + ".";
                }
            }

            if (attempt < 4)
                yield return new WaitForSeconds(3f);
        }

        TelegramQRStatusText.GetComponent<TextMeshProUGUI>().text = lastError;
        TelegramQRStatusText.SetActive(true);
        ForceRebuildLayout(TelegramAuth);
    }

    public void CloseTelegramQRPanel()
    {
        TelegramQRPanel.SetActive(false);

        TelegramQRCodeImage.texture = null;

        TelegramQRStatusText.SetActive(false);
        TelegramQRStatusText.GetComponent<TextMeshProUGUI>().text = "Сервер недоступен.\nПопробуйте позже";
    }

    // The auth/qr response was detail:"2fa" (a cloud password is set), so the QR path
    // cannot complete. Hide the QR and reveal the code panel straight in cloud-password
    // mode — reuses the Task-2 helpers; no new scene objects.
    private void ShowTelegram2faFromQr()
    {
        TelegramQRCodeImage.texture = null;
        TelegramQRStatusText.SetActive(false);
        TelegramQRPanel.SetActive(false);

        TelegramCodePanel.SetActive(true);
        TelegramNumberInput.gameObject.SetActive(false);
        TelegramCodePanel.transform.GetChild(3).gameObject.SetActive(false);
        GetTelegramCodeButton.gameObject.SetActive(false);
        if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.gameObject.SetActive(false);

        EnterTelegram2faMode();
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

        TelegramNumberInput.caretPosition = TelegramNumberInput.text.Length;
        TelegramNumberInput.ActivateInputField();

        if (!PlayerPrefs.GetString("TelegramCooldownFinishTime", "-1").Equals("-1"))
        {
            TelegramCodeTimer.SetActive(true);
        }

        if (TelegramNumberInput.text.Length >= 10 && !TelegramCodeTimer.activeSelf)
        {
            GetTelegramCodeButton.interactable = true;
        }
    }

    public void TelegramNumberInputChanged(string newText)
    {
        if (string.IsNullOrEmpty(newText) || newText.Length < 10 || TelegramCodeTimer.activeSelf)
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
        LoadingPanel.SetActive(true);
        GetTelegramCodeButton.interactable = false;

        string originalTgBtnText = GetTelegramCodeButton.GetComponentInChildren<TextMeshProUGUI>().text;
        SetButtonText(GetTelegramCodeButton, "Sending..");

        string jsonBody = "{\"phone\":\"7" + TelegramNumberInput.text + "\"}";

        using UnityWebRequest www = new($"https://wappi.pro/tapi/sync/auth/phone?profile_id={telegramProfileId}", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", wappiAuthToken);

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
                // Bounds-checked, never-throws detail read — see the QR twin above.
                string errDetail = TelegramAuthResponseParser.ExtractDetail(response);
                if (!string.IsNullOrEmpty(errDetail)) errorMsg = errDetail;
            }

            SetButtonText(GetTelegramCodeButton, errorMsg);
            yield return new WaitForSeconds(2f);
            SetButtonText(GetTelegramCodeButton, originalTgBtnText);

            if (TelegramNumberInput.text.Length >= 10)
                GetTelegramCodeButton.interactable = true;

            LoadingPanel.SetActive(false);
        }
        else
        {
            TelegramCodeTimer.SetActive(true);

            TelegramNumberInput.gameObject.SetActive(false);
            TelegramCodePanel.transform.GetChild(3).gameObject.SetActive(false);
            TelegramCodeInput.gameObject.SetActive(true);
            SetTelegramCodeEntryTexts(true);
            SendTelegramCodeButton.gameObject.SetActive(true);
            if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.gameObject.SetActive(true);

            // Rebuild layout and snap scroll immediately after toggling elements,
            // before any yield — prevents code panel jumping over QR section
            ForceRebuildLayout(TelegramAuth);
            var tgScrollRect = TelegramAuth.GetComponentInChildren<ScrollRect>();
            if (tgScrollRect != null) tgScrollRect.normalizedPosition = Vector2.zero;
            LoadingPanel.SetActive(false);

            // Show "Sent" confirmation briefly, then set persistent "another code" text
            string response = www.downloadHandler.text;
            // Bounds-checked status read: the old fixed Substring(startIndex, 4) threw if fewer
            // than four characters followed "status":" , and a 4-char prefix test matched any
            // status merely STARTING with "done". Compares the whole value instead.
            if (WappiStatusParser.TryGetStatus(response, out string sendStatus) && sendStatus == "done")
            {
                SetButtonText(GetTelegramCodeButton, "Sent");
                yield return new WaitForSeconds(2f);
            }
            SetButtonText(GetTelegramCodeButton, "Получить другой код");
        }
    }
    
    public void TelegramCodeInputChanged(string newText)
    {
        // In cloud-password mode the field is a Telegram password, not a numeric code —
        // any non-empty value enables submit (the min-5-digit gate is code-mode only).
        if (_telegram2faMode)
        {
            SendTelegramCodeButton.interactable = !string.IsNullOrEmpty(newText);
            return;
        }

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
        // Once the panel is in cloud-password mode this same submit button posts the
        // Telegram cloud password to auth/2fa instead of the pairing code to auth/code.
        if (_telegram2faMode)
        {
            yield return StartCoroutine(SubmitTelegram2fa());
            yield break;
        }

        LoadingPanel.SetActive(true);
        SendTelegramCodeButton.interactable = false;
        SetButtonText(SendTelegramCodeButton, "Авторизация...");

        string jsonBody = "{\"auth_code\":\"" + TelegramCodeInput.text + "\"}";

        using UnityWebRequest www = new($"https://wappi.pro/tapi/sync/auth/code?profile_id={telegramProfileId}", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", wappiAuthToken);

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
                // Bounds-checked, never-throws detail read — see the QR twin above. Bound-agnostic,
                // so it no longer matters whether the body follows the value with "uuid" or "status".
                string errDetail = TelegramAuthResponseParser.ExtractDetail(response);
                if (!string.IsNullOrEmpty(errDetail)) errorMsg = errDetail;
            }

            SetButtonText(SendTelegramCodeButton, errorMsg);
            yield return new WaitForSeconds(2f);
            SetButtonText(SendTelegramCodeButton, "Подтвердить код");

            if (TelegramCodeInput.text.Length >= 5)
                SendTelegramCodeButton.interactable = true;
        }
        else
        {
            string detail = TelegramAuthResponseParser.ExtractDetail(www.downloadHandler.text);

            if (TelegramAuthResponseParser.IsAuthSuccess(detail))
            {
                SetButtonText(SendTelegramCodeButton, "Авторизация завершена");

                if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
                _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());

                yield return new WaitForSeconds(2f);
                SetButtonText(SendTelegramCodeButton, "Подтвердить код");
            }
            else if (TelegramAuthResponseParser.IsTwoFactor(detail))
            {
                // Account has a cloud password: switch the panel into password mode
                // instead of failing. The next submit posts auth/2fa.
                EnterTelegram2faMode();
                LoadingPanel.SetActive(false);
                yield break;
            }
            else
            {
                // Any other / malformed detail -> re-prompt (fail-closed).
                SetButtonText(SendTelegramCodeButton, "Неверный код");
                yield return new WaitForSeconds(2f);
                SetButtonText(SendTelegramCodeButton, "Подтвердить код");
                if (TelegramCodeInput.text.Length >= 5)
                    SendTelegramCodeButton.interactable = true;
            }
        }

        LoadingPanel.SetActive(false);
    }
    // Cloud-password (2FA) copy on the shared title/body labels of the code panel.
    private void SetTelegram2faTexts()
    {
        if (TelegramPhoneTitle != null)
            TelegramPhoneTitle.text = "Облачный пароль";
        if (TelegramPhoneBody != null)
            TelegramPhoneBody.text = "Введите пароль от Telegram";
    }

    // Repurpose the already-visible code-entry input as the Telegram cloud-password field.
    // No new scene objects — reuses TelegramCodeInput + SendTelegramCodeButton.
    private void EnterTelegram2faMode()
    {
        // Already in password mode: a late QR-poll "2fa" response must not re-enter and
        // wipe a half-typed cloud password (IN-05). Texts/input are already set up.
        if (_telegram2faMode) return;

        _telegram2faMode = true;
        SetTelegram2faTexts();

        TelegramCodeInput.gameObject.SetActive(true);
        TelegramCodeInput.text = "";
        SendTelegramCodeButton.gameObject.SetActive(true);
        // Enabled once a non-empty password is typed (relaxed gate, see TelegramCodeInputChanged).
        SendTelegramCodeButton.interactable = false;
        SetButtonText(SendTelegramCodeButton, "Подтвердить пароль");

        ForceRebuildLayout(TelegramAuth);
        TelegramCodeInput.ActivateInputField();
    }

    // Clear the 2FA repurposing so the panel returns to numeric-code behavior next time.
    private void ResetTelegram2faMode()
    {
        _telegram2faMode = false;
        if (TelegramCodeInput != null) TelegramCodeInput.text = "";
    }

    // POST the Telegram cloud password to tapi/sync/auth/2fa. SECURITY: the password is
    // sent ONLY here over HTTPS, is never logged and never persisted, and the input field
    // is cleared immediately after the request completes (success or failure).
    private IEnumerator SubmitTelegram2fa()
    {
        LoadingPanel.SetActive(true);
        SendTelegramCodeButton.interactable = false;
        SetButtonText(SendTelegramCodeButton, "Авторизация...");

        // JsonConvert escapes quotes/backslashes — a cloud password is free-form text,
        // so manual concatenation would produce malformed JSON and hard-fail auth (WR-02).
        string jsonBody = JsonConvert.SerializeObject(new { pwd_code = TelegramCodeInput.text });

        using UnityWebRequest www = new($"https://wappi.pro/tapi/sync/auth/2fa?profile_id={telegramProfileId}", "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        www.uploadHandler = new UploadHandlerRaw(bodyRaw);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");
        www.SetRequestHeader("Authorization", wappiAuthToken);
        www.timeout = 30;

        yield return www.SendWebRequest();

        // SECURITY: never log the password / jsonBody. Clear the field on every path.
        TelegramCodeInput.text = "";

        if (www.result != UnityWebRequest.Result.Success)
        {
            string errorMsg = "Сервер недоступен";
            if (www.result == UnityWebRequest.Result.ConnectionError)
            {
                errorMsg = "Проверьте подключение";
            }
            else if (www.result == UnityWebRequest.Result.ProtocolError && www.downloadHandler != null)
            {
                string errDetail = TelegramAuthResponseParser.ExtractDetail(www.downloadHandler.text);
                if (!string.IsNullOrEmpty(errDetail)) errorMsg = errDetail;
            }

            SetButtonText(SendTelegramCodeButton, errorMsg);
            yield return new WaitForSeconds(2f);
            SetButtonText(SendTelegramCodeButton, "Подтвердить пароль");
            LoadingPanel.SetActive(false);
            yield break;
        }

        string detail = TelegramAuthResponseParser.ExtractDetail(www.downloadHandler.text);
        if (TelegramAuthResponseParser.IsAuthSuccess(detail))
        {
            SetButtonText(SendTelegramCodeButton, "Авторизация завершена");

            if (_telegramStatusCoroutine != null) StopCoroutine(_telegramStatusCoroutine);
            _telegramStatusCoroutine = StartCoroutine(GetTelegramProfileStatus());

            yield return new WaitForSeconds(2f);
        }
        else
        {
            // Wrong password / any non-success -> re-prompt (fail-closed).
            SetButtonText(SendTelegramCodeButton, "Неверный пароль");
            yield return new WaitForSeconds(2f);
            SetButtonText(SendTelegramCodeButton, "Подтвердить пароль");
        }

        LoadingPanel.SetActive(false);
    }

    public void CloseTelegramCodePanel()
    {
        TelegramCodePanel.SetActive(false);

        TelegramNumberInput.gameObject.SetActive(true);
        GetTelegramCodeButton.gameObject.SetActive(true);
        TelegramCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        TelegramCodeInput.gameObject.SetActive(false);
        SendTelegramCodeButton.gameObject.SetActive(false);
        SetTelegramCodeEntryTexts(false);

        if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.gameObject.SetActive(false);

        ResetTelegram2faMode();
        ForceRebuildLayout(TelegramAuth);
    }

    public void ChangeTelegramNumber()
    {
        TelegramNumberInput.gameObject.SetActive(true);
        GetTelegramCodeButton.gameObject.SetActive(true);
        SetButtonText(GetTelegramCodeButton, "Получить код");
        TelegramCodePanel.transform.GetChild(3).gameObject.SetActive(true);
        TelegramCodeInput.gameObject.SetActive(false);
        SendTelegramCodeButton.gameObject.SetActive(false);
        SetTelegramCodeEntryTexts(false);

        if (ChangeTelegramNumberButton != null) ChangeTelegramNumberButton.gameObject.SetActive(false);

        ResetTelegram2faMode();

        if (TelegramNumberInput.text.Length >= 10 && !TelegramCodeTimer.activeSelf)
            GetTelegramCodeButton.interactable = true;

        ForceRebuildLayout(TelegramAuth);
    }

    private IEnumerator GetTelegramProfileStatus()
    {
        bool authorized = false;

        while (!authorized)
        {
            // Auth screen is gone (wizard cancelled / settings back) — nothing to advance
            if (!TelegramAuth.activeInHierarchy) yield break;

            using UnityWebRequest www = UnityWebRequest.Get($"https://wappi.pro/tapi/sync/get/status?profile_id={telegramProfileId}");

            www.SetRequestHeader("Authorization", wappiAuthToken);

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string response = www.downloadHandler.text;

                // tapi get/status is pretty-printed with two "phone" keys — parse via the
                // whitespace/order-agnostic WappiStatusParser instead of substring scanning.
                if (WappiStatusParser.TryGetAuthorized(response, out bool isAuthorized) && isAuthorized)
                {
                    authorized = true;

                    // Telegram twin of the WhatsApp clock start (Task 15a) — see
                    // GetWhatsappProfileStatus. Every Telegram success path funnels here: QR,
                    // phone-code (SendTelegramCode) and the 2FA cloud-password step both restart
                    // this poller on auth_success rather than declaring success themselves.
                    TrialLedger.StartIfNeeded();

                    if (WappiStatusParser.TryGetPhone(response, out string phone))
                        TelegramNumberInput.text = phone;

                    // Show checkmark inside QR box, then navigate
                    yield return StartCoroutine(ShowAuthSuccess(TelegramAuth, TelegramAuthSuccessPanel));
                    telegramAuthCompleted = true;
                }
            }

            yield return new WaitForSeconds(5f);
        }
    }

    //////////////////////////////////////////////////////////SENT FROM OTHER CLASSES//////////////////////////////////////////////////////////

    public void GetCreateWhatsappProfile(string whatsappProfileId)
    {
        StartCoroutine(CreateWhatsappProfile(whatsappProfileId, false));
    }

    public void GetCreateTelegramProfile(string telegramProfileId)
    {
        StartCoroutine(CreateTelegramProfile(telegramProfileId, false));
    }

    public void GetDeleteWhatsappProfile(string whatsappProfileId)
    {
        StartCoroutine(DeleteWhatsappProfile(whatsappProfileId, false));
    }

    public void GetDeleteTelegramProfile(string telegramProfileId)
    {
        StartCoroutine(DeleteTelegramProfile(telegramProfileId, false));
    }


    public void GetCreateWhatsappWorkflow()
    {
        StartCoroutine(CreateWhatsappWorkflowFromEdit());
    }

    public void GetCreateTelegramWorkflow()
    {
        StartCoroutine(CreateTelegramWorkflowFromEdit());
    }

    public void GetEnableWhatsappWorkflow(string whatsappWorkflowId, bool enabled)
    {
        StartCoroutine(EnableWhatsappWorkflow(whatsappWorkflowId, enabled));
    }

    public void GetEnableTelegramWorkflow(string telegramWorkflowId, bool enabled)
    {
        StartCoroutine(EnableTelegramWorkflow(telegramWorkflowId, enabled));
    }

    public void GetDeleteWhatsappWorkflow(string whatsappWorkflowId)
    {
        StartCoroutine(DeleteWhatsappWorkflow(whatsappWorkflowId, false));
    }

    public void GetDeleteTelegramWorkflow(string telegramWorkflowId)
    {
        StartCoroutine(DeleteTelegramWorkflow(telegramWorkflowId, false));
    }


    public void GetSaveSettings(string whatsappWorkflowId, string telegramWorkflowId)
    {
        // Snapshot the persisted channel state BEFORE SaveSettings overwrites
        // it. SaveWorkflows decides whether a channel's n8n workflow has to be
        // (de)activated by comparing against this pre-save value, and its
        // Telegram branch only reaches that decision after a network
        // round-trip — reading PlayerPrefs there would read back what
        // SaveSettings had already written and never fire the activation.
        // Default 1 matches every other reader of these keys (CloseSettings,
        // LoadBots, the dirty check): a missing key means the channel reads as
        // ON, so a still-ON toggle is "unchanged" and no workflow is activated
        // behind the user's back.
        bool previousWhatsappOn = PlayerPrefs.GetInt(openBot.name + "isOnWhatsapp", 1) == 1;
        bool previousTelegramOn = PlayerPrefs.GetInt(openBot.name + "isOnTelegram", 1) == 1;

        StartCoroutine(SaveWorkflows(whatsappWorkflowId, telegramWorkflowId,
                                     previousWhatsappOn, previousTelegramOn));

        SaveSettings();
    }

    public void DeleteProfilesAndWorkflows(string whatsappProfileId, string telegramProfileId, string whatsappWorkflowId, string telegramWorkflowId)
    {
        StartCoroutine(DeleteWhatsappProfile(whatsappProfileId, true));
        StartCoroutine(DeleteTelegramProfile(telegramProfileId, true));

        StartCoroutine(DeleteWhatsappWorkflow(whatsappWorkflowId, true));
        StartCoroutine(DeleteTelegramWorkflow(telegramWorkflowId, true));
    }

    // Server-side sweep of a deleted bot's price-list knowledge: the n8n
    // DeleteBotFiles webhook removes every RAG chunk tagged with the bot's
    // workflow ids (including legacy chunks with no fileId) plus each file's
    // stored original in the price-lists bucket. Runs on Manager because the
    // calling Bot destroys itself — a coroutine on the Bot would die with it.
    public void DeleteBotFilesOnServer(string whatsappWorkflowId, string telegramWorkflowId)
    {
        bool noWhatsapp = string.IsNullOrEmpty(whatsappWorkflowId) || whatsappWorkflowId == Bot.UnauthedProfileSentinel;
        bool noTelegram = string.IsNullOrEmpty(telegramWorkflowId) || telegramWorkflowId == Bot.UnauthedProfileSentinel;
        if (noWhatsapp && noTelegram) return; // never-authed bot — nothing is tagged server-side

        StartCoroutine(DeleteBotFilesRoutine(
            string.IsNullOrEmpty(whatsappWorkflowId) ? Bot.UnauthedProfileSentinel : whatsappWorkflowId,
            string.IsNullOrEmpty(telegramWorkflowId) ? Bot.UnauthedProfileSentinel : telegramWorkflowId));
    }

    private IEnumerator DeleteBotFilesRoutine(string botWaId, string botTgId)
    {
        string url = $"{n8nBaseUrl}/webhook/DeleteBotFiles";
        string body = JsonConvert.SerializeObject(new { botWaId, botTgId });

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 30;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
            Debug.LogError($"[DeleteBotFiles] [{request.responseCode}] {url}: {request.error}\n{request.downloadHandler?.text}");
    }


    //////////////////////////////////////////////////////////SEND PROFILE REQUESTS//////////////////////////////////////////////////////////

    private IEnumerator CreateWhatsappProfile(string name, bool localId)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        using UnityWebRequest www = UnityWebRequest.Post($"https://wappi.pro/api/profile/add?name={name}", form);

        www.SetRequestHeader("Authorization", wappiAuthToken);
        www.timeout = 30;

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            // WR-04: was a silent dead-end — profileId stays "-1" and the wizard stalls with no trace.
            Debug.LogError($"[CreateWhatsappProfile] [{www.responseCode}] {www.url}: {www.error}");
        }
        else
        {
            string response = www.downloadHandler.text;

            // Throw-safe, order/whitespace-agnostic read of the profile/add response. The old
            // "+14 up to \",\"status\":" scan went negative-length (throw, hanging this nested
            // coroutine and its awaiting parent with the LoadingPanel up) if the server ever
            // emitted status first, and its hard-coded offset would store an id with a leading
            // quote from a pretty-printed body — corrupting every later Wappi call for this bot.
            if (WappiStatusParser.TryGetProfileId(response, out string createdWhatsappProfileId))
            {

                if (localId)
                {
                    whatsappProfileId = createdWhatsappProfileId;
                }
                else
                {
                    openBot.GetComponent<Bot>().whatsappProfileId = createdWhatsappProfileId;
                }

                PendingProfileLedger.MarkWhatsappPending(createdWhatsappProfileId);
            }
        }

        LoadingPanel.SetActive(false);
    }

    private IEnumerator DeleteWhatsappProfile(string whatsappProfileId, bool localId)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        using UnityWebRequest www = UnityWebRequest.Post($"https://wappi.pro/api/profile/delete?profile_id={whatsappProfileId}", form);

        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            if (localId)
            {
                this.whatsappProfileId = "-1";
            }
            else
            {
                openBot.GetComponent<Bot>().whatsappProfileId = "-1";
                PlayerPrefs.SetString(openBot.name + "WhatsappProfileId", "-1");
            }

            PendingProfileLedger.ClearWhatsappIfMatches(whatsappProfileId);
        }

        LoadingPanel.SetActive(false);
    }


    private IEnumerator CreateTelegramProfile(string name, bool localId)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        using UnityWebRequest www = UnityWebRequest.Post($"https://wappi.pro/tapi/profile/add?name={name}", form);

        www.SetRequestHeader("Authorization", wappiAuthToken);
        www.timeout = 30;

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            // WR-04: was a silent dead-end — profileId stays "-1" and the wizard stalls with no trace.
            Debug.LogError($"[CreateTelegramProfile] [{www.responseCode}] {www.url}: {www.error}");
        }
        else
        {
            string response = www.downloadHandler.text;

            // Throw-safe read — see the WhatsApp twin in CreateWhatsappProfile.
            if (WappiStatusParser.TryGetProfileId(response, out string createdTelegramProfileId))
            {

                if (localId)
                {
                    telegramProfileId = createdTelegramProfileId;
                }
                else
                {
                    openBot.GetComponent<Bot>().telegramProfileId = createdTelegramProfileId;
                }

                PendingProfileLedger.MarkTelegramPending(createdTelegramProfileId);
            }
        }

        LoadingPanel.SetActive(false);
    }

    private IEnumerator DeleteTelegramProfile(string telegramProfileId, bool localId)
    {
        LoadingPanel.SetActive(true);

        WWWForm form = new();

        using UnityWebRequest www = UnityWebRequest.Post($"https://wappi.pro/tapi/profile/delete?profile_id={telegramProfileId}", form);

        www.SetRequestHeader("Authorization", wappiAuthToken);

        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            if (localId)
            {
                this.telegramProfileId = "-1";
            }
            else
            {
                openBot.GetComponent<Bot>().telegramProfileId = "-1";
                PlayerPrefs.SetString(openBot.name + "TelegramProfileId", "-1");
            }

            PendingProfileLedger.ClearTelegramIfMatches(telegramProfileId);
        }

        LoadingPanel.SetActive(false);
    }


    //////////////////////////////////////////////////////////SEND WORKFLOW REQUESTS//////////////////////////////////////////////////////////

    private IEnumerator CreateWhatsappWorkflowFromStart(GameObject bot)
    {
        LoadingPanel.SetActive(true);
        
        WWWForm form = new();
        
        form.AddField("Name", bot.GetComponent<Bot>().BotName != null ? bot.GetComponent<Bot>().BotName.text : "");
        form.AddField("BusinessType", businessTypes.TryGetById(selectedBusinessId, out var bt1) ? bt1.displayName : "");
        form.AddField("BusinessTypeId", selectedBusinessId ?? "");
        form.AddField("WhatsappProfileId", whatsappProfileId);
        string tgId = bot.GetComponent<Bot>().telegramWorkflowId;
        form.AddField("TelegramWorkflowId", string.IsNullOrEmpty(tgId) ? Bot.UnauthedProfileSentinel : tgId);

        form.AddField("Business", "");
        form.AddField("Prompt", "");
        form.AddField("ProductsList", "");
        form.AddField("ServicesList", "");
        form.AddField("AppUserID", BillingIdentity.AppUserId);
        // BotKey = this bot's stable slot name (already assigned by the time this
        // coroutine runs — Manager.cs sets newBot.name before starting it). Lets the
        // server-side slot backstop retire the SAME bot's old channel row on a
        // re-register (e.g. change-number) instead of counting it as a second slot.
        form.AddField("BotKey", bot != null ? bot.name : "");

        using UnityWebRequest www = UnityWebRequest.Post($"{n8nBaseUrl}/webhook/CreateWhatsappWorkflow", form);
        // Without a timeout a connected-but-silent n8n parks this coroutine forever:
        // neither branch below runs, so the card's controls stay disabled and the
        // LoadingPanel never lifts. 30s matches every other request in this file.
        www.timeout = 30;
        yield return www.SendWebRequest();

        // The user can reach the card while the sibling channel's request is still in
        // flight (the branches below drop the shared LoadingPanel), and deleting the
        // bot destroys this GameObject — so never dereference it unguarded.
        var startedBot = bot != null ? bot.GetComponent<Bot>() : null;
        if (startedBot == null)
        {
            LoadingPanel.SetActive(false);
            yield break;
        }

        // A channel_limit rejection is a real HTTP 200 with no "id" (the billing
        // slot backstop's contract — see Task 8) — so success can no longer be judged
        // from www.result alone; the body must be inspected too.
        string response = www.result == UnityWebRequest.Result.Success ? www.downloadHandler.text : null;
        bool serverRejected = response != null && response.Contains("\"error\"");

        if (www.result != UnityWebRequest.Result.Success || serverRejected)
        {
            if (serverRejected)
                Debug.LogWarning($"CreateWhatsappWorkflow rejected: {response}");

            // The card SURVIVES this failure (DeleteWhatsappProfile only resets the
            // local id to the unauthed sentinel), so hand its controls back — the
            // creation path disabled both, and only the success branch below used
            // to re-enable them, stranding a bot that could be neither opened nor
            // switched between «Авто»/«Вместе» until the app was relaunched.
            startedBot.EditButton.interactable = true;
            startedBot.SetActivationInteractable(true);

            // …and forget the profile we are about to delete: the card would keep
            // painting a connected WhatsApp glyph off a dangling id, and a capsule
            // tap would write '*' suppression rows keyed to a profile that no
            // longer exists (AuthedProfileIds only rejects null/empty/"-1").
            startedBot.whatsappProfileId = Bot.UnauthedProfileSentinel;
            startedBot.RefreshCardSubline();

            StartCoroutine(DeleteWhatsappProfile(whatsappProfileId, true));
        }
        else
        {
            startedBot.active = true;
            PlayerPrefs.SetInt(bot.name + "Active", 1);
            startedBot.Status.text = "Active";
            startedBot.Status.color = new(0, 1, 0);

            startedBot.EditButton.interactable = true;
            startedBot.SetActivationInteractable(true);

            if (response.Contains("\"id\":"))
            {

                bot.GetComponent<Bot>().whatsappWorkflowId = ExtractWorkflowId(response);
                PlayerPrefs.SetString(bot.name + "WhatsappWorkflowId", bot.GetComponent<Bot>().whatsappWorkflowId);
                PlayerPrefs.SetString(bot.name + "WhatsappProfileId", bot.GetComponent<Bot>().whatsappProfileId);
                PendingProfileLedger.MarkWhatsappClaimed();
                SeedReplyModeDefaultForProfile(bot.name, bot.GetComponent<Bot>().whatsappProfileId);   // WR-02: '*' row for a channel authed after a Вместе default
            }
        }

        whatsappProfileId = "-1";

        LoadingPanel.SetActive(false);
        yield break; //to be deleted
    }

    private IEnumerator CreateWhatsappWorkflowFromEdit()
    {
        LoadingPanel.SetActive(true);

        _saveHadError = false;
        openBotSettings.Saved.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = SavingText;
        openBotSettings.Saved.SetActive(true);


        string productsList = "";

        for (int i = 0; i < openBotSettings.ProductsParent.transform.childCount; i++)
        {
            ProductCardView product = openBotSettings.ProductsParent.transform.GetChild(i).GetComponent<ProductCardView>();

            product.Name.Trim();
            if (!product.Name.Equals(""))
            {
                productsList += $"Product{i + 1}: {product.Name}" +
                    $"\nProduct{i + 1} Price: {product.Price}" +
                    $"\nProduct{i + 1} Description: {product.Description}"
                    + (i == openBotSettings.ProductsParent.transform.childCount - 1 ? "" : $"\n\n");
            }
        }


        string servicesList = "";

        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount; i++)
        {
            ServiceCardView service = openBotSettings.ServicesParent.transform.GetChild(i).GetComponent<ServiceCardView>();

            service.Name.Trim();
            if (!service.Name.Equals(""))
            {
                servicesList += $"Service{i + 1}: {service.Name}" +
                    $"\nService{i + 1} Price: {service.Price}" +
                    $"\nService{i + 1} Description: {service.Description}"
                    + (i == openBotSettings.ServicesParent.transform.childCount - 1 ? "" : $"\n\n");
            }
        }


        WWWForm form = new();

        form.AddField("Name", openBotSettings.BotNameField.Value);
        AddBusinessTypeFields(form);
        form.AddField("WhatsappProfileId", openBot.GetComponent<Bot>().whatsappProfileId);
        string tgId = openBot.GetComponent<Bot>().telegramWorkflowId;
        form.AddField("TelegramWorkflowId", string.IsNullOrEmpty(tgId) ? Bot.UnauthedProfileSentinel : tgId);

        form.AddField("Business", ComposeBusinessKnowledge(openBotSettings));
        form.AddField("Prompt", openBotSettings.PromptField.Value);
        form.AddField("ProductsList", "Products:\n" + productsList);
        form.AddField("ServicesList", "Services:\n" + servicesList);
        form.AddField("AppUserID", BillingIdentity.AppUserId);
        // BotKey = this (existing) bot's stable slot name — lets a re-auth/change-number
        // save on the SAME bot retire its own old channel row server-side.
        form.AddField("BotKey", openBot != null ? openBot.name : "");

        using UnityWebRequest www = UnityWebRequest.Post($"{n8nBaseUrl}/webhook/CreateWhatsappWorkflow", form);
        yield return www.SendWebRequest();

        bool resolved = false;
        if (www.result == UnityWebRequest.Result.Success)
        {
            string response = www.downloadHandler.text;

            if (response.Contains("\"id\":"))
            {

                openBot.GetComponent<Bot>().whatsappWorkflowId = ExtractWorkflowId(response);
                PlayerPrefs.SetString(openBot.name + "WhatsappWorkflowId", openBot.GetComponent<Bot>().whatsappWorkflowId);
                PendingProfileLedger.MarkWhatsappClaimed();
                SeedReplyModeDefaultForProfile(openBot.name, openBot.GetComponent<Bot>().whatsappProfileId);   // WR-02: '*' row for a channel authed after a Вместе default

                // The CreateWhatsappWorkflow webhook ACTIVATES the new workflow before it
                // responds. Honor the channel toggle — the only activation gate since the
                // capsule↔ReplyMode unification (a Вместе default is enforced separately:
                // SeedReplyModeDefaultForProfile above writes the '*' suppression row).
                if (PlayerPrefs.GetInt(openBot.name + "isOnWhatsapp", 1) != 1)
                {
                    StartCoroutine(SetWorkflowActiveRoutine(openBot.GetComponent<Bot>().whatsappWorkflowId, false));
                }

                CreateWhatsappWorkflowFromEditSuccess = true;
                resolved = true;
                Saved();
            }
        }

        // Failure or a 200 without an "id" left the "Saving.." pill stranded
        // forever (no else branch). Always settle it — show the error briefly,
        // then hide — so the pill can never hang.
        if (!resolved)
        {
            Debug.LogError($"[{www.responseCode}] CreateWhatsappWorkflow failed or returned no id: {www.error}");
            FailSavePanel();
        }

        LoadingPanel.SetActive(false);
    }

    private IEnumerator CreateTelegramWorkflowFromStart(GameObject bot)
    {
        LoadingPanel.SetActive(true);
        
        WWWForm form = new();
        
        form.AddField("Name", bot.GetComponent<Bot>().BotName != null ? bot.GetComponent<Bot>().BotName.text : "");
        form.AddField("BusinessType", businessTypes.TryGetById(selectedBusinessId, out var bt2) ? bt2.displayName : "");
        form.AddField("BusinessTypeId", selectedBusinessId ?? "");
        form.AddField("TelegramProfileId", telegramProfileId);
        string waId = bot.GetComponent<Bot>().whatsappWorkflowId;
        form.AddField("WhatsappWorkflowId", string.IsNullOrEmpty(waId) ? Bot.UnauthedProfileSentinel : waId);

        form.AddField("Business", "");
        form.AddField("Prompt", "");
        form.AddField("ProductsList", "");
        form.AddField("ServicesList", "");
        form.AddField("AppUserID", BillingIdentity.AppUserId);
        // See the WhatsApp twin: BotKey = this bot's already-assigned stable slot name.
        form.AddField("BotKey", bot != null ? bot.name : "");

        using UnityWebRequest www = UnityWebRequest.Post($"{n8nBaseUrl}/webhook/CreateTelegramWorkflow", form);
        // See the WhatsApp twin: no timeout means a silent n8n parks this coroutine
        // forever and neither branch below ever runs.
        www.timeout = 30;
        yield return www.SendWebRequest();

        // The card may have been deleted while the sibling channel was still in
        // flight — never dereference it unguarded.
        var startedBot = bot != null ? bot.GetComponent<Bot>() : null;
        if (startedBot == null)
        {
            LoadingPanel.SetActive(false);
            yield break;
        }

        // NOTE: this check was left inverted (debug flip, correct line commented
        // beside it) — on success it deleted the just-authorized profile and on
        // failure marked the bot active. Restored to mirror the WhatsApp twin.
        // Also see the WhatsApp twin: a channel_limit rejection is a real HTTP 200
        // with no "id" (Task 8 billing backstop), so the body must be inspected too.
        string response = www.result == UnityWebRequest.Result.Success ? www.downloadHandler.text : null;
        bool serverRejected = response != null && response.Contains("\"error\"");

        if (www.result != UnityWebRequest.Result.Success || serverRejected)
        {
            if (serverRejected)
                Debug.LogWarning($"CreateTelegramWorkflow rejected: {response}");

            // Mirror of the WhatsApp twin: the card survives, so give its Edit
            // button and «Авто» capsule back instead of stranding them disabled,
            // and drop the id of the profile being deleted so neither the subline
            // icons nor a capsule tap can act on it.
            startedBot.EditButton.interactable = true;
            startedBot.SetActivationInteractable(true);
            startedBot.telegramProfileId = Bot.UnauthedProfileSentinel;
            startedBot.RefreshCardSubline();

            StartCoroutine(DeleteTelegramProfile(telegramProfileId, true));
        }
        else
        {
            startedBot.active = true;
            PlayerPrefs.SetInt(bot.name + "Active", 1);
            startedBot.Status.text = "Active";
            startedBot.Status.color = new(0, 1, 0);

            startedBot.EditButton.interactable = true;
            startedBot.SetActivationInteractable(true);

            if (response.Contains("\"id\":"))
            {

                bot.GetComponent<Bot>().telegramWorkflowId = ExtractWorkflowId(response);
                PlayerPrefs.SetString(bot.name + "TelegramWorkflowId", bot.GetComponent<Bot>().telegramWorkflowId);
                PlayerPrefs.SetString(bot.name + "TelegramProfileId", bot.GetComponent<Bot>().telegramProfileId);
                PendingProfileLedger.MarkTelegramClaimed();
                SeedReplyModeDefaultForProfile(bot.name, bot.GetComponent<Bot>().telegramProfileId);   // WR-02: '*' row for a channel authed after a Вместе default
            }
        }

        telegramProfileId = "-1";

        LoadingPanel.SetActive(false);
        yield break; //to be deleted
    }

    private IEnumerator CreateTelegramWorkflowFromEdit()
    {
        LoadingPanel.SetActive(true);

        _saveHadError = false;
        openBotSettings.Saved.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = SavingText;
        openBotSettings.Saved.SetActive(true);


        string productsList = "";

        for (int i = 0; i < openBotSettings.ProductsParent.transform.childCount; i++)
        {
            ProductCardView product = openBotSettings.ProductsParent.transform.GetChild(i).GetComponent<ProductCardView>();

            product.Name.Trim();
            if (!product.Name.Equals(""))
            {
                productsList += $"Product{i + 1}: {product.Name}" +
                    $"\nProduct{i + 1} Price: {product.Price}" +
                    $"\nProduct{i + 1} Description: {product.Description}"
                    + (i == openBotSettings.ProductsParent.transform.childCount - 1 ? "" : $"\n\n");
            }
        }


        string servicesList = "";

        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount; i++)
        {
            ServiceCardView service = openBotSettings.ServicesParent.transform.GetChild(i).GetComponent<ServiceCardView>();

            service.Name.Trim();
            if (!service.Name.Equals(""))
            {
                servicesList += $"Service{i + 1}: {service.Name}" +
                    $"\nService{i + 1} Price: {service.Price}" +
                    $"\nService{i + 1} Description: {service.Description}"
                    + (i == openBotSettings.ServicesParent.transform.childCount - 1 ? "" : $"\n\n");
            }
        }


        WWWForm form = new();

        form.AddField("Name", openBotSettings.BotNameField.Value);
        AddBusinessTypeFields(form);
        form.AddField("TelegramProfileId", openBot.GetComponent<Bot>().telegramProfileId);
        string waId = openBot.GetComponent<Bot>().whatsappWorkflowId;
        form.AddField("WhatsappWorkflowId", string.IsNullOrEmpty(waId) ? Bot.UnauthedProfileSentinel : waId);

        form.AddField("Business", ComposeBusinessKnowledge(openBotSettings));
        form.AddField("Prompt", openBotSettings.PromptField.Value);
        form.AddField("ProductsList", "Products:\n" + productsList);
        form.AddField("ServicesList", "Services:\n" + servicesList);
        form.AddField("AppUserID", BillingIdentity.AppUserId);
        // See the WhatsApp twin: BotKey = this (existing) bot's stable slot name.
        form.AddField("BotKey", openBot != null ? openBot.name : "");

        using UnityWebRequest www = UnityWebRequest.Post($"{n8nBaseUrl}/webhook/CreateTelegramWorkflow", form);
        yield return www.SendWebRequest();

        bool resolved = false;
        if (www.result == UnityWebRequest.Result.Success)
        {
            string response = www.downloadHandler.text;

            if (response.Contains("\"id\":"))
            {

                openBot.GetComponent<Bot>().telegramWorkflowId = ExtractWorkflowId(response);
                PlayerPrefs.SetString(openBot.name + "TelegramWorkflowId", openBot.GetComponent<Bot>().telegramWorkflowId);
                PendingProfileLedger.MarkTelegramClaimed();
                SeedReplyModeDefaultForProfile(openBot.name, openBot.GetComponent<Bot>().telegramProfileId);   // WR-02: '*' row for a channel authed after a Вместе default

                // See CreateWhatsappWorkflowFromEdit: the webhook activates the new workflow
                // before responding — honor the channel toggle, the only activation gate
                // since the capsule↔ReplyMode unification.
                if (PlayerPrefs.GetInt(openBot.name + "isOnTelegram", 1) != 1)
                {
                    StartCoroutine(SetWorkflowActiveRoutine(openBot.GetComponent<Bot>().telegramWorkflowId, false));
                }

                CreateTelegramWorkflowFromEditSuccess = true;
                resolved = true;
                Saved();
            }
        }

        // Mirror of CreateWhatsappWorkflowFromEdit: never leave the pill stuck
        // on "Saving.." when the webhook fails or returns no id.
        if (!resolved)
        {
            Debug.LogError($"[{www.responseCode}] CreateTelegramWorkflow failed or returned no id: {www.error}");
            FailSavePanel();
        }

        LoadingPanel.SetActive(false);
    }

    // Pure n8n activate/deactivate with NO save-pill side effects — deliberately
    // does not touch the four *Saved gate flags or LoadingPanel. Used to reconcile a
    // workflow's active state to the channel toggle outside the save flow: the
    // Create*Workflow webhook activates the new workflow server-side before it responds
    // (node order: … → Activate Created Workflow → … → Send New Workflows Id), so a
    // channel authed while its toggle is off must be deactivated once we hold its id.
    private IEnumerator SetWorkflowActiveRoutine(string id, bool active)
    {
        // Same sentinels the Enable/Save paths skip — no n8n workflow to (de)activate.
        if (string.IsNullOrEmpty(id) || id.Equals("-1")) yield break;

        // See EnableWhatsappWorkflow: n8n's REST API 415s anything but application/json,
        // and Unity's transport stamps x-www-form-urlencoded on a bodyless POST, so pin it.
        using UnityWebRequest www = new UnityWebRequest($"{n8nBaseUrl}/api/v1/workflows/{id}/" + (active ? "activate" : "deactivate"), "POST");
        www.downloadHandler = new DownloadHandlerBuffer();
        www.timeout = 30;
        www.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
            Debug.LogError($"[{www.responseCode}] SetWorkflowActiveRoutine {www.url}: {www.error} {www.downloadHandler.text}");
    }

    private IEnumerator EnableWhatsappWorkflow(string id, bool enabled)
    {
        // "" (workflow never created) and "-1" (channel never authed) mean there is
        // nothing to (de)activate on n8n — the same sentinels SaveBotSettings skips.
        // The bot-card toggle fires this for both channels unconditionally, so a
        // single-channel bot would otherwise log a guaranteed 404 (/workflows/-1/…).
        if (string.IsNullOrEmpty(id) || id.Equals("-1"))
        {
            EnableWhatsappWorkflowSaved = true;
            Saved();
            yield break;
        }

        LoadingPanel.SetActive(true);

        // n8n's REST API only accepts application/json here and 415s anything else.
        // A body-less POST is NOT enough: Unity's libcurl transport stamps
        // Content-Type: application/x-www-form-urlencoded onto a POST with no upload
        // handler, so the JSON content type must be pinned explicitly. An empty body
        // with application/json is accepted (verified against n8n 2.27.4).
        using UnityWebRequest www = new UnityWebRequest($"{n8nBaseUrl}/api/v1/workflows/{id}/" + (enabled ? "activate" : "deactivate"), "POST");
        www.downloadHandler = new DownloadHandlerBuffer();
        www.timeout = 30;

        www.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        // Mark this sub-step done whether the request succeeded or not, so the
        // save pill's four-flag gate in Saved() can never hang on a failed
        // activate/deactivate. Record the failure so the pill ends on the error
        // state. (The bot-card activation toggle shares this coroutine but never
        // shows the pill, and the next "Saving.." show resets _saveHadError, so
        // a failure there can't leak into a later save's pill text.)
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[{www.responseCode}] EnableWhatsappWorkflow {www.url}: {www.error} {www.downloadHandler.text}");
            _saveHadError = true;
        }

        EnableWhatsappWorkflowSaved = true;
        Saved();

        LoadingPanel.SetActive(false);
    }

    private IEnumerator EnableTelegramWorkflow(string id, bool enabled)
    {
        // See EnableWhatsappWorkflow: sentinel ids ("" / "-1") have no n8n workflow.
        if (string.IsNullOrEmpty(id) || id.Equals("-1"))
        {
            EnableTelegramWorkflowSaved = true;
            Saved();
            yield break;
        }

        LoadingPanel.SetActive(true);

        // See EnableWhatsappWorkflow: Content-Type must be pinned to application/json
        // or Unity's transport substitutes x-www-form-urlencoded and n8n replies 415.
        using UnityWebRequest www = new UnityWebRequest($"{n8nBaseUrl}/api/v1/workflows/{id}/" + (enabled ? "activate" : "deactivate"), "POST");
        www.downloadHandler = new DownloadHandlerBuffer();
        www.timeout = 30;

        www.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);
        www.SetRequestHeader("Content-Type", "application/json");

        yield return www.SendWebRequest();

        // See EnableWhatsappWorkflow: always settle the gate, record failures.
        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[{www.responseCode}] EnableTelegramWorkflow {www.url}: {www.error} {www.downloadHandler.text}");
            _saveHadError = true;
        }

        EnableTelegramWorkflowSaved = true;
        Saved();

        LoadingPanel.SetActive(false);
    }

    private IEnumerator DeleteWhatsappWorkflow(string whatsappWorkflowId, bool deletingBot)
    {
        using UnityWebRequest whatsappRequest = UnityWebRequest.Delete($"{n8nBaseUrl}/api/v1/workflows/{whatsappWorkflowId}");

        whatsappRequest.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);

        yield return whatsappRequest.SendWebRequest();

        if (!deletingBot)
        {
            if (whatsappRequest.result == UnityWebRequest.Result.Success)
            {
                openBot.GetComponent<Bot>().whatsappWorkflowId = "-1";
                PlayerPrefs.SetString(openBot.name + "WhatsappWorkflowId", "-1");
            }
        }
    }

    private IEnumerator DeleteTelegramWorkflow(string telegramWorkflowId, bool deletingBot)
    {
        using UnityWebRequest telegramRequest = UnityWebRequest.Delete($"{n8nBaseUrl}/api/v1/workflows/{telegramWorkflowId}");

        telegramRequest.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);

        yield return telegramRequest.SendWebRequest();

        if (!deletingBot)
        {
            if (telegramRequest.result == UnityWebRequest.Result.Success)
            {
                openBot.GetComponent<Bot>().telegramWorkflowId = "-1";
                PlayerPrefs.SetString(openBot.name + "TelegramWorkflowId", "-1");
            }
        }
    }

    // previousWhatsappOn / previousTelegramOn are the persisted channel states
    // as they were BEFORE this save (snapshotted by GetSaveSettings). They are
    // passed in rather than read here because SaveSettings persists the new
    // values synchronously while this coroutine is suspended on a web request.
    private IEnumerator SaveWorkflows(string whatsappWorkflowId, string telegramWorkflowId,
                                      bool previousWhatsappOn, bool previousTelegramOn)
    {
        LoadingPanel.SetActive(true);

        _saveHadError = false;
        openBotSettings.Saved.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = SavingText;
        openBotSettings.Saved.SetActive(true);


        string productsList = "";

        for (int i = 0; i < openBotSettings.ProductsParent.transform.childCount; i++)
        {
            ProductCardView product = openBotSettings.ProductsParent.transform.GetChild(i).GetComponent<ProductCardView>();

            product.Name.Trim();
            if (!product.Name.Equals(""))
            {
                productsList += $"Product{i + 1}: {product.Name}" +
                    $"\nProduct{i + 1} Price: {product.Price}" +
                    $"\nProduct{i + 1} Description: {product.Description}"
                    + (i == openBotSettings.ProductsParent.transform.childCount - 1 ? "" : $"\n\n");
            }
        }


        string servicesList = "";

        for (int i = 0; i < openBotSettings.ServicesParent.transform.childCount; i++)
        {
            ServiceCardView service = openBotSettings.ServicesParent.transform.GetChild(i).GetComponent<ServiceCardView>();

            service.Name.Trim();
            if (!service.Name.Equals(""))
            {
                servicesList += $"Service{i + 1}: {service.Name}" +
                    $"\nService{i + 1} Price: {service.Price}" +
                    $"\nService{i + 1} Description: {service.Description}"
                    + (i == openBotSettings.ServicesParent.transform.childCount - 1 ? "" : $"\n\n");
            }
        }


        WWWForm form = new();

        form.AddField("WhatsappWorkflowId", whatsappWorkflowId);
        form.AddField("TelegramWorkflowId", telegramWorkflowId);
        form.AddField("Name", openBotSettings.BotNameField.Value);
        AddBusinessTypeFields(form);
        form.AddField("Business", ComposeBusinessKnowledge(openBotSettings));
        form.AddField("Prompt", openBotSettings.PromptField.Value);
        form.AddField("ProductsList", productsList);
        form.AddField("ServicesList", servicesList);


        EnableWhatsappWorkflowSaved = false;
        EditWhatsappWorkflowSaved = false;
        EnableTelegramWorkflowSaved = false;
        EditTelegramWorkflowSaved = false;


        // Since the capsule↔ReplyMode unification the channel toggles are the ONLY
        // workflow-activation gate: «выключить бота целиком» = both toggles off.
        // The bots-page capsule governs reply SUPPRESSION (ReplyMode), not activation.
        //
        // Which makes the ordering below load-bearing: the server reads ABSENCE of a
        // '*' row as «reply», so activating a channel while a Вместе bot has no row
        // (its seed write failed, or n8n was unreachable when the mode was set) would
        // let the bot answer real clients while every UI surface says «Вместе».
        //
        // BLOCKING on purpose. The fire-and-forget SyncReplyMode loses this race by
        // construction — SetReplyMode responds only after its Postgres upsert, while
        // /workflows/{id}/activate is a single in-process n8n call — so dispatching
        // both in the same frame would activate first and guarantee nothing. Waiting
        // for the 200 is what actually proves the row exists (Respond sits after
        // Upsert Reply Mode). An Auto bot needs no row at all: absence means «reply».
        bool replySuppressionReady = true;
        if (ReplyModeToggleBinder.GetMode(openBot.name) == ReplyModeToggleBinder.ReplyMode.Semi)
        {
            string[] suppressProfileIds = AuthedProfileIds(openBot.GetComponent<Bot>());
            if (suppressProfileIds.Length > 0)
            {
                replySuppressionReady = false;
                yield return SyncReplyModeRoutine(
                    BuildReplyModePayload(suppressProfileIds, "*", true),
                    ok => replySuppressionReady = ok);
            }
        }

        // Freshly-created bots reach here with empty whatsappWorkflowId/telegramWorkflowId
        // because CreateWhatsappWorkflowFromStart / CreateTelegramWorkflowFromStart have
        // their n8n create-workflow POST commented out and never assign an id. Treating
        // "" the same as the "-1" sentinel skips the doomed Edit webhook (which would hang
        // Saved()'s AND-gate forever and leave LoadingPanel stuck).
        var whatsappWid = openBot.GetComponent<Bot>().whatsappWorkflowId;
        if (string.IsNullOrEmpty(whatsappWid) || whatsappWid.Equals("-1"))
        {
            EnableWhatsappWorkflowSaved = true;
            EditWhatsappWorkflowSaved = true;
        }
        else
        {
            bool whatsappTurningOn = openBotSettings.WhatsappToggle.isOn;
            if (previousWhatsappOn != whatsappTurningOn)
            {
                // Waking a channel we could not silence first is the one move that
                // can make a «Вместе» bot answer a real client — refuse it and fail
                // the save loudly. Turning a channel OFF is always safe.
                if (whatsappTurningOn && !replySuppressionReady)
                {
                    Debug.LogError("[SaveWorkflows] WhatsApp activation skipped — the '*' " +
                                   "suppression row did not land, so the bot would auto-reply.");
                    _saveHadError = true;
                    EnableWhatsappWorkflowSaved = true;   // settle the gate, never hang the pill
                }
                else
                {
                    StartCoroutine(EnableWhatsappWorkflow(whatsappWid, whatsappTurningOn));
                }
            }
            else
            {
                EnableWhatsappWorkflowSaved = true;
            }

            // isOnWhatsapp is persisted by SaveSettings, not here — see the
            // comment beside those two SetInt calls.

            using UnityWebRequest editWhatsappRequest = UnityWebRequest.Post($"{n8nBaseUrl}/webhook/EditWhatsappWorkflow", form);

            editWhatsappRequest.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);

            yield return editWhatsappRequest.SendWebRequest();

            // Mark done either way: a failed Edit must not leave EditWhatsappWorkflowSaved
            // false forever, which would hang Saved()'s four-flag gate and strand the pill.
            if (editWhatsappRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[{editWhatsappRequest.responseCode}] EditWhatsappWorkflow failed: {editWhatsappRequest.error}");
                _saveHadError = true;
            }
            EditWhatsappWorkflowSaved = true;
        }


        var telegramWid = openBot.GetComponent<Bot>().telegramWorkflowId;
        if (string.IsNullOrEmpty(telegramWid) || telegramWid.Equals("-1"))
        {
            EnableTelegramWorkflowSaved = true;
            EditTelegramWorkflowSaved = true;
        }
        else
        {
            bool telegramTurningOn = openBotSettings.TelegramToggle.isOn;
            if (previousTelegramOn != telegramTurningOn)
            {
                // Mirror of the WhatsApp gate above.
                if (telegramTurningOn && !replySuppressionReady)
                {
                    Debug.LogError("[SaveWorkflows] Telegram activation skipped — the '*' " +
                                   "suppression row did not land, so the bot would auto-reply.");
                    _saveHadError = true;
                    EnableTelegramWorkflowSaved = true;   // settle the gate, never hang the pill
                }
                else
                {
                    StartCoroutine(EnableTelegramWorkflow(telegramWid, telegramTurningOn));
                }
            }
            else
            {
                EnableTelegramWorkflowSaved = true;
            }

            // isOnTelegram is persisted by SaveSettings, not here — writing it
            // at this point (after the WhatsApp Edit round-trip) is what forced
            // the user to press Save twice before the button dimmed.

            using UnityWebRequest editTelegramRequest = UnityWebRequest.Post($"{n8nBaseUrl}/webhook/EditTelegramWorkflow", form);

            editTelegramRequest.SetRequestHeader("X-N8N-API-KEY", n8nAPIKey);

            yield return editTelegramRequest.SendWebRequest();

            // See EditWhatsappWorkflow above: mark done either way so the gate can settle.
            if (editTelegramRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[{editTelegramRequest.responseCode}] EditTelegramWorkflow failed: {editTelegramRequest.error}");
                _saveHadError = true;
            }
            EditTelegramWorkflowSaved = true;
        }


        Saved();
    }

    private void Saved()
    {
        // The four *Saved flags now mean "this sub-step finished" (success OR
        // failure), so the gate settles the pill once everything is done rather
        // than hanging when a request fails. _saveHadError decides the text.
        bool workflowsDone = EditWhatsappWorkflowSaved && EnableWhatsappWorkflowSaved &&
                             EditTelegramWorkflowSaved && EnableTelegramWorkflowSaved;
        bool createDone = CreateWhatsappWorkflowFromEditSuccess || CreateTelegramWorkflowFromEditSuccess;

        if (workflowsDone || createDone)
        {
            LoadingPanel.SetActive(false);

            StartCoroutine(ShowSavedPanel(_saveHadError ? SaveFailedText : SavedText));

            CreateWhatsappWorkflowFromEditSuccess = false;
            CreateTelegramWorkflowFromEditSuccess = false;

            if (workflowsDone)
            {
                EditWhatsappWorkflowSaved = false;
                EnableWhatsappWorkflowSaved = false;
                EditTelegramWorkflowSaved = false;
                EnableTelegramWorkflowSaved = false;
            }

            _saveHadError = false;
        }
    }

    // Settle the pill into its failure state and hide it. Used by the
    // Create*WorkflowFromEdit paths, whose success is tracked outside the
    // four-flag gate, so a failure there can't route through Saved().
    private void FailSavePanel() => StartCoroutine(ShowSavedPanel(SaveFailedText));

    private IEnumerator ShowSavedPanel(string finalText)
    {
        // Capture the Saved GameObject reference up-front. The user can
        // press back during the 2-second wait, which routes through
        // SettleClosedInstant and nulls openBotSettings before this
        // coroutine resumes. Without the capture the post-wait line
        // NullRefs, the Saved child never gets SetActive(false), and
        // re-entering that bot's settings shows it still active because
        // SetActive(false) on the parent only hid it visually — its own
        // activeSelf stayed true. The GameObject itself isn't destroyed
        // (BotSettings clones persist under BotSettingsParent), so the
        // captured reference stays valid and SetActive(false) still works.
        if (openBotSettings == null) yield break;
        var saved = openBotSettings.Saved;
        if (saved == null) yield break;
        saved.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = finalText;

        yield return new WaitForSeconds(2f);

        if (saved != null) saved.SetActive(false);
    }


    //////////////////////////////////////////////////////////SEND TO TELEGRAM REQUESTS//////////////////////////////////////////////////////////

    // Delivers a support-form message to the owner's Telegram chat (both ids
    // live in secrets.json). Public so ProfileSubPages.Support can start it;
    // callback reports success so the sheet can close or keep the draft.
    public IEnumerator SendToTelegram(string message, System.Action<bool> callback = null)
    {
        if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(telegramBotToken) || string.IsNullOrEmpty(supportChatId))
        {
            if (string.IsNullOrEmpty(telegramBotToken))
                Debug.LogError("[SendToTelegram] telegramBotToken missing from secrets.json");
            if (string.IsNullOrEmpty(supportChatId))
                Debug.LogError("[SendToTelegram] supportChatId missing from secrets.json");
            callback?.Invoke(false);
            yield break;
        }

        string url = $"https://api.telegram.org/bot{telegramBotToken}/sendMessage";
        WWWForm form = new();
        form.AddField("chat_id", supportChatId);
        form.AddField("text", message);

        using UnityWebRequest www = UnityWebRequest.Post(url, form);
        www.timeout = 30;
        yield return www.SendWebRequest();

        bool ok = www.result == UnityWebRequest.Result.Success;
        if (!ok)
            Debug.LogError($"[SendToTelegram] [{www.responseCode}] {www.error}"); // never log the URL — it embeds the bot token
        callback?.Invoke(ok);
    }

    //////////////////////////////////////////////////////////SEND FILE//////////////////////////////////////////////////////////

    public void OnPickFileButtonPressed()
    {
        pdf = NativeFilePicker.ConvertExtensionToFileType("pdf"); // Returns "application/pdf" on Android and "com.adobe.pdf" on iOS
        txt = NativeFilePicker.ConvertExtensionToFileType("txt");

        NativeFilePicker.PickFile((path) =>
        {
            if (path == null)
            {
                Debug.Log("Operation cancelled");
            }
            else
            {
                Debug.Log("Picked file path: " + path);
                // Start the upload process using the file path
                //StartCoroutine(UploadFile(path));
            }
        }, null); // 'null' allows all file types
    }


    private void PickFile()
    {
        pdf = NativeFilePicker.ConvertExtensionToFileType("pdf"); // Returns "application/pdf" on Android and "com.adobe.pdf" on iOS
        txt = NativeFilePicker.ConvertExtensionToFileType("txt");
        //video = NativeFilePicker.ConvertExtensionToFileType("image");
        rtf = NativeFilePicker.ConvertExtensionToFileType("rtf");
        xml = NativeFilePicker.ConvertExtensionToFileType("xml");
        csv = NativeFilePicker.ConvertExtensionToFileType("csv");
        xls = NativeFilePicker.ConvertExtensionToFileType("xls");
        xlsx = NativeFilePicker.ConvertExtensionToFileType("xlsx");
        doc = "com.microsoft.word.doc";
        docx = "org.openxmlformats.wordprocessingml.document";


        //if (Input.mousePosition.x < Screen.width / 3)
        //{
        PickMediaFile();
        //}
        //else if (Input.mousePosition.x < Screen.width * 2 / 3)
        //{
        //    PickPDFFile();
        //}
        //else
        //{
        //    PickCreateAllTXTFile();
        //}
    }

    private void PickMediaFile()
    {
        #if UNITY_ANDROID
		// Use MIMEs on Android
        string[] fileTypes = new string[] { pdf, txt, rtf, xml, csv, xls, xlsx, doc, docx };
        #else
        // Use UTIs on iOS
        string[] fileTypes = new string[] { pdf, txt, rtf, xml, csv, xls, xlsx, doc, docx };
        //string[] fileTypes = new string[] { "public.image", "public.movie", pdf, txt, rtf };

#endif

        // Pick image(s) and/or video(s)
        NativeFilePicker.PickMultipleFiles((paths) =>
        {
            if (paths == null)
            {
            }
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

    private void PickPDFFile()
    {
        // Pick a PDF file
        NativeFilePicker.PickFile((path) =>
        {
            if (path == null)
                Debug.Log("Operation cancelled");
            else
                Debug.Log("Picked file: " + path);
        }, new string[] { video });
    }

    private void PickCreateAllTXTFile()
    {
        //    // Create a dummy text file
        //    string filePath = Path.Combine(Application.temporaryCachePath, "test.txt");
        //    File.WriteAllText(filePath, "Hello world!");

        //    // Export the file
        //    NativeFilePicker.ExportFile(filePath, (success) => Debug.Log("File exported: " + success));
        // Pick a PDF file
        NativeFilePicker.PickFile((path) =>
        {
            if (path == null)
                Debug.Log("Operation cancelled");
            else
                Debug.Log("Picked file: " + path);
        }, new string[] { pdf, txt, video });
    }

    private IEnumerator UploadFile(string filePath)
    {
        //string originalName = "MyImportantDocument.pdf";
        //string path = Path.Combine(filePath, originalName);

        string uploadUrl = $"{n8nBaseUrl}/webhook/UploadFile";

        byte[] fileData = File.ReadAllBytes(filePath);
        string fileName = Path.GetFileName(filePath);

        WWWForm form = new();

        //form.AddField("Name", fileName);
        //form.AddBinaryData("data", fileData, fileName, "application/pdf"); // Added mimeType here
        form.AddBinaryData("data", fileData, fileName); // Added mimeType here


        using UnityWebRequest www = UnityWebRequest.Post(uploadUrl, form);

        yield return www.SendWebRequest();


        if (www.result != UnityWebRequest.Result.Success)
        {
        }
        else
        {
        }
    }

    //Gallery
    private void PickGalleryFile()
    {
        //if (Input.mousePosition.x < Screen.width / 3)
        //{
        //    // Take a screenshot and save it to Gallery/Photos
        //    StartCoroutine(TakeScreenshotAndSave());
        //}
        //else
        //{
            // Don't attempt to pick media from Gallery/Photos if
            // another media pick operation is already in progress
            if (NativeGallery.IsMediaPickerBusy())
                return;

            PickImageOrVideo();

            //if (Input.mousePosition.x < Screen.width * 2 / 3)
            //{
            //    // Pick a PNG image from Gallery/Photos
            //    // If the selected image's width and/or height is greater than 512px, down-scale the image
            //    PickImage(512);
            //}
            //else
            //{
            //    // Pick a video from Gallery/Photos
            //    PickVideo();
            //}
        //}
    }

    // Example code doesn't use this function but it is here for reference
    private void PickImageOrVideo()
    {
        if (NativeGallery.CanSelectMultipleMediaTypesFromGallery())
        {
            NativeGallery.GetMixedMediaFromGallery((path) =>
            {
                Debug.Log("Media path: " + path);
                if (path != null)
                {
                    // Determine if user has picked an image, video or neither of these
                    switch (NativeGallery.GetMediaTypeOfFile(path))
                    {
                        case NativeGallery.MediaType.Image: Debug.Log("Picked image"); break;
                        case NativeGallery.MediaType.Video: Debug.Log("Picked video"); break;
                        default: Debug.Log("Probably picked something else"); break;
                    }
                }
            }, NativeGallery.MediaType.Image | NativeGallery.MediaType.Video, "Select an image or video");
        }
    }




    private IEnumerator TakeScreenshotAndSave()
    {
        yield return new WaitForEndOfFrame();

        Texture2D ss = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        ss.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        ss.Apply();

        // Save the screenshot to Gallery/Photos
        NativeGallery.SaveImageToGallery(ss, "GalleryTest", "Image.png", (success, path) => Debug.Log("Media save result: " + success + " " + path));

        // To avoid memory leaks
        Destroy(ss);
    }

    private void PickImage(int maxSize)
    {
        NativeGallery.GetImageFromGallery((path) =>
        {
            Debug.Log("Image path: " + path);
            if (path != null)
            {
                // Create Texture from selected image
                Texture2D texture = NativeGallery.LoadImageAtPath(path, maxSize);
                if (texture == null)
                {
                    Debug.Log("Couldn't load texture from " + path);
                    return;
                }

                // Assign texture to a temporary quad and destroy it after 5 seconds
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 2.5f;
                quad.transform.forward = Camera.main.transform.forward;
                quad.transform.localScale = new Vector3(1f, texture.height / (float)texture.width, 1f);

                Material material = quad.GetComponent<Renderer>().material;
                if (!material.shader.isSupported) // happens when Standard shader is not included in the build
                    material.shader = Shader.Find("Legacy Shaders/Diffuse");

                material.mainTexture = texture;

                Destroy(quad, 5f);

                // If a procedural texture is not destroyed manually, 
                // it will only be freed after a scene change
                Destroy(texture, 5f);
            }
        });
    }
}
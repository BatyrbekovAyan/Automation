using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Automation.BotSettingsUI;

public partial class BotSettings : MonoBehaviour
{
    #region Serialized — Tabs
    [SerializeField] private GameObject General;
    [SerializeField] private GameObject Business;
    [SerializeField] private GameObject Product;
    [SerializeField] private GameObject Service;
    [SerializeField] private GameObject Prompt;
    [SerializeField] private Button GeneralTabButton;
    [SerializeField] private Button BusinessTabButton;
    [SerializeField] private Button ProductTabButton;
    [SerializeField] private Button ServiceTabButton;
    [SerializeField] private Button PromptTabButton;
    [SerializeField] private TextMeshProUGUI headerTitle;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button backButton;
    public Button SaveButton => saveButton;
    #endregion

    [System.Serializable]
    public struct TabVisual
    {
        public TextMeshProUGUI label;
        public GameObject underline;
    }

    #region Serialized — Tab visuals
    [SerializeField] private TabVisual[] tabVisuals;
    [SerializeField] private Color tabActiveColor = new Color(0.106f, 0.486f, 0.922f); // #1B7CEB
    [SerializeField] private Color tabInactiveColor = new Color(0.557f, 0.557f, 0.576f); // #8E8E93
    #endregion

    #region Serialized — General tab
    [SerializeField] public EditableField BotNameField;
    public TMP_Dropdown BusinessTypeDropdown;
    [SerializeField] public ToggleRow whatsappRow;
    [SerializeField] public ToggleRow telegramRow;
    public Toggle WhatsappToggle => whatsappRow != null ? whatsappRow.Toggle : null;
    public Toggle TelegramToggle => telegramRow != null ? telegramRow.Toggle : null;
    [SerializeField] public EditableField WhatsappNumberField;
    [SerializeField] public EditableField TelegramNumberField;
    #endregion

    #region Serialized — Business / Prompt
    [SerializeField] public EditableTextArea BusinessField;
    [SerializeField] public EditableTextArea PromptField;
    #endregion

    #region Serialized — Products / Services
    [SerializeField] public GameObject ProductPrefab;
    [SerializeField] public GameObject ServicePrefab;
    [SerializeField] public RectTransform ProductsParent;
    [SerializeField] public RectTransform ServicesParent;
    [SerializeField] private AddItemButton addProductButton;
    [SerializeField] private AddItemButton addServiceButton;
    [SerializeField] private ItemEditSheet productEditSheet;
    [SerializeField] private ItemEditSheet serviceEditSheet;
    public AddItemButton AddProductButton => addProductButton;
    public AddItemButton AddServiceButton => addServiceButton;
    #endregion

    #region Serialized — Delete Bot
    [SerializeField] private Button deleteBotButton;
    [SerializeField] private GameObject deleteConfirmPopup;
    [SerializeField] private Button deleteConfirmButton;
    [SerializeField] private Button deleteCancelButton;
    #endregion

    #region Serialized — Change-number popups, save indicator, upload
    // Auth itself (QR / code panels, number inputs, timers, done/back buttons)
    // is now owned by Manager's shared auth page — see
    // ShowWhatsappAuthFromSettings / ShowTelegramAuthFromSettings in the
    // BotSettings.Auth partial. Only the "really change number?" popups,
    // the Save indicator, and the per-tab list-upload buttons live in the
    // BotSettings prefab now.
    [SerializeField] public GameObject Saved;
    [SerializeField] private GameObject ConfirmChangeWhatsappNumberPopup;
    [SerializeField] private GameObject ConfirmChangeTelegramNumberPopup;
    [SerializeField] private Button ConfirmChangeWhatsappNumberButton;
    [SerializeField] private Button CancelChangeWhatsappNumberButton;
    [SerializeField] private Button ConfirmChangeTelegramNumberButton;
    [SerializeField] private Button CancelChangeTelegramNumberButton;
    [SerializeField] private Button UploadProductsPriceListButton;
    [SerializeField] private Button UploadServicesPriceListButton;
    #endregion

    // Needed for PickMediaFile / UploadFile logic that references file extension fields
    private string pdf;
    private string txt;
    private string rtf;
    private string xml;
    private string csv;
    private string xls;
    private string xlsx;
    private string docx;

    // Each bot has its own BotSettings prefab instance, so a write-on-Awake
    // singleton would race the same way SwipeToBackBotSettings.Instance did
    // (last-instantiated wins, even when a different BotSettings is the one
    // the user is actually driving). Forward to Manager.openBotSettings
    // instead — that field has a single, deterministic lifecycle: set in
    // Bot.OpenSettings, cleared in BotSettings.SettleClosedInstant /
    // ConfirmDeleteBot. Read-only by design — assignments must go through
    // Manager.openBotSettings to keep one source of truth.
    public static BotSettings Instance => Manager.openBotSettings;



    void Start()
    {
        WireTabs();
        WireFields();
        WireProductsAndServices();
        WireAuthButtons();
        WireHeaderButtons();
        WireDeleteBot();
        WireUploadedFiles();
        SyncHeaderTitle();

        // General-tab layout: each authenticated-number field must sit right
        // under its platform toggle. The rebuilder groups both numbers after
        // both toggles, so re-order at runtime once references are bound.
        PlaceNumberFieldsUnderToggles();

        // Editing a phone number must be gated by the "really change number?"
        // confirmation. An invisible button over each number field intercepts
        // taps before the TMP_InputField grabs focus.
        WireNumberFieldAsChangeTrigger(WhatsappNumberField, OpenConfirmChangeWhatsappNumberPopup);
        WireNumberFieldAsChangeTrigger(TelegramNumberField, OpenConfirmChangeTelegramNumberPopup);
    }

    private void PlaceNumberFieldsUnderToggles()
    {
        if (whatsappRow != null && WhatsappNumberField != null &&
            whatsappRow.transform.parent == WhatsappNumberField.transform.parent)
        {
            WhatsappNumberField.transform.SetSiblingIndex(whatsappRow.transform.GetSiblingIndex() + 1);
        }
        if (telegramRow != null && TelegramNumberField != null &&
            telegramRow.transform.parent == TelegramNumberField.transform.parent)
        {
            TelegramNumberField.transform.SetSiblingIndex(telegramRow.transform.GetSiblingIndex() + 1);
        }
    }

    //////////////////////////////////////// CONFIRM-CHANGE POPUPS ////////////////////////////////////////
    //
    // ConfirmChangeWhatsappNumberPopup / ConfirmChangeTelegramNumberPopup are
    // baked into the BotSettings prefab by the editor menu
    // "Tools → BotSettings → Build Confirm-Change Popups"
    // (Assets/Editor/BotSettingsConfirmChangePopupBuilder.cs).
    //
    // Run that once after pulling this change; the popups will appear in the
    // prefab hierarchy and the serialized references on BotSettings will be
    // populated. No runtime building.

    //////////////////////////////////////// NUMBER-FIELD TAP-TO-CHANGE ////////////////////////////////////////

    // After the editor menu "Tools → BotSettings → Build Confirm-Change Popups"
    // runs, each number field is a NumberDisplayField card with a Button on
    // its root. This method just wires the tap to the popup opener — no
    // InputField fight, no hide-the-label hack.
    private static void WireNumberFieldAsChangeTrigger(EditableField field, UnityEngine.Events.UnityAction onTap)
    {
        if (field == null) return;
        var btn = field.GetComponent<Button>();
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(onTap);
    }

    private void WireHeaderButtons()
    {
        if (saveButton != null)
        {
            saveButton.onClick.AddListener(() =>
            {
                var bot = Manager.openBot != null ? Manager.openBot.GetComponent<Bot>() : null;
                if (bot != null)
                    Manager.Instance.GetSaveSettings(bot.whatsappWorkflowId, bot.telegramWorkflowId);
                else
                    Manager.Instance.SaveSettings();
            });
            saveButton.interactable = false;
        }
        if (backButton != null)
            backButton.onClick.AddListener(OnBackPressed);
    }

    private void OnBackPressed()
    {
        // If already animating (either slide-in or slide-out in progress), ignore
        // duplicate taps. The gesture commit path calls OnSwipeCommitted() directly
        // so it does not go through here.
        if (SwipeToBackBotSettings.Instance != null && SwipeToBackBotSettings.Instance.IsAnimating)
            return;

        RevertUnsavedEdits();

        // BotsPage must be visible during the slide-out so the parallax shows.
        if (BotsPage.Instance != null)
            BotsPage.Instance.gameObject.SetActive(true);

        if (SwipeToBackBotSettings.Instance != null)
            SwipeToBackBotSettings.Instance.SlideOutToBotsPage(SettleClosedInstant);
        else
            SettleClosedInstant(); // fallback when swipe component isn't wired
    }

    // Called by SwipeToBackBotSettings once the commit animation finishes (either
    // gesture-driven or programmatic via OnBackPressed). Deactivates the wrapper
    // and clears the open-bot references. Also used as the fallback when the
    // swipe component is missing.
    public void SettleClosedInstant()
    {
        if (Manager.BotSettingsParentStatic != null)
        {
            var parentGo = Manager.BotSettingsParentStatic.transform.parent != null
                ? Manager.BotSettingsParentStatic.transform.parent.gameObject
                : Manager.BotSettingsParentStatic;
            parentGo.SetActive(false);
        }
        if (BotsPage.Instance != null)
            BotsPage.Instance.gameObject.SetActive(true);

        Manager.openBot = null;
        Manager.openBotSettings = null;
    }

    // Runs the existing revert-unsaved-edits behavior. Extracted so
    // OnBackPressed and the gesture-commit path share one source of truth.
    private void RevertUnsavedEdits()
    {
        // Revert any unsaved edits from PlayerPrefs. CloseSettings uses
        // Toggle.SetIsOnWithoutNotify, which flips isOn but bypasses the
        // ToggleRow's onValueChanged listener, so the iOS-style thumb/track
        // stays stuck on the user's last choice. Resync the row visuals
        // below to the now-reverted Toggle.isOn state.
        Manager.Instance.CloseSettings();

        if (whatsappRow != null && WhatsappToggle != null)
            whatsappRow.SetIsOnQuiet(WhatsappToggle.isOn);
        if (telegramRow != null && TelegramToggle != null)
            telegramRow.SetIsOnQuiet(TelegramToggle.isOn);

        if (saveButton != null) saveButton.interactable = false;
    }

    // Called by SwipeToBackBotSettings when the user swipes past the commit
    // threshold (or flicks hard enough). Runs the same revert step that the tap
    // path runs, then settles the page closed. Does NOT start another animation
    // — the swipe component's snap coroutine is what animated us here.
    public void OnSwipeCommitted()
    {
        RevertUnsavedEdits();
        SettleClosedInstant();
    }

    //////////////////////////////////////// DELETE BOT ////////////////////////////////////////
    //
    // DeleteBotButton lives inside the BotSettings page and, when tapped,
    // shows DeleteConfirmPopup. Confirm reuses Bot.DeleteBot() (the exact
    // teardown path used by the bot-card delete on the Bots list) so both
    // entry points stay consistent.

    private void WireDeleteBot()
    {
        if (deleteBotButton != null)
            deleteBotButton.onClick.AddListener(OpenDeleteBotPopup);
        if (deleteConfirmButton != null)
            PopupUI.WireFingerUp(deleteConfirmButton, ConfirmDeleteBot);
        if (deleteCancelButton != null)
            PopupUI.WireFingerUp(deleteCancelButton, CancelDeleteBot);
    }

    private void OpenDeleteBotPopup()
    {
        if (deleteConfirmPopup != null) PopupUI.Show(deleteConfirmPopup);
    }

    private void CancelDeleteBot()
    {
        if (deleteConfirmPopup != null) PopupUI.Hide(deleteConfirmPopup);
    }

    private void ConfirmDeleteBot()
    {
        // Bot.DeleteBot() destroys this whole BotSettings GameObject
        // synchronously below, so starting the animated PopupUI.Hide tweens
        // (backdrop DOFade + card DOScale) would leave DOTween chasing
        // destroyed RectTransform/Image targets next frame. Snap the popup
        // off instead — the user never sees it, the page is gone.
        if (deleteConfirmPopup != null) deleteConfirmPopup.SetActive(false);

        var openBot = Manager.openBot;
        if (openBot == null) return;

        // Return to the Bots list before destruction. Bot.DeleteBot()
        // destroys both the Bot GameObject and this BotSettings instance,
        // so any code must run before that point.
        if (Manager.BotSettingsParentStatic != null)
        {
            var parentGo = Manager.BotSettingsParentStatic.transform.parent != null
                ? Manager.BotSettingsParentStatic.transform.parent.gameObject
                : Manager.BotSettingsParentStatic;
            parentGo.SetActive(false);
        }
        if (BotsPage.Instance != null)
        {
            // Slide-in pushed BotsPage left for the parallax depth effect.
            // The normal back path animates it back to X=0 via
            // SlideOutToBotsPage, but Delete skips that animation, so restore
            // the centered position explicitly before reactivating — otherwise
            // the list reappears shifted ~30% of screen width to the left.
            var botsPageRt = BotsPage.Instance.GetComponent<RectTransform>();
            if (botsPageRt != null)
                botsPageRt.anchoredPosition = new Vector2(0f, botsPageRt.anchoredPosition.y);
            BotsPage.Instance.gameObject.SetActive(true);
        }

        var bot = openBot.GetComponent<Bot>();
        Manager.openBot = null;
        Manager.openBotSettings = null;

        if (bot != null) bot.DeleteBot();
    }

    public void OnEnable()
    {
        // Defense-in-depth: never carry a stranded "Saving.." pill into a fresh
        // open. The pill is normally hidden by its own settle coroutine, but if
        // an edge ever left its activeSelf true on this persistent clone it
        // would otherwise reappear stuck the next time settings open.
        if (Saved != null) Saved.SetActive(false);

        StartCoroutine(CheckWhatsappUnauthorizationOutsideApp());
        StartCoroutine(CheckTelegramUnauthorizationOutsideApp());
        SyncHeaderTitle();
        RefreshUploadedFiles();
    }
    
    // Mirrors the bot's current name onto the header. Called on enable, after
    // wiring, from Manager.SaveSettings after the new name is persisted, and
    // from Manager.CloseSettings when the field is reverted to the saved
    // PlayerPref value. Deliberately NOT wired to BotNameField.OnCommitted so
    // the header reflects only the saved name, not the in-progress edit.
    public void SyncHeaderTitle()
    {
        if (headerTitle == null || BotNameField == null) return;
        headerTitle.text = BotNameField.Value;
    }

    public void OnDisable() => OpenGeneralTab();

    // Returns the vertical ScrollRect under the currently-active tab root, if
    // any. Used by SwipeToBackBotSettings to disable vertical scrolling during
    // a horizontal swipe gesture. Returns null when no tab is active or the
    // active tab has no ScrollRect child.
    public ScrollRect CurrentTabScrollRect
    {
        get
        {
            GameObject tab = null;
            if (General  != null && General.activeInHierarchy)  tab = General;
            else if (Business != null && Business.activeInHierarchy) tab = Business;
            else if (Product  != null && Product.activeInHierarchy)  tab = Product;
            else if (Service  != null && Service.activeInHierarchy)  tab = Service;
            else if (Prompt   != null && Prompt.activeInHierarchy)   tab = Prompt;
            return tab != null ? tab.GetComponentInChildren<ScrollRect>(false) : null;
        }
    }

    //////////////////////////////////////// TABS ////////////////////////////////////////

    public void OpenGeneralTab()  => SetActiveTab(general: true);
    public void OpenBusinessTab() => SetActiveTab(business: true);
    public void OpenProductTab()  => SetActiveTab(product: true);
    public void OpenServiceTab()  => SetActiveTab(service: true);
    public void OpenPromptTab()   => SetActiveTab(prompt: true);

    private void SetActiveTab(bool general = false, bool business = false,
                              bool product = false, bool service = false,
                              bool prompt = false)
    {
        General.SetActive(general);
        Business.SetActive(business);
        Product.SetActive(product);
        Service.SetActive(service);
        Prompt.SetActive(prompt);

        if (tabVisuals == null || tabVisuals.Length < 5) return;
        var active = new[] { general, business, product, service, prompt };
        for (int i = 0; i < tabVisuals.Length; i++)
        {
            if (tabVisuals[i].underline != null)
                tabVisuals[i].underline.SetActive(active[i]);
            if (tabVisuals[i].label != null)
                tabVisuals[i].label.color = active[i] ? tabActiveColor : tabInactiveColor;
        }
    }

    private void WireTabs()
    {
        if (GeneralTabButton != null)  GeneralTabButton.onClick.AddListener(OpenGeneralTab);
        if (BusinessTabButton != null) BusinessTabButton.onClick.AddListener(OpenBusinessTab);
        if (ProductTabButton != null)  ProductTabButton.onClick.AddListener(OpenProductTab);
        if (ServiceTabButton != null)  ServiceTabButton.onClick.AddListener(OpenServiceTab);
        if (PromptTabButton != null)   PromptTabButton.onClick.AddListener(OpenPromptTab);
    }

    //////////////////////////////////////// FIELDS ////////////////////////////////////////

    private void WireFields()
    {
        if (BotNameField != null)
        {
            BotNameField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        }
        if (WhatsappNumberField != null)
            WhatsappNumberField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        if (TelegramNumberField != null)
            TelegramNumberField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());

        if (BusinessField != null)
            BusinessField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());
        if (PromptField != null)
            PromptField.OnCommitted.AddListener(_ => Manager.Instance.EnableSave());

        if (BusinessTypeDropdown != null)
            BusinessTypeDropdown.onValueChanged.AddListener(_ => Manager.Instance.EnableSave());

        if (WhatsappToggle != null)
            WhatsappToggle.onValueChanged.AddListener(WhatsappChannelToggleChanged);
        if (TelegramToggle != null)
            TelegramToggle.onValueChanged.AddListener(TelegramChannelToggleChanged);
    }

    //////////////////////////////////////// PRODUCTS / SERVICES ////////////////////////////////////////

    private void WireProductsAndServices()
    {
        if (addProductButton != null) addProductButton.OnTap.AddListener(AddProduct);
        if (addServiceButton != null) addServiceButton.OnTap.AddListener(AddService);

        if (productEditSheet != null)
        {
            productEditSheet.OnAnyCommitted += () => Manager.Instance.EnableSave();
            productEditSheet.OnProductDeleted += DeleteProductCard;
        }
        if (serviceEditSheet != null)
        {
            serviceEditSheet.OnAnyCommitted += () => Manager.Instance.EnableSave();
            serviceEditSheet.OnServiceDeleted += DeleteServiceCard;
        }
    }

    // Callers that instantiate a ProductCardView into ProductsParent MUST
    // call this right after Instantiate so the card's tap opens the edit
    // sheet. BotSettings.Start no longer scans ProductsParent as a safety
    // net, so binding is now the caller's responsibility — Manager.LoadBots,
    // Manager.CloseSettings, and BotSettings.AddProduct all funnel through
    // here to keep the invariant "every card in the list is bound".
    public void RegisterProductCard(ProductCardView card)
    {
        if (card == null) return;
        card.OnEditRequested += c => productEditSheet.Show(c);
    }

    public void RegisterServiceCard(ServiceCardView card)
    {
        if (card == null) return;
        card.OnEditRequested += c => serviceEditSheet.Show(c);
    }

    public void AddProduct()
    {
        var go = Instantiate(ProductPrefab,
                             ProductPrefab.transform.position,
                             ProductPrefab.transform.rotation,
                             ProductsParent);

        var card = go.GetComponent<ProductCardView>();
        RegisterProductCard(card);

        // Blank the prefab's display defaults ("Название" / "0" / "Описание")
        // so ItemEditSheet.Show binds empty strings into the input fields —
        // the user shouldn't have to delete pre-typed text before entering theirs.
        if (card != null)
        {
            card.Name = string.Empty;
            card.Price = string.Empty;
            card.Description = string.Empty;
        }

        var anim = go.GetComponent<Animation>();
        if (anim != null) anim.Play();

        RebuildTabLayout(ProductsParent);
        // Append + auto-scroll so the newly added card is visible at the
        // bottom of the viewport once the user dismisses the edit sheet.
        ScrollTabToBottom(Product);

        Manager.Instance.EnableSave();

        // Open the edit sheet right away so the user can fill in the new
        // item's fields without an extra tap.
        if (card != null && productEditSheet != null) productEditSheet.Show(card, isNewlyAdded: true);
    }

    public void AddService()
    {
        var go = Instantiate(ServicePrefab,
                             ServicePrefab.transform.position,
                             ServicePrefab.transform.rotation,
                             ServicesParent);

        var card = go.GetComponent<ServiceCardView>();
        RegisterServiceCard(card);

        if (card != null)
        {
            card.Name = string.Empty;
            card.Price = string.Empty;
            card.Description = string.Empty;
        }

        var anim = go.GetComponent<Animation>();
        if (anim != null) anim.Play();

        RebuildTabLayout(ServicesParent);
        ScrollTabToBottom(Service);

        Manager.Instance.EnableSave();

        if (card != null && serviceEditSheet != null) serviceEditSheet.Show(card, isNewlyAdded: true);
    }

    // Scrolls the given tab's ScrollRect to the bottom. Canvases are flushed
    // first so the ScrollRect sees the freshly-grown content size and scrolls
    // to the real new bottom rather than the pre-add one.
    private static void ScrollTabToBottom(GameObject tab)
    {
        if (tab == null) return;
        var scroll = tab.GetComponent<ScrollRect>();
        if (scroll == null) return;
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 0f;
    }

    // Re-run layout on the list RectTransform and walk up the ancestors so a
    // parent VerticalLayoutGroup (childControlHeight=false) re-reads the new
    // ContentSizeFitter-driven height and repositions sibling buttons instead
    // of letting them overlap the last created card.
    private static void RebuildTabLayout(RectTransform listRoot)
    {
        if (listRoot == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(listRoot);
        var parent = listRoot.parent as RectTransform;
        while (parent != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            parent = parent.parent as RectTransform;
        }
    }

    private void DeleteProductCard(ProductCardView card)
    {
        // SetActive(false) removes the child from VerticalLayoutGroup
        // consideration immediately (VLG skips inactive children), so the
        // rebuild below reflows as if the card were already gone. Without
        // this, Destroy is deferred to end-of-frame and VLG keeps a blank
        // slot for the doomed card, leaving a gap between the section
        // header and ProductsParent until the next layout pass.
        if (card != null) card.gameObject.SetActive(false);
        Destroy(card != null ? card.gameObject : null);
        RebuildTabLayout(ProductsParent);
        Manager.Instance.EnableSave();
    }

    private void DeleteServiceCard(ServiceCardView card)
    {
        if (card != null) card.gameObject.SetActive(false);
        Destroy(card != null ? card.gameObject : null);
        RebuildTabLayout(ServicesParent);
        Manager.Instance.EnableSave();
    }

    //////////////////////////////////////// AUTH WIRING ////////////////////////////////////////

    private void WireAuthButtons()
    {
        if (ConfirmChangeWhatsappNumberButton != null)
            PopupUI.WireFingerUp(ConfirmChangeWhatsappNumberButton, ConfirmChangeWhatsappNumber);
        if (CancelChangeWhatsappNumberButton != null)
            PopupUI.WireFingerUp(CancelChangeWhatsappNumberButton, CancelChangeWhatsappNumber);
        if (ConfirmChangeTelegramNumberButton != null)
            PopupUI.WireFingerUp(ConfirmChangeTelegramNumberButton, ConfirmChangeTelegramNumber);
        if (CancelChangeTelegramNumberButton != null)
            PopupUI.WireFingerUp(CancelChangeTelegramNumberButton, CancelChangeTelegramNumber);

        if (UploadProductsPriceListButton != null)
            UploadProductsPriceListButton.onClick.AddListener(UploadPriceList);
        if (UploadServicesPriceListButton != null)
            UploadServicesPriceListButton.onClick.AddListener(UploadServiceList);
    }
}

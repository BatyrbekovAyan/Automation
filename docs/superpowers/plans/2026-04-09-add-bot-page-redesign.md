# Add Bot Page Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 6-step bot creation wizard with a single-page form design matching `Design/chat-app-ui.html` SCREEN 3, preserving all backend logic (API calls, profile creation, workflow creation, PlayerPrefs storage).

**Architecture:** The new design consolidates Platform selection, Bot Name, Business Type, and Description into a single scrollable form page with tappable rows. Each row opens a modal popup for input. A "Создать Бота" button triggers the full creation flow, which shows WhatsApp/Telegram auth panels as overlays during profile setup. An Editor menu script programmatically builds the UI hierarchy since the .unity scene file cannot be hand-edited reliably.

**Tech Stack:** Unity 6 (C# 9+), TextMeshPro, DOTween, UnityEngine.UI

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `Assets/Scripts/Main/Manager.cs` | Modify | Remove wizard navigation, add form-based UI methods, wire new buttons, add `CreateBotFromForm` coroutine |
| `Assets/Scripts/Editor/AddBotPageSetup.cs` | Create | Editor menu script that builds the entire Add Bot UI hierarchy programmatically in the scene |
| `Assets/Scenes/Main.unity` | Modify (via Editor script) | New UI hierarchy for the Add Bot tab's screen panel |

**Files NOT modified (reference only):**
- `Assets/Scripts/Main/Bot.cs` — calls `Manager.Instance.DeleteProfilesAndWorkflows()`, `Manager.Instance.GetEnableWhatsappWorkflow/Telegram()`. No changes needed.
- `Assets/Scripts/Main/BotSettings.cs` — its own auth panels and workflow editing. No changes needed.
- `Assets/Scripts/Main/BotsPage.cs` — `CreateBot()` calls `Chanel.SetActive(true)`. In the scene Inspector, reassign `Chanel` reference to point to the new `AddBotFormPage` so this works without code changes.
- `Assets/Scripts/Main/BottomTabManager.cs` — tab system. Tab index 2's `screenPanel` will be reassigned to the new Add Bot form page.

## Key Design Decisions

1. **Auth panels remain as overlays.** `WhatsappAuth` and `TelegramAuth` GameObjects are kept and shown as modal overlays during `CreateBotFromForm`. `GetWhatsappProfileStatus`/`GetTelegramProfileStatus` polling sets completion flags for auto-proceed.

2. **`Confirmation` panel kept for backend compatibility.** `CreateWhatsappWorkflowFromStart` / `CreateTelegramWorkflowFromStart` write text to `Confirmation` children — these methods stay unchanged. The panel text is set but the panel isn't shown as a step.

3. **Business type selection reuses `BusinessTypesList`.** The same button list is reparented into the business selector popup panel.

4. **Scene changes via Editor script.** The `.unity` file is serialized YAML — hand-editing is error-prone. An Editor menu item (`Tools > Setup Add Bot Page`) builds the UI hierarchy programmatically and wires all `SerializeField` references on `Manager`.

---

### Task 1: Modify Manager.cs — Update SerializeField Declarations

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs:13-130` (the `#region` block)

- [ ] **Step 1: Remove obsolete wizard-only fields and add new form fields**

Replace the `#region` ... `#endregion` block with updated declarations. Keep all fields still referenced by unchanged code. Add new fields for the single-page form UI.

Fields to **remove** (only used in old wizard methods being deleted):
- `Chanel`, `Name`, `Business`, `Summary`, `SummaryChannelWhatsapp`, `SummaryChennelTelegram`
- `CreateBotButton`, `ChanelContinueButton`, `ChanelBackButton`, `NameContinueButton`, `NameBackButton`
- `BusinessContinueButton`, `BusinessBackButton`, `LaunchButton`, `SummaryBackButton`
- `CancelButton`, `ConfirmationContinueButton`, `MyBotsButton`
- `WhatsappToggle`, `TelegramToggle`, `BotNameInput`
- `SummaryBusinessType`, `SummaryBotName`, `SummaryWhatsappNumber`, `SummaryTelegramNumber`

Fields to **keep** (still used in unchanged backend/auth code):
- `MainPage`, `WhatsappAuth`, `TelegramAuth`, `Confirmation`, `BotsPage`, `BotsParent`, `BotPrefab`, `BotSettings`, `BotSettingsParent`
- `ProductPrefab`, `ServicePrefab`
- All auth panel GameObjects: `WhatsappQRPanel`, `WhatsappCodePanel`, `TelegramQRPanel`, `TelegramCodePanel`, `WhatsappCodeTimer`, `TelegramCodeTimer`, `WhatsappCodeSendingMessage`, `TelegramCodeSendingMessage`
- Auth buttons: `WhatsappAuthContinueButton`, `WhatsappAuthBackButton`, `TelegramAuthContinueButton`, `TelegramAuthBackButton`, and all QR/Code open/close/get/send buttons
- Auth inputs: `WhatsappNumberInput`, `TelegramNumberInput`, `TelegramCodeInput`
- `WhatsappQRCodeImage`, `TelegramQRCodeImage`
- `BusinessTypesList`, `LoadingPanel`, `SaveButton`
- `ChatsButton`, `SettingsButton`, `ChatsPanel`

Fields to **add**:
```csharp
[Header("Add Bot Form")]
[SerializeField] private GameObject AddBotFormPage;
[SerializeField] private TextMeshProUGUI platformValueText;
[SerializeField] private Image platformIconImage;
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
```

New private state variables (add after `private string telegramProfileId = "-1";`):
```csharp
private int selectedPlatform; // 0=none, 1=WhatsApp, 2=Telegram, 3=Both
private string formBotName = "";
private string formDescription = "";
private bool businessTypeSelected;
private bool whatsappAuthCompleted;
private bool telegramAuthCompleted;
private bool isCreatingBot;
```

- [ ] **Step 2: Verify the file compiles**

Open in Unity or use `grep` to confirm no dangling references to removed fields exist in Manager.cs after applying subsequent tasks. (Compile check deferred to after Task 3 since Start/methods still reference old fields.)

---

### Task 2: Modify Manager.cs — Replace Start() Method

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs:133-345` (the `Start()` method)

- [ ] **Step 1: Replace Start() with new form wiring**

Replace the entire `Start()` method body. Keep initialization logic, replace button listeners.

```csharp
public void Start()
{
    Application.targetFrameRate = 60;

    businessType = BusinessTypesList[0].gameObject;
    businessButtonDefaultColor = businessType.GetComponent<Image>().color;

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

    // ── Add Bot Form ──
    if (platformRowButton != null) platformRowButton.onClick.AddListener(OpenPlatformSelector);
    if (botNameRowButton != null) botNameRowButton.onClick.AddListener(OpenBotNameInput);
    if (businessTypeRowButton != null) businessTypeRowButton.onClick.AddListener(OpenBusinessSelector);
    if (descriptionRowButton != null) descriptionRowButton.onClick.AddListener(OpenDescriptionInput);

    if (whatsappOptionButton != null) whatsappOptionButton.onClick.AddListener(() => SelectPlatform(1));
    if (telegramOptionButton != null) telegramOptionButton.onClick.AddListener(() => SelectPlatform(2));
    if (bothOptionButton != null) bothOptionButton.onClick.AddListener(() => SelectPlatform(3));

    if (createBotFormButton != null)
    {
        createBotFormButton.onClick.AddListener(() => StartCoroutine(CreateBotFromForm()));
        createBotFormButton.interactable = false;
    }

    // ── Auth panels — WhatsApp ──
    if (WhatsappAuthContinueButton != null)
    {
        WhatsappAuthContinueButton.onClick.AddListener(() =>
        {
            whatsappAuthCompleted = true;
            WhatsappAuth.SetActive(false);
        });
    }
    if (WhatsappAuthBackButton != null) WhatsappAuthBackButton.onClick.AddListener(CancelBotCreation);

    if (OpenWhatsappQRPanelButton != null) OpenWhatsappQRPanelButton.onClick.AddListener(() => StartCoroutine(OpenWhatsappQRPanel()));
    if (OpenWhatsappCodePanelButton != null) OpenWhatsappCodePanelButton.onClick.AddListener(OpenWhatsappCodePanel);
    if (CloseWhatsappQRPanelButton != null) CloseWhatsappQRPanelButton.onClick.AddListener(CloseWhatsappQRPanel);
    if (CloseWhatsappCodePanelButton != null) CloseWhatsappCodePanelButton.onClick.AddListener(CloseWhatsappCodePanel);
    if (GetWhatsappCodeButton != null) GetWhatsappCodeButton.onClick.AddListener(() => StartCoroutine(GetWhatsappCode()));

    // ── Auth panels — Telegram ──
    if (TelegramAuthContinueButton != null)
    {
        TelegramAuthContinueButton.onClick.AddListener(() =>
        {
            telegramAuthCompleted = true;
            TelegramAuth.SetActive(false);
        });
    }
    if (TelegramAuthBackButton != null) TelegramAuthBackButton.onClick.AddListener(CancelBotCreation);

    if (OpenTelegramQRPanelButton != null) OpenTelegramQRPanelButton.onClick.AddListener(() => StartCoroutine(OpenTelegramQRPanel()));
    if (OpenTelegramCodePanelButton != null) OpenTelegramCodePanelButton.onClick.AddListener(OpenTelegramCodePanel);
    if (CloseTelegramQRPanelButton != null) CloseTelegramQRPanelButton.onClick.AddListener(CloseTelegramQRPanel);
    if (CloseTelegramCodePanelButton != null) CloseTelegramCodePanelButton.onClick.AddListener(CloseTelegramCodePanel);
    if (GetTelegramCodeButton != null) GetTelegramCodeButton.onClick.AddListener(() => StartCoroutine(GetTelegramCode()));
    if (SendTelegramCodeButton != null) SendTelegramCodeButton.onClick.AddListener(() => StartCoroutine(SendTelegramCode()));

    // ── Auth input fields ──
    if (WhatsappNumberInput != null) WhatsappNumberInput.onValueChanged.AddListener(WhatsappNumberInputChanged);
    if (TelegramNumberInput != null) TelegramNumberInput.onValueChanged.AddListener(TelegramNumberInputChanged);
    if (TelegramCodeInput != null) TelegramCodeInput.onValueChanged.AddListener(TelegramCodeInputChanged);

    // ── Business type buttons ──
    foreach (Button business in BusinessTypesList)
    {
        business.onClick.AddListener(() => ChooseBusiness(business));
    }

    // ── Other ──
    if (ChatsButton != null) ChatsButton.onClick.AddListener(OpenChatsPanel);
    if (SettingsButton != null) SettingsButton.onClick.AddListener(() => StartCoroutine(GetWhatsappMesseges()));

    // Initialize popups as hidden
    if (platformSelectorPanel != null) platformSelectorPanel.SetActive(false);
    if (botNameInputPanel != null) botNameInputPanel.SetActive(false);
    if (businessSelectorPanel != null) businessSelectorPanel.SetActive(false);
    if (descriptionInputPanel != null) descriptionInputPanel.SetActive(false);
}
```

---

### Task 3: Modify Manager.cs — Replace Wizard Methods with Form Methods

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs:643-996` (the `CREATE BOT` section)

- [ ] **Step 1: Replace wizard navigation methods**

Delete everything from the `//CREATE BOT//` comment through the `ConfirmationContinue()` method (lines 643-996). Replace with the new form-based methods below. Keep `OpenMyBots()`.

```csharp
//////////////////////////////////////////////////////////CREATE BOT//////////////////////////////////////////////////////////

public void OpenMyBots()
{
    MainPage.SetActive(false);
    BotsPage.SetActive(true);
}

// ── Add Bot Form — Popup Controllers ──

public void OpenPlatformSelector()
{
    platformSelectorPanel.SetActive(true);
}

public void ClosePlatformSelector()
{
    platformSelectorPanel.SetActive(false);
}

public void SelectPlatform(int mode)
{
    selectedPlatform = mode;

    switch (mode)
    {
        case 1: // WhatsApp
            platformValueText.text = "WhatsApp";
            platformValueText.color = new Color32(37, 211, 102, 255); // #25D366
            if (platformIconImage != null)
            {
                platformIconImage.gameObject.SetActive(true);
                platformIconImage.color = new Color32(37, 211, 102, 255);
            }
            break;
        case 2: // Telegram
            platformValueText.text = "Telegram";
            platformValueText.color = new Color32(42, 171, 238, 255); // #2AABEE
            if (platformIconImage != null)
            {
                platformIconImage.gameObject.SetActive(true);
                platformIconImage.color = new Color32(42, 171, 238, 255);
            }
            break;
        case 3: // Both
            platformValueText.text = "WhatsApp + Telegram";
            platformValueText.color = new Color32(0, 122, 255, 255); // #007AFF
            if (platformIconImage != null)
            {
                platformIconImage.gameObject.SetActive(true);
                platformIconImage.color = new Color32(0, 122, 255, 255);
            }
            break;
    }

    ClosePlatformSelector();
    ValidateCreateForm();
}

public void OpenBotNameInput()
{
    botNameInputPanel.SetActive(true);
    if (botNamePopupInput != null)
    {
        botNamePopupInput.text = formBotName;
        botNamePopupInput.ActivateInputField();
    }
}

public void CloseBotNameInput()
{
    botNameInputPanel.SetActive(false);
}

public void ConfirmBotName()
{
    if (botNamePopupInput != null && !string.IsNullOrEmpty(botNamePopupInput.text))
    {
        formBotName = botNamePopupInput.text.Trim();
        botNameValueText.text = formBotName;
        botNameValueText.color = new Color32(28, 28, 30, 255); // --text-primary
    }

    CloseBotNameInput();
    ValidateCreateForm();
}

public void OpenBusinessSelector()
{
    businessSelectorPanel.SetActive(true);
}

public void CloseBusinessSelector()
{
    businessSelectorPanel.SetActive(false);
}

public void ChooseBusiness(Button chosenBusiness)
{
    businessType = chosenBusiness.gameObject;
    businessTypeSelected = true;

    foreach (Button business in BusinessTypesList)
    {
        business.gameObject.GetComponent<Image>().color = businessButtonDefaultColor;
    }
    chosenBusiness.gameObject.GetComponent<Image>().color = Color.green;

    if (businessTypeValueText != null)
    {
        businessTypeValueText.text = chosenBusiness.gameObject.name;
        businessTypeValueText.color = new Color32(28, 28, 30, 255);
    }

    CloseBusinessSelector();
    ValidateCreateForm();
}

public void OpenDescriptionInput()
{
    descriptionInputPanel.SetActive(true);
    if (descriptionPopupInput != null)
    {
        descriptionPopupInput.text = formDescription;
        descriptionPopupInput.ActivateInputField();
    }
}

public void CloseDescriptionInput()
{
    descriptionInputPanel.SetActive(false);
}

public void ConfirmDescription()
{
    if (descriptionPopupInput != null)
    {
        formDescription = descriptionPopupInput.text.Trim();
        if (!string.IsNullOrEmpty(formDescription))
        {
            descriptionValueText.text = formDescription;
            descriptionValueText.color = new Color32(28, 28, 30, 255);
        }
        else
        {
            descriptionValueText.text = "Необязательно";
            descriptionValueText.color = new Color32(199, 199, 204, 255); // --text-tertiary
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
    createBotFormButton.interactable = false;

    bool useWhatsapp = selectedPlatform == 1 || selectedPlatform == 3;
    bool useTelegram = selectedPlatform == 2 || selectedPlatform == 3;

    // Step 1: Create WhatsApp profile and authenticate
    if (useWhatsapp)
    {
        yield return StartCoroutine(CreateWhatsappProfile(formBotName, true));
        if (!isCreatingBot) yield break;

        WhatsappAuth.SetActive(true);
        WhatsappAuthContinueButton.interactable = false;
        PlayerPrefs.SetString("WhatsappCooldownFinishTime", "-1");
        WhatsappCodeTimer.SetActive(false);

        while (!whatsappAuthCompleted)
        {
            if (!isCreatingBot) yield break;
            yield return new WaitForSeconds(0.5f);
        }
    }

    // Step 2: Create Telegram profile and authenticate
    if (useTelegram)
    {
        yield return StartCoroutine(CreateTelegramProfile(formBotName, true));
        if (!isCreatingBot) yield break;

        TelegramAuth.SetActive(true);
        TelegramAuthContinueButton.interactable = false;
        PlayerPrefs.SetString("TelegramCooldownFinishTime", "-1");
        TelegramCodeTimer.SetActive(false);

        while (!telegramAuthCompleted)
        {
            if (!isCreatingBot) yield break;
            yield return new WaitForSeconds(0.5f);
        }
    }

    // Step 3: Instantiate bot
    GameObject newBot = Instantiate(BotPrefab, BotPrefab.transform.position, BotPrefab.transform.rotation, BotsParent.transform);
    newBot.name = "Bot" + id.ToString();

    newBot.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = formBotName;
    newBot.transform.GetChild(1).GetComponent<Toggle>().isOn = true;
    newBot.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = "Connecting..";
    newBot.GetComponent<Bot>().active = false;
    newBot.GetComponent<Bot>().EditButton.interactable = false;
    newBot.GetComponent<Bot>().ActivationSwitch.interactable = false;
    newBot.GetComponent<Bot>().whatsappProfileId = whatsappProfileId;
    newBot.GetComponent<Bot>().telegramProfileId = telegramProfileId;

    BotSettings newBotSettings = Instantiate(BotSettings, new Vector3(BotSettings.transform.position.x + Screen.width / 2, BotSettings.transform.position.y + Screen.height / 2, 0), BotSettings.transform.rotation, BotSettingsParentStatic.transform).GetComponent<BotSettings>();

    newBotSettings.BotNameButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = formBotName;
    newBotSettings.WhatsappToggle.isOn = useWhatsapp;
    newBotSettings.TelegramToggle.isOn = useTelegram;
    newBotSettings.BusinessTypeDropdown.value = businessType.transform.GetSiblingIndex();
    newBotSettings.WhatsappNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = useWhatsapp ? WhatsappNumberInput.text : "";
    newBotSettings.TelegramNumberButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = useTelegram ? TelegramNumberInput.text : "";
    newBotSettings.WhatsappNumberButton.transform.parent.gameObject.SetActive(useWhatsapp && !string.IsNullOrEmpty(WhatsappNumberInput.text));
    newBotSettings.TelegramNumberButton.transform.parent.gameObject.SetActive(useTelegram && !string.IsNullOrEmpty(TelegramNumberInput.text));

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
    PlayerPrefs.SetInt(newBot.name + "BusinessType", businessType.transform.GetSiblingIndex());
    PlayerPrefs.SetString(newBot.name + "WhatsappNumber", useWhatsapp ? WhatsappNumberInput.text : "");
    PlayerPrefs.SetString(newBot.name + "TelegramNumber", useTelegram ? TelegramNumberInput.text : "");

    PlayerPrefs.SetInt("ids", ++id);
    PlayerPrefs.Save();

    // Step 6: Reset form and navigate to bots tab
    ResetAddBotForm();
    isCreatingBot = false;

    BottomTabManager tabManager = FindObjectOfType<BottomTabManager>();
    if (tabManager != null)
    {
        tabManager.SwitchTab(3);
    }
}

private void CancelBotCreation()
{
    isCreatingBot = false;
    WhatsappAuth.SetActive(false);
    TelegramAuth.SetActive(false);

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
    businessType = BusinessTypesList[0].gameObject;

    if (platformValueText != null)
    {
        platformValueText.text = "Выберите";
        platformValueText.color = new Color32(142, 142, 147, 255);
    }
    if (platformIconImage != null) platformIconImage.gameObject.SetActive(false);
    if (botNameValueText != null)
    {
        botNameValueText.text = "Введите имя";
        botNameValueText.color = new Color32(142, 142, 147, 255);
    }
    if (businessTypeValueText != null)
    {
        businessTypeValueText.text = "Выберите тип";
        businessTypeValueText.color = new Color32(142, 142, 147, 255);
    }
    if (descriptionValueText != null)
    {
        descriptionValueText.text = "Необязательно";
        descriptionValueText.color = new Color32(199, 199, 204, 255);
    }

    WhatsappNumberInput.text = "";
    TelegramNumberInput.text = "";

    foreach (Button business in BusinessTypesList)
    {
        business.gameObject.GetComponent<Image>().color = businessButtonDefaultColor;
    }

    if (createBotFormButton != null) createBotFormButton.interactable = false;
}
```

---

### Task 4: Modify Manager.cs — Add Auth Completion Flags

**Files:**
- Modify: `Assets/Scripts/Main/Manager.cs` — `GetWhatsappProfileStatus()` and `GetTelegramProfileStatus()` methods

- [ ] **Step 1: Add auto-proceed flag in GetWhatsappProfileStatus**

In `GetWhatsappProfileStatus()`, after `WhatsappAuthContinueButton.interactable = true;` (around line 1391), add:

```csharp
whatsappAuthCompleted = true;
if (isCreatingBot)
{
    WhatsappAuth.SetActive(false);
}
```

- [ ] **Step 2: Add auto-proceed flag in GetTelegramProfileStatus**

In `GetTelegramProfileStatus()`, after `TelegramAuthContinueButton.interactable = true;` (around line 1987), add:

```csharp
telegramAuthCompleted = true;
if (isCreatingBot)
{
    TelegramAuth.SetActive(false);
}
```

---

### Task 5: Create Editor Script — AddBotPageSetup.cs

**Files:**
- Create: `Assets/Scripts/Editor/AddBotPageSetup.cs`

- [ ] **Step 1: Write the Editor script**

This script creates the full UI hierarchy under the Add Bot tab's screen panel when invoked via `Tools > Setup Add Bot Page`. It:

1. Finds the Manager component in the scene
2. Finds the Add Bot tab's screen panel (tab index 2 in BottomTabManager)
3. Clears the old wizard children (Chanel, Name, WhatsappAuth, TelegramAuth, Business, Summary, Confirmation)
4. Creates the new hierarchy:
   - **Header** — centered "Добавить Бота" title
   - **ScrollView** containing:
     - **HeroSection** — Image placeholder (robot), title TMP "Создайте нового бота", subtitle TMP
     - **FormCard** — rounded white panel with 4 FormRow buttons (Platform, BotName, BusinessType, Description). Each row: HorizontalLayout with label TMP + value area (icon Image + value TMP + chevron Image)
     - **CreateButton** — "Создать Бота" button (#007AFF, 14px corners, 17px padding)
   - **PlatformSelectorPanel** — overlay with 3 options (WhatsApp/Telegram/Both)
   - **BotNameInputPanel** — overlay with TMP_InputField + confirm/cancel buttons
   - **BusinessSelectorPanel** — overlay that contains the BusinessTypesList buttons
   - **DescriptionInputPanel** — overlay with multiline TMP_InputField + confirm/cancel buttons
5. Assigns all `SerializeField` references on the Manager component
6. Reassigns BotsPage's `Chanel` reference to `AddBotFormPage`
7. Marks scene as dirty for saving

**Colors used in the script** (from `Design/chat-app-ui.html`):

| Token | Hex | Usage |
|-------|-----|-------|
| `--ios-blue` | `#007AFF` | Create button background |
| `--bg` | `#F2F2F7` | Page background |
| `--white` | `#FFFFFF` | Cards, hero section |
| `--text-primary` | `#1C1C1E` | Main text, labels |
| `--text-secondary` | `#8E8E93` | Placeholder text, subtitle |
| `--text-tertiary` | `#C7C7CC` | Muted text ("Необязательно"), chevron |
| `--border` | `#E5E5EA` | Row separators |
| `--wa-green` | `#25D366` | WhatsApp platform color |
| `--tg-blue` | `#2AABEE` | Telegram platform color |

**Layout specs** (from CSS):

| Element | Spec |
|---------|------|
| Hero section | padding 24px 20px 16px, white bg, centered column |
| Hero title | 20px, weight 800, #1C1C1E, margin-top 14px |
| Hero subtitle | 14px, #8E8E93, centered, margin-top 6px |
| Form card | margin 10px 16px, white bg, 14px border-radius |
| Form row | padding 15px 16px, border-bottom 1px #E5E5EA, last row no border |
| Form label | 16px, #1C1C1E |
| Form value | 15px, #8E8E93 (placeholder), right-aligned with 6px gap |
| Chevron | 7x12, stroke #C7C7CC |
| Create button | width 100%-32px, margin 18px 16px, padding 17px, 14px radius, 17px font, weight 700, white text on #007AFF |

```csharp
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public class AddBotPageSetup : Editor
{
    // Color constants matching chat-app-ui.html
    static readonly Color32 IosBlue = new(0, 122, 255, 255);       // #007AFF
    static readonly Color32 BgColor = new(242, 242, 247, 255);     // #F2F2F7
    static readonly Color32 White = new(255, 255, 255, 255);
    static readonly Color32 TextPrimary = new(28, 28, 30, 255);    // #1C1C1E
    static readonly Color32 TextSecondary = new(142, 142, 147, 255); // #8E8E93
    static readonly Color32 TextTertiary = new(199, 199, 204, 255); // #C7C7CC
    static readonly Color32 Border = new(229, 229, 234, 255);      // #E5E5EA
    static readonly Color32 WaGreen = new(37, 211, 102, 255);      // #25D366
    static readonly Color32 TgBlue = new(42, 171, 238, 255);       // #2AABEE
    static readonly Color32 Overlay = new(0, 0, 0, 128);           // semi-transparent black

    [MenuItem("Tools/Setup Add Bot Page")]
    public static void Setup()
    {
        Manager manager = FindObjectOfType<Manager>();
        if (manager == null)
        {
            Debug.LogError("Manager not found in scene.");
            return;
        }

        BottomTabManager tabMgr = FindObjectOfType<BottomTabManager>();
        if (tabMgr == null)
        {
            Debug.LogError("BottomTabManager not found in scene.");
            return;
        }

        // Get the Canvas for sizing reference
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("Canvas not found in scene.");
            return;
        }

        // ── Create root page ──
        GameObject formPage = new GameObject("AddBotFormPage");
        formPage.transform.SetParent(canvas.transform, false);
        RectTransform formPageRT = formPage.AddComponent<RectTransform>();
        formPageRT.anchorMin = Vector2.zero;
        formPageRT.anchorMax = Vector2.one;
        formPageRT.offsetMin = Vector2.zero;
        formPageRT.offsetMax = Vector2.zero;
        Image formPageBg = formPage.AddComponent<Image>();
        formPageBg.color = White;

        // ── Header ──
        GameObject header = CreateChild(formPage, "Header", White);
        SetAnchorsTop(header, 50);
        TextMeshProUGUI headerTitle = CreateTMP(header, "HeaderTitle", "Добавить Бота", 18, FontStyles.Bold, TextPrimary);
        RectTransform headerTitleRT = headerTitle.GetComponent<RectTransform>();
        headerTitleRT.anchorMin = Vector2.zero;
        headerTitleRT.anchorMax = Vector2.one;
        headerTitleRT.offsetMin = new Vector2(34, 0);
        headerTitleRT.offsetMax = new Vector2(-34, 0);
        headerTitle.alignment = TextAlignmentOptions.Center;

        // ── ScrollView ──
        GameObject scrollView = CreateScrollView(formPage, "ScrollContent");

        // ── Hero Section ──
        GameObject hero = CreateChild(scrollView.transform.GetChild(0).GetChild(0).gameObject, "HeroSection");
        VerticalLayoutGroup heroVLG = hero.AddComponent<VerticalLayoutGroup>();
        heroVLG.childAlignment = TextAnchor.UpperCenter;
        heroVLG.spacing = 0;
        heroVLG.padding = new RectOffset(20, 20, 24, 16);
        heroVLG.childControlWidth = true;
        heroVLG.childControlHeight = false;
        heroVLG.childForceExpandWidth = true;
        heroVLG.childForceExpandHeight = false;
        ContentSizeFitter heroCSF = hero.AddComponent<ContentSizeFitter>();
        heroCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Robot placeholder image
        GameObject robotObj = new GameObject("RobotImage");
        robotObj.transform.SetParent(hero.transform, false);
        Image robotImg = robotObj.AddComponent<Image>();
        robotImg.color = new Color32(227, 242, 253, 255); // light blue placeholder
        LayoutElement robotLE = robotObj.AddComponent<LayoutElement>();
        robotLE.preferredWidth = 130;
        robotLE.preferredHeight = 148;

        // Hero title
        GameObject heroTitleObj = new GameObject("HeroTitle");
        heroTitleObj.transform.SetParent(hero.transform, false);
        TextMeshProUGUI heroTitle = heroTitleObj.AddComponent<TextMeshProUGUI>();
        heroTitle.text = "Создайте нового бота";
        heroTitle.fontSize = 20;
        heroTitle.fontStyle = FontStyles.Bold;
        heroTitle.color = TextPrimary;
        heroTitle.alignment = TextAlignmentOptions.Center;
        LayoutElement heroTitleLE = heroTitleObj.AddComponent<LayoutElement>();
        heroTitleLE.preferredHeight = 40;

        // Hero subtitle
        GameObject heroSubObj = new GameObject("HeroSubtitle");
        heroSubObj.transform.SetParent(hero.transform, false);
        TextMeshProUGUI heroSub = heroSubObj.AddComponent<TextMeshProUGUI>();
        heroSub.text = "Выберите платформу и настройте\nвашего помощника за несколько шагов.";
        heroSub.fontSize = 14;
        heroSub.color = TextSecondary;
        heroSub.alignment = TextAlignmentOptions.Center;
        LayoutElement heroSubLE = heroSubObj.AddComponent<LayoutElement>();
        heroSubLE.preferredHeight = 50;

        // ── Form Card ──
        GameObject formCard = CreateChild(scrollView.transform.GetChild(0).GetChild(0).gameObject, "FormCard");
        VerticalLayoutGroup formVLG = formCard.AddComponent<VerticalLayoutGroup>();
        formVLG.spacing = 0;
        formVLG.padding = new RectOffset(0, 0, 0, 0);
        formVLG.childControlWidth = true;
        formVLG.childControlHeight = false;
        formVLG.childForceExpandWidth = true;
        formVLG.childForceExpandHeight = false;
        ContentSizeFitter formCSF = formCard.AddComponent<ContentSizeFitter>();
        formCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        LayoutElement formCardLE = formCard.AddComponent<LayoutElement>();
        formCardLE.flexibleWidth = 1;
        Image formCardBg = formCard.AddComponent<Image>();
        formCardBg.color = White;
        // Note: rounded corners would need RoundedCorners shader — apply in Inspector

        // Form rows
        var (platformRow, platformValText, platformIcon) = CreateFormRow(formCard, "PlatformRow", "Платформа", "Выберите", true);
        var (nameRow, nameValText, _) = CreateFormRow(formCard, "BotNameRow", "Имя Бота", "Введите имя", false);
        var (businessRow, businessValText, _) = CreateFormRow(formCard, "BusinessTypeRow", "Тип бизнеса", "Выберите тип", false);
        var (descRow, descValText, _) = CreateFormRow(formCard, "DescriptionRow", "Описание", "Необязательно", false, true);
        descValText.color = TextTertiary;

        // ── Create Button ──
        GameObject btnObj = new GameObject("CreateBotButton");
        btnObj.transform.SetParent(scrollView.transform.GetChild(0).GetChild(0).gameObject.transform, false);
        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = IosBlue;
        Button createBtn = btnObj.AddComponent<Button>();
        LayoutElement btnLE = btnObj.AddComponent<LayoutElement>();
        btnLE.preferredHeight = 54;
        btnLE.flexibleWidth = 1;

        TextMeshProUGUI btnText = CreateTMP(btnObj, "ButtonText", "Создать Бота", 17, FontStyles.Bold, White);
        RectTransform btnTextRT = btnText.GetComponent<RectTransform>();
        btnTextRT.anchorMin = Vector2.zero;
        btnTextRT.anchorMax = Vector2.one;
        btnTextRT.offsetMin = Vector2.zero;
        btnTextRT.offsetMax = Vector2.zero;
        btnText.alignment = TextAlignmentOptions.Center;

        // ── Popup Panels ──
        GameObject platformPopup = CreatePopupPanel(formPage, "PlatformSelectorPanel", "Платформа");
        CreatePlatformOptions(platformPopup);
        GameObject namePopup = CreateInputPopup(formPage, "BotNameInputPanel", "Имя Бота", false, out TMP_InputField nameInput);
        GameObject businessPopup = CreatePopupPanel(formPage, "BusinessSelectorPanel", "Тип бизнеса");
        // BusinessTypesList buttons will be reparented here via Inspector
        GameObject descPopup = CreateInputPopup(formPage, "DescriptionInputPanel", "Описание", true, out TMP_InputField descInput);

        // ── Wire SerializeField references via SerializedObject ──
        SerializedObject so = new SerializedObject(manager);

        SetField(so, "AddBotFormPage", formPage);
        SetField(so, "platformValueText", platformValText);
        SetField(so, "platformIconImage", platformIcon);
        SetField(so, "botNameValueText", nameValText);
        SetField(so, "businessTypeValueText", businessValText);
        SetField(so, "descriptionValueText", descValText);
        SetField(so, "createBotFormButton", createBtn);
        SetField(so, "platformSelectorPanel", platformPopup);
        SetField(so, "botNameInputPanel", namePopup);
        SetField(so, "businessSelectorPanel", businessPopup);
        SetField(so, "descriptionInputPanel", descPopup);
        SetField(so, "botNamePopupInput", nameInput);
        SetField(so, "descriptionPopupInput", descInput);
        SetField(so, "platformRowButton", platformRow.GetComponent<Button>());
        SetField(so, "botNameRowButton", nameRow.GetComponent<Button>());
        SetField(so, "businessTypeRowButton", businessRow.GetComponent<Button>());
        SetField(so, "descriptionRowButton", descRow.GetComponent<Button>());

        // Platform option buttons
        Transform optionsParent = platformPopup.transform.Find("Content/Options");
        if (optionsParent != null)
        {
            SetField(so, "whatsappOptionButton", optionsParent.GetChild(0).GetComponent<Button>());
            SetField(so, "telegramOptionButton", optionsParent.GetChild(1).GetComponent<Button>());
            SetField(so, "bothOptionButton", optionsParent.GetChild(2).GetComponent<Button>());
        }

        so.ApplyModifiedProperties();

        // ── Update BottomTabManager tab 2 screen panel ──
        SerializedObject tabSO = new SerializedObject(tabMgr);
        SerializedProperty tabsProp = tabSO.FindProperty("tabs");
        if (tabsProp != null && tabsProp.arraySize > 2)
        {
            SerializedProperty tab2 = tabsProp.GetArrayElementAtIndex(2);
            SerializedProperty screenPanel = tab2.FindPropertyRelative("screenPanel");
            screenPanel.objectReferenceValue = formPage;
            tabSO.ApplyModifiedProperties();
        }

        // ── Update BotsPage Chanel reference ──
        BotsPage botsPage = FindObjectOfType<BotsPage>();
        if (botsPage != null)
        {
            SerializedObject bpSO = new SerializedObject(botsPage);
            SerializedProperty chanelProp = bpSO.FindProperty("Chanel");
            if (chanelProp != null)
            {
                chanelProp.objectReferenceValue = formPage;
                bpSO.ApplyModifiedProperties();
            }
        }

        EditorUtility.SetDirty(manager);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("Add Bot Page setup complete. Save the scene.");
    }

    // ── Helper: Create child with optional background ──
    static GameObject CreateChild(GameObject parent, string name, Color32? bgColor = null)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        if (bgColor.HasValue)
        {
            Image img = go.AddComponent<Image>();
            img.color = bgColor.Value;
        }
        return go;
    }

    // ── Helper: Create TMP text ──
    static TextMeshProUGUI CreateTMP(GameObject parent, string name, string text, float size, FontStyles style, Color32 color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.enableAutoSizing = false;
        return tmp;
    }

    // ── Helper: Set anchors to top strip ──
    static void SetAnchorsTop(GameObject go, float height)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, height);
        rt.anchoredPosition = Vector2.zero;
    }

    // ── Helper: Create ScrollView ──
    static GameObject CreateScrollView(GameObject parent, string name)
    {
        GameObject sv = new GameObject(name);
        sv.transform.SetParent(parent.transform, false);
        RectTransform svRT = sv.AddComponent<RectTransform>();
        svRT.anchorMin = Vector2.zero;
        svRT.anchorMax = Vector2.one;
        svRT.offsetMin = new Vector2(0, 0);
        svRT.offsetMax = new Vector2(0, -50); // below header
        ScrollRect scroll = sv.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        Image svMask = sv.AddComponent<Image>();
        svMask.color = new Color(1, 1, 1, 0);
        sv.AddComponent<Mask>().showMaskGraphic = false;

        // Viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(sv.transform, false);
        RectTransform vpRT = viewport.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = Vector2.zero;
        vpRT.offsetMax = Vector2.zero;

        // Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        RectTransform cRT = content.AddComponent<RectTransform>();
        cRT.anchorMin = new Vector2(0, 1);
        cRT.anchorMax = Vector2.one;
        cRT.pivot = new Vector2(0.5f, 1);
        cRT.sizeDelta = new Vector2(0, 0);
        VerticalLayoutGroup vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 0, 20);
        vlg.spacing = 0;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vpRT;
        scroll.content = cRT;

        return sv;
    }

    // ── Helper: Create form row → returns (rowGO, valueText, iconImage) ──
    static (GameObject, TextMeshProUGUI, Image) CreateFormRow(GameObject parent, string name, string label, string placeholder, bool hasIcon, bool isLast = false)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent.transform, false);
        Image rowBg = row.AddComponent<Image>();
        rowBg.color = White;
        row.AddComponent<Button>();
        HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 16, 0, 0);
        hlg.spacing = 6;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;
        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 50;
        rowLE.flexibleWidth = 1;

        // Separator line (bottom border) — skip for last row
        if (!isLast)
        {
            GameObject sep = new GameObject("Separator");
            sep.transform.SetParent(row.transform, false);
            RectTransform sepRT = sep.AddComponent<RectTransform>();
            sepRT.anchorMin = new Vector2(0.04f, 0);
            sepRT.anchorMax = new Vector2(0.96f, 0);
            sepRT.sizeDelta = new Vector2(0, 1);
            sepRT.anchoredPosition = Vector2.zero;
            Image sepImg = sep.AddComponent<Image>();
            sepImg.color = Border;
        }

        // Label
        TextMeshProUGUI labelTMP = CreateTMP(row, "Label", label, 16, FontStyles.Normal, TextPrimary);
        LayoutElement labelLE = labelTMP.gameObject.AddComponent<LayoutElement>();
        labelLE.preferredWidth = 120;
        labelLE.preferredHeight = 50;
        labelTMP.alignment = TextAlignmentOptions.MidlineLeft;

        // Spacer
        GameObject spacer = new GameObject("Spacer");
        spacer.transform.SetParent(row.transform, false);
        spacer.AddComponent<RectTransform>();
        LayoutElement spacerLE = spacer.AddComponent<LayoutElement>();
        spacerLE.flexibleWidth = 1;

        // Icon (optional, for platform row)
        Image iconImg = null;
        if (hasIcon)
        {
            GameObject icon = new GameObject("PlatformIcon");
            icon.transform.SetParent(row.transform, false);
            iconImg = icon.AddComponent<Image>();
            iconImg.color = WaGreen;
            LayoutElement iconLE = icon.AddComponent<LayoutElement>();
            iconLE.preferredWidth = 15;
            iconLE.preferredHeight = 15;
            icon.SetActive(false);
        }

        // Value text
        TextMeshProUGUI valTMP = CreateTMP(row, "ValueText", placeholder, 15, FontStyles.Normal, TextSecondary);
        LayoutElement valLE = valTMP.gameObject.AddComponent<LayoutElement>();
        valLE.preferredWidth = 140;
        valLE.preferredHeight = 50;
        valTMP.alignment = TextAlignmentOptions.MidlineRight;

        // Chevron
        GameObject chevron = new GameObject("Chevron");
        chevron.transform.SetParent(row.transform, false);
        Image chevImg = chevron.AddComponent<Image>();
        chevImg.color = TextTertiary;
        LayoutElement chevLE = chevron.AddComponent<LayoutElement>();
        chevLE.preferredWidth = 7;
        chevLE.preferredHeight = 12;

        return (row, valTMP, iconImg);
    }

    // ── Helper: Create popup overlay panel ──
    static GameObject CreatePopupPanel(GameObject parent, string name, string title)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent.transform, false);
        RectTransform panelRT = panel.AddComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = Overlay;

        // Content card
        GameObject card = CreateChild(panel, "Content", White);
        RectTransform cardRT = card.GetComponent<RectTransform>();
        cardRT.anchorMin = new Vector2(0.05f, 0.3f);
        cardRT.anchorMax = new Vector2(0.95f, 0.7f);
        cardRT.offsetMin = Vector2.zero;
        cardRT.offsetMax = Vector2.zero;
        VerticalLayoutGroup vlg = card.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 20, 20);
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // Title
        CreateTMP(card, "Title", title, 18, FontStyles.Bold, TextPrimary);

        panel.SetActive(false);
        return panel;
    }

    // ── Helper: Create platform option buttons ──
    static void CreatePlatformOptions(GameObject popup)
    {
        Transform content = popup.transform.Find("Content");
        GameObject options = new GameObject("Options");
        options.transform.SetParent(content, false);
        VerticalLayoutGroup vlg = options.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        ContentSizeFitter csf = options.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateOptionButton(options, "WhatsAppOption", "WhatsApp", WaGreen);
        CreateOptionButton(options, "TelegramOption", "Telegram", TgBlue);
        CreateOptionButton(options, "BothOption", "WhatsApp + Telegram", IosBlue);
    }

    static void CreateOptionButton(GameObject parent, string name, string text, Color32 color)
    {
        GameObject btn = new GameObject(name);
        btn.transform.SetParent(parent.transform, false);
        Image img = btn.AddComponent<Image>();
        img.color = color;
        btn.AddComponent<Button>();
        LayoutElement le = btn.AddComponent<LayoutElement>();
        le.preferredHeight = 48;

        TextMeshProUGUI tmp = CreateTMP(btn, "Text", text, 16, FontStyles.Bold, White);
        RectTransform tmpRT = tmp.GetComponent<RectTransform>();
        tmpRT.anchorMin = Vector2.zero;
        tmpRT.anchorMax = Vector2.one;
        tmpRT.offsetMin = Vector2.zero;
        tmpRT.offsetMax = Vector2.zero;
        tmp.alignment = TextAlignmentOptions.Center;
    }

    // ── Helper: Create input popup ──
    static GameObject CreateInputPopup(GameObject parent, string name, string title, bool multiline, out TMP_InputField inputField)
    {
        GameObject panel = CreatePopupPanel(parent, name, title);
        Transform content = panel.transform.Find("Content");

        // Input field
        GameObject inputObj = new GameObject("InputField");
        inputObj.transform.SetParent(content, false);
        Image inputBg = inputObj.AddComponent<Image>();
        inputBg.color = BgColor;
        inputField = inputObj.AddComponent<TMP_InputField>();
        LayoutElement inputLE = inputObj.AddComponent<LayoutElement>();
        inputLE.preferredHeight = multiline ? 100 : 48;

        // Text area
        GameObject textArea = new GameObject("TextArea");
        textArea.transform.SetParent(inputObj.transform, false);
        RectTransform taRT = textArea.AddComponent<RectTransform>();
        taRT.anchorMin = Vector2.zero;
        taRT.anchorMax = Vector2.one;
        taRT.offsetMin = new Vector2(10, 5);
        taRT.offsetMax = new Vector2(-10, -5);

        // Input text
        TextMeshProUGUI inputText = CreateTMP(textArea, "Text", "", 16, FontStyles.Normal, TextPrimary);
        RectTransform inputTextRT = inputText.GetComponent<RectTransform>();
        inputTextRT.anchorMin = Vector2.zero;
        inputTextRT.anchorMax = Vector2.one;
        inputTextRT.offsetMin = Vector2.zero;
        inputTextRT.offsetMax = Vector2.zero;

        // Placeholder
        TextMeshProUGUI placeholder = CreateTMP(textArea, "Placeholder", title + "...", 16, FontStyles.Italic, TextTertiary);
        RectTransform phRT = placeholder.GetComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero;
        phRT.anchorMax = Vector2.one;
        phRT.offsetMin = Vector2.zero;
        phRT.offsetMax = Vector2.zero;

        inputField.textComponent = inputText;
        inputField.placeholder = placeholder;
        inputField.textViewport = taRT;
        if (multiline) inputField.lineType = TMP_InputField.LineType.MultiLineSubmit;

        // Confirm button
        GameObject confirmBtn = new GameObject("ConfirmButton");
        confirmBtn.transform.SetParent(content, false);
        Image confirmBg = confirmBtn.AddComponent<Image>();
        confirmBg.color = IosBlue;
        confirmBtn.AddComponent<Button>();
        LayoutElement confirmLE = confirmBtn.AddComponent<LayoutElement>();
        confirmLE.preferredHeight = 44;

        TextMeshProUGUI confirmText = CreateTMP(confirmBtn, "Text", "Готово", 16, FontStyles.Bold, White);
        RectTransform ctRT = confirmText.GetComponent<RectTransform>();
        ctRT.anchorMin = Vector2.zero;
        ctRT.anchorMax = Vector2.one;
        ctRT.offsetMin = Vector2.zero;
        ctRT.offsetMax = Vector2.zero;
        confirmText.alignment = TextAlignmentOptions.Center;

        // Cancel button
        GameObject cancelBtn = new GameObject("CancelButton");
        cancelBtn.transform.SetParent(content, false);
        Image cancelBg = cancelBtn.AddComponent<Image>();
        cancelBg.color = new Color32(242, 242, 247, 255);
        cancelBtn.AddComponent<Button>();
        LayoutElement cancelLE = cancelBtn.AddComponent<LayoutElement>();
        cancelLE.preferredHeight = 44;

        TextMeshProUGUI cancelText = CreateTMP(cancelBtn, "Text", "Отмена", 16, FontStyles.Normal, IosBlue);
        RectTransform canRT = cancelText.GetComponent<RectTransform>();
        canRT.anchorMin = Vector2.zero;
        canRT.anchorMax = Vector2.one;
        canRT.offsetMin = Vector2.zero;
        canRT.offsetMax = Vector2.zero;
        cancelText.alignment = TextAlignmentOptions.Center;

        return panel;
    }

    // ── Helper: Set serialized field value ──
    static void SetField(SerializedObject so, string fieldName, Object value)
    {
        SerializedProperty prop = so.FindProperty(fieldName);
        if (prop != null)
        {
            prop.objectReferenceValue = value;
        }
        else
        {
            Debug.LogWarning($"Field '{fieldName}' not found on Manager.");
        }
    }
}
#endif
```

- [ ] **Step 2: Commit Tasks 1-5**

```bash
git add Assets/Scripts/Main/Manager.cs Assets/Scripts/Editor/AddBotPageSetup.cs
git commit -m "feat: redesign Add Bot page to single-page form

Replace 6-step wizard with a single scrollable form (Platform, Bot Name,
Business Type, Description). Auth panels shown as overlays during creation.
All backend logic preserved."
```

---

### Task 6: Run Editor Script and Wire Scene

This task is performed in the Unity Editor, not via code.

- [ ] **Step 1: Open Unity, run Tools > Setup Add Bot Page**

The script creates the entire UI hierarchy and wires `SerializeField` references.

- [ ] **Step 2: Verify Manager Inspector**

All new fields should be populated. Check that:
- `AddBotFormPage` is assigned
- All form row buttons, value texts, popup panels, and input fields are assigned
- Platform option buttons (whatsapp/telegram/both) are assigned
- `createBotFormButton` is assigned

- [ ] **Step 3: Wire popup confirm/cancel buttons**

The Editor script creates Confirm and Cancel buttons in each popup, but they need onClick listeners. In Manager.cs Start() the row buttons are wired but the popup confirm/cancel buttons need manual wiring via Inspector or code addition:

In `Start()`, add after the popup hide block:

```csharp
// Wire popup confirm/cancel buttons
if (botNameInputPanel != null)
{
    Button confirmName = botNameInputPanel.transform.Find("Content/ConfirmButton")?.GetComponent<Button>();
    Button cancelName = botNameInputPanel.transform.Find("Content/CancelButton")?.GetComponent<Button>();
    if (confirmName != null) confirmName.onClick.AddListener(ConfirmBotName);
    if (cancelName != null) cancelName.onClick.AddListener(CloseBotNameInput);
}
if (descriptionInputPanel != null)
{
    Button confirmDesc = descriptionInputPanel.transform.Find("Content/ConfirmButton")?.GetComponent<Button>();
    Button cancelDesc = descriptionInputPanel.transform.Find("Content/CancelButton")?.GetComponent<Button>();
    if (confirmDesc != null) confirmDesc.onClick.AddListener(ConfirmDescription);
    if (cancelDesc != null) cancelDesc.onClick.AddListener(CloseDescriptionInput);
}
```

- [ ] **Step 4: Reparent BusinessTypesList into businessSelectorPanel**

In Unity Inspector, move the existing BusinessTypesList button objects to be children of `BusinessSelectorPanel > Content`. This reuses the existing buttons in the new popup context.

- [ ] **Step 5: Reassign BotsPage.Chanel**

Verify that the Editor script correctly reassigned `BotsPage.Chanel` to `AddBotFormPage`. If not, manually drag `AddBotFormPage` into the `Chanel` slot on `BotsPage` in the Inspector.

- [ ] **Step 6: Hide old wizard panels**

Deactivate or delete the old wizard step GameObjects in the scene hierarchy:
- Old `Chanel` panel (platform toggle step)
- Old `Name` panel
- Old `Business` panel
- Old `Summary` panel
- Old `Confirmation` panel

Keep `WhatsappAuth` and `TelegramAuth` — they're still used as auth overlays.

- [ ] **Step 7: Add robot illustration sprite**

Create or import a robot illustration sprite for the hero section. Assign it to `AddBotFormPage > ScrollContent > Viewport > Content > HeroSection > RobotImage` Image component. If no sprite is available, the light blue placeholder rectangle serves as fallback.

- [ ] **Step 8: Apply rounded corners**

Apply the `RoundedCorners` shader (from the existing UPM package) to:
- FormCard background (14px radius)
- CreateBotButton background (14px radius)
- Popup panel content cards (14px radius)

- [ ] **Step 9: Save the scene and test**

Save `Main.unity`. Enter Play mode. Verify:
- Tab 2 shows the new Add Bot form page
- Tapping each form row opens the correct popup
- Selecting a platform updates the row value + color
- Entering a bot name updates the row value
- Selecting a business type updates the row value
- Create button enables only when Platform + Name + Business Type are set
- Tapping Create starts the creation flow (shows auth panel if needed)
- After auth + workflow creation, navigates to Bots tab
- Bots page "New Bot" button navigates to the Add Bot form
- Cancelling during auth cleans up profiles

- [ ] **Step 10: Commit scene changes**

```bash
git add Assets/Scenes/Main.unity
git commit -m "scene: wire new Add Bot form UI hierarchy"
```

---

## Verification Checklist

After all tasks are complete, verify each of these:

- [ ] Platform selection (WhatsApp / Telegram / Both) works and shows correct colored text
- [ ] Bot name input works and shows entered name on the form row
- [ ] Business type selection reuses existing buttons and updates the form row
- [ ] Description input works (optional, shows "Необязательно" when empty)
- [ ] Create button is disabled until Platform + Name + Business Type are filled
- [ ] WhatsApp auth overlay appears during creation when WhatsApp is selected
- [ ] Telegram auth overlay appears after WhatsApp auth when Both is selected
- [ ] QR code and phone code auth methods still work within overlays
- [ ] Auth auto-proceeds when profile status polling detects authorization
- [ ] Cancel during auth deletes created profiles
- [ ] Bot is instantiated with correct data after creation
- [ ] Workflows are created via n8n webhooks
- [ ] PlayerPrefs are saved correctly
- [ ] Navigation to Bots tab works after creation
- [ ] BotsPage "New Bot" button navigates to Add Bot form
- [ ] Existing bots load correctly on app start
- [ ] BotSettings editing still works (SaveSettings, CloseSettings, etc.)
- [ ] No compile errors or null reference exceptions

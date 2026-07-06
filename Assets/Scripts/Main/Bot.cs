using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;
using TMPro;

public class Bot : MonoBehaviour
{
    [SerializeField] public TextMeshProUGUI BotName;
    [SerializeField] public TextMeshProUGUI BotDesc;
    [SerializeField] public TextMeshProUGUI Status;
    [SerializeField] public Button EditButton;
    [SerializeField] public Toggle ActivationSwitch;

    [Tooltip("Footer caption under the divider: «Бот работает» / «Бот на паузе». " +
             "Wired by BotCardFooterBuilder.")]
    [SerializeField] private TextMeshProUGUI SwitchFooterLabel;

    [SerializeField] private Color backgroundActiveColor;
    [SerializeField] private Color handleActiveColor;

    [Header("Business Icon")]
    [SerializeField] private Image BotIconTile;
    [SerializeField] private Image BotIconImage;
    [SerializeField] private BusinessTypesSO businessTypes;

    /// <summary>
    /// Light gray fallback used by the BotsPage card and BotSwitcher avatar
    /// surfaces when a bot has no business type set. Single source of truth
    /// so designer tweaks land in one place.
    /// </summary>
    public static readonly Color NeutralTile = new Color(0.85f, 0.85f, 0.85f);

    /// <summary>
    /// Returns the bot's business icon sprite, or null when no business type
    /// is set (mid-wizard) or the SO has no entry for the saved id. Cheap —
    /// PlayerPrefs read + dictionary lookup; safe to call from OnEnable.
    /// </summary>
    public Sprite GetBusinessIconSprite()
    {
        if (businessTypes == null) return null;
        var id = PlayerPrefs.GetString(transform.name + "BusinessType", "");
        if (string.IsNullOrEmpty(id)) return null;
        return businessTypes.TryGetById(id, out var entry) ? entry.sprite : null;
    }

    /// <summary>
    /// Returns the bot's business icon tile color, or NeutralTile when no
    /// business type is set or the SO has no matching entry. Callers can
    /// always assign the result to an Image.color without null-checking.
    /// </summary>
    public Color GetBusinessIconTint()
    {
        if (businessTypes == null) return NeutralTile;
        var id = PlayerPrefs.GetString(transform.name + "BusinessType", "");
        if (string.IsNullOrEmpty(id)) return NeutralTile;
        return businessTypes.TryGetById(id, out var entry) ? entry.tileColor : NeutralTile;
    }


    public bool active = false;

    /// <summary>
    /// Sentinel value used as the default for whatsappProfileId/telegramProfileId
    /// when a bot has not yet completed auth. Treated as "no profile" by ChatManager.
    /// </summary>
    public const string UnauthedProfileSentinel = "-1";

    public string whatsappProfileId;
    public string telegramProfileId;

    public string whatsappWorkflowId;
    public string telegramWorkflowId;

    private RectTransform switchHandle;
    private Image switchBackgroundImage, switchHandleImage;
    private Color backgroundDefaultColor, handleDefaultColor;
    private Vector2 switchHandlePosition;

    private Color green = new(0, 1, 0);
    private Color red = new(1, 0, 0);
    private Color blue = new(0, 0.6980392f, 1);


    private void Awake ()
    {
        StartCoroutine(SetSwitches());
        ApplyBusinessIcon();


        ActivationSwitch.onValueChanged.AddListener(EnableBot);

        if (EditButton != null)
        {
            EditButton.onClick.AddListener(OpenSettings);
        }
    }


    private void OpenSettings()
    {
        // Keep BotsPage active during the slide-in so its parallax is visible.
        // It is deactivated in the slide-in onComplete callback below.
        Manager.BotSettingsParentStatic.transform.parent.gameObject.SetActive(true);

        SwipeToBackBotSettings activeSwipe = null;

        if (Manager.BotSettingsParentStatic.transform.childCount != 0)
        {
            foreach (Transform botSettings in Manager.BotSettingsParentStatic.transform)
            {
                if (botSettings.GetSiblingIndex() == transform.GetSiblingIndex())
                {
                    botSettings.gameObject.SetActive(true);
                    Manager.openBot = gameObject;
                    Manager.openBotSettings = botSettings.gameObject.GetComponent<BotSettings>();

                    // SetActive above fired BotSettings.OnEnable BEFORE the two
                    // assignments, so its RefreshUploadedFiles saw a null/stale
                    // openBot and hid the "Прайс-листы" section. Re-run now that
                    // the pairing is authoritative (same pattern as
                    // RefreshBusinessIcon: Manager writes, then explicit refresh).
                    if (Manager.openBotSettings != null)
                        Manager.openBotSettings.RefreshUploadedFiles();

                    // Each BotSettings prefab has its own SwipeBack child. Resolve
                    // the right one explicitly instead of relying on the static
                    // Instance — the cascade activation when the wrapper turns on
                    // can fire OnEnable on multiple SwipeBacks and the last one
                    // wins the singleton, even if it is about to be deactivated
                    // below by the non-matching branch.
                    activeSwipe = botSettings.GetComponentInChildren<SwipeToBackBotSettings>(includeInactive: true);
                    if (activeSwipe != null && !activeSwipe.gameObject.activeSelf)
                        activeSwipe.gameObject.SetActive(true);
                }
                else
                {
                    botSettings.gameObject.SetActive(false);
                }
            }
        }

        if (activeSwipe != null)
        {
            // Authoritative singleton update — supersedes any OnEnable assignment
            // that fired during the cascade above.
            SwipeToBackBotSettings.Instance = activeSwipe;
            activeSwipe.SlideInFromRight(() =>
            {
                if (BotsPage.Instance != null)
                    BotsPage.Instance.gameObject.SetActive(false);
            });
        }
        else
        {
            Debug.LogWarning("[Bot.OpenSettings] No SwipeToBackBotSettings found on the " +
                             "matching BotSettings — falling back to instant open. " +
                             "Run Tools/Bot Settings/Wire Swipe Back.");
            if (BotsPage.Instance != null) BotsPage.Instance.gameObject.SetActive(false);
        }
    }

    // Made public so BotSettings' in-page Delete flow can reuse the exact
    // same teardown (PlayerPrefs cleanup + profile/workflow deletes + destroy
    // both the Bot card and its paired BotSettings GameObject).
    public void DeleteBot()
    {
        if (PlayerPrefs.HasKey(transform.name + "Name"))
        {
            PlayerPrefs.DeleteKey(transform.name + "Name");
            PlayerPrefs.DeleteKey(transform.name + "isOn");
            PlayerPrefs.DeleteKey(transform.name + "Status");
            PlayerPrefs.DeleteKey(transform.name + "Active");
            PlayerPrefs.DeleteKey(transform.name + "isOnWhatsapp");
            PlayerPrefs.DeleteKey(transform.name + "isOnTelegram");
            PlayerPrefs.DeleteKey(transform.name + "BusinessType");
            PlayerPrefs.DeleteKey(transform.name + "WhatsappNumber");
            PlayerPrefs.DeleteKey(transform.name + "TelegramNumber");
            PlayerPrefs.DeleteKey(transform.name + "Business");
            PlayerPrefs.DeleteKey(transform.name + "Prompt");
            PlayerPrefs.DeleteKey(transform.name + "WhatsappWorkflowId");
            PlayerPrefs.DeleteKey(transform.name + "WhatsappProfileId");
            PlayerPrefs.DeleteKey(transform.name + "TelegramWorkflowId");
            PlayerPrefs.DeleteKey(transform.name + "TelegramProfileId");
            PlayerPrefs.DeleteKey(transform.name + "WhatsappSyncUntil");
            PlayerPrefs.DeleteKey(transform.name + "ReplyMode");

            if (PlayerPrefs.GetInt(transform.name + "ProductsNumber", 0) > 0)
            {
                for (int p = 0; p < PlayerPrefs.GetInt(transform.name + "ProductsNumber", 0); p++)
                {
                    if (PlayerPrefs.HasKey(transform.name + "Product" + p))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Product" + p);
                    }

                    if (PlayerPrefs.HasKey(transform.name + "Product" + p + "Price"))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Product" + p + "Price");
                    }

                    if (PlayerPrefs.HasKey(transform.name + "Product" + p + "Description"))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Product" + p + "Description");
                    }
                }
            }

            PlayerPrefs.DeleteKey(transform.name + "ProductsNumber");

            if (PlayerPrefs.GetInt(transform.name + "ServicesNumber", 0) > 0)
            {
                for (int s = 0; s < PlayerPrefs.GetInt(transform.name + "ServicesNumber", 0); s++)
                {
                    if (PlayerPrefs.HasKey(transform.name + "Service" + s))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Service" + s);
                    }

                    if (PlayerPrefs.HasKey(transform.name + "Service" + s + "Price"))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Service" + s + "Price");
                    }

                    if (PlayerPrefs.HasKey(transform.name + "Service" + s + "Description"))
                    {
                        PlayerPrefs.DeleteKey(transform.name + "Service" + s + "Description");
                    }
                }

            }

            PlayerPrefs.DeleteKey(transform.name + "ServicesNumber");

            UploadedFilesStore.Clear(transform.name, "product");
            UploadedFilesStore.Clear(transform.name, "service");
        }

        if (ChatManager.Instance != null)
        {
            ChatManager.Instance.PurgeCacheForBot(transform.name);
        }

        // Sweep the bot's price-list knowledge from Supabase BEFORE the
        // workflow ids stop meaning anything — chunks are tagged by these ids
        // and nothing could clean them up after the bot is gone.
        Manager.Instance.DeleteBotFilesOnServer(whatsappWorkflowId, telegramWorkflowId);

        Manager.Instance.DeleteProfilesAndWorkflows(whatsappProfileId, telegramProfileId, whatsappWorkflowId, telegramWorkflowId);

        Destroy(Manager.BotSettingsParentStatic.transform.GetChild(transform.GetSiblingIndex()).gameObject);
        Destroy(gameObject);
    }

    private void EnableBot (bool enabled)
    {
        switchHandle.DOAnchorPos (enabled ? switchHandlePosition * -1 : switchHandlePosition, .4f).SetEase (Ease.InOutBack);
        switchBackgroundImage.DOColor (enabled ? backgroundActiveColor : backgroundDefaultColor, .6f);
        switchHandleImage.DOColor (enabled ? handleActiveColor : handleDefaultColor, .4f);

        ApplySwitchFooterLabel(enabled);

        PlayerPrefs.SetInt(transform.name, enabled ? 1 : 0);
        Status.text = enabled ? active ? "Active" : "Connecting.." : "Not Active";
        Status.color = enabled ? active ? green : blue : red;
        
        Manager.Instance.GetEnableWhatsappWorkflow(whatsappWorkflowId, enabled);
        Manager.Instance.GetEnableTelegramWorkflow(telegramWorkflowId, enabled);
    }

    private void ApplySwitchFooterLabel(bool isOn)
    {
        if (SwitchFooterLabel == null) return;

        SwitchFooterLabel.text = BotSwitchFooter.TextFor(isOn);
        SwitchFooterLabel.color = BotSwitchFooter.ColorFor(isOn);
    }

    private IEnumerator SetSwitches()
    {
        yield return new WaitForEndOfFrame();

        switchHandle = ActivationSwitch.transform.GetChild(0).GetChild(0).GetComponent<RectTransform>();

        var track = ActivationSwitch.transform.GetChild(0).GetComponent<RectTransform>();
        switchHandle.localPosition = new Vector2(
            -BotSwitchFooter.RestOffset(track.rect.width, switchHandle.rect.width),
            switchHandle.localPosition.y);

        switchHandlePosition = switchHandle.anchoredPosition;

        switchBackgroundImage = switchHandle.parent.GetComponent<Image>();
        switchHandleImage = switchHandle.GetComponent<Image>();

        backgroundDefaultColor = switchBackgroundImage.color;
        handleDefaultColor = switchHandleImage.color;

        if (PlayerPrefs.GetInt(transform.name, 1) == 1)
        {
            ActivationSwitch.isOn = true;

            switchHandle.DOAnchorPos(switchHandlePosition * -1, .4f).SetEase(Ease.InOutBack);
            switchBackgroundImage.DOColor(backgroundActiveColor, .6f);
            switchHandleImage.DOColor(handleActiveColor, .4f);

            ApplySwitchFooterLabel(true);

            if (active)
            {
                Status.text = "Active";
                Status.color = green;
            }
            else
            {
                Status.text = "Connecting..";
                Status.color = blue;
            }
        }
        else
        {
            ActivationSwitch.isOn = false;
            Status.text = "Not Active";
            Status.color = red;

            ApplySwitchFooterLabel(false);
        }
    }

    public void RefreshBusinessIcon() => ApplyBusinessIcon();

    private void ApplyBusinessIcon()
    {
        if (businessTypes == null) return;

        var id = PlayerPrefs.GetString(transform.name + "BusinessType", "");
        // Empty id is expected when Awake fires before the Manager has renamed
        // the instantiated bot and written PlayerPrefs. Manager calls
        // RefreshBusinessIcon() explicitly once both are done.
        if (string.IsNullOrEmpty(id)) return;

        if (!businessTypes.TryGetById(id, out var entry))
        {
            Debug.LogWarning($"[Bot] No business type entry for id '{id}' on '{transform.name}'");
            return;
        }

        if (BotIconImage != null && entry.sprite != null) BotIconImage.sprite = entry.sprite;
        if (BotIconTile != null) BotIconTile.color = entry.tileColor;
    }

    private void OnDestroy ()
    {
        ActivationSwitch.onValueChanged.RemoveListener (EnableBot);
    }
}

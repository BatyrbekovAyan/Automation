using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;
using TMPro;

public class Bot : MonoBehaviour
{
    [SerializeField] public TextMeshProUGUI BotName;

    [Tooltip("C2 subline label: the business type name, or the blinking «Подключение…». " +
             "Bot owns its text and color now — Manager no longer writes it directly.")]
    [SerializeField] public TextMeshProUGUI BotDesc;

    [SerializeField] public TextMeshProUGUI Status;
    [SerializeField] public Button EditButton;

    [Header("Auto capsule (C2 card — wired by BotCardAutoPillBuilder; drives the bot's ReplyMode)")]
    [SerializeField] private Button autoPillButton;
    [SerializeField] private Image autoPillRing;
    [SerializeField] private Image autoPillFill;
    [SerializeField] private TextMeshProUGUI autoPillLabel;
    [SerializeField] private Image autoPillDotRing;
    [SerializeField] private Image autoPillDotCore;

    [Header("Channel icons (C2 subline — ready-made sprites, NEVER tinted)")]
    [SerializeField] private Image waChannelIcon;
    [SerializeField] private Image tgChannelIcon;

    [Tooltip("Brand-colored sprite for a connected + enabled channel.")]
    [SerializeField] private Sprite waIconColored;
    [SerializeField] private Sprite tgIconColored;

    [Tooltip("Ready-made gray sprite for a channel that is connected but toggled off " +
             "in Bot Settings. A tint would muddy the multi-color logos — swap, don't color.")]
    [SerializeField] private Sprite waIconGray;
    [SerializeField] private Sprite tgIconGray;

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

    private Color green = new(0, 1, 0);
    private Color blue = new(0, 0.6980392f, 1);

    // Display blue of the blinking «Подключение…» word (the retired status
    // pill's FgConnecting; the hidden Status data channel keeps its own blue).
    private static readonly Color ConnectingInk = new Color32(0x00, 0x7A, 0xFF, 0xFF);

    private Tween sublineBlink;
    private Color lastObservedStatusColor = new(-1f, -1f, -1f);
    private bool cardStateReady;


    private void Awake ()
    {
        StartCoroutine(InitCardState());
        ApplyBusinessIcon();

        if (autoPillButton != null)
            autoPillButton.onClick.AddListener(OnAutoPillTapped);

        if (EditButton != null)
        {
            EditButton.onClick.AddListener(OpenSettings);
        }
    }

    private void OnEnable()
    {
        Theme.Changed += HandleThemeChanged;
        // A mode committed elsewhere (chats header, bot-switcher chip) must
        // repaint this card — all three controls share the bot's ReplyMode.
        ReplyModeToggleBinder.OnReplyModeChanged += HandleReplyModeChanged;
        // Returning to the Боты tab after Bot Settings: the channel toggles or
        // the business type may have changed while this card was inactive.
        if (cardStateReady) RefreshCardState(animatePill: false);
    }

    private void OnDisable()
    {
        Theme.Changed -= HandleThemeChanged;
        ReplyModeToggleBinder.OnReplyModeChanged -= HandleReplyModeChanged;
        KillSublineBlink();
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
                    // RefreshPromptSuggestions has the same dependency: it reads
                    // openBot's BusinessType to bind the suggestion cloud, and
                    // this is the only call on the FIRST open (CloseSettings
                    // covers later cycles).
                    if (Manager.openBotSettings != null)
                    {
                        Manager.openBotSettings.RefreshUploadedFiles();
                        Manager.openBotSettings.RefreshPromptSuggestions();
                    }

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

    // Public entry for onboarding deep-links (success CTA «Загрузить прайс-лист» +
    // «Первые шаги» row 3). Reuses the exact Edit-button open path, then selects the
    // Product tab which hosts «Прайс-листы» (BotSettings has no separate Files tab).
    public void OpenSettingsAtProductTab()
    {
        OpenSettings();
        if (Manager.openBotSettings != null) Manager.openBotSettings.OpenProductTab();
    }

    // Public entry for the «Первые шаги» «Подключить {channel}» row (row 2). Same open
    // path, then the General tab — where the WhatsApp/Telegram connect toggles live
    // (BotSettings.cs:403).
    public void OpenSettingsAtGeneralTab()
    {
        OpenSettings();
        if (Manager.openBotSettings != null) Manager.openBotSettings.OpenGeneralTab();
    }

    // Made public so BotSettings' in-page Delete flow can reuse the exact
    // same teardown (PlayerPrefs cleanup + profile/workflow deletes + destroy
    // both the Bot card and its paired BotSettings GameObject).
    public void DeleteBot()
    {
        if (PlayerPrefs.HasKey(transform.name + "Name"))
        {
            // IN-01: activation state lives under TWO keys — the bare bot name (written by
            // EnableBot, read by SetSwitches) and "{name}isOn" (written at creation, read by
            // LoadBots). Only the latter was deleted, so the bare key leaked on every per-bot
            // delete and was cleaned only by the full PlayerPrefs.DeleteAll() wipe. Harmless
            // today because slot names are never reused (the "ids" counter is monotonic), but
            // it is exactly the key drift the bot-persistence skill warns about.
            PlayerPrefs.DeleteKey(transform.name);
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
            foreach (var contactKey in BotSettings.ContactKeys)
                PlayerPrefs.DeleteKey(transform.name + contactKey);
            PlayerPrefs.DeleteKey(transform.name + "WhatsappWorkflowId");
            PlayerPrefs.DeleteKey(transform.name + "WhatsappProfileId");
            PlayerPrefs.DeleteKey(transform.name + "TelegramWorkflowId");
            PlayerPrefs.DeleteKey(transform.name + "TelegramProfileId");
            PlayerPrefs.DeleteKey(transform.name + "WhatsappSyncUntil");
            PlayerPrefs.DeleteKey(transform.name + "TelegramSyncUntil");
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

        // Uploads outlive this screen and this bot — drop any still in flight
        // so a late completion can't rewrite the keys just deleted above.
        if (UploadCenter.Existing != null) UploadCenter.Existing.CancelForBot(transform.name);

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

        // Destroy() only takes effect after the current update loop, so a deleted card is
        // still a live child of BotsParent for the rest of the frame — and EVERY "has bots"
        // fact in the app is a childCount read (BotsPage.RefreshEmptyState, FirstStepsCard,
        // ChatManager.ComputeCurrentEmptyState). BotsPage's OnEnable refresh — the one the
        // in-settings delete relies on, since ConfirmDeleteBot re-activates the page just
        // before calling here — runs inside that same frame, so it counted the phantom card
        // and left the «Первые шаги» banner up (and the empty state hidden) until the next
        // tab switch. Detach first: the roster is then truthful the instant the delete happens,
        // whichever order the readers run in. Safe for the one caller that deletes in a loop —
        // ProfileSubPages.RunWipe walks BotsParent BACKWARDS, so shrinking childCount here
        // cannot skip a sibling, and the paired BotSettings clones stay index-aligned because
        // they are only Destroy()ed (never detached).
        Transform botsRoot = transform.parent;
        transform.SetParent(null, false);

        // The «Первые шаги» latches are global, not per-bot: without this, deleting the only
        // bot and creating another re-showed the checklist with the old bot's rows checked.
        OnboardingProgressReset.OnBotDeleted(botsRoot != null ? botsRoot.childCount : 0);

        // Deletion is a fact-changing moment like every other one that already refreshes the
        // card (bot created, channel authed, price list uploaded, wizard back-out) — it was
        // simply missing from that list, leaving the banner to depend on BotsPage's OnEnable.
        // Fire-and-forget, null-guarded; unlike RefreshEmptyState this never auto-opens the
        // Add-Bot overlay, so it is safe on the «Удалить все данные» wipe path too.
        FirstStepsCard.Instance?.RefreshFromFacts();

        Destroy(gameObject);
    }

    /// <summary>
    /// The capsule's state — the bot's ReplyMode default, the SAME store the
    /// chats-header «Авто» button drives (unified 2026-08-13). Workflow
    /// activation is no longer this control's business: workflows stay active
    /// per the channel toggles, and «Вместе» suppresses replies server-side.
    /// </summary>
    private bool AutoOn => AutoButtonModel.IsAutoOn(ReplyModeToggleBinder.GetMode(transform.name));

    /// <summary>Manager gates the capsule while workflows are being created
    /// (the old switch did this via Toggle.interactable).</summary>
    public void SetActivationInteractable(bool value)
    {
        if (autoPillButton != null) autoPillButton.interactable = value;
    }

    private void OnAutoPillTapped()
    {
        if (!cardStateReady) return;

        autoPillButton.transform.DOKill();
        autoPillButton.transform.localScale = Vector3.one;
        autoPillButton.transform.DOPunchScale(Vector3.one * -0.04f, 0.18f, 1, 0.5f);

        // The header pill's asymmetry: enabling confirms (the bot starts
        // messaging real clients), switching back to «Вместе» is instant.
        // RequestEnableAuto can't be reused here — its popup lives under the
        // inactive chats screen — so the card shows its own overlay confirm
        // and commits through the same binder entry; the repaint (and the
        // chats-header sync) arrives via OnReplyModeChanged.
        if (AutoButtonModel.ConfirmRequired(ReplyModeToggleBinder.GetMode(transform.name)))
            BotActivationConfirm.Show(BotName != null ? BotName.font : null, () =>
            {
                if (this != null)   // card may die before confirm
                    ReplyModeToggleBinder.CommitMode(transform.name, ReplyModeToggleBinder.ReplyMode.Auto);
            });
        else
            ReplyModeToggleBinder.DisableAuto(transform.name);
    }

    private void HandleReplyModeChanged(string botId, ReplyModeToggleBinder.ReplyMode _)
    {
        if (cardStateReady && botId == transform.name) RefreshCardState(animatePill: true);
    }

    // Deferred one frame: Manager renames the instantiated card and writes its
    // PlayerPrefs after Instantiate, so an Awake-time read would see another
    // bot's defaults (the old SetSwitches used the same trick).
    private IEnumerator InitCardState()
    {
        yield return new WaitForEndOfFrame();

        cardStateReady = true;
        // The «Not Active» (red) state died with the master switch — the hidden
        // status channel now only distinguishes connected from connecting.
        Status.text = active ? "Active" : "Connecting..";
        Status.color = active ? green : blue;

        RefreshCardState(animatePill: false);
    }

    /// <summary>Manager entry: re-derive the subline (business type + channel
    /// icons) after it renames/creates a bot or saves settings.</summary>
    public void RefreshCardSubline() => RefreshCardState(animatePill: false);

    private void HandleThemeChanged() => RefreshCardState(animatePill: false);

    // Manager writes Status directly on auth success (and this class writes it
    // on activation changes) — mirror the color like the retired BotStatusPill
    // did, so every writer repaints the card without knowing about it.
    private void LateUpdate()
    {
        if (!cardStateReady || Status == null) return;
        if (ColorsClose(Status.color, lastObservedStatusColor)) return;
        RefreshCardState(animatePill: false);
    }

    private void RefreshCardState(bool animatePill)
    {
        if (!cardStateReady) return;
        if (Status != null) lastObservedStatusColor = Status.color;

        // The capsule is the chats-header «Авто» pill 1:1 — one painter, one
        // look, and since the unification one STORE (the bot's ReplyMode).
        ReplyModeToggleBinder.PaintChip(AutoOn, autoPillRing, autoPillFill,
            autoPillLabel, autoPillDotRing, autoPillDotCore, animatePill);

        RefreshSubline();
    }

    private void RefreshSubline()
    {
        if (BotDesc == null) return;

        bool connecting = Status != null && ColorsClose(Status.color, blue);
        string typeName = connecting ? "" : BusinessTypeDisplayName();
        BotDesc.text = BotCardModel.SublineText(connecting, typeName);

        if (connecting)
        {
            var c = BotDesc.color;
            BotDesc.color = new Color(ConnectingInk.r, ConnectingInk.g, ConnectingInk.b, c.a);
            SetIconState(waChannelIcon, BotChannelIconState.Hidden, null);
            SetIconState(tgChannelIcon, BotChannelIconState.Hidden, null);
            EnsureSublineBlink();
        }
        else
        {
            KillSublineBlink();
            BotDesc.color = Theme.Color(ThemeRole.InkTertiary);
            ApplyChannelIcon(waChannelIcon, whatsappProfileId, "isOnWhatsapp", waIconColored, waIconGray);
            ApplyChannelIcon(tgChannelIcon, telegramProfileId, "isOnTelegram", tgIconColored, tgIconGray);
        }

        bool anyIcon = (waChannelIcon != null && waChannelIcon.gameObject.activeSelf)
                    || (tgChannelIcon != null && tgChannelIcon.gameObject.activeSelf);
        bool hasText = !string.IsNullOrEmpty(BotDesc.text);
        BotDesc.gameObject.SetActive(hasText);

        // Collapse the whole subline row when it is empty — but only when the
        // builder has run (BotDesc's parent is the SubRow, never BotDetails).
        Transform subRow = BotDesc.transform.parent;
        if (subRow != null && subRow.name == "SubRow")
            subRow.gameObject.SetActive(hasText || anyIcon);
    }

    private void ApplyChannelIcon(Image icon, string profileId, string channelKeySuffix,
        Sprite coloredSprite, Sprite graySprite)
    {
        if (icon == null) return;
        var state = BotCardModel.IconState(profileId,
            PlayerPrefs.GetInt(transform.name + channelKeySuffix, 1) == 1);
        SetIconState(icon, state,
            state == BotChannelIconState.Colored ? coloredSprite : graySprite);
    }

    // The two states are two authored sprites — the Image tint stays white so
    // the multi-color logos render exactly as designed (a Color multiply would
    // dirty the white glyph inside Telegram's disc).
    private static void SetIconState(Image icon, BotChannelIconState state, Sprite sprite)
    {
        if (icon == null) return;
        bool visible = state != BotChannelIconState.Hidden;
        icon.gameObject.SetActive(visible);
        if (!visible) return;

        if (sprite != null) icon.sprite = sprite;
        icon.color = Color.white;
    }

    private string BusinessTypeDisplayName()
    {
        if (businessTypes == null) return "";
        var id = PlayerPrefs.GetString(transform.name + "BusinessType", "");
        return businessTypes.TryGetById(id, out var entry) ? entry.displayName : "";
    }

    private void EnsureSublineBlink()
    {
        if (sublineBlink != null && sublineBlink.IsActive()) return;
        sublineBlink = BotDesc.DOFade(0.35f, 0.55f)
            .SetLoops(-1, LoopType.Yoyo)
            .SetEase(Ease.InOutSine);
    }

    private void KillSublineBlink()
    {
        if (sublineBlink != null)
        {
            sublineBlink.Kill();
            sublineBlink = null;
        }
        if (BotDesc != null)
        {
            var c = BotDesc.color;
            BotDesc.color = new Color(c.r, c.g, c.b, 1f);
        }
    }

    private static bool ColorsClose(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.01f &&
        Mathf.Abs(a.g - b.g) < 0.01f &&
        Mathf.Abs(a.b - b.b) < 0.01f;

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
        if (autoPillButton != null) autoPillButton.onClick.RemoveListener(OnAutoPillTapped);
        KillSublineBlink();
    }
}

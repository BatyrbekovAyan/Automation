using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// «Первые шаги» first-run checklist card (ONB-04), rendered above the bots list on
/// BotsPage. A thin MonoBehaviour over the pure <see cref="FirstStepsChecklist"/>:
/// it snapshots live facts (bot existence, channel auth, uploaded files, first-reply
/// latch), asks the pure class for the 4 step states, renders the rows with a
/// 0.05s-stagger fade cascade, wires each row's deep-link, and permanently hides once
/// 4/4 is done. The row-4 first-reply latch is written at the event SOURCE by
/// <see cref="OnboardingFirstReplyLatch"/> (installed in ChatManager.Awake) — this
/// card only READS it: the card is inactive on the Chats tab where messages arrive.
///
/// Steps are MILESTONES: each is derived from live facts every Refresh, but once a
/// step has been achieved it latches (<see cref="OnboardingKeys"/>) and never
/// regresses — toggling a messenger off or deleting uploaded files must not undo
/// onboarding progress (owner decision 2026-07-23). Latches: row 2
/// <see cref="OnboardingKeys.ChannelConnectedSeen"/>, row 3
/// <see cref="OnboardingKeys.PriceListUploadedSeen"/>, row 4
/// <see cref="OnboardingKeys.FirstBotReplySeen"/>, 4/4
/// <see cref="OnboardingKeys.ChecklistDone"/>.
///
/// Analogs: DashboardPage (row-template Find idiom, OnEnable refresh, per-row
/// deep-links, DOTween) + BotStatusPill (Hex helper + static Color palette block).
/// All refs are [SerializeField] private — the field names are the FirstStepsCardBuilder
/// stamping contract.
/// </summary>
public class FirstStepsCard : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI progressLabel;
    [SerializeField] private Image progressFill;

    [Header("Rows (exactly 4 children in copy-deck order)")]
    [Tooltip("Parent holding the 4 checklist rows. Each row child exposes " +
             "CheckCircle (Image) / CheckCircle/CheckMark (Image) / Label (TMP) / " +
             "Chevron (GameObject) and a Button on the row root.")]
    [SerializeField] private Transform rowsRoot;
    [SerializeField] private TextMeshProUGUI hintLabel;   // row-4 guidance caption

    [Header("Facts")]
    [Tooltip("Manager.BotsParent — the container the bot cards are instantiated under. " +
             "childCount>0 is the authoritative 'has bots' fact.")]
    [SerializeField] private Transform botsParent;

    [Header("Layout")]
    [Tooltip("Bots-list top padding reserved while the banner is visible, so the first " +
             "bot card clears the card. Restored to the original value on permanent hide.")]
    [SerializeField] private int reservedListTopPadding = 700;

    // Palette (mockup: onboarding-proposal.html §checklist).
    private static readonly Color DoneCircle = Hex("#23A55A");
    private static readonly Color TodoCircle = Hex("#E1E5EC");
    private static readonly Color InkLabel = Hex("#1A1A2E");
    private static readonly Color DoneLabel = Hex("#9AA0AA");

    private const int StepCount = 4;
    private const float CascadeStagger = 0.05f;
    private const float CascadeDuration = 0.3f;

    // Copy deck (spec §Screen specs). Row 2 is deliberately channel-neutral:
    // the product is two-channel, and a bot can hold both, so naming one
    // messenger here is wrong or ambiguous (owner decision 2026-07-23).
    private static readonly string[] RowLabelsBase =
    {
        "Создать бота",
        "Подключить мессенджер",
        "Загрузить прайс-лист",
        "Получить первый ответ бота",
    };
    private const string CardTitle = "Первые шаги";
    private const string Row4Hint = "Попросите знакомого написать вам — и посмотрите, как бот ответит";

    /// <summary>Set in Awake so external fact-change hooks (bot created, channel
    /// authed, wizard back-out, price-list uploaded, return to Bots) can call
    /// <see cref="RefreshFromFacts"/> without a serialized reference. The card root is
    /// always active in the scene, so Awake runs and Instance is always resolvable.</summary>
    public static FirstStepsCard Instance;

    private CanvasGroup _cg;
    private VerticalLayoutGroup _botsVlg;
    private int _origListTopPadding = -1;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
        _cg = GetComponent<CanvasGroup>();
        if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
    }

    /// <summary>External refresh entry — invoked by every fact-changing moment so the
    /// card is a live mirror of onboarding progress without waiting for OnEnable (the
    /// AddBotPanel is an overlay on the still-active Bots page, so OnEnable never re-fires
    /// after bot creation — D3).</summary>
    public void RefreshFromFacts() => Refresh();

    private void OnEnable()
    {
        Refresh();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    private void Refresh()
    {
        bool checklistDone = PlayerPrefs.GetInt(OnboardingKeys.ChecklistDone, 0) == 1;
        bool botExists = botsParent != null && botsParent.childCount > 0;

        // Single visibility gate — covers BOTH the D1 zero-bot hide (the EmptyState is the
        // step-1 guidance) and the permanent 4/4 hide (ChecklistDone latched). The root
        // stays ACTIVE so Instance/RefreshFromFacts survive; visibility is a CanvasGroup
        // toggle only (a self-SetActive(false) root could never be re-shown by a hook).
        if (!FirstStepsCardVisibility.ShouldShow(botExists, checklistDone))
        {
            SetContentVisible(false);
            RestoreListPadding();
            return;
        }

        SetContentVisible(true);

        Bot bot = botExists ? botsParent.GetChild(0).GetComponent<Bot>() : null;

        // "Connected" = ANY authed profile, independent of the current channel toggles —
        // the toggles mean "use this channel", not "connected"; flipping one off must not
        // uncheck the milestone.
        bool channelAuthed = LatchedFact(OnboardingKeys.ChannelConnectedSeen, bot != null && (
            bot.whatsappProfileId != Bot.UnauthedProfileSentinel ||
            bot.telegramProfileId != Bot.UnauthedProfileSentinel));
        bool hasFiles = LatchedFact(OnboardingKeys.PriceListUploadedSeen, bot != null && (
            UploadedFilesStore.Load(bot.name, "product").Count > 0 ||
            UploadedFilesStore.Load(bot.name, "service").Count > 0));
        bool firstReply = PlayerPrefs.GetInt(OnboardingKeys.FirstBotReplySeen, 0) == 1;

        bool[] steps = FirstStepsChecklist.StepStates(botExists, channelAuthed, hasFiles, firstReply);
        int done = 0;
        foreach (bool s in steps) if (s) done++;

        if (titleLabel != null) titleLabel.text = CardTitle;
        if (progressLabel != null) progressLabel.text = $"{done} из {StepCount}";
        SetProgress(done);

        ReserveListPadding();
        RenderRows(steps);

        if (hintLabel != null) hintLabel.text = Row4Hint;

        // Latch completion → permanent hide (spec: card never resurfaces after 4/4).
        // ChecklistDone is now set, so the ShouldShow gate above keeps it hidden forever
        // on every subsequent Refresh; hiding via CanvasGroup keeps the root reachable.
        if (FirstStepsChecklist.AllDone(steps))
        {
            PlayerPrefs.SetInt(OnboardingKeys.ChecklistDone, 1);
            PlayerPrefs.Save();
            RestoreListPadding();
            SetContentVisible(false);
        }
    }

    // Milestone latch: returns the step state per FirstStepsChecklist.Milestone and
    // persists the latch the first time the live fact is observed true.
    private static bool LatchedFact(string key, bool liveFact)
    {
        bool latched = PlayerPrefs.GetInt(key, 0) == 1;
        bool state = FirstStepsChecklist.Milestone(latched, liveFact);
        if (state && !latched)
        {
            PlayerPrefs.SetInt(key, 1);
            PlayerPrefs.Save();
        }
        return state;
    }

    private void SetProgress(int done)
    {
        if (progressFill == null) return;
        var rt = progressFill.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(Mathf.Clamp01(done / (float)StepCount), 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void RenderRows(bool[] steps)
    {
        if (rowsRoot == null) return;
        for (int i = 0; i < StepCount && i < rowsRoot.childCount; i++)
        {
            var row = rowsRoot.GetChild(i);
            string label = RowLabelsBase[i];
            BindRow(row, i, label, steps[i]);
            PlayCascade(row, i);
        }
    }

    private void BindRow(Transform row, int index, string label, bool done)
    {
        var labelTmp = row.Find("Label")?.GetComponent<TextMeshProUGUI>();
        var circle = row.Find("CheckCircle")?.GetComponent<Image>();
        var check = row.Find("CheckCircle/CheckMark")?.gameObject;
        var chevron = row.Find("Chevron")?.gameObject;
        var button = row.GetComponent<Button>();

        if (labelTmp != null)
        {
            labelTmp.text = label;
            labelTmp.color = done ? DoneLabel : InkLabel;
            labelTmp.fontStyle = done
                ? labelTmp.fontStyle | FontStyles.Strikethrough
                : labelTmp.fontStyle & ~FontStyles.Strikethrough;
        }
        if (circle != null) circle.color = done ? DoneCircle : TodoCircle;
        if (check != null) check.SetActive(done);        // white tick only on completed rows
        if (chevron != null) chevron.SetActive(!done);   // tap-affordance chevron only on todo rows

        if (button != null)
        {
            int captured = index;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnRowTapped(captured));
        }
    }

    // Fade-in stagger — position stays owned by the rows' VerticalLayoutGroup, so the
    // cascade animates CanvasGroup alpha only (ui-scripts list-cascade idiom).
    private void PlayCascade(Transform row, int index)
    {
        var cg = row.GetComponent<CanvasGroup>();
        if (cg == null) return;
        cg.DOKill();
        cg.alpha = 0f;
        cg.DOFade(1f, CascadeDuration).SetDelay(index * CascadeStagger).SetEase(Ease.OutCubic);
    }

    // ── Per-row deep-links ─────────────────────────────────────────────────────

    private void OnRowTapped(int index)
    {
        Bot bot = (botsParent != null && botsParent.childCount > 0)
            ? botsParent.GetChild(0).GetComponent<Bot>()
            : null;

        switch (index)
        {
            case 0:  // «Создать бота» → Add-Bot overlay
                BotsPage.Instance?.StartNewBot();
                break;
            case 1:  // «Подключить мессенджер» → bot settings GENERAL tab (connect toggles live here)
                bot?.OpenSettingsAtGeneralTab();
                break;
            case 2:  // «Загрузить прайс-лист» → bot settings PRODUCT tab («Прайс-листы»)
                bot?.OpenSettingsAtProductTab();
                break;
            case 3:  // «Получить первый ответ бота» → Chats tab (hint stays visible under the row)
                FindFirstObjectByType<BottomTabManager>()?.SwitchTab(BottomTabManager.WhatsAppTabIndex);
                break;
        }
    }

    // ── Bots-list top inset (reversible, self-heals on permanent hide) ─────────

    private void ReserveListPadding()
    {
        if (botsParent == null) return;
        _botsVlg ??= botsParent.GetComponent<VerticalLayoutGroup>();
        if (_botsVlg == null) return;

        if (_origListTopPadding < 0) _origListTopPadding = _botsVlg.padding.top;
        if (_botsVlg.padding.top == reservedListTopPadding) return;

        _botsVlg.padding.top = reservedListTopPadding;
        LayoutRebuilder.MarkLayoutForRebuild(botsParent as RectTransform);
    }

    private void RestoreListPadding()
    {
        if (_botsVlg == null || _origListTopPadding < 0) return;
        _botsVlg.padding.top = _origListTopPadding;
        LayoutRebuilder.MarkLayoutForRebuild(botsParent as RectTransform);
        _origListTopPadding = -1;
    }

    // ── Visibility (CanvasGroup, root stays active) ────────────────────────────

    // Hiding via CanvasGroup (not SetActive) keeps the root active so Instance and
    // RefreshFromFacts always resolve. alpha 0 + non-interactive = hidden + click-through.
    private void SetContentVisible(bool visible)
    {
        if (_cg == null) return;
        _cg.alpha = visible ? 1f : 0f;
        _cg.blocksRaycasts = visible;
        _cg.interactable = visible;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }
}

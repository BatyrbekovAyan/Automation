using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// «Первые шаги» first-run checklist card (ONB-04), rendered above the bots list on
/// BotsPage. A thin MonoBehaviour over the pure <see cref="FirstStepsChecklist"/>:
/// it snapshots live facts (bot existence, channel auth, uploaded files, first-reply
/// proxy), asks the pure class for the 4 step states + channel label, renders the
/// rows with a 0.05s-stagger fade cascade, wires each row's deep-link, latches the
/// first-reply proxy off ChatManager events, and permanently hides once 4/4 is done.
///
/// Per-step state is NEVER stored — it is derived LIVE every Refresh (T-11-06-01).
/// Only the 4/4 completion (<see cref="OnboardingKeys.ChecklistDone"/>) and the
/// first-reply proxy (<see cref="OnboardingKeys.FirstBotReplySeen"/>) are latched.
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

    // Copy deck (spec §Screen specs). Row 1's channel label is filled at Refresh time.
    private static readonly string[] RowLabelsBase =
    {
        "Создать бота",
        "Подключить {0}",
        "Загрузить прайс-лист",
        "Получить первый ответ бота",
    };
    private const string CardTitle = "Первые шаги";
    private const string Row4Hint = "Попросите знакомого написать вам — и посмотрите, как бот ответит";

    private VerticalLayoutGroup _botsVlg;
    private int _origListTopPadding = -1;
    private bool _subscribed;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed || ChatManager.Instance == null) return;
        ChatManager.Instance.OnBatchMessagesLoaded += HandleBatch;
        ChatManager.Instance.OnLiveMessagesReceived += LatchIfReplySeen;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || ChatManager.Instance == null) { _subscribed = false; return; }
        ChatManager.Instance.OnBatchMessagesLoaded -= HandleBatch;
        ChatManager.Instance.OnLiveMessagesReceived -= LatchIfReplySeen;
        _subscribed = false;
    }

    // ── First-reply latch (Pitfall 5 proxy) ───────────────────────────────────

    // OnBatchMessagesLoaded carries (msgs, _, _); adapt to the single-list latch.
    private void HandleBatch(List<MessageViewModel> msgs, bool _, bool __) => LatchIfReplySeen(msgs);

    // isIncoming==false is the spec's demonstrative proxy for "the bot has replied" —
    // it also covers the owner's own outgoing message (accepted, T-11-06-03).
    private void LatchIfReplySeen(List<MessageViewModel> msgs)
    {
        if (PlayerPrefs.GetInt(OnboardingKeys.FirstBotReplySeen, 0) == 1) return;
        if (msgs == null || !msgs.Exists(m => m != null && !m.isIncoming)) return;

        PlayerPrefs.SetInt(OnboardingKeys.FirstBotReplySeen, 1);
        PlayerPrefs.Save();
        if (isActiveAndEnabled) Refresh();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    private void Refresh()
    {
        // Permanent hide: once 4/4 latched, never resurrect even if data later changes.
        if (PlayerPrefs.GetInt(OnboardingKeys.ChecklistDone, 0) == 1)
        {
            RestoreListPadding();
            gameObject.SetActive(false);
            return;
        }

        bool botExists = botsParent != null && botsParent.childCount > 0;
        Bot bot = botExists ? botsParent.GetChild(0).GetComponent<Bot>() : null;

        bool isWa = bot != null && PlayerPrefs.GetInt(bot.name + "isOnWhatsapp", 1) == 1;
        bool isTg = bot != null && PlayerPrefs.GetInt(bot.name + "isOnTelegram", 1) == 1;
        string channelLbl = FirstStepsChecklist.ChannelLabel(isWa, isTg);

        bool channelAuthed = bot != null && (
            (isWa && bot.whatsappProfileId != Bot.UnauthedProfileSentinel) ||
            (isTg && bot.telegramProfileId != Bot.UnauthedProfileSentinel));
        bool hasFiles = bot != null && (
            UploadedFilesStore.Load(bot.name, "product").Count > 0 ||
            UploadedFilesStore.Load(bot.name, "service").Count > 0);
        bool firstReply = PlayerPrefs.GetInt(OnboardingKeys.FirstBotReplySeen, 0) == 1;

        bool[] steps = FirstStepsChecklist.StepStates(botExists, channelAuthed, hasFiles, firstReply);
        int done = 0;
        foreach (bool s in steps) if (s) done++;

        if (titleLabel != null) titleLabel.text = CardTitle;
        if (progressLabel != null) progressLabel.text = $"{done} из {StepCount}";
        SetProgress(done);

        ReserveListPadding();
        RenderRows(steps, channelLbl);

        if (hintLabel != null) hintLabel.text = Row4Hint;

        // Latch completion → permanent hide (spec: card never resurfaces after 4/4).
        if (FirstStepsChecklist.AllDone(steps))
        {
            PlayerPrefs.SetInt(OnboardingKeys.ChecklistDone, 1);
            PlayerPrefs.Save();
            RestoreListPadding();
            gameObject.SetActive(false);
        }
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

    private void RenderRows(bool[] steps, string channelLbl)
    {
        if (rowsRoot == null) return;
        for (int i = 0; i < StepCount && i < rowsRoot.childCount; i++)
        {
            var row = rowsRoot.GetChild(i);
            string label = i == 1 ? string.Format(RowLabelsBase[1], channelLbl) : RowLabelsBase[i];
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
            case 1:  // «Подключить {channel}» → bot settings GENERAL tab (connect toggle lives here)
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

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }
}

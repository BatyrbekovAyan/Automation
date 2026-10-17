using System.Collections;
using Automation.BotSettingsUI;
using TMPro;
using UnityEngine;

/// <summary>
/// Part B «расписание бота» (spec 2026-10-14 §4 B2): the «РАСПИСАНИЕ РАБОТЫ» section of the Основное tab — a toggle,
/// «С» / «До» (HH:MM), seven day chips Пн–Вс, one error line and a hint. Authored ADDITIVELY by
/// Tools/BotSettings/Add Schedule Section (BotScheduleBuilder); nothing here builds UI.
///
/// The values travel the path of every other field: Manager hydrates the section from BotScheduleStore
/// (<see cref="ApplySchedule"/> — LoadBots, the wizard, the revert in CloseSettings), the dirty check reads
/// <see cref="EditedSchedule"/>, SaveSettings persists it. A time is validated when its field loses focus
/// (ScheduleModel.TryParseHhMm): a wrong one shows ScheduleCopy.Invalid and keeps Save disabled through
/// BotSettingsDirtyPolicy.CanSave — the one verdict, no second gate — and fixing it while typing clears the line at once;
/// a valid one is shown back in its canonical form («9:00», «21.30», «930», «9» → «09:00», «21:30», «09:30», «09:00»)
/// once the field has let go (<see cref="NormalizeTimeWhenReleased"/>). The fields are only ever WRITTEN while blurred
/// (<see cref="WriteTime"/> force-blurs a focused one first): a write into a focused field round-trips through the shared
/// iOS keyboard buffer.
///
/// Fix round 3 (I-1): one delivery line says where the SAVED schedule stands on its way to the server
/// (ScheduleSyncService.StateOf → ScheduleCopy.SyncStatus): pending, failed, or hidden once the server holds it. It
/// repaints on ScheduleSyncService.OnStateChanged and on Theme.Changed (its colour is owned here — no ThemedColor), and
/// opening the bot's settings re-arms an undelivered schedule first (<see cref="OnScheduleSettingsOpened"/>).
/// </summary>
public partial class BotSettings
{
    [SerializeField] private ToggleRow scheduleRow;
    [SerializeField] private EditableField scheduleStartField;
    [SerializeField] private EditableField scheduleEndField;
    [SerializeField] private DayChip[] scheduleDayChips = new DayChip[0];
    [SerializeField] private TextMeshProUGUI scheduleError;
    [SerializeField] private TextMeshProUGUI scheduleSyncStatus;

    private int scheduleDaysMask = ScheduleModel.AllDays;
    private bool scheduleTimeErrorShown;

    public ToggleRow ScheduleRow => scheduleRow;
    public EditableField ScheduleStartField => scheduleStartField;
    public EditableField ScheduleEndField => scheduleEndField;
    public DayChip[] ScheduleDayChips => scheduleDayChips;
    public TextMeshProUGUI ScheduleError => scheduleError;
    public TextMeshProUGUI ScheduleSyncStatus => scheduleSyncStatus;

    /// <summary>False on a prefab that predates the builder — the snapshot then mirrors the saved schedule.</summary>
    public bool HasScheduleUi => scheduleRow != null && scheduleRow.Toggle != null
                                 && scheduleStartField != null && scheduleEndField != null;

    /// <summary>What the section holds now; a time that does not parse reads as ScheduleModel.InvalidMinute.</summary>
    public BotSchedule EditedSchedule => new BotSchedule(
        scheduleRow != null && scheduleRow.Toggle != null && scheduleRow.Toggle.isOn,
        scheduleDaysMask,
        ParsedOrInvalid(scheduleStartField),
        ParsedOrInvalid(scheduleEndField));

    /// <summary>
    /// Shows a stored schedule (hydration in LoadBots / the wizard, the revert in CloseSettings) without raising any edit.
    /// ToggleRow.SetIsOnQuiet repaints in every state — also behind a hidden tab (the revert after a switch to «Бизнес»),
    /// and before the row's Awake it only takes the value, which Awake then paints.
    /// </summary>
    public void ApplySchedule(BotSchedule schedule)
    {
        if (!HasScheduleUi) return;
        scheduleRow.SetIsOnQuiet(schedule.Enabled);
        WriteTime(scheduleStartField, schedule.StartMin);
        WriteTime(scheduleEndField, schedule.EndMin);
        scheduleDaysMask = schedule.DaysMask;
        scheduleTimeErrorShown = false;
        PaintScheduleDays();
        RefreshScheduleError();
    }

    private void WireSchedule()
    {
        if (!HasScheduleUi) return;
        scheduleRow.Toggle.onValueChanged.AddListener(_ => OnScheduleEdited());
        foreach (var field in new[] { scheduleStartField, scheduleEndField })
        {
            WireDirtyOnEdit(field);
            field.Blurred += OnScheduleTimeBlurred;
            if (field.InputField != null) field.InputField.onValueChanged.AddListener(_ => OnScheduleTimeTyped());
        }
        foreach (var chip in scheduleDayChips)
            if (chip != null) chip.Pressed += ToggleScheduleDay;
        PaintScheduleDays();
        RefreshScheduleError();
    }

    // Validation on blur: a half-typed time is not an error yet. A valid one is written back canonical — later, once the
    // field's TMP has let go: on a direct switch to the other field this fires while the TMP may still be focused.
    private void OnScheduleTimeBlurred(EditableField field)
    {
        scheduleTimeErrorShown = !ScheduleTimesParse();
        RefreshScheduleError();
        if (isActiveAndEnabled) StartCoroutine(NormalizeTimeWhenReleased(field));
    }

    // The Prompts-tab rule: never write into a focused field. Wait out the TMP's focus (it lags the wrapper's blur by a few
    // frames), one more frame, re-check both flags — a field the owner went back into is left alone — and only then show
    // a time that parses in its canonical form. An invalid one stays as typed: the error line explains it.
    private IEnumerator NormalizeTimeWhenReleased(EditableField field)
    {
        float giveUpAt = Time.unscaledTime + 1f;
        while (field.InputField != null && field.InputField.isFocused && Time.unscaledTime < giveUpAt)
            yield return null;
        yield return null;
        if (field.IsFocused || field.InputField == null || field.InputField.isFocused) yield break;
        if (!ScheduleModel.TryParseHhMm(field.Value, out int minutes)) yield break;
        string canonical = ScheduleModel.FormatHhMm(minutes);
        if (field.Value != canonical) field.Value = canonical;
    }

    // A shown error goes away the moment the value becomes right; a new one waits for the blur.
    private void OnScheduleTimeTyped()
    {
        if (!scheduleTimeErrorShown || !ScheduleTimesParse()) return;
        scheduleTimeErrorShown = false;
        RefreshScheduleError();
    }

    private void ToggleScheduleDay(int day)
    {
        scheduleDaysMask = ScheduleModel.ToggleDay(scheduleDaysMask, day);
        PaintScheduleDays();
        OnScheduleEdited();
    }

    private void OnScheduleEdited()
    {
        RefreshScheduleError();
        Manager.Instance.EnableSave();
    }

    private bool ScheduleTimesParse()
        => ScheduleModel.TryParseHhMm(scheduleStartField.Value, out _) && ScheduleModel.TryParseHhMm(scheduleEndField.Value, out _);

    private void PaintScheduleDays()
    {
        foreach (var chip in scheduleDayChips)
            if (chip != null) chip.SetSelected(ScheduleModel.HasDay(scheduleDaysMask, chip.Day));
    }

    private void RefreshScheduleError()
    {
        if (scheduleError == null || !HasScheduleUi) return;
        string message = ScheduleCopy.ErrorFor(scheduleTimeErrorShown && !ScheduleTimesParse(), scheduleRow.Toggle.isOn, scheduleDaysMask);
        if (message != null) scheduleError.text = message;
        if (scheduleError.gameObject.activeSelf != (message != null)) scheduleError.gameObject.SetActive(message != null);
    }

    // ── the delivery line (fix round 3, I-1) ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The settings just opened on this bot: an undelivered schedule (waiting out a backoff, parked, refused) is tried
    /// again now, and the line shows where it stands. Called by Bot.OpenSettings once Manager.openBot names this screen's
    /// bot — OnEnable runs before that pairing.
    /// </summary>
    public void OnScheduleSettingsOpened()
    {
        string bot = ScheduleBotId;
        if (bot != null) ScheduleSyncService.Existing?.Rearm(bot);
        RefreshScheduleSync();
    }

    /// <summary>Paints the delivery line for a state: hidden when confirmed, the copy and its role's colour otherwise.</summary>
    public void ApplyScheduleSyncState(ScheduleDelivery state)
    {
        if (scheduleSyncStatus == null) return;
        string message = ScheduleCopy.SyncStatus(state);
        if (message != null)
        {
            scheduleSyncStatus.text = message;
            scheduleSyncStatus.color = Theme.Color(state == ScheduleDelivery.Failed ? ThemeRole.Destructive : ThemeRole.InkSecondary);
        }
        if (scheduleSyncStatus.gameObject.activeSelf != (message != null)) scheduleSyncStatus.gameObject.SetActive(message != null);
    }

    private void BindScheduleSync()
    {
        ScheduleSyncService.OnStateChanged += HandleScheduleSyncState;
        Theme.Changed += RefreshScheduleSync;
        RefreshScheduleSync();
    }

    private void UnbindScheduleSync()
    {
        ScheduleSyncService.OnStateChanged -= HandleScheduleSyncState;
        Theme.Changed -= RefreshScheduleSync;
    }

    // null = every bot (a launch, the full wipe, a re-arm on return to the app).
    private void HandleScheduleSyncState(string botId)
    {
        if (botId == null || botId == ScheduleBotId) RefreshScheduleSync();
    }

    private void RefreshScheduleSync()
    {
        string bot = ScheduleBotId;
        var service = ScheduleSyncService.Existing;
        ApplyScheduleSyncState(bot != null && service != null ? service.StateOf(bot) : ScheduleDelivery.Confirmed);
    }

    // This screen's bot while it is the open one (Bot.OpenSettings pairs them); null otherwise — the line then hides.
    private string ScheduleBotId => Manager.openBotSettings == this && Manager.openBot != null ? Manager.openBot.name : null;

    private static int ParsedOrInvalid(EditableField field)
        => field != null && ScheduleModel.TryParseHhMm(field.Value, out int minutes) ? minutes : ScheduleModel.InvalidMinute;

    private static void WriteTime(EditableField field, int minutes)
    {
        if (field.IsFocused || (field.InputField != null && field.InputField.isFocused)) field.ForceBlur();
        field.Value = ScheduleModel.FormatHhMm(minutes);
    }
}

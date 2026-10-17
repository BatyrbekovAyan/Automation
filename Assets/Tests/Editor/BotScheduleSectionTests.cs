using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Automation.BotSettingsUI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Part B «расписание бота»: the «РАСПИСАНИЕ РАБОТЫ» section that Tools/BotSettings/Add Schedule Section
/// (BotScheduleBuilder) adds to the Основное tab — additively, resolved through BotSettings' serialized refs. These pin
/// the prefab the builder produced (a stale builder or a hand edit fails here, not on device) and the day chip's
/// two-state colours, which the chip owns itself.
/// </summary>
public class BotScheduleSectionTests
{
    private const string PrefabPath = "Assets/Prefabs/BotSettings.prefab";

    private static BotSettings Settings()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "BotSettings.prefab not found");
        var settings = prefab.GetComponent<BotSettings>();
        Assert.IsNotNull(settings);
        return settings;
    }

    [TearDown]
    public void ResetTheme() => Theme.ResetForTests();

    [Test]
    public void TheSection_IsWired_WithItsCopy()
    {
        var s = Settings();
        Assert.IsTrue(s.HasScheduleUi, "Run Tools/BotSettings/Add Schedule Section.");
        Assert.AreEqual(ScheduleCopy.ToggleLabel, s.ScheduleRow.Label);
        Assert.IsFalse(s.ScheduleRow.Toggle.isOn, "a schedule is OFF by default");
        Assert.AreEqual(ScheduleCopy.FromLabel, s.ScheduleStartField.Label);
        Assert.AreEqual(ScheduleCopy.ToLabel, s.ScheduleEndField.Label);
        Assert.AreEqual(ScheduleCopy.StartPlaceholder, ((TMP_Text)s.ScheduleStartField.InputField.placeholder).text);
        Assert.AreEqual(ScheduleCopy.EndPlaceholder, ((TMP_Text)s.ScheduleEndField.InputField.placeholder).text);
        Assert.AreEqual("09:00", s.ScheduleStartField.Value, "the prefab shows the default window");
        Assert.AreEqual("21:00", s.ScheduleEndField.Value);

        var header = s.ScheduleRow.transform.parent.GetComponentsInChildren<SectionHeader>(true)
            .FirstOrDefault(h => h.Text == ScheduleCopy.SectionTitle);
        Assert.IsNotNull(header, "the section header");
        Assert.IsNotNull(s.ScheduleError, "the error line");
        Assert.IsFalse(s.ScheduleError.gameObject.activeSelf, "no error until a value is wrong");
        var hint = s.ScheduleRow.transform.parent.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(t => t.text == ScheduleCopy.Hint);
        Assert.IsNotNull(hint, "the hint under the section");
        Assert.IsTrue(hint.gameObject.activeSelf);
    }

    [Test]
    public void TheBlock_SitsInTheGeneralTab_RightAboveDeleteBot()
    {
        var s = Settings();
        Transform content = s.BotNameField.transform.parent;
        Assert.AreSame(content, s.ScheduleRow.transform.parent, "the toggle lives in the Основное tab's own list");
        var delete = content.Find("DeleteBotButton");
        Assert.IsNotNull(delete);
        var header = content.Cast<Transform>().First(t => t.GetComponent<SectionHeader>() != null
                                                          && t.GetComponent<SectionHeader>().Text == ScheduleCopy.SectionTitle);
        Transform hours = s.ScheduleStartField.transform.parent;
        Transform days = s.ScheduleDayChips[0].transform.parent;
        var hint = content.Cast<Transform>().First(t => t.GetComponent<TextMeshProUGUI>() != null
                                                        && t.GetComponent<TextMeshProUGUI>().text == ScheduleCopy.Hint);
        var status = content.Find("ScheduleSyncStatus");
        Assert.IsNotNull(status, "the delivery line (fix round 3) — run Tools/BotSettings/Add Schedule Section");
        var order = new[] { header, s.ScheduleRow.transform, hours, days, s.ScheduleError.transform, status, hint, delete };
        foreach (var t in order) Assert.AreSame(content, t.parent, $"{t.name} must be a direct child of the tab's list");
        for (int i = 1; i < order.Length; i++)
            Assert.AreEqual(order[i - 1].GetSiblingIndex() + 1, order[i].GetSiblingIndex(),
                $"{order[i - 1].name} → {order[i].name}: the block is contiguous and ends right above «Удалить бота»");
        Assert.AreSame(hours, s.ScheduleEndField.transform.parent, "«С» and «До» share one row");
        Assert.IsNotNull(hours.GetComponent<HorizontalLayoutGroup>());
        Assert.IsNotNull(days.GetComponent<HorizontalLayoutGroup>());
    }

    [Test]
    public void TheGeneralTab_ScrollsACoveredFieldAboveTheKeyboard()
    {
        var general = Settings().BotNameField.GetComponentInParent<ScrollRect>(true);
        Assert.IsNotNull(general, "the Основное tab is a ScrollRect");
        Assert.IsNotNull(general.GetComponent<FormKeyboardScroll>(),
            "the schedule fields sit low in the tab — without FormKeyboardScroll the keyboard covers them");
    }

    [Test]
    public void SevenDayChips_MondayFirst_TheRingTakesTheTap_NoThemedColor()
    {
        var chips = Settings().ScheduleDayChips;
        Assert.AreEqual(7, chips.Length);
        for (int day = 0; day < 7; day++)
        {
            var chip = chips[day];
            Assert.IsNotNull(chip, $"chip {day}");
            Assert.AreEqual(day, chip.Day);
            Assert.AreEqual(ScheduleCopy.DayName(day), chip.GetComponentInChildren<TextMeshProUGUI>(true).text);
            var button = chip.GetComponent<Button>();
            Assert.IsNotNull(button);
            Assert.AreSame(chip.GetComponent<Image>(), button.targetGraphic, "the ring is the Button's target graphic");
            Assert.IsTrue(chip.GetComponent<Image>().raycastTarget, "the ring is the chip's raycast target");
            Assert.IsEmpty(chip.GetComponentsInChildren<ThemedColor>(true),
                "the chip owns its colours (Theme.Changed) — a ThemedColor would be a second owner");
        }
    }

    [Test]
    public void ADayChip_PaintsBothStates_FromTheTheme_AndNeverHidesItsRing()
    {
        var light = ScriptableObject.CreateInstance<ThemeAsset>();
        Theme.OverrideForTests(light, light, ThemeMode.Light);
        var go = Object.Instantiate(Settings().ScheduleDayChips[2].gameObject);
        try
        {
            var chip = go.GetComponent<DayChip>();
            var ring = go.GetComponent<Image>();
            var face = go.transform.Find("Face").GetComponent<Image>();
            var label = go.GetComponentInChildren<TextMeshProUGUI>(true);

            chip.SetSelected(true);
            Assert.IsTrue(chip.Selected);
            Assert.AreEqual(Theme.Color(ThemeRole.AccentFill), face.color);
            Assert.AreEqual(face.color, ring.color, "selected: the ring hides by matching the fill");
            Assert.AreEqual(Theme.Color(ThemeRole.AccentOnFill), label.color);

            chip.SetSelected(false);
            Assert.AreEqual(Theme.Color(ThemeRole.Surface), face.color);
            Assert.AreEqual(Theme.Color(ThemeRole.Border), ring.color);
            Assert.AreEqual(Theme.Color(ThemeRole.InkSecondary), label.color);
            Assert.IsTrue(ring.enabled, "never disabled — an off chip must still take the tap that turns it on");
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(light);
        }
    }

    // ── the toggle's painted state (review round 2) ──────────────────────────────────────────────────────

    private static readonly MethodInfo ToggleRowAwake =
        typeof(ToggleRow).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);

    private sealed class Painted
    {
        public RectTransform Thumb;
        public Image Track;
        public Color On, Off;
        public float Offset;
    }

    private static Painted PaintOf(ToggleRow row)
    {
        var so = new SerializedObject(row);
        return new Painted
        {
            Thumb = (RectTransform)row.transform.Find("Toggle/Thumb"),
            Track = row.transform.Find("Toggle/Track").GetComponent<Image>(),
            On = so.FindProperty("onColor").colorValue,
            Off = so.FindProperty("offColor").colorValue,
            Offset = so.FindProperty("thumbOffsetX").floatValue,
        };
    }

    [Test]
    public void Revert_WithTheGeneralTabHidden_RepaintsTheScheduleToggle()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var settings = root.GetComponent<BotSettings>();
            var row = settings.ScheduleRow;
            Assert.IsNotNull(ToggleRowAwake, "ToggleRow.Awake renamed — update this test");
            ToggleRowAwake.Invoke(row, null);                 // settings opened on «Основное»: the row ran Awake
            var paint = PaintOf(row);
            Vector2 off = paint.Thumb.anchoredPosition;

            row.SetIsOnQuiet(true);                           // the owner switched the schedule on
            Assert.AreEqual(off + new Vector2(paint.Offset, 0f), paint.Thumb.anchoredPosition);

            var general = settings.BotNameField.GetComponentInParent<ScrollRect>(true).gameObject;
            general.SetActive(false);                         // …opened «Бизнес»: the Основное tab is hidden
            settings.ApplySchedule(BotSchedule.Default);      // …and went Back: CloseSettings reverts to the saved «off»

            Assert.IsFalse(row.Toggle.isOn);
            Assert.AreEqual(off, paint.Thumb.anchoredPosition,
                "the thumb must follow the reverted value while its tab is hidden — ToggleRow has no OnEnable repaint, so a " +
                "stale thumb would show the switch opposite to the saved (and server) state on the next open");
            Assert.AreEqual(paint.Off, paint.Track.color);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void SetIsOnQuiet_BeforeTheRowEverRan_LeavesThePaintToAwake()
    {
        // A never-opened settings screen (LoadBots, the wizard, a retirement mark at boot) has rows that never ran Awake:
        // painting then would record the thumb's ON position as its OFF baseline for good.
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var row = root.GetComponent<BotSettings>().ScheduleRow;
            var paint = PaintOf(row);
            Vector2 authoredOff = paint.Thumb.anchoredPosition;

            row.SetIsOnQuiet(true);
            Assert.IsTrue(row.Toggle.isOn, "the value is taken at once");
            Assert.AreEqual(authoredOff, paint.Thumb.anchoredPosition, "…but nothing is painted before Awake records the baseline");

            ToggleRowAwake.Invoke(row, null);
            Assert.AreEqual(authoredOff + new Vector2(paint.Offset, 0f), paint.Thumb.anchoredPosition, "Awake paints the value it finds");
            Assert.AreEqual(paint.On, paint.Track.color);

            row.SetIsOnQuiet(false);
            Assert.AreEqual(authoredOff, paint.Thumb.anchoredPosition, "the baseline is the authored OFF position");
            Assert.AreEqual(paint.Off, paint.Track.color);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ── a valid time is shown in its canonical form once the field lets go (review round 2) ──────────────────

    [TestCase("9:00", "09:00")]
    [TestCase(" 21:30 ", "21:30")]
    [TestCase("0:05", "00:05")]
    [TestCase("09:00", "09:00")]
    [TestCase("25:00", "25:00")]   // invalid: left as typed, the error line explains it
    [TestCase("9", "09:00")]       // fix round 3 (M3): a bare hour, a dot, a dash, a space, three or four digits
    [TestCase("21.30", "21:30")]
    [TestCase("9-30", "09:30")]
    [TestCase("9 30", "09:30")]
    [TestCase("930", "09:30")]
    [TestCase("2130", "21:30")]
    [TestCase("9.5", "9.5")]       // still invalid: one minute digit is ambiguous
    public void ABlurredTime_IsWrittenBackCanonical_OnlyWhenItParses(string typed, string shown)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var settings = root.GetComponent<BotSettings>();
            var field = settings.ScheduleStartField;
            field.Value = typed;
            var normalize = typeof(BotSettings).GetMethod("NormalizeTimeWhenReleased", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(normalize, "the blur's deferred write-back is gone");
            var routine = (IEnumerator)normalize.Invoke(settings, new object[] { field });
            for (int frame = 0; frame < 120 && routine.MoveNext(); frame++) { }
            Assert.AreEqual(shown, field.Value);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [Test]
    public void TheWriteBack_WaitsForTheFieldToLetGo_AndNeverTouchesAFocusedOne()
    {
        string src = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Main/BotSettings.Schedule.cs"));
        int blur = src.IndexOf("private void OnScheduleTimeBlurred(EditableField field)", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(blur, 0);
        string blurBody = src.Substring(blur, src.IndexOf("\n    }", blur, System.StringComparison.Ordinal) - blur);
        Assert.IsTrue(blurBody.Contains("StartCoroutine(NormalizeTimeWhenReleased(field))"), "the blur defers the write-back");
        Assert.IsFalse(blurBody.Contains(".Value ="), "never a write in the blur itself — the TMP can still be focused on a field switch");

        int routine = src.IndexOf("private IEnumerator NormalizeTimeWhenReleased(", System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(routine, 0);
        string body = src.Substring(routine);
        int wait = body.IndexOf("InputField.isFocused", System.StringComparison.Ordinal);
        int frame = body.IndexOf("yield return null;", System.StringComparison.Ordinal);
        int guard = body.IndexOf("if (field.IsFocused", System.StringComparison.Ordinal);
        int write = body.IndexOf("field.Value = ", System.StringComparison.Ordinal);
        Assert.IsTrue(wait >= 0 && frame > 0 && guard > frame && write > guard,
            "wait out the TMP focus, one more frame, re-check both focus flags, only then write (the Prompts-tab rule)");
    }

    // ── the delivery line (fix round 3, I-1): a schedule that has not reached the server says so ─────────────────

    private static TextMeshProUGUI SyncStatusOf(BotSettings settings)
    {
        var field = typeof(BotSettings).GetField("scheduleSyncStatus", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "BotSettings.scheduleSyncStatus is gone");
        return field.GetValue(settings) as TextMeshProUGUI;
    }

    [Test]
    public void TheDeliveryLine_IsWired_Hidden_AndOwnsItsColour()
    {
        var status = SyncStatusOf(Settings());
        Assert.IsNotNull(status, "Run Tools/BotSettings/Add Schedule Section — the delivery line is not wired");
        Assert.AreEqual("ScheduleSyncStatus", status.name);
        Assert.IsFalse(status.gameObject.activeSelf, "hidden until a schedule is on its way or failed");
        Assert.IsNull(status.GetComponent<ThemedColor>(), "two colours by state — the section owns them; a ThemedColor would be a second owner");
        Assert.IsFalse(status.raycastTarget);
        Assert.AreEqual(TextWrappingModes.Normal, status.textWrappingMode);
        Assert.GreaterOrEqual(((RectTransform)status.transform).sizeDelta.y, 40f, "at least the error line's one-line box");
    }

    [TestCase(ScheduleDelivery.Pending, ScheduleCopy.SyncPending, ThemeRole.InkSecondary)]
    [TestCase(ScheduleDelivery.Failed, ScheduleCopy.SyncFailed, ThemeRole.Destructive)]
    [TestCase(ScheduleDelivery.Rejected, ScheduleCopy.SyncRejected, ThemeRole.Destructive)]
    public void TheDeliveryLine_SaysWhereTheScheduleIs_InItsStatesColour(ScheduleDelivery state, string text, ThemeRole role)
    {
        var light = ScriptableObject.CreateInstance<ThemeAsset>();
        Theme.OverrideForTests(light, light, ThemeMode.Light);
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var settings = root.GetComponent<BotSettings>();
            var status = settings.ScheduleSyncStatus;
            Assert.IsNotNull(status);
            settings.ApplyScheduleSyncState(state);
            Assert.IsTrue(status.gameObject.activeSelf, $"{state}: the line is shown");
            Assert.AreEqual(text, status.text);
            Assert.AreEqual(Theme.Color(role), status.color);
            settings.ApplyScheduleSyncState(ScheduleDelivery.Confirmed);
            Assert.IsFalse(status.gameObject.activeSelf, "the server holds it — the line goes");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
            Object.DestroyImmediate(light);
        }
    }

    [Test]
    public void TheDeliveryLine_FollowsTheService_AndOpeningTheSettingsReArmsTheBot()
    {
        string schedule = Src("Scripts/Main/BotSettings.Schedule.cs");
        Assert.IsTrue(MethodBody(schedule, "private void BindScheduleSync()").Contains("ScheduleSyncService.OnStateChanged += HandleScheduleSyncState"));
        Assert.IsTrue(MethodBody(schedule, "private void UnbindScheduleSync()").Contains("ScheduleSyncService.OnStateChanged -= HandleScheduleSyncState"));
        Assert.IsTrue(MethodBody(schedule, "private void BindScheduleSync()").Contains("Theme.Changed += RefreshScheduleSync")
                      && MethodBody(schedule, "private void UnbindScheduleSync()").Contains("Theme.Changed -= RefreshScheduleSync"),
            "the line's colour is owned here — it repaints with the theme");
        Assert.IsTrue(MethodBody(schedule, "private void HandleScheduleSyncState(string botId)").Contains("RefreshScheduleSync()"));
        string refresh = MethodBody(schedule, "private void RefreshScheduleSync()");
        Assert.IsTrue(refresh.Contains(".StateOf(") && refresh.Contains("ApplyScheduleSyncState("), "the line shows the service's verdict");
        Assert.IsTrue(refresh.Contains("ApplyScheduleSyncState(ScheduleDeliveryLine.Shown("),
            "…through the pending grace (round 4) — a normal Save shows no line");
        Assert.IsTrue(refresh.Contains("ScheduleDeliveryLine.RecheckInMs(") && refresh.Contains("ScheduleGraceRecheck("),
            "a desire still pending when the grace ends must then be shown — the section looks again by itself");
        Assert.IsTrue(MethodBody(schedule, "private void UnbindScheduleSync()").Contains("scheduleSyncGraceRoutine = null"),
            "the grace coroutine dies with the screen — its latch resets in OnDisable (the BotSettings coroutine rule)");

        string settings = Src("Scripts/Main/BotSettings.cs");
        Assert.IsTrue(MethodBody(settings, "public void OnEnable()").Contains("BindScheduleSync()"));
        Assert.IsTrue(MethodBody(settings, "public void OnDisable()").Contains("UnbindScheduleSync()"));

        string opened = MethodBody(schedule, "public void OnScheduleSettingsOpened()");
        int rearm = opened.IndexOf("ScheduleSyncService.Existing?.Rearm(", System.StringComparison.Ordinal);
        int paint = opened.IndexOf("RefreshScheduleSync()", System.StringComparison.Ordinal);
        Assert.IsTrue(rearm >= 0 && paint > rearm, "re-arm the bot's undelivered schedule, then show where it stands");
        string open = MethodBody(Src("Scripts/Main/Bot.cs"), "private void OpenSettings()");
        int pair = open.IndexOf("Manager.openBotSettings = ", System.StringComparison.Ordinal);
        int hook = open.IndexOf("Manager.openBotSettings.OnScheduleSettingsOpened()", System.StringComparison.Ordinal);
        Assert.IsTrue(pair >= 0 && hook > pair, "after the pairing — BotSettings.OnEnable ran before Manager.openBot named its bot");
    }

    private static string Src(string relative) => File.ReadAllText(Path.Combine(Application.dataPath, relative));

    private static string MethodBody(string source, string signature)
    {
        int at = source.IndexOf(signature, System.StringComparison.Ordinal);
        Assert.GreaterOrEqual(at, 0, $"{signature} not found");
        int open = source.IndexOf('{', at);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        Assert.Fail($"{signature}: unbalanced braces");
        return null;
    }
}

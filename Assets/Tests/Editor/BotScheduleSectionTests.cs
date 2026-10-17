using System.Linq;
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
        var order = new[] { header, s.ScheduleRow.transform, hours, days, s.ScheduleError.transform, hint, delete };
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
}

using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Part B «расписание бота»: outside the bot's hours the server answers exactly as in «Вместе» (no auto-reply), so the
/// suggestions panel must be there for an «Авто» bot too — otherwise the chat sits in silence with no panel. An
/// EFFECTIVE-mode override, like the quota fallback: the same predicate and zone as the server, nothing written.
/// </summary>
public class ScheduleFallbackPolicyTests
{
    // 2026-10-12 is a Monday. In Almaty (UTC+5): 07:00Z = 12:00, 14:00Z = 19:00, 20:00Z = 01:00 Tuesday.
    private static readonly DateTimeOffset MondayNoonAlmaty = new DateTimeOffset(2026, 10, 12, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MondayEveningAlmaty = new DateTimeOffset(2026, 10, 12, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TuesdayNightAlmaty = new DateTimeOffset(2026, 10, 12, 20, 0, 0, TimeSpan.Zero);

    private static readonly BotSchedule Weekdays9To18 = new BotSchedule(true, 31, 540, 1080);

    [Test]
    public void OutsideAnEnabledWindow_TheChatBehavesAsTogether()
    {
        Assert.IsTrue(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, MondayEveningAlmaty, "Asia/Almaty"));
        Assert.IsTrue(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, TuesdayNightAlmaty, "Asia/Almaty"));
    }

    [Test]
    public void InsideTheWindow_TheStoredModeStands()
        => Assert.IsFalse(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, MondayNoonAlmaty, "Asia/Almaty"));

    [Test]
    public void ADisabledSchedule_NeverFallsBack()
    {
        var disabled = new BotSchedule(false, 31, 540, 1080);
        Assert.IsFalse(ScheduleFallbackPolicy.ShouldFallBackToSemi(disabled, MondayEveningAlmaty, "Asia/Almaty"));
        Assert.IsFalse(ScheduleFallbackPolicy.ShouldFallBackToSemi(BotSchedule.Default, MondayEveningAlmaty, "Asia/Almaty"),
            "a bot without a schedule answers as today");
    }

    [Test]
    public void TheBotsZoneDecides_NotUtc()
    {
        // 07:00Z: noon in Almaty (inside), 07:00 in UTC (outside).
        Assert.IsFalse(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, MondayNoonAlmaty, "Asia/Almaty"));
        Assert.IsTrue(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, MondayNoonAlmaty, "UTC"));
        Assert.IsFalse(ScheduleFallbackPolicy.ShouldFallBackToSemi(Weekdays9To18, MondayNoonAlmaty, null),
            "a legacy bot without a zone is judged in Asia/Almaty, the server's default");
    }

    [Test]
    public void AnEnabledScheduleWithNoDay_IsNeverOpen()
        => Assert.IsTrue(ScheduleFallbackPolicy.ShouldFallBackToSemi(new BotSchedule(true, 0, 0, 0), MondayNoonAlmaty, "Asia/Almaty"));

    [Test]
    public void ThePanel_AsksThePolicy_AndThePolicyWritesNoMode()
    {
        string controller = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Chat/SuggestionsController.cs"));
        var effective = Regex.Match(controller, @"private static bool EffectiveSemiAuto\(bool storedOn\)(?<body>[^;]*;)", RegexOptions.Singleline);
        Assert.IsTrue(effective.Success, "EffectiveSemiAuto(bool storedOn) not found");
        string body = effective.Groups["body"].Value;
        Assert.IsTrue(body.Contains("storedOn") && body.Contains("QuotaFallbackPolicy.ShouldFallBackToSemi(UsageStore.Current)"),
            "the stored mode and the quota fallback stay");
        Assert.IsTrue(body.Contains("IsOffHoursForOpenBot()"), "the schedule term is the open bot's");
        string helper = Regex.Match(controller, @"private static bool IsOffHoursForOpenBot\(\)(?<body>.*?)\n    \}", RegexOptions.Singleline).Groups["body"].Value;
        Assert.IsTrue(helper.Contains("ScheduleFallbackPolicy.ShouldFallBackToSemi(") && helper.Contains("BotScheduleStore.Read(")
                      && helper.Contains("BusinessTimeZone.ForBot("), "the open bot's saved schedule in the bot's own zone");

        string policy = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Billing/ScheduleFallbackPolicy.cs"));
        foreach (var store in new[] { "SemiAutoStore", "ReplyModeToggleBinder", "SyncReplyMode", "PlayerPrefs" })
            Assert.IsFalse(Regex.Replace(policy, @"(///|//)[^\n]*", "").Contains(store),
                $"the fallback is an EFFECTIVE mode — it must never touch {store}");
    }
}

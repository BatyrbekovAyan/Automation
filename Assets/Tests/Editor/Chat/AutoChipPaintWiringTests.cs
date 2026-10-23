using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEngine;

/// <summary>
/// The four «Авто» capsules — the bot card, the chats header, the bot-switcher row and the open-chat chip — are painted by ONE
/// painter, ReplyModeToggleBinder.PaintChip, with the bot's learning percent from AutoReadinessPolicy.StateFor, and repaint on a
/// fresh Get Usage snapshot (spec 2026-10-22-learning-progress-percent §4.3–§4.4). Their sameness rests on that one path. EditMode
/// cannot tap a capsule in the scene, so the wiring is pinned by reflection and by text; the painter's behaviour itself is
/// AutoChipProgressTests'.
/// </summary>
public class AutoChipPaintWiringTests
{
    private static string Src(string rel) => File.ReadAllText(Path.Combine(Application.dataPath, rel));

    /// <summary>The file with its // comments cut away — a comment can name a call without making it.</summary>
    private static string Code(string rel) => Regex.Replace(Src(rel), @"//[^\n]*", "");

    [Test]
    public void ThereIsOnePaintChip_AndItTakesThePercentAndTheProgressLayer()
    {
        var all = typeof(ReplyModeToggleBinder).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "PaintChip").ToArray();
        Assert.AreEqual(1, all.Length);
        var types = all[0].GetParameters().Select(p => p.ParameterType).ToArray();
        CollectionAssert.Contains(types, typeof(int?));
        CollectionAssert.Contains(types, typeof(AutoChipProgress));
    }

    [TestCase("Scripts/Main/Bot.cs")]
    [TestCase("Scripts/UI/ReplyModeToggleBinder.cs")]
    [TestCase("Scripts/UI/BotSwitcherRowView.cs")]
    [TestCase("Scripts/UI/SemiAutoToggle.cs")]
    public void EveryAutoChip_PaintsThroughTheSharedPainter_WithTheBotsLearningState(string file)
    {
        string src = Src(file);
        StringAssert.Contains("PaintChip(", src);
        StringAssert.Contains("AutoReadinessPolicy.StateFor(", src);
        StringAssert.Contains("UsageStore.OnUsageChanged", src);
    }

    /// <summary>
    /// …and each place asks StateFor about ITS OWN bot — a capsule painting another bot's percent would pass every test above.
    /// The card asks about its slot (transform.name); the chats header about the bot it shows (currentBotId, set by Refresh);
    /// the sheet row about the bot it was bound to (botId = bot.transform.name); the open chat's chip about the open chat's bot
    /// (ChatManager's CurrentBotId). Every StateFor call and every assignment of the key is pinned, comments stripped.
    /// </summary>
    [TestCase("Scripts/Main/Bot.cs", "transform.name", null)]
    [TestCase("Scripts/UI/ReplyModeToggleBinder.cs", "currentBotId", @"currentBotId\s*=\s*botId")]
    [TestCase("Scripts/UI/BotSwitcherRowView.cs", "botId", @"botId\s*=\s*bot\.transform\.name")]
    [TestCase("Scripts/UI/SemiAutoToggle.cs", "botKey",
        @"botKey\s*=\s*ChatManager\.Instance\s*!=\s*null\s*\?\s*ChatManager\.Instance\.CurrentBotId\s*:\s*null")]
    public void EveryAutoChip_AsksStateFor_AboutItsOwnBot(string file, string key, string everyAssignment)
    {
        string code = Code(file);
        string[] asked = Regex.Matches(code, @"\bAutoReadinessPolicy\.StateFor\(([^()]*)\)").Cast<Match>()
            .Select(m => m.Groups[1].Value.Trim()).ToArray();
        Assert.IsNotEmpty(asked, $"{file} never asks StateFor");
        Assert.IsTrue(asked.All(k => k == key), $"{file} asks StateFor about {string.Join(", ", asked)} — its own bot is {key}");
        if (everyAssignment == null) return;

        string[] assignments = Regex.Matches(code, $@"\b{Regex.Escape(key)}\s*=(?!=)[^;]*;").Cast<Match>().Select(m => m.Value).ToArray();
        Assert.IsNotEmpty(assignments, $"{file}: {key} is never assigned — where does its bot come from?");
        foreach (string assignment in assignments)
            Assert.IsTrue(Regex.IsMatch(assignment, $@"^{everyAssignment}\s*;$"), $"{file}: «{assignment}» — {key} must be this place's own bot");
    }

    [Test]
    public void TheChatsHeader_ShowsTheActiveBot_OnEnableAndOnEverySwitch()
    {
        string header = Code("Scripts/UI/ReplyModeToggleBinder.cs");
        Assert.IsTrue(Regex.IsMatch(header, @"\bvoid\s+Refresh\(\s*string\s+botId\s*\)"), "Refresh takes the bot it shows — the currentBotId StateFor is asked about");
        Assert.IsTrue(Regex.IsMatch(header, @"\bRefresh\(\s*ChatManager\.Instance\.CurrentBotId\s*\)"), "on enable: ChatManager's active bot");
        Assert.IsTrue(Regex.IsMatch(header, @"\bOnActiveBotChanged\s*\+=\s*Refresh\b"), "…and on every switch of the active bot");
    }

    [TestCase("Scripts/Main/Bot.cs")]
    [TestCase("Scripts/UI/ReplyModeToggleBinder.cs")]
    [TestCase("Scripts/UI/BotSwitcherRowView.cs")]
    [TestCase("Scripts/UI/SemiAutoToggle.cs")]
    public void EveryAutoChip_LetsGoOfTheUsageEvent_WhereItTakesIt(string file)
    {
        string src = Src(file);
        StringAssert.Contains("UsageStore.OnUsageChanged +=", src);
        StringAssert.Contains("UsageStore.OnUsageChanged -=", src, "a capsule that leaves the screen must stop repainting");
    }

    /// <summary>The names the layer builder wires through SerializedObject — a rename here silently leaves a capsule unfilled.</summary>
    [TestCase(typeof(Bot), "autoPillProgress", typeof(AutoChipProgress))]
    [TestCase(typeof(Bot), "learnCaption", typeof(TextMeshProUGUI))]
    [TestCase(typeof(ReplyModeToggleBinder), "progress", typeof(AutoChipProgress))]
    [TestCase(typeof(ReplyModeToggleBinder), "learnCaption", typeof(TextMeshProUGUI))]
    [TestCase(typeof(BotSwitcherRowView), "chipProgress", typeof(AutoChipProgress))]
    [TestCase(typeof(SemiAutoToggle), "progress", typeof(AutoChipProgress))]
    public void TheLayerFields_CarryTheNamesTheBuilderWires(Type owner, string field, Type type)
    {
        FieldInfo info = owner.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(info, $"{owner.Name}.{field}");
        Assert.AreEqual(type, info.FieldType, $"{owner.Name}.{field}");
        Assert.IsTrue(info.IsDefined(typeof(SerializeField), false), $"{owner.Name}.{field} is serialized, so the builder can wire it");
    }

    /// <summary>Owner's decision 3: the caption lives on the card and in the chats header; the row and the chat chip are too small.</summary>
    [TestCase("Scripts/UI/BotSwitcherRowView.cs")]
    [TestCase("Scripts/UI/SemiAutoToggle.cs")]
    public void TheSmallCapsules_CarryTheFillAlone_NoCaption(string file)
    {
        string src = Src(file);
        Assert.IsFalse(src.Contains("AutoChipLook.Caption(") || src.Contains("PaintCaption("), $"{file} shows only the fill");
    }

    [Test]
    public void TheConnectFace_DrawsNoFill_AndNoCaption()
    {
        string bot = Src("Scripts/Main/Bot.cs");
        StringAssert.Contains("autoPillProgress.Clear()", MethodBody(bot, "private void PaintConnectPill("),
            "«Подключить» outranks the learning fill — and Clear makes the capsule forget its percent, so the «Авто» face that " +
            "comes back is a first paint (AutoChipProgressTests)");
        StringAssert.Contains("showsConnect ? null : AutoChipLook.Caption(", bot, "…and the caption");
    }

    private static string MethodBody(string text, string signature)
    {
        int at = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.GreaterOrEqual(at, 0, $"{signature} not found");
        int open = text.IndexOf('{', at);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(open, i - open + 1);
        }
        Assert.Fail($"{signature}: unbalanced braces");
        return null;
    }
}

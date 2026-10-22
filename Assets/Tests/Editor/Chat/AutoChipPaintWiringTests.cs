using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
        StringAssert.Contains("autoPillProgress.Apply(0f, Color.clear, false)", MethodBody(bot, "private void PaintConnectPill("),
            "«Подключить» outranks the learning fill");
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

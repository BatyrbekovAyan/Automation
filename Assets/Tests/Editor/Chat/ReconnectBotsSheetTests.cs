using System;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using NUnit.Framework;
using TMPro;
using UnityEngine;

/// <summary>
/// F2 (plan 2026-10-11 §5.1–5.2, task T9): the real «Подключите ботов снова» sheet, built at runtime as on device. A row
/// per waiting channel; «Позже» (and therefore Back, which the router maps to the same <see cref="ReconnectBotsSheet.Dismiss"/>)
/// closes it and connects nothing; a row's «Подключить» closes it and then hands THAT row to the caller.
/// The taps are driven through the very <see cref="DelayedFingerUpAction"/> the sheet wired (its real-release event).
/// </summary>
public class ReconnectBotsSheetTests
{
    private static readonly List<RetiredChannelPolicy.Candidate> TwoRows = new List<RetiredChannelPolicy.Candidate>
    {
        new RetiredChannelPolicy.Candidate { BotKey = "Bot0", BotName = "Цветы", Channel = ChatChannel.WhatsApp },
        new RetiredChannelPolicy.Candidate { BotKey = "Bot2", BotName = "Ремонт", Channel = ChatChannel.Telegram },
    };

    [SetUp]
    public void SetUp() => ReconnectBotsSheet.ResetForTests();

    [TearDown]
    public void TearDown() => ReconnectBotsSheet.ResetForTests();

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform hit = Find(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>Fires the real-release event the sheet subscribed to on <paramref name="target"/> — a finished tap.</summary>
    private static void Tap(Transform target)
    {
        Assert.IsNotNull(target, "tap target");
        var handler = target.GetComponent<DelayedFingerUpAction>();
        Assert.IsNotNull(handler, $"{target.name} is wired through PopupUI.WireFingerUp");
        var field = typeof(DelayedFingerUpAction).GetField("OnRealRelease", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field, "DelayedFingerUpAction.OnRealRelease backing field");
        ((Action)field.GetValue(handler))?.Invoke();
    }

    [Test]
    public void NoWaitingChannel_ShowsNothing()
    {
        ReconnectBotsSheet.Show(new List<RetiredChannelPolicy.Candidate>(), null, null, _ => Assert.Fail("nothing to connect"));
        Assert.IsFalse(ReconnectBotsSheet.IsOpen);
        ReconnectBotsSheet.Show(null, null, null, null);
        Assert.IsFalse(ReconnectBotsSheet.IsOpen);
    }

    [Test]
    public void OneRowPerWaitingChannel_NamedByBotAndChannel()
    {
        ReconnectBotsSheet.Show(TwoRows, null, null, _ => { });
        Assert.IsTrue(ReconnectBotsSheet.IsOpen);
        Transform panel = ReconnectBotsSheet.PanelForTests.transform;
        Assert.AreEqual(RetiredChannelCopy.SheetTitle, Find(panel, "Title").GetComponent<TextMeshProUGUI>().text);
        Assert.AreEqual(RetiredChannelCopy.SheetLater, Find(Find(panel, "Later"), "Label").GetComponent<TextMeshProUGUI>().text);
        for (int i = 0; i < TwoRows.Count; i++)
        {
            Transform row = Find(panel, $"Row{i}");
            Assert.IsNotNull(row, $"row {i}");
            Assert.AreEqual(RetiredChannelCopy.SheetRow(TwoRows[i].BotName, TwoRows[i].Channel),
                row.Find("Label").GetComponent<TextMeshProUGUI>().text);
            Assert.AreEqual(RetiredChannelCopy.SheetRowCta, Find(row, "Connect").Find("Label").GetComponent<TextMeshProUGUI>().text);
        }
        Assert.IsNull(Find(panel, "Row2"), "no third row");

        ReconnectBotsSheet.Show(TwoRows.GetRange(0, 1), null, null, _ => { });
        Assert.IsNull(Find(panel, "Row1"), "a re-show rebuilds the rows — none left over from the previous list");
    }

    [Test]
    public void Later_ClosesTheSheet_AndConnectsNothing()
    {
        bool connected = false;
        ReconnectBotsSheet.Show(TwoRows, null, null, _ => connected = true);
        Tap(Find(ReconnectBotsSheet.PanelForTests.transform, "Later"));
        Assert.IsFalse(ReconnectBotsSheet.IsOpen, "«Позже» closes the sheet");
        DOTween.CompleteAll();
        Assert.IsFalse(ReconnectBotsSheet.PanelForTests.activeSelf, "and it leaves the screen");
        Assert.IsFalse(connected, "nothing is connected — the cards keep their «Подключить»");
    }

    [Test]
    public void Back_IsTheSameDismiss()
    {
        bool connected = false;
        ReconnectBotsSheet.Show(TwoRows, null, null, _ => connected = true);
        var targets = new List<BackTarget>
        {
            new BackTarget("reconnect-bots-sheet", () => ReconnectBotsSheet.IsOpen, ReconnectBotsSheet.Dismiss),
        };
        Assert.AreEqual("reconnect-bots-sheet", BackNavigation.Dispatch(targets));
        DOTween.CompleteAll();
        Assert.IsFalse(ReconnectBotsSheet.IsOpen);
        Assert.IsFalse(connected);
        Assert.IsNull(BackNavigation.Dispatch(targets), "a second press finds it closed");
    }

    [Test]
    public void ARowsConnect_ClosesFirst_ThenHandsOverThatRow()
    {
        var handed = new List<RetiredChannelPolicy.Candidate>();
        ReconnectBotsSheet.Show(TwoRows, null, null, handed.Add);
        Tap(Find(Find(ReconnectBotsSheet.PanelForTests.transform, "Row1"), "Connect"));
        Assert.IsFalse(ReconnectBotsSheet.IsOpen, "the sheet leaves before the settings slide in");
        Assert.IsEmpty(handed, "the row is handed over only once the sheet has left the screen");
        DOTween.CompleteAll();
        Assert.AreEqual(1, handed.Count);
        Assert.AreEqual("Bot2", handed[0].BotKey);
        Assert.AreEqual(ChatChannel.Telegram, handed[0].Channel);

        Tap(Find(Find(ReconnectBotsSheet.PanelForTests.transform, "Row0"), "Connect"));
        DOTween.CompleteAll();
        Assert.AreEqual(1, handed.Count, "a tap on a closed sheet connects nothing more");
    }
}

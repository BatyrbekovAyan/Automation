using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;

/// <summary>
/// Learning B: the soft lock's sheet — copy, the two actions, Back = «Понятно». The taps go through the very
/// DelayedFingerUpAction the sheet wired (PopupUI.WireFingerUp's real-release event), as ReconnectBotsSheetTests does.
/// </summary>
public class AutoLockSheetTests
{
    [SetUp] public void SetUp() => AutoLockSheet.ResetForTests();
    [TearDown] public void TearDown() => AutoLockSheet.ResetForTests();

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
    public void Copy_NamesTheProgressAndTheThreshold_NoForbiddenGlyphs()
    {
        Assert.AreEqual("ИИ-сотрудник ещё учится", AutoReadinessCopy.SheetTitle);
        string body = AutoReadinessCopy.SheetBody(7, perChat: false);
        StringAssert.Contains("Авто откроется, когда", body);                 // the LOCK lifts — nothing switches itself on
        StringAssert.DoesNotContain("включится", body);                       // review 2026-10-14 #3: no automatic switch-on promise
        StringAssert.Contains("10 раз из последних 20", body);
        StringAssert.Contains("сейчас 7 из 20", body);
        StringAssert.DoesNotContain("в этом чате", body);
        StringAssert.Contains("Включить авто-режим в этом чате можно и сейчас.", AutoReadinessCopy.SheetBody(7, perChat: true));
        Assert.AreEqual("Учится · 7/20", AutoReadinessCopy.PillText(7));
        Assert.AreEqual("Понятно", AutoReadinessCopy.Understood);
        Assert.AreEqual("Включить всё равно", AutoReadinessCopy.EnableAnyway);
        foreach (string s in new[] { AutoReadinessCopy.SheetTitle, body, AutoReadinessCopy.PillText(0), AutoReadinessCopy.Understood, AutoReadinessCopy.EnableAnyway })
            foreach (char forbidden in new[] { '№', '→', '−', '✕' })
                Assert.IsFalse(s.Contains(forbidden), $"«{forbidden}» is not in the SF Pro atlases: {s}");
    }

    [Test]
    public void Show_PaintsTheBody_ThenUnderstoodClosesWithoutProceeding()
    {
        int proceeded = 0;
        AutoLockSheet.Show(null, 7, perChat: false, () => proceeded++);
        Assert.IsTrue(AutoLockSheet.IsShowing);
        Transform panel = AutoLockSheet.PanelForTests.transform;
        Assert.AreEqual(AutoReadinessCopy.SheetBody(7, perChat: false), Find(panel, "Body").GetComponent<TextMeshProUGUI>().text);
        Assert.AreEqual(AutoReadinessCopy.Understood, Find(Find(panel, "DismissButton"), "Label").GetComponent<TextMeshProUGUI>().text);
        Assert.AreEqual(AutoReadinessCopy.EnableAnyway, Find(Find(panel, "EnableButton"), "Label").GetComponent<TextMeshProUGUI>().text);

        Tap(Find(panel, "DismissButton"));
        Assert.IsFalse(AutoLockSheet.IsShowing);
        Assert.AreEqual(0, proceeded, "«Понятно» (and Back) never enables «Авто»");
    }

    [Test]
    public void Show_ThenEnableAnywayTap_ProceedsExactlyOnce_AndCloses()
    {
        int proceeded = 0;
        AutoLockSheet.Show(null, 3, perChat: true, () => proceeded++);
        Tap(Find(AutoLockSheet.PanelForTests.transform, "EnableButton"));
        Assert.AreEqual(1, proceeded, "the soft lock stays soft: the override runs the ordinary enable path");
        Assert.IsFalse(AutoLockSheet.IsShowing);
        AutoLockSheet.EnableAnyway();   // a second call after the close is inert
        Assert.AreEqual(1, proceeded);
    }

    [Test]
    public void Back_IsUnderstood_AndAReshowRepaintsTheProgress()
    {
        int proceeded = 0;
        AutoLockSheet.Show(null, 2, perChat: false, () => proceeded++);
        AutoLockSheet.Dismiss();                       // what AndroidBackRouter calls
        Assert.IsFalse(AutoLockSheet.IsShowing);
        AutoLockSheet.Show(null, 9, perChat: false, () => proceeded++);
        Assert.IsTrue(AutoLockSheet.IsShowing);
        StringAssert.Contains("сейчас 9 из 20", Find(AutoLockSheet.PanelForTests.transform, "Body").GetComponent<TextMeshProUGUI>().text);
        Assert.AreEqual(0, proceeded);
    }
}

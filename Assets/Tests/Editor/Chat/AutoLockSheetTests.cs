using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Learning B + the percent (spec 2026-10-22 §4.5–§4.6): the soft lock's sheet — the percent copy, the big percent, the bar
/// with its marker at the unlock threshold, the geometry that stacks them, the two actions, Back = «Понятно». The taps go
/// through the very DelayedFingerUpAction the sheet wired (PopupUI.WireFingerUp's real-release event), as
/// ReconnectBotsSheetTests does. Every «N %» carries a no-break space (U+00A0) so the number never parts from the sign;
/// the tests spell it as \u00A0 — a literal one is invisible and survives no editor.
/// </summary>
public class AutoLockSheetTests
{
    private const string Almost = "Почти готово: ИИ-сотрудник уже угадывает ваши ответы, но нужны ответы ещё ";
    private const string PerChatTail = " Включить авто-режим в этом чате можно и сейчас.";

    [SetUp] public void SetUp() => AutoLockSheet.ResetForTests();
    [TearDown] public void TearDown() => AutoLockSheet.ResetForTests();

    private static LearningState Learning(int percent = 45, int unlockAt = 70) =>
        new LearningState(percent, unlockAt, capped: false, needChats: 0, needDays: 0, locked: true);

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

    private static string TextOf(Transform panel, string name)
    {
        Transform hit = Find(panel, name);
        Assert.IsNotNull(hit, $"{name} is built");
        return hit.GetComponent<TextMeshProUGUI>().text;
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

    /// <summary>The marker's centre, in the bar's own units, must sit at <paramref name="fraction"/> of the bar's width (±0.5u) and on its mid-line.</summary>
    private static void AssertMarkerAt(Transform panel, float fraction)
    {
        var bar = (RectTransform)Find(panel, "Bar");
        var marker = (RectTransform)Find(panel, "Marker");
        Vector3 centre = bar.InverseTransformPoint(marker.TransformPoint(marker.rect.center));
        Assert.AreEqual(bar.rect.xMin + fraction * bar.rect.width, centre.x, 0.5f, "the marker sits at its share of the bar's width");
        Assert.AreEqual(bar.rect.center.y, centre.y, 0.5f, "the marker is centred on the bar's mid-line");

        // «Авто» hangs under the mark: same centre line.
        var label = (RectTransform)Find(panel, "MarkerLabel");
        Vector3 labelCentre = bar.InverseTransformPoint(label.TransformPoint(label.rect.center));
        Assert.AreEqual(centre.x, labelCentre.x, 0.5f, "the label is centred under the marker");
    }

    /// <summary>Distance of the element's top edge from the card's top edge, and its height — in the card's units.</summary>
    private static (float top, float height) BoxIn(RectTransform card, Transform element)
    {
        var corners = new Vector3[4];   // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
        ((RectTransform)element).GetWorldCorners(corners);
        float top = card.rect.yMax - card.InverseTransformPoint(corners[1]).y;
        float bottom = card.rect.yMax - card.InverseTransformPoint(corners[0]).y;
        return (top, bottom - top);
    }

    // ---- copy -----------------------------------------------------------

    [Test]
    public void Copy_IsPercentBased_NoForbiddenGlyphs()
    {
        Assert.AreEqual("ИИ-сотрудник ещё учится", AutoReadinessCopy.SheetTitle);
        Assert.AreEqual("Авто", AutoReadinessCopy.MarkerLabel);
        Assert.AreEqual("Учится · 45\u00A0%", AutoReadinessCopy.Caption(45));
        Assert.AreEqual("45\u00A0%", AutoReadinessCopy.Percent(45));
        Assert.AreEqual("0\u00A0%", AutoReadinessCopy.Percent(0));
        Assert.AreEqual("100\u00A0%", AutoReadinessCopy.Percent(100));

        string plain = AutoReadinessCopy.SheetBody(70, false, 0, 0, false);
        Assert.AreEqual("«Авто» откроется на 70\u00A0%. Процент растёт, когда панель угадывает ваш ответ: "
                        + "отвечайте в режиме «Выбрать» — каждая отправка учит ИИ-сотрудника.", plain);
        StringAssert.Contains("откроется", plain);                            // the LOCK lifts — nothing switches itself on
        StringAssert.DoesNotContain("включится", plain);                      // review 2026-10-14 #3: no automatic switch-on promise
        StringAssert.DoesNotContain("в этом чате", plain);

        Assert.AreEqual(Almost + "в 2 других чатах.", AutoReadinessCopy.SheetBody(70, true, 2, 0, false));
        Assert.AreEqual(Almost + "в 1 другом чате.", AutoReadinessCopy.SheetBody(70, true, 1, 0, false));
        Assert.AreEqual(Almost + "в 1 другой день.", AutoReadinessCopy.SheetBody(70, true, 0, 1, false));
        Assert.AreEqual(Almost + "в 2 других дня.", AutoReadinessCopy.SheetBody(70, true, 0, 2, false));
        Assert.AreEqual(Almost + "в 2 других чатах и в 1 другой день.", AutoReadinessCopy.SheetBody(70, true, 2, 1, false));

        StringAssert.EndsWith(PerChatTail, AutoReadinessCopy.SheetBody(70, false, 0, 0, true));
        StringAssert.EndsWith(PerChatTail, AutoReadinessCopy.SheetBody(70, true, 2, 1, true));
        Assert.AreEqual(plain + PerChatTail, AutoReadinessCopy.SheetBody(70, false, 0, 0, true), "the per-chat tail is the old tail, appended");

        Assert.AreEqual("Понятно", AutoReadinessCopy.Understood);
        Assert.AreEqual("Включить всё равно", AutoReadinessCopy.EnableAnyway);

        // SF Pro has none of № → − ✕ (CLAUDE.md, «An authored UI character must exist in the static SF Pro atlases»).
        foreach (string s in new[]
                 {
                     AutoReadinessCopy.SheetTitle, AutoReadinessCopy.MarkerLabel, AutoReadinessCopy.Caption(100), AutoReadinessCopy.Percent(100),
                     plain, AutoReadinessCopy.SheetBody(70, false, 0, 0, true), AutoReadinessCopy.SheetBody(70, true, 5, 11, true),
                     AutoReadinessCopy.Understood, AutoReadinessCopy.EnableAnyway,
                 })
            foreach (char forbidden in new[] { '№', '→', '−', '✕' })
                Assert.IsFalse(s.Contains(forbidden), $"«{forbidden}» is not in the SF Pro atlases: {s}");
    }

    // Spec §6: RuPlural for 1, 2, 5 and 11 (and the 21 / 22 that repeat the 1 / 2 forms).
    [TestCase(1, "в 1 другом чате")]
    [TestCase(2, "в 2 других чатах")]
    [TestCase(5, "в 5 других чатах")]
    [TestCase(11, "в 11 других чатах")]
    [TestCase(21, "в 21 другом чате")]
    [TestCase(22, "в 22 других чатах")]
    public void SheetBody_Capped_AgreesTheChatsWithTheCount(int chats, string expected) =>
        Assert.AreEqual(Almost + expected + ".", AutoReadinessCopy.SheetBody(70, true, chats, 0, false));

    [TestCase(1, "в 1 другой день")]
    [TestCase(2, "в 2 других дня")]
    [TestCase(5, "в 5 других дней")]
    [TestCase(11, "в 11 других дней")]
    [TestCase(21, "в 21 другой день")]
    [TestCase(22, "в 22 других дня")]
    public void SheetBody_Capped_AgreesTheDaysWithTheCount(int days, string expected) =>
        Assert.AreEqual(Almost + expected + ".", AutoReadinessCopy.SheetBody(70, true, 0, days, false));

    [Test]
    public void SheetBody_Capped_BothPartsKeepTheirOwnForms_ChatsFirst()
    {
        Assert.AreEqual(Almost + "в 5 других чатах и в 11 других дней.", AutoReadinessCopy.SheetBody(70, true, 5, 11, false));
        Assert.AreEqual(Almost + "в 21 другом чате и в 22 других дня.", AutoReadinessCopy.SheetBody(70, true, 21, 22, false));
    }

    [Test]
    public void SheetBody_CappedWithNothingMissing_FallsBackToTheOrdinaryText()
    {
        // A `capped` the server did not back with a missing chat or day must never print «…нужны ответы ещё .».
        string ordinary = AutoReadinessCopy.SheetBody(70, false, 0, 0, false);
        Assert.AreEqual(ordinary, AutoReadinessCopy.SheetBody(70, true, 0, 0, false));
        Assert.AreEqual(ordinary, AutoReadinessCopy.SheetBody(70, true, -1, -3, false));
        Assert.AreEqual(ordinary + PerChatTail, AutoReadinessCopy.SheetBody(70, true, 0, 0, true));
    }

    // ---- the sheet ------------------------------------------------------

    [Test]
    public void Show_PaintsThePercent_TheBar_AndTheMarker()
    {
        AutoLockSheet.Show(null, new LearningState(45, 70, false, 0, 0, true), perChat: false, null);
        Transform panel = AutoLockSheet.PanelForTests.transform;

        Assert.AreEqual("45\u00A0%", TextOf(panel, "Percent"));
        var fill = (RectTransform)Find(Find(panel, "Bar"), "Fill");
        Assert.IsNotNull(fill, "Bar/Fill");
        Assert.AreEqual(0f, fill.anchorMin.x, 1e-4f, "the fill starts at the bar's left edge");
        Assert.AreEqual(0.45f, fill.anchorMax.x, 1e-4f, "…and ends at the percent (FirstStepsCard.SetProgress's pattern)");
        Assert.AreEqual(0f, fill.anchorMin.y, 1e-4f);
        Assert.AreEqual(1f, fill.anchorMax.y, 1e-4f);
        Assert.AreEqual(Vector2.zero, fill.offsetMin);
        Assert.AreEqual(Vector2.zero, fill.offsetMax);

        AssertMarkerAt(panel, 0.70f);
        Assert.AreEqual("Авто", TextOf(panel, "MarkerLabel"));
        Assert.AreEqual(AutoReadinessCopy.SheetBody(70, false, 0, 0, false), TextOf(panel, "Body"));
    }

    [TestCase(70)]
    [TestCase(35)]
    [TestCase(100)]
    public void Show_PutsTheMarkerAtTheServersThreshold_NotAtAConstant(int unlockAt)
    {
        AutoLockSheet.Show(null, Learning(percent: 20, unlockAt: unlockAt), perChat: false, null);
        AssertMarkerAt(AutoLockSheet.PanelForTests.transform, unlockAt / 100f);
    }

    [Test]
    public void Show_TheMarkerHasItsAuthoredSize()
    {
        AutoLockSheet.Show(null, Learning(), perChat: false, null);
        var marker = (RectTransform)Find(AutoLockSheet.PanelForTests.transform, "Marker");
        Assert.AreEqual(AutoLockSheet.MarkerWidth, marker.rect.width, 0.01f);
        Assert.AreEqual(AutoLockSheet.MarkerHeight, marker.rect.height, 0.01f);
    }

    [Test]
    public void Show_Capped_SaysWhatIsMissing()
    {
        AutoLockSheet.Show(null, new LearningState(69, 70, true, 2, 0, true), perChat: false, null);
        Transform panel = AutoLockSheet.PanelForTests.transform;
        Assert.AreEqual(Almost + "в 2 других чатах.", TextOf(panel, "Body"));
        Assert.AreEqual("69\u00A0%", TextOf(panel, "Percent"));
        Assert.AreEqual(0.69f, ((RectTransform)Find(Find(panel, "Bar"), "Fill")).anchorMax.x, 1e-4f);
    }

    [Test]
    public void Show_ThePerChatBody_CarriesTheChatTail()
    {
        AutoLockSheet.Show(null, Learning(), perChat: true, null);
        Assert.AreEqual(AutoReadinessCopy.SheetBody(70, false, 0, 0, true), TextOf(AutoLockSheet.PanelForTests.transform, "Body"));
    }

    [Test]
    public void Show_PaintsEveryPartInItsThemeRole()
    {
        AutoLockSheet.Show(null, Learning(), perChat: false, null);
        Transform panel = AutoLockSheet.PanelForTests.transform;
        Assert.AreEqual(Theme.Color(ThemeRole.AccentText), Find(panel, "Percent").GetComponent<TextMeshProUGUI>().color, "the big percent");
        Assert.AreEqual(Theme.Color(ThemeRole.Border), Find(panel, "Bar").GetComponent<Image>().color, "the track");
        Assert.AreEqual(Theme.Color(ThemeRole.AccentFill), Find(Find(panel, "Bar"), "Fill").GetComponent<Image>().color, "the fill");
        Assert.AreEqual(Theme.Color(ThemeRole.InkPrimary), Find(panel, "Marker").GetComponent<Image>().color, "the mark");
        Assert.AreEqual(Theme.Color(ThemeRole.InkSecondary), Find(panel, "MarkerLabel").GetComponent<TextMeshProUGUI>().color, "the mark's label");
    }

    [Test]
    public void Show_PaintsTheBody_ThenUnderstoodClosesWithoutProceeding()
    {
        int proceeded = 0;
        AutoLockSheet.Show(null, Learning(), perChat: false, () => proceeded++);
        Assert.IsTrue(AutoLockSheet.IsShowing);
        Transform panel = AutoLockSheet.PanelForTests.transform;
        Assert.AreEqual(AutoReadinessCopy.SheetBody(70, false, 0, 0, false), TextOf(panel, "Body"));
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
        AutoLockSheet.Show(null, Learning(percent: 12), perChat: true, () => proceeded++);
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
        AutoLockSheet.Show(null, Learning(percent: 20), perChat: false, () => proceeded++);
        AutoLockSheet.Dismiss();                       // what AndroidBackRouter calls
        Assert.IsFalse(AutoLockSheet.IsShowing);

        AutoLockSheet.Show(null, new LearningState(75, 80, false, 0, 0, true), perChat: false, () => proceeded++);
        Assert.IsTrue(AutoLockSheet.IsShowing);
        Transform panel = AutoLockSheet.PanelForTests.transform;
        Assert.AreEqual("75\u00A0%", TextOf(panel, "Percent"));
        Assert.AreEqual(0.75f, ((RectTransform)Find(Find(panel, "Bar"), "Fill")).anchorMax.x, 1e-4f, "the fill follows the new percent");
        AssertMarkerAt(panel, 0.80f);
        Assert.AreEqual(AutoReadinessCopy.SheetBody(80, false, 0, 0, false), TextOf(panel, "Body"));
        Assert.AreEqual(0, proceeded);
    }

    // ---- geometry -------------------------------------------------------

    [Test]
    public void Geometry_StacksTopToBottom_WithoutOverlap()
    {
        Assert.LessOrEqual(AutoLockSheet.TitleTop + AutoLockSheet.TitleHeight, AutoLockSheet.PercentTop, "the title ends above the percent");
        Assert.LessOrEqual(AutoLockSheet.PercentTop + AutoLockSheet.PercentHeight, AutoLockSheet.BarTop, "the percent ends above the bar");
        Assert.LessOrEqual(AutoLockSheet.BarTop + AutoLockSheet.BarHeight, AutoLockSheet.MarkerLabelTop, "the bar ends above the marker's label");
        Assert.LessOrEqual(AutoLockSheet.MarkerLabelTop + AutoLockSheet.MarkerLabelHeight, AutoLockSheet.BodyTop, "the label ends above the body");

        // The marker is centred on the bar and pokes out of it: it must clear the percent above and the label below too.
        float markerTop = AutoLockSheet.BarTop + AutoLockSheet.BarHeight / 2f - AutoLockSheet.MarkerHeight / 2f;
        float markerBottom = markerTop + AutoLockSheet.MarkerHeight;
        Assert.GreaterOrEqual(markerTop, AutoLockSheet.PercentTop + AutoLockSheet.PercentHeight, "the mark clears the percent");
        Assert.LessOrEqual(markerBottom, AutoLockSheet.MarkerLabelTop, "the mark clears its label");
        Assert.Greater(AutoLockSheet.MarkerHeight, AutoLockSheet.BarHeight, "the mark is taller than the track — that is what makes it a mark");

        Assert.AreEqual(608f, AutoLockSheet.CardWidth - 2f * AutoLockSheet.BarSideInset, 0.001f, "the bar spans the body's text column");
        Assert.AreEqual(AutoLockSheet.BodySideInset, AutoLockSheet.BarSideInset, 0.001f);
    }

    /// <summary>
    /// The invariant the layout lives by, whatever the title does: every part between the title and the body keeps its authored
    /// distance from the card's top PLUS the title's growth (the fitter slides the body by the same amount), so nothing overprints
    /// and every authored gap survives — title→percent, label→body, body→buttons. Also: the marker stays on the (moved) bar.
    /// Returns the title's growth so a caller can say whether the wrapped path ran.
    /// </summary>
    private static float AssertTheStackFollowsTheTitle(Transform panel, float unlockFraction)
    {
        var card = (RectTransform)Find(panel, "Content");
        (float top, float height) title = BoxIn(card, Find(panel, "Title"));
        (float top, float height) percent = BoxIn(card, Find(panel, "Percent"));
        (float top, float height) bar = BoxIn(card, Find(panel, "Bar"));
        (float top, float height) marker = BoxIn(card, Find(panel, "Marker"));
        (float top, float height) label = BoxIn(card, Find(panel, "MarkerLabel"));
        (float top, float height) body = BoxIn(card, Find(panel, "Body"));
        (float top, float height) button = BoxIn(card, Find(panel, "DismissButton"));

        float growth = title.height - AutoLockSheet.TitleHeight;
        Assert.GreaterOrEqual(growth, -0.01f, "the fitter only ever grows the title box");
        Assert.AreEqual(AutoLockSheet.TitleTop, title.top, 0.01f, "Title top");

        Assert.AreEqual(AutoLockSheet.PercentTop + growth, percent.top, 0.01f, "Percent follows the title");
        Assert.AreEqual(AutoLockSheet.PercentHeight, percent.height, 0.01f, "Percent height");
        Assert.AreEqual(AutoLockSheet.BarTop + growth, bar.top, 0.01f, "Bar follows the title");
        Assert.AreEqual(AutoLockSheet.BarHeight, bar.height, 0.01f, "Bar height");
        Assert.AreEqual(AutoLockSheet.CardWidth - 2f * AutoLockSheet.BarSideInset, ((RectTransform)Find(panel, "Bar")).rect.width, 0.01f, "Bar width");
        Assert.AreEqual(AutoLockSheet.BarTop + AutoLockSheet.BarHeight / 2f - AutoLockSheet.MarkerHeight / 2f + growth, marker.top, 0.01f, "Marker is centred on the bar");
        Assert.AreEqual(AutoLockSheet.MarkerHeight, marker.height, 0.01f, "Marker height");
        Assert.AreEqual(AutoLockSheet.MarkerLabelTop + growth, label.top, 0.01f, "MarkerLabel follows the title");
        Assert.AreEqual(AutoLockSheet.MarkerLabelHeight, label.height, 0.01f, "MarkerLabel height");

        Assert.LessOrEqual(title.top + title.height, percent.top + 0.01f, "every line of the title ends above the percent");
        Assert.AreEqual(AutoLockSheet.BodyTop - (AutoLockSheet.MarkerLabelTop + AutoLockSheet.MarkerLabelHeight),
            body.top - (label.top + label.height), 0.01f, "the authored gap between the mark's label and the body survived");
        Assert.AreEqual(24f, button.top - (body.top + body.height), 0.5f, "the authored clearance above the buttons survived");

        var bodyTmp = Find(panel, "Body").GetComponent<TextMeshProUGUI>();
        float needed = bodyTmp.GetPreferredValues(bodyTmp.rectTransform.rect.width, 32767f).y;
        Assert.Greater(needed, 0f, "the body was measured");
        Assert.GreaterOrEqual(body.height, needed - 0.5f, "the body box holds every line the text needs");

        Assert.AreEqual(AutoLockSheet.CardWidth, card.rect.width, 0.01f, "the fitter never touches the width");
        Assert.GreaterOrEqual(card.rect.height, AutoLockSheet.CardHeight - 0.01f, "the card only ever grows from its authored height");
        AssertMarkerAt(panel, unlockFraction);
        return growth;
    }

    [Test]
    public void Show_SeatsEveryPart_WhereTheConstantsPutIt()
    {
        AutoLockSheet.Show(null, Learning(), perChat: false, null);
        AssertTheStackFollowsTheTitle(AutoLockSheet.PanelForTests.transform, 0.70f);
    }

    [Test]
    public void Show_WhenTheTitleWraps_TheStackBetweenTheTitleAndTheBodyFollowsIt()
    {
        AutoLockSheet.Show(null, Learning(), perChat: false, null);
        Transform panel = AutoLockSheet.PanelForTests.transform;
        // Force a wrapped title whatever the font: the title is built once and Show never rewrites its text.
        Find(panel, "Title").GetComponent<TextMeshProUGUI>().text =
            "ИИ-сотрудник ещё учится, и пока он учится, «Авто» остаётся закрытым для этого ИИ-сотрудника";
        AutoLockSheet.Dismiss();

        AutoLockSheet.Show(null, Learning(), perChat: true, null);
        float growth = AssertTheStackFollowsTheTitle(panel, 0.70f);
        Assert.Greater(growth, 40f, "the title really wrapped (a line is ~50u), so the wrapped path ran");
    }

    /// <summary>
    /// TMP's own measurement, with the REAL fonts a scene label hands the sheet. At 44pt Bold the title is 643u wide in SF Pro
    /// Semibold (572u in Regular) against its 624u column, so under Semibold it wraps and under Regular it does not — the
    /// invariant must hold on both paths. And the longest bodies (the ordinary text, or the server's «почти готово» naming both
    /// conditions — each with the per-chat tail) outgrow their authored 180u box: the fit has to grow it and the card (a Fit that
    /// ran before PopupUI.Show measures nothing and leaves them over the buttons).
    /// Medium/Bold are left out on purpose: their font materials carry stale TMP shader ratios in the repo, and measuring text with
    /// them makes the Editor rewrite those 80MB assets.
    /// </summary>
    [TestCase("SFProText-Semibold SDF", false)]
    [TestCase("SFProText-Semibold SDF", true)]
    [TestCase("SFProText-Regular SDF", false)]
    [TestCase("SFProText-Regular SDF", true)]
    public void Show_WithTheRealFont_NothingOverprints_WhetherOrNotTheTitleWraps(string fontName, bool capped)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"Assets/TextMesh Pro/Fonts/{fontName}.asset");
        Assert.IsNotNull(font, $"{fontName} moved — update AutoLockSheetTests");

        AutoLockSheet.Show(font, new LearningState(capped ? 69 : 20, 70, capped, needChats: capped ? 11 : 0, needDays: capped ? 11 : 0, locked: true),
            perChat: true, null);
        float growth = AssertTheStackFollowsTheTitle(AutoLockSheet.PanelForTests.transform, 0.70f);

        var card = (RectTransform)Find(AutoLockSheet.PanelForTests.transform, "Content");
        TestContext.Out.WriteLine($"[FIT] {fontName} capped={capped}: title grew {growth:F1}u, card {card.rect.height:F1}u (authored {AutoLockSheet.CardHeight:F0}u)");
    }
}

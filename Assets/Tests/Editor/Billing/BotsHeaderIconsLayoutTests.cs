using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The «Боты» header icon row, and why it needs a ContentSizeFitter.
///
/// Device report 2026-08-28: with the trial active, the «Пробный · N дн.» pill hung off the
/// right edge of the screen with its tail clipped, and the «+» button was nowhere to be seen.
/// The cause is a uGUI rule that reads the opposite way round from the inspector:
/// HorizontalOrVerticalLayoutGroup.SetChildrenAlongAxis starts at <c>pos = padding.left</c>
/// and only re-seats that start from <c>childAlignment</c> inside <c>if (surplusSpace > 0)</c>.
/// So «Middle Right» holds only while the row is WIDER than its content; the moment the
/// content overflows, the row lays out from its LEFT edge and spills to the RIGHT — off the
/// screen, since the row is anchored to the screen's right edge.
///
/// HeaderIcons was authored 190 wide for the 80-wide «+» alone (surplus 110 → correct). A
/// visible pill adds ~300 + 30 spacing → content ~410 in a 190 row → overflow. The pill was
/// therefore mis-seated in EVERY trial state and correct in every state that hides it, which
/// is why it survived to a device.
///
/// EditMode cannot exercise a raycast (an unrendered canvas leaves Graphic.depth at −1) but
/// it CAN run layout, so the geometry below is the real uGUI pass, not a restatement of the
/// serialized numbers.
/// </summary>
public class BotsHeaderIconsLayoutTests
{
    // NavHeader/HeaderIcons as authored in Main.unity (1080×1920 reference units).
    private const float CanvasWidth = 1080f;
    private const float RowWidth = 190f;
    private const float RowHeight = 60f;
    private const float RowRightInset = 55f;
    private const float Spacing = 30f;
    private const float PillWidth = 300f;   // BotsPageBillingWirer's authored placeholder
    private const float PillHeight = 120f;  // taller than the row on purpose (tap target)
    private const float ButtonSize = 80f;

    private GameObject host;
    private RectTransform row;
    private RectTransform pill;
    private RectTransform button;

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        var hostRect = (RectTransform)host.transform;
        hostRect.sizeDelta = new Vector2(CanvasWidth, 300f);

        row = NewRect("HeaderIcons", hostRect);
        row.anchorMin = new Vector2(1f, 0f);
        row.anchorMax = new Vector2(1f, 0f);
        row.pivot = new Vector2(1f, 0f);
        row.anchoredPosition = new Vector2(-RowRightInset, 60f);
        row.sizeDelta = new Vector2(RowWidth, RowHeight);

        var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.childAlignment = TextAnchor.MiddleRight;
        group.spacing = Spacing;
        group.childControlWidth = false;
        group.childControlHeight = false;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;

        pill = NewRect("TrialPill", row);
        pill.sizeDelta = new Vector2(PillWidth, PillHeight);

        button = NewRect("NewBotButton", row);
        button.sizeDelta = new Vector2(ButtonSize, ButtonSize);
    }

    [TearDown]
    public void TearDown()
    {
        if (host != null) Object.DestroyImmediate(host);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private void Relayout() => LayoutRebuilder.ForceRebuildLayoutImmediate(row);

    private void AddFitter()
    {
        var fitter = row.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private static Rect WorldRect(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    /// <summary>The screen's right edge, in the same space the world rects are measured in.</summary>
    private float ScreenRight => WorldRect((RectTransform)host.transform).xMax;

    // ── The rule this fix exists for ─────────────────────────────────────────

    [Test]
    public void Narrow_row_ignores_right_alignment_and_overflows_to_the_right()
    {
        Relayout();

        Rect rowRect = WorldRect(row);
        Rect pillRect = WorldRect(pill);
        Rect buttonRect = WorldRect(button);

        // «Middle Right» does NOT hold here: the first child starts at the row's LEFT edge.
        Assert.AreEqual(rowRect.xMin, pillRect.xMin, 0.5f,
            "uGUI changed: an overflowing HorizontalLayoutGroup no longer starts at padding.left. " +
            "Re-derive the ContentSizeFitter on HeaderIcons before deleting it.");

        // …and everything past the row's width is off the screen, which is the reported bug.
        Assert.Greater(pillRect.xMax, ScreenRight, "the pill should overflow the screen without the fitter");
        Assert.Greater(buttonRect.xMin, ScreenRight, "the «+» should be pushed off screen without the fitter");
    }

    // ── What the ContentSizeFitter buys ──────────────────────────────────────

    [Test]
    public void Fitter_keeps_the_pill_on_screen_and_the_button_pinned_right()
    {
        AddFitter();
        Relayout();

        Rect rowRect = WorldRect(row);
        Rect pillRect = WorldRect(pill);
        Rect buttonRect = WorldRect(button);

        Assert.AreEqual(PillWidth + Spacing + ButtonSize, rowRect.width, 0.5f,
            "the row should size itself to pill + spacing + button");

        // The row grows leftwards: pivot.x 1 against a right anchor, so the «+» never moves.
        Assert.AreEqual(ScreenRight - RowRightInset, rowRect.xMax, 0.5f, "the row's right edge must stay pinned");
        Assert.AreEqual(rowRect.xMax, buttonRect.xMax, 0.5f, "the «+» must keep the row's right edge");

        Assert.LessOrEqual(pillRect.xMax, ScreenRight, "the pill must be fully on screen");
        Assert.AreEqual(buttonRect.xMin - Spacing, pillRect.xMax, 0.5f, "the pill must sit one spacing left of the «+»");
        Assert.Greater(pillRect.xMin, WorldRect((RectTransform)host.transform).xMin,
            "the pill must not reach the screen's left edge");
    }

    [Test]
    public void Fitter_follows_a_reworded_pill_without_moving_the_button()
    {
        AddFitter();
        Relayout();
        float buttonRight = WorldRect(button).xMax;
        float pillRight = WorldRect(pill).xMax;

        // «Пробный · 14 дн.» measures wider than «Пробный · 4 дн.»; ResizePill writes the
        // measured width onto the rect and asks for exactly this rebuild.
        pill.sizeDelta = new Vector2(PillWidth + 120f, PillHeight);
        Relayout();

        Assert.AreEqual(buttonRight, WorldRect(button).xMax, 0.5f, "a wider pill must not move the «+»");
        Assert.AreEqual(pillRight, WorldRect(pill).xMax, 0.5f, "a wider pill must grow leftwards");
        Assert.LessOrEqual(WorldRect(pill).xMax, ScreenRight, "the wider pill must stay on screen");
    }

    [Test]
    public void Fitter_shrinks_back_to_the_button_when_the_pill_is_hidden()
    {
        AddFitter();
        pill.gameObject.SetActive(false);
        Relayout();

        Rect rowRect = WorldRect(row);
        Assert.AreEqual(ButtonSize, rowRect.width, 0.5f, "a hidden pill must not leave its width behind");
        Assert.AreEqual(ScreenRight - RowRightInset, WorldRect(button).xMax, 0.5f,
            "the «+» must keep its authored place with no pill");
    }

    // ── The scene carries the fix ────────────────────────────────────────────

    [Test]
    public void Scene_HeaderIcons_carries_a_horizontal_ContentSizeFitter()
    {
        const string scenePath = "Assets/Scenes/Main.unity";
        string scene = File.ReadAllText(scenePath);

        List<string> componentIds = HeaderIconsComponentIds(scene);
        Assert.GreaterOrEqual(componentIds.Count, 2,
            "HeaderIcons component list not parsed — the guard would be a false green");

        string fitter = null;
        foreach (string id in componentIds)
        {
            string block = ObjectBlock(scene, id);
            if (block != null && block.Contains("UnityEngine.UI.ContentSizeFitter")) fitter = block;
        }

        Assert.IsNotNull(fitter,
            "NavHeader/HeaderIcons lost its ContentSizeFitter — the trial pill will run off the " +
            "right edge of the screen and take the «+» with it. Re-run Tools/Billing/Wire Bots Page Billing.");
        StringAssert.Contains("m_HorizontalFit: 2", fitter, "the fitter must fit its PREFERRED width");
        StringAssert.Contains("m_VerticalFit: 0", fitter, "the row's height is authored — vertical must stay Unconstrained");
    }

    private static List<string> HeaderIconsComponentIds(string scene)
    {
        var ids = new List<string>();
        var match = Regex.Match(scene, @"m_Component:\n((?:  - component: \{fileID: \d+\}\n)+)  m_Layer: \d+\n  m_Name: HeaderIcons\n");
        if (!match.Success) return ids;
        foreach (Match id in Regex.Matches(match.Groups[1].Value, @"fileID: (\d+)"))
            ids.Add(id.Groups[1].Value);
        return ids;
    }

    private static string ObjectBlock(string scene, string fileId)
    {
        var match = Regex.Match(scene, @"^--- !u!\d+ &" + fileId + @"\n(.*?)(?=^--- !u!|\z)",
            RegexOptions.Singleline | RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }
}

using NUnit.Framework;

// Covers ConfirmCardLayout — the pure seam behind the confirm cards shared by
// the chats-header «Авто» button, the per-chat SemiAutoToggle and the bots-page
// BotActivationConfirm.
//
// The bug it exists for (device 2026-09-04, iPhone 17 Pro Max): the per-chat
// title «Включить авто-режим в этом чате?» wraps to two lines inside a 64u box
// whose TMP overflow mode is Overflow, so its second line drew straight over
// the body — which never moved, because every element sits at an absolute
// offset inside a fixed-height card.
//
// The numbers below are MEASURED, not guessed: SF Pro Text Semibold
// (unitsPerEM 2048, pointSize 215, ascent 204.71191, descent -51.86035, so a
// line is 1.19336 em) with TMP's 7% bold spacing, wrapped greedily at the
// popup's real 640u text column. Keep them — they are what makes the
// "nothing moves today" guarantee falsifiable rather than a claim.
public class ConfirmCardLayoutTests
{
    // --- the chats popup as Main.unity authors it ------------------------

    private const float CardHeight = 440f;
    private const float TitleTop = 52f;    // Title.anchoredPosition.y = -52
    private const float TitleHeight = 64f;
    private const float BodyY = -118f;
    private const float BodyHeight = 130f;

    // Buttons are bottom-anchored: 104u tall at y = 44, so they own the card's
    // bottom 148u. Everything above that is the text block's to use.
    private const float ButtonBlock = 148f;

    // --- measured text heights -------------------------------------------

    // 42pt line = 50.12u; 34pt line = 40.57u.
    private const float TitleOneLine = 50.12f;    // «Включить авто-режим?» — 546.1u wide, fits 640
    private const float TitleTwoLines = 100.24f;  // «Включить авто-режим в этом чате?» — 825.5u wide
    private const float BodyThreeLines = 121.72f; // both current bodies wrap to three lines
    private const float BodyFourLines = 162.29f;  // a hypothetical longer body

    // --- the state that already looked right must not move ---------------

    [Test]
    public void ShortTitle_AndThreeLineBody_ChangeNothing()
    {
        var g = Solve(TitleOneLine, BodyThreeLines);

        Assert.AreEqual(TitleHeight, g.TitleHeight, 0.001f,
            "A one-line title already fits the authored 64u box — growing it would move the body for no reason");
        Assert.AreEqual(BodyY, g.BodyY, 0.001f);
        Assert.AreEqual(BodyHeight, g.BodyHeight, 0.001f);
        Assert.AreEqual(CardHeight, g.CardHeight, 0.001f,
            "Every copy that renders correctly today must be byte-identical after the fix");
    }

    // --- the reported bug -------------------------------------------------

    [Test]
    public void WrappedTitle_PushesBodyDown_InsteadOfDrawingOverIt()
    {
        var g = Solve(TitleTwoLines, BodyThreeLines);

        Assert.AreEqual(101f, g.TitleHeight, 0.001f,
            "Two 50.12u lines need 101u once ceiled — the authored 64u box is where the overlap came from");
        Assert.Less(g.BodyY, BodyY,
            "The body must move DOWN (more negative y), which is the whole fix");
        Assert.AreEqual(-155f, g.BodyY, 0.001f);
    }

    [Test]
    public void WrappedTitle_TitleAndBodyNeverOverlap()
    {
        var g = Solve(TitleTwoLines, BodyThreeLines);

        float titleBottom = TitleTop + g.TitleHeight;
        float bodyTop = -g.BodyY;

        Assert.GreaterOrEqual(bodyTop, titleBottom,
            "The body's top edge must sit at or below the title's last line");
    }

    [Test]
    public void WrappedTitle_GrowsTheCardByExactlyWhatItPushedDown()
    {
        var g = Solve(TitleTwoLines, BodyThreeLines);

        Assert.AreEqual(477f, g.CardHeight, 0.001f);
        Assert.AreEqual(CardHeight + (g.TitleHeight - TitleHeight), g.CardHeight, 0.001f);
    }

    // --- the authored gaps are the thing being preserved ------------------

    [Test]
    public void TitleToBodyGap_SurvivesGrowth()
    {
        // The gap is (bodyTop - titleTop - titleHeight); titleTop is fixed, so
        // pinning (-BodyY - TitleHeight) pins the gap without passing titleTop in.
        float authored = -BodyY - TitleHeight;

        var g = Solve(TitleTwoLines, BodyThreeLines);

        Assert.AreEqual(authored, -g.BodyY - g.TitleHeight, 0.001f,
            "The 2u breathing room the scene authored between title and body must not silently change");
    }

    [Test]
    public void BodyToButtonClearance_SurvivesGrowth()
    {
        float authored = (CardHeight - ButtonBlock) - (-BodyY + BodyHeight);
        Assert.AreEqual(44f, authored, 0.001f, "Sanity: the authored clearance is 44u");

        var g = Solve(TitleTwoLines, BodyFourLines);
        float grown = (g.CardHeight - ButtonBlock) - (-g.BodyY + g.BodyHeight);

        Assert.AreEqual(authored, grown, 0.001f,
            "Growing the card must buy room for the text, never eat the gap above the buttons");
    }

    // --- the body can be the thing that overflows -------------------------

    [Test]
    public void OverflowingBody_GrowsItsOwnBoxAndTheCard_WithoutMovingItself()
    {
        var g = Solve(TitleOneLine, BodyFourLines);

        Assert.AreEqual(163f, g.BodyHeight, 0.001f);
        Assert.AreEqual(BodyY, g.BodyY, 0.001f,
            "Only the title's growth moves the body — its own growth extends it downward");
        Assert.AreEqual(CardHeight + 33f, g.CardHeight, 0.001f);
    }

    [Test]
    public void BothOverflowing_GrowthsAddUp()
    {
        var g = Solve(TitleTwoLines, BodyFourLines);

        Assert.AreEqual(101f, g.TitleHeight, 0.001f);
        Assert.AreEqual(163f, g.BodyHeight, 0.001f);
        Assert.AreEqual(CardHeight + 37f + 33f, g.CardHeight, 0.001f);
    }

    // --- refusing to act on a measurement that isn't one ------------------

    [Test]
    public void UnmeasurableText_LeavesTheCardExactlyAsAuthored()
    {
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            var g = Solve(bad, bad);

            Assert.AreEqual(TitleHeight, g.TitleHeight, 0.001f, $"title, preferred = {bad}");
            Assert.AreEqual(BodyY, g.BodyY, 0.001f, $"body y, preferred = {bad}");
            Assert.AreEqual(BodyHeight, g.BodyHeight, 0.001f, $"body height, preferred = {bad}");
            Assert.AreEqual(CardHeight, g.CardHeight, 0.001f,
                $"An unusable measurement ({bad}) must leave the authored card alone, not collapse it");
        }
    }

    [Test]
    public void IsMeasured_AcceptsOnlyUsablePositiveNumbers()
    {
        Assert.IsTrue(ConfirmCardLayout.IsMeasured(1f));
        Assert.IsFalse(ConfirmCardLayout.IsMeasured(0f));
        Assert.IsFalse(ConfirmCardLayout.IsMeasured(-1f));
        Assert.IsFalse(ConfirmCardLayout.IsMeasured(float.NaN));
        Assert.IsFalse(ConfirmCardLayout.IsMeasured(float.PositiveInfinity));
    }

    // --- the applier calls this on every show -----------------------------

    [Test]
    public void SolvingRepeatedlyFromTheBaseline_IsIdempotent()
    {
        var first = Solve(TitleTwoLines, BodyThreeLines);
        var second = Solve(TitleTwoLines, BodyThreeLines);

        Assert.AreEqual(first.TitleHeight, second.TitleHeight, 0.001f);
        Assert.AreEqual(first.BodyY, second.BodyY, 0.001f);
        Assert.AreEqual(first.BodyHeight, second.BodyHeight, 0.001f);
        Assert.AreEqual(first.CardHeight, second.CardHeight, 0.001f,
            "ConfirmCardFitter re-solves from the captured baseline on every show — growth must never compound");
    }

    // --- a grown card still fits the screen -------------------------------

    /// <summary>
    /// The CanvasScaler is match-WIDTH, so the canvas is always 1080u wide and
    /// its height is 1080 x (screenHeight / screenWidth). ScreenContainer is
    /// inset for the 204u nav bar, which parks the card's centre 104u above the
    /// canvas centre. This checks the grown card still clears the top edge on
    /// the shortest portrait canvas the app can be shown on — the direction that
    /// would break first, since the card grows symmetrically about a centre that
    /// already sits high.
    /// </summary>
    [Test]
    public void GrownCard_ClearsTheTopEdge_OnEveryPortraitAspect()
    {
        const float centreOffset = 104f;   // ScreenContainer anchoredPosition.y

        // Worst case the rule can produce with this copy: both boxes overflow.
        var g = Solve(TitleTwoLines, BodyFourLines);
        float topEdge = centreOffset + g.CardHeight / 2f;

        foreach ((string aspect, float ratio) in new[]
                 {
                     ("19.5:9 (iPhone 17 Pro Max)", 2340f / 1080f),
                     ("16:9", 1920f / 1080f),
                     ("4:3 (Android tablet)", 1440f / 1080f),
                 })
        {
            float halfCanvas = 1080f * ratio / 2f;

            Assert.Less(topEdge, halfCanvas,
                $"A {g.CardHeight}u card centred {centreOffset}u high runs off the top at {aspect}");
        }
    }

    // --- the bots-page twin's authored card -------------------------------

    [Test]
    public void BotsPageTwin_TodaysCopy_ChangesNothing()
    {
        // BotActivationConfirm: card 720x440, title top 56 / height 60 at 44pt,
        // body top 136 / height 140 at 32pt. One 44pt line is 52.51u; the body
        // wraps to three 38.19u lines.
        var g = ConfirmCardLayout.Solve(440f, 60f, 52.51f, -136f, 140f, 114.56f);

        Assert.AreEqual(60f, g.TitleHeight, 0.001f);
        Assert.AreEqual(-136f, g.BodyY, 0.001f);
        Assert.AreEqual(140f, g.BodyHeight, 0.001f);
        Assert.AreEqual(440f, g.CardHeight, 0.001f);
    }

    // ---------------------------------------------------------------------

    private static ConfirmCardLayout.Geometry Solve(float titlePreferred, float bodyPreferred) =>
        ConfirmCardLayout.Solve(CardHeight, TitleHeight, titlePreferred, BodyY, BodyHeight, bodyPreferred);
}

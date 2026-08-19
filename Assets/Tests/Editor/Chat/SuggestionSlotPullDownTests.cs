using NUnit.Framework;

// EditMode coverage for SuggestionSlotPullDown — the pure rules of the thread pull-down, the
// SECOND way into the collapsed slot (owner request 2026-08-19) next to the 42u handle.
// Two properties are pinned here because both are invisible at the call site and expensive to
// rediscover on device:
//   (1) ENGAGE IS A POSITION TEST, not a delta test. The gesture starts when the finger crosses the
//       composer's TOP EDGE — a delta-based rule would start it wherever the finger happened to be,
//       which is the difference between "the composer follows my finger" and "the panel jumped".
//   (2) CONTINUITY. Because the line is the composer's top edge, at the engage instant the finger
//       IS that edge, so the tracking height at zero delta must equal the height already on screen.
//       Any discontinuity here shows up on device as the panel teleporting under the finger.
// The ceiling is the engage height on purpose: this gesture may shrink the slot and put it back,
// never grow it — expanding belongs to the handle.
public class SuggestionSlotPullDownTests
{
    private const float ComposerTop = 984f;   // composer's top edge with the panel at standard
    private const float Standard = 780f;      // the slot height at engage

    // --- ShouldEngage --------------------------------------------------------

    [Test]
    public void ShouldEngage_AboveTheComposer_DoesNot()
        => Assert.IsFalse(SuggestionSlotPullDown.ShouldEngage(
            ComposerTop + 1f, ComposerTop, alreadyEngaged: false, eligible: true));

    [Test]
    public void ShouldEngage_ExactlyOnTheEdge_DoesNot()
        => Assert.IsFalse(SuggestionSlotPullDown.ShouldEngage(
            ComposerTop, ComposerTop, alreadyEngaged: false, eligible: true));

    [Test]
    public void ShouldEngage_JustBelowTheEdge_Does()
        => Assert.IsTrue(SuggestionSlotPullDown.ShouldEngage(
            ComposerTop - 0.5f, ComposerTop, alreadyEngaged: false, eligible: true));

    // The grab height must be captured exactly once, or every later frame would re-origin the
    // gesture and the slot would stop following the finger.
    [Test]
    public void ShouldEngage_AlreadyEngaged_DoesNot()
        => Assert.IsFalse(SuggestionSlotPullDown.ShouldEngage(
            0f, ComposerTop, alreadyEngaged: true, eligible: true));

    [Test]
    public void ShouldEngage_Ineligible_DoesNot()
        => Assert.IsFalse(SuggestionSlotPullDown.ShouldEngage(
            0f, ComposerTop, alreadyEngaged: false, eligible: false));

    [Test]
    public void ShouldEngage_NonFiniteFinger_DoesNot()
    {
        Assert.IsFalse(SuggestionSlotPullDown.ShouldEngage(float.NaN, ComposerTop, false, true));
        Assert.IsFalse(SuggestionSlotPullDown.ShouldEngage(float.NegativeInfinity, ComposerTop, false, true));
    }

    // A broken geometry read must not become an engage line at the bottom of the world.
    [Test]
    public void ShouldEngage_NonFiniteComposerTop_DoesNot()
    {
        Assert.IsFalse(SuggestionSlotPullDown.ShouldEngage(0f, float.NaN, false, true));
        Assert.IsFalse(SuggestionSlotPullDown.ShouldEngage(0f, float.PositiveInfinity, false, true));
    }

    // --- HeightFromPull ------------------------------------------------------

    [Test]
    public void HeightFromPull_AtTheEngageInstant_IsExactlyTheEngageHeight()
        => Assert.AreEqual(Standard, SuggestionSlotPullDown.HeightFromPull(
            Standard, ComposerTop, ComposerTop), 0.0001f);

    [Test]
    public void HeightFromPull_FingerDown_ShrinksOneToOne()
        => Assert.AreEqual(Standard - 200f, SuggestionSlotPullDown.HeightFromPull(
            Standard, ComposerTop - 200f, ComposerTop), 0.0001f);

    [Test]
    public void HeightFromPull_PastTheBottom_ClampsAtZero()
        => Assert.AreEqual(0f, SuggestionSlotPullDown.HeightFromPull(
            Standard, ComposerTop - Standard - 500f, ComposerTop), 0.0001f);

    // Dragging back up restores the slot and STOPS there — the pull-down never expands.
    [Test]
    public void HeightFromPull_FingerBackUp_RestoresButNeverGrows()
    {
        Assert.AreEqual(Standard, SuggestionSlotPullDown.HeightFromPull(
            Standard, ComposerTop, ComposerTop), 0.0001f);
        Assert.AreEqual(Standard, SuggestionSlotPullDown.HeightFromPull(
            Standard, ComposerTop + 400f, ComposerTop), 0.0001f);
    }

    // A dropped pointer frame must hold the slot where it was, never teleport it.
    [Test]
    public void HeightFromPull_NonFiniteFinger_HoldsTheEngageHeight()
        => Assert.AreEqual(Standard, SuggestionSlotPullDown.HeightFromPull(
            Standard, float.NaN, ComposerTop), 0.0001f);

    [Test]
    public void HeightFromPull_EngagedAtZero_StaysAtZero()
        => Assert.AreEqual(0f, SuggestionSlotPullDown.HeightFromPull(
            0f, ComposerTop - 100f, ComposerTop), 0.0001f);
}

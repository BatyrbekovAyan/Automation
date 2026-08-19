using System.Collections.Generic;
using NUnit.Framework;

// EditMode coverage for SlotPullDownRecognizer — the thread pull-down driven through its raw
// pointer entry points, with no Unity lifecycle and no scene. This is where the gesture's
// SEQUENCING is pinned, which the pure seams cannot see:
//   · nothing moves until the finger crosses the composer's top edge (an ordinary scroll must stay
//     an ordinary scroll — the engage line sits over the lower part of the screen);
//   · the grab height is captured ONCE, so the slot keeps following the finger;
//   · over a LIVE keyboard the gesture is a ONE-SHOT dismissal and then goes inert, because Unity
//     cannot drag the native keyboard — a second fire would blur an already-blurred field;
//   · Released fires exactly once and carries the flick velocity;
//   · Reset() abandons a gesture SILENTLY, because its callers (the «+» sheet eviction, the chat
//     screen closing) already own the slot's recovery and a snap here would fight them.
// Screen px are converted with the injected canvas scale, so a 3x device drags 1:1 and not at a
// third of the finger's speed.
public class SlotPullDownRecognizerTests
{
    private const float Scale = 3f;             // screen px per canvas unit
    private const float ComposerTopScreen = 984f * Scale;
    private const float SlotHeight = 780f;

    private SlotPullDownRecognizer _r;
    private List<float> _dragged;
    private List<float> _releasedHeights;
    private List<float> _releasedVelocities;
    private int _grabs;
    private int _keyboardPulls;
    private bool _eligible;
    private bool _keyboardVisible;

    [SetUp]
    public void SetUp()
    {
        _dragged = new List<float>();
        _releasedHeights = new List<float>();
        _releasedVelocities = new List<float>();
        _grabs = 0;
        _keyboardPulls = 0;
        _eligible = true;
        _keyboardVisible = false;

        _r = new SlotPullDownRecognizer
        {
            HeightProvider = () => SlotHeight,
            ComposerTopScreenYProvider = () => ComposerTopScreen,
            CanvasScaleProvider = () => Scale,
            EligibleProvider = () => _eligible,
            KeyboardVisibleProvider = () => _keyboardVisible,
        };
        _r.Grabbed += () => _grabs++;
        _r.Dragged += h => _dragged.Add(h);
        _r.Released += (h, v) => { _releasedHeights.Add(h); _releasedVelocities.Add(v); };
        _r.KeyboardPullDown += () => _keyboardPulls++;
    }

    // --- Engagement ----------------------------------------------------------

    [Test]
    public void DragAboveTheComposer_MovesNothing()
    {
        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen + 300f, 0.02f);
        _r.PointerMoved(0, ComposerTopScreen + 100f, 0.04f);
        _r.PointerUp(0, ComposerTopScreen + 50f, 0.06f);

        Assert.AreEqual(0, _grabs);
        Assert.IsEmpty(_dragged);
        Assert.IsEmpty(_releasedHeights);
    }

    [Test]
    public void CrossingTheComposer_GrabsOnceAndTracksTheFinger()
    {
        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen + 60f, 0.02f);
        _r.PointerMoved(0, ComposerTopScreen, 0.04f);              // exactly on the edge — not yet
        _r.PointerMoved(0, ComposerTopScreen - 300f, 0.06f);       // crosses: 100 canvas units down
        _r.PointerMoved(0, ComposerTopScreen - 600f, 0.08f);       // 200 canvas units down

        Assert.AreEqual(1, _grabs);
        Assert.AreEqual(2, _dragged.Count);
        // The origin is the finger's position on the CROSSING frame, not the composer edge, so the
        // first tracked height is the height already on screen — the continuity property.
        Assert.AreEqual(SlotHeight, _dragged[0], 0.001f);
        Assert.AreEqual(SlotHeight - 100f, _dragged[1], 0.001f);
    }

    // Continuity: the first tracked height must be the height already on screen, or the panel
    // teleports on the frame the gesture starts.
    [Test]
    public void TheEngageFrameItself_ReportsTheEngageHeight()
    {
        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen - 0.003f, 0.02f);

        Assert.AreEqual(1, _dragged.Count);
        Assert.AreEqual(SlotHeight, _dragged[0], 0.01f);
    }

    [Test]
    public void FingerBackAboveTheComposer_RestoresButNeverGrows()
    {
        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen - 300f, 0.02f);
        _r.PointerMoved(0, ComposerTopScreen + 900f, 0.04f);

        Assert.AreEqual(SlotHeight, _dragged[_dragged.Count - 1], 0.001f);
    }

    [Test]
    public void Ineligible_NeverEngages()
    {
        _eligible = false;
        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen - 600f, 0.02f);
        _r.PointerUp(0, ComposerTopScreen - 600f, 0.04f);

        Assert.AreEqual(0, _grabs);
        Assert.IsEmpty(_releasedHeights);
    }

    // --- Release -------------------------------------------------------------

    [Test]
    public void Release_FiresOnceWithTheFinalHeightAndVelocity()
    {
        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen - 150f, 0.00f);
        _r.PointerMoved(0, ComposerTopScreen - 450f, 0.02f);
        _r.PointerUp(0, ComposerTopScreen - 750f, 0.04f);

        Assert.AreEqual(1, _releasedHeights.Count);
        Assert.AreEqual(SlotHeight - 200f, _releasedHeights[0], 0.001f);
        Assert.Less(_releasedVelocities[0], 0f, "a downward gesture must report a negative velocity");
        Assert.AreEqual(-5000f, _releasedVelocities[0], 1f);
    }

    [Test]
    public void ReleaseWithoutEngaging_FiresNothing()
    {
        _r.PointerDown(0);
        _r.PointerUp(0, ComposerTopScreen + 300f, 0.02f);

        Assert.IsEmpty(_releasedHeights);
    }

    [Test]
    public void ReleaseWithoutAPointerDown_IsIgnored()
    {
        _r.PointerMoved(0, ComposerTopScreen - 300f, 0.02f);
        _r.PointerUp(0, ComposerTopScreen - 300f, 0.04f);

        Assert.AreEqual(0, _grabs);
        Assert.IsEmpty(_releasedHeights);
    }

    // --- Second finger -------------------------------------------------------

    [Test]
    public void ASecondPointer_CannotDriveOrEndTheGesture()
    {
        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen - 300f, 0.02f);
        _r.PointerMoved(1, ComposerTopScreen - 900f, 0.03f);
        _r.PointerUp(1, ComposerTopScreen - 900f, 0.04f);

        Assert.AreEqual(1, _dragged.Count);
        Assert.IsEmpty(_releasedHeights);

        _r.PointerUp(0, ComposerTopScreen - 300f, 0.05f);
        Assert.AreEqual(1, _releasedHeights.Count);
    }

    // --- Keyboard branch -----------------------------------------------------

    [Test]
    public void OverALiveKeyboard_FiresTheOneShotAndNeverTracks()
    {
        _keyboardVisible = true;
        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen - 300f, 0.02f);
        _r.PointerMoved(0, ComposerTopScreen - 900f, 0.04f);
        _r.PointerUp(0, ComposerTopScreen - 900f, 0.06f);

        Assert.AreEqual(1, _keyboardPulls);
        Assert.AreEqual(0, _grabs);
        Assert.IsEmpty(_dragged);
        Assert.IsEmpty(_releasedHeights);
    }

    [Test]
    public void OverALiveKeyboard_ANewGestureCanFireAgain()
    {
        _keyboardVisible = true;
        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen - 300f, 0.02f);
        _r.PointerUp(0, ComposerTopScreen - 300f, 0.04f);

        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen - 300f, 0.06f);

        Assert.AreEqual(2, _keyboardPulls);
    }

    // --- Reset ---------------------------------------------------------------

    [Test]
    public void Reset_AbandonsTheGestureSilently()
    {
        _r.PointerDown(0);
        _r.PointerMoved(0, ComposerTopScreen - 300f, 0.02f);
        Assert.IsTrue(_r.IsEngaged);

        _r.Reset();

        Assert.IsFalse(_r.IsEngaged);
        _r.PointerMoved(0, ComposerTopScreen - 900f, 0.04f);
        _r.PointerUp(0, ComposerTopScreen - 900f, 0.06f);

        Assert.AreEqual(1, _dragged.Count, "no tracking after a reset");
        Assert.IsEmpty(_releasedHeights, "a reset must not snap");
    }

    // --- Degenerate providers -------------------------------------------------

    [Test]
    public void ADegenerateCanvasScale_DragsOneToOneInsteadOfDividingByZero()
    {
        _r.CanvasScaleProvider = () => 0f;
        _r.ComposerTopScreenYProvider = () => 984f;

        _r.PointerDown(0);
        _r.PointerMoved(0, 984f - 100f, 0.02f);   // engage
        _r.PointerMoved(0, 984f - 300f, 0.04f);   // 200 further down, at 1:1

        Assert.AreEqual(2, _dragged.Count);
        Assert.AreEqual(SlotHeight, _dragged[0], 0.001f);
        Assert.AreEqual(SlotHeight - 200f, _dragged[1], 0.001f);
    }

    [Test]
    public void ABrokenComposerReading_NeverEngages()
    {
        _r.ComposerTopScreenYProvider = () => float.NaN;

        _r.PointerDown(0);
        _r.PointerMoved(0, -100000f, 0.02f);

        Assert.AreEqual(0, _grabs);
        Assert.IsEmpty(_dragged);
    }
}

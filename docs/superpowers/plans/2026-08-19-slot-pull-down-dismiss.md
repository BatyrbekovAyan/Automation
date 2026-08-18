# Suggestions Slot Pull-Down Dismiss — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dragging the message thread downward past the composer's top edge collapses the suggestions slot with the finger (iOS interactive keyboard dismissal), as a second entry into the same mechanic the 42u drag handle already drives.

**Architecture:** The thread's `SnappyFlickScrollRect` re-broadcasts its drag stream as three neutral C# events. A plain-C# `SlotPullDownRecognizer` (owned by `SuggestionsController`, no MonoBehaviour, no scene edit) turns that stream into the same `Grabbed / Dragged / Released` events the existing handle emits, so both entries share one set of controller handlers. Over a live native keyboard the recognizer instead fires a one-shot `KeyboardPullDown`, because Unity cannot drag the native keyboard. All decision logic lives in pure static seams (`SuggestionSlotPullDown`, `SuggestionSlotDetents.SnapWithFlick`, `DragVelocitySampler`) that are fully EditMode-testable.

**Tech Stack:** Unity 6000.3.9f1, C#, uGUI (`ScrollRect`, `PointerEventData`), NUnit EditMode tests in the predefined `Assembly-CSharp-Editor` (no asmdef), DOTween (already used by the controller).

**Spec:** `docs/superpowers/specs/2026-08-19-slot-pull-down-dismiss-design.md`

## Global Constraints

- **Zero scene edits.** `Assets/Scenes/Main.unity` must not be opened, saved, or dirtied by any task. No new `[SerializeField]`, no builder, no wirer. The project has a recorded history of parallel sessions clobbering the scene.
- **All lengths are CANVAS reference units** in the safe-adjusted space (the space `KeyboardAwarePanel.VirtualBottomInset` works in), never screen pixels. Screen px appear only where a pointer position enters the system, and are divided by the canvas scale factor immediately.
- **Pointer Y is POSITIVE-IS-UP** (Unity input events, origin bottom-left). A downward flick therefore reports a NEGATIVE velocity. Do not negate anywhere.
- **New `.cs` files are silently excluded from compilation** until Unity imports them. After creating a file, confirm its `.meta` sibling exists before trusting any test result.
- **Do not modify** `SuggestionsPanel`, `KeyboardAwarePanel`, `ScrollTopInsetCompensator`, `SuggestionSlotSwap`, or `SuggestionSlotGestures`.
- **Tests live in** `Assets/Tests/Editor/Chat/`, no namespace, `using NUnit.Framework;` only. Follow the existing house style: a file-level comment block explaining WHAT invariant the file pins and WHY it is expensive to rediscover.
- **Constants:** `DragVelocitySampler.WindowSeconds = 0.08f`, `SuggestionSlotDetents.FlickVelocityCanvasPxPerSec = 2200f`, `SuggestionSlotDetents.MinFlickTravelCanvasPx = 60f`. These are device-tuning knobs; keep them `public const` in the pure seams.

## How to run the tests

The Unity Editor currently has this project open (`Temp/UnityLockfile` present), so use the **bridge**:

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Then click the Unity window once (the bridge polls only while Unity has focus) and read the result:

```bash
cat Temp/claude/test-summary.json
```

Gate on `"total"` being the full suite count (~1812+) and `"failed": 0`. A `total` of 0 is a FALSE GREEN, not a pass. The bridge runs `AssetDatabase.Refresh` before executing, which is what imports brand-new `.cs` files.

If the Editor is closed instead, use the headless runner (it refuses to run while the Editor holds the lock):

```bash
Tools/run-tests-headless.sh
```

## File Structure

**Create:**
- `Assets/Scripts/Chat/DragVelocitySampler.cs` — finger velocity from (position, time) samples; injectable clock. Shared by both drag entries.
- `Assets/Scripts/Chat/SuggestionSlotPullDown.cs` — pure engage predicate + tracking height for the pull-down.
- `Assets/Scripts/Chat/SlotPullDownRecognizer.cs` — plain-C# gesture recognizer; the only place `PointerEventData` is unpacked.
- `Assets/Tests/Editor/Chat/DragVelocitySamplerTests.cs`
- `Assets/Tests/Editor/Chat/SuggestionSlotPullDownTests.cs`
- `Assets/Tests/Editor/Chat/SlotPullDownRecognizerTests.cs`

**Modify:**
- `Assets/Scripts/Chat/SuggestionSlotDetents.cs` — add `SnapWithFlick` + its two constants.
- `Assets/Scripts/Chat/SuggestionSlotStateMachine.cs` — add `SuggestionSlotInput.PullDownDismiss` + its two transition rows.
- `Assets/Scripts/Main/SnappyFlickScrollRect.cs` — add three drag events.
- `Assets/Scripts/UI/SuggestionSlotDragHandle.cs` — `Released` carries velocity.
- `Assets/Scripts/Chat/SuggestionsController.cs` — own + wire the recognizer; flick-aware release; new vetoes.
- `Assets/Tests/Editor/Chat/SuggestionSlotDetentsTests.cs` — `SnapWithFlick` coverage.
- `Assets/Tests/Editor/Chat/SuggestionSlotStateMachineTests.cs` — `PullDownDismiss` coverage.
- `CLAUDE.md`, `.claude/skills/sketch-findings-automation/references/suggestions-panel.md` — docs.

---

### Task 1: DragVelocitySampler

**Files:**
- Create: `Assets/Scripts/Chat/DragVelocitySampler.cs`
- Test: `Assets/Tests/Editor/Chat/DragVelocitySamplerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public sealed class DragVelocitySampler` with `public const float WindowSeconds = 0.08f`, `void Reset()`, `void Sample(float canvasY, float timeSeconds)`, `float VelocityCanvasPxPerSec { get; }`.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/DragVelocitySamplerTests.cs`:

```csharp
using NUnit.Framework;

// EditMode coverage for DragVelocitySampler — the finger-speed reader behind the flick rule
// (SuggestionSlotDetents.SnapWithFlick) for BOTH slot drag entries. Pins the two properties that
// make a flick rule safe on a touch screen: velocity is averaged over a short WINDOW rather than
// taken from a single frame (one dropped or coalesced pointer frame is a spike, and a spike would
// collapse the slot off an ordinary scroll), and every degenerate input — one sample, a frozen
// clock, a non-finite coordinate — reports ZERO rather than a number, because zero is the only
// value that cannot be mistaken for a flick.
public class DragVelocitySamplerTests
{
    [Test]
    public void NoSamples_IsZero()
        => Assert.AreEqual(0f, new DragVelocitySampler().VelocityCanvasPxPerSec);

    [Test]
    public void OneSample_IsZero()
    {
        var s = new DragVelocitySampler();
        s.Sample(100f, 0f);
        Assert.AreEqual(0f, s.VelocityCanvasPxPerSec);
    }

    [Test]
    public void TwoSamples_UpwardTravel_IsPositive()
    {
        var s = new DragVelocitySampler();
        s.Sample(0f, 0f);
        s.Sample(100f, 0.05f);
        Assert.AreEqual(2000f, s.VelocityCanvasPxPerSec, 0.01f);
    }

    [Test]
    public void TwoSamples_DownwardTravel_IsNegative()
    {
        var s = new DragVelocitySampler();
        s.Sample(100f, 0f);
        s.Sample(0f, 0.05f);
        Assert.AreEqual(-2000f, s.VelocityCanvasPxPerSec, 0.01f);
    }

    [Test]
    public void FrozenClock_IsZero()
    {
        var s = new DragVelocitySampler();
        s.Sample(0f, 3f);
        s.Sample(500f, 3f);
        Assert.AreEqual(0f, s.VelocityCanvasPxPerSec);
    }

    // The whole point of the window: a finger that rested for half a second and then flicked must
    // report the FLICK, not the average of the rest and the flick.
    [Test]
    public void SamplesOlderThanWindow_AreExcluded()
    {
        var s = new DragVelocitySampler();
        s.Sample(0f, 0f);
        s.Sample(0f, 0.50f);
        s.Sample(-100f, 0.55f);
        Assert.AreEqual(-2000f, s.VelocityCanvasPxPerSec, 0.01f);
    }

    [Test]
    public void NonFiniteSample_IsDropped_AndEarlierSamplesSurvive()
    {
        var s = new DragVelocitySampler();
        s.Sample(0f, 0f);
        s.Sample(100f, 0.05f);
        s.Sample(float.NaN, 0.06f);
        s.Sample(200f, float.PositiveInfinity);
        Assert.AreEqual(2000f, s.VelocityCanvasPxPerSec, 0.01f);
    }

    [Test]
    public void Reset_ClearsTheWindow()
    {
        var s = new DragVelocitySampler();
        s.Sample(0f, 0f);
        s.Sample(100f, 0.05f);
        s.Reset();
        Assert.AreEqual(0f, s.VelocityCanvasPxPerSec);
    }

    // More samples than the ring holds: the oldest fall out, and the reported velocity stays the
    // recent one rather than wrapping to a stale slot.
    [Test]
    public void MoreSamplesThanCapacity_ReportsTheRecentWindow()
    {
        var s = new DragVelocitySampler();
        for (int i = 0; i < 40; i++) s.Sample(i * 10f, i * 0.01f);
        Assert.Greater(s.VelocityCanvasPxPerSec, 900f);
        Assert.Less(s.VelocityCanvasPxPerSec, 1100f);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Drop the trigger, focus Unity, read the summary:

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Expected: the run reports a COMPILE error (`DragVelocitySampler` could not be found). That is the correct failure — the class does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Chat/DragVelocitySampler.cs`:

```csharp
/// <summary>
/// Finger speed for the suggestions slot's two drag entries — the 42u handle
/// (<see cref="SuggestionSlotDragHandle"/>) and the thread pull-down
/// (<see cref="SlotPullDownRecognizer"/>). Keeps a short ring of (position, time) samples and
/// reports the AVERAGE velocity across the newest window, never a single-frame delta: on a touch
/// screen one dropped or coalesced pointer frame turns a steady drag into a spike, and the flick
/// rule (<see cref="SuggestionSlotDetents.SnapWithFlick"/>) would then collapse the slot off an
/// ordinary scroll.
/// <para>
/// Positions are CANVAS units on Unity's POSITIVE-IS-UP pointer axis, so a downward flick reports
/// a NEGATIVE velocity — callers must not negate. The clock is injected (callers pass
/// Time.unscaledTime), so the whole class is EditMode-testable with no Unity lifecycle.
/// </para>
/// <para>
/// Every degenerate input reports exactly 0 rather than a number: fewer than two samples, a clock
/// that did not advance, and non-finite coordinates. Zero is the only value that can never be
/// mistaken for a flick, which is what keeps a broken pointer frame from dismissing the panel.
/// </para>
/// </summary>
public sealed class DragVelocitySampler
{
    /// <summary>How far back the average reaches. Long enough to absorb a dropped frame at 60 fps,
    /// short enough that the answer describes the END of the gesture rather than its middle.</summary>
    public const float WindowSeconds = 0.08f;

    private const int Capacity = 8;

    private readonly float[] _y = new float[Capacity];
    private readonly float[] _t = new float[Capacity];
    private int _count;
    private int _head;   // next write slot

    public void Reset()
    {
        _count = 0;
        _head = 0;
    }

    /// <summary>Record one sample. A non-finite position or time is a broken pointer frame: it is
    /// DROPPED rather than stored, so the samples around it stay usable.</summary>
    public void Sample(float canvasY, float timeSeconds)
    {
        if (!float.IsFinite(canvasY) || !float.IsFinite(timeSeconds)) return;
        _y[_head] = canvasY;
        _t[_head] = timeSeconds;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    /// <summary>Canvas units per second across the newest <see cref="WindowSeconds"/>.</summary>
    public float VelocityCanvasPxPerSec
    {
        get
        {
            if (_count < 2) return 0f;

            int newest = (_head - 1 + Capacity) % Capacity;
            int oldest = newest;
            for (int i = 1; i < _count; i++)
            {
                int idx = (newest - i + Capacity) % Capacity;
                if (_t[newest] - _t[idx] > WindowSeconds) break;
                oldest = idx;
            }

            float dt = _t[newest] - _t[oldest];
            return dt > 0f ? (_y[newest] - _y[oldest]) / dt : 0f;
        }
    }
}
```

- [ ] **Step 4: Verify Unity imported the new file**

```bash
ls Assets/Scripts/Chat/DragVelocitySampler.cs.meta
```

Expected: the path prints. If it does not exist, Unity has not imported the file and any test result is stale — focus the Unity window and wait for the import spinner, then re-check.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Focus Unity, then:

```bash
cat Temp/claude/test-summary.json
```

Expected: `"failed": 0` and `"total"` at the full suite count (the 9 new tests included). A `total` of 0 is a false green — re-run.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/DragVelocitySampler.cs Assets/Scripts/Chat/DragVelocitySampler.cs.meta Assets/Tests/Editor/Chat/DragVelocitySamplerTests.cs Assets/Tests/Editor/Chat/DragVelocitySamplerTests.cs.meta
git commit -m "feat(slot): windowed finger-velocity sampler for the slot drag entries"
```

---

### Task 2: Flick-aware detent snap

**Files:**
- Modify: `Assets/Scripts/Chat/SuggestionSlotDetents.cs`
- Test: `Assets/Tests/Editor/Chat/SuggestionSlotDetentsTests.cs` (append)

**Interfaces:**
- Consumes: nothing (pure math; `DragVelocitySampler` merely produces the velocity value at runtime).
- Produces: `SuggestionSlotDetents.SnapWithFlick(float draggedCanvasPx, float standardCanvasPx, float expandedCanvasPx, float velocityCanvasPxPerSec, float travelCanvasPx, float ceilingCanvasPx) -> SlotDetent`, plus `public const float FlickVelocityCanvasPxPerSec = 2200f` and `public const float MinFlickTravelCanvasPx = 60f`.

- [ ] **Step 1: Write the failing tests**

Append to `Assets/Tests/Editor/Chat/SuggestionSlotDetentsTests.cs`, immediately before the file's closing `}`:

```csharp
    // --- SnapWithFlick: velocity beats position, but only for a real flick ---
    // The travel minimum is the load-bearing half. The pull-down's engage line sits at the
    // composer's top edge — roughly the lower 40% of the screen — so ordinary "scroll back through
    // history" gestures routinely graze it at speed. Without the minimum, every one of them would
    // report a flick and kill the panel.

    private const float FastDown = -SuggestionSlotDetents.FlickVelocityCanvasPxPerSec - 1f;
    private const float FastUp = SuggestionSlotDetents.FlickVelocityCanvasPxPerSec + 1f;
    private const float BigTravel = SuggestionSlotDetents.MinFlickTravelCanvasPx + 1f;
    private const float TinyTravel = SuggestionSlotDetents.MinFlickTravelCanvasPx - 1f;

    [Test]
    public void SnapWithFlick_FastDown_CollapsesEvenAboveHalf()
        => Assert.AreEqual(SlotDetent.Collapsed, SuggestionSlotDetents.SnapWithFlick(
            Standard - 1f, Standard, Expanded, FastDown, BigTravel, Expanded));

    [Test]
    public void SnapWithFlick_FastDown_BelowTravelMinimum_FallsBackToSnap()
        => Assert.AreEqual(
            SuggestionSlotDetents.Snap(Standard - 1f, Standard, Expanded),
            SuggestionSlotDetents.SnapWithFlick(
                Standard - 1f, Standard, Expanded, FastDown, TinyTravel, Expanded));

    [Test]
    public void SnapWithFlick_SlowDown_FallsBackToSnap()
        => Assert.AreEqual(
            SuggestionSlotDetents.Snap(Standard - 1f, Standard, Expanded),
            SuggestionSlotDetents.SnapWithFlick(
                Standard - 1f, Standard, Expanded, -10f, BigTravel, Expanded));

    // The handle's ceiling IS the expanded detent, so a flick up there may expand.
    [Test]
    public void SnapWithFlick_FastUp_HandleCeiling_Expands()
        => Assert.AreEqual(SlotDetent.Expanded, SuggestionSlotDetents.SnapWithFlick(
            10f, Standard, Expanded, FastUp, BigTravel, Expanded));

    // The pull-down's ceiling is the height it engaged at, so a flick up there RESTORES and stops.
    // This is what keeps "pull down a little, change your mind" from expanding a panel the owner
    // never dragged above standard.
    [Test]
    public void SnapWithFlick_FastUp_PullDownCeiling_StopsAtStandard()
        => Assert.AreEqual(SlotDetent.Standard, SuggestionSlotDetents.SnapWithFlick(
            10f, Standard, Expanded, FastUp, BigTravel, Standard));

    [Test]
    public void SnapWithFlick_FastUp_NoThirdDetent_StopsAtStandard()
        => Assert.AreEqual(SlotDetent.Standard, SuggestionSlotDetents.SnapWithFlick(
            10f, Standard, WithinEpsilon, FastUp, BigTravel, WithinEpsilon));

    [Test]
    public void SnapWithFlick_ExactlyAtVelocityThreshold_CountsAsFlick()
        => Assert.AreEqual(SlotDetent.Collapsed, SuggestionSlotDetents.SnapWithFlick(
            Standard, Standard, Expanded,
            -SuggestionSlotDetents.FlickVelocityCanvasPxPerSec,
            SuggestionSlotDetents.MinFlickTravelCanvasPx, Expanded));

    [Test]
    public void SnapWithFlick_ZeroVelocity_IsNeverAFlick()
        => Assert.AreEqual(
            SuggestionSlotDetents.Snap(Standard, Standard, Expanded),
            SuggestionSlotDetents.SnapWithFlick(Standard, Standard, Expanded, 0f, BigTravel, Expanded));

    // Travel is a DISTANCE: a gesture that grew the slot by a lot and flicked down still collapses.
    [Test]
    public void SnapWithFlick_NegativeTravel_IsMeasuredAsDistance()
        => Assert.AreEqual(SlotDetent.Collapsed, SuggestionSlotDetents.SnapWithFlick(
            Standard, Standard, Expanded, FastDown, -BigTravel, Expanded));

    [Test]
    public void SnapWithFlick_NonFiniteVelocity_FallsBackToSnap()
        => Assert.AreEqual(
            SuggestionSlotDetents.Snap(Standard, Standard, Expanded),
            SuggestionSlotDetents.SnapWithFlick(
                Standard, Standard, Expanded, float.NaN, BigTravel, Expanded));

    [Test]
    public void SnapWithFlick_NonFiniteTravel_FallsBackToSnap()
        => Assert.AreEqual(
            SuggestionSlotDetents.Snap(Standard, Standard, Expanded),
            SuggestionSlotDetents.SnapWithFlick(
                Standard, Standard, Expanded, FastDown, float.NaN, Expanded));

    // A ceiling read that came back broken must never be treated as "tall enough to expand".
    [Test]
    public void SnapWithFlick_FastUp_NonFiniteCeiling_StopsAtStandard()
        => Assert.AreEqual(SlotDetent.Standard, SuggestionSlotDetents.SnapWithFlick(
            10f, Standard, Expanded, FastUp, BigTravel, float.NaN));

    // With no flick the seam must be byte-identical to the rule the handle shipped with, across
    // the whole range — otherwise this task silently re-tunes the existing gesture.
    [Test]
    public void SnapWithFlick_WithoutAFlick_MatchesSnapEverywhere()
    {
        for (float h = 0f; h <= Expanded + 200f; h += 25f)
            Assert.AreEqual(
                SuggestionSlotDetents.Snap(h, Standard, Expanded),
                SuggestionSlotDetents.SnapWithFlick(h, Standard, Expanded, 0f, BigTravel, Expanded),
                $"drift at height {h}");
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Expected: compile error — `SnapWithFlick` does not exist.

- [ ] **Step 3: Write the implementation**

In `Assets/Scripts/Chat/SuggestionSlotDetents.cs`, add these members inside the `SuggestionSlotDetents` class, directly after the existing `MinThreadVisibleCanvasPx` constant:

```csharp
    /// <summary>
    /// Finger speed at which a release counts as a flick rather than a placement (canvas units per
    /// second). A device-tuning knob — it lives here so both drag entries read the same number.
    /// </summary>
    public const float FlickVelocityCanvasPxPerSec = 2200f;

    /// <summary>
    /// How far the SLOT must actually have moved before a fast release is allowed to count as a
    /// flick. Load-bearing, not a nicety: the pull-down's engage line is the composer's top edge —
    /// roughly the lower 40% of the screen — so ordinary "scroll back through history" gestures
    /// routinely cross it at speed. Without this minimum every one of them would read as a flick
    /// and collapse the panel the owner was reading.
    /// </summary>
    public const float MinFlickTravelCanvasPx = 60f;
```

And add these methods directly after the existing `Snap` method:

```csharp
    /// <summary>
    /// Where a released drag settles once speed is taken into account: a genuine flick wins over
    /// the half-way rule, everything else falls through to <see cref="Snap"/> unchanged.
    /// <para>
    /// <paramref name="velocityCanvasPxPerSec"/> is on Unity's POSITIVE-IS-UP pointer axis, so a
    /// flick DOWN is negative. <paramref name="travelCanvasPx"/> is how far the SLOT moved since
    /// the grab (a distance — the sign is ignored), not how far the finger moved.
    /// <paramref name="ceilingCanvasPx"/> is the GESTURE's own ceiling and is what makes the two
    /// entries differ on a flick up: the handle's ceiling is the expanded detent, so it may expand;
    /// the pull-down's ceiling is the height it engaged at, so it restores and stops there — that
    /// gesture is a dismissal and must never grow a panel past where the owner found it.
    /// </para>
    /// </summary>
    public static SlotDetent SnapWithFlick(
        float draggedCanvasPx, float standardCanvasPx, float expandedCanvasPx,
        float velocityCanvasPxPerSec, float travelCanvasPx, float ceilingCanvasPx)
    {
        if (!IsFlick(velocityCanvasPxPerSec, travelCanvasPx))
            return Snap(draggedCanvasPx, standardCanvasPx, expandedCanvasPx);

        return velocityCanvasPxPerSec < 0f
            ? SlotDetent.Collapsed
            : TallestUnderCeiling(standardCanvasPx, expandedCanvasPx, ceilingCanvasPx);
    }

    /// <summary>Both gates must pass. A non-finite reading of either is a broken frame and is never
    /// a flick — the position rule is always the safe fallback.</summary>
    private static bool IsFlick(float velocityCanvasPxPerSec, float travelCanvasPx)
        => float.IsFinite(velocityCanvasPxPerSec) && float.IsFinite(travelCanvasPx)
           && Mathf.Abs(velocityCanvasPxPerSec) >= FlickVelocityCanvasPxPerSec
           && Mathf.Abs(travelCanvasPx) >= MinFlickTravelCanvasPx;

    /// <summary>The tallest detent a gesture with this ceiling is allowed to land on. The 1u slack
    /// matches <see cref="HasExpandedDetent"/>'s epsilon, so a ceiling that IS the expanded detent
    /// still qualifies after float arithmetic.</summary>
    private static SlotDetent TallestUnderCeiling(
        float standardCanvasPx, float expandedCanvasPx, float ceilingCanvasPx)
        => HasExpandedDetent(standardCanvasPx, expandedCanvasPx)
           && float.IsFinite(ceilingCanvasPx)
           && expandedCanvasPx <= ceilingCanvasPx + 1f
            ? SlotDetent.Expanded
            : SlotDetent.Standard;
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Focus Unity, then `cat Temp/claude/test-summary.json`. Expected: `"failed": 0`, full `total`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/SuggestionSlotDetents.cs Assets/Tests/Editor/Chat/SuggestionSlotDetentsTests.cs
git commit -m "feat(slot): flick-aware detent snap, ceiling-scoped on the way up"
```

---

### Task 3: The pull-down's pure rules

**Files:**
- Create: `Assets/Scripts/Chat/SuggestionSlotPullDown.cs`
- Test: `Assets/Tests/Editor/Chat/SuggestionSlotPullDownTests.cs`

**Interfaces:**
- Consumes: `SuggestionSlotGestures.HeightFromDrag(float heightAtGrabCanvasPx, float dragDeltaCanvasPx, float maxCanvasPx) -> float` (existing).
- Produces: `SuggestionSlotPullDown.ShouldEngage(float fingerCanvasY, float composerTopCanvasY, bool alreadyEngaged, bool eligible) -> bool` and `SuggestionSlotPullDown.HeightFromPull(float heightAtEngageCanvasPx, float fingerCanvasY, float engageFingerCanvasY) -> float`.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/SuggestionSlotPullDownTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Expected: compile error — `SuggestionSlotPullDown` does not exist.

- [ ] **Step 3: Write the implementation**

Create `Assets/Scripts/Chat/SuggestionSlotPullDown.cs`:

```csharp
/// <summary>
/// Pure rules for the thread PULL-DOWN — the second way into the collapsed slot (owner request
/// 2026-08-19), next to the 42u handle. It mirrors iOS's interactive keyboard dismissal: while the
/// finger drags the message thread downward nothing moves, and the moment it reaches the composer's
/// top edge the slot starts tracking it 1:1.
/// <para>
/// ENGAGE IS A POSITION TEST, not a delta test. The gesture starts at the composer's top edge —
/// the line the owner actually described — and that choice is what makes the handoff continuous:
/// at the crossing instant the finger IS that edge, so <see cref="HeightFromPull"/> returns exactly
/// the height already on screen and the panel cannot jump under the finger. A delta-based rule
/// would start the gesture wherever the finger happened to be.
/// </para>
/// <para>
/// TRACKING is deliberately the handle's own arithmetic
/// (<see cref="SuggestionSlotGestures.HeightFromDrag"/>) with the ceiling pinned to the height at
/// engage: this gesture may shrink the slot and put it back, never grow it past where it started.
/// It is a dismissal, not a second way to expand — expanding stays the handle's job.
/// </para>
/// <para>
/// All lengths are CANVAS reference units in the safe-adjusted space (the space
/// KeyboardAwarePanel.VirtualBottomInset works in), on Unity's POSITIVE-IS-UP pointer axis.
/// </para>
/// </summary>
public static class SuggestionSlotPullDown
{
    /// <summary>
    /// Does this frame start the pull-down? True only on the crossing frame of an eligible gesture.
    /// <paramref name="alreadyEngaged"/> makes every later frame false so the grab height is
    /// captured exactly once — re-origining mid-gesture would stop the slot following the finger.
    /// A non-finite coordinate is a broken pointer frame or a broken geometry read and must never
    /// take the slot. <paramref name="eligible"/> is the caller's whole veto set (nothing to
    /// dismiss, a modal owning the region, the chat still opening, a back-swipe in flight) folded
    /// into one bit, so this seam stays free of scene knowledge.
    /// </summary>
    public static bool ShouldEngage(
        float fingerCanvasY, float composerTopCanvasY, bool alreadyEngaged, bool eligible)
    {
        if (alreadyEngaged || !eligible) return false;
        if (!float.IsFinite(fingerCanvasY) || !float.IsFinite(composerTopCanvasY)) return false;
        return fingerCanvasY < composerTopCanvasY;
    }

    /// <summary>
    /// Slot height while the pull-down runs. The height at engage is BOTH the origin and the
    /// ceiling. Delegates to the handle's arithmetic on purpose — one implementation of "track the
    /// finger, clamp to the floor and the ceiling, survive a dropped frame" for both entries.
    /// </summary>
    public static float HeightFromPull(
        float heightAtEngageCanvasPx, float fingerCanvasY, float engageFingerCanvasY)
        => SuggestionSlotGestures.HeightFromDrag(
            heightAtEngageCanvasPx, fingerCanvasY - engageFingerCanvasY, heightAtEngageCanvasPx);
}
```

- [ ] **Step 4: Verify Unity imported the new file**

```bash
ls Assets/Scripts/Chat/SuggestionSlotPullDown.cs.meta
```

Expected: the path prints.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Focus Unity, then `cat Temp/claude/test-summary.json`. Expected: `"failed": 0`, full `total`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/SuggestionSlotPullDown.cs Assets/Scripts/Chat/SuggestionSlotPullDown.cs.meta Assets/Tests/Editor/Chat/SuggestionSlotPullDownTests.cs Assets/Tests/Editor/Chat/SuggestionSlotPullDownTests.cs.meta
git commit -m "feat(slot): pure engage + tracking rules for the thread pull-down"
```

---

### Task 4: PullDownDismiss in the transition table

**Files:**
- Modify: `Assets/Scripts/Chat/SuggestionSlotStateMachine.cs`
- Test: `Assets/Tests/Editor/Chat/SuggestionSlotStateMachineTests.cs` (append)

**Interfaces:**
- Consumes: existing `SuggestionSlotState`, `SlotTransition`.
- Produces: `SuggestionSlotInput.PullDownDismiss` — resolves `Keyboard -> Collapsed` with `BlurField = true` in BOTH reply modes, and is inert in every other state.

- [ ] **Step 1: Write the failing tests**

Append to `Assets/Tests/Editor/Chat/SuggestionSlotStateMachineTests.cs`, immediately before the file's closing `}`:

```csharp
    // --- PullDownDismiss (owner request 2026-08-19) --------------------------
    // The thread pull-down over a LIVE keyboard. It is deliberately NOT KeyboardDismissed: that
    // input hands the slot back to the panel (the panel is the slot's default tenant), and here the
    // owner has just pushed the whole slot off the screen — a panel springing up in the keyboard's
    // place is the opposite of what the gesture asked for. Over the PANEL the pull-down is
    // interactive and resolves through AfterDrag instead, which is why every non-keyboard state is
    // inert here rather than collapsing.

    [Test]
    public void PullDownDismiss_OverTheKeyboard_CollapsesAndBlurs_SemiAuto()
    {
        SlotTransition t = SuggestionSlotStateMachine.Resolve(
            SuggestionSlotState.Keyboard, SuggestionSlotInput.PullDownDismiss, SemiAuto);
        Assert.AreEqual(SuggestionSlotState.Collapsed, t.State);
        Assert.IsTrue(t.BlurField);
        Assert.IsFalse(t.FocusField);
        Assert.IsFalse(t.ContentRefreshOnly);
    }

    [Test]
    public void PullDownDismiss_OverTheKeyboard_CollapsesAndBlurs_Auto()
    {
        SlotTransition t = SuggestionSlotStateMachine.Resolve(
            SuggestionSlotState.Keyboard, SuggestionSlotInput.PullDownDismiss, Auto);
        Assert.AreEqual(SuggestionSlotState.Collapsed, t.State);
        Assert.IsTrue(t.BlurField);
        Assert.IsFalse(t.FocusField);
    }

    // Contrast pin: the SAME state under the OTHER input must still raise the panel. If someone
    // "simplifies" PullDownDismiss into KeyboardDismissed, this pair stops disagreeing and the
    // gesture silently reverts.
    [Test]
    public void PullDownDismiss_DiffersFromKeyboardDismissed_InSemiAuto()
    {
        SlotTransition pull = SuggestionSlotStateMachine.Resolve(
            SuggestionSlotState.Keyboard, SuggestionSlotInput.PullDownDismiss, SemiAuto);
        SlotTransition dismissed = SuggestionSlotStateMachine.Resolve(
            SuggestionSlotState.Keyboard, SuggestionSlotInput.KeyboardDismissed, SemiAuto);
        Assert.AreEqual(SuggestionSlotState.Collapsed, pull.State);
        Assert.AreEqual(SuggestionSlotState.Panel, dismissed.State);
    }

    [Test]
    public void PullDownDismiss_WithoutAKeyboard_IsInert()
    {
        foreach (SuggestionSlotState state in AllStates)
        {
            if (state == SuggestionSlotState.Keyboard) continue;

            SlotTransition semi = SuggestionSlotStateMachine.Resolve(
                state, SuggestionSlotInput.PullDownDismiss, SemiAuto);
            Assert.AreEqual(state, semi.State, $"semi-auto moved from {state}");
            Assert.IsFalse(semi.BlurField, $"semi-auto blurred from {state}");
            Assert.IsFalse(semi.FocusField, $"semi-auto focused from {state}");

            // In «Авто» Panel/Expanded are normalised away, so every non-keyboard state reads as
            // Collapsed and stays there.
            SlotTransition auto = SuggestionSlotStateMachine.Resolve(
                state, SuggestionSlotInput.PullDownDismiss, Auto);
            Assert.AreEqual(SuggestionSlotState.Collapsed, auto.State, $"auto moved from {state}");
            Assert.IsFalse(auto.BlurField, $"auto blurred from {state}");
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Expected: compile error — `SuggestionSlotInput.PullDownDismiss` does not exist.

- [ ] **Step 3: Write the implementation**

In `Assets/Scripts/Chat/SuggestionSlotStateMachine.cs`, add this member to the `SuggestionSlotInput` enum, directly after `KeyboardDismissed`:

```csharp
    /// <summary>
    /// The owner dragged the message thread down past the composer while the native KEYBOARD owned
    /// the slot (the pull-down gesture, 2026-08-19). Deliberately distinct from
    /// <see cref="KeyboardDismissed"/>: that one hands the slot back to the panel, and here the
    /// owner has just pushed the whole slot off the screen. Over the PANEL the pull-down is
    /// interactive and resolves through <see cref="SuggestionSlotStateMachine.AfterDrag"/> instead.
    /// </summary>
    PullDownDismiss,
```

In `ResolveSemiAuto`, add this case directly after the `KeyboardDismissed` case:

```csharp
            case SuggestionSlotInput.PullDownDismiss:
                // Only the keyboard branch reaches here: dismissing it must land on Collapsed, NOT
                // hand the slot to the panel. Every other state is inert — the panel's own
                // pull-down is a live drag and settles through AfterDrag.
                return state == SuggestionSlotState.Keyboard
                    ? ToBlurred(SuggestionSlotState.Collapsed)
                    : To(state);
```

In `ResolveAuto`, add the same case directly after its `KeyboardDismissed` case:

```csharp
            case SuggestionSlotInput.PullDownDismiss:
                // «Авто» has no panel, so this is simply the keyboard leaving for good — the same
                // destination ThreadTap already produces here.
                return state == SuggestionSlotState.Keyboard
                    ? ToBlurred(SuggestionSlotState.Collapsed)
                    : To(state);
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Focus Unity, then `cat Temp/claude/test-summary.json`. Expected: `"failed": 0`, full `total`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/SuggestionSlotStateMachine.cs Assets/Tests/Editor/Chat/SuggestionSlotStateMachineTests.cs
git commit -m "feat(slot): PullDownDismiss collapses the slot instead of returning the panel"
```

---

### Task 5: The recognizer + the scroll's drag events

**Files:**
- Modify: `Assets/Scripts/Main/SnappyFlickScrollRect.cs`
- Create: `Assets/Scripts/Chat/SlotPullDownRecognizer.cs`
- Test: `Assets/Tests/Editor/Chat/SlotPullDownRecognizerTests.cs`

**Interfaces:**
- Consumes: `SuggestionSlotPullDown.ShouldEngage` / `.HeightFromPull` (Task 3), `DragVelocitySampler` (Task 1).
- Produces: `SnappyFlickScrollRect.DragBegan / DragMoved / DragEnded` (`event System.Action<PointerEventData>`), and `SlotPullDownRecognizer` with settable `Func<float> HeightProvider`, `Func<float> ComposerTopScreenYProvider`, `Func<float> CanvasScaleProvider`, `Func<bool> EligibleProvider`, `Func<bool> KeyboardVisibleProvider`; events `Grabbed` (`Action`), `Dragged` (`Action<float>`), `Released` (`Action<float,float>`), `KeyboardPullDown` (`Action`); methods `Attach(SnappyFlickScrollRect)`, `Detach()`, `Reset()`, `PointerDown(int)`, `PointerMoved(int, float, float)`, `PointerUp(int, float, float)`; property `bool IsEngaged`.

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/SlotPullDownRecognizerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Expected: compile error — `SlotPullDownRecognizer` does not exist.

- [ ] **Step 3: Add the drag events to the scroll**

In `Assets/Scripts/Main/SnappyFlickScrollRect.cs`, add these events directly after the `private float preDragVelocityY;` field:

```csharp
    /// <summary>
    /// The thread's drag stream, re-broadcast for gesture layers that must see EVERY drag this
    /// list receives — including the ones forwarded by a TYPED call rather than through
    /// ExecuteEvents: SwipeToReply on every bubble (`_scroll.OnDrag(e)`), DragShield, and
    /// SwipeToBack's left-band routing, which resolves to that same SwipeToReply. A component of
    /// its own on this GameObject would see only the drags that start in the gaps BETWEEN bubbles
    /// — dead over most of the thread. ScrollRect's own callbacks are the one point they converge.
    /// This class stays a plain scroll and knows nothing about its listeners.
    /// </summary>
    public event System.Action<PointerEventData> DragBegan;
    public event System.Action<PointerEventData> DragMoved;
    public event System.Action<PointerEventData> DragEnded;
```

Add `DragBegan?.Invoke(eventData);` as the LAST line of the existing `OnBeginDrag`.

Add this new override directly after `OnBeginDrag`:

```csharp
    public override void OnDrag(PointerEventData eventData)
    {
        base.OnDrag(eventData);
        DragMoved?.Invoke(eventData);
    }
```

Add `DragEnded?.Invoke(eventData);` as the LAST line of the existing `OnEndDrag` (after the flick-velocity assignment, so listeners see a settled scroll).

- [ ] **Step 4: Write the recognizer**

Create `Assets/Scripts/Chat/SlotPullDownRecognizer.cs`:

```csharp
using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Turns the message thread's own drag stream into the suggestions slot's PULL-DOWN gesture (owner
/// request 2026-08-19): drag the thread down, and the moment the finger reaches the composer's top
/// edge the slot follows it — iOS's interactive keyboard dismissal, applied to the slot the
/// suggestions panel and the native keyboard share.
/// <para>
/// Plain C#, deliberately NOT a MonoBehaviour: <see cref="SuggestionsController"/> owns one and
/// attaches it to the thread's <see cref="SnappyFlickScrollRect"/>, so the whole gesture ships with
/// ZERO scene edits — nothing to serialize, nothing to wire, no Main.unity save.
/// </para>
/// <para>
/// WHY THE SCROLLRECT AND NOT A COMPONENT OF ITS OWN: bubbles carry SwipeToReply, which forwards a
/// vertical drag with a TYPED call (`_scroll.OnDrag(e)`) instead of through ExecuteEvents; DragShield
/// does the same, and SwipeToBack's left-band routing resolves its target to that same SwipeToReply.
/// A sibling IDragHandler on the Scroll GameObject would therefore only ever see drags that start in
/// the gaps BETWEEN bubbles — dead over most of the thread. ScrollRect's own drag callbacks are the
/// one point every path lands in.
/// </para>
/// <para>
/// It emits the same three events the 42u handle does, so the controller drives both entries with a
/// single set of handlers. Over a LIVE keyboard it emits <see cref="KeyboardPullDown"/> instead and
/// goes inert for the rest of the touch: Unity cannot move the native keyboard with the finger, so
/// that branch is a one-shot dismissal rather than a track.
/// </para>
/// <para>
/// Pointer input arrives as plain floats (screen px + a clock), which is what makes the whole
/// gesture EditMode-testable; <see cref="Attach"/> is the only place PointerEventData is unpacked.
/// </para>
/// </summary>
public sealed class SlotPullDownRecognizer
{
    /// <summary>Live slot height in canvas units — the controller returns
    /// KeyboardAwarePanel.AppliedBottomInset, so engaging mid-animation catches the panel exactly
    /// where it visually is.</summary>
    public Func<float> HeightProvider;

    /// <summary>The composer's top edge in SCREEN pixels — the engage line. Polled every frame
    /// because it rides the slot inset: a value captured once would put the line in the wrong place
    /// the moment the slot is anything but the height it had at capture.</summary>
    public Func<float> ComposerTopScreenYProvider;

    /// <summary>Screen px per canvas unit. A missing or degenerate value falls back to 1 so an
    /// unwired scene drags 1:1 rather than dividing by zero.</summary>
    public Func<float> CanvasScaleProvider;

    /// <summary>The controller's whole veto set folded into one bit — see
    /// <see cref="SuggestionSlotPullDown.ShouldEngage"/>.</summary>
    public Func<bool> EligibleProvider;

    /// <summary>True while the native keyboard owns the slot; selects the one-shot branch.</summary>
    public Func<bool> KeyboardVisibleProvider;

    public event Action Grabbed;
    public event Action<float> Dragged;           // proposed slot height, canvas units
    public event Action<float, float> Released;   // final height + velocity (canvas units, canvas units/s)
    public event Action KeyboardPullDown;         // fires ONCE per gesture, over a live keyboard

    public bool IsEngaged { get; private set; }

    private readonly DragVelocitySampler _velocity = new DragVelocitySampler();
    private SnappyFlickScrollRect _scroll;
    private bool _tracking;
    private int _pointerId;
    private float _heightAtEngageCanvasPx;
    private float _engageFingerCanvasY;
    private float _lastHeightCanvasPx;

    /// <summary>Listen to a thread. Re-attaching is safe: the previous subscription is dropped
    /// first, so a controller that re-resolves its scroll can never end up double-firing.</summary>
    public void Attach(SnappyFlickScrollRect scroll)
    {
        Detach();
        _scroll = scroll;
        if (_scroll == null) return;
        _scroll.DragBegan += HandleScrollDragBegan;
        _scroll.DragMoved += HandleScrollDragMoved;
        _scroll.DragEnded += HandleScrollDragEnded;
    }

    public void Detach()
    {
        if (_scroll != null)
        {
            _scroll.DragBegan -= HandleScrollDragBegan;
            _scroll.DragMoved -= HandleScrollDragMoved;
            _scroll.DragEnded -= HandleScrollDragEnded;
            _scroll = null;
        }
        Reset();
    }

    /// <summary>
    /// Abandon the gesture WITHOUT emitting <see cref="Released"/>. Its callers — the «+» sheet
    /// evicting the slot, the chat screen closing — already own the slot's recovery, and a snap
    /// fired from here would land underneath whatever they are doing.
    /// </summary>
    public void Reset()
    {
        _tracking = false;
        IsEngaged = false;
        _velocity.Reset();
    }

    // --- PointerEventData adapters: the only Unity-typed code in this class ---

    private void HandleScrollDragBegan(PointerEventData e) => PointerDown(e.pointerId);

    private void HandleScrollDragMoved(PointerEventData e)
        => PointerMoved(e.pointerId, e.position.y, Time.unscaledTime);

    private void HandleScrollDragEnded(PointerEventData e)
        => PointerUp(e.pointerId, e.position.y, Time.unscaledTime);

    // --- Gesture core: plain floats, no Unity types --------------------------

    public void PointerDown(int pointerId)
    {
        _tracking = true;
        IsEngaged = false;
        _pointerId = pointerId;
        _velocity.Reset();
    }

    public void PointerMoved(int pointerId, float fingerScreenY, float timeSeconds)
    {
        if (!_tracking || pointerId != _pointerId) return;

        float fingerCanvasY = fingerScreenY / Scale;
        _velocity.Sample(fingerCanvasY, timeSeconds);

        if (!IsEngaged && !TryEngage(fingerCanvasY)) return;
        if (!IsEngaged) return;   // a Grabbed handler tore the screen down synchronously

        _lastHeightCanvasPx = SuggestionSlotPullDown.HeightFromPull(
            _heightAtEngageCanvasPx, fingerCanvasY, _engageFingerCanvasY);
        Dragged?.Invoke(_lastHeightCanvasPx);
    }

    public void PointerUp(int pointerId, float fingerScreenY, float timeSeconds)
    {
        if (!_tracking || pointerId != _pointerId) return;
        _tracking = false;
        if (!IsEngaged) return;

        float fingerCanvasY = fingerScreenY / Scale;
        _velocity.Sample(fingerCanvasY, timeSeconds);
        _lastHeightCanvasPx = SuggestionSlotPullDown.HeightFromPull(
            _heightAtEngageCanvasPx, fingerCanvasY, _engageFingerCanvasY);

        IsEngaged = false;   // cleared FIRST — Released must fire exactly once even if a handler
                             // re-enters, mirroring SuggestionSlotDragHandle.OnEndDrag
        Released?.Invoke(_lastHeightCanvasPx, _velocity.VelocityCanvasPxPerSec);
    }

    private bool TryEngage(float fingerCanvasY)
    {
        float composerTopScreenY = ComposerTopScreenYProvider != null
            ? ComposerTopScreenYProvider()
            : float.NaN;

        if (!SuggestionSlotPullDown.ShouldEngage(
                fingerCanvasY, composerTopScreenY / Scale, IsEngaged,
                EligibleProvider != null && EligibleProvider()))
            return false;

        // Over a LIVE keyboard there is nothing to track: Unity cannot drag the native keyboard, it
        // can only dismiss it. Fire once, stop tracking, and let it play its own animation with the
        // composer following IT rather than the finger.
        if (KeyboardVisibleProvider != null && KeyboardVisibleProvider())
        {
            _tracking = false;
            KeyboardPullDown?.Invoke();
            return false;
        }

        _heightAtEngageCanvasPx = HeightProvider != null ? HeightProvider() : 0f;
        _engageFingerCanvasY = fingerCanvasY;
        _lastHeightCanvasPx = _heightAtEngageCanvasPx;

        // Flag BEFORE the event, for the same reason SuggestionSlotDragHandle sets IsDragging
        // first: a Grabbed handler may close the chat synchronously, and Reset() must be able to
        // close a gesture that is already open rather than leaving one nothing can ever end.
        IsEngaged = true;
        Grabbed?.Invoke();
        return true;
    }

    private float Scale
    {
        get
        {
            float s = CanvasScaleProvider != null ? CanvasScaleProvider() : 1f;
            return float.IsFinite(s) && s > 0f ? s : 1f;
        }
    }
}
```

- [ ] **Step 5: Verify Unity imported the new file**

```bash
ls Assets/Scripts/Chat/SlotPullDownRecognizer.cs.meta
```

Expected: the path prints.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Focus Unity, then `cat Temp/claude/test-summary.json`. Expected: `"failed": 0`, full `total`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Main/SnappyFlickScrollRect.cs Assets/Scripts/Chat/SlotPullDownRecognizer.cs Assets/Scripts/Chat/SlotPullDownRecognizer.cs.meta Assets/Tests/Editor/Chat/SlotPullDownRecognizerTests.cs Assets/Tests/Editor/Chat/SlotPullDownRecognizerTests.cs.meta
git commit -m "feat(slot): thread pull-down recognizer off the scroll's drag stream"
```

---

### Task 6: Flick-aware release for the existing handle

**Files:**
- Modify: `Assets/Scripts/UI/SuggestionSlotDragHandle.cs`
- Modify: `Assets/Scripts/Chat/SuggestionsController.cs`

**Interfaces:**
- Consumes: `DragVelocitySampler` (Task 1), `SuggestionSlotDetents.SnapWithFlick` (Task 2).
- Produces: `SuggestionSlotDragHandle.Released` becomes `event Action<float, float>` (height, velocity). `SuggestionsController.HandleDragReleased(float finalCanvasPx, float velocityCanvasPxPerSec)`; `SuggestionsController.BeginSlotDrag(bool ceilingIsEngageHeight)`; new fields `_dragExpandedCanvasPx`, `_slotHeightAtGrabCanvasPx`.

This task changes an event signature, so the handle and the controller must move together or the project will not compile.

- [ ] **Step 1: Give the handle a velocity sampler**

In `Assets/Scripts/UI/SuggestionSlotDragHandle.cs`:

Change the `Released` event declaration from `public event Action<float> Released;` to:

```csharp
    /// <summary>Final proposed height + the finger's release velocity (canvas units per second, on
    /// Unity's POSITIVE-IS-UP axis, so a flick DOWN is negative). The CONTROLLER snaps —
    /// SuggestionSlotDetents.SnapWithFlick lets a genuine flick beat the half-way rule.</summary>
    public event Action<float, float> Released;
```

Add this field beside the other private state:

```csharp
    // Shared with the thread pull-down so the two entries into this gesture feel identical: a fast
    // release collapses (or expands) regardless of where the finger stopped.
    private readonly DragVelocitySampler _velocity = new DragVelocitySampler();
```

In `OnBeginDrag`, directly after `_grabPointerScreenY = eventData.position.y;`, add:

```csharp
        _velocity.Reset();
        _velocity.Sample(eventData.position.y / CanvasScale, Time.unscaledTime);
```

In `OnDrag`, directly after the `if (!IsDragging || eventData.pointerId != _activePointerId) return;` guard, add:

```csharp
        _velocity.Sample(eventData.position.y / CanvasScale, Time.unscaledTime);
```

In `OnEndDrag`, directly after the same guard, add the sample, and change the invocation:

```csharp
        _velocity.Sample(eventData.position.y / CanvasScale, Time.unscaledTime);
        _lastProposedCanvasPx = ProposedHeight(eventData);
        IsDragging = false;                            // cleared first — Released must fire once
        Released?.Invoke(_lastProposedCanvasPx, _velocity.VelocityCanvasPxPerSec);
```

In `OnDisable`, change the invocation to pass zero — an interrupted gesture has no release, so it can never be a flick:

```csharp
        Released?.Invoke(_lastProposedCanvasPx, 0f);
```

- [ ] **Step 2: Split the grab so each entry carries its own ceiling**

In `Assets/Scripts/Chat/SuggestionsController.cs`, add these two fields directly after the existing `private float _dragCeilingCanvasPx;`:

```csharp
    // The REAL expanded detent, captured at grab beside the ceiling. The two differ for the thread
    // pull-down, whose ceiling is the height it engaged at — Snap still needs the real detent so a
    // gesture that started at Expanded can settle back on it.
    private float _dragExpandedCanvasPx;
    // Where the slot was when the gesture began: the flick rule measures the SLOT's travel, not the
    // finger's, so a fast release that barely moved the panel is not a flick.
    private float _slotHeightAtGrabCanvasPx;
```

Replace the whole existing `HandleDragGrabbed` method with:

```csharp
    private void HandleDragGrabbed() => BeginSlotDrag(ceilingIsEngageHeight: false);

    /// <summary>
    /// The thread pull-down's ceiling is the height it engaged at, not the Expanded detent: that
    /// gesture may shrink the slot and put it back, never grow it. Dragging the finger back up past
    /// the composer must restore the panel and stop — expanding stays the handle's job.
    /// </summary>
    private void HandlePullDownGrabbed() => BeginSlotDrag(ceilingIsEngageHeight: true);

    private void BeginSlotDrag(bool ceilingIsEngageHeight)
    {
        if (_keyboardMover == null || _panel == null) return;
        _draggingSlot = true;
        _insetTween?.Kill();                       // a tween and a finger must never write the inset together
        _slotHeightAtGrabCanvasPx = _keyboardMover.AppliedBottomInset;
        _dragExpandedCanvasPx = ExpandedDetent(StandardDetent());
        _dragCeilingCanvasPx = ceilingIsEngageHeight
            ? _slotHeightAtGrabCanvasPx
            : _dragExpandedCanvasPx;
        // 1:1 finger tracking: SmoothDamp would leave the panel trailing the drag, and a smoothed
        // inset lagging a SHRINKING slot breaks FollowInset's applied ≤ slot assumption.
        _keyboardMover.TrackInsetImmediately = true;
        _panel.SetFadeSuppressed(false);           // the fade is settled again on release
    }
```

- [ ] **Step 3: Make the release flick-aware**

In the same file, change `HandleDragReleased`'s signature and its snap call. Replace:

```csharp
    private void HandleDragReleased(float finalCanvasPx)
    {
        if (!_draggingSlot) return;
        _draggingSlot = false;
        if (_keyboardMover != null) _keyboardMover.TrackInsetImmediately = false;

        float standard = StandardDetent();
        float expanded = _dragCeilingCanvasPx;
        SlotDetent snapped = SuggestionSlotDetents.Snap(finalCanvasPx, standard, expanded);
```

with:

```csharp
    private void HandleDragReleased(float finalCanvasPx, float velocityCanvasPxPerSec)
    {
        if (!_draggingSlot) return;
        _draggingSlot = false;
        if (_keyboardMover != null) _keyboardMover.TrackInsetImmediately = false;

        float standard = StandardDetent();
        float expanded = _dragExpandedCanvasPx;
        // A genuine flick beats the half-way rule; the gesture's own ceiling decides how far UP a
        // flick may land, which is what keeps the pull-down from expanding a panel the owner never
        // dragged above standard.
        SlotDetent snapped = SuggestionSlotDetents.SnapWithFlick(
            finalCanvasPx, standard, expanded, velocityCanvasPxPerSec,
            finalCanvasPx - _slotHeightAtGrabCanvasPx, _dragCeilingCanvasPx);
```

Leave the rest of the method exactly as it is.

- [ ] **Step 4: Run the tests to verify nothing regressed**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Focus Unity, then `cat Temp/claude/test-summary.json`. Expected: `"failed": 0` and the SAME `total` as after Task 5 — this task adds no tests, it must not remove any either. A compile error here means the `Released` signature change missed a subscriber.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/UI/SuggestionSlotDragHandle.cs Assets/Scripts/Chat/SuggestionsController.cs
git commit -m "feat(slot): handle release carries velocity; each entry owns its ceiling"
```

---

### Task 7: Wire the pull-down into the controller

**Files:**
- Modify: `Assets/Scripts/Chat/SuggestionsController.cs`

**Interfaces:**
- Consumes: everything produced by Tasks 1–6.
- Produces: nothing further; this is the last runtime task.

- [ ] **Step 1: Add the recognizer field**

In `Assets/Scripts/Chat/SuggestionsController.cs`, add directly after the `_slotHeightAtGrabCanvasPx` field from Task 6:

```csharp
    // The SECOND entry into the same gesture (owner request 2026-08-19): drag the thread down past
    // the composer and the slot follows the finger, iOS-style. Resolved off the thread this
    // controller already finds, so nothing is serialized and the scene is untouched.
    private readonly SlotPullDownRecognizer _pullDown = new SlotPullDownRecognizer();
    private readonly Vector3[] _composerCorners = new Vector3[4];   // reused; no GC on the drag path
```

- [ ] **Step 2: Attach it in Awake**

In `Awake`, directly AFTER the existing `_threadInset = ...` assignment (the recognizer needs the thread, which that line resolves), add:

```csharp
        var threadScroll = _threadInset != null
            ? _threadInset.GetComponent<SnappyFlickScrollRect>()
            : null;
        if (threadScroll != null)
        {
            _pullDown.HeightProvider = () => _keyboardMover != null ? _keyboardMover.AppliedBottomInset : 0f;
            _pullDown.ComposerTopScreenYProvider = ComposerTopScreenY;
            _pullDown.CanvasScaleProvider = () => CanvasScale;
            _pullDown.EligibleProvider = () => PullDownEligible;
            _pullDown.KeyboardVisibleProvider = () => _keyboardMover != null && _keyboardMover.NativeKeyboardVisible;
            _pullDown.Grabbed += HandlePullDownGrabbed;
            _pullDown.Dragged += HandleDragMoved;          // the handle's own tracking path, verbatim
            _pullDown.Released += HandleDragReleased;      // ...and its snap
            _pullDown.KeyboardPullDown += HandleKeyboardPullDown;
            _pullDown.Attach(threadScroll);
        }
```

- [ ] **Step 3: Detach in OnDestroy, reset in OnDisable and on cancel**

In `OnDestroy`, add before the closing brace:

```csharp
        _pullDown.Grabbed -= HandlePullDownGrabbed;
        _pullDown.Dragged -= HandleDragMoved;
        _pullDown.Released -= HandleDragReleased;
        _pullDown.KeyboardPullDown -= HandleKeyboardPullDown;
        _pullDown.HeightProvider = null;                   // never outlive this controller
        _pullDown.ComposerTopScreenYProvider = null;
        _pullDown.CanvasScaleProvider = null;
        _pullDown.EligibleProvider = null;
        _pullDown.KeyboardVisibleProvider = null;
        _pullDown.Detach();
```

In `OnDisable`, directly after the existing `_draggingSlot = false;` line, add:

```csharp
        _pullDown.Reset();               // a pull-down cut short by the chat closing
```

In `CancelSlotDrag`, add after the existing body:

```csharp
        _pullDown.Reset();               // the «+» sheet outranks the gesture; abandon without snapping
```

- [ ] **Step 4: Add the engage line, the veto set, and the keyboard branch**

Add these members directly after the existing `CancelSlotDrag` method:

```csharp
    /// <summary>
    /// The engage line in SCREEN pixels: the composer's top edge, read live because it rides the
    /// slot inset. Corner 1 is the rect's TOP-LEFT (GetWorldCorners returns BL, TL, TR, BR). A
    /// missing composer yields NaN, which SuggestionSlotPullDown.ShouldEngage rejects — a broken
    /// reading must never become an engage line at the bottom of the world.
    /// </summary>
    private float ComposerTopScreenY()
    {
        var rt = _bottomPanel != null ? _bottomPanel.transform as RectTransform : null;
        if (rt == null) return float.NaN;
        rt.GetWorldCorners(_composerCorners);
        Camera cam = _rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? _rootCanvas.worldCamera
            : null;
        return RectTransformUtility.WorldToScreenPoint(cam, _composerCorners[1]).y;
    }

    /// <summary>
    /// May a pull-down engage this frame? Every veto is a region that already belongs to someone
    /// else, plus the one case where there is nothing to dismiss. Deliberately NOT gated on
    /// <c>_semiAutoOn</c> (unlike PumpThreadTap): «Авто» has no panel, but the native keyboard still
    /// owns the slot there and dismissing it by scrolling is exactly the gesture being added.
    /// </summary>
    private bool PullDownEligible
    {
        get
        {
            if (_keyboardMover == null || _draggingSlot) return false;
            bool kbUp = _keyboardMover.NativeKeyboardVisible;
            if (!kbUp && !PanelOwnsSlot) return false;   // already collapsed — nothing to pull down
            if (AttachOpen || ReactionBarShowing || PhotoViewerOpen) return false;
            if (SwipeToBack.IsSliding) return false;
            return SlotOpenAllowed;
        }
    }

    /// <summary>
    /// The pull-down reached the composer while the native keyboard owned the slot. Collapse
    /// outright — deliberately NOT the KeyboardDismissed path, which hands the slot back to the
    /// panel: the owner has just pushed the whole slot off the screen, and a panel springing up in
    /// the keyboard's place is the opposite of what the gesture asked for.
    /// </summary>
    private void HandleKeyboardPullDown() => ApplySlotInput(SuggestionSlotInput.PullDownDismiss);
```

- [ ] **Step 5: Extract the photo-viewer check so both gestures share it**

Add this property directly above the existing `ReactionBarShowing` property:

```csharp
    // A modal above the thread still answers a raycast (RaycastAll does no occlusion culling), so
    // overlays are rejected by name — shared by the thread tap and the pull-down.
    private static bool PhotoViewerOpen =>
        PhotoViewer.Instance != null && PhotoViewer.Instance.panel != null
        && PhotoViewer.Instance.panel.activeSelf;
```

In `PressLandedOnThread`, replace these two lines:

```csharp
        if (PhotoViewer.Instance != null && PhotoViewer.Instance.panel != null
            && PhotoViewer.Instance.panel.activeSelf) return false;
```

with:

```csharp
        if (PhotoViewerOpen) return false;
```

- [ ] **Step 6: Run the tests to verify nothing regressed**

```bash
mkdir -p Temp/claude && : > Temp/claude/run-tests.trigger
```

Focus Unity, then `cat Temp/claude/test-summary.json`. Expected: `"failed": 0` and the same `total` as after Task 6.

- [ ] **Step 7: Verify the scene was not touched**

```bash
git status --short Assets/Scenes/Main.unity
```

Expected: NO output. Any output means the scene was dirtied — revert it with `git checkout -- Assets/Scenes/Main.unity` before committing.

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Chat/SuggestionsController.cs
git commit -m "feat(slot): thread pull-down dismisses the panel and the keyboard"
```

---

### Task 8: Documentation

**Files:**
- Modify: `.claude/skills/sketch-findings-automation/references/suggestions-panel.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: the shipped behaviour from Tasks 1–7.
- Produces: nothing in code.

- [ ] **Step 1: Amend the locked 005-E spec**

In `.claude/skills/sketch-findings-automation/references/suggestions-panel.md`, find the bullet that begins «**The handle** — grabber strip on the panel's top edge» and add this bullet directly after it:

```markdown
- **The thread pull-down — ADDED 2026-08-19 (owner request), amends this model.** Dragging the
  message thread downward past the COMPOSER'S TOP EDGE takes the slot with the finger (iOS
  `keyboardDismissMode = .interactive`). The engage line is a POSITION test, which is what makes it
  continuous: at the crossing instant the finger is that edge, so the slot starts at exactly the
  height already on screen. Its ceiling is the height it engaged at — it may shrink the slot and put
  it back, never grow it, so expanding stays the handle's. On release it uses the same detent snap
  as the handle plus a velocity rule (a genuine flick beats the half-way rule; a travel minimum
  stops an ordinary fast scroll that merely grazed the line from counting). **Over a LIVE keyboard
  it is a ONE-SHOT dismissal** — Unity cannot drag the native keyboard — and it lands on
  **collapsed**, NOT on the panel: this is the one place the «keyboard leaves ⇒ panel takes the
  slot» rule is deliberately overridden (`SuggestionSlotInput.PullDownDismiss`). Works in «Авто»
  too, where only the keyboard tenant exists.

  Consequently the line above — «`collapsed` … is reachable ONLY via the handle» — now reads
  «only via the handle or the thread pull-down». The velocity rule was added to the handle at the
  same time so the two entries feel identical.
```

- [ ] **Step 2: Record the traps in CLAUDE.md**

In `CLAUDE.md`, find the paragraph beginning «**Suggestions-slot interaction model (sketch 005-E, 2026-08-14).**» and append this sentence to the end of that paragraph (after the sentence about `SuggestionsSlotHeadlessBuild.Run`):

```markdown
**Second collapse entry — the thread pull-down (2026-08-19).** Dragging the thread down past the composer's top edge takes the slot with the finger (`SlotPullDownRecognizer` + the pure `SuggestionSlotPullDown`), and over a live keyboard it is a one-shot dismissal landing on Collapsed via `SuggestionSlotInput.PullDownDismiss` — the ONE deliberate override of «keyboard leaves ⇒ panel takes the slot». Three things are load-bearing. (1) It hangs off `SnappyFlickScrollRect`'s own drag events, NOT off a sibling `IDragHandler`: bubbles carry `SwipeToReply`, which forwards vertical drags with a TYPED `_scroll.OnDrag(e)` call rather than through `ExecuteEvents` (so does `DragShield`, and `SwipeToBack`'s left-band routing resolves to that same `SwipeToReply`), so a sibling component would only see drags starting in the gaps BETWEEN bubbles. `ScrollRect`'s callbacks are the one point every path converges on, and the component is already in the scene — the gesture ships with zero scene edits. (2) The engage test is a POSITION test against the composer's LIVE top edge, never a delta: that is what makes the handoff continuous (at the crossing instant the finger IS that edge, so the tracked height equals the height on screen and the panel cannot jump), and the line must be re-read every frame because it rides the inset. (3) The flick rule (`SuggestionSlotDetents.SnapWithFlick`, shared with the handle) needs BOTH a velocity threshold and `MinFlickTravelCanvasPx` — the engage line sits at roughly the lower 40% of the screen, so ordinary «scroll back through history» gestures cross it at speed and would otherwise collapse the panel every time; and the flick's upward outcome is scoped by the GESTURE's ceiling (the handle's is the Expanded detent, the pull-down's is its engage height), or pulling down a little and flicking back up would expand a panel the owner never dragged.
```

- [ ] **Step 3: Verify no code changed**

```bash
git status --short -- 'Assets/**'
```

Expected: no output from this task's changes (any pre-existing unrelated modifications in the working tree stay untouched — do not stage them).

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md .claude/skills/sketch-findings-automation/references/suggestions-panel.md
git commit -m "docs(slot): record the thread pull-down and its three traps"
```

---

## After the plan: device pass

None of the following is testable in EditMode; it is the owner's iOS pass, and the numbers below are the only things expected to change:

1. **1:1 feel.** The Editor takes `KeyboardAwarePanel.ApplyInstant`, where `TrackInsetImmediately` has no visible effect — the drag looks identical with the flag on or off. Only a device shows whether the panel truly tracks the finger.
2. **Thread jitter.** `ScrollTopInsetCompensator.LateUpdate` and `ScrollRect.LateUpdate` now both run while the inset moves under a finger. If the thread stutters during a collapse, give `ScrollTopInsetCompensator` a `[DefaultExecutionOrder]` that puts it after `ScrollRect` — do not pre-apply it.
3. **The two constants.** `FlickVelocityCanvasPxPerSec` (2200) and `MinFlickTravelCanvasPx` (60). If ordinary history scrolling collapses the panel too eagerly, raise the travel minimum first — it is the guard aimed at exactly that case.
4. **The keyboard branch.** Only reachable with a real native keyboard.

# Voice Waveform + Playback Speed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the audio-bubble progress slider with a tappable/scrubbable decorative waveform and add an always-visible speed pill that cycles 1× → 1.5× → 2×, for both voice notes and audio files.

**Architecture:** All decision math lives in a pure, scene-free `AudioBubbleMath` (EditMode-tested). A new `AudioWaveform` MonoBehaviour renders deterministic bars at runtime and owns tap/drag-to-seek. `AudioController` gains session-sticky speed + a real `Seek` path (the bridges already expose `Seek`/`SetSpeed` after this plan; native ExoPlayer/AVPlayer apply speed). `MessageItemView` swaps the slider for the waveform + pill.

**Tech Stack:** Unity 6 (C#), UnityEngine.UI + TMPro, NUnit (EditMode tests), ExoPlayer (Android native), AVPlayer (iOS native).

**Workflow notes (this project):**
- No git worktree — work on the current branch.
- The developer runs **EditMode tests** and compiles in their **open Unity Editor** (Window ▸ General ▸ Test Runner ▸ EditMode). Native `.java`/`.mm` changes compile only in a device build, not the Editor.
- Commits stage the `.cs` **and** its `.cs.meta` (Unity generates the `.meta` on import — focus the Editor once so the file imports before staging). Commits are subject to per-task consent during execution.
- Test the UI in Game view at **1080×2400**.

---

## File Structure

**New**
- `Assets/Scripts/Chat/AudioBubbleMath.cs` — pure static math: bar heights, played-bar split, speed cycle, fraction→seconds. No `UnityEngine` deps.
- `Assets/Scripts/UI/AudioWaveform.cs` — MonoBehaviour: runtime bar rendering + pointer seek.
- `Assets/Tests/Editor/Chat/AudioBubbleMathTests.cs` — EditMode tests for `AudioBubbleMath`.

**Modified**
- `Assets/Scripts/Chat/AudioController.cs` — add `Seek`, `SeekTo`, `CurrentSpeed`, `OnSpeedChanged`, `SetSpeed`, `CycleSpeed`.
- `Assets/Scripts/Chat/AndroidBridge.cs` — add `SetSpeed`.
- `Assets/Scripts/Chat/IOSBridge.cs` — add `SetSpeed` (+ DllImport).
- `Assets/Plugins/Android/AudioPlayer.java` — add `setSpeed`, reapply on `play`.
- `Assets/Plugins/iOS/AudioPlayer.mm` — add `setSpeed`, apply rate on play/resume.
- `Assets/Scripts/UI/MessageItemView.cs` — swap slider→waveform, add speed pill, rewire audio handlers, subscribe `OnSpeedChanged`.
- `Assets/Prefabs/MessageTextIncoming.prefab` + `Assets/Prefabs/MessageTextOutgoing.prefab` — audio panel: remove Slider, add Waveform + SpeedPill (manual Editor wiring — see Task 6).

> **Deviation from spec (flag for reviewer):** the spec proposed an `Assets/Editor/AudioBubbleBuilder.cs`. Because `AudioWaveform` generates its bars at runtime, the prefab only needs ~3 new GameObjects, so the plan uses the spec's sanctioned **manual Editor wiring** fallback instead of a blind prefab-surgery script. No `AudioBubbleBuilder.cs` is created.

---

## Task 1: `AudioBubbleMath` (pure logic, TDD)

**Files:**
- Create: `Assets/Scripts/Chat/AudioBubbleMath.cs`
- Test: `Assets/Tests/Editor/Chat/AudioBubbleMathTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/Editor/Chat/AudioBubbleMathTests.cs`:

```csharp
using NUnit.Framework;

public class AudioBubbleMathTests
{
    [Test]
    public void BarHeights_SameSeed_IsDeterministic()
    {
        var a = AudioBubbleMath.BarHeights("msg-123", 32);
        var b = AudioBubbleMath.BarHeights("msg-123", 32);
        Assert.AreEqual(a, b);
    }

    [Test]
    public void BarHeights_DifferentSeed_Differs()
    {
        var a = AudioBubbleMath.BarHeights("msg-123", 32);
        var b = AudioBubbleMath.BarHeights("msg-999", 32);
        Assert.AreNotEqual(a, b);
    }

    [Test]
    public void BarHeights_HasRequestedLength()
    {
        Assert.AreEqual(32, AudioBubbleMath.BarHeights("x", 32).Length);
    }

    [Test]
    public void BarHeights_WithinBounds()
    {
        foreach (var h in AudioBubbleMath.BarHeights("seed", 64))
        {
            Assert.GreaterOrEqual(h, AudioBubbleMath.MinBarFraction);
            Assert.LessOrEqual(h, 1f);
        }
    }

    [Test]
    public void BarHeights_NonPositiveCount_IsEmpty()
    {
        Assert.AreEqual(0, AudioBubbleMath.BarHeights("seed", 0).Length);
    }

    [Test]
    public void PlayedBarCount_Zero_None()
    {
        Assert.AreEqual(0, AudioBubbleMath.PlayedBarCount(0f, 32));
    }

    [Test]
    public void PlayedBarCount_Full_All()
    {
        Assert.AreEqual(32, AudioBubbleMath.PlayedBarCount(1f, 32));
    }

    [Test]
    public void PlayedBarCount_Half()
    {
        Assert.AreEqual(16, AudioBubbleMath.PlayedBarCount(0.5f, 32));
    }

    [Test]
    public void PlayedBarCount_ClampsAboveOne()
    {
        Assert.AreEqual(32, AudioBubbleMath.PlayedBarCount(1.4f, 32));
    }

    [Test]
    public void PlayedBarCount_ClampsBelowZero()
    {
        Assert.AreEqual(0, AudioBubbleMath.PlayedBarCount(-0.3f, 32));
    }

    [Test]
    public void NextSpeed_CyclesForward()
    {
        Assert.AreEqual(1.5f, AudioBubbleMath.NextSpeed(1f));
        Assert.AreEqual(2f, AudioBubbleMath.NextSpeed(1.5f));
        Assert.AreEqual(1f, AudioBubbleMath.NextSpeed(2f));
    }

    [Test]
    public void NextSpeed_TolerantOfDrift()
    {
        Assert.AreEqual(2f, AudioBubbleMath.NextSpeed(1.499f));
    }

    [Test]
    public void SecondsFromFraction_Endpoints()
    {
        Assert.AreEqual(0f, AudioBubbleMath.SecondsFromFraction(0f, 30));
        Assert.AreEqual(30f, AudioBubbleMath.SecondsFromFraction(1f, 30));
    }

    [Test]
    public void SecondsFromFraction_Midpoint()
    {
        Assert.AreEqual(15f, AudioBubbleMath.SecondsFromFraction(0.5f, 30));
    }

    [Test]
    public void SecondsFromFraction_ZeroDuration_IsZero()
    {
        Assert.AreEqual(0f, AudioBubbleMath.SecondsFromFraction(0.5f, 0));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

In Unity: Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All.
Expected: the test assembly **fails to compile** (red) — `AudioBubbleMath` does not exist yet. That is the expected red state.

- [ ] **Step 3: Write the minimal implementation**

Create `Assets/Scripts/Chat/AudioBubbleMath.cs`:

```csharp
using System;

/// <summary>
/// Pure, scene-free math for the audio bubble: decorative waveform heights,
/// progress split, speed cycling, and seek mapping. No UnityEngine deps so it
/// is EditMode-testable (mirrors ScrollFabMath / MediaBubbleSize).
/// </summary>
public static class AudioBubbleMath
{
    public const float MinBarFraction = 0.12f;
    public static readonly float[] SpeedSteps = { 1f, 1.5f, 2f };

    /// Deterministic bar heights in [MinBarFraction, 1] from a stable hash of the seed.
    public static float[] BarHeights(string seed, int barCount)
    {
        if (barCount <= 0) return Array.Empty<float>();

        uint state = Fnv1a(seed);
        if (state == 0) state = 1;

        var heights = new float[barCount];
        for (int i = 0; i < barCount; i++)
        {
            state = unchecked(state * 1664525u + 1013904223u); // LCG (Numerical Recipes)
            float unit = (state >> 8) / 16777216f;             // top 24 bits -> [0,1)
            heights[i] = MinBarFraction + unit * (1f - MinBarFraction);
        }
        return heights;
    }

    /// Number of leading "played" bars at the given progress fraction (0..1).
    public static int PlayedBarCount(float fraction, int barCount)
    {
        if (barCount <= 0) return 0;
        float clamped = fraction < 0f ? 0f : (fraction > 1f ? 1f : fraction);
        int played = (int)Math.Round((double)(clamped * barCount), MidpointRounding.AwayFromZero);
        if (played < 0) played = 0;
        if (played > barCount) played = barCount;
        return played;
    }

    /// Next speed in the cycle (wraps 2x -> 1x). Tolerant of float drift.
    public static float NextSpeed(float current)
    {
        int idx = 0;
        float best = float.MaxValue;
        for (int i = 0; i < SpeedSteps.Length; i++)
        {
            float d = Math.Abs(SpeedSteps[i] - current);
            if (d < best) { best = d; idx = i; }
        }
        return SpeedSteps[(idx + 1) % SpeedSteps.Length];
    }

    /// Scrub fraction (0..1) -> seek target in seconds (clamped to duration).
    public static float SecondsFromFraction(float fraction, int durationSeconds)
    {
        if (durationSeconds <= 0) return 0f;
        float clamped = fraction < 0f ? 0f : (fraction > 1f ? 1f : fraction);
        return clamped * durationSeconds;
    }

    private static uint Fnv1a(string s)
    {
        unchecked
        {
            uint hash = 2166136261u;
            if (!string.IsNullOrEmpty(s))
            {
                for (int i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= 16777619u;
                }
            }
            return hash;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

In Unity: Test Runner ▸ EditMode ▸ Run All.
Expected: all 15 `AudioBubbleMathTests` PASS (green).

- [ ] **Step 5: Commit**

After the Editor imports both new files (so `.meta` files exist):

```bash
git add Assets/Scripts/Chat/AudioBubbleMath.cs Assets/Scripts/Chat/AudioBubbleMath.cs.meta \
        Assets/Tests/Editor/Chat/AudioBubbleMathTests.cs Assets/Tests/Editor/Chat/AudioBubbleMathTests.cs.meta
git commit -m "feat(chat): add AudioBubbleMath (waveform/speed/seek math) + tests"
```

---

## Task 2: `AudioWaveform` MonoBehaviour

Renders deterministic bars at runtime (so the prefab needs no authored bars) and owns tap/drag-to-seek. Not unit-testable (UI + pointer events) — verified visually in Task 6.

**Files:**
- Create: `Assets/Scripts/UI/AudioWaveform.cs`

- [ ] **Step 1: Write the component**

Create `Assets/Scripts/UI/AudioWaveform.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Decorative voice/audio waveform. Generates bars at runtime from
/// AudioBubbleMath.BarHeights and supports tap/drag-to-seek. The prefab only
/// needs this component on a stretched RectTransform — bars are created here.
/// </summary>
public class AudioWaveform : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform barsContainer;
    [SerializeField] private Sprite barSprite;            // optional rounded sprite; null = sharp bars
    [SerializeField] private int barCount = 32;
    [SerializeField] private float barSpacing = 2f;
    [SerializeField] private Color playedColor   = new Color32(0x12, 0x8C, 0x7E, 0xFF);
    [SerializeField] private Color unplayedColor = new Color32(0xC6, 0xCD, 0xCB, 0xFF);

    /// Fired on pointer-up/tap with the seek fraction (0..1). Plain delegate
    /// (not event) so a pooled list item can reassign it cleanly on rebind.
    public Action<float> OnSeek;

    private readonly List<Image> _bars = new List<Image>();
    private float[] _heights = Array.Empty<float>();
    private float _progress;
    private bool _isDragging;

    public bool IsDragging => _isDragging;

    void Awake()
    {
        if (barsContainer == null) barsContainer = (RectTransform)transform;

        // Pointer events require a raycast-target Graphic on this object.
        var raycaster = GetComponent<Image>();
        if (raycaster == null) raycaster = gameObject.AddComponent<Image>();
        raycaster.color = new Color(0f, 0f, 0f, 0f);
        raycaster.raycastTarget = true;
    }

    public void SetSeed(string messageId)
    {
        _heights = AudioBubbleMath.BarHeights(messageId, barCount);
        EnsureBars(_heights.Length);
        LayoutBars();
        _progress = 0f;
        ApplyColors();
    }

    public void SetProgress(float fraction)
    {
        _progress = Mathf.Clamp01(fraction);
        ApplyColors();
    }

    private void EnsureBars(int count)
    {
        while (_bars.Count < count)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(barsContainer, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            if (barSprite != null) { img.sprite = barSprite; img.type = Image.Type.Sliced; }
            _bars.Add(img);
        }
        for (int i = 0; i < _bars.Count; i++)
            _bars[i].gameObject.SetActive(i < count);
    }

    private void LayoutBars()
    {
        int n = _heights.Length;
        if (n == 0) return;
        for (int i = 0; i < n; i++)
        {
            float x0 = i / (float)n;
            float x1 = (i + 1) / (float)n;
            float hFrac = Mathf.Clamp(_heights[i], 0.02f, 1f);

            var rt = _bars[i].rectTransform;
            rt.anchorMin = new Vector2(x0, 0.5f - hFrac * 0.5f);
            rt.anchorMax = new Vector2(x1, 0.5f + hFrac * 0.5f);
            rt.offsetMin = new Vector2(barSpacing * 0.5f, 0f);
            rt.offsetMax = new Vector2(-barSpacing * 0.5f, 0f);
        }
    }

    private void ApplyColors()
    {
        int played = AudioBubbleMath.PlayedBarCount(_progress, _heights.Length);
        for (int i = 0; i < _heights.Length; i++)
            _bars[i].color = i < played ? playedColor : unplayedColor;
    }

    public void OnPointerDown(PointerEventData e)
    {
        _isDragging = true;
        UpdateFromPointer(e);
    }

    public void OnDrag(PointerEventData e) => UpdateFromPointer(e);

    public void OnPointerUp(PointerEventData e)
    {
        UpdateFromPointer(e);
        _isDragging = false;
        OnSeek?.Invoke(_progress);
    }

    private void UpdateFromPointer(PointerEventData e)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                barsContainer, e.position, e.pressEventCamera, out var local))
            return;
        float w = barsContainer.rect.width;
        if (w <= 0f) return;
        SetProgress((local.x - barsContainer.rect.xMin) / w);
    }
}
```

- [ ] **Step 2: Verify it compiles**

Focus the Unity Editor so it recompiles. Expected: Console shows **no errors** (depends on `AudioBubbleMath` from Task 1).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/UI/AudioWaveform.cs Assets/Scripts/UI/AudioWaveform.cs.meta
git commit -m "feat(chat): add AudioWaveform component (runtime bars + tap/drag seek)"
```

---

## Task 3: `AudioController` + bridges — speed & seek (C#)

Adds session-sticky speed and a real `Seek` path. These compile in the Editor (native calls sit behind platform `#if`s). No unit test — pure plumbing verified on device (Task 4/6).

**Files:**
- Modify: `Assets/Scripts/Chat/AudioController.cs`
- Modify: `Assets/Scripts/Chat/AndroidBridge.cs`
- Modify: `Assets/Scripts/Chat/IOSBridge.cs`

- [ ] **Step 1: Add the speed field + event to `AudioController`**

In `Assets/Scripts/Chat/AudioController.cs`, find:

```csharp
    private string currentUrl;
    private bool isPaused = false;
```

Replace with:

```csharp
    private string currentUrl;
    private bool isPaused = false;

    public static float CurrentSpeed { get; private set; } = 1f;
    public event Action<float> OnSpeedChanged;
```

- [ ] **Step 2: Add `Seek`/`SeekTo`/`SetSpeed`/`CycleSpeed` to `AudioController`**

Find the end of `Stop()` and the start of `OnNativeProgress`:

```csharp
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidBridge.Stop();
#elif UNITY_IOS && !UNITY_EDITOR
        IOSBridge.Stop();
#endif
    }
    
    public void OnNativeProgress(string data)
```

Replace with:

```csharp
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidBridge.Stop();
#elif UNITY_IOS && !UNITY_EDITOR
        IOSBridge.Stop();
#endif
    }

    public void Seek(float seconds)
    {
        if (string.IsNullOrEmpty(currentUrl)) return;
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidBridge.Seek(seconds);
#elif UNITY_IOS && !UNITY_EDITOR
        IOSBridge.Seek(seconds);
#endif
    }

    public void SeekTo(string url, float seconds)
    {
        if (currentUrl != url) PlayAudio(url);
        Seek(seconds);
    }

    public void CycleSpeed() => SetSpeed(AudioBubbleMath.NextSpeed(CurrentSpeed));

    public void SetSpeed(float speed)
    {
        CurrentSpeed = speed;
        OnSpeedChanged?.Invoke(speed);
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidBridge.SetSpeed(speed);
#elif UNITY_IOS && !UNITY_EDITOR
        IOSBridge.SetSpeed(speed);
#endif
    }
    
    public void OnNativeProgress(string data)
```

- [ ] **Step 3: Add `SetSpeed` to `AndroidBridge`**

In `Assets/Scripts/Chat/AndroidBridge.cs`, find:

```csharp
    public static void Seek(float seconds)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
    player.CallStatic("seekTo", seconds);
#endif
    }
```

Replace with:

```csharp
    public static void Seek(float seconds)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
    player.CallStatic("seekTo", seconds);
#endif
    }

    public static void SetSpeed(float speed)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        player.CallStatic("setSpeed", speed);
#endif
    }
```

- [ ] **Step 4: Add `SetSpeed` to `IOSBridge`**

In `Assets/Scripts/Chat/IOSBridge.cs`, find:

```csharp
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void seekPlayer(float seconds);
#endif
    
    public static void Seek(float seconds)
    {
#if UNITY_IOS && !UNITY_EDITOR
    seekPlayer(seconds);
#endif
    }
```

Replace with:

```csharp
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void seekPlayer(float seconds);
#endif
    
    public static void Seek(float seconds)
    {
#if UNITY_IOS && !UNITY_EDITOR
    seekPlayer(seconds);
#endif
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void setSpeed(float speed);
#endif

    public static void SetSpeed(float speed)
    {
#if UNITY_IOS && !UNITY_EDITOR
    setSpeed(speed);
#endif
    }
```

- [ ] **Step 5: Verify it compiles**

Focus the Editor to recompile. Expected: Console shows **no errors**.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Chat/AudioController.cs Assets/Scripts/Chat/AndroidBridge.cs Assets/Scripts/Chat/IOSBridge.cs
git commit -m "feat(chat): wire AudioController seek + session-sticky playback speed"
```

---

## Task 4: Native speed (ExoPlayer + AVPlayer)

These files compile only in a device build, not the Editor. Store a `desiredSpeed` so a newly-built player honors the sticky speed; on iOS only write `rate` while playing (writing `rate` to a paused `AVPlayer` would start it).

**Files:**
- Modify: `Assets/Plugins/Android/AudioPlayer.java`
- Modify: `Assets/Plugins/iOS/AudioPlayer.mm`

- [ ] **Step 1: Android — import `PlaybackParameters`**

In `Assets/Plugins/Android/AudioPlayer.java`, find:

```java
import com.google.android.exoplayer2.MediaItem;
import com.google.android.exoplayer2.Player;
```

Replace with:

```java
import com.google.android.exoplayer2.MediaItem;
import com.google.android.exoplayer2.PlaybackParameters;
import com.google.android.exoplayer2.Player;
```

- [ ] **Step 2: Android — add the `desiredSpeed` field**

Find:

```java
    private static Runnable progressTask;
    private static String currentUrl;
```

Replace with:

```java
    private static Runnable progressTask;
    private static String currentUrl;
    private static float desiredSpeed = 1.0f;
```

- [ ] **Step 3: Android — reapply speed when a new player is built**

Find:

```java
            player.prepare();
            player.play();

            startProgressUpdates();
```

Replace with:

```java
            player.prepare();
            player.setPlaybackParameters(new PlaybackParameters(desiredSpeed));
            player.play();

            startProgressUpdates();
```

- [ ] **Step 4: Android — add the `setSpeed` method**

Find the whole `seekTo` method:

```java
    public static void seekTo(final float seconds) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity != null) {
            activity.runOnUiThread(() -> {
                if (player != null) {
                    player.seekTo((long)(seconds * 1000));
                }
            });
        }
    }
```

Replace with (same method + `setSpeed` appended):

```java
    public static void seekTo(final float seconds) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity != null) {
            activity.runOnUiThread(() -> {
                if (player != null) {
                    player.seekTo((long)(seconds * 1000));
                }
            });
        }
    }

    public static void setSpeed(final float speed) {
        desiredSpeed = speed;
        final Activity activity = UnityPlayer.currentActivity;
        if (activity != null) {
            activity.runOnUiThread(() -> {
                if (player != null) {
                    player.setPlaybackParameters(new PlaybackParameters(speed));
                }
            });
        }
    }
```

- [ ] **Step 5: iOS — add the `desiredSpeed` global**

In `Assets/Plugins/iOS/AudioPlayer.mm`, find:

```objc
static id endObserver = nil; // --- ADDED: To track when the audio finishes ---
static NSString *currentUrl = nil;
```

Replace with:

```objc
static id endObserver = nil; // --- ADDED: To track when the audio finishes ---
static NSString *currentUrl = nil;
static float desiredSpeed = 1.0f;
```

- [ ] **Step 6: iOS — apply rate on play**

Find:

```objc
    player = [[AVPlayer alloc] initWithURL:audioUrl];
    [player play];
```

Replace with:

```objc
    player = [[AVPlayer alloc] initWithURL:audioUrl];
    [player play];
    player.rate = desiredSpeed;
```

- [ ] **Step 7: iOS — apply rate on resume**

Find:

```objc
void resumePlayer()
{
    if (player) [player play];
}
```

Replace with:

```objc
void resumePlayer()
{
    if (player) { [player play]; player.rate = desiredSpeed; }
}
```

- [ ] **Step 8: iOS — add the `setSpeed` function**

Find the whole `seekPlayer` function:

```objc
void seekPlayer(float seconds)
{
    if (!player) return;

    CMTime time = CMTimeMakeWithSeconds(seconds, 600);
    [player seekToTime:time];
}
```

Replace with (same function + `setSpeed` appended):

```objc
void seekPlayer(float seconds)
{
    if (!player) return;

    CMTime time = CMTimeMakeWithSeconds(seconds, 600);
    [player seekToTime:time];
}

void setSpeed(float speed)
{
    desiredSpeed = speed;
    if (player && player.rate != 0.0f) player.rate = speed; // only while playing
}
```

- [ ] **Step 9: Commit** (no Editor compile possible — committed as-is, verified on device in Task 6)

```bash
git add Assets/Plugins/Android/AudioPlayer.java Assets/Plugins/iOS/AudioPlayer.mm
git commit -m "feat(chat): native playback-speed support (ExoPlayer setPlaybackParameters, AVPlayer rate)"
```

---

## Task 5: `MessageItemView` — swap slider for waveform + speed pill

Depends on Task 2 (`AudioWaveform`) and Task 3 (`AudioController.SeekTo`/`CurrentSpeed`/`CycleSpeed`/`OnSpeedChanged`). Compiles in the Editor; behaviour verified in Task 6.

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs`

- [ ] **Step 1: Sanity-check no external refs to the removed fields**

Run:

```bash
grep -rn "\.audioSlider\|\.isDragging" Assets/Scripts Assets/Editor
```
Expected: **no matches** outside `MessageItemView.cs` itself. (If any appear, update them to the new `audioWaveform`/`IsDragging` API before continuing.)

- [ ] **Step 2: Replace the slider fields**

In `Assets/Scripts/UI/MessageItemView.cs`, find:

```csharp
    public Slider audioSlider;
    public bool isDragging;
```

Replace with:

```csharp
    [SerializeField] private AudioWaveform audioWaveform;
    [SerializeField] private Button speedPillButton;
    [SerializeField] private TextMeshProUGUI speedPillLabel;
```

- [ ] **Step 3: Subscribe/unsubscribe `OnSpeedChanged`**

Find (in `OnEnable`):

```csharp
            AudioController.Instance.OnAudioStarted += HandleAudioStarted;
            AudioController.Instance.OnAudioStopped += HandleAudioStopped;
            AudioController.Instance.OnAudioProgress += HandleAudioProgress;
```

Replace with:

```csharp
            AudioController.Instance.OnAudioStarted += HandleAudioStarted;
            AudioController.Instance.OnAudioStopped += HandleAudioStopped;
            AudioController.Instance.OnAudioProgress += HandleAudioProgress;
            AudioController.Instance.OnSpeedChanged += HandleSpeedChanged;
```

Find (in `OnDisable`):

```csharp
            AudioController.Instance.OnAudioStarted -= HandleAudioStarted;
            AudioController.Instance.OnAudioStopped -= HandleAudioStopped;
            AudioController.Instance.OnAudioProgress -= HandleAudioProgress;
```

Replace with:

```csharp
            AudioController.Instance.OnAudioStarted -= HandleAudioStarted;
            AudioController.Instance.OnAudioStopped -= HandleAudioStopped;
            AudioController.Instance.OnAudioProgress -= HandleAudioProgress;
            AudioController.Instance.OnSpeedChanged -= HandleSpeedChanged;
```

- [ ] **Step 4: Rewrite `HandleAudioMedia`**

Find the whole method:

```csharp
    void HandleAudioMedia(MessageViewModel vm)
    {
        audioPanel.SetActive(true);
        // messageText.gameObject.SetActive(false); 
        
        if (timeText != null) timeText.margin = new Vector4(0, 0, 0, 0);
        
        if (audioSlider != null)
        {
            audioSlider.gameObject.SetActive(true);
            audioSlider.minValue = 0f;
            audioSlider.maxValue = vm.duration > 0 ? vm.duration : 1f;
            audioSlider.value = 0f;
        }

        TimeSpan t = TimeSpan.FromSeconds(vm.duration);
        if (audioDurationText) audioDurationText.text = string.Format("{0:D1}:{1:D2}", t.Minutes, t.Seconds);

        if (audioPlayButton)
        {
            audioPlayButton.onClick.RemoveAllListeners();
            audioPlayButton.onClick.AddListener(() => 
            {
                if (ScrollClickBlocker.IsBlocking) return;
                AudioController.Instance.ToggleAudio(vm.mediaUrl);
            });
        }
    }
```

Replace with:

```csharp
    void HandleAudioMedia(MessageViewModel vm)
    {
        audioPanel.SetActive(true);

        if (timeText != null) timeText.margin = new Vector4(0, 0, 0, 0);

        if (audioWaveform != null)
        {
            audioWaveform.gameObject.SetActive(true);
            audioWaveform.SetSeed(vm.messageId);
            audioWaveform.SetProgress(0f);
            audioWaveform.OnSeek = fraction => SeekAudio(vm, fraction);
        }

        TimeSpan t = TimeSpan.FromSeconds(vm.duration);
        if (audioDurationText) audioDurationText.text = string.Format("{0:D1}:{1:D2}", t.Minutes, t.Seconds);

        if (audioPlayButton)
        {
            audioPlayButton.onClick.RemoveAllListeners();
            audioPlayButton.onClick.AddListener(() =>
            {
                if (ScrollClickBlocker.IsBlocking) return;
                AudioController.Instance.ToggleAudio(vm.mediaUrl);
            });
        }

        if (speedPillButton != null)
        {
            speedPillButton.onClick.RemoveAllListeners();
            speedPillButton.onClick.AddListener(() =>
            {
                if (ScrollClickBlocker.IsBlocking) return;
                AudioController.Instance.CycleSpeed();
            });
        }
        if (speedPillLabel != null)
            speedPillLabel.text = FormatSpeed(AudioController.CurrentSpeed);
    }

    void SeekAudio(MessageViewModel vm, float fraction)
    {
        if (AudioController.Instance == null) return;
        float seconds = AudioBubbleMath.SecondsFromFraction(fraction, vm.duration);
        AudioController.Instance.SeekTo(vm.mediaUrl, seconds);
    }

    void HandleSpeedChanged(float speed)
    {
        if (speedPillLabel != null) speedPillLabel.text = FormatSpeed(speed);
    }

    static string FormatSpeed(float speed)
    {
        string num = (speed == Mathf.Floor(speed)) ? speed.ToString("0") : speed.ToString("0.0");
        return num + "×";
    }
```

- [ ] **Step 5: Rewrite `HandleAudioStopped` and `HandleAudioProgress`**

Find:

```csharp
    void HandleAudioStopped(string stoppedUrl) 
    { 
        if (currentVm != null && currentVm.mediaUrl == stoppedUrl) 
        {
            if (audioButtonIcon) audioButtonIcon.sprite = playIcon;
            if (audioSlider != null) audioSlider.value = 0f; 
        } 
    }    
    void HandleAudioProgress(string url, float pos, float dur) 
    { 
        if (currentVm == null || currentVm.mediaUrl != url || isDragging) return; 
        
        if (audioSlider != null)
        {
            audioSlider.maxValue = dur > 0 ? dur : 1f; 
            audioSlider.value = pos; 
        }
    }
```

Replace with:

```csharp
    void HandleAudioStopped(string stoppedUrl)
    {
        if (currentVm != null && currentVm.mediaUrl == stoppedUrl)
        {
            if (audioButtonIcon) audioButtonIcon.sprite = playIcon;
            if (audioWaveform != null) audioWaveform.SetProgress(0f);
            if (audioDurationText != null && currentVm != null)
            {
                TimeSpan total = TimeSpan.FromSeconds(currentVm.duration);
                audioDurationText.text = string.Format("{0:D1}:{1:D2}", total.Minutes, total.Seconds);
            }
        }
    }
    void HandleAudioProgress(string url, float pos, float dur)
    {
        if (currentVm == null || currentVm.mediaUrl != url) return;
        if (audioWaveform != null && audioWaveform.IsDragging) return;

        if (audioWaveform != null)
            audioWaveform.SetProgress(dur > 0f ? pos / dur : 0f);

        if (audioDurationText != null)
        {
            TimeSpan elapsed = TimeSpan.FromSeconds(pos);
            audioDurationText.text = string.Format("{0:D1}:{1:D2}", elapsed.Minutes, elapsed.Seconds);
        }
    }
```

- [ ] **Step 6: Verify it compiles**

Focus the Editor. Expected: Console shows **no errors**. (The Game view will still show the old/empty audio panel until Task 6 wires the prefab — that's expected.)

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "feat(chat): drive audio bubble from AudioWaveform + speed pill"
```

---

## Task 6: Prefab wiring (manual, Editor) + functional verification

Wire both audio-bubble prefabs and confirm behaviour. Bars are runtime-generated, so each prefab needs only the `AudioWaveform` GameObject + the speed pill.

**Files:**
- Modify: `Assets/Prefabs/MessageTextIncoming.prefab`
- Modify: `Assets/Prefabs/MessageTextOutgoing.prefab`

- [ ] **Step 1: Wire `MessageTextIncoming.prefab`**

Open the prefab (double-click in Project). Inside the `audioPanel` GameObject:

1. **Delete** the existing `Slider` GameObject (it was previously referenced by `audioSlider`).
2. **Add** an empty child `Waveform`:
   - Add component **AudioWaveform** (it auto-adds a transparent raycast `Image` and uses its own RectTransform as the bars container).
   - RectTransform: stretch it to occupy the space the slider held (sit between the play button and the right edge). Suggested: anchor stretch horizontally, fixed height ~56 (on the 1080-wide bubble), left inset after the play button (~+108), right inset before the pill (~+96).
   - Leave `barSprite` empty for now (sharp bars are fine); `barCount = 32`.
3. **Add** a child `SpeedPill` (Button) at the right end of the play row:
   - Add **Button** + an **Image** background (rounded if you have a sprite); size ~64×40; light fill (e.g. `#E7F3EB`).
   - Add a child **TextMeshProUGUI** `PillLabel`: text `1×`, color teal `#128C7E`, bold, ~22px, center-aligned.
4. Select the root `MessageTextIncoming` GameObject → on the **MessageItemView** component assign the new serialized fields:
   - `Audio Waveform` → the `Waveform` GameObject's `AudioWaveform`.
   - `Speed Pill Button` → the `SpeedPill` Button.
   - `Speed Pill Label` → the `PillLabel` TextMeshProUGUI.
5. Save the prefab.

- [ ] **Step 2: Wire `MessageTextOutgoing.prefab`**

Repeat Step 1 for `MessageTextOutgoing.prefab` (same structure; the green bubble background already comes from the prefab — waveform colors are identical).

- [ ] **Step 3: Functional verification in the Editor (Game view, 1080×2400)**

Enter Play mode, open a chat with a voice note and an audio file. Confirm:
- Both bubbles show a bar waveform (gray) + a `1×` pill at the right of the play row.
- Tapping play colors the played bars teal and the duration counts up; the play icon swaps to stop.
- Tapping/dragging the waveform moves the played split (seek). In the Editor native playback is a no-op, so audio won't move — confirm the **visual** seek + that no Console errors fire.
- Tapping the pill cycles `1× → 1.5× → 2× → 1×`, and the label updates on **every** visible audio bubble simultaneously (shared `CurrentSpeed`).

- [ ] **Step 4: On-device verification (Android + iOS)**

Build to a device. Confirm actual audio: playback, waveform progress tracking real position, tap-to-seek jumps playback, and the speed pill changes real playback speed (pitch-preserved) — and that the chosen speed sticks to the next voice note you play.

- [ ] **Step 5: Commit the prefabs**

```bash
git add Assets/Prefabs/MessageTextIncoming.prefab Assets/Prefabs/MessageTextOutgoing.prefab
git commit -m "feat(chat): wire waveform + speed pill into audio bubble prefabs"
```

---

## Self-Review

**1. Spec coverage**

| Spec item | Task |
|---|---|
| Decorative waveform (hash of message ID) | Task 1 `BarHeights` + Task 2 `SetSeed` |
| Unified for Voice + Audio | Task 5 (`HandleAudioMedia` runs for both via existing `MessageType.Audio || Voice` branch) |
| Speed 1×/1.5×/2× cycle | Task 1 `NextSpeed` + Task 3 `CycleSpeed` |
| Always-visible pill, inline right (Layout B) | Task 6 prefab layout |
| Session-sticky speed, default 1×, no PlayerPrefs | Task 3 (`static CurrentSpeed`, native `desiredSpeed`) |
| Tap + drag seek, replaces Slider/`isDragging` | Task 2 pointer handlers + Task 3 `Seek`/`SeekTo` + Task 5 `SeekAudio` |
| Instantiated/pooled bars | Task 2 `EnsureBars` |
| Pure logic in `AudioBubbleMath` | Task 1 |
| `AudioWaveform` in `Assets/Scripts/UI/` | Task 2 |
| Live pill sync across bubbles | Task 3 `OnSpeedChanged` + Task 5 `HandleSpeedChanged` |
| Unknown/zero duration guards | Task 1 (`SecondsFromFraction` dur≤0→0) + Task 5 (`dur>0f? :0f`) |
| Seek on non-playing note → play then seek | Task 3 `SeekTo` |
| EditMode tests mirror `AttachmentDisplayFormatTests` | Task 1 Step 1 |

No gaps. (Spec's `AudioBubbleBuilder.cs` intentionally replaced by manual wiring — flagged at top.)

**2. Placeholder scan:** No TBD/TODO; every code step shows complete code; no "similar to Task N".

**3. Type/signature consistency:** `BarHeights(string,int)`, `PlayedBarCount(float,int)`, `NextSpeed(float)`, `SecondsFromFraction(float,int)`; `AudioWaveform.SetSeed(string)`, `SetProgress(float)`, `IsDragging`, `OnSeek` (Action<float> field, reassigned in Task 5); `AudioController.Seek(float)`, `SeekTo(string,float)`, `SetSpeed(float)`, `CycleSpeed()`, `CurrentSpeed`, `OnSpeedChanged` — all consistent across tasks. Native `setSpeed` matches bridge `CallStatic("setSpeed", …)` / DllImport `setSpeed`.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-06-02-voice-waveform-playback-speed.md`. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. (Note: this Unity flow needs *you* to run EditMode tests / compile in your open Editor and to do the manual prefab wiring in Task 6, so the subagent will pause at those checkpoints for you.)
2. **Inline Execution** — Execute tasks in this session using executing-plans, with checkpoints for review.

Which approach?

# Voice Waveform + Playback Speed (Design)

**Date:** 2026-06-02
**Status:** Approved (brainstorm) — ready for implementation plan
**Related (orthogonal):** `docs/superpowers/specs/2026-06-02-video-upload-progress-ring-design.md` — also adds a control to a media bubble but touches the outgoing-video panel, not `audioPanel`. No overlap.

## Problem

Audio bubbles (`MessageType.Voice` and `MessageType.Audio`) render a plain Unity `Slider` for progress and have no way to change playback speed. The slider is functional but generic — it does not read as a voice note, and long voice notes can only be heard at 1×. We want the WhatsApp-style **waveform** scrubber and a **playback-speed** control.

Current audio bubble UI is wired in `MessageItemView.HandleAudioMedia` ([MessageItemView.cs:3016](../../../Assets/Scripts/UI/MessageItemView.cs)): `audioPlayButton` + `audioButtonIcon` (play/stop sprites), `audioSlider` (a `Slider`), `audioDurationText`. Progress arrives via `AudioController.OnAudioProgress(url, pos, dur)` and updates the slider ([MessageItemView.cs:3054](../../../Assets/Scripts/UI/MessageItemView.cs)).

Playback itself is native. `AudioController` ([AudioController.cs](../../../Assets/Scripts/Chat/AudioController.cs)) routes to `AndroidBridge`/`IOSBridge`, which call into native players: ExoPlayer on Android ([AudioPlayer.java](../../../Assets/Plugins/Android/AudioPlayer.java)) and `AVPlayer` on iOS ([AudioPlayer.mm](../../../Assets/Plugins/iOS/AudioPlayer.mm)). Both bridges already expose `Seek(float seconds)`. Neither exposes speed.

The Wappi API gives us **only `duration` (seconds)** for audio — no amplitude/peak data.

## Goal

Replace the audio-bubble slider with a tappable/scrubbable **waveform**, and add an always-visible **speed pill** that cycles 1× → 1.5× → 2×. Applies uniformly to both voice notes and audio files behind one shared component.

## Decisions (locked)

| Decision | Choice | Rationale |
|---|---|---|
| Waveform data source | **Decorative** — bar heights derived deterministically from a hash of the message ID | API provides no amplitude data. Visually indistinguishable from a real waveform on a short note; zero decode cost; renders before the file downloads; no native DSP. Rendering UI is identical to a real waveform, so a future upgrade only swaps the data source. |
| Scope | **Unified** — one component for `MessageType.Voice` and `MessageType.Audio` | Incoming audio in a bot/customer chat is overwhelmingly voice notes; a waveform on a rare mp3 is harmless and the shared component is less code. Matches the current shared play+slider+duration layout. |
| Speed steps | **1× → 1.5× → 2×**, tap to cycle, wraps to 1× | WhatsApp-familiar; covers the main need (getting through long notes). |
| Speed pill placement | **Always visible, inline at the right of the play row** (Layout B) | User-selected. Reads as a control; compact single-row feel. |
| Speed persistence | **Session-sticky, in-memory, default 1×, resets on app restart** (no PlayerPrefs) | Matches WhatsApp's "set once, applies onward" feel without persistence complexity. |
| Seek | **Tap-to-seek + drag-to-scrub on the waveform**, replacing the `Slider` + `isDragging` | Native `Seek(seconds)` already exists on both bridges. |
| Waveform rendering | **Instantiated/pooled `Image` bars** under a horizontal layout | Unity-idiomatic, fine for the few audio bubbles on screen. Shader / procedural-mesh rejected as overkill. |
| Pure-logic location | `Assets/Scripts/Chat/AudioBubbleMath.cs` (no `UnityEngine` deps) | Mirrors existing pure helpers (`MediaBubbleSize`, `ScrollFabMath`, `AttachmentDisplayFormat`) so the math is EditMode-testable without a scene. |
| Component location | `Assets/Scripts/UI/AudioWaveform.cs` | Alongside the other view components. |

## Architecture

Two new runtime files + one new Editor builder + one new test file, plus edits to the controller, both bridges, both native players, and `MessageItemView`. Prefab wiring done via the Editor builder.

### New — `Assets/Scripts/Chat/AudioBubbleMath.cs` (pure, EditMode-testable)

All decision logic, no `UnityEngine` types, so it unit-tests without a scene (mirrors `MediaBubbleSize`/`ScrollFabMath`).

```csharp
public static class AudioBubbleMath
{
    public static readonly float[] SpeedSteps = { 1f, 1.5f, 2f };

    // Deterministic bar heights in [MinBarFraction, 1] from a stable hash of the seed.
    // Same seed => same pattern across redraws/sessions; different seeds differ.
    public static float[] BarHeights(string seed, int barCount); // FNV-1a hash -> seeded LCG

    // How many leading bars are "played" at the given progress fraction (0..1).
    public static int PlayedBarCount(float fraction, int barCount);

    // Next speed in the cycle (wraps 2x -> 1x). Tolerant of float drift.
    public static float NextSpeed(float current);

    // Pointer/scrub fraction (0..1) -> seek target in seconds (clamped to duration).
    public static float SecondsFromFraction(float fraction, int durationSeconds);
}
```

- `MinBarFraction` ≈ 0.12 so even "quiet" bars are visible.
- `BarHeights` hashes the seed (message ID) with FNV-1a, feeds a small LCG to produce `barCount` values, normalized to `[MinBarFraction, 1]`. Deterministic and allocation-light.

### New — `Assets/Scripts/UI/AudioWaveform.cs` (MonoBehaviour)

Replaces the `Slider` inside `audioPanel`. Renders the bars and owns seek input.

```csharp
public class AudioWaveform : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform barsContainer; // HorizontalLayoutGroup
    [SerializeField] private Image barPrefab;             // 3px rounded bar
    [SerializeField] private int barCount = 32;
    [SerializeField] private Color playedColor   = new Color32(0x12,0x8C,0x7E,0xFF);
    [SerializeField] private Color unplayedColor = new Color32(0xC6,0xCD,0xCB,0xFF);

    public event System.Action<float> OnSeek;   // fraction 0..1, emitted on pointer-up/tap

    public void SetSeed(string messageId);   // builds/pools bars from AudioBubbleMath.BarHeights
    public void SetProgress(float fraction); // recolors played vs unplayed split
    public bool IsDragging { get; }           // absorbs MessageItemView.isDragging
}
```

- Bars are pooled per view: build once on `SetSeed`, reuse `Image` children on rebind (no per-frame allocation).
- `SetProgress` recolors using `AudioBubbleMath.PlayedBarCount` — no re-layout.
- Pointer handlers map local x → fraction. During a drag the waveform updates its own played/unplayed split live; on pointer-up/tap it emits `OnSeek`. While dragging, incoming `SetProgress` from playback is ignored (the consumer checks `IsDragging`).

### Edit — `Assets/Scripts/Chat/AudioController.cs`

Add session-sticky speed, default 1×.

```csharp
public static float CurrentSpeed { get; private set; } = 1f;
public event Action<float> OnSpeedChanged;

public void CycleSpeed() => SetSpeed(AudioBubbleMath.NextSpeed(CurrentSpeed));

public void SetSpeed(float speed)
{
    CurrentSpeed = speed;
    OnSpeedChanged?.Invoke(speed);          // live-syncs every visible pill
#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidBridge.SetSpeed(speed);
#elif UNITY_IOS && !UNITY_EDITOR
    IOSBridge.SetSpeed(speed);
#endif
}
```

- `PlayAudio` and `Resume` reapply `CurrentSpeed` after starting native playback so a new track honors the sticky speed.
- `OnSpeedChanged` lets all visible bubbles update their pill label even though speed is global.

### Edit — `Assets/Scripts/Chat/AndroidBridge.cs`

```csharp
public static void SetSpeed(float speed)
{
#if UNITY_ANDROID && !UNITY_EDITOR
    player.CallStatic("setSpeed", speed);
#endif
}
```

### Edit — `Assets/Scripts/Chat/IOSBridge.cs`

```csharp
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void setSpeed(float speed);
#endif
public static void SetSpeed(float speed)
{
#if UNITY_IOS && !UNITY_EDITOR
    setSpeed(speed);
#endif
}
```

### Edit — `Assets/Plugins/Android/AudioPlayer.java` (ExoPlayer)

```java
private static float desiredSpeed = 1.0f;

public static void setSpeed(final float speed) {
    desiredSpeed = speed;
    final Activity activity = UnityPlayer.currentActivity;
    if (activity != null) activity.runOnUiThread(() -> {
        if (player != null) player.setPlaybackParameters(new PlaybackParameters(speed));
    });
}
```

- In `play(...)`, after `player.prepare()`, call `player.setPlaybackParameters(new PlaybackParameters(desiredSpeed))` so a freshly built player honors the sticky speed. ExoPlayer applies speed whether playing or paused and does **not** auto-start.

### Edit — `Assets/Plugins/iOS/AudioPlayer.mm` (AVPlayer)

```objc
static float desiredSpeed = 1.0f;

void setSpeed(float speed) {
    desiredSpeed = speed;
    if (player && player.rate != 0.0f) player.rate = speed; // only while playing
}
```

- **Caveat:** setting `AVPlayer.rate` while paused **starts** playback. So `setSpeed` only writes `rate` when already playing; otherwise it just stores `desiredSpeed`. In `playUrl` (after `[player play]`) and `resumePlayer` (after `[player play]`), set `player.rate = desiredSpeed`. `pausePlayer` leaves rate at 0 naturally.

### Edit — `Assets/Scripts/UI/MessageItemView.cs`

- Replace the `public Slider audioSlider;` / `public bool isDragging;` fields with `[SerializeField] private AudioWaveform audioWaveform;` plus `[SerializeField] private Button speedPillButton;` and `[SerializeField] private TextMeshProUGUI speedPillLabel;`.
- `HandleAudioMedia(vm)` ([:3016](../../../Assets/Scripts/UI/MessageItemView.cs)):
  - `audioWaveform.SetSeed(vm.messageId); audioWaveform.SetProgress(0f);`
  - Wire play button (unchanged `ToggleAudio`).
  - Subscribe `audioWaveform.OnSeek += f => SeekTo(vm, f);` where `SeekTo` computes seconds via `AudioBubbleMath.SecondsFromFraction(f, vm.duration)`; if `vm.mediaUrl` is not the current track, `PlayAudio` then `Seek`, else `Seek`.
  - Speed pill: set `speedPillLabel.text = FormatSpeed(AudioController.CurrentSpeed)`; on click `AudioController.Instance.CycleSpeed()`.
- `HandleAudioProgress(url, pos, dur)` ([:3054](../../../Assets/Scripts/UI/MessageItemView.cs)): guard on `audioWaveform.IsDragging`; `audioWaveform.SetProgress(dur > 0 ? pos/dur : 0)`; duration text counts up elapsed (`pos`).
- `HandleAudioStarted/Stopped` ([:3045](../../../Assets/Scripts/UI/MessageItemView.cs)): swap play/stop icon (unchanged); on stop `audioWaveform.SetProgress(0)` and show total duration again.
- Subscribe to `AudioController.OnSpeedChanged` in `OnEnable`/unsubscribe in `OnDisable` to keep `speedPillLabel` in sync across bubbles.
- Bubble sizing constants (`VoiceWidth/Height`, `AudioFileWidth/Height`, [:89](../../../Assets/Scripts/UI/MessageItemView.cs)) stay; the waveform fills the row.

### New — `Assets/Editor/AudioBubbleBuilder.cs` (`[MenuItem]` builder)

Follows the project builder pattern (e.g. `AttachmentPreviewScreenBuilder`). Operates on both audio-bubble prefabs (`Assets/Prefabs/MessageTextIncoming.prefab` and `Assets/Prefabs/MessageTextOutgoing.prefab`): inside `audioPanel`, removes the `Slider`, adds the waveform container (`AudioWaveform` + `HorizontalLayoutGroup` + bar prefab reference) and the speed pill (`Button` + `TextMeshProUGUI`), and assigns the new `[SerializeField]` references on the `MessageItemView`. Manual wiring in the Editor is the fallback.

## Behavior details / edge cases

- **Unknown/zero duration:** bars still render (decorative); progress fraction guarded (`dur > 0 ? pos/dur : 0`). Seek uses `vm.duration`; if 0, seek is a no-op until native reports a real duration via `OnAudioProgress`.
- **Seek on a non-playing note:** start playback (`PlayAudio`) then `Seek` to the tapped fraction (WhatsApp behavior).
- **Editor:** no native player — `AudioController` speed/seek calls are platform-guarded no-ops; the waveform still renders and scrubs visually. Only `AudioBubbleMath` is unit-tested.
- **Progress cadence:** Android posts every 100 ms, iOS every 500 ms — waveform recolor follows; acceptable.
- **Global speed, many bubbles:** `OnSpeedChanged` updates every visible pill label; new bubbles read `CurrentSpeed` on bind.

## Testing

EditMode unit tests in `Assets/Tests/Editor/Chat/AudioBubbleMathTests.cs` (mirrors `AttachmentDisplayFormatTests`):

- `BarHeights`: same seed → identical array; different seeds → different arrays; all values within `[MinBarFraction, 1]`; correct length.
- `PlayedBarCount`: 0 → 0; 1 → barCount; 0.5 → half; clamps outside [0,1].
- `NextSpeed`: 1 → 1.5 → 2 → 1; tolerant of float drift (e.g. 1.4999).
- `SecondsFromFraction`: 0 → 0; 1 → duration; clamps; duration 0 → 0.

Native playback (speed actually changes pitch-corrected audio on device), pointer seek, and prefab wiring are verified manually in the Unity Editor / on device — not unit-testable.

## Files

**New**
- `Assets/Scripts/Chat/AudioBubbleMath.cs`
- `Assets/Scripts/UI/AudioWaveform.cs`
- `Assets/Editor/AudioBubbleBuilder.cs`
- `Assets/Tests/Editor/Chat/AudioBubbleMathTests.cs`

**Edited**
- `Assets/Scripts/Chat/AudioController.cs` — `CurrentSpeed`, `SetSpeed`, `CycleSpeed`, `OnSpeedChanged`, reapply on play/resume
- `Assets/Scripts/Chat/AndroidBridge.cs` — `SetSpeed`
- `Assets/Scripts/Chat/IOSBridge.cs` — `SetSpeed`
- `Assets/Plugins/Android/AudioPlayer.java` — `setSpeed` + reapply in `play`
- `Assets/Plugins/iOS/AudioPlayer.mm` — `setSpeed` + apply rate on play/resume
- `Assets/Scripts/UI/MessageItemView.cs` — swap slider→waveform, add speed pill, rewire audio handlers
- `Assets/Prefabs/MessageTextIncoming.prefab` + `Assets/Prefabs/MessageTextOutgoing.prefab` — audio panel rebuild (via builder)

## Out of scope

- Real (decoded) waveforms — deferred; the rendering UI is unchanged if we add it later.
- Persisting chosen speed across app restarts.
- Recording / sending voice notes (this is playback only).
- Pitch-correction tuning beyond the platform default.

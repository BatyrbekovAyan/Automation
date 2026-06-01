# WhatsApp-style Video Compression (Explicit Bitrate) — Design Spec

**Status:** Approved (brainstorm complete), pending implementation plan
**Date:** 2026-06-01
**Goal:** Compress outgoing videos to a tight, predictable size budget (WhatsApp-style) so normal-length clips send instead of being rejected by the upload cap.
**Builds on:** the shipped format-conversion fix (`docs/superpowers/specs/2026-06-01-video-mp4-conversion-design.md`). Platform scope: **iOS only** (Android Media3 remains a deferred Phase 2).

---

## 1. Problem & root cause

After the format fix, HEVC `.mov` clips convert and deliver — but anything beyond a short clip is rejected by the 16 MB cap. Measured transcode output (current `AVAssetExportPreset1280x720`):

| Clip | Output | Implied bitrate |
|---|---|---|
| 1 min | 84.9 MB | ~11.3 Mbps |
| 50 s | 67.2 MB | ~10.7 Mbps |
| 42 s | 57.5 MB | ~11.0 Mbps |
| 24 s | 32.3 MB | ~10.8 Mbps |

**Root cause: `AVAssetExportPreset1280x720` is a *quality* preset (~11 Mbps), not a *size* preset.** The files are huge because of bitrate, not length. WhatsApp re-encodes to a low bitrate budget (~1–2 Mbps), so a 1-min clip is ~7–12 MB; for genuinely huge files it falls back to the document path (≤2 GB). We will match the low-bitrate transcode.

## 2. Approach

**Replace `AVAssetExportSession` with an `AVAssetReader` → `AVAssetWriter` pipeline** in `VideoConverter.mm`, encoding to an explicit bitrate. (Chosen over a lower-quality preset or `fileLengthLimit` because only the reader/writer path gives precise, predictable WhatsApp-like sizes and cleanly exposes progress.) The start/poll/error/free C interface to Unity is unchanged, plus one new progress poll.

**Encoder budget (balanced 720p):**
- Video: **H.264** (Main profile), capped to **≤1280×720** preserving aspect (even dimensions), `AVVideoAverageBitRateKey = 2_000_000` (~2 Mbps), sane keyframe interval.
- Audio: **AAC, 128 kbps**, 44.1 kHz.
- Container: **MP4**, faststart (`shouldOptimizeForNetworkUse`). Target ≈ **~15 MB/min**.

**Per-source decision (avoid needless re-encode):** compute `approxBitrate = fileSize·8 / duration`.
- **Use as-is** (native status 3, upload original untouched) when already **MP4 + H.264 + under cap + low bitrate (≲3 Mbps)**.
- **Transcode** (the reader/writer pipeline) for **everything else** — HEVC, `.mov`, or any high-bitrate/oversized H.264. This is where the user's clips go (~85 MB/min → ~15 MB/min).

**Progress hook (build native now, wire UI later):** the reader/writer loop tracks `processedTime / totalDuration`; expose `_PollVideoConvertProgress(jobId)` → 0–1. The C# `Convert` coroutine ignores it for now; the future circular progress bar (deferred) just reads it — no second trip into the native file.

## 3. Components

### 3.1 `Assets/Plugins/iOS/VideoConverter.mm` (REWRITE the transcode path)
Keep: the job table (`gJobs`/`gLock`, `dispatch_once` init), `_StartVideoConvert`/`_PollVideoConvert`/`_VideoConvertError`/`_FreeVideoConvertJob`, the codec/size inspection, and the use-as-is (status 3) fast path. **Replace** the `AVAssetExportSession` transcode branch with:
- `AVAssetReader` with `AVAssetReaderTrackOutput`s for video (decompressed) + audio (decompressed LPCM).
- `AVAssetWriter` (`AVFileTypeMPEG4`, faststart) with two `AVAssetWriterInput`s: video (`AVVideoCodecTypeH264`, target W/H ≤720p, `AVVideoCompressionPropertiesKey` → `AVVideoAverageBitRateKey 2_000_000` + keyframe interval) and audio (`kAudioFormatMPEG4AAC`, 128 kbps).
- A sample-pump loop (`requestMediaDataWhenReadyOnQueue` / `copyNextSampleBuffer` / `appendSampleBuffer`) that updates a `progress` field; on completion `finishWritingWithCompletionHandler` → status 1; on failure → status 2.
- Add `_PollVideoConvertProgress(int jobId)` returning the 0–1 progress (the job struct gains a `float progress`).

The exact sample-loop/scaling mechanics are an implementation detail for the plan; the iOS build + device matrix is the validation gate (no EditMode coverage for native).

### 3.2 `Assets/Scripts/Chat/VideoConverter.cs` (minimal)
- Add the `[DllImport] _PollVideoConvertProgress(int)` extern (so the future UI bar can read progress without touching this file again). The `Convert` coroutine is otherwise unchanged.

### 3.3 `Assets/Scripts/Main/ChatManager.MediaSend.cs` (constants)
- `WappiVideoCapBytes`: 16 MB → **128 MB** (`128L * 1024 * 1024`).
- Media upload timeout: `www.timeout` 120 → **300**.

### 3.4 `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` (constant)
- `MaxVideoPickBytes`: 512 MB → **1 GB** (`1024L * 1024 * 1024`).

## 4. Limits & the one open number

- Pre-stage pick ceiling: **1 GB**. Post-conversion cap: **128 MB**. Upload timeout: **300 s**.
- **The 128 MB cap is provisional.** Every prior test died at the *old* 16 MB cap before reaching Wappi, so we've never confirmed Wappi accepts a large MP4. At 2 Mbps most clips are ≪128 MB, but the device matrix must include **one near-cap clip (~100 MB)** to confirm Wappi accepts it; if Wappi 400s near 128 MB, lower the cap to the measured-safe value.

## 5. Out of scope (deferred)
- **Document fallback** for clips still >128 MB after compression: at 2 Mbps a clip must run **>~70 min** to exceed 128 MB — vanishingly rare, so a >128 MB result just hits the existing clean Failed bubble. Notable future option if the cap data warrants.
- **Circular progress-bar UI** (around the play/stop button): the native progress hook ships now; the UI is a separate future task.
- **Android** (Media3 `Transformer`): Phase 2.

## 6. Error handling / edge cases
- Reader/writer setup or encode failure → native status 2 → C# fires `OnMessageStatusChanged(Failed)` + tap-to-retry (unchanged).
- Already MP4/H.264/low-bitrate/under-cap → status 3, upload original untouched (keeps the bubble's local-playback file).
- Still >128 MB after compression (extreme length) → existing post-conversion cap check → Failed bubble.
- Retry re-converts from the original pick (unchanged; native fast-path keeps an already-deliverable file cheap).
- Converted temp (`send_{tempId}.mp4`) cleanup on success only (unchanged).

## 7. Testing
- **EditMode:** the existing `VideoConverter` Editor pass-through test still holds (Editor path unchanged).
- **Device matrix (the gate):** the four clips that failed (1 min/50 s/42 s/24 s) now compress to ~15 MB/min and **deliver**; eyeball quality at 720p/2 Mbps; one near-cap (~100 MB) clip to confirm Wappi accepts it; already-MP4/H.264 small clip sent untouched; HEVC + H.264 `.mov` both deliver; corrupt file fails clean; tap-to-retry re-converts.

## 8. File list
- MODIFY `Assets/Plugins/iOS/VideoConverter.mm` — reader/writer transcode + progress, keep interface/fast-path.
- MODIFY `Assets/Scripts/Chat/VideoConverter.cs` — add `_PollVideoConvertProgress` extern.
- MODIFY `Assets/Scripts/Main/ChatManager.MediaSend.cs` — cap 128 MB, timeout 300 s.
- MODIFY `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` — pick ceiling 1 GB.

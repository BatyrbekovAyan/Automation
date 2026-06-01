# Video → MP4/H.264 Conversion Before Send — Design Spec

**Status:** Approved (brainstorm complete), pending implementation plan
**Date:** 2026-06-01
**Goal:** Make picked videos actually deliver to WhatsApp by converting them to an MP4 (H.264 + AAC) container on-device before upload, the way the native WhatsApp app does.
**Platform scope:** iOS first (native `AVAssetExportSession`). Android (Media3 `Transformer`) is a deferred Phase 2 — out of scope here.

---

## 1. Problem & confirmed root cause

The user reported that "most of my videos cannot be sent," while the same videos send fine from the native WhatsApp app (which they correctly guessed compresses them).

This was diagnosed empirically (see §2 evidence). The original hypotheses — a 16 MB size limit, then an HEVC codec issue — were **both disproven**. The confirmed root cause is the **container format**:

> Wappi's `POST /api/sync/message/video/send` (base64 `b64_file`) only reliably delivers videos that are an **MP4 container with H.264 video + AAC audio**. The iPhone camera produces `.mov` (QuickTime container), HEVC by default ("High Efficiency") or H.264 ("Most Compatible"). We currently upload those raw `.mov` bytes, so WhatsApp never delivers them. WhatsApp's own "compression" is really a transcode-to-MP4/H.264.

This finding is also persisted in project memory (`project_wappi_video_mp4.md`).

## 2. Evidence (from the debugging session)

| Test | File | Result |
|---|---|---|
| 30 MB `.mov` HEVC | large | connection dropped mid-upload (transport) |
| 20 MB `.mov` HEVC @30 s | large | our 30 s timeout fired |
| 20 MB `.mov` HEVC @300 s | large | upload completed → Wappi `400 {"status":"error"}` |
| 6 MB `.mov` HEVC | small | sometimes `done` (tick) but **never delivered**; sometimes failed |
| 2.6 MB `.mov` **H.264** @300 s | tiny | upload completed → Wappi **never responded** (read timeout) |
| 7.4 MB **`.mp4`** H.264 | small | `httpCode=200 {"status":"done", message_id:…}` → **delivered & shown in WhatsApp** |
| Photo (any) | — | sends & delivers fine (same transport) |
| Video via Wappi's **own dashboard** | — | delivers fine (Wappi's video service works) |

Conclusions: photos + text + the Wappi dashboard all work, so transport/auth/request-shape are fine. Only raw `.mov` from our app fails — across every size and both codecs — while a real `.mp4` succeeds. Wappi's API-docs example `b64_file` decodes to an MP4 (`ftyp mp42`). → **container is the differentiator.**

## 3. Success criteria

- A picked iPhone camera video (HEVC or H.264, `.mov`) is converted to MP4/H.264/AAC and **delivers to the recipient's WhatsApp**.
- An already-compatible `.mp4`/H.264 video under the cap sends unchanged (no needless re-encode).
- Oversized videos are downscaled to fit Wappi's ~16 MB cap where feasible; if still too large, the send fails cleanly (Failed bubble + tap-to-retry).
- The optimistic bubble still appears **instantly** on send; conversion happens in the background.

## 4. Approach decision

**Chosen: native platform APIs, iOS-first.** iOS `AVAssetExportSession` is one Apple API that remuxes H.264 `.mov`→`.mp4`, transcodes HEVC→H.264, and downscales via presets — no app-size cost, no licensing issues, highest quality, exactly what WhatsApp does. Built as a small native plugin + C# bridge, mirroring the existing `IOSBridge`/`AndroidBridge` pattern.

**Rejected:**
- **FFmpeg (FFmpegKit / wrapper):** one cross-platform API but +~20–40 MB app size, GPL/LGPL licensing, and FFmpegKit was retired (2025) — overkill vs. native APIs that already do this.
- **Unity Asset Store plugin:** third-party dependency, quality/longevity risk, less control over remux-vs-transcode.
- **Server-side conversion:** re-architects the direct device→Wappi flow and uploads the large raw `.mov` to a server first — against the existing pattern and slow.

## 5. Flow / architecture

Conversion happens in the **network half** (`PostMediaMessageRoutine`), not before staging — so the optimistic bubble appears instantly and *retries* (already routed through `PostMediaMessageRoutine` in Part C Task 6) get conversion for free.

```
pick .mov → StageLocalMedia (instant optimistic bubble, Pending — UNCHANGED)
          → PostMediaMessageRoutine:
                if GalleryVideo:
                    VideoConverter.Convert(mediaPath → temp .mp4)   // native, yield-polled
                      • fail            → Failed bubble + tap-to-retry + log
                      • ok              → uploadPath = .mp4
                                          entry.mediaPath = uploadPath; Outbox.Update(entry)  // retry reuses .mp4
                                          if size > cap → Failed ("too large even after compression")
                Base64Encoder.EncodeFileAsync(uploadPath) → upload (downstream UNCHANGED)
                → reconcile temp id → real message_id (Sent), or Failed
```

In-bubble local playback keeps using the original `.mov` (`videoUrl = file://…`); only the upload uses the `.mp4`.

## 6. Components

### 6.1 iOS native plugin — `Assets/Plugins/iOS/VideoConverter.mm` (NEW, Objective-C++)

Async `AVAssetExportSession` exposed to Unity via a **start + poll** model (mirrors the off-thread base64 `Task` + `WaitUntil` pattern; avoids `UnitySendMessage` threading quirks):

```objc
int  _StartVideoConvert(const char* inPath, const char* outPath, long maxBytes);  // returns jobId
int  _PollVideoConvert(int jobId);          // 0=running 1=done 2=failed 3=use-original-as-is
const char* _VideoConvertError(int jobId);  // message when status==2
```

Per-job logic:
1. Load `AVURLAsset`; read the video track codec via `CMFormatDescriptionGetMediaSubType` → `kCMVideoCodecType_H264` ('avc1') vs `kCMVideoCodecType_HEVC` ('hvc1').
2. Choose strategy:
   - **H.264 and already `.mp4` under cap** → status `3` (upload original untouched).
   - **H.264 in `.mov`** → `AVAssetExportPresetPassthrough` (remux only, no re-encode, near-instant, lossless).
   - **HEVC, or over cap** → `AVAssetExportPreset1280x720` (transcode to H.264/AAC ≤720p — fixes codec and shrinks).
3. Export with `outputFileType = AVFileTypeMPEG4`, `shouldOptimizeForNetworkUse = YES` (faststart). If passthrough reports incompatible, fall back to the 720p transcode.
4. Completion handler stores status/error in a thread-guarded job table that C# polls. `AVAssetExportSession.progress` (0–1) is available for a future "preparing video…" indicator.

A matching `VideoConverter.mm.meta` and any iOS framework links (`AVFoundation`, `CoreMedia`) will be set up during implementation.

### 6.2 C# bridge — `Assets/Scripts/Chat/VideoConverter.cs` (NEW)

`DllImport("__Internal")` wrappers behind `#if UNITY_IOS && !UNITY_EDITOR`, plus a coroutine:

```csharp
// Yields until done. Returns the path to upload (converted .mp4, or original when no
// conversion is needed) via onResult, or invokes onError. On Editor/Android (no native
// impl): pass-through → returns inputPath unchanged.
public static IEnumerator Convert(string inputPath, string outputPath, long maxBytes,
                                  System.Action<string> onResult, System.Action<string> onError);
```

- iOS: `_StartVideoConvert` → `yield return new WaitUntil(() => _PollVideoConvert(job) != 0)` → resolve.
- Editor + Android: pass-through (return `inputPath`). Android video therefore remains unconverted — **still failing as it does today until Phase 2 Media3** (accepted: no regression; Android video is already broken).

### 6.3 Integration — `Assets/Scripts/Main/ChatManager.MediaSend.cs` (MODIFY)

In `PostMediaMessageRoutine`, before the base64 step, add the video-only convert block from §5: convert → on failure fire `OnMessageStatusChanged(Failed)` and `yield break`; on success set `uploadPath`, persist the converted path back into the outbox entry (so retries skip re-conversion), and run the post-conversion size check. Base64 + upload + reconcile downstream are unchanged except they encode `uploadPath`. Delete the temp `.mp4` after a successful send; keep it on failure for retry.

### 6.4 Size-cap repurpose — `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` (MODIFY)

Repurpose the Part C Task 7 guard (do **not** delete the inline-error-label machinery):
- The pre-stage check changes from a **16 MB block** to a **high "pathological pick" ceiling** (~512 MB) so normal large clips pass through to conversion, while a multi-GB pick still fails fast with the existing inline label.
- Real ~16 MB enforcement moves **post-conversion** (in `PostMediaMessageRoutine`, §6.3); since the preview is already closed by then, an over-cap result surfaces as a **Failed bubble** (exclamation + tap-to-retry), not the inline label.

### 6.5 Upload timeout

Media uploads use a flat **120 s** `request.timeout` (converted files are ≤16 MB → ~21 MB base64 body; 120 s is generous), a documented exception to the 30 s networking rule. Text sends stay 30 s. (Confirmed during debugging: 30 s is too short even for a ~10 MB body.)

## 7. Error handling / edge cases

- Conversion fails (corrupt/unsupported) → Failed bubble + tap-to-retry + `Debug.LogError`.
- Already MP4/H.264 under cap → no conversion (native status `3`), upload original.
- Still > cap after 720p transcode (very long clip) → Failed. A second lower-res pass (e.g. 540p) is a noted **future** option, not v1.
- Temp `.mp4` lives in `Application.temporaryCachePath`, named per `tempId`; deleted after a successful send, retained on failure.
- Android/Editor: pass-through (proceeds, fails as today on Android — accepted).

## 8. Testing

- **EditMode (no device)** covers the C# logic only: the "is this a video needing the convert step?" gate, the post-conversion size check, the 120 s timeout value, and `VideoConverter`'s Editor pass-through (returns input path). The native `AVAssetExportSession` is not EditMode-testable.
- **Manual iOS device matrix:** HEVC `.mov` (transcode → delivers), H.264 `.mov` (remux → delivers), large clip (downscale → delivers or clean fail), already-`.mp4` (sends as-is), corrupt file (clean fail).

## 9. Out of scope (separate follow-ups)

- **Android Media3 `Transformer`** conversion (Phase 2).
- **`SeedVideoThumbCache` bug:** on iOS, `NativeGallery.GetVideoThumbnail` returns a non-readable texture so `EncodeToPNG` throws ("Texture is not readable") and staged video thumbnails don't cache. Non-blocking (send proceeds); separate fix.

## 10. File list

- NEW `Assets/Plugins/iOS/VideoConverter.mm`
- NEW `Assets/Scripts/Chat/VideoConverter.cs`
- MODIFY `Assets/Scripts/Main/ChatManager.MediaSend.cs` (convert step + post-conversion size check + 120 s timeout + temp cleanup)
- MODIFY `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` (retune guard: 16 MB block → ~512 MB ceiling)
- NEW `Assets/Tests/Editor/Chat/VideoConverterTests.cs`

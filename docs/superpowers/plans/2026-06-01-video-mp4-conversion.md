# Video → MP4/H.264 Conversion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert picked videos to an MP4 (H.264 + AAC) container on-device before uploading to Wappi, so iPhone `.mov`/HEVC clips actually deliver to WhatsApp.

**Architecture:** A native iOS plugin (`AVAssetExportSession`) remuxes/transcodes/downscales the picked video to MP4; a C# bridge exposes it as a coroutine; `PostMediaMessageRoutine` runs the conversion in the network half (after the optimistic bubble, before base64), so initial sends and tap-to-retry both get it. Editor/Android fall back to pass-through (no conversion) — Android video stays unfixed until a future Media3 phase.

**Tech Stack:** Unity 6 (C#, coroutines, `UnityWebRequest`), Objective-C++ iOS plugin (`AVFoundation`/`CoreMedia`), NUnit EditMode tests.

**Source spec:** `docs/superpowers/specs/2026-06-01-video-mp4-conversion-design.md`

**Project conventions that matter here:**
- NO `.asmdef`, NO namespaces — all types are global (runtime → `Assembly-CSharp`).
- Coroutines only in MonoBehaviours; off-main-thread work is awaited via `yield return new WaitUntil(...)` / `yield return null` polling — never `async`/`await`.
- Native plugins live in `Assets/Plugins/iOS/*.mm` (precedent: `AudioPlayer.mm`); C# bridges use `#if UNITY_IOS && !UNITY_EDITOR` `[DllImport("__Internal")]` (precedent: `IOSBridge.cs`).
- EditMode tests live in `Assets/Tests/Editor/Chat/`, NUnit `[Test]`, no namespace (precedent: `OutboxEntryMediaCompatTests.cs`).
- **Verification loop:** the human compiles and runs tests in their open Unity Editor and reports RED/GREEN or "Console clean." Native `.mm` code only compiles in an iOS build and only runs on a device — those tasks are verified by an iOS build + the device matrix in the Final section, not by EditMode tests.
- Commits go to `main` with the human's consent; stage only each task's named `.cs`/`.mm` files plus their Unity-generated `.meta` files.

---

## File Structure

- **Create** `Assets/Scripts/Chat/VideoConverter.cs` — C# bridge. Pure responsibility: expose "convert this video file to MP4 at this path" as a coroutine, with an Editor/Android pass-through. Holds the `[DllImport]` externs and the poll loop.
- **Create** `Assets/Plugins/iOS/VideoConverter.mm` — native iOS converter using `AVAssetExportSession`. Pure responsibility: codec/size inspection + remux/transcode/downscale to MP4, exposed as start/poll/error C functions.
- **Create** `Assets/Tests/Editor/Chat/VideoConverterTests.cs` — EditMode coverage for the C# pass-through behavior (the only part runnable without a device).
- **Modify** `Assets/Scripts/Main/ChatManager.MediaSend.cs` — `PostMediaMessageRoutine` gains the convert step (video only), a post-conversion size check, a 120 s media timeout, a temp-file cleanup, and a `WappiVideoCapBytes` constant.
- **Modify** `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` — retune the Part C pre-stage guard from a 16 MB block to a ~512 MB pathological-pick ceiling (rename `MaxVideoUploadBytes` → `MaxVideoPickBytes`).

---

## Task 1: `VideoConverter` C# bridge + Editor pass-through (TDD)

This is the only task with EditMode-runnable logic: in the Editor (and on Android), `Convert` must pass the input path straight through to `onResult` without touching native code. The iOS DllImport branch is compiled out in the Editor, so the test reliably exercises pass-through.

**Files:**
- Create: `Assets/Scripts/Chat/VideoConverter.cs`
- Test: `Assets/Tests/Editor/Chat/VideoConverterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Assets/Tests/Editor/Chat/VideoConverterTests.cs`:

```csharp
using NUnit.Framework;

public class VideoConverterTests
{
    // In the Editor (UNITY_EDITOR defined), the iOS branch is compiled out, so
    // Convert must pass the original path straight through with no conversion.
    [Test]
    public void Convert_InEditor_PassesInputPathThroughToOnResult()
    {
        string result = null;
        string error = null;

        var routine = VideoConverter.Convert(
            "/tmp/in.mov", "/tmp/out.mp4", 16L * 1024 * 1024,
            r => result = r,
            e => error = e);

        // Drive the coroutine to completion synchronously (Editor path is immediate).
        while (routine.MoveNext()) { }

        Assert.AreEqual("/tmp/in.mov", result);
        Assert.IsNull(error);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

In Unity: Test Runner → EditMode → Run. Expected: **compile error / FAIL** — `VideoConverter` does not exist yet. Confirm RED with the human.

- [ ] **Step 3: Write the minimal implementation**

Create `Assets/Scripts/Chat/VideoConverter.cs`:

```csharp
using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Converts a picked video to an MP4 (H.264/AAC) file suitable for Wappi upload.
/// On iOS this calls a native AVAssetExportSession plugin (VideoConverter.mm) and
/// polls it from a coroutine. In the Editor and on Android there is no native
/// converter, so it passes the original path through unchanged — meaning Android
/// video sends remain unconverted (and will fail) until a future Media3 phase.
/// </summary>
public static class VideoConverter
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int    _StartVideoConvert(string inPath, string outPath, long maxBytes);
    [DllImport("__Internal")] private static extern int    _PollVideoConvert(int jobId);   // 0 run, 1 done, 2 fail, 3 use-original
    [DllImport("__Internal")] private static extern IntPtr _VideoConvertError(int jobId);
#endif

    /// <summary>
    /// Yields until conversion finishes. Invokes onResult with the path to upload
    /// (the converted .mp4, or the original path when no conversion is needed), or
    /// invokes onError with a message. Never throws.
    /// </summary>
    public static IEnumerator Convert(string inputPath, string outputPath, long maxBytes,
                                      Action<string> onResult, Action<string> onError)
    {
#if UNITY_IOS && !UNITY_EDITOR
        int jobId = _StartVideoConvert(inputPath, outputPath, maxBytes);
        int status = _PollVideoConvert(jobId);
        while (status == 0)
        {
            yield return null;
            status = _PollVideoConvert(jobId);
        }

        if (status == 2)
        {
            string message = Marshal.PtrToStringAnsi(_VideoConvertError(jobId)) ?? "video conversion failed";
            onError?.Invoke(message);
            yield break;
        }

        // status 1 = converted to outputPath; status 3 = already deliverable, use original.
        onResult?.Invoke(status == 3 ? inputPath : outputPath);
#else
        // Editor + Android: no native converter — pass the original through unchanged.
        onResult?.Invoke(inputPath);
        yield break;
#endif
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

In Unity: Test Runner → EditMode → Run. Expected: **PASS** (`Convert_InEditor_PassesInputPathThroughToOnResult`). Confirm GREEN with the human.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Chat/VideoConverter.cs Assets/Scripts/Chat/VideoConverter.cs.meta \
        Assets/Tests/Editor/Chat/VideoConverterTests.cs Assets/Tests/Editor/Chat/VideoConverterTests.cs.meta
git commit -m "$(cat <<'EOF'
feat(chat): add VideoConverter bridge with Editor/Android pass-through

C# coroutine that calls the native iOS converter and polls it; Editor and
Android fall back to passing the original path through unchanged.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: native iOS converter `VideoConverter.mm`

Implements the three C functions `VideoConverter.cs` imports. No EditMode test is possible — native code compiles only in an iOS build and runs only on a device. Verification here is an **iOS build that compiles**; functional verification is the device matrix in the Final section.

**Files:**
- Create: `Assets/Plugins/iOS/VideoConverter.mm`

- [ ] **Step 1: Create the native plugin**

Create `Assets/Plugins/iOS/VideoConverter.mm`:

```objc
// Converts a picked video to MP4 (H.264/AAC) via AVAssetExportSession.
// Exposed to Unity as start/poll/error C functions (see VideoConverter.cs).
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>

// status: 0 running, 1 done, 2 failed, 3 use-original-as-is
typedef struct {
    int   status;
    char *error;   // strdup'd on failure; freed when overwritten
} ConvertJob;

static NSMutableDictionary<NSNumber *, NSValue *> *gJobs = nil;
static NSObject *gLock = nil;
static int gNextJob = 1;

static void EnsureInit() {
    if (gJobs == nil) { gJobs = [NSMutableDictionary dictionary]; gLock = [NSObject new]; }
}

static void SetJob(int jobId, int status, const char *err) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return;
        ConvertJob *job = (ConvertJob *)v.pointerValue;
        if (err) { if (job->error) free(job->error); job->error = strdup(err); }
        job->status = status;
    }
}

static void RunExport(AVAssetExportSession *session, int jobId) {
    [session exportAsynchronouslyWithCompletionHandler:^{
        if (session.status == AVAssetExportSessionStatusCompleted) {
            SetJob(jobId, 1, NULL);
        } else {
            NSString *msg = session.error ? session.error.localizedDescription : @"export failed";
            SetJob(jobId, 2, msg.UTF8String);
        }
    }];
}

extern "C" int _StartVideoConvert(const char *inPathC, const char *outPathC, long maxBytes) {
    EnsureInit();
    NSString *inPath  = [NSString stringWithUTF8String:inPathC];
    NSString *outPath = [NSString stringWithUTF8String:outPathC];

    ConvertJob *job = (ConvertJob *)calloc(1, sizeof(ConvertJob));
    int jobId;
    @synchronized (gLock) { jobId = gNextJob++; gJobs[@(jobId)] = [NSValue valueWithPointer:job]; }

    AVURLAsset *asset = [AVURLAsset URLAssetWithURL:[NSURL fileURLWithPath:inPath] options:nil];
    AVAssetTrack *vtrack = [asset tracksWithMediaType:AVMediaTypeVideo].firstObject;
    if (vtrack == nil) { SetJob(jobId, 2, "no video track"); return jobId; }

    BOOL isH264 = NO;
    NSArray *fmts = vtrack.formatDescriptions;
    if (fmts.count > 0) {
        CMFormatDescriptionRef desc = (__bridge CMFormatDescriptionRef)fmts.firstObject;
        isH264 = (CMFormatDescriptionGetMediaSubType(desc) == kCMVideoCodecType_H264);
    }

    unsigned long long fileSize = 0;
    NSDictionary *attrs = [[NSFileManager defaultManager] attributesOfItemAtPath:inPath error:nil];
    if (attrs) fileSize = [attrs fileSize];
    BOOL isMp4    = [[inPath.pathExtension lowercaseString] isEqualToString:@"mp4"];
    BOOL underCap = (maxBytes <= 0) || (fileSize <= (unsigned long long)maxBytes);

    // Already an MP4/H.264 under the cap → tell Unity to upload the original untouched.
    if (isH264 && isMp4 && underCap) { SetJob(jobId, 3, NULL); return jobId; }

    // Remux H.264 that's under cap; otherwise transcode/downscale to 720p H.264.
    NSString *preset = (isH264 && underCap) ? AVAssetExportPresetPassthrough
                                            : AVAssetExportPreset1280x720;

    [[NSFileManager defaultManager] removeItemAtPath:outPath error:nil];

    AVAssetExportSession *exp = [[AVAssetExportSession alloc] initWithAsset:asset presetName:preset];
    if (exp == nil) { SetJob(jobId, 2, "could not create export session"); return jobId; }
    exp.outputURL = [NSURL fileURLWithPath:outPath];
    exp.outputFileType = AVFileTypeMPEG4;
    exp.shouldOptimizeForNetworkUse = YES;

    // Passthrough is not always compatible with MP4 output; fall back to transcode.
    if ([preset isEqualToString:AVAssetExportPresetPassthrough]) {
        [exp determineCompatibleFileTypesWithCompletionHandler:^(NSArray<AVFileType> *types) {
            if ([types containsObject:AVFileTypeMPEG4]) {
                RunExport(exp, jobId);
            } else {
                AVAssetExportSession *t = [[AVAssetExportSession alloc] initWithAsset:asset
                                                                          presetName:AVAssetExportPreset1280x720];
                t.outputURL = [NSURL fileURLWithPath:outPath];
                t.outputFileType = AVFileTypeMPEG4;
                t.shouldOptimizeForNetworkUse = YES;
                RunExport(t, jobId);
            }
        }];
    } else {
        RunExport(exp, jobId);
    }
    return jobId;
}

extern "C" int _PollVideoConvert(int jobId) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return 2;
        return ((ConvertJob *)v.pointerValue)->status;
    }
}

extern "C" const char *_VideoConvertError(int jobId) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return "unknown job";
        ConvertJob *job = (ConvertJob *)v.pointerValue;
        return job->error ? job->error : "";   // Unity copies via PtrToStringAnsi; do not free here
    }
}
```

- [ ] **Step 2: Verify the iOS build compiles**

Build the project for iOS (`File → Build Settings → iOS → Build`, or your usual iOS build). Expected: **the generated Xcode project compiles with no errors** in `VideoConverter.mm` (`AVFoundation`/`CoreMedia` are linked automatically by Unity for files that import them). Confirm with the human. (Functional behavior is verified on-device in the Final section — not here.)

- [ ] **Step 3: Commit**

```bash
git add Assets/Plugins/iOS/VideoConverter.mm Assets/Plugins/iOS/VideoConverter.mm.meta
git commit -m "$(cat <<'EOF'
feat(ios): native AVAssetExportSession video→MP4 converter

Remuxes H.264 .mov, transcodes HEVC, downscales oversized to 720p H.264,
and reports already-deliverable MP4/H.264 so callers upload it untouched.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: integrate conversion into `PostMediaMessageRoutine`

Run the converter for videos before base64, persist the converted path for retries, enforce the cap post-conversion, lengthen the upload timeout, and clean up the temp file on success. No EditMode test (coroutine + native + network); verified by an Editor compile and the device matrix.

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.MediaSend.cs`

- [ ] **Step 1: Add the `WappiVideoCapBytes` constant**

In `Assets/Scripts/Main/ChatManager.MediaSend.cs`, add the constant immediately after the `public partial class ChatManager` opening brace, before the `StageLocalMedia` `/// <summary>`:

```csharp
public partial class ChatManager
{
    // Wappi's video endpoint only delivers MP4/H.264 under ~16 MB; a converted
    // file still above this can't be sent (see design spec / project memory).
    private const long WappiVideoCapBytes = 16L * 1024 * 1024;
```

- [ ] **Step 2: Insert the convert step before base64**

Replace this block (currently at the top of `PostMediaMessageRoutine`, right after the endpoint-URL guard):

```csharp
        // --- off-thread read + base64 (no frame hitch / no OOM stall on the main thread) ---
        var encodeTask = Base64Encoder.EncodeFileAsync(entry.mediaPath);
```

with:

```csharp
        // --- video: ensure MP4/H.264 before upload (Wappi/WhatsApp reject .mov/HEVC) ---
        string uploadPath = entry.mediaPath;
        if (kind == AttachmentKind.GalleryVideo)
        {
            string convertedPath = System.IO.Path.Combine(Application.temporaryCachePath, $"send_{entry.tempId}.mp4");
            bool   convertOk     = false;
            string convertResult = null;
            string convertErr    = null;
            yield return VideoConverter.Convert(entry.mediaPath, convertedPath, WappiVideoCapBytes,
                r => { convertOk = true; convertResult = r; },
                e => { convertErr = e; });

            if (!convertOk || string.IsNullOrEmpty(convertResult))
            {
                Debug.LogError($"[Wappi] video convert failed for {entry.mediaPath}: {convertErr}");
                OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
                yield break;
            }

            uploadPath = convertResult;
            // Persist the converted path so a tap-to-retry reuses it (the native
            // converter then short-circuits an already-MP4/H.264 file as "use as-is").
            if (uploadPath != entry.mediaPath) { entry.mediaPath = uploadPath; Outbox.Update(entry); }

            long convertedBytes = new System.IO.FileInfo(uploadPath).Length;
            if (convertedBytes > WappiVideoCapBytes)
            {
                Debug.LogWarning($"[Wappi] video still {convertedBytes} bytes after conversion (cap {WappiVideoCapBytes}); failing send");
                OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
                yield break;
            }
        }

        // --- off-thread read + base64 (no frame hitch / no OOM stall on the main thread) ---
        var encodeTask = Base64Encoder.EncodeFileAsync(uploadPath);
```

- [ ] **Step 3: Lengthen the media upload timeout**

In the same routine, replace:

```csharp
        www.timeout = 30;
```

with:

```csharp
        www.timeout = 120;   // media uploads carry multi-MB base64 bodies; 30s (text default) is too short
```

- [ ] **Step 4: Delete the converted temp file on a successful send**

In the success branch, immediately after the line:

```csharp
            Outbox.RemoveAt(sendCacheRoot, entry.chatId, entry.tempId);
```

add:

```csharp
            if (uploadPath != null && uploadPath.StartsWith(Application.temporaryCachePath))
            {
                try { System.IO.File.Delete(uploadPath); } catch { /* best-effort cleanup */ }
            }
```

(`uploadPath` is the converted temp only when conversion produced a new file; a pass-through or non-video keeps `entry.mediaPath`, which is not under `temporaryCachePath`, so nothing is deleted in those cases.)

- [ ] **Step 5: Verify clean compile**

In Unity, let it recompile. Expected: **zero Console errors.** `VideoConverter`, `WappiVideoCapBytes`, and `Application.temporaryCachePath` all resolve. Confirm "Console clean" with the human.

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.MediaSend.cs
git commit -m "$(cat <<'EOF'
feat(chat): convert video to MP4/H.264 before Wappi upload

PostMediaMessageRoutine runs VideoConverter for videos before base64, caps
the converted size, uses a 120s media timeout, and cleans up the temp file
on success. Retries reuse the converted file.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: retune the pre-stage guard in `AttachmentPreviewScreen`

The Part C guard rejects videos over 16 MB *before* staging. Now that conversion shrinks files, that's wrong — a 25 MB HEVC clip may convert to 8 MB and send fine. Replace the 16 MB block with a high "pathological pick" ceiling so normal large clips pass through to conversion; the real ~16 MB enforcement now lives post-conversion (Task 3).

**Files:**
- Modify: `Assets/Scripts/Chat/AttachmentPreviewScreen.cs`

- [ ] **Step 1: Rename + revalue the constant**

Replace:

```csharp
    private const long MaxVideoUploadBytes = 16L * 1024 * 1024;   // WhatsApp-like; tunable
```

with:

```csharp
    // Pathological-pick ceiling only: reject absurdly large videos before we bother
    // converting. The real ~16 MB Wappi cap is enforced post-conversion in
    // ChatManager.PostMediaMessageRoutine, since conversion shrinks the file.
    private const long MaxVideoPickBytes = 512L * 1024 * 1024;
```

- [ ] **Step 2: Update the guard in `OnSendTapped`**

Replace:

```csharp
        // Reject over-cap video BEFORE staging. pick.FileSizeBytes is already
        // populated by the picker (used for the document size label), so no extra I/O.
        if (_currentPick.Kind == AttachmentKind.GalleryVideo &&
            _currentPick.FileSizeBytes > MaxVideoUploadBytes)
        {
            ShowSizeError($"Video is too large to send (max {MaxVideoUploadBytes / (1024 * 1024)} MB).");
            if (sendButton != null) sendButton.interactable = true;   // let the user go Back and re-pick
            return;                                                   // do NOT stage, do NOT close
        }
```

with:

```csharp
        // Reject only absurdly large videos here; normal large clips are shrunk by
        // on-device conversion before upload (see PostMediaMessageRoutine).
        if (_currentPick.Kind == AttachmentKind.GalleryVideo &&
            _currentPick.FileSizeBytes > MaxVideoPickBytes)
        {
            ShowSizeError("This video is too large to process.");
            if (sendButton != null) sendButton.interactable = true;   // let the user go Back and re-pick
            return;                                                   // do NOT stage, do NOT close
        }
```

- [ ] **Step 3: Verify clean compile**

In Unity, let it recompile. Expected: **zero Console errors.** `MaxVideoUploadBytes` is fully replaced by `MaxVideoPickBytes` (no stale references). Confirm "Console clean" with the human.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Chat/AttachmentPreviewScreen.cs
git commit -m "$(cat <<'EOF'
feat(chat): retune pre-stage video guard to a pathological-pick ceiling

Conversion now shrinks oversized videos, so block only absurdly large picks
(>512 MB) pre-stage; the real ~16 MB cap is enforced post-conversion.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification (iOS device matrix)

Native conversion can't be exercised in EditMode, so the acceptance gate is a manual run on a real iOS device with a connected Wappi WhatsApp profile. Build to device and, for each case, confirm the recipient's WhatsApp actually shows/plays the video and the bubble reaches Sent (single tick):

- [ ] **HEVC `.mov` camera clip** (Settings → Camera → Formats → High Efficiency): transcodes → delivers.
- [ ] **H.264 `.mov` camera clip** (Most Compatible): remuxes (fast) → delivers.
- [ ] **Large clip (e.g. 60–100 MB)**: downscales to 720p → delivers, or fails cleanly (Failed bubble) if still over cap.
- [ ] **Already-`.mp4`/H.264 under cap**: sent untouched (no needless re-encode) → delivers.
- [ ] **Corrupt/zero-byte file**: clean Failed bubble + `[Wappi] video convert failed` log, no crash.
- [ ] **Tap-to-retry** on a network-failed video: reuses the converted `.mp4` (native reports "use as-is"), no re-transcode.
- [ ] **Bot-switch mid-send**: still reconciles to the originating bot (existing snapshot behavior unchanged).

If all pass, complete via `superpowers:finishing-a-development-branch`.

---

## Spec coverage map (self-review)

| Spec section | Task |
|---|---|
| §6.1 iOS native converter (codec detect, passthrough/transcode/downscale, MP4) | Task 2 |
| §6.2 C# bridge + Editor/Android pass-through | Task 1 |
| §6.3 integration in PostMediaMessageRoutine (convert step, retry reuse, temp cleanup) | Task 3 |
| §6.4 size-cap repurpose (pre-stage ceiling vs post-conversion cap) | Task 3 (post cap) + Task 4 (pre-stage ceiling) |
| §6.5 120 s media upload timeout | Task 3 |
| §7 error/edge cases (convert fail, already-MP4, still-over-cap, cleanup) | Tasks 2 + 3 + Final |
| §8 testing (EditMode pass-through; device matrix) | Task 1 (EditMode) + Final (device) |
| §9 out of scope (Android Media3, SeedVideoThumbCache) | not in this plan, by design |

**Type/signature consistency check:** `VideoConverter.Convert(string, string, long, Action<string>, Action<string>)` is defined in Task 1 and called identically in Task 3. Native status codes (0/1/2/3) match between `VideoConverter.mm` (Task 2) and the poll loop in `VideoConverter.cs` (Task 1). `WappiVideoCapBytes` (Task 3) and `MaxVideoPickBytes` (Task 4) are distinct constants in distinct classes — intentional. No placeholders; every code step shows complete code.

# WhatsApp-style Video Compression (Explicit Bitrate) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transcode outgoing videos to ≤720p H.264 at an explicit ~2 Mbps (≈15 MB/min) so normal-length clips fit the upload cap, instead of the current ~11 Mbps / ~85 MB/min.

**Architecture:** Rewrite the transcode path of the native iOS plugin `VideoConverter.mm` from an `AVAssetExportSession` quality preset to an `AVAssetReader → AVAssetWriter` pipeline with an explicit average bitrate; keep the existing start/poll/error/free C interface and the "already-small → use as-is" fast path; add a progress poll. Then raise three Unity-side limit constants (pick ceiling, post-conversion cap, upload timeout).

**Tech Stack:** Objective-C++ (`AVFoundation`/`CoreMedia`/`AudioToolbox`), Unity 6 C# (`DllImport`), iOS only.

**Source spec:** `docs/superpowers/specs/2026-06-01-video-compression-bitrate-design.md`

**Project conventions:** NO `.asmdef`/namespaces (all global, Assembly-CSharp). Native `.mm` compiles only in an iOS build (the Editor never compiles it) and runs only on device → native tasks are verified by an iOS build + the on-device matrix, not EditMode. Commits go to `main` with the human's consent; stage only each task's named files (+ Unity `.meta` for new files only — these tasks modify existing files, so no new `.meta`). The human compiles/builds and reports.

---

## File Structure

- **Modify** `Assets/Plugins/iOS/VideoConverter.mm` — replace the transcode path with the reader/writer pipeline + progress; keep the C interface, job table, and use-as-is fast path. (Full replacement file given in Task 1.)
- **Modify** `Assets/Scripts/Chat/VideoConverter.cs` — add the `_PollVideoConvertProgress` extern (forward-declares the native progress hook for the future UI bar).
- **Modify** `Assets/Scripts/Main/ChatManager.MediaSend.cs` — `WappiVideoCapBytes` 16 MB → 128 MB; media `www.timeout` 120 → 300.
- **Modify** `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` — `MaxVideoPickBytes` 512 MB → 1 GB.

---

## Task 1: Rewrite `VideoConverter.mm` transcode path (reader/writer @ ~2 Mbps + progress)

No EditMode test is possible (native). Verification = the iOS build compiles + the on-device matrix (Final section).

**Files:**
- Modify (full replacement): `Assets/Plugins/iOS/VideoConverter.mm`

- [ ] **Step 1: Replace the entire file**

Overwrite `Assets/Plugins/iOS/VideoConverter.mm` with exactly:

```objc
// Converts a picked video to MP4 (H.264/AAC) for Wappi upload.
// HEVC / .mov / high-bitrate sources are transcoded to <=720p H.264 at ~2 Mbps via an
// AVAssetReader -> AVAssetWriter pipeline; an already-small MP4/H.264 is used as-is.
// Exposed to Unity as start/poll/progress/error/free C functions (see VideoConverter.cs).
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>
#import <AudioToolbox/AudioToolbox.h>

// status: 0 running, 1 done, 2 failed, 3 use-original-as-is
typedef struct {
    int   status;
    float progress;   // 0..1 during transcode
    char *error;      // strdup'd on failure
} ConvertJob;

static NSMutableDictionary<NSNumber *, NSValue *> *gJobs = nil;
static NSObject *gLock = nil;
static int gNextJob = 1;

// Encoder budget (WhatsApp-style).
static const int  kMaxLongSide      = 1280;
static const int  kMaxShortSide     = 720;
static const int  kVideoBitrate     = 2000000;   // ~2 Mbps
static const int  kAudioBitrate     = 128000;    // 128 kbps
static const long kUseAsIsMaxBitrate = 2500000;  // already small enough -> skip re-encode

static void EnsureInit() {
    static dispatch_once_t once;
    dispatch_once(&once, ^{ gJobs = [NSMutableDictionary dictionary]; gLock = [NSObject new]; });
}

static void SetJob(int jobId, int status, const char *err) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return;
        ConvertJob *job = (ConvertJob *)v.pointerValue;
        if (job->status != 0) return;   // already terminal; never overwrite
        if (err) { if (job->error) free(job->error); job->error = strdup(err); }
        job->status = status;
    }
}

static void SetProgress(int jobId, float p) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return;
        ((ConvertJob *)v.pointerValue)->progress = p;
    }
}

static int EvenClamp(CGFloat x) { int v = (int)(x + 0.5); v -= (v % 2); return v < 2 ? 2 : v; }

// AVAssetReader -> AVAssetWriter transcode to <=720p H.264 @ ~2 Mbps + AAC 128k MP4.
static void TranscodeReaderWriter(AVURLAsset *asset, NSString *outPath, int jobId) {
    NSError *err = nil;

    AVAssetTrack *vtrack = [asset tracksWithMediaType:AVMediaTypeVideo].firstObject;
    if (vtrack == nil) { SetJob(jobId, 2, "no video track"); return; }

    AVAssetReader *reader = [AVAssetReader assetReaderWithAsset:asset error:&err];
    if (reader == nil) { SetJob(jobId, 2, (err.localizedDescription ?: @"reader init failed").UTF8String); return; }

    [[NSFileManager defaultManager] removeItemAtPath:outPath error:nil];
    AVAssetWriter *writer = [AVAssetWriter assetWriterWithURL:[NSURL fileURLWithPath:outPath] fileType:AVFileTypeMPEG4 error:&err];
    if (writer == nil) { SetJob(jobId, 2, (err.localizedDescription ?: @"writer init failed").UTF8String); return; }
    writer.shouldOptimizeForNetworkUse = YES;

    // Target size: fit the displayed frame within (1280 x 720), preserve aspect, never upscale.
    CGSize natural = vtrack.naturalSize;
    CGAffineTransform tx = vtrack.preferredTransform;
    CGSize disp = CGSizeApplyAffineTransform(natural, tx);
    CGFloat dispW = fabs(disp.width), dispH = fabs(disp.height);
    CGFloat longer = MAX(dispW, dispH), shorter = MIN(dispW, dispH);
    CGFloat scale = (longer > 0 && shorter > 0)
        ? MIN((CGFloat)kMaxLongSide / longer, (CGFloat)kMaxShortSide / shorter) : 1.0;
    if (scale > 1.0) scale = 1.0;
    int outW = EvenClamp(natural.width * scale);
    int outH = EvenClamp(natural.height * scale);

    AVAssetReaderTrackOutput *vOut = [AVAssetReaderTrackOutput
        assetReaderTrackOutputWithTrack:vtrack
        outputSettings:@{ (id)kCVPixelBufferPixelFormatTypeKey: @(kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange) }];
    vOut.alwaysCopiesSampleData = NO;
    if (![reader canAddOutput:vOut]) { SetJob(jobId, 2, "cannot add video reader output"); return; }
    [reader addOutput:vOut];

    AVAssetWriterInput *vIn = [AVAssetWriterInput
        assetWriterInputWithMediaType:AVMediaTypeVideo
        outputSettings:@{
            AVVideoCodecKey: AVVideoCodecTypeH264,
            AVVideoWidthKey: @(outW),
            AVVideoHeightKey: @(outH),
            AVVideoCompressionPropertiesKey: @{
                AVVideoAverageBitRateKey: @(kVideoBitrate),
                AVVideoMaxKeyFrameIntervalKey: @(60),
                AVVideoProfileLevelKey: AVVideoProfileLevelH264MainAutoLevel
            }
        }];
    vIn.expectsMediaDataInRealTime = NO;
    vIn.transform = tx;   // carry rotation as metadata; encoder scales natural buffers to outW x outH
    if (![writer canAddInput:vIn]) { SetJob(jobId, 2, "cannot add video writer input"); return; }
    [writer addInput:vIn];

    AVAssetTrack *atrack = [asset tracksWithMediaType:AVMediaTypeAudio].firstObject;
    AVAssetReaderTrackOutput *aOut = nil;
    AVAssetWriterInput *aIn = nil;
    if (atrack) {
        aOut = [AVAssetReaderTrackOutput
            assetReaderTrackOutputWithTrack:atrack
            outputSettings:@{ AVFormatIDKey: @(kAudioFormatLinearPCM) }];
        aOut.alwaysCopiesSampleData = NO;
        if ([reader canAddOutput:aOut]) {
            [reader addOutput:aOut];
            aIn = [AVAssetWriterInput
                assetWriterInputWithMediaType:AVMediaTypeAudio
                outputSettings:@{
                    AVFormatIDKey: @(kAudioFormatMPEG4AAC),
                    AVNumberOfChannelsKey: @(2),
                    AVSampleRateKey: @(44100),
                    AVEncoderBitRateKey: @(kAudioBitrate)
                }];
            aIn.expectsMediaDataInRealTime = NO;
            if ([writer canAddInput:aIn]) { [writer addInput:aIn]; } else { aIn = nil; aOut = nil; }
        } else {
            aOut = nil;
        }
    }

    if (![reader startReading]) { SetJob(jobId, 2, (reader.error.localizedDescription ?: @"startReading failed").UTF8String); return; }
    if (![writer startWriting]) { SetJob(jobId, 2, (writer.error.localizedDescription ?: @"startWriting failed").UTF8String); return; }
    [writer startSessionAtSourceTime:kCMTimeZero];

    CMTime dur = asset.duration;
    double durSec = (dur.timescale > 0) ? (double)dur.value / dur.timescale : 0;

    dispatch_group_t group = dispatch_group_create();

    dispatch_group_enter(group);
    dispatch_queue_t vQ = dispatch_queue_create("videoconv.v", DISPATCH_QUEUE_SERIAL);
    [vIn requestMediaDataWhenReadyOnQueue:vQ usingBlock:^{
        while (vIn.isReadyForMoreMediaData) {
            CMSampleBufferRef sb = [vOut copyNextSampleBuffer];
            if (sb) {
                if (durSec > 0) {
                    double sec = CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(sb));
                    SetProgress(jobId, (float)MIN(1.0, MAX(0.0, sec / durSec)));
                }
                [vIn appendSampleBuffer:sb];
                CFRelease(sb);
            } else {
                [vIn markAsFinished];
                dispatch_group_leave(group);
                break;
            }
        }
    }];

    if (aIn) {
        dispatch_group_enter(group);
        dispatch_queue_t aQ = dispatch_queue_create("videoconv.a", DISPATCH_QUEUE_SERIAL);
        [aIn requestMediaDataWhenReadyOnQueue:aQ usingBlock:^{
            while (aIn.isReadyForMoreMediaData) {
                CMSampleBufferRef sb = [aOut copyNextSampleBuffer];
                if (sb) {
                    [aIn appendSampleBuffer:sb];
                    CFRelease(sb);
                } else {
                    [aIn markAsFinished];
                    dispatch_group_leave(group);
                    break;
                }
            }
        }];
    }

    dispatch_group_notify(group, dispatch_get_global_queue(QOS_CLASS_DEFAULT, 0), ^{
        if (reader.status == AVAssetReaderStatusFailed) {
            [writer cancelWriting];
            SetJob(jobId, 2, (reader.error.localizedDescription ?: @"reader failed").UTF8String);
            return;
        }
        [writer finishWritingWithCompletionHandler:^{
            if (writer.status == AVAssetWriterStatusCompleted) {
                SetProgress(jobId, 1.0f);
                SetJob(jobId, 1, NULL);
            } else {
                SetJob(jobId, 2, (writer.error.localizedDescription ?: @"writer failed").UTF8String);
            }
        }];
    });
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
    BOOL isMp4 = [[inPath.pathExtension lowercaseString] isEqualToString:@"mp4"];
    BOOL underCap = (maxBytes <= 0) || (fileSize <= (unsigned long long)maxBytes);

    CMTime dur = asset.duration;
    double durSec = (dur.timescale > 0) ? (double)dur.value / dur.timescale : 0;
    long approxBitrate = (durSec > 0) ? (long)((double)fileSize * 8.0 / durSec) : LONG_MAX;

    // Already small/deliverable -> upload original untouched.
    if (isH264 && isMp4 && underCap && approxBitrate <= kUseAsIsMaxBitrate) {
        SetJob(jobId, 3, NULL);
        return jobId;
    }

    // Everything else (HEVC, .mov, high-bitrate/oversized H.264) -> transcode.
    // Run async so the call returns immediately; Unity polls status/progress.
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        TranscodeReaderWriter(asset, outPath, jobId);
    });
    return jobId;
}

extern "C" int _PollVideoConvert(int jobId) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return 2;
        return ((ConvertJob *)v.pointerValue)->status;
    }
}

extern "C" float _PollVideoConvertProgress(int jobId) {
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return 0.0f;
        return ((ConvertJob *)v.pointerValue)->progress;
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

extern "C" void _FreeVideoConvertJob(int jobId) {
    EnsureInit();
    @synchronized (gLock) {
        NSValue *v = gJobs[@(jobId)];
        if (!v) return;
        ConvertJob *job = (ConvertJob *)v.pointerValue;
        if (job->error) free(job->error);
        free(job);
        [gJobs removeObjectForKey:@(jobId)];
    }
}
```

- [ ] **Step 2: Verify the iOS build compiles**

Build for iOS. Expected: the generated Xcode project **compiles** `VideoConverter.mm` with no errors (`AVFoundation`/`CoreMedia`/`AudioToolbox` link automatically for files that import them). Confirm with the human. Functional behavior is validated in the Final device matrix.

- [ ] **Step 3: Commit**

```bash
git add Assets/Plugins/iOS/VideoConverter.mm
git commit -m "$(cat <<'EOF'
feat(ios): transcode video at explicit ~2 Mbps via AVAssetReader/Writer

Replaces the high-bitrate AVAssetExportPreset1280x720 (~11 Mbps) with a
reader/writer pipeline at <=720p H.264 ~2 Mbps + AAC 128k (~15 MB/min).
Keeps the use-as-is fast path; adds _PollVideoConvertProgress.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Add the `_PollVideoConvertProgress` extern in `VideoConverter.cs`

Forward-declares the native progress hook so the future circular-progress UI can read it without touching the bridge again. The `Convert` coroutine is unchanged.

**Files:**
- Modify: `Assets/Scripts/Chat/VideoConverter.cs`

- [ ] **Step 1: Add the extern**

Replace:

```csharp
    [DllImport("__Internal")] private static extern IntPtr _VideoConvertError(int jobId);
    [DllImport("__Internal")] private static extern void   _FreeVideoConvertJob(int jobId);
#endif
```

with:

```csharp
    [DllImport("__Internal")] private static extern IntPtr _VideoConvertError(int jobId);
    [DllImport("__Internal")] private static extern void   _FreeVideoConvertJob(int jobId);
    [DllImport("__Internal")] private static extern float  _PollVideoConvertProgress(int jobId);
#endif
```

- [ ] **Step 2: Verify clean compile**

In Unity, let it recompile. Expected: **zero Console errors** (the extern is under `#if UNITY_IOS && !UNITY_EDITOR`, so the Editor `#else` path is unchanged).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/VideoConverter.cs
git commit -m "$(cat <<'EOF'
feat(chat): declare _PollVideoConvertProgress extern for future progress UI

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Raise the cap + timeout in `ChatManager.MediaSend.cs`

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.MediaSend.cs`

- [ ] **Step 1: Raise the post-conversion cap to 128 MB**

Replace:

```csharp
    private const long WappiVideoCapBytes = 16L * 1024 * 1024;
```

with:

```csharp
    private const long WappiVideoCapBytes = 128L * 1024 * 1024;
```

- [ ] **Step 2: Raise the media upload timeout to 300 s**

Replace:

```csharp
        www.timeout = 120;   // media uploads carry multi-MB base64 bodies; 30s (text default) is too short
```

with:

```csharp
        www.timeout = 300;   // media uploads carry multi-MB base64 bodies; 30s (text default) is too short
```

- [ ] **Step 2b: Verify clean compile**

In Unity, recompile. Expected: **zero Console errors**.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Main/ChatManager.MediaSend.cs
git commit -m "$(cat <<'EOF'
feat(chat): raise media cap to 128MB and upload timeout to 300s

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Raise the pre-stage pick ceiling in `AttachmentPreviewScreen.cs`

**Files:**
- Modify: `Assets/Scripts/Chat/AttachmentPreviewScreen.cs`

- [ ] **Step 1: Raise the pick ceiling to 1 GB**

Replace:

```csharp
    private const long MaxVideoPickBytes = 512L * 1024 * 1024;
```

with:

```csharp
    private const long MaxVideoPickBytes = 1024L * 1024 * 1024;
```

- [ ] **Step 2: Verify clean compile**

In Unity, recompile. Expected: **zero Console errors**.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Chat/AttachmentPreviewScreen.cs
git commit -m "$(cat <<'EOF'
feat(chat): raise pre-stage video pick ceiling to 1GB

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification (iOS device matrix)

Build to a real iOS device with a Wappi WhatsApp profile. For each, confirm the bubble reaches **Sent** and the video plays in the **recipient's WhatsApp**:

- [ ] The four previously-failing clips (1 min/50 s/42 s/24 s): each now produces an `.mp4` of **~15 MB/min** (≈15/12.5/10.5/6 MB) and **delivers**. (Previously ~85/67/57/32 MB → cap-rejected.)
- [ ] **Quality eyeball** at 720p/2 Mbps — acceptable for chat.
- [ ] **Near-cap clip** (a long clip that compresses to ~100 MB): confirms Wappi accepts a large MP4. If it 400s, report the `[Wappi]` line — we lower `WappiVideoCapBytes` to the measured-safe value.
- [ ] **Already-MP4/H.264 small clip**: sent untouched (use-as-is), still replays locally in the bubble.
- [ ] **HEVC `.mov`** and **H.264 `.mov`**: both transcode and deliver; **portrait** clip arrives upright (rotation preserved).
- [ ] **Corrupt/zero-byte file**: clean Failed bubble + `[Wappi] video convert failed` log, no crash.
- [ ] **Tap-to-retry** a network-failed video: re-converts and resends.

If all pass, complete via `superpowers:finishing-a-development-branch`.

---

## Spec coverage map (self-review)

| Spec section | Task |
|---|---|
| §2 reader/writer @ 2 Mbps / ≤720p / AAC 128k, MP4 faststart | Task 1 |
| §2 use-as-is (MP4+H.264+under-cap+low-bitrate) decision | Task 1 (`kUseAsIsMaxBitrate`, status 3) |
| §2 progress hook (`_PollVideoConvertProgress`) | Task 1 (native) + Task 2 (C# extern) |
| §3.3 cap 128 MB + timeout 300 s | Task 3 |
| §3.4 pick ceiling 1 GB | Task 4 |
| §4 provisional cap — near-cap device check | Final matrix |
| §6 error/edge cases (setup/encode fail, use-as-is, retry) | Task 1 + Final |
| §7 testing (EditMode pass-through unchanged; device matrix) | Final |
| §5 out of scope (document fallback, progress UI, Android) | not in this plan, by design |

**Placeholder scan:** none — Task 1 ships the complete `.mm`; Tasks 2–4 show exact old/new strings. **Type/signature consistency:** the C interface (`_StartVideoConvert`/`_PollVideoConvert`/`_PollVideoConvertProgress`/`_VideoConvertError`/`_FreeVideoConvertJob`) matches between the `.mm` (Task 1) and the C# externs (Task 2 adds the one new `float` extern; the rest already exist). Constants (`WappiVideoCapBytes`, `MaxVideoPickBytes`) match their committed declarations being replaced.
```

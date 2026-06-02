# Incoming Video Thumbnails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** For every incoming video, natively extract a frame from its remote URL and use it as the bubble thumbnail, so previews never depend on Wappi providing a `JPEGThumbnail`.

**Architecture:** A pure-C# concurrency queue (`VideoThumbQueue`) drives an iOS-native `AVAssetImageGenerator` extractor (`VideoThumbnailExtractor.mm`) via a coroutine bridge (`VideoThumbnailExtractor.cs`), orchestrated by a new `ChatManager.VideoThumbs.cs` partial. Each server-sourced video is enqueued from `CreateViewModel`; the extracted JPEG is cached under a durable `vthumb://{id}` key and the existing `OnMessageMediaRefreshed` event re-binds the bubble. Editor + Android fall back to the server thumbnail.

**Tech Stack:** Unity 6 (C#, coroutines, NUnit EditMode tests), Objective-C++ / AVFoundation (iOS), `MediaCacheManager` disk cache, DOTween (already wired). No namespaces, no asmdefs.

---

## Execution notes (Unity-specific — read before starting)

- **You are NOT the test/compile runner.** Ayan compiles and runs EditMode tests in the open Unity Editor (Test Runner → Run All). Every "verify" step below is a checkpoint: hand off, wait for explicit **GREEN / console-clean** before marking the step done. Never claim a compile or test result on your own authority.
- **No git worktrees.** Work in the live working directory.
- **Only Task 1 is EditMode-testable.** Tasks 2–6 are verified by compile-clean (Editor) + the on-device pass in Task 7 (native extraction + UI are iOS-device-only; in the Editor the extractor returns `onError`).
- **Commits are per-task and need Ayan's explicit go-ahead each time.** Stage only the task's named `.cs`/`.mm` files **plus their Unity-generated `.meta`** — never `git add -A` (the tree carries unrelated uncommitted changes). New files only get a `.meta` after the Editor imports them, so focus the Editor first.
- Spec: `docs/superpowers/specs/2026-06-02-incoming-video-thumbnails-design.md`.

## File structure

**New:**
- `Assets/Scripts/Chat/VideoThumbQueue.cs` — pure dedup + concurrency bookkeeping (no UnityEngine deps; unit-testable).
- `Assets/Tests/Editor/Chat/VideoThumbQueueTests.cs` — EditMode tests for the queue.
- `Assets/Plugins/iOS/VideoThumbnailExtractor.mm` — native AVFoundation frame extractor (job/poll).
- `Assets/Scripts/Chat/VideoThumbnailExtractor.cs` — coroutine bridge (iOS native; Editor/Android `onError`).
- `Assets/Scripts/Main/ChatManager.VideoThumbs.cs` — orchestration partial (queue, enqueue, extraction coroutine, teardown).

**Edit:**
- `Assets/Scripts/Main/ChatManager.cs` — enqueue from `CreateViewModel`.
- `Assets/Scripts/Main/ChatManager.BotState.cs` — clear queue in `SetActiveBot`.
- `Assets/Scripts/UI/MessageItemView.cs` — accept `vthumb://` in the thumbnail scheme check.

---

## Task 1: VideoThumbQueue (pure C#, TDD)

**Files:**
- Create: `Assets/Scripts/Chat/VideoThumbQueue.cs`
- Test: `Assets/Tests/Editor/Chat/VideoThumbQueueTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/Tests/Editor/Chat/VideoThumbQueueTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

public class VideoThumbQueueTests
{
    [Test]
    public void TryEnqueue_NewId_ReturnsTrue()
    {
        var q = new VideoThumbQueue(2);
        Assert.IsTrue(q.TryEnqueue("a"));
    }

    [Test]
    public void TryEnqueue_DuplicateId_ReturnsFalse()
    {
        var q = new VideoThumbQueue(2);
        q.TryEnqueue("a");
        Assert.IsFalse(q.TryEnqueue("a"));
    }

    [Test]
    public void TryEnqueue_NullOrEmpty_ReturnsFalse()
    {
        var q = new VideoThumbQueue(2);
        Assert.IsFalse(q.TryEnqueue(null));
        Assert.IsFalse(q.TryEnqueue(""));
    }

    [Test]
    public void Dispatch_RespectsMaxConcurrent()
    {
        var q = new VideoThumbQueue(2);
        q.TryEnqueue("a"); q.TryEnqueue("b"); q.TryEnqueue("c");
        List<string> first = q.Dispatch();
        Assert.AreEqual(2, first.Count);
        Assert.AreEqual(2, q.InFlightCount);
        Assert.AreEqual(1, q.PendingCount);
        List<string> second = q.Dispatch();   // cap reached
        Assert.AreEqual(0, second.Count);
    }

    [Test]
    public void Complete_FreesSlot_AllowsNextDispatch()
    {
        var q = new VideoThumbQueue(2);
        q.TryEnqueue("a"); q.TryEnqueue("b"); q.TryEnqueue("c");
        List<string> first = q.Dispatch();     // a, b
        q.Complete(first[0]);                  // free one slot
        List<string> next = q.Dispatch();      // c
        Assert.AreEqual(1, next.Count);
        Assert.AreEqual("c", next[0]);
    }

    [Test]
    public void Clear_ResetsState_AndAllowsReEnqueue()
    {
        var q = new VideoThumbQueue(2);
        q.TryEnqueue("a");
        q.Dispatch();
        q.Clear();
        Assert.AreEqual(0, q.InFlightCount);
        Assert.AreEqual(0, q.PendingCount);
        Assert.IsTrue(q.TryEnqueue("a"));      // enqueueable again after Clear
    }
}
```

- [ ] **Step 2: Have Ayan run the tests — confirm they FAIL**

Ask Ayan: focus the Editor, let it import, then Test Runner → EditMode → Run All. Expected: the `VideoThumbQueueTests` FAIL to compile / are red ("VideoThumbQueue not found"). Wait for confirmation.

- [ ] **Step 3: Implement VideoThumbQueue**

Create `Assets/Scripts/Chat/VideoThumbQueue.cs`:

```csharp
using System.Collections.Generic;

/// <summary>
/// Pure dedup + concurrency bookkeeping for incoming-video thumbnail extraction.
/// No UnityEngine dependency, so it is EditMode-unit-testable without the runtime.
/// The driver (ChatManager.VideoThumbs) owns the coroutines and the durable
/// cache-file check; this class only tracks which ids are queued / in-flight / done
/// and how many may run at once. Durable cross-session de-dup is the cache file,
/// not the in-memory 'known' set (which Clear() wipes on bot switch).
/// </summary>
public class VideoThumbQueue
{
    private readonly int maxConcurrent;
    private readonly Queue<string> pending = new();
    private readonly HashSet<string> known = new();      // queued OR in-flight OR completed this session
    private readonly HashSet<string> inFlight = new();

    public VideoThumbQueue(int maxConcurrent = 2)
    {
        this.maxConcurrent = maxConcurrent < 1 ? 1 : maxConcurrent;
    }

    public int InFlightCount => inFlight.Count;
    public int PendingCount => pending.Count;

    /// <summary>Queues an id unless already known (queued/in-flight/completed). Returns true if newly queued.</summary>
    public bool TryEnqueue(string messageId)
    {
        if (string.IsNullOrEmpty(messageId)) return false;
        if (!known.Add(messageId)) return false;
        pending.Enqueue(messageId);
        return true;
    }

    /// <summary>Returns up to (maxConcurrent - inFlight) ids to start now, moving them to in-flight.</summary>
    public List<string> Dispatch()
    {
        var started = new List<string>();
        while (inFlight.Count < maxConcurrent && pending.Count > 0)
        {
            string id = pending.Dequeue();
            inFlight.Add(id);
            started.Add(id);
        }
        return started;
    }

    /// <summary>Marks an in-flight id finished (success or fail), freeing a slot. Stays 'known' for the session.</summary>
    public void Complete(string messageId)
    {
        inFlight.Remove(messageId);
    }

    /// <summary>Drops all state (bot switch / chat close). The durable de-dup is the cache file.</summary>
    public void Clear()
    {
        pending.Clear();
        known.Clear();
        inFlight.Clear();
    }
}
```

- [ ] **Step 4: Have Ayan run the tests — confirm they PASS**

Ask Ayan: Test Runner → Run All. Expected: all six `VideoThumbQueueTests` GREEN, console clean. Wait for confirmation.

- [ ] **Step 5: Commit (with Ayan's go-ahead)**

After the Editor has generated the `.meta` files:

```bash
git add Assets/Scripts/Chat/VideoThumbQueue.cs Assets/Scripts/Chat/VideoThumbQueue.cs.meta \
        Assets/Tests/Editor/Chat/VideoThumbQueueTests.cs Assets/Tests/Editor/Chat/VideoThumbQueueTests.cs.meta
git commit -m "feat(chat): add VideoThumbQueue concurrency bookkeeping for video thumbs"
```

---

## Task 2: iOS native frame extractor

**Files:**
- Create: `Assets/Plugins/iOS/VideoThumbnailExtractor.mm`

No automated test — native, device-only. Verified together with the bridge in Task 7. Mirrors `Assets/Plugins/iOS/VideoConverter.mm`'s job/poll lifecycle.

- [ ] **Step 1: Write the native extractor**

Create `Assets/Plugins/iOS/VideoThumbnailExtractor.mm`:

```objc
#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>
#import <UIKit/UIKit.h>

// Job-based async thumbnail extraction from a (remote) video URL, polled from a
// Unity coroutine. AVURLAsset over HTTPS fetches only the byte ranges needed for the
// moov + the keyframe near the requested time — no full download. Mirrors
// VideoConverter.mm's job/poll lifecycle and error-string handling.

typedef NS_ENUM(NSInteger, ThumbJobStatus) {
    ThumbJobRunning = 0,
    ThumbJobDone    = 1,
    ThumbJobFailed  = 2
};

@interface ThumbJob : NSObject
@property (nonatomic) ThumbJobStatus status;
@property (nonatomic, strong) NSString *error;
@property (nonatomic, strong) AVAssetImageGenerator *generator; // retained until generation completes
@end
@implementation ThumbJob
@end

static NSMutableDictionary<NSNumber *, ThumbJob *> *gThumbJobs = nil;
static int gThumbNextId = 1;

static NSMutableDictionary<NSNumber *, ThumbJob *> *ThumbJobs(void) {
    if (gThumbJobs == nil) gThumbJobs = [NSMutableDictionary dictionary];
    return gThumbJobs;
}

extern "C" int _StartThumbExtract(const char *cUrl, const char *cOutPath, double timeSec) {
    @autoreleasepool {
        NSString *urlStr  = cUrl     ? [NSString stringWithUTF8String:cUrl]     : @"";
        NSString *outPath = cOutPath ? [NSString stringWithUTF8String:cOutPath] : @"";

        int jobId = gThumbNextId++;
        ThumbJob *job = [ThumbJob new];
        job.status = ThumbJobRunning;
        ThumbJobs()[@(jobId)] = job;

        NSURL *url = [NSURL URLWithString:urlStr];
        if (url == nil || outPath.length == 0) {
            job.status = ThumbJobFailed;
            job.error  = @"invalid URL or output path";
            return jobId;
        }

        AVURLAsset *asset = [AVURLAsset URLAssetWithURL:url options:nil];
        AVAssetImageGenerator *gen = [[AVAssetImageGenerator alloc] initWithAsset:asset];
        gen.appliesPreferredTrackTransform = YES;                 // correct rotation
        gen.maximumSize = CGSizeMake(640, 640);                   // long-edge cap for a thumbnail
        gen.requestedTimeToleranceBefore = kCMTimeZero;
        gen.requestedTimeToleranceAfter  = CMTimeMakeWithSeconds(1.0, 600); // snap to a nearby keyframe
        job.generator = gen;

        [asset loadValuesAsynchronouslyForKeys:@[@"duration"] completionHandler:^{
            NSError *durErr = nil;
            if ([asset statusOfValueForKey:@"duration" error:&durErr] != AVKeyValueStatusLoaded) {
                job.status = ThumbJobFailed;
                job.error  = durErr ? durErr.localizedDescription : @"asset load failed";
                return;
            }

            Float64 durationSec = CMTimeGetSeconds(asset.duration);
            Float64 t = timeSec;
            if (durationSec > 0 && t > durationSec) t = durationSec * 0.5;
            if (t < 0) t = 0;
            CMTime requested = CMTimeMakeWithSeconds(t, 600);

            [gen generateCGImagesAsynchronouslyForTimes:@[[NSValue valueWithCMTime:requested]]
                completionHandler:^(CMTime requestedTime, CGImageRef image, CMTime actualTime,
                                    AVAssetImageGeneratorResult result, NSError *error) {
                    if (result != AVAssetImageGeneratorSucceeded || image == NULL) {
                        job.status = ThumbJobFailed;
                        job.error  = error ? error.localizedDescription : @"frame generation failed";
                        return;
                    }
                    UIImage *ui  = [UIImage imageWithCGImage:image];
                    NSData  *jpg = UIImageJPEGRepresentation(ui, 0.9);
                    BOOL ok = jpg != nil && [jpg writeToFile:outPath atomically:YES];
                    if (ok) { job.status = ThumbJobDone; }
                    else    { job.status = ThumbJobFailed; job.error = @"write failed"; }
                }];
        }];

        return jobId;
    }
}

extern "C" int _PollThumbExtract(int jobId) {
    ThumbJob *job = ThumbJobs()[@(jobId)];
    if (job == nil) return ThumbJobFailed;
    return (int)job.status;
}

extern "C" const char *_ThumbExtractError(int jobId) {
    ThumbJob *job = ThumbJobs()[@(jobId)];
    if (job == nil || job.error == nil) return "";
    return [job.error UTF8String];   // valid until the job is freed (read immediately by C#)
}

extern "C" void _FreeThumbExtractJob(int jobId) {
    [ThumbJobs() removeObjectForKey:@(jobId)];
}
```

- [ ] **Step 2: Have Ayan confirm the Editor imports it without error**

Ask Ayan: focus the Editor; the `.mm` is iOS-only so it won't compile in the Editor, but Unity must import it (generate `.meta`) with no importer errors in the console. Wait for confirmation. (Functional verification is on device, Task 7.)

- [ ] **Step 3: Commit (with Ayan's go-ahead)**

```bash
git add Assets/Plugins/iOS/VideoThumbnailExtractor.mm Assets/Plugins/iOS/VideoThumbnailExtractor.mm.meta
git commit -m "feat(ios): native AVAssetImageGenerator thumbnail extractor for remote videos"
```

---

## Task 3: C# coroutine bridge

**Files:**
- Create: `Assets/Scripts/Chat/VideoThumbnailExtractor.cs`

- [ ] **Step 1: Write the bridge**

Create `Assets/Scripts/Chat/VideoThumbnailExtractor.cs`:

```csharp
using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Extracts a single thumbnail frame from a (remote) video URL into outputPath.
/// On iOS this drives the native AVAssetImageGenerator job (VideoThumbnailExtractor.mm)
/// and polls it from a coroutine. In the Editor and on Android there is no native
/// extractor, so it reports onError and the caller keeps the server JPEGThumbnail.
/// Mirrors VideoConverter. Never throws.
/// </summary>
public static class VideoThumbnailExtractor
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int    _StartThumbExtract(string url, string outPath, double timeSec);
    [DllImport("__Internal")] private static extern int    _PollThumbExtract(int jobId);   // 0 run, 1 done, 2 fail
    [DllImport("__Internal")] private static extern IntPtr _ThumbExtractError(int jobId);
    [DllImport("__Internal")] private static extern void   _FreeThumbExtractJob(int jobId);
#endif

    /// <summary>
    /// Yields until extraction finishes. Invokes onResult(outputPath) on success, or
    /// onError(message) on failure or on a platform without a native extractor.
    /// </summary>
    public static IEnumerator Extract(string url, string outputPath, double timeSec,
                                      Action<string> onResult, Action<string> onError)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(outputPath))
        {
            onError?.Invoke("empty url or output path");
            yield break;
        }
#if UNITY_IOS && !UNITY_EDITOR
        int jobId = _StartThumbExtract(url, outputPath, timeSec);
        int status = _PollThumbExtract(jobId);
        while (status == 0)
        {
            yield return null;
            status = _PollThumbExtract(jobId);
        }

        if (status == 2)
        {
            string message = Marshal.PtrToStringAnsi(_ThumbExtractError(jobId)) ?? "thumbnail extraction failed";
            _FreeThumbExtractJob(jobId);
            onError?.Invoke(message);
            yield break;
        }

        _FreeThumbExtractJob(jobId);
        onResult?.Invoke(outputPath);
#else
        // Editor + Android: no native extractor — caller keeps the server thumbnail.
        onError?.Invoke("no native thumbnail extractor on this platform");
        yield break;
#endif
    }
}
```

- [ ] **Step 2: Have Ayan confirm compile-clean**

Ask Ayan: focus the Editor, let it recompile, confirm console is clean (the `#else` branch is what compiles in-Editor). Wait for confirmation.

- [ ] **Step 3: Commit (with Ayan's go-ahead)**

```bash
git add Assets/Scripts/Chat/VideoThumbnailExtractor.cs Assets/Scripts/Chat/VideoThumbnailExtractor.cs.meta
git commit -m "feat(chat): coroutine bridge for native video thumbnail extraction"
```

---

## Task 4: ChatManager orchestration partial

**Files:**
- Create: `Assets/Scripts/Main/ChatManager.VideoThumbs.cs`

Depends on Tasks 1 + 3. References existing members: `GetCacheRoot()`, `OnMessageMediaRefreshed`, `MediaCacheManager`, `ChatHistoryCache`, `MessageType.Video`, `MessageViewModel`.

- [ ] **Step 1: Write the partial**

Create `Assets/Scripts/Main/ChatManager.VideoThumbs.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Incoming-video thumbnail extraction, split out of ChatManager (mirrors
/// ChatManager.MediaSend.cs / .BotState.cs). For every server-sourced video, natively
/// extracts a frame from its remote URL and caches it under "vthumb://{id}", replacing
/// Wappi's server JPEGThumbnail so previews never depend on the server providing one.
/// iOS does the extraction; Editor + Android fall back to the server thumb (see
/// VideoThumbnailExtractor). Extraction coroutines run on 'this', so SetActiveBot's
/// StopAllCoroutines cancels in-flight work on a bot switch; ClearVideoThumbQueue then
/// resets the bookkeeping.
/// </summary>
public partial class ChatManager
{
    private const int    VideoThumbMaxConcurrent = 2;
    private const double VideoThumbTimeSec       = 1.0;   // grab ~1s in (clamped to duration native-side)

    private VideoThumbQueue _videoThumbQueue;
    // Live VM instance per queued id, so completion can mutate it + fire OnMessageMediaRefreshed.
    private readonly Dictionary<string, MessageViewModel> _pendingThumbVms = new();

    /// <summary>
    /// Queues an incoming video for native thumbnail extraction. No-op for non-video,
    /// urlless, or already-extracted messages. Durable de-dup is the on-disk vthumb file.
    /// Called from CreateViewModel for every server-sourced message.
    /// </summary>
    public void EnqueueIncomingVideoThumb(MessageViewModel vm)
    {
        if (vm == null || vm.type != MessageType.Video) return;
        if (string.IsNullOrEmpty(vm.videoUrl) || string.IsNullOrEmpty(vm.messageId)) return;
        if (MediaCacheManager.Instance == null) return;

        string vthumbUrl = "vthumb://" + vm.messageId;

        // Durable de-dup: our frame was extracted in a prior session and persists on disk.
        if (MediaCacheManager.Instance.IsImageCached(vthumbUrl))
        {
            if (vm.thumbnailUrl != vthumbUrl)
            {
                vm.thumbnailUrl = vthumbUrl;
                OnMessageMediaRefreshed?.Invoke(vm);
            }
            return;
        }

        _videoThumbQueue ??= new VideoThumbQueue(VideoThumbMaxConcurrent);
        if (_videoThumbQueue.TryEnqueue(vm.messageId))
        {
            _pendingThumbVms[vm.messageId] = vm;
            PumpVideoThumbQueue();
        }
    }

    private void PumpVideoThumbQueue()
    {
        if (_videoThumbQueue == null) return;
        foreach (string id in _videoThumbQueue.Dispatch())
            StartCoroutine(RunVideoThumbExtraction(id));
    }

    private IEnumerator RunVideoThumbExtraction(string messageId)
    {
        if (!_pendingThumbVms.TryGetValue(messageId, out MessageViewModel vm) || vm == null)
        {
            _videoThumbQueue.Complete(messageId);
            PumpVideoThumbQueue();
            yield break;
        }

        string cacheRoot = GetCacheRoot();
        string vthumbUrl = "vthumb://" + messageId;
        // Compute (and ensure the media dir exists) before the native atomic write.
        string finalPath = MediaCacheManager.Instance.GetFilePathFromUrl(vthumbUrl);

        bool ok = false;
        yield return VideoThumbnailExtractor.Extract(
            vm.videoUrl, finalPath, VideoThumbTimeSec,
            _   => ok = true,
            err => Debug.LogWarning($"[ChatManager] video thumb extract failed for {messageId}: {err}"));

        if (ok && System.IO.File.Exists(finalPath))
        {
            vm.thumbnailUrl = vthumbUrl;
            UpdateCachedThumbnailUrl(cacheRoot, vm.chatId, messageId, vthumbUrl);
            OnMessageMediaRefreshed?.Invoke(vm);
        }

        _pendingThumbVms.Remove(messageId);
        _videoThumbQueue.Complete(messageId);
        PumpVideoThumbQueue();
    }

    private void UpdateCachedThumbnailUrl(string cacheRoot, string chatId, string messageId, string thumbnailUrl)
    {
        if (string.IsNullOrEmpty(chatId)) return;
        List<MessageViewModel> cached = ChatHistoryCache.LoadHistory(cacheRoot, chatId);
        for (int i = 0; i < cached.Count; i++)
        {
            if (cached[i].messageId == messageId)
            {
                cached[i].thumbnailUrl = thumbnailUrl;
                ChatHistoryCache.SaveHistory(cacheRoot, chatId, cached);
                return;
            }
        }
    }

    /// <summary>Resets all queue state. Called from SetActiveBot after StopAllCoroutines
    /// (which already cancels in-flight extraction coroutines running on this).</summary>
    public void ClearVideoThumbQueue()
    {
        _videoThumbQueue?.Clear();
        _pendingThumbVms.Clear();
    }
}
```

- [ ] **Step 2: Have Ayan confirm compile-clean**

Ask Ayan: focus the Editor, recompile, confirm console is clean (no missing-member errors against `GetCacheRoot`, `OnMessageMediaRefreshed`, `ChatHistoryCache`). Wait for confirmation.

- [ ] **Step 3: Commit (with Ayan's go-ahead)**

```bash
git add Assets/Scripts/Main/ChatManager.VideoThumbs.cs Assets/Scripts/Main/ChatManager.VideoThumbs.cs.meta
git commit -m "feat(chat): orchestrate incoming-video thumbnail extraction queue"
```

---

## Task 5: Wire enqueue + teardown

**Files:**
- Modify: `Assets/Scripts/Main/ChatManager.cs` (`CreateViewModel`, ~993-1017)
- Modify: `Assets/Scripts/Main/ChatManager.BotState.cs` (`SetActiveBot`, ~121)

- [ ] **Step 1: Enqueue from CreateViewModel**

In `Assets/Scripts/Main/ChatManager.cs`, change `CreateViewModel` to capture the VM and enqueue videos. Replace:

```csharp
    MessageViewModel CreateViewModel(NormalizedMessage msg)
    {
        return new MessageViewModel
        {
            messageId = msg.id,
            chatId = msg.chatId,
            senderName = msg.senderName,
            type = msg.messageType,
            text = msg.text,
            fileName = msg.fileName, // <--- Add this line!
            mediaUrl = msg.mediaUrl,
            thumbnailUrl = msg.thumbnailUrl,
            aspectRatio = msg.aspectRatio,
            expireTime = msg.expireTime,
            mimeType = msg.mimeType,
            videoUrl = msg.videoUrl,
            duration = msg.duration,
            isSticker = msg.isSticker,
            isIncoming = !msg.fromMe,
            timestamp = msg.time,
            fileSize = msg.fileSize,
            pageCount = msg.pageCount,
            deliveryStatus = msg.deliveryStatus
        };
    }
```

with:

```csharp
    MessageViewModel CreateViewModel(NormalizedMessage msg)
    {
        var vm = new MessageViewModel
        {
            messageId = msg.id,
            chatId = msg.chatId,
            senderName = msg.senderName,
            type = msg.messageType,
            text = msg.text,
            fileName = msg.fileName, // <--- Add this line!
            mediaUrl = msg.mediaUrl,
            thumbnailUrl = msg.thumbnailUrl,
            aspectRatio = msg.aspectRatio,
            expireTime = msg.expireTime,
            mimeType = msg.mimeType,
            videoUrl = msg.videoUrl,
            duration = msg.duration,
            isSticker = msg.isSticker,
            isIncoming = !msg.fromMe,
            timestamp = msg.time,
            fileSize = msg.fileSize,
            pageCount = msg.pageCount,
            deliveryStatus = msg.deliveryStatus
        };

        // Proactively extract a native thumbnail for videos (replaces the server
        // JPEGThumbnail when ready). No-op off-iOS / already-cached / urlless.
        if (vm.type == MessageType.Video) EnqueueIncomingVideoThumb(vm);

        return vm;
    }
```

- [ ] **Step 2: Clear the queue on bot switch**

In `Assets/Scripts/Main/ChatManager.BotState.cs`, in `SetActiveBot`, add `ClearVideoThumbQueue();` immediately after `StopAllCoroutines();`. Replace:

```csharp
        StopAllCoroutines();
        BeginLoadForActiveBot();
```

with:

```csharp
        StopAllCoroutines();      // also cancels in-flight thumbnail extraction coroutines
        ClearVideoThumbQueue();   // reset queue bookkeeping the cancelled coroutines never freed
        BeginLoadForActiveBot();
```

- [ ] **Step 3: Have Ayan confirm compile-clean**

Ask Ayan: recompile, confirm console clean. Wait for confirmation.

- [ ] **Step 4: Commit (with Ayan's go-ahead)**

```bash
git add Assets/Scripts/Main/ChatManager.cs Assets/Scripts/Main/ChatManager.BotState.cs
git commit -m "feat(chat): enqueue incoming-video thumbs on parse; clear on bot switch"
```

---

## Task 6: Render the extracted frame (vthumb:// scheme)

**Files:**
- Modify: `Assets/Scripts/UI/MessageItemView.cs` (`ShowSmartThumbnail`, ~1834)

The extracted frame is cached under `vthumb://{id}`. `ShowSmartThumbnail` only recognizes `thumb://`, so re-bind (via `OnMessageMediaRefreshed` → `HandleMediaRefreshed`) would otherwise fall through to the dark blank card. Broaden the check; both schemes resolve through `MediaCacheManager.LoadImageFromCache`.

- [ ] **Step 1: Broaden the scheme check**

In `Assets/Scripts/UI/MessageItemView.cs`, replace:

```csharp
            if (vm.thumbnailUrl.StartsWith("thumb://"))
            {
                Texture2D cachedTex = MediaCacheManager.Instance.LoadImageFromCache(vm.thumbnailUrl);
                if (cachedTex != null) 
                {
                    ApplyTextureAspectFill(cachedTex, false, bubbleRatio);
                    imageLoaded = true;
                }
            }
```

with:

```csharp
            if (vm.thumbnailUrl.StartsWith("thumb://") || vm.thumbnailUrl.StartsWith("vthumb://"))
            {
                // thumb://  = server JPEGThumbnail (or staged); vthumb:// = our native frame.
                // Both are MediaCacheManager keys, so the same cache load handles either.
                Texture2D cachedTex = MediaCacheManager.Instance.LoadImageFromCache(vm.thumbnailUrl);
                if (cachedTex != null) 
                {
                    ApplyTextureAspectFill(cachedTex, false, bubbleRatio);
                    imageLoaded = true;
                }
            }
```

- [ ] **Step 2: Have Ayan confirm compile-clean**

Ask Ayan: recompile, confirm console clean. Wait for confirmation.

- [ ] **Step 3: Commit (with Ayan's go-ahead)**

```bash
git add Assets/Scripts/UI/MessageItemView.cs
git commit -m "feat(chat): render vthumb:// native video thumbnails in message bubbles"
```

---

## Task 7: On-device verification (iOS) + Android fallback

No code. This is the integration acceptance pass — Ayan runs it on devices.

- [ ] **Step 1: iOS device build + functional checks**

Ask Ayan to build to an iOS device and verify:
1. Open a chat with an incoming video **that has** a server thumbnail → bubble shows the server thumb instantly, then swaps to the sharper native frame.
2. Incoming video **without** a server thumbnail (or one Wappi historically omitted) → bubble shows the native frame shortly after open (was blank/dark before).
3. A video-heavy chat → thumbnails fill in newest-first, two at a time; scrolling stays smooth, no frame hitching.
4. Reopen the same chat → thumbnails appear instantly from cache, and the console shows **no** repeated extraction for already-done videos.
5. A portrait video renders upright (rotation transform applied), not sideways.
6. Console is free of `video thumb extract failed` spam for healthy URLs (occasional failures on expired links are expected → server thumb stays).

- [ ] **Step 2: Android fallback check**

Ask Ayan to run on an Android device (or build): incoming video bubbles fall back to the server `JPEGThumbnail`; no crash, no native-call errors in logcat.

- [ ] **Step 3: Done**

When both pass, the feature is verified. (Android native `MediaMetadataRetriever` extraction remains a future drop-in behind `VideoThumbnailExtractor.Extract`, per the spec.)

---

## Notes on deferred scope (from spec)

- **Expired-URL retry:** v1 falls back to the server thumb when the s3 URL is past `expireTime`. A v2 could refresh the link via `DownloadMediaForMessage` and retry once.
- **Failed-extraction retry:** a failure stays "known" for the session; it is retried after a chat reopen (sync re-parses → `CreateViewModel` re-enqueues once the queue was cleared).
- **Android native extractor:** drop-in behind the existing `VideoThumbnailExtractor.Extract` `#else` seam.

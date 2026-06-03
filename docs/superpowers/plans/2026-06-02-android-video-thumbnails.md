# Android Video Thumbnails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Android its own natively-extracted video thumbnails (parity with iOS), instead of falling back to Wappi's server `JPEGThumbnail`.

**Architecture:** A new Android Java plugin (`MediaMetadataRetriever`, background thread, job/poll API) mirrors the iOS `.mm`. The existing `VideoThumbnailExtractor.cs` bridge gains a `#elif UNITY_ANDROID` branch that drives it with the same poll loop as iOS. Everything downstream (`VideoThumbQueue`, `ChatManager.VideoThumbs`, `vthumb://` cache, `OnMessageMediaRefreshed` render) is already platform-agnostic — Android just starts succeeding instead of hitting `onError`.

**Tech Stack:** Android `MediaMetadataRetriever` + `Bitmap` (framework, no new gradle deps), Unity `AndroidJavaClass` interop, C# coroutines.

---

## Execution notes (read before starting)

- **Continues on the existing branch** `feat/incoming-video-thumbnails` (the iOS work + docs already live there). Per-task commits to that branch with your go-ahead.
- **You are the compile/device runner.** I can't run Unity headless or build Android. Each "verify" step hands off to you; wait for your explicit confirmation before the commit.
- **No EditMode tests here** — the shared `VideoThumbQueue` is already covered (iOS plan, Task 1) and the Java path is device-only. Verification is: bridge compiles clean (Editor), Java imports clean, and the Android device pass in Task 3.
- **No worktrees.** Stage only each task's named file(s) **plus their Unity-generated `.meta`** — never `git add -A`.

## File structure

**New:**
- `Assets/Plugins/Android/VideoThumbnailExtractor.java` — native `MediaMetadataRetriever` frame extractor, job/poll API on a background thread.

**Edit:**
- `Assets/Scripts/Chat/VideoThumbnailExtractor.cs` — add a `#elif UNITY_ANDROID && !UNITY_EDITOR` poll branch (cached `AndroidJavaClass`).

---

## Task 1: Android native frame extractor

**Files:**
- Create: `Assets/Plugins/Android/VideoThumbnailExtractor.java`

No automated test — native, device-only (verified in Task 3). Job/poll lifecycle mirrors `Assets/Plugins/iOS/VideoThumbnailExtractor.mm`. Uses only framework classes (`android.media.MediaMetadataRetriever`, `android.graphics.Bitmap`), so no `mainTemplate.gradle` change. Throttling is already handled C#-side (`VideoThumbQueue`, max 2 concurrent), so at most 2 background threads run at once.

- [ ] **Step 1: Write the Java plugin**

Create `Assets/Plugins/Android/VideoThumbnailExtractor.java`:

```java
package com.unity.video;

import android.graphics.Bitmap;
import android.media.MediaMetadataRetriever;

import java.io.FileOutputStream;
import java.util.HashMap;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;

// Job-based async thumbnail extraction from a (remote) video URL, polled from a
// Unity coroutine. Mirrors VideoThumbnailExtractor.mm's job/poll lifecycle so the
// C# bridge drives both platforms with the same loop. MediaMetadataRetriever does
// blocking network I/O, so each job runs on its own background thread.
public class VideoThumbnailExtractor {

    private static final int RUNNING = 0;
    private static final int DONE = 1;
    private static final int FAILED = 2;
    private static final int MAX_EDGE = 640; // long-edge cap for a thumbnail

    private static final class Job {
        volatile int status = RUNNING;
        volatile String error = "";
    }

    private static final ConcurrentHashMap<Integer, Job> jobs = new ConcurrentHashMap<>();
    private static final AtomicInteger nextId = new AtomicInteger(1);

    public static int startThumbExtract(final String url, final String outPath, final double timeSec) {
        final int jobId = nextId.getAndIncrement();
        final Job job = new Job();
        jobs.put(jobId, job);

        new Thread(new Runnable() {
            @Override
            public void run() {
                MediaMetadataRetriever retriever = new MediaMetadataRetriever();
                try {
                    retriever.setDataSource(url, new HashMap<String, String>());

                    long timeUs = (long) (timeSec * 1_000_000L);
                    Bitmap frame = retriever.getFrameAtTime(timeUs, MediaMetadataRetriever.OPTION_CLOSEST_SYNC);
                    if (frame == null) {
                        fail(job, "no frame at requested time");
                        return;
                    }

                    Bitmap scaled = scaleDown(frame);
                    FileOutputStream out = new FileOutputStream(outPath);
                    try {
                        scaled.compress(Bitmap.CompressFormat.JPEG, 90, out);
                        out.flush();
                    } finally {
                        out.close();
                    }
                    if (scaled != frame) scaled.recycle();
                    frame.recycle();

                    job.status = DONE;
                } catch (Throwable t) {
                    fail(job, t.getMessage() != null ? t.getMessage() : t.toString());
                } finally {
                    try { retriever.release(); } catch (Exception ignored) {}
                }
            }
        }).start();

        return jobId;
    }

    private static Bitmap scaleDown(Bitmap src) {
        int w = src.getWidth();
        int h = src.getHeight();
        int longest = Math.max(w, h);
        if (longest <= MAX_EDGE) return src;
        float ratio = (float) MAX_EDGE / (float) longest;
        int nw = Math.max(1, Math.round(w * ratio));
        int nh = Math.max(1, Math.round(h * ratio));
        return Bitmap.createScaledBitmap(src, nw, nh, true);
    }

    private static void fail(Job job, String message) {
        job.error = (message != null) ? message : "thumbnail extraction failed";
        job.status = FAILED;
    }

    public static int pollThumbExtract(int jobId) {
        Job job = jobs.get(jobId);
        return (job == null) ? FAILED : job.status;
    }

    public static String thumbExtractError(int jobId) {
        Job job = jobs.get(jobId);
        return (job == null) ? "unknown job" : job.error;
    }

    public static void freeThumbExtractJob(int jobId) {
        jobs.remove(jobId);
    }
}
```

- [ ] **Step 2: Have Ayan confirm the Editor imports it cleanly**

Ask Ayan: focus the Editor; the `.java` is Android-only (folder convention) so it won't compile in the Editor, but Unity must import it with no importer/console errors. Wait for confirmation. (Functional verification is on device, Task 3.)

- [ ] **Step 3: Commit (with Ayan's go-ahead)**

```bash
git add Assets/Plugins/Android/VideoThumbnailExtractor.java Assets/Plugins/Android/VideoThumbnailExtractor.java.meta
git commit -m "feat(android): native MediaMetadataRetriever thumbnail extractor for remote videos"
```

---

## Task 2: Android branch in the C# bridge

**Files:**
- Modify: `Assets/Scripts/Chat/VideoThumbnailExtractor.cs`

Two edits: add a cached `AndroidJavaClass` in the platform block, and an Android poll branch in `Extract` that matches the iOS loop exactly.

- [ ] **Step 1: Add the cached AndroidJavaClass to the platform block**

In `Assets/Scripts/Chat/VideoThumbnailExtractor.cs`, replace:

```csharp
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int    _StartThumbExtract(string url, string outPath, double timeSec);
    [DllImport("__Internal")] private static extern int    _PollThumbExtract(int jobId);   // 0 run, 1 done, 2 fail
    [DllImport("__Internal")] private static extern IntPtr _ThumbExtractError(int jobId);
    [DllImport("__Internal")] private static extern void   _FreeThumbExtractJob(int jobId);
#endif
```

with:

```csharp
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int    _StartThumbExtract(string url, string outPath, double timeSec);
    [DllImport("__Internal")] private static extern int    _PollThumbExtract(int jobId);   // 0 run, 1 done, 2 fail
    [DllImport("__Internal")] private static extern IntPtr _ThumbExtractError(int jobId);
    [DllImport("__Internal")] private static extern void   _FreeThumbExtractJob(int jobId);
#elif UNITY_ANDROID && !UNITY_EDITOR
    // Cached for the app lifetime (mirrors AndroidBridge.cs). Drives the Java job/poll API.
    private static readonly AndroidJavaClass _android = new AndroidJavaClass("com.unity.video.VideoThumbnailExtractor");
#endif
```

- [ ] **Step 2: Add the Android poll branch in `Extract`**

In the same file, replace:

```csharp
        _FreeThumbExtractJob(jobId);
        onResult?.Invoke(outputPath);
#else
        // Editor + Android: no native extractor — caller keeps the server thumbnail.
        onError?.Invoke("no native thumbnail extractor on this platform");
        yield break;
#endif
```

with:

```csharp
        _FreeThumbExtractJob(jobId);
        onResult?.Invoke(outputPath);
#elif UNITY_ANDROID && !UNITY_EDITOR
        int jobId = _android.CallStatic<int>("startThumbExtract", url, outputPath, timeSec);
        int status = _android.CallStatic<int>("pollThumbExtract", jobId);
        while (status == 0)
        {
            yield return null;
            status = _android.CallStatic<int>("pollThumbExtract", jobId);
        }

        if (status == 2)
        {
            string message = _android.CallStatic<string>("thumbExtractError", jobId) ?? "thumbnail extraction failed";
            _android.CallStatic("freeThumbExtractJob", jobId);
            onError?.Invoke(message);
            yield break;
        }

        _android.CallStatic("freeThumbExtractJob", jobId);
        onResult?.Invoke(outputPath);
#else
        // Editor: no native extractor — caller keeps the server thumbnail.
        onError?.Invoke("no native thumbnail extractor on this platform");
        yield break;
#endif
```

- [ ] **Step 3: Have Ayan confirm compile-clean**

Ask Ayan: recompile in the Editor, confirm console is clean. (The Editor still compiles the `#else` path; the Android branch compiles only when the active build target is Android — Ayan can optionally switch the build target to Android to compile-check that branch, but it isn't required to proceed.) Wait for confirmation.

- [ ] **Step 4: Commit (with Ayan's go-ahead)**

```bash
git add Assets/Scripts/Chat/VideoThumbnailExtractor.cs
git commit -m "feat(android): drive native thumbnail extractor from the bridge poll loop"
```

---

## Task 3: On-device verification (Android)

No code. Ayan runs this on an Android device/build.

- [ ] **Step 1: Android device build + functional checks**

Ask Ayan to build to an Android device and verify:
1. An incoming video **without** a server thumbnail (previously blank on Android) now shows a native frame shortly after the chat opens.
2. Scroll-back through older incoming **and** outgoing videos → thumbnails fill in (two at a time), scrolling stays smooth, **no ANR / no main-thread stall**.
3. Reopen the chat → thumbnails appear instantly from the `vthumb://` cache; no repeated extraction in logcat.
4. **Rotation:** a portrait video renders upright. (If it's sideways, that's the one known `MediaMetadataRetriever` quirk — I'll add `METADATA_KEY_VIDEO_ROTATION` handling in the Java as a fast follow.)
5. A video that fails to decode/stream falls back to the server thumb; logcat shows the `video thumb extract failed` warning but **no crash**.

- [ ] **Step 2: iOS regression glance**

Ask Ayan to confirm iOS still behaves as before (the change is additive/`#elif`-gated, so iOS is untouched — a quick sanity check only).

- [ ] **Step 3: Done**

When Android shows native thumbnails and iOS is unaffected, the addendum is complete.

---

## Notes on deferred scope (from spec)

- **Rotation:** v1 relies on `getFrameAtTime`'s output orientation; if device testing shows sideways portrait frames, add `METADATA_KEY_VIDEO_ROTATION` + `Matrix` rotation in the Java.
- **Codecs `MediaMetadataRetriever` can't stream:** v1 falls back to the server thumb; a download-then-extract fallback is possible later.

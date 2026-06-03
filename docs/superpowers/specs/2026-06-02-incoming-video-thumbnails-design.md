# Incoming Video Thumbnails — Native Frame Extraction (Design)

**Date:** 2026-06-02
**Status:** Approved (brainstorm) — ready for implementation plan
**Related (orthogonal):** `docs/superpowers/specs/2026-05-29-media-ghost-recovery-design.md` — touches a different `ChatManager.Normalize` branch (outgoing-media reconcile), no logic overlap.

## Problem

Incoming video bubbles depend entirely on Wappi's server-provided base64 `JPEGThumbnail`. `ChatManager.Normalize` decodes it and caches it under `thumb://{msg.id}` ([ChatManager.cs:1080-1098](../../../Assets/Scripts/Main/ChatManager.cs)). When the server omits it, `thumbnailUrl` falls back to `""` and the bubble shows no preview until the full video loads. We do not want to rely on Wappi sending a thumbnail.

Unlike staged/outgoing videos — which have a local file and use `NativeGallery.GetVideoThumbnail` — incoming videos are **URL-only**: `VideoController.PlayVideo` streams `videoPlayer.url` directly ([VideoController.cs:69](../../../Assets/Scripts/Chat/VideoController.cs)) and `DownloadMediaForMessage` only refreshes the link ([ChatManager.cs:1332](../../../Assets/Scripts/Main/ChatManager.cs)). The bytes never hit disk, so there is no local file to hand a thumbnailer.

## Goal

Proactively extract a native, correctly-rotated frame from every incoming video's remote URL and use it as the thumbnail, replacing the server thumbnail. Guarantee a preview regardless of whether Wappi provides one.

## Decisions (locked)

| Decision | Choice | Rationale |
|---|---|---|
| Trigger | Proactive on receipt (history batch + live), throttled | Guaranteed-instant preview; user accepts bandwidth cost |
| Server thumb | Always extract our own and replace | User does not want to depend on Wappi quality/presence |
| Mechanism | Custom native (iOS `AVAssetImageGenerator`) | Fetches only moov + keyframe — most bandwidth-efficient; native frame quality + rotation |
| Platform | iOS (`AVAssetImageGenerator`) + Android (`MediaMetadataRetriever`) native; Editor falls back to server thumb | Native frames on both platforms behind one `VideoThumbnailExtractor.Extract` seam (Android added as an addendum — see end) |
| Placeholder | Server `JPEGThumbnail` shown transiently, then overwritten by native frame | Strictly better UX, free (already decoded) |

## Architecture

Three new files + three edits. All native async work follows `VideoConverter`'s proven job/poll-from-coroutine pattern ([VideoConverter.cs](../../../Assets/Scripts/Chat/VideoConverter.cs)).

### New — `Assets/Plugins/iOS/VideoThumbnailExtractor.mm`

C job API mirroring `VideoConverter.mm`:

```
int         _StartThumbExtract(const char* url, const char* outPath, double timeSec); // returns jobId
int         _PollThumbExtract(int jobId);   // 0 running, 1 done, 2 fail
const char* _ThumbExtractError(int jobId);
void        _FreeThumbExtractJob(int jobId);
```

Implementation: `AVURLAsset(URL)` → `AVAssetImageGenerator` with `appliesPreferredTrackTransform = YES` (correct rotation) and a `maximumSize` cap (e.g. 640px long edge) → `generateCGImagesAsynchronouslyForTimes:` at `CMTimeMakeWithSeconds(timeSec)`. On success write JPEG (q≈0.9) to `outPath` (matches the `.jpg` cache naming; `Texture2D.LoadImage` content-sniffs on read) and set status `done`; on failure set status `fail` + message. Jobs held in an `NSMutableDictionary` keyed by an incrementing id. `timeSec` is clamped to the asset duration native-side. `AVURLAsset` over HTTPS fetches only the byte ranges needed — no full download.

### New — `Assets/Scripts/Chat/VideoThumbnailExtractor.cs`

Static class mirroring `VideoConverter`:

```csharp
public static IEnumerator Extract(string url, string outputPath, double timeSec,
                                  Action<string> onResult, Action<string> onError)
```

- **iOS:** start job, poll with `yield return null` until status ≠ 0, then resolve (`onResult(outputPath)`) or `onError(message)`, always `_FreeThumbExtractJob`.
- **Editor + Android:** `onError?.Invoke("no native thumbnail extractor on this platform")` — caller keeps the server thumb. Never throws.

### New — `Assets/Scripts/Chat/VideoThumbQueue.cs` (plain C#, no UnityEngine deps)

Pure, EditMode-testable state machine that owns dedup + concurrency bookkeeping (kept separate from the MonoBehaviour so it can be unit-tested without the runtime):

```csharp
bool        TryEnqueue(string messageId);   // false if already queued/in-flight/done
List<string> Dispatch();                    // up to (maxConcurrent - inFlight) ids to start
void        Complete(string messageId);     // success or fail; frees a slot
void        Clear();                        // bot switch / chat close
```

Durable dedup signal is the cache file (below), checked by the caller before `TryEnqueue`.

### New — `Assets/Scripts/Main/ChatManager.VideoThumbs.cs` (partial, like `ChatManager.MediaSend.cs`)

Drives the queue with coroutines:

- `public void EnqueueIncomingVideoThumb(MessageViewModel vm)` — guards: `vm.type == Video`, non-empty `vm.videoUrl`, our extracted frame not already cached (see key below), and `VideoThumbQueue.TryEnqueue(vm.messageId)`.
- Worker drains `Dispatch()` respecting `maxConcurrent = 2`. For each id: `finalPath = MediaCacheManager.GetFilePathFromUrl("vthumb://{id}")` → `VideoThumbnailExtractor.Extract(vm.videoUrl, finalPath, timeSec ≈ 1.0)` (native writes the JPEG atomically to `finalPath`).
  - **success:** `vm.thumbnailUrl = "vthumb://{id}"` → persist updated VM into `ChatHistoryCache` → fire `OnMessageMediaRefreshed(vm)` → `Complete(id)`.
  - **fail:** no-op (server placeholder stays) → `Complete(id)`.
- **Enqueue point:** the single `CreateViewModel` choke point — every server-sourced message is built there with `type`/`videoUrl`/`messageId` set, so one insertion covers both history and live. Separate from `Normalize`, so it does not collide with the in-flight ghost-recovery edits.
- **Lifecycle:** on bot switch and chat close, `Clear()` the queue and free native jobs. The durable signal is the cache file, so nothing is lost. Pause the worker on `OnApplicationPause(true)` to avoid background bandwidth.

### Cache key + durable dedup — `vthumb://{id}`

- Server placeholder stays at `thumb://{id}` (unchanged from today).
- Our extracted frame is a **distinct** key `vthumb://{id}`. Its file existence is the durable "already extracted" marker — survives restart for free, no side bookkeeping.
- On enqueue, if `vthumb://{id}` is already cached (prior session), set `vm.thumbnailUrl = "vthumb://{id}"` and skip extraction. On chat reopen the persisted VM already carries `vthumb://{id}`, so the native frame shows instantly with no re-extraction.

### UI refresh — reuse existing `OnMessageMediaRefreshed`

The existing **`ChatManager.OnMessageMediaRefreshed(MessageViewModel)`** event already drives a targeted single-bubble refresh: `MessageItemView.HandleMediaRefreshed` re-`Bind`s when `currentVm.messageId == refreshed.messageId`, and `Bind` reloads the thumbnail from cache. After extraction we mutate `vm.thumbnailUrl = "vthumb://{id}"` and fire `OnMessageMediaRefreshed(vm)` — **no new event, no `MessageListView` change**. (Re-firing `OnLiveMessagesReceived` is not an option: the live path appends and would duplicate the bubble, [MessageListView.cs:296](../../../Assets/Scripts/UI/MessageListView.cs).)

### Edit — `Assets/Scripts/UI/MessageItemView.cs`

- Broaden the thumbnail-scheme check in `ShowSmartThumbnail` at [MessageItemView.cs:1834](../../../Assets/Scripts/UI/MessageItemView.cs) so both `thumb://` and `vthumb://` route to `LoadImageFromCache` (both are MediaCacheManager keys — `GetFilePathFromUrl` hashes any string). The existing `HandleMediaRefreshed` → `Bind` path then renders the new frame; no bespoke reload method needed.

### Edit — `Assets/Scripts/Main/ChatManager.cs` + `ChatManager.BotState.cs`

- Enqueue from the single `CreateViewModel` choke point: `if (vm.type == MessageType.Video) EnqueueIncomingVideoThumb(vm);`. Separate from `Normalize`, avoiding the in-flight ghost-recovery edits.
- `ClearVideoThumbQueue()` in `SetActiveBot` after its `StopAllCoroutines()` (which already cancels in-flight extraction coroutines running on `this`).

## Data flow

```
Incoming video (history batch OR live)
 └─ Normalize: thumbnailUrl = "thumb://{id}" from server JPEGThumbnail (transient placeholder)
 └─ CreateViewModel → EnqueueIncomingVideoThumb(vm)             [dedup]
      └─ worker (maxConcurrent=2) → VideoThumbnailExtractor.Extract(vm.videoUrl, timeSec≈1s)   [iOS native]
           ├─ success → native writes JPEG to cache("vthumb://{id}") → vm.thumbnailUrl="vthumb://{id}"
           │            → persist → OnMessageMediaRefreshed(vm)
           │               → MessageItemView.HandleMediaRefreshed → Bind → ShowSmartThumbnail loads vthumb://
           └─ fail (Android/Editor/expired/network) → keep "thumb://{id}" server placeholder (no-op)
```

## Throttling, lifecycle, performance

- `maxConcurrent = 2` (tunable). Videos extract in parse order, two at a time.
- Dedup via `VideoThumbQueue` (in-session) + `vthumb://{id}` cache existence (durable across restart).
- Worker paused while app backgrounded.
- Storage: thumbnails are small images in the existing per-bot media dir; `MediaCacheManager.ClearCache` already wipes them. No eviction needed.

## Edge cases

- **Expired s3 URL** (`videoUrl` past `expireTime`): native fetch fails → keep server placeholder (v1). *Future v2:* refresh via `DownloadMediaForMessage`, retry once.
- **No `videoUrl`** (no `s3Info`): skip; server thumb only.
- **Group chats:** identical path.
- **Bot switch / chat close mid-extract:** `Clear()` + free jobs; durable cache file means no loss.
- **Native job leak:** always `_FreeThumbExtractJob` after resolve/fail.
- **Very short / zero-duration clip:** clamp `timeSec` to `[0, duration]` native-side.

## Testing

- **EditMode (user runs in Editor):** `VideoThumbQueueTests` — enqueue, dedup, concurrency cap, completion frees a slot. Pure logic, no runtime.
- **Editor play mode:** `Extract` returns `onError` (no native) → verify graceful fallback keeps server thumb, no exceptions thrown.
- **iOS device (user runs):**
  1. Incoming video **with** server thumb → instant server preview, then swaps to the sharper native frame.
  2. Incoming video **without** server thumb → native frame appears after extraction.
  3. Video-heavy chat → thumbnails fill in (two at a time), scrolling stays smooth.
  4. Reopen chat → instant from `vthumb://` cache, **no** re-extraction (verify via logs).
  5. Portrait video renders upright (rotation transform applied).
- **Android device (user runs):** bubbles fall back to server thumb; no crash, no native calls.

## Out of scope / future

- Android: download-then-extract fallback for the occasional codec `MediaMetadataRetriever` can't stream (v1 falls back to the server thumb).
- v2 expired-URL refresh-then-retry.
- Viewport-exact extraction priority (v1 extracts in parse order).

## Files

**New:**
- `Assets/Plugins/iOS/VideoThumbnailExtractor.mm`
- `Assets/Scripts/Chat/VideoThumbnailExtractor.cs`
- `Assets/Scripts/Chat/VideoThumbQueue.cs`
- `Assets/Scripts/Main/ChatManager.VideoThumbs.cs`
- `Assets/Plugins/Android/VideoThumbnailExtractor.java` (addendum)
- `Assets/Tests/Editor/Chat/VideoThumbQueueTests.cs`

**Edit:**
- `Assets/Scripts/Main/ChatManager.cs` — enqueue from `CreateViewModel`.
- `Assets/Scripts/Main/ChatManager.BotState.cs` — `ClearVideoThumbQueue()` in `SetActiveBot`.
- `Assets/Scripts/UI/MessageItemView.cs` — broaden scheme check to `vthumb://` (reuses existing `OnMessageMediaRefreshed` → `HandleMediaRefreshed`).

## Android native extraction (addendum — iOS parity)

Closes the deferred Android item so Android gets its own native frames instead of relying on Wappi's `JPEGThumbnail`. **Additive:** one new Java file + a `#elif` branch in the existing bridge. Orchestration (`ChatManager.VideoThumbs`), the `vthumb://` cache, and rendering are already platform-agnostic — Android just starts succeeding instead of hitting `onError`.

### New — `Assets/Plugins/Android/VideoThumbnailExtractor.java` (`com.unity.video`)

Job/poll API mirroring the iOS C externs so the bridge coroutine stays symmetric:
- `int startThumbExtract(String url, String outPath, double timeSec)` — returns a jobId and spawns a **background thread** (network `setDataSource` on the UI thread would ANR). On that thread: `MediaMetadataRetriever.setDataSource(url)` → `getFrameAtTime(timeSec*1e6, OPTION_CLOSEST_SYNC)` → scale the `Bitmap` to ≤640px long edge via `Bitmap.createScaledBitmap` (`getScaledFrameAtTime` needs API 27 > our minSdk 25) → `compress(JPEG, 90)` to `outPath` → set status. Jobs in a `ConcurrentHashMap<Integer, Job>`; `retriever.release()` in a `finally`.
- `int pollThumbExtract(int jobId)` — 0 running, 1 done, 2 fail.
- `String thumbExtractError(int jobId)`.
- `void freeThumbExtractJob(int jobId)`.

### Edit — `Assets/Scripts/Chat/VideoThumbnailExtractor.cs`

Add a `#elif UNITY_ANDROID && !UNITY_EDITOR` branch that caches a static `AndroidJavaClass("com.unity.video.VideoThumbnailExtractor")` and drives it with the **same `yield return null` poll loop** as iOS (`CallStatic<int>("startThumbExtract", …)` → poll `pollThumbExtract` → `onResult(outputPath)` / `onError(thumbExtractError(jobId))` → `freeThumbExtractJob`). The Editor `#else` (`onError`) is unchanged.

### Decisions

- **Poll bridge, not `UnitySendMessage`** — keeps `Extract` identical across platforms; no receiver GameObject.
- **Failure = best-effort + server-thumb fallback** — any `setDataSource`/decode failure → `pollThumbExtract` returns 2 → bridge `onError` → keep the server thumb (identical to iOS failure semantics). `MediaMetadataRetriever` over network is less robust than AVFoundation for odd codecs; standard MP4/H.264 (what Wappi delivers) is generally fine.
- **minSdk 25** → `getFrameAtTime` + manual scale (not `getScaledFrameAtTime`).
- **INTERNET permission** — already present (app networks throughout; Unity auto-adds it). No manifest change.

### Testing

- No new EditMode tests (shared `VideoThumbQueue` already covered; the Java path is device-only).
- Android device: an incoming/outgoing video **without** a server thumb now shows a native frame; no ANR/jank while a video-heavy chat extracts two-at-a-time; failures fall back to the server thumb; rotation looks correct.

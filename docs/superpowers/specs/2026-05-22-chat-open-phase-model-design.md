# Chat-Open Phase Model & Bubble Memory Cleanup

**Date**: 2026-05-22
**Status**: Approved, awaiting implementation plan
**Scope**: `Assets/Scripts/Main/ChatManager.cs`, `Assets/Scripts/Chat/SwipeToBack.cs`, `Assets/Scripts/UI/MessageListView.cs`, `Assets/Scripts/UI/MessageItemView.cs`, `Assets/Scripts/Chat/MediaCacheManager.cs`

## 1. Problem

Two coupled defects in the chat-open flow:

**Animation feel.** Opening a chat does not feel professional. Cache parse, sync result processing, layout rebuild, and image decode all overlap the slide-in animation, drop the slide framerate, and leave UI work running after the slide settles. Slide-out shows the same overlap symptom — late network responses and finishing decodes run while the user is dragging back.

**Memory crash.** Opening a media-heavy chat 6–9 times in a row crashes the app on device. Root cause: every `MessageItemView` creates `Texture2D` and `Sprite` instances in code (`new Texture2D(2,2)`, `Sprite.Create(...)`) but never destroys them. When `MessageListView.Clear()` destroys the bubble `GameObject`, the orphaned native textures and sprites stay resident. A heavy chat creates ~15–50 textures per open × several MB each; after 6–9 opens the device OOM-kills the process.

The fixes overlap because cleanup must happen at chat-switch and slide-out boundaries — the same boundaries the animation phase model owns.

## 2. Goal

Reshape chat-open into three discrete phases with hard gates between them, and add owned-resource cleanup to `MessageItemView` so every bubble frees its dynamic textures and sprites when destroyed.

**Phase model**

| Phase | Window | Owns |
|-------|--------|------|
| **Prep** | t=0 → 300 ms | Cache load + parse + first-screen split. Network sync begins. No UI activation, no spawning, no layout. |
| **Slide** | 300 → ~600 ms | Slide-in animation alone. All heavy main-thread work is gated. |
| **Populate** | 600 ms onward | Spawn first-screen bubbles, drain pending sync results, resume decodes. |

Slide-out is gated symmetrically: bubble spawning and decodes pause during the swipe-back drag and snap, and bubbles are freed when the slide-out completes.

## 3. Scope

**In scope**

- A `ChatOpenPhase` state machine on `ChatManager` (Idle / Prep / Slide / Populate).
- A literal 300 ms prep window between tap and slide-in start.
- A widened `IsSliding`-style gate covering swipe-back drag, slide-in, and slide-out.
- Owned-resource ledger on `MessageItemView` with `OnDestroy` cleanup.
- `Resources.UnloadUnusedAssets()` once per chat-switch during the prep window.
- Removal of the dormant `spriteMemoryCache` in `MediaCacheManager`.
- Bubble cleanup on slide-out completion (currently happens only at next `OnChatSelected`).

**Out of scope**

- Bubble layout, sizing, or visual styling (already shipped).
- Sync protocol or pagination logic.
- Media disk cache (the file-cache half of `MediaCacheManager` stays).
- Chat list rendering, chat list pagination.
- Replacing the snap-tween with a different easing or library.
- Editor-only profiling instrumentation — `ChatOpenLog` stays as-is.

## 4. Phase A — Prep (t=0 → 300 ms)

Triggered synchronously by `SelectChat(chatId)`. The user has just tapped a chat row. Nothing is visible yet.

**Synchronous work** (runs in the tap handler before the prep coroutine starts):

1. Stop active per-chat coroutines.
2. Fire `OnChatSelected(chatId)` so `MessageListView` runs `Clear()` and destroys old bubbles. Each bubble's `OnDestroy` frees its owned texture and sprites.
3. Set `_phase = Prep`.
4. Optimistic unread-clear on the selected `ChatViewModel` (existing behavior, unchanged).

**Prep coroutine** (`OpenChatRoutine`):

1. On device only (`!Application.isEditor`), call `Resources.UnloadUnusedAssets()` to release orphaned natives. Editor skips this — the cost shows up as iteration friction.
2. Load cache from disk via `ChatHistoryCache.LoadHistory`.
3. **Cache present path:** Promote stale-Pending tempIds to Failed (existing logic, moved verbatim). Sort newest-first, register all ids in `seenMessageIds`, compute `FirstScreenMessageCount`. Split into `_pendingFirstBatch` (first-screen newest) and `_cachedQueue` (older overflow). Start `SyncLatestMessages` — its result is queued via `_pendingSyncResult`, never fired during Prep.
4. **No cache path** (first-time open of a brand-new chat): Start `GetMessagesRoutine` for page 1. Its result is captured into `_pendingFirstBatch` + `_cachedQueue` using the same first-screen split. If the network request completes within the 300 ms window, the slide reveals a populated screen at first paint. If not, `_pendingFirstBatch` is empty when the slide ends, and the bubbles paint in Phase C once the response lands (existing fetch-then-render shape, just gated by `_phase`).
5. Wait until `t >= 300 ms` from tap time (use `Time.realtimeSinceStartup`, not frame count). If prep finished early, the remaining time is intentional lead-in.
6. Transition to Phase B by calling `SwipeToBack.SlideInToMessages(onComplete: PopulateBubbles)`.

**Activation rule:** the panel is NOT activated during Prep. `SwipeToBack.SlideInToMessages` is the only entry point that activates the panel and positions it off-screen, atomically with starting the slide. This prevents the one-frame flash that killed the prior pre-spawn attempt.

**Cancellation:** if `SelectChat` is invoked again during Prep, the in-flight `OpenChatRoutine` is stopped, `_pendingFirstBatch` and `_pendingSyncResult` reset, `_phase` resets to `Prep` for the new chat, and the timer restarts.

## 5. Phase B — Slide (300 → ~600 ms)

`_phase = Slide`. `SwipeToBack.IsSliding = true`.

The slide-in animation tween in `SwipeToBack.SnapToPosition` runs alone. Every cross-cutting concern below gates on either `IsSliding` or `_phase`:

- **`MessageItemView.AcquireDecodeSlot`** — extends today's check. Wait while `IsSliding` OR `_phase == Prep`. (No decodes during prep either — panel isn't visible, work may be cancelled.)
- **`MessageListView.HandleLiveMessages`** — defers new-message spawning if `_phase != Populate`. Replaces the existing `isInitialLoadInProgress` flag (the phase covers the same semantics).
- **`ChatManager.SyncLatestMessages`** — widens its current `IsSliding`-wait to `_phase != Populate`, keeping the 500 ms cap.

When `SnapToPosition` finishes, it invokes the `onComplete` callback. The slide-in's callback transitions to Phase C.

## 6. Phase C — Populate (600 ms → settled)

`_phase = Populate`. `IsSliding = false`.

1. Fire `OnBatchMessagesLoaded(_pendingFirstBatch, isLoadMore: false, hasMore: true)`. `MessageListView.UpdateListRoutine` spawns the first-screen bubbles with its existing per-item yield budget.
2. Drain any queued sync result: brand-new messages via `OnLiveMessagesReceived`, status updates via `OnMessageStatusChanged`. These fire AFTER `OnBatchMessagesLoaded` so `_activeChatCache` is in place when they land.
3. Image decodes ungate and resume on the per-frame budget.
4. `MessageListView.UpdateListRoutine` clearing `isInitialLoadInProgress` is removed — phase already covers it.

## 7. Slide-Out Symmetry

Slide-out runs in two segments today: user's finger drag + snap-tween. `IsSliding` only covers the snap. Bubble spawning and decodes can race the drag portion.

**Changes:**

- `SwipeToBack.OnBeginDrag`: if the drag direction is horizontal-right, set `IsSliding = true` for the duration of the drag.
- `SwipeToBack.OnEndDrag`: keep `IsSliding = true`; let `SnapToPosition` flip it back at the end of the snap.
- After `SnapToPosition` finishes a slide-out (`triggerBack == true`), fire `onSwipeComplete` (existing) AND a new `OnSlideOutComplete` event consumed by `MessageListView`. `MessageListView` runs `Clear()` to destroy bubbles immediately — every swipe-back recovers the memory of the chat the user just left.

**Cancellation while sliding-in:**

If the user starts a swipe-back during Phase B, the simplest viable behavior is to lock out swipe input until `_phase == Populate`. The slide-in is only ~300 ms — the lockout is brief and visually clean. Mid-tween cancellation looks janky and adds state to `SnapToPosition`; deferred.

## 8. Memory Cleanup (`MessageItemView`)

**Ownership ledger** — three private fields:

```csharp
private Texture2D _ownedTexture;          // backing texture for the bubble's image
private Sprite    _ownedBubbleSprite;     // messageImage.sprite when dynamically created
private Sprite    _ownedFullScreenSprite; // fullScreenSprite when distinct
```

**Helper** — centralises the destroy-then-assign pattern. Every dynamic-texture or dynamic-sprite assignment site routes through this:

```csharp
private void AssignDynamicVisuals(Texture2D tex, Sprite bubbleSpr, Sprite fullSpr)
{
    if (_ownedBubbleSprite != null && _ownedBubbleSprite != bubbleSpr)
        Destroy(_ownedBubbleSprite);
    if (_ownedFullScreenSprite != null
        && _ownedFullScreenSprite != fullSpr
        && _ownedFullScreenSprite != bubbleSpr)
        Destroy(_ownedFullScreenSprite);
    if (_ownedTexture != null && _ownedTexture != tex)
        Destroy(_ownedTexture);

    _ownedTexture = tex;
    _ownedBubbleSprite = bubbleSpr;
    _ownedFullScreenSprite = fullSpr;
}
```

**Sites to route through the helper:**

- `ApplyTextureAspectFill` — sprite-create paths (image, sticker, cropped landscape, full sprite).
- `SmartMediaRoutine` cache-intercept path — `new Texture2D(2,2) + LoadImage`.
- `DownloadSmartHDBytes` success branch — texture create then `ApplyTextureAspectFill`.
- `ShowSmartThumbnail` — thumbnail texture path.
- `LoadBase64Image` — base64-decoded texture path.
- Animated-WebP frame path (sticker animation) — first-frame `Texture2D` and `Sprite.Create`.
- Manual-download retry path.

`stickerPlaceholder`, `playIcon`, `stopIcon`, `downloadArrowIcon`, `messageImage.sprite = null` assignments are NOT routed through the helper — those use project assets, not dynamic resources.

**`OnDestroy` hook:**

```csharp
void OnDestroy()
{
    if (_ownedBubbleSprite != null) Destroy(_ownedBubbleSprite);
    if (_ownedFullScreenSprite != null && _ownedFullScreenSprite != _ownedBubbleSprite)
        Destroy(_ownedFullScreenSprite);
    if (_ownedTexture != null) Destroy(_ownedTexture);
}
```

**Destroy order:** sprite first, then texture. An `Image` reading from a destroyed texture while the sprite still exists can render garbage on iOS. The natural order in the helper and `OnDestroy` is sprite-then-texture.

## 9. `MediaCacheManager` Audit

**Remove (dead code):**

- `MaxMemorySpriteCount` constant
- `spriteMemoryCache` dictionary
- `spriteAccessOrder` linked list
- `GetSpriteFromMemory`, `StoreSpriteInMemory`

A repo-wide search returned zero callers of `GetSpriteFromMemory` or `StoreSpriteInMemory`. Carrying the in-memory sprite cache as dead code is a liability — if a future change reintroduces it without coordinating with the new ownership model, the `Image` consuming a cached sprite would crash once `OnDestroy` destroys its underlying texture. Removing it now is safer than annotating it.

**Keep:**

- `IsImageCached`, `SaveImageToCache`, `LoadImageFromCache`, `GetFilePathFromUrl`, `ClearCache`, `EnsureBotScoped`, `urlPathCache`, `cachedUrlBotId`.

The file-cache path is untouched. Disk caching is the load-bearing optimization; the dead memory cache adds nothing on top.

## 10. Edge Cases

| Scenario | Behavior |
|---|---|
| **Rapid re-tap (Chat A → Chat B within 100 ms)** | `SelectChat(B)` stops `OpenChatRoutine(A)`, resets pending buffers, resets phase to Prep, restarts the 300 ms timer for B. Chat A's bubbles are never spawned. |
| **Swipe-back during Phase A** | Panel never activated. `ShowChatList` already early-returns when `MessageListPanel.activeSelf == false`. Also stop `OpenChatRoutine` and reset phase to Idle. |
| **Swipe-back attempted during Phase B (slide-in)** | Swipe input locked out until `_phase == Populate`. Drag is ignored; chat fully opens, then user can swipe back. |
| **Swipe-back during Phase C (still spawning)** | `OnSlideOutComplete` → `Clear()` destroys all bubbles including in-flight ones. `StopAllCoroutines()` on `MessageListView` kills `UpdateListRoutine`. Each bubble's `OnDestroy` frees its owned resources. |
| **Sync returns during Prep** | Result queued in `_pendingSyncResult`. Fired after `OnBatchMessagesLoaded` in Phase C so `_activeChatCache` is set up first. |
| **Sync returns mid-Slide** | Existing `IsSliding`-wait, widened to `_phase != Populate`. Same 500 ms cap. |
| **Sync error / no profile** | `_pendingSyncResult` stays empty. Populate proceeds with cache only. |
| **App backgrounded mid-Prep** | Coroutine pauses on Unity's pause semantics. Resumes on focus. Slide eventually completes. Not a regression. |
| **Bot switch during Prep** | Existing `SetActiveBot` calls `StopAllCoroutines` on `ChatManager` — kills `OpenChatRoutine`. New behavior: also reset `_phase = Idle` so a subsequent `SelectChat` starts cleanly. |
| **Same chat re-opened** | Same chat tap → same logic. Old bubbles get destroyed, new bubbles spawn from refreshed cache. Doesn't short-circuit; the phase model is uniform. |

## 11. Acceptance Criteria

1. Tapping a chat row → no visible activity for 300 ms → slide-in begins → animation runs at the device's vsync rate (60 fps on most phones) with no spawn or decode work overlapping it → slide settles → bubbles appear progressively.
2. Slide-out animation runs at vsync rate from the moment the user starts dragging.
3. Opening a media-heavy chat 10+ times in a row does not crash. Native memory growth between opens is bounded by the disk cache, not by leaked textures.
4. Returning to chat list (slide-out) immediately destroys the chat's bubbles and frees their textures.
5. The `spriteMemoryCache` is removed from `MediaCacheManager`. `IsImageCached` / `SaveImageToCache` / `LoadImageFromCache` still work.
6. Existing functionality preserved: optimistic send, ghost-recovery dedup, status-tick refresh, pagination, swipe-back gesture, group-chat sender names, date separators.

## 12. Non-Goals

- Faster than 300 ms feel. The 300 ms wait is the intended product behavior.
- Removing the per-frame decode budget. It's the right shape; the phase gate sits on top.
- Object pooling for bubbles. Out of scope; cleanup model assumes destroy-on-clear.
- Async I/O for cache reads. `File.ReadAllBytes` cost during prep is acceptable inside the 300 ms window.
- Profiler-driven micro-optimization. Correctness of the phase boundaries first; perf measurement after.

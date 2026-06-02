# Outgoing Video Upload Progress Ring (Design)

**Date:** 2026-06-02
**Status:** Approved (brainstorm) — ready for implementation plan
**Related:** `docs/superpowers/specs/2026-06-02-incoming-video-thumbnails-design.md` — touches incoming/URL-only videos; this spec is outgoing/staged-send only. No logic overlap.

## Problem

When a user sends a video, the optimistic bubble appears immediately with a play button over the thumbnail and `deliveryStatus = Pending`, then `PostMediaMessageRoutine` runs convert → encode → upload silently ([ChatManager.MediaSend.cs:213](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs)). Today the only signal back to the bubble is the terminal `OnMessageStatusChanged(... Sent | Failed)` — there is **no in-flight progress**. For a multi-MB video that spends seconds converting + uploading, the bubble looks frozen and the send can't be cancelled.

## Goal

Ring the outgoing video bubble's play button with a WhatsApp-style radial progress indicator that fills `0 → 100%` across the whole send pipeline, with a cancel (X) in the center that aborts the in-flight send and removes the bubble.

## Decisions (locked)

| Decision | Choice | Rationale |
|---|---|---|
| Progress semantics | Whole-pipeline % (convert + encode + upload as one 0→1) | User wants a single continuous fill, not a phase-by-phase or busy spinner |
| Phase weighting | Convert `0→0.30`, Encode `0.30→0.40`, Upload `0.40→1.00` (tunable consts) | Convert (iOS) + upload are the slow, measurable phases; encode is a fast opaque `Task` |
| Center icon during send | Cancel (X) | User chose WhatsApp-style cancel over keeping the play button |
| Cancel result | Fully delete the bubble + outbox entry (no "cancelled" placeholder) | Matches WhatsApp; keeps the transcript clean |
| Plumbing | Event-driven through `ChatManager` (new `OnMediaSendProgress` + `OnMessageRemoved`) | Matches the existing "views subscribe to ChatManager events, never poll" convention; survives bot-switch |
| Scope | Video only | Images/documents have no play button and upload fast; the ring concept is video-specific |

### Rejected alternatives

- **View polls `GetSendProgress(tempId)` in `Update()`** — violates the event-driven UI convention; per-frame cost on every bubble.
- **Stash `Action<float>` on `MessageViewModel`** — the VM is a pure data model; a live callback breaks on rebind / cache-replay / bot-switch.

## Architecture

Five script edits plus one prefab, no new files. Progress + cancel are wired into the **single** shared send coroutine, so the optimistic path *and* tap-to-retry get both for free (retry re-runs `PostMediaMessageRoutine` and re-fires `Pending`).

### Edit — `Assets/Scripts/Main/ChatManager.cs` (events)

Two new events alongside the existing media events (~[ChatManager.cs:105-123](../../../Assets/Scripts/Main/ChatManager.cs)):

```csharp
public event Action<string, float>  OnMediaSendProgress;  // (tempId, 0..1)
public event Action<string>         OnMessageRemoved;     // (tempId) — bubble should self-destruct
```

`OnMessageRemoved` fills a genuine gap: today only `OnLiveMessagesReceived` adds bubbles; nothing removes a single one.

### Edit — `Assets/Scripts/Main/ChatManager.MediaSend.cs` (progress + cancel)

**Phase-mapping helper (pure, unit-tested):**

```csharp
// Maps (phase, intra-phase 0..1) onto the whole-pipeline 0..1.
internal static float SendProgress(SendPhase phase, float sub) => phase switch
{
    SendPhase.Convert => 0.00f + 0.30f * Mathf.Clamp01(sub),
    SendPhase.Encode  => 0.30f + 0.10f * Mathf.Clamp01(sub),
    SendPhase.Upload  => 0.40f + 0.60f * Mathf.Clamp01(sub),
    _ => 0f,
};
```

**In-flight registry** keyed by tempId so cancel can reach a running send:

```csharp
private class MediaSendContext
{
    public Coroutine routine;
    public UnityWebRequest request;  // non-null only during the upload phase
    public bool cancelled;
}
private readonly Dictionary<string, MediaSendContext> _inFlight = new();
```

**`PostMediaMessageRoutine` changes:**
- Register a `MediaSendContext` at entry; unregister in all exit paths.
- Convert: pass an `onProgress` lambda into `VideoConverter.Convert` → `OnMediaSendProgress(tempId, SendProgress(Convert, p))`.
- Encode: emit `SendProgress(Encode, 1f)` once the `Task` completes (opaque, so it snaps).
- Upload: replace `yield return www.SendWebRequest()` ([line 289](../../../Assets/Scripts/Main/ChatManager.MediaSend.cs)) with a non-blocking poll:
  ```csharp
  ctx.request = www;
  var op = www.SendWebRequest();
  while (!op.isDone)
  {
      OnMediaSendProgress?.Invoke(entry.tempId, SendProgress(SendPhase.Upload, www.uploadProgress));
      yield return null;
  }
  ctx.request = null;
  ```
- Check `ctx.cancelled` at every phase boundary (after convert, after encode, after the upload loop); on cancel, `yield break` **without** firing `Failed`.

**`CancelMediaSend(string tempId)`:**
1. Look up the context; set `cancelled = true`.
2. `ctx.request?.Abort()` (upload aborts immediately; the loop's `cancelled` check prevents a `Failed` fire).
3. `StopCoroutine(ctx.routine)` on the same runner that started it (`Manager.Instance` or `this`).
4. Remove from `ChatHistoryCache`, `Outbox` ([OutboxStore.Remove](../../../Assets/Scripts/Chat/OutboxStore.cs)), and `seenMessageIds`.
5. Best-effort delete staged temp files (`staged_video_{tempId}.mov`, `send_{tempId}.mp4`).
6. Fire `OnMessageRemoved(tempId)`.

### Edit — `Assets/Scripts/Chat/VideoConverter.cs` (surface convert progress)

`Convert` gains an optional progress callback that surfaces the already-present native poll ([`_PollVideoConvertProgress`, line 20](../../../Assets/Scripts/Chat/VideoConverter.cs)):

```csharp
public static IEnumerator Convert(string inputPath, string outputPath, long maxBytes,
                                  Action<string> onResult, Action<string> onError,
                                  Action<float> onProgress = null)
```

- **iOS:** invoke `onProgress(_PollVideoConvertProgress(jobId))` inside the existing poll loop.
- **Editor + Android:** invoke `onProgress?.Invoke(1f)` once before the pass-through `onResult` (convert is instant, so the ring snaps to the convert ceiling).

### Edit — `Assets/Scripts/UI/MessageListView.cs` (removal)

Subscribe to `OnMediaSendProgress`/`OnMessageRemoved` lifecycle and add:

```csharp
void HandleMessageRemoved(string tempId)  // find the spawned bubble by BoundVm.messageId, Destroy it
```

Mirrors the existing `HandleLiveMessages` ([MessageListView.cs:342](../../../Assets/Scripts/UI/MessageListView.cs)) subscribe/unsubscribe pairing in `OnEnable`/`OnDestroy`.

### Edit — `Assets/Scripts/UI/MessageItemView.cs` + `Assets/Prefabs/MessageTextOutgoing.prefab`

**Prefab** (`MessageTextOutgoing` only — outgoing is the sole sender path via [`ResolvePrefab`](../../../Assets/Scripts/UI/MessageListView.cs)):
- Add a radial progress `Image` (`type = Filled`, `fillMethod = Radial360`, `fillOrigin = Top`, clockwise) wrapping the play button, and a cancel (X) graphic — both children of `playOverlay`.
- Ring style follows the WhatsApp palette already in the bubble: white arc, ~6px stroke on the 1080-ref canvas, on a faint dark scrim. Animate `fillAmount` changes via DOTween (`DOFillAmount`, ~0.15s) so byte-level jumps read smoothly.

**Script** (new `[SerializeField] private` refs: `uploadRing`, `cancelButton`/`cancelIcon`):
- In `OnEnable`/`OnDisable`, subscribe/unsubscribe `OnMediaSendProgress` → `HandleSendProgress(tempId, p)` next to the existing `OnMessageStatusChanged` wiring ([line 236](../../../Assets/Scripts/UI/MessageItemView.cs)).
- `HandleSendProgress`: id-match (same dual old/new id guard as `HandleStatusChanged`), and only for `MessageType.Video` outgoing. Show ring + X (hide the play icon), set `fillAmount`.
- Cancel button `onClick` → `ChatManager.Instance.CancelMediaSend(currentVm.messageId)` (guard `ScrollClickBlocker.IsBlocking`, as other taps do).
- On `Sent` (existing `HandleStatusChanged` → `SetDeliveryStatus` at [line 3357](../../../Assets/Scripts/UI/MessageItemView.cs)): hide ring + X, restore play icon. On `Failed`: hide ring + X, restore play icon; existing Failed-tick + tap-to-retry path is unchanged.
- Initial bind: an outgoing video bubble that is `Pending` with no progress yet shows the ring at the last-known/zero fill + X (so a reopened in-flight send still reads as uploading).

## Edge cases

- **Cancel mid-convert (iOS):** the native `AVAssetExportSession` job can't be hard-killed. We set `cancelled`, stop the coroutine, and best-effort delete temps. A job that finishes after cancel may briefly recreate `send_{tempId}.mp4` in `Library/Caches` — purgeable, acceptable.
- **Cancel mid-encode:** the `Task` can't be cheaply cancelled; we discard its result via the `cancelled` check. Minor wasted CPU.
- **Bubble destroyed by chat-switch mid-send:** the send runs headless on `Manager.Instance` and completes normally; no bubble to update. If later cancelled, `OnMessageRemoved` no-ops on a view that's already gone.
- **Retry:** `RetryOutboxMessage` re-fires `Pending` ([ChatManager.Outbox.cs:37](../../../Assets/Scripts/Main/ChatManager.Outbox.cs)) and re-runs the routine, so the ring + cancel reappear automatically.
- **Images/documents:** `OnMediaSendProgress` may fire for them, but `MessageItemView` renders the ring only for `MessageType.Video`; their existing spinner/behavior is untouched.

## Testing

- **EditMode (pure):** `SendProgress(phase, sub)` mapping — boundaries (0, 1), clamping, monotonic across phases.
- **EditMode (bookkeeping):** registry add/remove and cancel-cleanup ordering against fakes for `ChatHistoryCache` / `Outbox` (no `UnityWebRequest`).
- **Manual on-device (iOS):** real convert progress + upload progress + cancel mid-each-phase; verify bubble deletion, temp-file cleanup, and that no `Failed` tick flashes on cancel. Upload progress and native convert do not run in the Editor.

## Non-goals

- No progress ring for image or document sends.
- No pause/resume; cancel is terminal.
- No change to the fullscreen `VideoController`.

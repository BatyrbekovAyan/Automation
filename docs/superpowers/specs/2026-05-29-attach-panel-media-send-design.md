# Attach Panel — Part C: Wappi Media Upload + Persistence + Real message_id

**Date**: 2026-05-29
**Status**: Approved, awaiting implementation plan
**Predecessors**: `2026-05-26-attach-sheet-design.md` (part "a" — AttachSheet UI + native pickers + `OnPicked`), `2026-05-28-attach-sheet-preview-caption-design.md` (part "b" — preview screen + caption + in-memory optimistic staging)
**Scope**: new `Assets/Scripts/Main/ChatManager.MediaSend.cs`, new `Assets/Scripts/Chat/Base64Encoder.cs`, new `Assets/Scripts/Chat/WappiMediaRequestFactory.cs`, additive edits to `Assets/Scripts/Chat/OutboxStore.cs`, `Assets/Scripts/Main/ChatManager.Outbox.cs`, `Assets/Scripts/Main/ChatManager.cs`, and a pre-stage size guard in `Assets/Scripts/Chat/AttachmentPreviewScreen.cs`. New EditMode tests under `Assets/Tests/Editor/Chat/`.

## 1. Problem

Part "b" left `ChatManager.StageLocalMedia(pick, caption, preloadedImage)` as an **in-memory-only** optimistic stub: it builds a `MessageViewModel`, seeds the image/video thumbnail into `MediaCacheManager` under a synthetic `staged://` URL, and fires `OnLiveMessagesReceived`. It does **not** persist to `ChatHistoryCache`, does **not** enqueue in `Outbox`, and does **not** upload to Wappi. The staged bubble shows at `DeliveryStatus.Pending` forever, vanishes on chat/bot switch, and never reaches the recipient.

The end state of part "b" is "bubble appears locally; nothing is sent."

## 2. Goal

Make a picked image / video / document actually send to WhatsApp through Wappi, with the **same resilience the text send already has**: the optimistic bubble persists across chat reopen, the real `message_id` from Wappi replaces the temp id, the status flips `Pending → Sent`, a failed send shows a red bubble with tap-to-retry, and a mid-send bot switch lands the result in the originating bot. After part "c", the attach panel is the WhatsApp-complete flow end to end.

The behavior must mirror `SendTextMessageRoutine` / `PostTextMessageRoutine` / `RetryRoutine` as closely as possible — that proven text path is the template.

## 3. Decisions locked from brainstorming

- **Full text parity** for resilience (Q1). Persist the optimistic bubble to `ChatHistoryCache`, enqueue in `Outbox`, fail-in-place with tap-to-retry. `OutboxEntry` is extended (not replaced) with media metadata.
- **Reuse the staged JPEG** for image uploads (Q2). The ≤1024px q90 JPEG that part "b" already decoded (HEIC-handled) and wrote to `MediaCacheManager` under `staged://image/{tempId}` is the byte source — no second decode, WhatsApp-like compression, smaller payload.
- **Video: size cap + off-thread encode** (Q3). A pre-stage size guard rejects over-cap videos with a toast; base64 encoding runs off the main thread for all kinds to avoid frame hitches / OOM.
- **Code structure: Approach A** — mirror the text path in a new `ChatManager` partial; extend `OutboxEntry` + `RetryRoutine`; extract a tiny testable base64 helper and a pure request factory.
- **Retry durability: Light (path-only)** for video/document. The outbox stores the source path. In-session retry works (the common case). After an app restart, if the OS cleared the picked file, retry fails gracefully to `Failed`. No new file-copy/cleanup subsystem. Images always retry (the staged JPEG lives in `persistentDataPath`).
- **Scope: image, video, document.** `audio/send` exists in the Wappi API but is out of scope — the attach sheet has no `AttachmentKind.Audio` (audio/voice is a separate recording feature).

## 4. Wappi media-send contract (confirmed from the dashboard)

All four are `POST`, `Authorization: {token}` header, `profile_id` as a **query** param, body `application/json`, and `b64_file` is **raw base64 with no `data:` prefix**. The success response is the same shape as the text `message/send` (so reconciliation is identical).

| Kind | Endpoint | Body fields |
|---|---|---|
| Image | `https://wappi.pro/api/sync/message/img/send?profile_id={id}` | `recipient`, `caption`, `b64_file` |
| Video | `https://wappi.pro/api/sync/message/video/send?profile_id={id}` | `recipient`, `caption`, `b64_file` |
| Document | `https://wappi.pro/api/sync/message/document/send?profile_id={id}` | `recipient`, `caption`, `file_name`, `b64_file` |
| Audio *(out of scope)* | `https://wappi.pro/api/sync/message/audio/send?profile_id={id}` | `recipient`, `b64_file` |

Success response (200):
```json
{
  "status": "done",
  "timestamp": 1679823745,
  "time": "2023-03-26T12:42:25+03:00",
  "message_id": "3EB086550A587F3D4A22",
  "task_id": "8d7e6a20-20e2-4582-a79b-2a4267ab4ae4"
}
```
We read `status` (must equal `"done"`) and `message_id`. `task_id`, `timestamp`, `time` are ignored. **The existing `WappiSendTextResponse` (`status` + `message_id` + `timestamp`) is reused as-is** — no new response model.

`recipient` is the bare phone (`79995579399`). We normalize the chat id the same way `PostTextMessageRoutine` does: strip a trailing `@c.us`; leave `@g.us` (groups) and anything else unchanged.

## 5. Scope

**In scope**

- Replace the *body tail* of `ChatManager.StageLocalMedia` so that, after the existing VM-build + cache-seed, it persists to `ChatHistoryCache`, updates the chat-list last-message preview, enqueues an `Outbox` media entry, fires `OnLiveMessagesReceived` (unchanged), and starts the network coroutine. Relocate `StageLocalMedia` and its seed helpers into the new `ChatManager.MediaSend.cs` partial.
- New `PostMediaMessageRoutine(OutboxEntry, sendCacheRoot)` — the network+reconcile half, shared by the initial send and retry, mirroring `PostTextMessageRoutine`.
- Extend `OutboxEntry` with a `kind` discriminator and media fields (append-only).
- Branch `RetryRoutine` on `entry.kind`.
- `Base64Encoder` (off-thread file→base64) and `WappiMediaRequestFactory` (pure endpoint+body builder) — both unit-tested.
- Pre-stage video size guard in `AttachmentPreviewScreen.OnSendTapped`.

**Out of scope**

- `audio/send` (no `AttachmentKind.Audio`).
- Telegram media send (this is the Wappi/WhatsApp path; Telegram is a separate surface).
- Tap-to-open on a just-sent document and tap-to-play robustness beyond what part "b" already provides (in-session `file://` playback). The recipient receives the file; local re-open across reopen relies on the later server sync.
- Multi-attachment batches (locked single-attachment in part "b").
- Image editing / recipient picker / forward.
- A durable cross-restart copy of video/document bytes (explicitly rejected in Q-retry; Light path-only chosen).
- Any change to `MessageItemView`'s rendering pipeline, `AttachSheet`, or the editor builders.
- Replacing the `staged://` cache entry with the real Wappi CDN URL after success (the bubble already holds the bytes; the eventual server sync supersedes it). Cache cleanup remains the bounded per-bot leak noted in part "b" §8.

## 6. Architecture & files

**New files**

- `Assets/Scripts/Main/ChatManager.MediaSend.cs` — `partial class ChatManager`. Houses the relocated `StageLocalMedia` (now with the persist + enqueue + dispatch tail), the relocated seed helpers (`SeedImageCache`, `SeedImageCacheFromTexture`, `SeedVideoThumbCache`, `ReadVideoMetadata`), and the new `PostMediaMessageRoutine`. Mirrors the `ChatManager.Outbox.cs` split that keeps the god-object trimmed.
- `Assets/Scripts/Chat/Base64Encoder.cs` — static, no Unity dependency:
  ```csharp
  public static class Base64Encoder
  {
      // Reads the file and base64-encodes it on a thread-pool thread so the
      // ~33% size inflation + large-buffer alloc never blocks a frame.
      public static Task<string> EncodeFileAsync(string path) => Task.Run(() =>
      {
          byte[] bytes = File.ReadAllBytes(path);
          return Convert.ToBase64String(bytes);
      });
  }
  ```
- `Assets/Scripts/Chat/WappiMediaRequestFactory.cs` — pure, testable. Maps `AttachmentKind` → endpoint URL and builds the JSON body. No `UnityWebRequest`, no I/O.
  ```csharp
  public static class WappiMediaRequestFactory
  {
      private const string Base = "https://wappi.pro/api/sync/message/";

      public static string EndpointFor(AttachmentKind kind, string profileId) => kind switch
      {
          AttachmentKind.Photo or AttachmentKind.GalleryImage => $"{Base}img/send?profile_id={profileId}",
          AttachmentKind.GalleryVideo                         => $"{Base}video/send?profile_id={profileId}",
          AttachmentKind.Document                             => $"{Base}document/send?profile_id={profileId}",
          _ => null
      };

      public static string NormalizeRecipient(string chatId) =>
          chatId != null && chatId.EndsWith("@c.us") ? chatId.Replace("@c.us", "") : chatId;

      // file_name is only serialized for documents (NullValueHandling.Ignore drops it elsewhere).
      public static string BuildBody(AttachmentKind kind, string chatId, string caption, string fileName, string b64)
      {
          var req = new WappiSendMediaRequest
          {
              recipient = NormalizeRecipient(chatId),
              caption   = caption ?? "",
              b64_file  = b64,
              file_name = kind == AttachmentKind.Document
                          ? (string.IsNullOrEmpty(fileName) ? "file" : fileName)
                          : null
          };
          return JsonConvert.SerializeObject(req,
              new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
      }
  }

  [Serializable]
  public class WappiSendMediaRequest
  {
      public string recipient;
      public string caption;
      public string file_name;
      public string b64_file;
  }
  ```

**Modified files**

- `Assets/Scripts/Chat/OutboxStore.cs` — extend `OutboxEntry` (§7).
- `Assets/Scripts/Main/ChatManager.Outbox.cs` — `RetryRoutine` branches on `kind` (§9).
- `Assets/Scripts/Main/ChatManager.cs` — remove the relocated `StageLocalMedia`/seed-helper bodies (moved to the partial). All shared state (`seenMessageIds`, `OnMessageStatusChanged`, `OnLiveMessagesReceived`, `GetActiveProfileId`, `GetCacheRoot`, `GetChat`, `currentChatId`) is reused unchanged.
- `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` — pre-stage video size guard in `OnSendTapped` (§8).

**New tests** — `Assets/Tests/Editor/Chat/Base64EncoderTests.cs`, `WappiMediaRequestFactoryTests.cs`, `OutboxEntryMediaCompatTests.cs` (matches the existing `AttachmentDisplayFormatTests.cs` EditMode pattern).

**Untouched (intentionally)** — `AttachSheet.cs`, `MessagesBottomPanel.cs`, `MessageItemView.cs`, `MediaCacheManager.cs` (its `GetFilePathFromUrl` is sufficient), all `Assets/Editor/*Builder.cs`.

## 7. Data model — `OutboxEntry` extension

Appended fields. JsonUtility fills missing fields with defaults when deserializing pre-existing **text** outbox files, so `kind` defaults to `Text` (0) and old entries keep working. Append-only, same discipline as `DeliveryStatus`.

```csharp
public enum OutboxKind { Text = 0, Media = 1 }   // append-only; persisted as int ordinal

[Serializable]
public class OutboxEntry
{
    // --- existing (unchanged) ---
    public string tempId;
    public string chatId;
    public string text;          // caption for media entries
    public long   timestamp;
    public int    attemptCount;
    public string profileId;

    // --- appended for part c ---
    public int    kind;            // OutboxKind ordinal; 0 = Text (back-compat default)
    public int    attachmentKind;  // AttachmentKind ordinal (Photo=0..Document=3)
    public string mediaPath;       // upload byte source: staged-JPEG disk path (image) | pick.Path (video/doc)
    public string mimeType;
    public string fileName;
    // staged cache URLs so a reopened chat re-renders the bubble from disk:
    public string mediaUrl;        // staged://image/{tempId}  or  staged://document/{tempId}
    public string thumbnailUrl;    // thumb://staged/{tempId}  (video)
    public string videoUrl;        // file://{pick.Path}       (video, in-session playback)
    public float  aspectRatio;
    public int    duration;
}
```

`text` carries the caption for media entries (it's already the field text reconciliation copies into the cached VM; reusing it keeps `PostMediaMessageRoutine` and `PostTextMessageRoutine` symmetric). The dedicated `mediaUrl`/`thumbnailUrl`/`videoUrl`/`aspectRatio`/`duration` fields exist only so a chat reopened *before* the upload resolves can rebuild the exact same bubble from `ChatHistoryCache` + the on-disk `staged://` cache.

## 8. Pre-stage video size guard

In `AttachmentPreviewScreen.OnSendTapped`, before calling `StageLocalMedia`, for `GalleryVideo` only:

```csharp
const long MaxVideoUploadBytes = 16L * 1024 * 1024;   // WhatsApp-like; tunable

if (pick.Kind == AttachmentKind.GalleryVideo && pick.FileSizeBytes > MaxVideoUploadBytes)
{
    ShowSizeError($"Video is too large to send (max {MaxVideoUploadBytes / (1024 * 1024)} MB).");
    if (sendButton != null) sendButton.interactable = true;  // user can go Back and re-pick
    return;   // do NOT stage, do NOT close the preview
}
```

`pick.FileSizeBytes` is already populated by the picker (used today for the document size label), so no extra I/O. The cap is a single named constant; images and documents are not capped in part "c" (WhatsApp tolerates large docs and our images are pre-downscaled to ≤1024px), but the constant location makes a future global guard trivial.

**Feedback surface (the rejection must not be silent — quality bar).** The project has **no** string-toast / snackbar API: `PopupUI.Show(GameObject panel, …)` is panel-based (it animates a *prebuilt* modal `GameObject`). Rather than build a new global toast subsystem (out of scope), `ShowSizeError(string)` renders the message **inline on the already-open preview screen** via a small error `TMP_Text` added to `AttachmentPreviewScreen`'s own hierarchy — no new shared UI, no new manager, and the user gets clear feedback before going Back to re-pick. This is the one UX mechanism chosen during self-review (flagged for confirmation at the review gate); if a reusable message panel is preferred later, only `ShowSizeError`'s body changes.

## 9. Send flow

### 9.1 `StageLocalMedia` (relocated, with the new tail)

Keeps the **entire existing body** (VM build + `seenMessageIds.Add` + per-kind cache seeding via `SeedImageCacheFromTexture` / `SeedImageCache` / `SeedVideoThumbCache` / `ReadVideoMetadata`, exactly as implemented today), then adds the text-parity tail. The cache-seed stays synchronous and completes before we return, so the caller (`AttachmentPreviewScreen`) is free to destroy `preloadedImage` immediately afterward — the upload reads bytes from disk, never from that texture.

```csharp
public void StageLocalMedia(AttachmentPick pick, string caption, Texture2D preloadedImage = null)
{
    if (string.IsNullOrEmpty(currentChatId)) return;
    if (pick == null || string.IsNullOrEmpty(pick.Path)) return;

    string sendCacheRoot   = GetCacheRoot();            // snapshot for bot-switch safety
    string activeProfileId = GetActiveProfileId();
    if (string.IsNullOrEmpty(activeProfileId)) { Debug.LogWarning("[ChatManager] StageLocalMedia: no profile"); return; }

    string tempId = "staging_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    // ---- existing VM build + per-kind cache seed (unchanged from part b) ----
    // ... builds `vm`, sets vm.type / vm.mediaUrl / vm.thumbnailUrl / vm.videoUrl /
    //     vm.aspectRatio / vm.duration exactly as today ...

    // ---- NEW: persist (parity with SendTextMessageRoutine) ----
    var cached = ChatHistoryCache.LoadHistory(sendCacheRoot, currentChatId);
    cached.Add(vm);
    ChatHistoryCache.SaveHistory(sendCacheRoot, currentChatId, cached);

    var chatVm = GetChat(currentChatId);
    if (chatVm != null) chatVm.UpdateLastMessage(LastMessagePreview(pick, caption), now);

    // ---- NEW: enqueue media outbox entry ----
    string mediaPath = (vm.type == MessageType.Image)
        ? MediaCacheManager.Instance.GetFilePathFromUrl(vm.mediaUrl)   // staged JPEG on disk
        : pick.Path;                                                   // video / document source

    Outbox.Add(new OutboxStore.OutboxEntry
    {
        tempId = tempId, chatId = currentChatId, text = caption ?? "",
        timestamp = now, attemptCount = 1, profileId = activeProfileId,
        kind = (int)OutboxKind.Media, attachmentKind = (int)pick.Kind,
        mediaPath = mediaPath, mimeType = pick.MimeType, fileName = pick.FileName,
        mediaUrl = vm.mediaUrl, thumbnailUrl = vm.thumbnailUrl, videoUrl = vm.videoUrl,
        aspectRatio = vm.aspectRatio, duration = vm.duration
    });

    // ---- existing optimistic UI fire (unchanged) ----
    OnLiveMessagesReceived?.Invoke(new List<MessageViewModel> { vm });

    // ---- NEW: network half on Manager.Instance (bot-switch safe), like SendTextMessage ----
    var entry = Outbox.Find(tempId);
    MonoBehaviour runner = Manager.Instance != null ? (MonoBehaviour)Manager.Instance : this;
    runner.StartCoroutine(PostMediaMessageRoutine(entry, sendCacheRoot));
}
```

`LastMessagePreview(pick, caption)` is a **new private helper** (the text path passes `text` straight into `UpdateLastMessage` at `ChatManager.cs:1359`; there is no existing preview formatter to reuse). Rule: a non-empty caption wins, else a kind label — `📷 Photo` / `🎞 Video` / `📄 {fileName}`.

### 9.2 `PostMediaMessageRoutine` (network + reconcile — shared by send and retry)

Structurally identical to `PostTextMessageRoutine`; the only differences are the off-thread encode and the kind-routed URL/body.

```csharp
private IEnumerator PostMediaMessageRoutine(OutboxStore.OutboxEntry entry, string sendCacheRoot)
{
    if (entry == null) yield break;
    var kind = (AttachmentKind)entry.attachmentKind;
    string url = WappiMediaRequestFactory.EndpointFor(kind, entry.profileId);
    if (url == null) { OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed); yield break; }

    // --- off-thread read + base64 (no frame hitch) ---
    var encodeTask = Base64Encoder.EncodeFileAsync(entry.mediaPath);
    yield return new WaitUntil(() => encodeTask.IsCompleted);
    if (encodeTask.IsFaulted || string.IsNullOrEmpty(encodeTask.Result))
    {
        Debug.LogError($"[Wappi] media encode failed for {entry.mediaPath}: {encodeTask.Exception?.Message}");
        OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
        yield break;
    }

    string body = WappiMediaRequestFactory.BuildBody(kind, entry.chatId, entry.text, entry.fileName, encodeTask.Result);

    using UnityWebRequest www = new UnityWebRequest(url, "POST");
    www.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
    www.downloadHandler = new DownloadHandlerBuffer();
    www.SetRequestHeader("Content-Type", "application/json");
    www.SetRequestHeader("Authorization", Manager.wappiAuthToken);
    www.timeout = 30;

    yield return www.SendWebRequest();

    if (www.result != UnityWebRequest.Result.Success)
    {
        Debug.LogError($"[Wappi] {url} failed: {www.error}\n{www.downloadHandler?.text}");
        OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
        yield break;
    }

    WappiSendTextResponse resp = null;
    try { resp = JsonConvert.DeserializeObject<WappiSendTextResponse>(www.downloadHandler.text); }
    catch (Exception ex) { Debug.LogError($"[Wappi] media parse failed: {ex.Message}"); }

    if (resp != null && resp.status == "done" && !string.IsNullOrEmpty(resp.message_id))
    {
        // --- identical reconcile to PostTextMessageRoutine ---
        seenMessageIds.Remove(entry.tempId);
        seenMessageIds.Add(resp.message_id);

        var cached = ChatHistoryCache.LoadHistory(sendCacheRoot, entry.chatId);
        for (int i = 0; i < cached.Count; i++)
            if (cached[i].messageId == entry.tempId)
            { cached[i].messageId = resp.message_id; cached[i].deliveryStatus = DeliveryStatus.Sent; break; }
        ChatHistoryCache.SaveHistory(sendCacheRoot, entry.chatId, cached);

        Outbox.RemoveAt(sendCacheRoot, entry.chatId, entry.tempId);
        OnMessageStatusChanged?.Invoke(entry.tempId, resp.message_id, DeliveryStatus.Sent);
    }
    else
    {
        Debug.LogWarning($"[Wappi] media non-done: {www.downloadHandler.text}");
        OnMessageStatusChanged?.Invoke(entry.tempId, entry.tempId, DeliveryStatus.Failed);
    }
}
```

The `seenMessageIds` swap is load-bearing: it makes the later server echo of this message (arriving via live sync / `chats/filter`) recognized as already-seen, so no duplicate bubble is drawn — same guarantee the text path relies on.

## 10. Retry & persistence

`RetryOutboxMessage` is unchanged. `RetryRoutine` (in `ChatManager.Outbox.cs`) gains a branch:

```csharp
private IEnumerator RetryRoutine(string tempId, OutboxStore.OutboxEntry entry)
{
    string retryCacheRoot = GetCacheRoot();   // snapshot before any yield (unchanged)
    try
    {
        if (entry.kind == (int)OutboxKind.Media)
            yield return PostMediaMessageRoutine(entry, retryCacheRoot);
        else
            yield return PostTextMessageRoutine(entry.chatId, entry.text, tempId, entry.profileId, retryCacheRoot);
    }
    finally { _retriesInFlight.Remove(tempId); }
}
```

Durability (Light, locked):
- **Image** — `mediaPath` is the `staged://image/{tempId}` disk file in `persistentDataPath`; survives restart; retry always succeeds.
- **Video / document** — `mediaPath` is the original picked path. In-session retry (tap the red bubble right after a failure) succeeds. After an app restart where the OS purged the picked file, `Base64Encoder.EncodeFileAsync` throws `FileNotFoundException` → caught → `Failed`. The bubble stays red; the user re-picks. No crash, no silent loss.

## 11. Error handling & edge cases

1. **Bot switch mid-send** — runner is `Manager.Instance`, `sendCacheRoot` is snapshotted pre-flight, and reconcile uses `Outbox.RemoveAt(sendCacheRoot, …)`, so a send started on bot A completing after a switch to bot B still clears bot A's outbox and updates bot A's cache. Mirrors text exactly.
2. **Over-cap video** — handled pre-stage (§8); never reaches `StageLocalMedia`.
3. **Off-thread encode exception** (file gone, read error) — `encodeTask.IsFaulted` → `Failed`; outbox entry retained.
4. **Empty caption** — sent as `""`; image/video accept it, document accepts it.
5. **Document `file_name`** — from `pick.FileName`, fallback `"file"`; only serialized for documents.
6. **Group recipients (`@g.us`)** — left intact (only `@c.us` stripped), matching the text path.
7. **`staged://` cache leak** — unchanged from part "b" §8: the staged entry is keyed by `tempId` and lingers after the server eventually supersedes the message. Bounded per-bot; cleanup deferred.
8. **Double-tap Send** — `AttachmentPreviewScreen.OnSendTapped` already disables `sendButton` before calling `StageLocalMedia`; the size-guard early-return re-enables it.

## 12. Testing

**EditMode unit (no device, no network):**
- `Base64EncoderTests` — round-trips known bytes to expected base64; empty file → `""`; a multi-MB temp file completes and matches `Convert.ToBase64String`.
- `WappiMediaRequestFactoryTests` — `EndpointFor` returns the right path per kind (and `null` for unsupported); `NormalizeRecipient` strips `@c.us`, preserves `@g.us` and bare ids; `BuildBody` includes `file_name` only for `Document`, omits it (no `"file_name":null`) for image/video, and always carries `recipient`/`caption`/`b64_file`.
- `OutboxEntryMediaCompatTests` — a legacy text-only JSON (`{tempId,chatId,text,timestamp,attemptCount,profileId}`) deserializes with `kind == 0` (Text); a media entry round-trips through `JsonUtility` with all fields intact.

**Manual / device:**
- Send real image, video, document to a test WhatsApp number; confirm the recipient receives it with caption and the sender bubble flips `Pending → Sent` with the real `message_id`.
- Airplane mode → bubble goes `Failed`; restore network, tap retry → `Sent`.
- Reopen the chat while a send is in flight → bubble is present (persisted) and resolves.
- Switch bots immediately after tapping Send → result lands in the originating bot's chat.
- Over-cap video → toast, no bubble.
- Verify no duplicate bubble appears when the message later returns via background sync (the `seenMessageIds` swap).

## 13. Relationship to part "b" handoff contract

Part "b" §11 sketched this work but predates two facts: (a) the implemented `StageLocalMedia` gained a `preloadedImage` parameter + `SeedImageCacheFromTexture` to kill a double HEIC decode, and assigns documents a `staged://document/{tempId}` placeholder `mediaUrl`; (b) the Wappi endpoints are now confirmed (`img/send`, `video/send`, `document/send` — not the guessed `/vid/send` `/file/send`). This spec supersedes that sketch and is written against the **actual current code**. Part "b"'s optional suggestion to replace the `staged://` entry with the real Wappi URL after success is **declined** here (§5 out-of-scope) — unnecessary churn given the server sync supersedes the message anyway.

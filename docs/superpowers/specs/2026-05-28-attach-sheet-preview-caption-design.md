# Attach Sheet — Part B: Preview Screen + Caption + Optimistic Staging

**Date**: 2026-05-28
**Status**: Approved, awaiting implementation plan
**Predecessor**: `2026-05-26-attach-sheet-design.md` (part "a" — AttachSheet UI + native pickers + `OnPicked` event)
**Successor (planned)**: Part "c" — Wappi media-upload, persistence to `ChatHistoryCache` + `Outbox`, real `message_id` swap
**Scope**: new `Assets/Scripts/Chat/AttachmentPreviewScreen.cs`, new `Assets/Editor/AttachmentPreviewScreenBuilder.cs`, additive method `ChatManager.StageLocalMedia`. No edits to `AttachSheet.cs`, `MessagesBottomPanel.cs`, `MessageItemView.cs`, or `MediaCacheManager.cs`.

## 1. Problem

`AttachSheet.OnPicked` fires an `AttachmentPick` payload (Kind + Path + FileName + MimeType + FileSizeBytes) but the only subscriber is a `Debug.Log` inside `AttachSheet.Awake()`. The user has no way to confirm what they picked, add a caption, or see it appear in the chat. The current end state of part "a" is "picker returned; nothing happened in the UI."

## 2. Goal

After a successful pick, route the user through a full-screen **AttachmentPreviewScreen** that shows the media (image / video-thumbnail / document tile), accepts an optional caption, and on **Send** stages an in-memory-only optimistic bubble in the active chat at `DeliveryStatus.Pending`. **Back** discards without staging. No persistence to `ChatHistoryCache` or `Outbox`, no Wappi upload — those are part "c".

The staged bubble must render correctly using existing image/video/document bubble views, and the wiring must transition cleanly to part "c" by replacing the in-memory-only call with the real upload + persist path.

## 3. Decisions locked from brainstorming

- **Staging semantics**: in-memory only (option b from Q1). `DeliveryStatus.Pending`, no `ChatHistoryCache` write, no `Outbox` entry. Bubble vanishes on chat switch / bot switch. No cache pollution between part "b" and part "c".
- **Single attachment per session** (option a from Q2). One pick → one preview → one staged bubble.
- **Video preview**: static first-frame thumbnail + centered play overlay glyph (option b from Q3). No playable `VideoPlayer` in the preview. Duration badge bottom-right when known.

## 4. Scope

**In scope**

- `AttachmentPreviewScreen` MonoBehaviour controller — full-screen overlay, opaque background, owns three kind-specific content panels (Image / Video / Document) and the caption + Send / Back chrome.
- `AttachmentPreviewScreenBuilder` editor menu item that constructs the screen hierarchy and wires `[SerializeField]` refs. Idempotent.
- Subscriber pattern: `AttachmentPreviewScreen` self-subscribes to `AttachSheet.OnPicked` in `OnEnable`; pre-existing `Debug.Log` subscriber in `AttachSheet.Awake()` stays for diagnostics.
- New `ChatManager.StageLocalMedia(AttachmentPick pick, string caption)` — builds the `MessageViewModel` with `type` derived from `Kind`, status `Pending`, pre-seeds `MediaCacheManager` for image/video kinds, fires `OnLiveMessagesReceived`. Does NOT write to `ChatHistoryCache`, does NOT touch `Outbox`, does NOT post to Wappi.
- Video first-frame thumbnail extraction via `NativeGallery.GetVideoThumbnail(path)`, PNG-encoded and pre-seeded into `MediaCacheManager`.
- Caption input via `DeferredDismissInputField` (same component the chat input uses) to inherit the smooth-keyboard-dismiss behavior.

**Out of scope**

- Wappi media-upload endpoint integration. `StageLocalMedia` intentionally has no network call; in part "c" we add the upload coroutine and the cache/outbox persistence.
- Multi-attachment (locked in Q2).
- Playable video in the preview (locked in Q3).
- Discard-confirmation dialog when user has typed a caption then hits Back — silent discard.
- Image editing (crop / rotate / draw).
- Recipient picker / forward-to-multiple-chats.
- PDF page-count extraction for the document tile — display zero pages, don't expose `pageCount` in the staged bubble.
- Modifications to `MessageItemView`'s rendering pipeline. Pre-seeding `MediaCacheManager` is what avoids needing them.
- Changes to `AttachSheet.cs`. Self-subscribing to its existing `OnPicked` event is the only contract.
- Changes to `MessagesBottomPanel.cs`. The plus button still toggles AttachSheet; the preview screen is downstream of `OnPicked`.

## 5. Architecture & files

**New files**

- `Assets/Scripts/Chat/AttachmentPreviewScreen.cs` — MonoBehaviour controller. `[SerializeField]` references to:
  - `AttachSheet attachSheet` — for `OnPicked` subscription
  - `GameObject root` — the full-screen overlay GameObject (toggled via `SetActive`)
  - Three content panels: `GameObject imagePanel`, `GameObject videoPanel`, `GameObject documentPanel`
  - `RawImage imagePreview` (used by both imagePanel and videoPanel — videoPanel adds a `GameObject playOverlay` on top)
  - `TextMeshProUGUI documentFileName`, `TextMeshProUGUI documentFileSize`, `Image documentIcon`
  - `DeferredDismissInputField captionField`
  - `Button sendButton`, `Button backButton`
  - `KeyboardAwarePanel captionKeyboardPanel` — a new instance scoped to the preview screen's bottom bar
  - `CanvasGroup rootCanvasGroup` — used for the fade-in/out tween
  - `List<MimeIconEntry> mimeIcons` — serializable struct list of `(string mimePrefix, Sprite sprite)` for `SpriteForMime` lookup
  - Holds private `AttachmentPick _currentPick` and `Texture2D _currentPreviewTexture` (released on close)
  - Holds private `Tween _fadeTween` to allow killing an in-flight fade if Show/Hide are called re-entrantly

- `Assets/Editor/AttachmentPreviewScreenBuilder.cs` — `[MenuItem("Automation/Build/Attachment Preview Screen")]` editor builder. Mirrors `AttachSheetBuilder.cs` structure. Idempotent (deletes existing node first). Constructs the hierarchy at the project's 1080×2400 reference resolution. Wires `[SerializeField]` references via `SerializedObject`.

**Modified files**

- `Assets/Scripts/Main/ChatManager.cs` — add public method `StageLocalMedia(AttachmentPick, string)` and three private helpers (`SeedImageCache`, `SeedVideoThumbCache`, `ReadImageAspect` / `ReadVideoMetadata`). Placed in the same region as `SendTextMessage`. ~80–100 lines including helpers.

**Untouched (intentionally)**

- `AttachSheet.cs` — no changes. We rely on its existing `OnPicked` event only.
- `MessagesBottomPanel.cs` — no changes. It already calls `attachSheet.Toggle()`; the preview screen is wired through `OnPicked`, not through the bottom panel.
- `MessageItemView.cs` — no changes to the rendering path. Pre-seeding `MediaCacheManager` with a synthetic URL is what lets the existing fast-path render the staged bubble.
- `MediaCacheManager.cs` — its existing `SaveImageToCache(url, bytes)` API is sufficient. No new methods needed.

## 6. Hierarchy & per-kind layouts

Built by `AttachmentPreviewScreenBuilder` as a top-level child of the root Canvas, immediately below the AttachSheet in sibling order so the AttachSheet renders above it during the brief moment both are alive on the same frame (the sheet finishes closing before the preview opens, so this matters only as a safety guarantee).

```
AttachmentPreviewScreen          script holder — permanently SetActive(true)
└─ Root                          RectTransform: anchors stretched (0,0)–(1,1)
                                  CanvasGroup (interactable on open, off on close)
                                  Opaque Image background, #0E1416
                                  Toggled via SetActive — starts inactive
   ├─ TopBar                     anchored top, height 88px, padding 24/24/16/16
   │   ├─ BackButton             88×88, icon "chevron-left" white, hit area inflated
   │   └─ Title                  TMP, 16pt regular #FFFFFF, "Preview" — centered
   │
   ├─ ContentArea                anchors stretched top-of-bottom-bar to under-topbar
   │   │                           CanvasGroup, holds the 3 kind-specific panels
   │   ├─ ImagePanel             anchors stretched, padding 24
   │   │   └─ RawImage           anchored centered, AspectRatioFitter (mode FitInParent)
   │   │                           the user's photo, aspect-fit
   │   │
   │   ├─ VideoPanel             anchors stretched, padding 24
   │   │   ├─ RawImage           same as ImagePanel — shows the first-frame thumbnail
   │   │   ├─ PlayOverlay        80×80 centered, semi-translucent circle #00000080
   │   │   │   └─ Image          56×56 white play-glyph icon
   │   │   └─ DurationBadge      bottom-right of RawImage, 12pt #FFFFFF on #00000080 pill,
   │   │                           padding 8/4, format "MM:SS" — only shown if duration>0
   │   │
   │   └─ DocumentPanel          anchors centered, 360×220, RoundedCorners radius 16
   │                               background #1E2528 (dark card)
   │       ├─ Icon               56×56 file-glyph color-tinted to MIME family
   │       ├─ FileName           TMP 16pt semibold #FFFFFF, single line, truncate w/ "…"
   │       └─ FileSize           TMP 12pt regular #9AA1A6, format "1.4 MB · PDF"
   │                              (or just size if no MIME)
   │
   └─ BottomBar                  anchors stretched bottom, height grows w/ caption (min 88)
                                   background #1E2528 (matches doc card)
                                   HorizontalLayoutGroup, padding 16/16/12/12, spacing 12
                                   has its own KeyboardAwarePanel
       ├─ CaptionField           flexible width, DeferredDismissInputField
       │                           TMP 14pt #FFFFFF, placeholder "Add a caption…" #6F7479
       │                           multi-line, min 1 row, max 4 rows then scrolls
       │   └─ (background)        #2A3236 rounded pill, radius = field height / 2
       │
       └─ SendButton             88×88, circle background #25D366 (WhatsApp green)
                                   white "send" glyph centered
                                   always interactable (empty caption sends just the media)
```

**Kind → panel routing** (in `AttachmentPreviewScreen.Show(pick)`):

- `Photo`, `GalleryImage` → `imagePanel` active, load `pick.Path` via `File.ReadAllBytes` → `Texture2D.LoadImage` → `imagePreview.texture`
- `GalleryVideo` → `videoPanel` active, call `NativeGallery.GetVideoThumbnail(pick.Path, ...)` → `imagePreview.texture`; query `NativeGallery.GetVideoProperties(pick.Path).duration` for duration badge
- `Document` → `documentPanel` active, set `fileName.text = pick.FileName`, `fileSize.text = HumanReadableBytes(pick.FileSizeBytes) + (mime ? " · " + ShortMime(mime) : "")`, `icon.sprite = SpriteForMime(pick.MimeType)`

`SpriteForMime` is a `List<MimeIconEntry>` of pre-authored doc icons keyed by MIME-family prefix (`application/pdf` → pdf icon, `application/vnd.openxml*` → office icon, `image/*` → image icon, `video/*` → video icon, `text/*` → text icon, fallback → generic doc icon). The MIME prefix matching is `pick.MimeType?.StartsWith(entry.mimePrefix, StringComparison.OrdinalIgnoreCase) ?? false`; first match wins; no match → generic.

**Formatting helpers** (private statics in `AttachmentPreviewScreen`):

- `HumanReadableBytes(long bytes)` — `< 1 KB` → `"<1 KB"`, `< 1024 KB` → `"{n} KB"` (no decimals), `< 1024 MB` → `"{n.n} MB"` (1 decimal), else `"{n.n} GB"` (1 decimal). Uses `InvariantCulture`.
- `ShortMime(string mime)` — substring after the last `/`, uppercased, with two compatibility overrides: `vnd.openxmlformats-officedocument.wordprocessingml.document` → `"DOCX"`, `vnd.openxmlformats-officedocument.spreadsheetml.sheet` → `"XLSX"`. Returns empty string if mime is null/empty or has no `/`.

## 7. Caption, Send, Back, lifecycle

**Open path** (`AttachmentPreviewScreen.Show(pick)`):

1. `_currentPick = pick`; route to the right panel per §6; load preview texture.
2. `captionField.text = ""` — never pre-fill from the chat input. The chat input's existing text is left alone, untouched.
3. `root.SetActive(true)`.
4. Kill any in-flight `_fadeTween`. Tween `rootCanvasGroup.alpha` 0 → 1 over 0.18s, `Ease.OutQuad`. Set `rootCanvasGroup.interactable = true` and `blocksRaycasts = true` at tween start.
5. Do NOT auto-focus the caption field. User taps it to add a caption — most sends will be caption-less, and auto-keyboard-up feels aggressive. (Matches WhatsApp.)
6. `sendButton.interactable = true`.

**Send path** (`SendButton.onClick`):

1. `sendButton.interactable = false` — prevents double-stage from a quick second tap.
2. Snapshot `caption = captionField.text?.Trim()` and `pick = _currentPick`.
3. Call `ChatManager.Instance.StageLocalMedia(pick, caption);`.
4. If caption field is currently focused, deactivate it first so the keyboard collapses before the screen fades.
5. Kill any in-flight `_fadeTween`. Tween `rootCanvasGroup.alpha` 1 → 0 over 0.18s, `Ease.OutQuad`. On complete: `root.SetActive(false)`, destroy `_currentPreviewTexture`, null `_currentPick`, `rootCanvasGroup.interactable = false`, `blocksRaycasts = false`.

**Back path** (`BackButton.onClick`):

1. Same close animation as Send, but no `StageLocalMedia` call.
2. Same cleanup (destroy preview texture, null current pick, fade out, deactivate).
3. The chat's runtime state is unchanged — no bubble appears, the chat input retains whatever text it had.

**`OnPicked` subscription lifecycle**:

- `OnEnable()`: `attachSheet.OnPicked += Show;`
- `OnDisable()`: `attachSheet.OnPicked -= Show;`
- The script lives on a permanently-enabled GameObject so the subscription stays alive across pick → preview → close → next pick cycles. Builder places the script on a thin `AttachmentPreviewScreen` empty GameObject and the actual visual content on a child `Root` GameObject — script alive always, visual toggled.

**Caption keyboard handling**:

- The caption field uses `DeferredDismissInputField` for the same finger-up dismiss behavior the chat input has.
- A dedicated `KeyboardAwarePanel` instance on the BottomBar tracks the keyboard area so the BottomBar lifts above the keyboard when the caption is focused. This panel is independent from the chat's `KeyboardAwarePanel` and is wired only to the preview screen's BottomBar `RectTransform`.
- When the preview closes, the caption field is deactivated first to ensure the keyboard collapses before the screen fades.

**Chat switching during preview**: out of scope to actively prevent. The opaque full-screen overlay blocks all touches outside the preview, so there's no UI path to switch chats / bots while preview is open. If switching somehow occurs anyway (e.g., via a future bot push notification), the staged bubble lands in whichever chat `ChatManager.currentChatId` resolves to at the moment Send is tapped. Acceptable risk for part "b"; revisit if a switch path appears.

## 8. Staging API on ChatManager

New public method:

```csharp
public void StageLocalMedia(AttachmentPick pick, string caption)
{
    if (string.IsNullOrEmpty(currentChatId)) return;
    if (pick == null || string.IsNullOrEmpty(pick.Path)) return;

    string tempId = "staging_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    seenMessageIds.Add(tempId);

    var vm = new MessageViewModel
    {
        messageId      = tempId,
        chatId         = currentChatId,
        senderName     = "Me",
        isIncoming     = false,
        timestamp      = now,
        text           = caption ?? "",
        mimeType       = pick.MimeType,
        fileName       = pick.FileName,
        fileSize       = pick.FileSizeBytes,
        deliveryStatus = DeliveryStatus.Pending,
    };

    switch (pick.Kind)
    {
        case AttachmentKind.Photo:
        case AttachmentKind.GalleryImage:
            vm.type        = MessageType.Image;
            vm.mediaUrl    = SeedImageCache(pick.Path, tempId);
            vm.aspectRatio = ReadImageAspect(pick.Path);
            break;

        case AttachmentKind.GalleryVideo:
            vm.type        = MessageType.Video;
            vm.mediaUrl    = SeedVideoThumbCache(pick.Path, tempId);
            vm.videoUrl    = "file://" + pick.Path;       // unused in part b; reserved for part c upload swap
            (vm.aspectRatio, vm.duration) = ReadVideoMetadata(pick.Path);
            break;

        case AttachmentKind.Document:
            vm.type = MessageType.Document;
            // No mediaUrl/videoUrl — document bubble uses fileName + fileSize + mimeType.
            break;
    }

    OnLiveMessagesReceived?.Invoke(new List<MessageViewModel> { vm });
}
```

**Private helpers**:

```csharp
private string SeedImageCache(string localPath, string tempId)
{
    string syntheticUrl = $"staged://image/{tempId}";
    try
    {
        byte[] bytes = System.IO.File.ReadAllBytes(localPath);
        MediaCacheManager.Instance.SaveImageToCache(syntheticUrl, bytes);
    }
    catch (System.Exception ex)
    {
        Debug.LogWarning($"[ChatManager] SeedImageCache failed for {localPath}: {ex.Message}");
    }
    return syntheticUrl;
}

private string SeedVideoThumbCache(string localPath, string tempId)
{
    string syntheticUrl = $"staged://thumb/{tempId}";
    Texture2D thumb = NativeGallery.GetVideoThumbnail(localPath);
    if (thumb == null) return syntheticUrl;   // bubble's existing missing-thumb placeholder renders
    try
    {
        byte[] png = thumb.EncodeToPNG();
        MediaCacheManager.Instance.SaveImageToCache(syntheticUrl, png);
    }
    finally { UnityEngine.Object.Destroy(thumb); }
    return syntheticUrl;
}

private float ReadImageAspect(string path)
{
    try
    {
        byte[] bytes = System.IO.File.ReadAllBytes(path);
        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(bytes)) { UnityEngine.Object.Destroy(tex); return 1.0f; }
        float ratio = tex.height > 0 ? (float)tex.width / tex.height : 1.0f;
        UnityEngine.Object.Destroy(tex);
        return ratio;
    }
    catch { return 1.0f; }
}

private (float aspect, int durationSec) ReadVideoMetadata(string path)
{
    try
    {
        var props = NativeGallery.GetVideoProperties(path);
        float aspect = props.height > 0 ? (float)props.width / props.height : 1.0f;
        int durationSec = (int)(props.duration / 1000);   // GetVideoProperties returns ms
        return (aspect, durationSec);
    }
    catch { return (1.0f, 0); }
}
```

**Why pre-seed instead of changing the renderer**:

- The image cache fast-path at `MessageItemView.cs:1696` checks `IsImageCached(mediaUrl)` and loads from disk instantly if true. Pre-seeding hits this path.
- For video, `ShowSmartThumbnail` does the same against `vm.mediaUrl`.
- One bot-scoped cache file per staged media. Cleaned up by the existing bot-purge logic.
- Zero changes to the 3000-line `MessageItemView`.

**Trade-off**: a `staged://image/{tempId}` cache entry remains on disk after the staged bubble is replaced by part "c"'s real upload (which gets its own real-URL keyed entry). This is a small disk-space leak per staged message, bounded by the active bot's lifetime. Acceptable for part "b"; we can add a cleanup pass in part "c" if it becomes a problem.

## 9. Editor builder & sprite assets

`AttachmentPreviewScreenBuilder.cs` follows the project's editor-builder pattern (matches `AttachSheetBuilder.cs`):

- `[MenuItem("Automation/Build/Attachment Preview Screen")]`
- Idempotent: deletes any existing `AttachmentPreviewScreen` node under the root Canvas before rebuilding.
- Constructs the hierarchy from §6 at 1080×2400 reference resolution with anchors/pivots/sizes laid out in canvas px.
- Adds the `AttachmentPreviewScreen` component to the script-holder node, the `KeyboardAwarePanel` to the BottomBar, the `DeferredDismissInputField` to the caption field, the `CanvasGroup` to the Root node.
- Wires `[SerializeField]` references via `SerializedObject` / `SerializedProperty.objectReferenceValue` + `ApplyModifiedProperties`.
- Locates the existing `AttachSheet` in the scene via `Object.FindObjectsByType<AttachSheet>` and wires it into the `attachSheet` ref. If missing, logs error and aborts (matches `AttachSheetBuilder`'s safety pattern).
- Leaves sprite slots empty for the user to drop authored assets:
  - `Sprite chevronLeftIconSprite` (back button)
  - `Sprite sendIconSprite` (send button glyph)
  - `Sprite playIconSprite` (video play overlay)
  - `Sprite docIconPdf`, `docIconOffice`, `docIconImage`, `docIconVideo`, `docIconText`, `docIconGeneric` — the MIME-family icon set, pre-populated as `MimeIconEntry` rows in the `mimeIcons` list with prefixes set and sprite refs empty.

**Sprite assets** authored externally as 1× PNGs and placed under `Assets/Sprites/AttachmentPreview/`. Import settings: `Sprite (2D and UI)`, `Filter Mode: Bilinear`, `Compression: None`.

## 10. Risks & validation

1. **Pre-seed cache key collision**: `staged://image/{tempId}` uses `staging_<unix-ms>` as the temp id; collision requires two staged messages in the same millisecond. Acceptable.
2. **`NativeGallery.GetVideoThumbnail` returns null on some Android devices** (per the plugin's docs). Mitigation: graceful fallback — the bubble's existing missing-thumb placeholder renders. Video still appears in chat with file name + duration badge.
3. **`NativeGallery.GetVideoProperties` is sync and can stall on huge videos**. Mitigation: the picker already returned the path, so the user has confirmed selection — a brief stall is acceptable. If it becomes a real problem, move it to a coroutine in part "c" alongside the upload.
4. **Caption field keyboard fights with the chat screen's `KeyboardAwarePanel`** which is already in the scene. Mitigation: the preview screen uses its own dedicated `KeyboardAwarePanel` wired only to its own BottomBar `RectTransform`. The chat screen's keyboard panel sits behind the opaque preview overlay and receives no touch events. Validate on both iOS and Android during execute.
5. **`OnLiveMessagesReceived` subscribers might re-trigger sync logic** that doesn't expect a `messageId` starting with `staging_`. Mitigation: the existing `seenMessageIds.Add(tempId)` guards against duplicate ingestion. Same pattern as `SendTextMessage`'s `sending_` ids — proven safe.
6. **Bubble lifecycle on chat switch**: staged bubbles are in `MessageListView`'s in-memory list only (no cache write). Switching chats clears the list naturally. Switching bots clears it too. Staged bubble vanishes silently — matches the in-memory-only decision from Q1.
7. **`DeferredDismissInputField` behavior on a non-chat screen**: the component was authored for the chat input. Re-using it on the preview's caption field should work transparently (it's a `TMP_InputField` subclass with finger-up dismiss), but the focus-restore-after-Send dance in `MessagesBottomPanel.KeepKeyboardOpenRoutine` is chat-screen-specific and does NOT apply here. Validate the caption field deactivates cleanly when Send/Back is tapped.
8. **Memory leak on `_currentPreviewTexture`**: `Texture2D.LoadImage` and `NativeGallery.GetVideoThumbnail` both allocate textures that must be explicitly destroyed. Tracked via the `_currentPreviewTexture` field, destroyed on close (Send or Back). Also destroy on `OnDisable` defensively in case the screen is disabled mid-preview by a scene event.
9. **Tap-Play on a staged video bubble**: `vm.videoUrl = "file://" + pick.Path` is set on the staged video so part "c" can swap it for a Wappi URL. Until then, if the user taps Play on the staged bubble, `VideoController.PlayVideo("file://...", aspect)` runs. Unity's `VideoPlayer` supports `file://` URIs on both iOS and Android, so playback should "just work" — an incidental free preview. Validation: confirm during execute. If `VideoController` rejects non-`http` URLs (e.g., due to existing `targetUrl.StartsWith("http")` guards in its internals), fall back to setting `vm.videoUrl = ""` and accept that tap-Play on a Pending video bubble is a no-op until part "c" lands.

## 11. Handoff contract to part "c"

Part "c" replaces the body of `ChatManager.StageLocalMedia` (or adds a sibling method) to:

1. Keep the optimistic `OnLiveMessagesReceived` fire from part "b" as-is.
2. Add `ChatHistoryCache.SaveHistory` for the staged message (now persisted).
3. Add `Outbox.Add` with a new media-bearing `OutboxEntry` variant (extend the existing record with `localPath`, `mimeType`, `caption`).
4. Add the Wappi media-upload coroutine (`POST` to `https://wappi.pro/api/sync/message/img/send`, `/vid/send`, `/file/send` per the Wappi docs — actual endpoint paths to be confirmed during part "c" research).
5. On upload success, perform the same temp-id swap as `PostTextMessageRoutine` does for text, replacing `vm.mediaUrl` with the real Wappi URL and flipping `deliveryStatus` to `Sent`.
6. Optional: delete the `staged://image/{tempId}` cache entry after the real URL cache entry is established, to address the trade-off noted in §8.

The preview screen, the editor builder, and the `OnPicked → Show → StageLocalMedia` wiring are untouched by part "c". The only file part "c" needs to edit on the part-"b" surface is `ChatManager.cs`.

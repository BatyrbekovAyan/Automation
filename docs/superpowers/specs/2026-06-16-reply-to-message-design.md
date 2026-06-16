# Reply to message (WhatsApp-style) — design

Status: approved design, ready for implementation planning
Date: 2026-06-16
Scope: WhatsApp / Wappi only. Telegram is out of scope for v1.

## 1. Overview

Add the ability to reply to a specific message, WhatsApp-style:

- **Triggers**: swipe-right on a bubble (primary) and long-press → action menu with a **Reply** entry.
- **Compose preview**: a "replying to …" bar above the input showing the quoted sender + one-line snippet, with an ✕ to cancel.
- **Quoted card in the bubble**: a tinted block at the top of the bubble with a colored accent bar, the original sender's name, a one-line snippet, and a 32px thumbnail for image/video (icon + label for audio/voice/document). Tapping it scrolls to the original and flashes it.
- **Reply payload is text.** Replying *with* media is out of scope for v1; replying *to* media (with a thumbnail in the quote) is in scope.

The quoted content is resolved **locally** from the in-memory chat cache by message id (primary), falling back to the API-embedded `reply_message` snapshot, then a generic placeholder.

### In scope
- Text replies on WhatsApp/Wappi.
- Rendering quoted cards for incoming and outgoing replies, including media thumbnails in the quote.
- Swipe + long-press triggers, compose preview bar, scroll-to-original with highlight.

### Out of scope (future)
- Telegram replies (separate send path).
- Replying *with* media/attachments.
- Auto-paginating older pages to locate a quoted original that isn't loaded.
- A buffering store for live/webhook-delivered replies whose original isn't loaded (mirror of `ReactionStore`) — only needed once the n8n live-reply transport lands.

## 2. UX reference

See the approved mockup from the brainstorming session: quoted card inside the bubble (accent bar + sender + snippet + optional thumbnail), reply preview bar above the composer with ✕, swipe-right gesture revealing a reply arrow, and a long-press action menu with **Reply**.

## 3. Architecture

Replies flow through the existing four-layer message pipeline. A new field added to one layer must be carried through every layer by hand (there is no auto-mapping):

```
wappi messages/get JSON
  └─ RawMessage         (+ isReply, replyMessage:JToken)            [JsonConvert]
       └─ Normalize()   → NormalizedMessage  (+ flat quoted* fields, resolved via ReplyParser)
            └─ CreateViewModel() → MessageViewModel (+ flat quoted* fields)   [JsonUtility-persisted]
                 └─ MessageItemView  (renders the quoted card — pure render, no cache access)
```

Plus a new pure helper:

- **`ReplyParser`** (static, unit-tested, sibling to `ReactionParser` / `MediaBubbleSize`) — turns `(isReply, replyMessage JToken, cache-resolver delegate)` into the flat quoted preview fields. Resolution order: **cache-by-id → embedded snapshot → generic placeholder.** Never throws.

### Key constraint that drives the data-model choices
`RawMessage` is **only** ever deserialized via Newtonsoft `JsonConvert` (`ChatManager` lines 447 / 952 / 1025), so a `JToken replyMessage` field is safe and idiomatic (it sits beside the existing `body` / `s3Info` / `mediaInfo` JTokens). `MessageViewModel`, by contrast, **is persisted by Unity `JsonUtility`** (`ChatHistoryCache.SaveHistory/LoadHistory`), which cannot serialize `JToken` — so the quoted snapshot **must be flattened to primitives** (`string` + the `MessageType` enum, both of which round-trip through `JsonUtility`). Putting a `JToken` on `MessageViewModel` would corrupt the on-disk cache.

### Resolve once, render pure (resolves a design contradiction)
The quoted fields are resolved **exactly once, in `ChatManager.Normalize`**, and stored flat on the VM. `MessageItemView` only *reads* those fields — it never calls `ReplyParser` or touches `ChatHistoryCache` at render time (that would re-run on every recycle and can't be persisted). Consequence to accept consciously: the re-pagination "already-seen" path only runs `RefreshCachedMessageMedia`, so a quote that first resolved to a placeholder (original not yet paged in) will **not** auto-upgrade when the original later loads. This is acceptable v1 behavior and is documented; see §8 decision D4.

## 4. Data model changes

### 4.1 `RawMessage` (`Assets/Scripts/Chat/RawMessage.cs`)
Add two fields (keep the existing `stanzaId` — it stays the reaction target):
```csharp
[JsonProperty("isReply")]      public bool   isReply;
[JsonProperty("reply_message")] public JToken replyMessage;
```
Do **not** add `quoted*` fields here. `JToken` is correct (Newtonsoft-only path).

> ⚠ **Field names unverified** — `isReply` / `reply_message` and the snapshot sub-fields (`id` / `body` / `type` / `caption` / `file_name` / `JPEGThumbnail` / `senderName`) come from the public Wappi docs, not a captured payload. See §7 Phase 0.

### 4.2 `NormalizedMessage` (`Assets/Scripts/Chat/NormalizedMessage.cs`)
Add five flat fields (transient class, never serialized — enum is fine):
```csharp
public string      quotedMessageId;
public string      quotedSenderName;
public string      quotedText;
public MessageType quotedType;
public string      quotedThumbnailUrl;
```

### 4.3 `MessageViewModel` (`Assets/Scripts/UI/MessageViewModel.cs`)
Mirror the same five fields as **public** (`string` + `MessageType quotedType`). `JsonUtility` serializes public enums (as int) and these strings, exactly like the existing `reactions` list — so they persist through `ChatHistoryCache`. **No `JToken` here.**

## 5. `ReplyParser` (new pure helper)

`Assets/Scripts/Chat/ReplyParser.cs`. Static, no MonoBehaviour, fully unit-testable.

```csharp
public static class ReplyParser
{
    // resolver: messageId -> cached MessageViewModel (or null). Provided by ChatManager.
    public static void Resolve(
        RawMessage raw,
        System.Func<string, MessageViewModel> resolveById,
        System.Func<string, MessageType> parseType,
        out QuotedPreview preview);   // or out the five fields
}
```

Behavior:
1. If `raw.type == "reaction"` or there's no reply indicator (`!isReply` and `replyMessage == null` and `stanzaId` empty) → all quoted fields empty. (The reaction guard is also enforced at the call site; double-guarded.)
2. Extract the quoted id from `replyMessage["id"]` (fall back to `stanzaId`).
3. **Cache-by-id**: `resolveById(id)`. If found, build the preview from the live VM (sender, text/caption, type, thumbnail/media url) — this gives the freshest content and a tappable target.
4. **Embedded snapshot fallback**: if not cached, read from `replyMessage` — `type` → `parseType(...)`, `quotedText` = `caption` for media else `body`, `quotedThumbnailUrl` from the snapshot thumbnail if present.
5. **Placeholder**: if neither resolves, `quotedText` = "" / generic, `quotedType` = `Unknown`, no thumbnail. Card still renders (text-less originals are a valid state).
6. **Sender label**: if the quoted target is `fromMe` (outgoing), `quotedSenderName` = "You" and the card uses the outgoing accent color — **not** "Me" through `GetSenderColor` (which would hash a random group color). Otherwise the real sender name + `GetSenderColor` input.

`MessageType quotedType` is mapped through the **same** `ParseMessageType` switch the pipeline already uses (fed the snapshot's `type` string), so `"image"/"video"` → thumbnail path, `"ptt"` → `Voice`, `"audio"/"document"` → icon + label, `"chat"` → text, unknown → `Unknown` (never throws).

## 6. `ChatManager` changes

### 6.1 `Normalize(RawMessage raw)` (~lines 1148–1292)
At the **end** of `Normalize`, just before `return msg;`, **gated by `if (raw.type != "reaction")`**, call `ReplyParser.Resolve(...)` and copy the five quoted fields onto `msg`. The reaction guard is required *inside* `Normalize` because reaction stanzas reach `Normalize` before the `MessageType.Reaction` `continue` in the three loops — without the guard a reaction carrying a `reply_message` would trigger a `StageServerThumbnail` disk write for a message that's about to be discarded.

For media quotes, prefer the existing `thumb://{id}` already in cache (via `ThumbnailKeyResolver`) over staging a second copy from the snapshot; only `StageServerThumbnail(quotedId, …)` from the snapshot when the real original is not present (avoids a future key-collision if a thinner snapshot has different bytes for the same id).

### 6.2 `CreateViewModel(NormalizedMessage msg)` (~lines 1100–1121)
Copy the five `quoted*` fields into the new `MessageViewModel` initializer. **This is the only norm→VM copy on the server path — miss it and quotes silently never reach the UI (the classic four-layer drop).**

### 6.3 `FindActiveById` helper (new)
There is no existing get-by-id API. Add:
```csharp
private MessageViewModel FindActiveById(string id)  // linear scan of _activeChatCache
```
modeled on `ReactionStore.FindById`. **Null-guard `_activeChatCache`** (it is null during cold load, only assigned at lines 614/745); when null, fall back to `ChatHistoryCache.LoadHistory(GetCacheRoot(), chatId)` or straight to the snapshot. This delegate is what `Normalize` passes to `ReplyParser`. Note the 100-message cap (line 604) + 50/page window: originals older than the window won't be in cache — the snapshot fallback is **mandatory**, not optional.

## 7. Send + compose state

### 7.1 Compose state — `ChatManager.Outbox.cs` partial
Reply state lives with the outbox/retry state (not on the bottom panel):
```csharp
private MessageViewModel _replyTarget;
public event System.Action<MessageViewModel> OnReplyTargetChanged;  // null payload == cleared
public void BeginReply(MessageViewModel target) { _replyTarget = target; OnReplyTargetChanged?.Invoke(target); }
public void CancelReply()                       { _replyTarget = null;  OnReplyTargetChanged?.Invoke(null); }
```

### 7.2 `WappiSendTextRequest` (~lines 1714–1719)
```csharp
[JsonProperty("quoted_message_id", NullValueHandling = NullValueHandling.Ignore)]
public string quotedMessageId;
```
The existing `JsonConvert.SerializeObject` at 1604 passes **no settings**, so use the **per-property attribute** (Newtonsoft honors property-level `NullValueHandling` without serializer settings). The key is omitted entirely when the message isn't a reply; the call site is unchanged.

> ⚠ **Send param name unverified** (`quoted_message_id` is the whatsapp-web.js/Wappi convention). Confirm in §7 Phase 0 before shipping.

### 7.3 Send routine threading (`SendTextMessage` / `SendTextMessageRoutine` / `PostTextMessageRoutine`)
- Keep `MessagesBottomPanel`'s `SendTextMessage(text)` call site unchanged; read `_replyTarget` from `ChatManager` inside the routine.
- In `SendTextMessageRoutine`, **snapshot the reply target into locals before the first `yield`** (same discipline as the existing `sendCacheRoot`/`tempId`/`now` snapshot at 1542–1546).
- Stamp the optimistic `instantMessage` VM (1550–1561) with the quoted fields so the bubble shows the quote instantly, and write `quotedMessageId` into the `OutboxEntry` (1572–1580).
- After capturing the snapshot, **`CancelReply()`** so the next message isn't a reply.
- Add a `string quotedMessageId` parameter to `PostTextMessageRoutine` (1593) and set `requestData.quotedMessageId` at 1603. `PostTextMessageRoutine` is **shared by first-send and retry**, so threading it here is what makes a retried reply still quote.

### 7.4 `OutboxStore.OutboxEntry` (~lines 28–50)
Add `public string quotedMessageId;` to the append-only block. `JsonUtility` back-compat: missing field on old persisted entries deserializes to `null` = "not a reply" (safe). `RetryRoutine` threads `entry.quotedMessageId` into `PostTextMessageRoutine`.

### 7.5 Reply preview bar — `MessagesBottomPanel`
Subscribe to `OnReplyTargetChanged` in `OnEnable` / unsubscribe in `OnDisable`; show/hide a preview bar (accent + sender + snippet + ✕). The ✕ calls `ChatManager.CancelReply()`. The send button needs **no change** — the quote is read from `ChatManager` state inside the routine.

**Placement caution**: `ExpandableInput.cs` reads `minHeight = bottomPanelRect.rect.height` **once in `Start`** and drives input auto-grow off it; `KeyboardAwarePanel` stomps runtime offsets every frame (project memory). Place the preview bar as a **sibling above** `bottomPanelRect` (a layout child of the panel root), **not** as a child of the ExpandableInput-controlled rect and not via a runtime `anchoredPosition` nudge. Verify keyboard open/close with the bar visible.

## 8. Lifecycle correctness (the blocker-class edge cases)

| # | Issue | Resolution |
|---|-------|-----------|
| L1 | **Chat-switch leaks the reply target** into the next chat (panel stays active across chat→chat, so `OnDisable` never fires). | Call `CancelReply()` **inside `OpenChat`** (≈367–380) before `currentChatId` is reassigned, alongside the existing `seenMessageIds.Clear()`. |
| L2 | **Quoting a still-Pending (`sending_…`) message** sends an invalid id to Wappi. | Render the quoted **card** locally from the temp-id, but in `SendTextMessageRoutine` only set the wire `quotedMessageId` when the captured target id does **not** start with `"sending_"`. (See D1 for the product choice.) |
| L3 | **Temp-id→real-id swap orphans quotes** that referenced the original's temp-id. | When rewriting a temp-id→server-id (`PostTextMessageRoutine` 1642–1652 and ghost-recovery 481–672), also scan `_activeChatCache`/`cachedList` for any VM whose `quotedMessageId == oldTempId` and rewrite it to the new id. |
| L4 | **Re-pagination discards freshly-resolved quoted fields** (already-seen path runs only `RefreshCachedMessageMedia`). | Accept "quote preview frozen at first normalize" for v1 (D4), documented. Optional future: extend the refresh to re-copy quoted fields when `quotedText` is empty/placeholder. |

## 9. Bubble rendering — `MessageItemView` + prefabs

> Hierarchy correction: `MessageItemView` + `HorizontalLayoutGroup` sit on the **root** (the left/right alignment row). The `VerticalLayoutGroup` the quoted card goes into is the **`Bubble` child** (root child index 2).

### 9.1 Serialized refs (Header "Reply Quote")
Add `[SerializeField] private` refs (new fields private per `ui-scripts.md`): `GameObject quotedCard; Image quotedAccentBar; TextMeshProUGUI quotedSenderText; TextMeshProUGUI quotedSnippetText; Image quotedThumbnail;` plus a `Button` on the card (or a `SwipeToReply`-independent tap). Wire in **both** prefabs.

### 9.2 `RenderQuotedCard(vm)` — called near the top of `Bind` (after sender-name logic, before the per-type media branches)
- **Recycled reset**: when `vm.quotedMessageId` is empty, `quotedCard.SetActive(false)` and free any prior quoted texture at the **top** of the method (mirrors the sender-name / download-arrow reset). Otherwise a re-bound bubble keeps a stale quote.
- Set `quotedSenderText` (with "You"/`GetSenderColor` logic from `ReplyParser`), `quotedSnippetText` (sanitized + truncated), and show/hide `quotedThumbnail` by `quotedType`.
- Wire the card tap → `MessageListView.ScrollToMessage(vm.quotedMessageId)`.

### 9.3 Sibling order — `ReorderBubbleSiblings` (841–883)
Insert the card **after** the sender-name block and **before** the `orderedMedia[]` loop:
```csharp
if (quotedCard != null && quotedCard.activeSelf) quotedCard.transform.SetSiblingIndex(currentIndex++);
```
Yields **senderName → quote → media → caption**. Do **not** add the card to `orderedMedia[]` (those are mutually-exclusive media-region objects; the quote coexists with media).

### 9.4 Layout clamps — `ApplyDynamicLayout` (885–1212)
- **Width**: the Bubble VLG sets `childForceExpandWidth = true` (≈891), so give the card a `LayoutElement.preferredWidth` clamp (mirror the 444px download-card pattern) so it neither blows the bubble out nor drags short text bubbles wider (also feeds `MirrorSize` on the outgoing green bubble — verify there).
- **Negative spacing**: no-caption image/video uses `spacing = -42` (≈1110) and audio `-34` (≈1073) to pull the time chip up. With a top card present, that negative spacing also pulls the **quote onto the media**. When `quotedCard.activeSelf`, clamp these spacings to **≥ 0** (or add explicit top padding) for image/video/audio. **This is the single most likely visual regression** — needs an in-Editor visual check.
- Re-check the sender-name `minHeight += 56f` hack (≈1096–1100), which assumes sender-name is the only top child, when both a group sender name and a quote show on a no-caption media bubble.

### 9.5 Thumbnail loading & disposal
`DisposeOwned()` runs at the head of every `ApplyTextureAspectFill` (≈3243) and destroys everything in the shared `_ownedDisposables` list. **Do not** route the quoted thumbnail through `TrackOwned` — it would be destroyed by the next main-media decode. Load via `ThumbnailKeyResolver.Resolve(...)` + `MediaCacheManager.LoadImageFromCache(...)` (the path `DisplayMedia` uses) into a **separate `_quotedDisposables` list** freed in `OnDestroy`/next `RenderQuotedCard`, or reuse the cached texture without minting a new owned sprite. If not cached → type icon + label; **never** fire a network fetch from the card. Quoting downloading/expired media → icon + label (cheap card).

### 9.6 Snippet sanitization
Run the snippet through `UnicodeEmojiConverter.ConvertRealEmojisToSprites(snippet, MissingEmojiMode.Hide)` (emoji parity with `senderName`), then **leading-trim the prepended ZWS** before length-truncation. One line via `textWrappingMode = NoWrap`, `overflowMode = Ellipsis`, `maxVisibleLines = 1` (the `documentNameText`/`linkPreviewTitle` pattern). **Never** the inline-time-reservation path. May need `SubscribeToEmojiReady` for missing glyphs.

### 9.7 Prefab changes (both `MessageTextIncoming.prefab` and `MessageTextOutgoing.prefab`)
Add a `QuotedCard` child under `Bubble` (seed it just after `SenderName` so inactive recycled bubbles look right pre-`Bind`), with an inner layout (accent bar | text column [sender / snippet] | 32px thumbnail). Wire the five serialized refs into the `MessageItemView` component in **both** prefabs. Build via the editor-builder pattern (`.claude/rules/editor-scripts.md`) or in the open Editor — hand-editing LFS YAML risks dangling fileIDs. Commit `.prefab` + `.meta` together. Expect a benign serialized-`currentVm` diff (the inline VM grows by the new fields).

## 10. Triggers

### 10.1 `SwipeToReply` (new, `Assets/Scripts/Chat/SwipeToReply.cs`)
**Model on `AudioWaveform.cs`, not `SwipeToBack`.** `AudioWaveform` is already a per-bubble drag child inside the ScrollRect that does the right arbitration. Requirements:
- `Awake`: cache `_parentScroll = GetComponentInParent<ScrollRect>()` (a `SnappyFlickScrollRect`); needs a raycast-target Graphic to receive pointer events (add a transparent `Image` if not on the bubble background).
- `OnBeginDrag`: classify `Mathf.Abs(d.y) > Mathf.Abs(d.x)` → **forward all four** `IInitializePotentialDrag/IBeginDrag/IDrag/IEndDrag` calls to `_parentScroll` (forwarding only `OnDrag` makes `SnappyFlickScrollRect`'s flick-velocity math read a stale `dragStartPosition` and mis-flick). Else claim horizontal.
- Accept **right-only** (`d.x > 0`) for the reveal; left-swipes route to parent.
- Horizontal branch: translate the inner **`Bubble` RectTransform** (not the root — the root is positioned by the content VLG) and reveal a reply arrow.
- `OnEndDrag`: past a distance/velocity threshold (borrow `SwipeToBack`'s `dragStartTime`/`dragStartPos` flick math) → `ChatManager.BeginReply(BoundVm)` + haptic, then snap the bubble back.
- **Never** toggle `ScrollRect.vertical` (that's a per-bubble component mutating shared state — it would freeze scrolling for all bubbles).
- Read **live `BoundVm`** (never cache the VM in `Awake`); reset transform offset/flags at the **end of `Bind`** (bubbles aren't pooled across chats but the same instance is re-`Bind`-ed in place for tail-merging).

### 10.2 Long-press → `MessageActionMenu` (new)
- **Detection**: `DelayedFingerUpAction.OnPress` (fires in `OnPointerDown`) starts a hold timer (~400–500ms); cancel it in `OnPointerUp`/`OnBeginDrag`; fire only if movement < tolerance (reuse `DragShield`'s `maxMoveFromDown` idea) and `!ScrollClickBlocker.IsBlocking` (so a fling-stop touch doesn't open the menu).
- **Menu**: reuse the `PopupUI` convention (`Show`/`Hide`/`AbsorbEvents`/`WireFingerUp`) — backdrop Image + a Content card with a **Reply** row (structured to hold Copy/Forward/React later). Reply → `ChatManager.BeginReply(BoundVm)`.
- **Precedence on media rects**: long-press menu vs tap-to-open `PhotoViewer`/`VideoController` — long-press wins via the hold timer; tap (short) still opens media. Confirm the hold duration doesn't collide with double-tap-to-select-word on text.

## 11. Scroll-to-original + highlight — `MessageListView` / `MessageItemView`

### 11.1 `MessageListView.ScrollToMessage(string id)` (new, after `ScrollSeparatorToTop` ≈965–989)
- **No id→view index exists** — linear-scan `content` children for `MessageItemView.BoundVm.messageId == id` (same scan as `HandleMessageRemoved` 390–408; null-guard skips date separators / spacers).
- **Target math**: generalize `ScrollSeparatorToTop` (pivot/scale-agnostic): `Canvas.ForceUpdateCanvases()`, `GetWorldCorners`, take top-left, `InverseTransformPoint` into content-local, `distanceFromTop = clamp(contentRt.rect.yMax - localTopY, 0, scrollableH)`, then **center** by subtracting `viewportH * 0.4` before normalizing (clamp ≥ 0), `verticalNormalizedPosition = 1 - distanceFromTop/scrollableH`. Guard `scrollableH <= 1f` → `vNP = 0` and return.
- **Animate**: reuse the `HandleScrollToBottomClicked` DOTween pattern (285–308) — zero `scrollRect.velocity` first, `DOTween.To` on `verticalNormalizedPosition` (~0.3s OutCubic). Store `_scrollToMessageTween`; **Kill it in `OnDisable` and `OnChatSelected`** like `_scrollToBottomTween`.
- **Pagination guard**: programmatic `vNP` writes fire `OnScroll` → can trip `LoadNextPage`. Hold `isLoadingData = true` across the tween (precedent: `PlaceUnreadSeparatorAndLand` ≈866), restore on complete.
- **Not instantiated**: id not found → **silent no-op** (v1, D5). Don't auto-paginate.
- On complete → `view.FlashHighlight()`.

### 11.2 `MessageItemView.FlashHighlight()` (new, ≈1546–1557)
Use a **transient overlay `Image` child** (raycastTarget = false) `DOFade` 0 → ~0.25 → 0 — **not** a `bubbleBackground.color` tween. `bubbleBackground.color` is owned by `UpdateBubbleVisuals` (3388–3452) and is `Color.clear` for transparent sticker/jumbo-emoji bubbles, so a color tween would be invisible there and could be stomped mid-pulse. The overlay works uniformly and is immune to `Bind`. Expose as a public method so the list view never reaches into bubble internals.

## 12. Pre-implementation verification — **Phase 0 (must complete before the ReplyParser field mapping is trusted)**

The Wappi wire shapes are **unverified against a real payload**:
1. **Capture a real reply payload.** The `#if UNITY_EDITOR` block at `ChatManager.cs` ≈1004–1009 dumps `messages/get` responses to `persistentDataPath/response.txt`. Send yourself a reply in a test chat, open it in the Editor, and read the dump. Pin: the exact key for the reply flag (`isReply`?), the quoted object key (`reply_message`?), its sub-fields (`id`/`body`/`type`/`caption`/`file_name`/`JPEGThumbnail`/`senderName`?), and whether `reply_message.id` is in the same id space as `RawMessage.id` (so the cache-by-id exact match works) or a `stanzaId`-style id needing normalization.
2. **Confirm the send param name** for `/message/send` (`quoted_message_id` vs `quotedMessageId` vs `reply_to` vs a nested object) against the Wappi Postman collection / dashboard / support.

Build `ReplyParser` tolerant of missing keys (null → fall through to placeholder), then pin the names once captured. If `reply_message` turns out to be a bare id string, the resolver relies entirely on cache-by-id + placeholder (no embedded snapshot for media thumbnails).

## 13. Decisions & defaults (override at spec review)

- **D1 — Reply to a not-yet-Sent (Pending/Failed) message**: **default = disable the reply trigger on bubbles whose `deliveryStatus` is `Pending`/`Failed`** (swipe + menu no-op). This eliminates the temp-id wire-suppression (L2) and the temp-id→real-id quote migration (L3) blockers for v1, at the cost of not being able to reply to a message that's still sending. *Alternative*: allow it, render the local preview, suppress the wire id while pending, and migrate on ack (L2 + L3 implemented). Recommended: **disable for v1**; revisit if it feels wrong on device.
- **D2 — Quote sender label for your own messages**: show **"You"** + outgoing accent (not "Me" + hashed color).
- **D3 — Quoted card width**: **fixed/clamped `preferredWidth`** (WhatsApp-style), not content-hug.
- **D4 — Quote freshness**: resolved once at `Normalize`; a placeholder quote (original not yet paged in) does **not** auto-upgrade on later pagination. Documented, accepted for v1.
- **D5 — Tap a quote whose original isn't loaded**: **silent no-op** (no toast, no auto-pagination) for v1.
- **D6 — Highlight**: transient overlay-Image flash, centered landing (`distanceFromTop − viewportH*0.4`).

> If D1 is left at the recommended default, the L2/L3 rows in §8 become "not applicable for v1" (no temp-id can be a reply target), simplifying the implementation significantly.

## 14. Testing (EditMode, `Assets/Tests/Editor/Chat/`)

Pure/unit (no MonoBehaviour), sibling to `ReactionParserTests`:
1. **`ReplyParser` resolution matrix**: cache HIT by exact id → cached preview; cache MISS + snapshot present → snapshot preview; cache MISS + snapshot absent → placeholder; `resolveById` returns null / cache null (cold load) → no throw, falls to snapshot/placeholder.
2. **`ReplyParser` type mapping**: snapshot `type` → `ParseMessageType` (`image`/`video` → media, `ptt` → Voice, `audio`/`document` → icon+label, `chat` → text, unknown → Unknown, no crash).
3. **`ReplyParser` sender label**: `fromMe == true` → "You" + outgoing accent; `fromMe == false` → real sender name; empty sender degrades gracefully.
4. **`Normalize` reaction guard**: a reaction raw carrying a `reply_message` → empty quoted fields, **no** `StageServerThumbnail` disk write.
5. **Four-layer carry**: `Normalize` + `CreateViewModel` for chat/image/video/audio/doc replies → all five `quoted*` fields present on the resulting `MessageViewModel`.
6. **`WappiSendTextRequest` serialization**: `quotedMessageId == null` → key **absent**; set → key present with value (per-property `NullValueHandling.Ignore`, no serializer settings). Mirror `WappiMediaRequestFactoryTests`.
7. **`ChatHistoryCache` round-trip**: save a reply VM, load it, all five `quoted*` (incl. `MessageType` enum) survive `JsonUtility`.
8. **`OutboxStore` back-compat**: pre-existing entry → `quotedMessageId == null`; new entry persists/reloads it; retried reply re-attaches the quote.
9. **Snippet sanitization**: leading ZWS trimmed before truncation; pure-emoji snippet renders non-empty; over-length truncates to one line.
10. **`ScrollToMessage` target math** (extract a pure helper): centered landing `clamp(distanceFromTop − viewportH*0.4, 0, scrollableH)`; `scrollableH <= 1` → `vNP = 0`; id-not-found → no scroll mutation.
11. *(only if D1 alternative chosen)* reply-to-pending omits wire `quoted_message_id`; temp-id→real-id migration rewrites a reply's `quotedMessageId`.

## 15. Files

**New**
- `Assets/Scripts/Chat/ReplyParser.cs`
- `Assets/Scripts/Chat/SwipeToReply.cs`
- `Assets/Scripts/Chat/MessageActionMenu.cs` (+ menu UI under the messages panel / a PopupUI-style prefab)
- `Assets/Tests/Editor/Chat/ReplyParserTests.cs` (+ additions to existing test files)

**Modified**
- `Assets/Scripts/Chat/RawMessage.cs`
- `Assets/Scripts/Chat/NormalizedMessage.cs`
- `Assets/Scripts/UI/MessageViewModel.cs`
- `Assets/Scripts/Chat/OutboxStore.cs`
- `Assets/Scripts/Main/ChatManager.cs` (`Normalize`, `CreateViewModel`, `FindActiveById`, `OpenChat` cancel, send routines, `WappiSendTextRequest`, temp-id swap migration if D1-alt)
- `Assets/Scripts/Main/ChatManager.Outbox.cs` (reply state + event, retry threading)
- `Assets/Scripts/UI/MessageItemView.cs` (quoted card render, layout clamps, disposal, snippet, swipe/long-press wiring, `FlashHighlight`)
- `Assets/Scripts/UI/MessageListView.cs` (`ScrollToMessage`)
- `Assets/Scripts/Chat/MessagesBottomPanel.cs` (reply preview bar)
- `Assets/Prefabs/MessageTextIncoming.prefab` + `.meta`
- `Assets/Prefabs/MessageTextOutgoing.prefab` + `.meta`

## 16. Suggested build sequence

1. **Phase 0** — capture the real Wappi reply payload + confirm the send param name (§12). Pin field names.
2. **Data + parser** — `RawMessage`/`NormalizedMessage`/`MessageViewModel` fields, `ReplyParser`, `Normalize`/`CreateViewModel`/`FindActiveById` + tests 1–5, 7.
3. **Send + compose** — reply state/event, `WappiSendTextRequest`, send-routine threading, `OutboxStore`, `OpenChat` cancel, preview bar + tests 6, 8.
4. **Bubble card** — prefab `QuotedCard` + `RenderQuotedCard` + layout clamps + disposal + snippet + test 9. Visual check in Editor.
5. **Triggers** — `SwipeToReply` + long-press `MessageActionMenu`.
6. **Scroll-to-original** — `ScrollToMessage` + `FlashHighlight` + test 10.
7. End-to-end device pass (incoming reply, outgoing reply, media quote, swipe, long-press, scroll-to-original, keyboard + preview bar).

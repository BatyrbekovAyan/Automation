# WhatsApp Read-Receipt Ticks on Outgoing Messages — Design

**Date:** 2026-05-13
**Scope:** `Screen_Whatsapp/MessagesPanel` — outgoing message bubbles only
**Goal:** Render WhatsApp's iconic delivery-status ticks (clock / single grey / double grey / double blue / red `!`) inside outgoing message bubbles, with an outbox that lets the user tap a failed message to retry.

---

## 1. Summary

The messages panel today renders outgoing bubbles without any delivery-status indication, even though the chat-list previews ([`ChatPreviewFormatter`](../../../Assets/Scripts/Chat/ChatPreviewFormatter.cs)) already display ticks correctly. The required sprite glyphs are present in [`ChatTicks.asset`](../../../Assets/Resources/) (`tick_sent`, `tick_double`, `tick_double_blue`, plus `tick_pending` and `tick_failed`) and the asset is registered as a TMP fallback via [`ChatTicksFallbackRegistrar`](../../../Assets/Scripts/Chat/ChatTicksFallbackRegistrar.cs), so all five sprites are already addressable via bare `<sprite name="...">` tags inside any TMP text in the project.

This spec extends the existing `Raw → Normalized → ViewModel → View` pipeline with a `DeliveryStatus` enum, adds optimistic-send + outbox semantics so locally-failed messages persist across chat reopens with tap-to-retry, and renders the right tick by appending a sprite tag inside the bubble's existing `timeText` field. No prefab changes required.

**Out of scope for v1** — explicitly:
- Media-send outbox entries (`SendTextMessage` is the only sender today; media attach is still a stub).
- Live (push-style) status updates for received-status changes on already-rendered bubbles. Server statuses are read once when the bubble is created; the next chat reopen picks up newer values.
- The "info" screen showing per-recipient read times in group chats.
- Disappearing-message indicators.

## 2. User-visible behavior

| Action | Result |
|---|---|
| Send a text message | An outgoing bubble appears immediately with a **clock** `🕓` next to the time. |
| Wappi accepts the send (typically <1 s) | Clock flips to **single grey tick** `✓`. |
| Recipient's WhatsApp delivers the message | Single tick becomes **double grey tick** `✓✓`. (Visible next time the chat is opened or live-fetched.) |
| Recipient opens the chat and reads | Double grey becomes **double blue tick** `✓✓`. (Same visibility rule as above.) |
| Send fails (no network, server error, etc.) | Clock flips to **red `!`**. The tap-to-retry button activates on the time-row area. |
| Tap the red `!` | The button flips back to clock, the POST re-fires with the same payload. Resolves to single tick on success or red `!` again on failure. |
| Back out of the chat with a red `!` visible | Failed message persists. Reopening the chat shows the red `!` in its original timestamp position. |
| Force-quit the app while a clock is on screen | On next launch the entry is "stale-pending" and is promoted to red `!` (Failed). The user sees a recoverable state, not a phantom clock. |
| Open a chat | All outgoing bubbles render their correct tick at load time. Failed-outbox entries are spliced in by timestamp. |
| Open an incoming-only chat or look at incoming bubbles | No tick is ever rendered. (`DeliveryStatus.None` → null sprite tag.) |

## 3. Visual design

The tick sits **inside** the existing `timeText` TMP element, appended after the time with a single space:

```
10:42 ✓✓
```

| Element | Spec |
|---|---|
| Tick glyphs | Already authored in `ChatTicks.asset` — `tick_pending`, `tick_sent`, `tick_double`, `tick_double_blue`, `tick_failed` |
| Tick render mechanism | `<sprite name="...">` inline tag inside the existing `timeText.text` |
| Time-to-tick separator | Single space character |
| Tick height | Inherits from `timeText.fontSize` (TMP sprite tags auto-scale to text size) |
| Tick color | Atlas-baked — sprites ignore `timeText.color`, so the existing `ApplyDynamicLayout` color-flipping (white over media, grey under bubble) does not tint the ticks |
| Tap-to-retry hit area | The whole `timeText` rect, enabled only when `DeliveryStatus == Failed`. Forgiving by design; pixel-precise tick hit area is a fast-follow option (see §8). |
| Failed-message bubble color | Unchanged — same green as a normal outgoing bubble. Only the tick changes. |

No new prefab fields, no new `Sprite` references to wire — all five glyphs render through the TMP fallback chain already registered at startup.

## 4. Architecture

### 4.1 Data flow

The existing pipeline is unchanged in shape; one new field threads through every layer:

```
Wappi messages/get JSON  ({ "delivery_status": "sent" | "delivered" | "read" })
  └── RawMessage.deliveryStatusRaw  (string)
        └── ChatManager.Normalize() → DeliveryTickFormatter.ParseWappiString
              └── NormalizedMessage.deliveryStatus  (DeliveryStatus enum)
                    └── ChatManager.CreateViewModel()
                          └── MessageViewModel.deliveryStatus
                                └── MessageItemView.Bind() → timeText sprite tag
```

A new event `ChatManager.OnMessageStatusChanged(string oldMessageId, string newMessageId, DeliveryStatus status)` lets already-rendered bubbles update in place. The id-pair lets a single event signal both an id swap (`oldId=tempId`, `newId=server message id`) and a status change in one call.

### 4.2 File-by-file change list

| File | Change |
|---|---|
| `Assets/Scripts/Chat/DeliveryStatus.cs` | **NEW.** `public enum DeliveryStatus { None, Pending, Sent, Delivered, Read, Failed }` |
| `Assets/Scripts/Chat/DeliveryTickFormatter.cs` | **NEW.** Static `GetSprite(DeliveryStatus) → string` returning the matching `<sprite name="..."> ` tag, or `null` for `None`. Mirrors [`ChatPreviewFormatter.GetTickSprite`](../../../Assets/Scripts/Chat/ChatPreviewFormatter.cs#L65). |
| `Assets/Scripts/Chat/OutboxStore.cs` | **NEW.** Plain C# class. Per-chat persistence under `{persistentDataPath}/outbox_{chatId}.json`. `OutboxEntry` inner type: `tempId, chatId, text, timestamp, attemptCount, profileId`. Atomic writes via `.tmp` + `File.Replace`. |
| `Assets/Scripts/Chat/RawMessage.cs` | Add `[JsonProperty("delivery_status")] public string deliveryStatusRaw;` |
| `Assets/Scripts/Chat/NormalizedMessage.cs` | Add `public DeliveryStatus deliveryStatus;` |
| `Assets/Scripts/UI/MessageViewModel.cs` | Add `public DeliveryStatus deliveryStatus;` |
| `Assets/Scripts/Main/ChatManager.cs` | (a) Parse `deliveryStatus` inside `Normalize()`; (b) declare new event `OnMessageStatusChanged`; (c) extend `OnChatSelected` to splice outbox entries into the rendered list; (d) hook outbox into `SendTextMessage`. |
| `Assets/Scripts/Main/ChatManager.Outbox.cs` | **NEW** partial class. Owns the `OutboxStore` field, the splice helper, and `RetryOutboxMessage(string tempId)`. Splits outbox out of the existing god-object consistent with the existing `ChatManager.BotState.cs` partial pattern. |
| `Assets/Scripts/UI/MessageItemView.cs` | (a) Render tick inside the existing `timeText` formatting block (~line 268); (b) subscribe to `ChatManager.OnMessageStatusChanged` in `OnEnable` / unsub in `OnDisable`; (c) add `SetDeliveryStatus(DeliveryStatus)`, `RefreshTimeAndTick()`, `UpdateRetryButton(bool)`. |

Total: 5 new files, 4 modified files. No prefab edits, no scene edits.

### 4.3 State ownership

| Status | Source of truth | Persisted? |
|---|---|---|
| `Sent` / `Delivered` / `Read` | Wappi (`messages/get`) | No — refetched on every history load |
| `Pending` | `OutboxStore` (in-memory while POST is in flight) | Persisted but stale entries promoted to `Failed` on next chat open |
| `Failed` | `OutboxStore` | Yes — persists across chat reopens and app restarts |
| `None` | Implicit (incoming messages, unknown statuses) | N/A |

## 5. Component details

### 5.1 `DeliveryTickFormatter`

```csharp
public static class DeliveryTickFormatter
{
    public static string GetSprite(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Pending   => "<sprite name=\"tick_pending\">",
        DeliveryStatus.Sent      => "<sprite name=\"tick_sent\">",
        DeliveryStatus.Delivered => "<sprite name=\"tick_double\">",
        DeliveryStatus.Read      => "<sprite name=\"tick_double_blue\">",
        DeliveryStatus.Failed    => "<sprite name=\"tick_failed\">",
        _                        => null,
    };

    private static readonly HashSet<string> LoggedUnknown = new();

    public static DeliveryStatus ParseWappiString(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return DeliveryStatus.None;
        switch (raw.ToLowerInvariant())
        {
            case "sent":      return DeliveryStatus.Sent;
            case "delivered": return DeliveryStatus.Delivered;
            case "read":      return DeliveryStatus.Read;
            default:
                if (LoggedUnknown.Add(raw))
                    Debug.LogWarning($"[DeliveryTickFormatter] Unknown Wappi status: '{raw}'");
                return DeliveryStatus.None;
        }
    }
}
```

`ChatManager.Normalize()` calls `ParseWappiString(raw.deliveryStatusRaw)` for outgoing messages (`raw.fromMe == true`) and skips the call for incoming messages (which stay at `DeliveryStatus.None`).

### 5.2 `OutboxStore`

```csharp
public class OutboxStore
{
    [Serializable] public class OutboxEntry
    {
        public string tempId;
        public string chatId;
        public string text;
        public long timestamp;
        public int attemptCount;
        public string profileId;   // bot's profile_id at send time — used by retry
    }

    public IReadOnlyList<OutboxEntry> GetFor(string chatId);
    public OutboxEntry Find(string tempId);
    public void Add(OutboxEntry entry);
    public void Remove(string tempId);
    public void Update(OutboxEntry entry);   // for attemptCount bumps
}
```

Public surface is deliberately small. The in-memory cache (`Dictionary<chatId, List<entry>>`) is lazy-loaded per chat; writes hit disk synchronously through a `.tmp + File.Replace` swap.

### 5.3 `MessageItemView` additions

Inside the existing `Bind()` time block around line 268:

```csharp
if (timeText != null)
{
    DateTime localTime = DateTimeOffset.FromUnixTimeSeconds(vm.timestamp).LocalDateTime;
    string time = localTime.ToString("HH:mm");
    string tick = vm.isIncoming ? null : DeliveryTickFormatter.GetSprite(vm.deliveryStatus);
    timeText.text = tick != null ? $"{time} {tick}" : time;
}
```

New methods:

```csharp
public void SetDeliveryStatus(DeliveryStatus newStatus)
{
    if (currentVm == null || currentVm.isIncoming) return;
    currentVm.deliveryStatus = newStatus;
    RefreshTimeAndTick();
    UpdateRetryButton(newStatus == DeliveryStatus.Failed);
}

private void RefreshTimeAndTick() { /* re-runs only the block above */ }

private void UpdateRetryButton(bool enabled)
{
    // Lazily AddComponent<Button> on timeText (raycastTarget=true) on first failure.
    // onClick → ChatManager.Instance.RetryOutboxMessage(currentVm.messageId).
    // Disable + clear listeners when status is anything other than Failed.
}

private void HandleStatusChanged(string oldId, string newId, DeliveryStatus status)
{
    if (currentVm == null || currentVm.isIncoming) return;
    if (currentVm.messageId != oldId) return;
    if (newId != oldId) currentVm.messageId = newId;
    SetDeliveryStatus(status);
}
```

Subscribe in `OnEnable`, unsub in `OnDisable` — matches the existing event-driven view pattern at [MessageListView.cs:47](../../../Assets/Scripts/UI/MessageListView.cs#L47).

### 5.4 `ChatManager.SendTextMessage` modifications

The existing flow (lines 686–736) gains four new steps. New code in **bold**:

1. Generate `tempId`.
2. Create optimistic `MessageViewModel { messageId=tempId, deliveryStatus=Pending, ... }`.
3. **`OutboxStore.Add(new OutboxEntry { tempId, chatId, text, timestamp=now, attemptCount=1, profileId=currentBot.profileId })`**.
4. `seenMessageIds.Add(tempId)` (existing).
5. Fire `OnLiveMessagesReceived` → bubble renders with clock.
6. POST coroutine to Wappi (existing).
7. **On success** (`response.status == "done"` && `!IsNullOrEmpty(response.message_id)`):
   - `seenMessageIds.Remove(tempId); seenMessageIds.Add(response.message_id)` (existing).
   - **Fire `OnMessageStatusChanged(tempId, response.message_id, DeliveryStatus.Sent)`** → bubble swaps its `messageId` and re-renders the tick.
   - **`OutboxStore.Remove(tempId)`**.
8. **On error** (network failure or `response.status != "done"`):
   - **Fire `OnMessageStatusChanged(tempId, tempId, DeliveryStatus.Failed)`** → bubble shows red `!` and lazy-adds the retry Button.
   - **Entry stays in outbox** (no Remove call).

### 5.5 `ChatManager.RetryOutboxMessage`

```csharp
public void RetryOutboxMessage(string tempId)
{
    OutboxEntry entry = outbox.Find(tempId);
    if (entry == null) return;

    // Idempotency: only retry from a Failed state — guards against double-tap.
    MessageViewModel vm = FindLoadedMessage(entry.chatId, tempId);
    if (vm != null && vm.deliveryStatus != DeliveryStatus.Failed) return;

    entry.attemptCount++;
    outbox.Update(entry);

    OnMessageStatusChanged?.Invoke(tempId, tempId, DeliveryStatus.Pending);
    StartCoroutine(PostTextMessageRoutine(entry.chatId, entry.text, tempId, entry.profileId));
}
```

`PostTextMessageRoutine` is the existing POST extracted into a reusable coroutine that accepts `tempId` + `profileId` instead of synthesizing them. On success/error it follows steps 7/8 from §5.4 unchanged.

### 5.6 Chat-open splice

`OnChatSelected(chatId)` already loads cached messages and fires `OnBatchMessagesLoaded`. New step, runs after cache load but before the first server fetch.

**Key simplification — `OutboxEntry` has no status field.** The outbox only holds *unresolved* sends. The disposition of an entry is determined by app lifecycle:

- While the in-flight POST coroutine is running → the entry's matching `MessageViewModel.deliveryStatus` is `Pending`. The entry exists in `OutboxStore` and in memory; it is **not** rendered from the outbox splice (it's already on screen via `OnLiveMessagesReceived`).
- When that POST resolves successfully → entry is removed from the outbox.
- When that POST fails → entry remains in the outbox; the bubble's `deliveryStatus` is set to `Failed`.
- When the chat is reopened in a fresh session → every entry the splice finds is, by definition, an unresolved-from-a-previous-session send. It is materialized into a `MessageViewModel` with `deliveryStatus = Failed`. The "stale Pending" case collapses into this same path.

```csharp
var unresolved = outbox.GetFor(chatId);
foreach (var e in unresolved)
{
    var vm = new MessageViewModel
    {
        messageId      = e.tempId,
        chatId         = e.chatId,
        type           = MessageType.Chat,
        text           = e.text,
        timestamp      = e.timestamp,
        isIncoming     = false,
        deliveryStatus = DeliveryStatus.Failed,
    };
    messagesToFold.Add(vm);
}
```

Folded into the sorted messages list before `OnBatchMessagesLoaded` fires. [`MessageListView.UpdateListRoutine`](../../../Assets/Scripts/UI/MessageListView.cs#L314) already sorts by timestamp and handles date separators, so the failed bubbles take their natural chronological position with no rendering changes.

A subtle case: if the user is *currently in* a chat when a send fails and then backs out without retrying, the in-memory bubble is destroyed but the outbox entry persists. Reopening the chat re-creates the bubble via the splice with `Failed` — same outcome as the cross-session case.

## 6. Edge cases

| Case | Handling |
|---|---|
| User taps retry twice fast | `RetryOutboxMessage` early-returns if the VM is not currently `Failed` (idempotency). |
| Bot switch during pending send | `OutboxEntry.profileId` was captured at send time; retry uses **that** profile_id, not the active bot's. Wrong-bot sends are impossible. |
| Unknown Wappi status value | Logged once per session via `LoggedUnknown` set, treated as `None`. Bubble renders no tick rather than mis-rendering. |
| Bubble destroyed before status update arrives | `OnDisable` unsubscribes from `OnMessageStatusChanged`. No leaked listener, no NRE. Outbox state is unaffected; next chat open re-renders correctly. |
| Retry Button listener leak | `MessageItemView.OnDisable` calls `retryButton?.onClick.RemoveAllListeners()`, mirroring [MessagesBottomPanel.cs:48](../../../Assets/Scripts/Chat/MessagesBottomPanel.cs#L48). |
| App killed mid-POST | Entry stays `Pending` in the JSON file. Stale-pending promotion on next chat open flips it to `Failed`. |
| Echo of optimistic send arrives via live channel | Existing `seenMessageIds` dedup (line 280) drops it — the realId was added to the set in step 7 of §5.4 before the live echo arrives. |
| Sprite-tag color inheritance | TMP sprites render at atlas color regardless of `timeText.color`. The existing `ApplyDynamicLayout` colour flips (white over media, grey under bubble) do not tint the tick. |
| Missing sprite in `ChatTicks.asset` | TMP renders the `?` fallback glyph and logs once. The asset is shipped in the repo, so this only fires if it's deleted. |
| Outbox file is corrupted JSON | `OutboxStore` catches the parse exception, logs once per chat, and treats the chat's outbox as empty. The user loses persistence on that chat only; the rest of the app stays healthy. |

## 7. Wappi API contract — resolved

Confirmed by user (no probe needed):

- **Endpoint:** `messages/get` (and by extension `messages/all/get`).
- **Field:** `delivery_status` on each message object.
- **Type:** String. Same domain as the chat-list field `last_message_delivery_status`.
- **Known values:** `"sent"`, `"delivered"`, `"read"`.
- **Other values / missing field:** Treated as `DeliveryStatus.None` — no tick rendered. The one-shot warning via `DeliveryTickFormatter.LoggedUnknown` flags any new value Wappi adds without flooding the console.
- **Incoming messages:** May or may not carry the field; we don't parse it because incoming messages are never assigned a tick regardless. The `fromMe` gate inside `ChatManager.Normalize()` skips the parse for `fromMe == false`.

## 8. Testing

### 8.1 EditMode unit tests (Unity Test Framework, plain NUnit)

- **`DeliveryTickFormatterTests`**
  - `GetSprite` for each enum value → expected sprite-tag string.
  - `GetSprite(None)` → `null`.
  - `ParseWappiString` round-trip for the three known strings (`"sent"` / `"delivered"` / `"read"`), case-insensitive.
  - `ParseWappiString` returns `None` for an unknown value and for `null` / empty input; logs only once for repeated unknowns.

- **`OutboxStoreTests`**
  - `Add` then `GetFor` returns one entry.
  - `Remove` deletes it.
  - Save/reload through a temp `persistentDataPath` override survives `attemptCount` bumps.
  - Corrupted JSON returns empty without crashing.

### 8.2 Manual PlayMode checklist

A real Wappi account + a second WhatsApp device are required:

1. Send a normal text message → clock → single tick → double tick → blue when recipient reads. Each transition visually correct.
2. Airplane-mode mid-send → red `!` appears. Toggle airplane off, tap `!`, see clock return then single tick. Confirm `outbox_<chatId>.json` is empty after success.
3. Force-quit the app mid-send → reopen chat → previously-pending message renders as Failed. Tap retries successfully.
4. Switch bots, switch back, retry an old failed message → sends from the original bot's number (entry-captured `profileId`).
5. Group chat with mixed message states → each sender's name color, each tick state, all correct.
6. Scroll an existing long chat to old outgoing messages → ticks render correctly in already-loaded history (verifies the static-at-load path through `UpdateListRoutine`).
7. Switch chat to one with zero outgoing messages → no ticks render anywhere.

## 9. Migration / rollout

- No data migration: existing chat history JSON files have no `deliveryStatus` field; they parse with `None` and render no tick — matching today's behavior. Outgoing bubbles only get ticks once the cache is refreshed by a server fetch.
- No feature flag: the change is additive and visually unmistakable.
- No backwards-compat concern: the outbox is a brand-new persistence surface. Nothing reads the file but us.

## 10. Follow-ups (out of scope)

- Pixel-precise tap area on the tick glyph only (currently the whole "10:42 ✓✓" rect is tappable when Failed). Use the existing [`TMPLinkHandler`](../../../Assets/Scripts/Chat/TMPLinkHandler.cs) with a `<link="retry">` tag around the sprite.
- Live (push-style) updates for `Sent → Delivered → Read` transitions on bubbles currently on screen.
- Outbox entries for media sends (blocked until `MessagesBottomPanel.OnAttachClicked` is implemented).
- Group-chat "info" sheet with per-recipient read times.
- "Delete failed message" gesture (long-press → delete) as an alternative to tap-to-retry.

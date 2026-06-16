# Send reactions to WhatsApp messages — design

**Date:** 2026-06-16
**Status:** Approved (design); ready for planning
**Scope:** WhatsApp only (Wappi). Telegram out of scope.

## Goal

The app already **receives and displays** reactions. Add the ability to **send** a
reaction to any message: long-press a bubble, pick an emoji from a quick bar (or a
full picker), and the reaction is sent to WhatsApp via Wappi and shown instantly.

## Why this is small

The entire *display* half already exists and is directly reusable:

| Existing piece | File | Reused for |
| --- | --- | --- |
| `MessageReaction { emoji, reactorKey, senderName, fromMe, time }` | `Assets/Scripts/Chat/MessageReaction.cs` | Synthesized `"me"` reaction for optimistic display |
| `ReactionStore.ApplyToMessage(msg, ev)` (pure add/replace/remove per reactor) | `Assets/Scripts/Chat/ReactionStore.cs` | Applying our own reaction locally |
| `ReactionEvent { targetId, emoji, reactorKey, fromMe, time, IsRemoval }` | `Assets/Scripts/Chat/ReactionParser.cs` | The event we synthesize on tap |
| `ReactionPillView.Render(reactions)` (+ re-render on sprite ready) | `Assets/Scripts/UI/ReactionPillView.cs` | Pill updates with zero new rendering code |
| `OnMessageReactionsChanged` event | `Assets/Scripts/Main/ChatManager.cs` | Same notify path incoming reactions use |
| `MessageViewModel.messageId` (= Wappi stanza id) | `Assets/Scripts/UI/MessageViewModel.cs` | The `message_id` we send |
| `PostTextMessageRoutine` send pattern | `Assets/Scripts/Main/ChatManager.cs` | Template for the reaction send coroutine |

So sending is three new pieces (**trigger**, **picker**, **API call**) feeding back
into the pipeline that already renders reactions. **No new persistent data fields.**

## Confirmed API contract (Wappi)

Verified against the Wappi dashboard API docs (`/api-documentation`, "Работа с
сообщениями → Синхронные методы → post_api_sync_message_reaction"):

```
POST https://wappi.pro/api/sync/message/reaction?profile_id={profileId}
Authorization: {Manager.wappiAuthToken}
Content-Type: application/json

{
  "body": "👍",                       // the emoji; "" (empty) removes the reaction
  "message_id": "3EB06C32E4E7B2690CBE" // target message's Wappi stanza id
}
```

Note: **there is no `recipient` field** (unlike `message/send`). The reaction is
targeted purely by `message_id`.

Success response (`status == "done"`):

```json
{ "status": "done", "timestamp": 1683969388, "time": "2023-05-13T12:16:28+03:00",
  "message_id": "3EB0A7E2F4F0B8AFE013", "uuid": "71ad40e9-b023" }
```

The response `message_id` is the reaction stanza's own id; we do not need to store it.

## Interaction (locked)

- **Trigger:** long-press a message bubble (WhatsApp standard).
- **Picker:** quick bar of six common emoji `👍 ❤️ 😂 😮 😢 🙏` inline, plus a `+`
  that opens a full emoji picker grid.
- **Result:** the reaction attaches as the existing pill on the bubble.
- **Own messages:** you can react to your own (outgoing) messages too.
- The Reply / Copy / Forward context menu (shown in the brainstorm mockup for realism)
  is **out of scope** here — separate feature.

## Components

### 1. Long-press gesture (new)
A hold-timer gesture (~450 ms) on the message bubble that reports
`(MessageViewModel, bubble screen rect)` to the overlay controller on fire.

Constraints (must coexist with the scrolling message list):
- Cancel if `ScrollClickBlocker.IsBlocking`, if `SwipeToBack.IsSliding`, or if the
  finger moves past a small drag threshold (it's a scroll, not a hold).
- Must forward drag to the parent `ScrollRect` so scrolling still works — follow the
  existing passthrough pattern (`ClickPassthrough` / `DelayedFingerUpAction`).
- Attached to **both** `MessageTextIncoming` and `MessageTextOutgoing` prefabs.

### 2. Reaction bar overlay — one shared instance (new)
A single `ReactionBarController` owned by the chat screen (instantiated once, like a
popup) — **not** a child of every bubble.

- On trigger: dim with a full-screen scrim; show the quick-six bar clamped just above
  the long-pressed bubble (kept within screen bounds); show the `+` button.
- Emoji buttons render through the existing emoji system (TMP `<sprite>` tags via
  `UnicodeEmojiConverter`) so they match chat rendering exactly.
- Tap emoji → send + dismiss. Tap `+` → open full picker (Plan B). Tap scrim → dismiss.
- **Rejected alternative:** one bar prefab per bubble — multiplies instances across a
  long list and complicates positioning/dismissal. One overlay is lighter and matches
  WhatsApp.

### 3. Full emoji picker — `+` (new, Plan B)
A scrollable grid of common emoji, rendered through the **existing** sprite system
(`UnicodeEmojiConverter` sprite tags + `EmojiPatchService` lazy-loading missing sprites
from the Twemoji CDN). There is no emoji picker in the app today, so this is net-new UI
and is the bulk of the effort — hence staged as **Plan B** (see Staging).

### 4. Send routine in ChatManager (new)
`SendReactionRoutine` mirroring `PostTextMessageRoutine` (same `UnityWebRequest` POST +
`JsonConvert` + `Authorization: Manager.wappiAuthToken`, `timeout = 30`).

```csharp
public void SendReaction(MessageViewModel target, string emoji); // public entry
private IEnumerator SendReactionRoutine(string targetMessageId, string emoji, string profileId);

public class WappiSendReactionRequest  { public string body; public string message_id; }
public class WappiSendReactionResponse { public string status; public long timestamp;
                                         public string time; public string message_id; public string uuid; }
```

- URL: `https://wappi.pro/api/sync/message/reaction?profile_id={profileId}`.
- `body` = emoji, or `""` to remove.
- Resolve `profileId` the same way the text-send path does (active chat's WhatsApp
  profile id).
- Success = `response.status == "done"`.

### 5. Optimistic update + reconcile (new glue in ChatManager)
On tap, **before** the network call:
1. Compute the new state from the bubble's current `"me"` reaction:
   - none → add tapped emoji; same emoji → remove (`body=""`); different → replace.
2. Synthesize `ReactionEvent { targetId = target.messageId, emoji = newEmojiOr"", reactorKey = "me", fromMe = true, time = now }`, apply via `ReactionStore.ApplyToMessage`, fire `OnMessageReactionsChanged(target)` → pill updates instantly (identical to an incoming reaction).
3. Send to Wappi.
4. On failure: revert to the snapshot of the prior `"me"` state (re-apply + re-fire) and show a small toast.

Semantics fall straight out of the existing reducer: **one reaction per message from
you**, tap-same-to-remove, different-emoji-replaces.

## Data model

No new persistent fields. Reactions already persist via `ChatHistoryCache`
(`MessageViewModel.reactions`). We only synthesize an in-memory `"me"` `MessageReaction`.

## Staging

- **Plan A (core, fully shippable on its own):** long-press gesture → quick-six bar →
  optimistic display → `SendReaction` → toggle/replace/remove + revert-on-failure.
- **Plan B:** the `+` full emoji picker grid.

Plan A delivers a complete, usable reaction-send loop. Plan B is sequenced after.

## Files

**New**
- `Assets/Scripts/Chat/ReactionBarController.cs` + overlay prefab (quick-six bar + scrim + `+`).
- Long-press gesture component (new `MonoBehaviour`, or a method set on `MessageItemView`).
- *(Plan B)* `Assets/Scripts/Chat/EmojiPickerPanel.cs` + grid prefab.

**Modified**
- `Assets/Scripts/Main/ChatManager.cs` — `SendReaction` + `SendReactionRoutine` +
  request/response classes + optimistic-apply/revert glue.
- `Assets/Scripts/UI/MessageItemView.cs` + `MessageTextIncoming` / `MessageTextOutgoing`
  prefabs — wire long-press; expose the bubble's screen rect.
- Likely `Assets/Scripts/UI/MessageListView.cs` — own/instantiate the single
  `ReactionBarController` overlay.

## Testing

EditMode tests (`Assembly-CSharp-Editor`, no asmdef — same as
`DeliveryTickFormatterTests`) cover the pure logic:
- optimistic apply / toggle-off / replace / remove on a `MessageViewModel` via the
  `"me"` `ReactionEvent` path;
- `WappiSendReactionRequest` body construction (correct `body` / `message_id`, empty
  `body` for removal).

Gesture + overlay positioning + the send round-trip are verified via the test bridge /
on device (not unit-testable).

## Risks / open items

- **Quick-six sprite availability:** the bar must render instantly offline. Confirm the
  six quick-reaction emoji (`👍 ❤️ 😂 😮 😢 🙏`) are in the static sprite atlas (or
  preload them) so the bar doesn't wait on a CDN fetch.
- **Gesture vs scroll:** the long-press must not break list scrolling — relies on the
  cancel-on-drag + passthrough constraints above.
- **Overlay positioning:** clamp the bar within screen bounds when the long-pressed
  bubble is near the top/bottom edge.
- **Profile id resolution:** reaction send must use the active chat's WhatsApp profile
  id, same source as text send.

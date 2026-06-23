# Architecture Research

**Domain:** AI reply-suggestions panel ("semi-auto" mode) wired into an existing Unity WhatsApp chat client + n8n
**Researched:** 2026-06-23
**Confidence:** HIGH

This is integration architecture for a SUBSEQUENT milestone on a brownfield app. It does NOT re-derive the existing architecture — it designs how the NEW suggestions feature plugs into the existing `ChatManager` event model, composer, persistence, and n8n transport while respecting the app's confirmed Wappi concurrent-response crossing bugs. All claims below are grounded in the current source (read at research time): `ChatManager.cs`, `ChatManager.QuoteResolve.cs`, `MessagesBottomPanel.cs`, `ExpandableInput.cs`, `QuickReplyPanel.cs`, `Bot.cs`.

## Standard Architecture

### System Overview

```
┌────────────────────────────────────────────────────────────────────────┐
│                        WhatsApp Chat Screen (Canvas)                     │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │  MessageListView (existing)        SemiAutoToggle (NEW, top bar)  │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │  SuggestionsPanel (NEW) — 4 cards: text + label + confidence      │  │
│  │     sits ABOVE the composer, same host area as QuickReplyPanel    │  │
│  │     [refresh ⟳]  [card][card]  [card][card]                       │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │  MessagesBottomPanel + ExpandableInput (existing composer)        │  │
│  └──────────────────────────────────────────────────────────────────┘  │
├────────────────────────────────────────────────────────────────────────┤
│                     SuggestionsController (NEW, MonoBehaviour)           │
│   - subscribes ChatManager.OnLiveMessagesReceived / OnChatSelected       │
│   - owns per-chat state, correlation ids, re-cluster steering            │
│   - pushes picked text into composer (never auto-sends)                  │
├────────────────────────────────────────────────────────────────────────┤
│   ISuggestionsProvider  ◄── THE SEAM (Phase 1 vs Phase 2 swap point)    │
│   ┌─────────────────────────┐        ┌──────────────────────────────┐  │
│   │ MockSuggestionsProvider │  (P1)  │ N8nSuggestionsProvider  (P2) │  │
│   │  canned data + delay    │        │  serial guarded webhook PULL │  │
│   └─────────────────────────┘        └──────────────┬───────────────┘  │
├──────────────────────────────────────────────────────┼──────────────────┤
│   SemiAutoStore (NEW)            ChatManager fetch gate (existing)       │
│   PlayerPrefs per-chat toggle    _chatFetchesInFlight / WaitForChat...   │
└──────────────────────────────────────────────────────┼──────────────────┘
                                                        ▼
                                          n8n webhook (POST, X-N8N-API-KEY)
                                          /webhook/SuggestReplies
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| `SuggestionsController` | Orchestrates the feature: listens to chat events, decides when to request, holds correlation/chat ids + re-cluster steering, routes picks to composer. The feature's only stateful hub. | MonoBehaviour singleton (`SuggestionsController.Instance`) mirroring `ChatManager`/`Manager` convention; subscribes in `OnEnable`, unsubscribes in `OnDisable`. |
| `SuggestionsPanel` | Pure view. Renders 4 cards (text + intent label + confidence), refresh button, loading/empty/error states; raises `OnCardPicked(SuggestionItem)` and `OnRefreshRequested`. No networking, no state. | MonoBehaviour view modeled on `QuickReplyPanel` (2×2 grid via `HorizontalLayoutGroup` rows). Built with `unity-ui-builder` quality bar. |
| `ISuggestionsProvider` | THE SEAM. Async contract: "given a request, deliver a `SuggestionSet` via callback." Hides whether data is mock or live. | C# interface; one method `RequestSuggestions(SuggestionRequest, Action<SuggestionResult>)`. |
| `MockSuggestionsProvider` | Phase-1 implementation: returns canned 4-suggestion sets after a simulated latency; varies output on re-cluster so the loop is demoable. | Plain C# class (no MonoBehaviour needed) or thin MonoBehaviour; uses a coroutine runner for the fake delay. |
| `N8nSuggestionsProvider` | Phase-2 implementation: PULL request to an n8n webhook, parses the suggestion payload, respects the serial fetch gate. | Plain C# orchestrator + a coroutine on `ChatManager`/`Manager`; follows `QuoteResolve` serial-drain template. |
| `SemiAutoStore` | Persists + reads the per-chat semi-auto toggle; gates whether the panel is allowed to populate. | Static helper over `PlayerPrefs`, bot-scoped key (see State section). |
| `SuggestionSet` / `SuggestionItem` / `SuggestionRequest` | Serializable data models for the payload + request envelope. | Plain C# classes in `Assets/Scripts/Chat/` (response models convention) or a new `Assets/Scripts/Suggestions/`. |

## Recommended Project Structure

```
Assets/Scripts/
├── Suggestions/                      # NEW — feature folder, keeps the seam discoverable
│   ├── SuggestionsController.cs      # event wiring, state, pick→composer routing
│   ├── ISuggestionsProvider.cs       # THE SEAM (interface)
│   ├── MockSuggestionsProvider.cs    # Phase 1 impl (canned data + fake latency)
│   ├── N8nSuggestionsProvider.cs     # Phase 2 impl (serial guarded n8n PULL)
│   ├── SuggestionSet.cs              # SuggestionItem[] + correlationId + chatId
│   ├── SuggestionRequest.cs          # request envelope (chatId, trigger, steerTowardText)
│   └── SemiAutoStore.cs              # per-chat toggle persistence
├── UI/
│   └── SuggestionsPanel.cs           # NEW — 4-card view (modeled on QuickReplyPanel)
│       └── SuggestionCardView.cs     # NEW — single card (text + label + confidence)
└── Main/
    └── ChatManager.Suggestions.cs    # NEW partial — ONLY if the live PULL coroutine
                                       # must live on ChatManager to share the fetch gate
```

### Structure Rationale

- **`Assets/Scripts/Suggestions/` (new folder):** Keeps the whole feature — and crucially the provider seam — in one place so the Phase-1→Phase-2 swap is a single, obvious edit. The existing tree already separates `Chat/` (models/logic), `UI/` (views), `Main/` (orchestration); a dedicated feature folder is consistent and avoids bloating the already-flagged-as-too-large `ChatManager`/`MessageItemView`.
- **`SuggestionsPanel` in `UI/`:** Matches where list/item views live and where `QuickReplyPanel`'s sibling concerns sit; the panel is a view, so it belongs with the views.
- **`ChatManager.Suggestions.cs` partial (conditional):** The serial fetch gate (`_chatFetchesInFlight`, `WaitForChatFetchesToDrain()`) is private to `ChatManager`. If the live PULL must share that gate (it should — see Wappi section), the coroutine either lives in a new partial OR `ChatManager` exposes a small `public IEnumerator WaitForChatFetchesToDrain()` and an increment/decrement pair the provider can call. Prefer exposing the gate over adding a 10th `ChatManager` partial, since CONCERNS already flags partial sprawl.

## Architectural Patterns

### Pattern 1: Provider Seam (Strategy behind an interface) — enables UI-first build

**What:** `SuggestionsController` depends only on `ISuggestionsProvider`. Phase 1 injects `MockSuggestionsProvider`; Phase 2 injects `N8nSuggestionsProvider`. The UI, the event wiring, the re-cluster loop, the composer hand-off, and the toggle are ALL built and polished in Phase 1 against the mock — n8n does not need to exist.

**When to use:** Exactly this situation — the build order mandates polished UI before the backend exists, and a real network dependency would block visual work and make the re-cluster loop untestable.

**Trade-offs:** One interface + one extra class. Negligible cost; it is the cleanest way to satisfy the "mock first, wire second" requirement and keeps the live path from leaking into the UI.

**Example:**
```csharp
public interface ISuggestionsProvider
{
    // Async by callback (matches the codebase's System.Action<T> coroutine convention).
    // Implementations MUST echo request.correlationId back on the result so a late/stale
    // response for a previous chat or a superseded re-cluster can be discarded.
    void RequestSuggestions(SuggestionRequest request, Action<SuggestionResult> onResult);
}

// Controller is provider-agnostic. Swap is one line at composition time:
//   _provider = USE_LIVE ? new N8nSuggestionsProvider(...) : new MockSuggestionsProvider();
```

### Pattern 2: Event-driven auto-populate with correlation guard

**What:** `SuggestionsController` subscribes to `ChatManager.OnLiveMessagesReceived` (signature: `Action<List<MessageViewModel>>`). On each batch it checks: (a) is semi-auto enabled for `currentChatId`? (b) does the batch contain an **incoming** customer message (`vm.isIncoming == true`) for the open chat? If yes, it issues a `SuggestionRequest` with a fresh `correlationId`. Manual refresh issues the same request with `trigger = Manual`. Because `OnLiveMessagesReceived` also fires for the user's own optimistic sends (`isIncoming == false`), the `isIncoming` filter is essential to avoid suggesting replies to the owner's own messages.

**When to use:** The standard event-binding pattern already used by every view in this app (subscribe in `OnEnable`, unsubscribe in `OnDisable`). Reuse it; do not poll.

**Trade-offs:** Must dedupe/cancel: a new incoming message while a request is in flight should supersede the old one. The `correlationId` echo (Pattern 1) makes stale results discardable; store `_activeCorrelationId` and drop any result whose id != current.

**Example:**
```csharp
void OnEnable()  { ChatManager.Instance.OnLiveMessagesReceived += HandleLive;
                   ChatManager.Instance.OnChatSelected         += HandleChatSelected; }
void OnDisable() { ChatManager.Instance.OnLiveMessagesReceived -= HandleLive;
                   ChatManager.Instance.OnChatSelected         -= HandleChatSelected; }

void HandleLive(List<MessageViewModel> batch)
{
    if (!SemiAutoStore.IsEnabled(ChatManager.Instance.CurrentChatId)) return;
    bool hasIncoming = batch.Exists(m => m.isIncoming && m.chatId == ChatManager.Instance.CurrentChatId);
    if (hasIncoming) Request(SuggestionTrigger.AutoIncoming, steerTowardText: null);
}
```
> Note: `ChatManager.currentChatId` is currently `private`. This milestone must add a `public string CurrentChatId => currentChatId;` accessor (one line) so the controller can read the open chat.

### Pattern 3: Pick → composer hand-off (never auto-send)

**What:** Tapping a card raises `OnCardPicked`. The controller writes the card's text into the existing composer input (`MessagesBottomPanel.inputField.text = picked.text`) and focuses it (`inputField.ActivateInputField()`), so `ExpandableInput` grows the field naturally. It does NOT call `ChatManager.SendTextMessage`. The owner edits then taps the existing Send button, which already routes through `SendTextMessage(currentChatId, text)`. Picking ALSO fires a re-cluster request steered toward the picked text (Pattern 4).

**When to use:** This is the core-value control guarantee ("never auto-sends, maximizes owner control"). The composer already exists and is battle-tested; the feature only feeds it text.

**Trade-offs:** The controller needs a reference to the composer's `TMP_InputField`. Expose a tiny method on `MessagesBottomPanel` (`public void LoadDraft(string text)`) rather than reaching into its serialized `inputField` from outside — keeps the composer's focus/keyboard logic (`KeepKeyboardOpenRoutine` style) encapsulated.

**Example:**
```csharp
void HandleCardPicked(SuggestionItem picked)
{
    _composer.LoadDraft(picked.text);                 // sets inputField.text + ActivateInputField
    Request(SuggestionTrigger.Recluster, steerTowardText: picked.text); // fresh set of 4
}
```

### Pattern 4: Re-cluster as a fresh request with a steering field

**What:** Picking a card does not mutate the existing 4 in place; it requests a brand-new `SuggestionSet` of 4, re-ranked toward the pick. The steering signal travels in `SuggestionRequest.steerTowardText` (and optionally `steerTowardLabel`). Phase-1 mock honors it by biasing its canned variants; Phase-2 n8n uses it as an LLM re-rank/regenerate hint. Each re-cluster gets a new `correlationId`, so the in-flight previous set is discarded on arrival.

**When to use:** Whenever the owner picks. This realizes the "spectrum of control" refine loop from PROJECT.md.

**Trade-offs:** Round-trips accumulate (auto → pick → re-cluster → pick → re-cluster…). With PULL transport each is a clean serial request; with PUSH it is far harder to correlate which inbound payload answers which pick (see Push vs Pull).

## Data Flow

### Request Flow (auto-on-incoming, manual refresh, and re-cluster all share one path)

```
[Incoming customer msg]  OR  [owner taps ⟳]  OR  [owner picks a card]
        ↓                          ↓                      ↓
ChatManager.OnLiveMessagesReceived │              OnCardPicked → LoadDraft(composer)
        ↓ (isIncoming filter)      │                      ↓
   SuggestionsController.Request(trigger, steerTowardText)
        ↓  builds SuggestionRequest { chatId, correlationId=new, trigger, steerTowardText }
        ↓  sets _activeCorrelationId = request.correlationId
   ISuggestionsProvider.RequestSuggestions(request, onResult)
        ├── Phase 1: MockSuggestionsProvider → fake delay → canned/steered SuggestionSet
        └── Phase 2: N8nSuggestionsProvider → WaitForChatFetchesToDrain() → POST webhook → parse
        ↓
   onResult(SuggestionResult { set, correlationId })
        ↓  GUARD: drop if result.correlationId != _activeCorrelationId
        ↓        drop if set.chatId != ChatManager.CurrentChatId   (chat switched)
   SuggestionsPanel.SetSuggestions(set.items)   → 4 cards re-render
```

### State Management

```
SemiAutoStore (PlayerPrefs, bot-scoped per-chat key)
    ↓ IsEnabled(chatId) gates the controller
SuggestionsController._activeCorrelationId   (in-memory; stale-result discard)
SuggestionsController._lastSetByChat[chatId] (optional in-memory cache so reopening a
                                              chat shows the last set without a refetch)
SuggestionsPanel  (pure view; holds no business state)
```

### Key Data Flows

1. **Auto-populate:** `OnLiveMessagesReceived` (incoming, semi-auto on) → request → provider → guarded result → panel. No new Wappi/n8n inbound socket required; reuses the existing live-sync that already polls `messages/get` every ~3s.
2. **Composer hand-off:** card tap → `MessagesBottomPanel.LoadDraft` → owner edits → existing Send button → existing `ChatManager.SendTextMessage`. The feature never owns the send path.
3. **Re-cluster loop:** pick → load draft + new steered request → new set supersedes old via correlation guard.
4. **Toggle gate:** owner flips per-chat toggle → `SemiAutoStore.SetEnabled` → controller starts/stops honoring events for that chat; panel shows/hides.

## Push vs Pull (n8n transport) — DECISION

**Recommendation: PULL — the app requests suggestions from a dedicated n8n webhook and waits for the synchronous response, exactly like every other app→n8n call today.**

| Criterion | PULL (app → n8n webhook, wait for response) | PUSH (n8n → app inbound webhook) |
|-----------|---------------------------------------------|----------------------------------|
| Fits existing transport | YES — all n8n calls are already outbound POST with `X-N8N-API-KEY`; INTEGRATIONS confirms "n8n webhooks are outbound only; app does not expose HTTP webhooks." | NO — would require the mobile app to run an HTTP server or add a polling/long-poll/WS channel that does not exist today. |
| Correlation (auto + manual + re-cluster) | TRIVIAL — request carries `correlationId`; the matching response carries it back. Each re-cluster is its own request/response pair. | HARD — inbound payloads are unsolicited; the app must match each to the right pending pick/refresh by `correlationId` and tolerate out-of-order/duplicate/missing pushes. |
| Re-cluster round-trips | Clean: pick → one request → one response, serialized. | Messy: a push could arrive for a re-cluster the owner already superseded; reconciliation logic is significant. |
| Coroutine model | Native fit — `IEnumerator + yield return SendWebRequest()` returns the suggestions directly. | Mismatch — no place to receive an async inbound event in a coroutine-only MonoBehaviour app. |
| Mobile reachability | No public endpoint needed; works behind NAT/cellular. | Requires the device to be addressable by n8n (push service, tunnel, or polling) — infra the project does not have. |
| Wappi crossing-bug discipline | Same serial/guard discipline reused (PULL is a request the app initiates and can gate). | N/A but introduces its own ordering hazards. |

**Why PULL wins for THIS app:** auto-on-incoming, manual refresh, and re-cluster are all owner/app-initiated events that map 1:1 to a request. PULL turns each into a self-correlating request/response, fits the coroutine + outbound-webhook + secrets conventions already in place, and needs zero new inbound infrastructure. PUSH only pays off when the backend must notify the client of events the client did not ask for — which is not the shape of this feature.

**Latency note:** PULL means the panel shows a loading state while n8n + the LLM run (likely 1–4s). That is acceptable and expected; the Phase-1 mock should simulate this latency so the loading/skeleton UI is designed up front. If n8n responses ever exceed `UnityWebRequest`'s 30s timeout (networking rule), the request fails gracefully to an error/retry card — do NOT raise the timeout.

### Concrete payload schema

**Request (app → `/webhook/SuggestReplies`, POST JSON):**
```json
{
  "correlationId": "sg_1719100000123_a1b2",   // unique per request; echoed back
  "chatId": "77011234567@c.us",                // the open WhatsApp chat
  "profileId": "<wappi profile id of active bot>",
  "botId": "Bot0",                             // PlayerPrefs entity key root
  "trigger": "auto_incoming",                  // auto_incoming | manual | recluster
  "steerTowardText": null,                     // re-cluster: the picked reply text (null otherwise)
  "steerTowardLabel": null,                    // optional: picked intent label
  "context": {                                 // optional recent-turn context for the LLM
    "lastIncomingText": "Do you deliver to Almaty?",
    "recentTurns": [ { "fromMe": false, "text": "..." }, { "fromMe": true, "text": "..." } ]
  }
}
```

**Response (n8n → app):**
```json
{
  "status": "done",
  "correlationId": "sg_1719100000123_a1b2",    // MUST match request; else discard
  "chatId": "77011234567@c.us",                // MUST match open chat; else discard
  "suggestions": [
    { "text": "Yes, we deliver across Almaty within 2 hours.", "label": "Delivery: yes", "confidence": 0.92 },
    { "text": "Delivery to Almaty is 1500₸, same day.",         "label": "Delivery: price", "confidence": 0.78 },
    { "text": "Could you share your district so I can confirm?", "label": "Clarify",        "confidence": 0.64 },
    { "text": "We currently deliver only within the city center.","label": "Delivery: limited","confidence": 0.41 }
  ]
}
```

**C# models (in `Assets/Scripts/Suggestions/`, parsed with `JsonConvert` per networking rule):**
```csharp
[Serializable] public class SuggestionItem { public string text; public string label; public float confidence; }
[Serializable] public class SuggestionSet  { public string correlationId; public string chatId; public List<SuggestionItem> suggestions; }
public enum SuggestionTrigger { AutoIncoming, Manual, Recluster }
public class SuggestionRequest { public string correlationId, chatId, profileId, botId; public SuggestionTrigger trigger; public string steerTowardText, steerTowardLabel; }
public class SuggestionResult  { public SuggestionSet set; public bool ok; public string error; }
```
- **`confidence`** is `0..1`; the card maps it to a visual (bar/percent/color). Always present so the UI never branches on missing data.
- **`label`** is a short human intent tag the owner scans. Free-form string from n8n; the UI must clamp/ellipsize it (do not let it blow out card width — see TMP/width pitfalls in CONCERNS).
- **`correlationId` + `chatId`** are the two discard keys. This is the same defensive posture as `CrossChatResponseGuard` for Wappi, applied to suggestions.
- **`steerTowardText`** is the single re-cluster steering field requested in the brief; `steerTowardLabel` is an optional refinement.

## The Mock → Live Provider Seam

**Where it lives:** `ISuggestionsProvider` in `Assets/Scripts/Suggestions/`. It is the ONLY boundary the rest of the feature knows about. The UI (`SuggestionsPanel`, `SuggestionCardView`), the controller, the re-cluster loop, the composer hand-off, the toggle gate, and the correlation/discard logic all sit ABOVE the seam and are written once, in Phase 1, against `MockSuggestionsProvider`.

```
        SuggestionsController  ──depends on──►  ISuggestionsProvider
                                                      ▲          ▲
                                          (Phase 1)   │          │   (Phase 2)
                                  MockSuggestionsProvider   N8nSuggestionsProvider
```

- **Phase 1 (`MockSuggestionsProvider`):** Returns canned 4-item sets after a coroutine-simulated delay (e.g. 1.2s) so the loading/skeleton state is designed for real. Honors `trigger`/`steerTowardText` by returning a *different* steered set on re-cluster (e.g. shuffle + bias toward variants near the picked text) so the full pick→re-cluster loop is demoable and polishable with no backend. Echoes back `correlationId` + `chatId` so the controller's discard guards are exercised in Phase 1.
- **Phase 2 (`N8nSuggestionsProvider`):** Same interface; performs the serial guarded PULL (next section), parses the payload into `SuggestionSet`, echoes `correlationId`/`chatId`. Swapping it in is a one-line composition change in `SuggestionsController` (ideally behind a `[SerializeField] bool useLiveSuggestions` or a `Secrets`/build flag), so Phase 1 work is never touched.
- **Seam discipline:** Nothing above the seam may reference `UnityWebRequest`, n8n URLs, or Wappi. If the UI needs latency/error states, the MOCK provides them — that guarantees the live provider has nowhere to surprise the UI later.

## Respecting the Wappi crossing bugs (and applying the same discipline to n8n)

The brief is explicit: new fetches must be serial/guarded, never concurrent. Two confirmed Wappi server bugs (CONCERNS + memory): `/messages/get` and `/media/download` cross-deliver responses under concurrency. The codebase already guards `messages/get` with `_chatFetchesInFlight` + `WaitForChatFetchesToDrain()` + `CrossChatResponseGuard`, and `QuoteResolve.cs` is the canonical "background backfill that defers to in-flight chat fetches" template.

**How the suggestions feature respects this:**

1. **The auto-trigger adds NO new Wappi calls.** Auto-populate keys off `OnLiveMessagesReceived`, which is already produced by the existing guarded `messages/get` live sync. The feature is a *consumer* of that event, not a new fetcher. This is the single most important design choice for honoring the constraint — it sidesteps the Wappi crossing surface entirely for the trigger path.
2. **The live n8n PULL is gated through the same drain.** `N8nSuggestionsProvider` must `yield return ChatManager.Instance.WaitForChatFetchesToDrain()` before its POST, exactly as `QuoteResolve` does, so a suggestion fetch never overlaps an in-flight chat-open/sync/pagination `messages/get`. (n8n is a different host, but the gate also serializes the suggestion requests amongst themselves and keeps the app from hammering during a chat switch.) This requires `ChatManager` to expose `WaitForChatFetchesToDrain()` publicly (currently private).
3. **Serial, one-in-flight suggestion requests.** Follow the `QuoteResolve` shape: a single in-flight flag, supersede-don't-parallelize on a new request (cancel/ignore the previous via `correlationId`), and abort on bot switch by snapshotting `GetActiveProfileId()` and bailing if it changed mid-request. Never fire two suggestion PULLs concurrently.
4. **Discard-on-mismatch guard.** The `correlationId` + `chatId` checks on the response mirror `CrossChatResponseGuard` for Wappi: a late suggestion for a chat the owner already left, or a superseded re-cluster, is dropped — never rendered.
5. **If suggestion context ever needs message text**, read it from the in-memory `_activeChatCache`/VMs the controller already receives — do NOT issue a fresh `messages/get`. Avoid adding any new Wappi `messages/get` caller; CONCERNS warns the gate "locks HIGH risk if new message fetches bypass" it.

## State: per-chat semi-auto toggle

**Decision: per-chat, bot-scoped, persisted in `PlayerPrefs`** — consistent with the app's bot persistence convention (`PlayerPrefs` keyed by entity name, e.g. `Bot0Name`). The toggle is per-conversation (PROJECT.md key decision), so it cannot reuse the per-bot key shape directly; it needs the chat id in the key, scoped under the bot so two bots on the same number don't collide.

**Key shape:** `{botId}_semiAuto_{chatId}` → int (0/1). Example: `Bot0_semiAuto_77011234567@c.us`.
- `botId` = the active bot's `transform.name` (the existing entity key root).
- `chatId` = `ChatManager.currentChatId` (the WhatsApp jid).
- Default = 0 (off) — semi-auto is opt-in per chat, so the panel never appears unexpectedly.

```csharp
public static class SemiAutoStore
{
    static string Key(string chatId) => $"{ActiveBotId()}_semiAuto_{chatId}";
    public static bool IsEnabled(string chatId) =>
        !string.IsNullOrEmpty(chatId) && PlayerPrefs.GetInt(Key(chatId), 0) == 1;
    public static void SetEnabled(string chatId, bool on)
    { if (string.IsNullOrEmpty(chatId)) return; PlayerPrefs.SetInt(Key(chatId), on ? 1 : 0); PlayerPrefs.Save(); }
}
```

**How it gates the panel:**
- On `OnChatSelected(chatId)` and on toggle flip: `SuggestionsController` reads `SemiAutoStore.IsEnabled(chatId)`. Off → panel hidden, events ignored, no requests. On → panel shown (empty/idle), and an initial request may fire (or wait for the next incoming message — a product choice; recommend: on enable, fire one Manual request so the owner immediately sees suggestions).
- The toggle UI lives in the chat top bar (a `ToggleRow`-style control already exists in `BotSettings/`). Flipping it persists immediately and re-evaluates the gate.

**Cleanup caveat (note for the roadmap, not a blocker):** CONCERNS flags that bot deletion must clear ALL `PlayerPrefs` keys for a bot or orphan data lingers. These new `{botId}_semiAuto_*` keys are per-chat and unbounded in count, so they cannot be enumerated by a fixed delete list in `Bot.DeleteBot`. Options: (a) prefix-scan and delete on bot delete (PlayerPrefs has no key enumeration on all platforms — unreliable), or (b) accept orphaned toggle keys as harmless (they only matter if a future chat reuses the exact jid under the same bot name). Recommend (b) for this milestone and document it; do not over-engineer.

## Scaling Considerations

| Scale | Architecture Adjustments |
|-------|--------------------------|
| 1 owner, tens of chats | Trivial. One in-flight PULL, mock or live, no concurrency. |
| Heavy incoming traffic on one open chat | Debounce auto-triggers: if incoming messages arrive in bursts, coalesce to one request per quiet window (e.g. 600ms) so re-clusters don't thrash. The correlation guard already discards superseded sets; debounce just avoids wasted n8n calls. |
| n8n/LLM latency spikes | Loading state + 30s timeout + graceful error card. Never block the composer or the chat UI; suggestions are additive. |

### Scaling Priorities
1. **First bottleneck:** auto-trigger thrash on chatty customers → add a short debounce in `SuggestionsController.Request` (coalesce rapid incoming messages).
2. **Second bottleneck:** n8n round-trip latency → keep the panel async + skeleton; consider caching the last set per chat in-memory so re-opening a chat is instant while a fresh request runs in the background.

## Anti-Patterns

### Anti-Pattern 1: Auto-sending the picked suggestion
**What people do:** Tap a card → immediately POST `message/send`.
**Why it's wrong:** Violates the core-value control guarantee; the owner loses the edit-before-send step that builds trust. Also bypasses the composer's reply-target/keyboard logic.
**Do this instead:** Load the text into the composer and let the owner edit + tap Send (Pattern 3).

### Anti-Pattern 2: A second `messages/get` caller for suggestion context
**What people do:** Fetch recent messages from Wappi to build the LLM prompt.
**Why it's wrong:** Adds a new caller to the crossing-prone `messages/get` and risks bypassing `_chatFetchesInFlight` — CONCERNS rates that HIGH risk.
**Do this instead:** Build context from the in-memory VMs the controller already has, or let n8n read history server-side. Never add a Wappi `messages/get` for this feature.

### Anti-Pattern 3: Concurrent / fire-and-forget suggestion PULLs
**What people do:** Issue a new PULL on every incoming message and every pick without superseding.
**Why it's wrong:** Parallel requests + out-of-order responses render stale suggestions; ignores the serial discipline the app enforces everywhere else.
**Do this instead:** One in-flight, supersede via `correlationId`, gate behind `WaitForChatFetchesToDrain()`, abort on bot switch (mirror `QuoteResolve`).

### Anti-Pattern 4: PUSH from n8n into the app
**What people do:** Have n8n call back into the app with suggestions.
**Why it's wrong:** The app exposes no inbound webhook (INTEGRATIONS), the coroutine model has no inbound event seam, and correlating unsolicited pushes to picks/refreshes is far harder than request/response.
**Do this instead:** PULL (decision above).

### Anti-Pattern 5: Letting the live provider leak above the seam
**What people do:** Reference `UnityWebRequest`/n8n URLs from the controller or panel "just for Phase 2."
**Why it's wrong:** Couples the UI to the backend and breaks the UI-first build order.
**Do this instead:** Everything network lives in `N8nSuggestionsProvider` behind `ISuggestionsProvider`.

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| n8n | Phase 2 PULL: outbound POST to a NEW webhook (`/webhook/SuggestReplies`) with `X-N8N-API-KEY`, JSON request body, JSON response parsed by `JsonConvert`. Same shape as existing `Create*/Edit*Workflow` calls. | New webhook + workflow are Phase-2 backend work (the "n8n automations modified" requirement). Reuse `Manager.n8nAPIKey` + 30s timeout. |
| Wappi | No new calls. Consume `ChatManager.OnLiveMessagesReceived`; gate any incidental work behind `WaitForChatFetchesToDrain()`. | Honors the confirmed crossing-bug constraints by not adding a fetcher. |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| `ChatManager` → `SuggestionsController` | Events (`OnLiveMessagesReceived`, `OnChatSelected`) + new public reads (`CurrentChatId`, `WaitForChatFetchesToDrain`) | Add the two public accessors to `ChatManager`; do not duplicate state. |
| `SuggestionsController` → composer | New `MessagesBottomPanel.LoadDraft(string)` method | Keeps composer focus/keyboard logic encapsulated; controller never touches `inputField` directly. |
| `SuggestionsController` ↔ provider | `ISuggestionsProvider` (the seam) | The swap point; Phase 1 mock, Phase 2 n8n. |
| `SuggestionsController` ↔ persistence | `SemiAutoStore` static over `PlayerPrefs` | Per-chat, bot-scoped key. |
| Send path | UNCHANGED — existing Send button → `ChatManager.SendTextMessage` | Feature only fills the draft. |

## Build-Order / Dependency Ordering Implications (for the roadmap)

The build order is fixed (Phase 1 = polished UI on MOCK, Phase 2 = n8n live), and this architecture is designed to make that clean. Suggested internal ordering:

**Phase 1 (UI + interaction on mock data — no n8n):**
1. **Data models + seam** (`SuggestionItem/Set/Request/Result`, `ISuggestionsProvider`, `SuggestionTrigger`). Foundation for everything; no dependencies.
2. **`MockSuggestionsProvider`** (canned + steered + simulated latency + correlation echo). Lets all later UI work run end-to-end.
3. **Minimal `ChatManager` seams**: add `public string CurrentChatId` and `public IEnumerator WaitForChatFetchesToDrain()` (the latter is Phase-2-facing but cheap to expose now). Low-risk one-liners.
4. **`SuggestionsPanel` + `SuggestionCardView`** (4 cards: text/label/confidence; refresh; loading/empty/error states). Built to the `unity-ui-builder` quality bar, modeled on `QuickReplyPanel`.
5. **`SemiAutoStore` + toggle UI** (per-chat persistence + top-bar toggle that gates the panel).
6. **`SuggestionsController`** wiring: subscribe to events, auto-on-incoming (isIncoming filter), manual refresh, correlation/discard guards, debounce.
7. **Composer hand-off** (`MessagesBottomPanel.LoadDraft`) + **re-cluster loop** (pick → draft + steered request). At this point the whole feature is demoable and polishable with zero backend.

**Phase 2 (n8n live):**
8. **n8n side**: new `/webhook/SuggestReplies` workflow that emits `{text,label,confidence}[]` + honors `steerTowardText` for re-cluster (the "modify automations" requirement).
9. **`N8nSuggestionsProvider`**: serial guarded PULL behind the seam (gate via `WaitForChatFetchesToDrain`, supersede via correlation, abort on bot switch).
10. **Flip the seam** (`useLiveSuggestions = true`) and validate end-to-end. No Phase-1 UI code changes.

**Critical dependency facts:**
- Steps 1–2 unblock ALL Phase-1 UI work — do them first.
- Step 3's accessors are tiny but touch `ChatManager`; doing them in Phase 1 avoids a Phase-2 edit to a high-risk file.
- The seam (step 1) is what lets steps 4–7 finish before step 8 even starts — this is the entire point of the mock-first order.
- Phase 2 touches NO Phase-1 UI: only adds `N8nSuggestionsProvider` + flips one flag. If the roadmap finds Phase-2 requiring UI changes, the seam was breached — treat as a red flag.

## Sources

- `Assets/Scripts/Main/ChatManager.cs` — `OnLiveMessagesReceived` (Action<List<MessageViewModel>>), `currentChatId`, `SendTextMessage`, `_chatFetchesInFlight`, `WaitForChatFetchesToDrain` (read 2026-06-23) — HIGH
- `Assets/Scripts/Main/ChatManager.QuoteResolve.cs` — canonical serial-drain + gate-deferral + bot-switch-abort + cache template the live provider mirrors — HIGH
- `Assets/Scripts/Chat/MessagesBottomPanel.cs`, `ExpandableInput.cs` — composer send path, input field, focus/keyboard logic — HIGH
- `Assets/Scripts/Chat/QuickReplyPanel.cs` — closest existing analog for the 4-card panel (2×2 layout, event-based pick) — HIGH
- `Assets/Scripts/Main/Bot.cs` — `PlayerPrefs` per-entity key convention informing the toggle key shape — HIGH
- `.planning/codebase/CONCERNS.md`, `.planning/codebase/INTEGRATIONS.md`, `.planning/codebase/ARCHITECTURE.md` — Wappi crossing bugs, n8n outbound-only transport, event model — HIGH
- `.planning/PROJECT.md` — feature requirements, control-guarantee, per-chat toggle, mock-first build order — HIGH

---
*Architecture research for: AI reply-suggestions panel integration (Unity WhatsApp client + n8n)*
*Researched: 2026-06-23*

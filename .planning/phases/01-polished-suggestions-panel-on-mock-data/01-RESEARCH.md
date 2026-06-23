# Phase 1: Polished Suggestions Panel on Mock Data - Research

**Researched:** 2026-06-23
**Domain:** Unity 6 (URP) mobile C# — chat UI panel, provider seam, coroutine-based mock + concurrency guard, per-chat PlayerPrefs persistence
**Confidence:** HIGH (all findings verified against the live codebase; greenfield seam with no external deps to install)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D-01:** A single tap on a suggestion card does BOTH actions at once — loads the card's reply text into the composer (editable) AND regenerates a fresh, steered set of 4 re-clustered toward that pick. INT-01 + INT-04 unified into one gesture; no separate "steer" affordance.
- **D-02:** An explicit card tap OVERWRITES whatever is in the composer (deliberate action). Distinct from auto-populate-on-incoming, which must NEVER overwrite an in-progress composer edit (INT-02). Rule: **deliberate action overwrites, automatic action defers.**
- **D-03:** Nothing ever auto-sends. Tapping only loads + re-clusters; owner edits and uses the existing Send button.
- **D-04:** Cards render as a **vertical stack of 4**, best-first top-to-bottom (NOT a 2×2 grid, NOT a horizontal scroll row).
- **D-05:** Each card's reply text caps at ~2 lines and truncates cleanly (ellipsis) per PANEL-06.
- **D-06:** Intent label is a **subtle single-accent rounded chip** (one consistent muted accent + label text), NOT per-category colors, NOT plain text.
- **D-07:** "Recommended" badge on the **top card only**; no numeric % anywhere. Placement/styling is Claude's discretion.
- **D-08:** Per-chat semi-auto toggle is an **icon toggle in the chat top bar** (open-chat header).
- **D-09:** The toggle's lit "on" state IS the persistent mode indicator. No separate banner/pill.
- **D-10:** While semi-auto is ON, the panel is **always expanded** — no manual collapse, no reopen handle.
- **D-11:** **PANEL-05 reinterpretation** — dismiss/collapse satisfied by: (a) composer is always present/usable (free-typing escape hatch), and (b) toggle OFF removes the panel entirely. There is NO separate collapse gesture/state. Treat PANEL-05 as "toggle off = hide," not a collapse handle.
- **D-12:** Loading state = **4 shimmer skeleton cards** matching the real card shape — first load AND each re-cluster. No spinner; no layout pop.
- **D-13:** On a new incoming customer message, the always-expanded panel re-populates (skeleton → fresh set). Must never overwrite a dirty composer draft (see D-02).
- **D-14:** `MockSuggestionsProvider` returns **Russian-language** replies (CIS market) covering greeting, price inquiry, availability, booking/order, polite decline. Include ≥1 deliberately long reply (truncation) and a variety of intent labels.
- **D-15:** Mock simulates realistic latency, supports steered re-cluster (fresh set given the picked reply), and includes an adversarial out-of-order/superseded path (exercises DATA-03) plus a simulated error path.

### Claude's Discretion
- Empty-state and error-state visuals within PANEL-04 (empty: "No suggestions — type your reply"; error: inline message + retry), following `unity-ui-builder`.
- Exact "Recommended" badge placement/styling, chip accent color, card padding/spacing (4px multiples), skeleton shimmer timing, panel show/hide motion (DOTween).
- Manual-refresh affordance placement (INT-03) — likely a small refresh control on the panel.
- The precise shape of `ISuggestionsProvider`, the correlation/sequence guard, and the mediating controller (within the patterns in code_context).

### Deferred Ideas (OUT OF SCOPE)
- FB-01 thumbs-up/down, FB-02 analytics, POL-01 streaming reveal, POL-02 Telegram support — all v2.
- n8n live wiring (N8N-01..04) — Phase 2, behind the same seam with ZERO Phase-1 UI edits.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SEMI-01 | Per-chat toggle to flip a chat into semi-auto | §SemiAutoToggle (header host = `MessageHeaderView`/`TopBar`); accessible-name wiring |
| SEMI-02 | State persists per chat across restarts + bot switches | §Persistence — key `{CurrentBotId}_semiAuto_{chatId}`; `PlayerPrefs.GetInt/SetInt`; read on `OnChatSelected` + `OnActiveBotChanged` |
| SEMI-03 | Panel appears only in semi-auto chats; others stay manual | §SemiAutoToggle controller reads persisted state per chat-open; default 0 (off) |
| PANEL-01 | Bottom sheet above composer in WhatsApp chat screen | §Architecture — new panel GO sibling of `quickReplyPanel` under MessagesPanel, above composer |
| PANEL-02 | 4 cards, reply text + intent label | §Card Model + §Standard Stack (`SuggestionCard`, `IntentChip`) |
| PANEL-03 | Best-first order, "Recommended" badge on top card, no % | §Card Model — provider returns ranked list; badge on index 0 only |
| PANEL-04 | Loading/empty/error states, no jank | §Panel State Machine (5 states); skeleton in-place; fixed footprint |
| PANEL-05 | Dismiss/collapse → free typing (reinterpreted D-11) | §SemiAutoToggle — toggle off = hide; composer always usable |
| PANEL-06 | Long reply text truncates cleanly | §Card Model — TMP `overflowMode=Ellipsis`, 2-line cap, explicit width clamp |
| INT-01 | Tap card loads text into composer, never auto-sends | §Composer Hand-off — set `inputField.text`; no Send call |
| INT-02 | Auto-populate on incoming, never overwrite dirty draft | §Auto-populate Trigger — `OnLiveMessagesReceived` + dirty-draft guard |
| INT-03 | Manual refresh | §Panel — refresh control issues a fresh provider request (new seq) |
| INT-04 | Pick regenerates steered set of 4 | §Provider Seam — `Refresh(steerTowardText)`; unified with INT-01 (D-01) |
| DATA-01 | `ISuggestionsProvider` seam | §Provider Seam — concrete interface proposal |
| DATA-02 | `MockSuggestionsProvider` with realistic stub data | §Mock Provider — coroutine latency, steered re-cluster, adversarial/error paths |
| DATA-03 | Reject stale/out-of-order responses; survive rapid picks + chat switches | §Correlation/Sequence Guard — adapted from QuoteResolve capture-and-discard |
| DATA-04 | `ChatManager` public current-chat accessor (+ drain hook) | §ChatManager Accessors — add `CurrentChatId` getter + public `WaitForChatFetchesToDrain` |
</phase_requirements>

## Summary

This is a **brownfield, self-contained, greenfield-seam** phase: there is **no existing suggestions code** (`grep` for `ISuggestionsProvider`/`SemiAuto`/`SuggestionCard` returns nothing), so the seam is built from scratch — but it must be modeled precisely on four established analog files so it inherits the project's proven concurrency discipline. The entire feature ships against a `MockSuggestionsProvider` with **no n8n, UnityWebRequest, or Wappi reference above the seam**; Phase 2 swaps in `N8nSuggestionsProvider` with zero UI edits, so the seam boundary must sit at a pure C# interface that takes a request value-object and returns a result value-object via a `System.Action<T>` callback (the project's universal async-result convention).

All dependencies already exist in the project — **nothing to install**: DOTween 2.2.8+, RoundedCorners (`Nobi.UiRoundedCorners`), TMPro, Newtonsoft.Json 13.0.4, NUnit3/UTF 1.6.0. The work is: (1) a pure-C# provider seam + mock; (2) a correlation/sequence guard adapted from `ChatManager.QuoteResolve.cs`'s "capture instance + id, discard crossed/superseded" pattern (suggestions have no `chatId` payload, so the guard is a monotonic request sequence + active-chat capture); (3) two new public `ChatManager` accessors (`CurrentChatId` getter — currently `private string currentChatId`; and a public drain hook wrapping the existing private `WaitForChatFetchesToDrain`); (4) the panel/card/chip/badge/skeleton UI built via an `[MenuItem]` Editor builder following `ChatsSearchBarBuilder`/`EmptyStateViewBuilder`; and (5) per-chat PlayerPrefs persistence keyed `{CurrentBotId}_semiAuto_{chatId}` read on `OnChatSelected` + `OnActiveBotChanged`.

**Primary recommendation:** Build the seam as a plain-C# interface with two value objects (`SuggestionRequest`, `SuggestionResult`) and a callback return; drive everything through a single MonoBehaviour controller (`SuggestionsController`) that owns the active-chat capture + monotonic sequence guard, subscribes to `ChatManager` events in `Awake` (header) / `OnEnable`/`OnDisable` (panel) exactly like `MessageListView`/`MessageHeaderView` do, and hands off to the composer by setting `MessagesBottomPanel.inputField.text` — never calling Send. Keep all guard/persistence/provider logic in plain testable C# classes so the EditMode harness can cover them headlessly.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Suggestion sourcing (mock now, n8n later) | Provider seam (plain C#) | — | DATA-01: UI must be backend-agnostic; the seam is the only place that knows the source |
| Stale/out-of-order rejection (DATA-03) | Controller (MonoBehaviour) | Provider (echoes correlation id) | Guard must live where the active-chat + sequence state lives; provider only echoes the id back |
| Current-chat scoping (DATA-04) | ChatManager (data/state) | Controller (reads accessor) | `currentChatId` already lives in ChatManager; expose it, don't duplicate chat state in UI |
| Per-chat semi-auto persistence (SEMI-02) | Plain C# store + PlayerPrefs | Controller (reads on chat-open) | Mirrors existing per-bot/per-chat PlayerPrefs conventions; store is unit-testable |
| Panel/card/chip/badge/skeleton rendering | UI (MonoBehaviour views) | Editor builder (constructs GOs) | uGUI canvas tier; construction via `[MenuItem]` builder per project convention |
| Auto-populate trigger (INT-02) | Controller subscribing to `OnLiveMessagesReceived` | ChatManager (event source) | Reuse the existing live-message event; add NO new Wappi `messages/get` caller (CONCERNS.md) |
| Composer hand-off (INT-01) | Controller → `MessagesBottomPanel.inputField.text` | — | Composer is owned by `MessagesBottomPanel`; hand-off is a text set, send stays manual |

## Standard Stack

### Core (all already present — nothing to install)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Unity | 6000.3.9f1 | Engine | `[CITED: STACK.md / ProjectSettings/ProjectVersion.txt]` Project's pinned version |
| DOTween | 2.2.8+ | Panel slide/fade, skeleton shimmer, card punch | `[CITED: STACK.md]` Project rule: DOTween for ALL UI motion, never Animator |
| RoundedCorners (`Nobi.UiRoundedCorners`) | (UPM, ceorkm) | Card/chip/badge corners on null-sprite Image | `[CITED: STACK.md]` Project standard; `ImageWithRoundedCorners.radius` + `ImageWithIndependentRoundedCorners.r` (Vector4) |
| TextMeshPro (TMPro) | built-in | ALL text (Cyrillic-capable existing font) | `[CITED: CONVENTIONS.md]` Never legacy Text |
| Newtonsoft.Json | 13.0.4 | Only if a serialized request/response shape is wanted (Phase-2-ready) | `[CITED: STACK.md]` Project's complex-JSON parser; mock needs none, but keep DTOs `JsonConvert`-friendly for the Phase-2 swap |
| Unity Test Framework / NUnit3 | UTF 1.6.0 / NUnit3 | EditMode tests for provider/guard/persistence | `[CITED: TESTING.md]` |

### Supporting (project analog files to model on — not libraries)
| File | Purpose | When to Use |
|------|---------|-------------|
| `Assets/Scripts/Chat/QuickReplyPanel.cs` + `QuickReplyButton.cs` | 4-item reply panel; `SetReplies`/`Clear`/`Hide` spawn/clear lifecycle | Model the panel spawn/clear; reshape to vertical stack of cards; DROP the dual-action arrow (single tap does both per D-01) |
| `Assets/Scripts/Chat/MessagesBottomPanel.cs` | Owns composer (`inputField`, `sendButton`, `SendTextMessage`), holds `quickReplyPanel`, has a DOTween slide-up bar | Hand-off target (`inputField.text`); motion analog (`DOAnchorPosY` + `OnComplete(SetActive(false))`) |
| `Assets/Scripts/Main/ChatManager.QuoteResolve.cs` | Serial, guarded PULL: capture `profileId`/`chatId`, discard crossed/superseded responses, `WaitForChatFetchesToDrain` before each fetch | The exact template for the provider fetch + correlation/sequence guard |
| `Assets/Scripts/Chat/CrossChatResponseGuard.cs` | Static pure function detecting a foreign-chat response | DATA-03 conceptual analog; NOTE: suggestions have no `chatId` payload so use a sequence guard instead |
| `Assets/Scripts/UI/MessageListView.cs` | Subscribes `OnChatSelected` in `Awake`, `OnLiveMessagesReceived`/`OnBatchMessagesLoaded` in `OnEnable`/`OnDisable` | The subscription-lifecycle template for the controller |
| `Assets/Scripts/Chat/MessageHeaderView.cs` | Open-chat header; subscribes `OnChatSelected` in `Awake`; tracks `currentChatId` | Host for the per-chat toggle; subscription lifecycle for header-bound state |
| `Assets/Editor/ChatsSearchBarBuilder.cs` / `EmptyStateViewBuilder.cs` / `BotSwitcherSheetBuilder.cs` | `[MenuItem]` builder pattern: validate selection, build GO tree, wire refs via `SerializedObject`, `AddRoundedCorners` | The construction pattern for the new panel/toggle UI |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `System.Action<SuggestionResult>` callback return | `IEnumerator` coroutine that yields the result | Callback is the project's universal async convention (`FetchData(param, callback)` per networking rule); a coroutine return forces the seam consumer to be a MonoBehaviour and leaks the runner — callback keeps the interface pure C# |
| MonoBehaviour `MockSuggestionsProvider` (needs a coroutine runner) | Plain C# class that takes a `MonoBehaviour runner` to start its latency coroutine | A MonoBehaviour provider is unit-testable only in PlayMode. Recommend: provider is plain C# implementing `ISuggestionsProvider`; the **controller** supplies the coroutine runner (`this`) — keeps the mock's *ranking/steer/error logic* pure and EditMode-testable, latency lives behind the runner. See §Mock Provider. |
| Persisting semi-auto as a new per-chat JSON file | PlayerPrefs key `{CurrentBotId}_semiAuto_{chatId}` | CONTEXT.md locks the PlayerPrefs key scheme (SEMI-02); STATE.md accepts orphaned keys on bot delete this milestone. Don't over-engineer a new store. |

**Installation:** None. All dependencies already present in the project (verified: `Assets/Plugins`, UPM, NuGet). Do not add packages.

**Version verification:** Versions cited above come from the project's own `.planning/codebase/STACK.md` (generated 2026-06-23) and `ProjectSettings/ProjectVersion.txt`. No registry lookup performed because no new packages are introduced — every dependency is already pinned in the repo. `[VERIFIED: codebase — no new packages needed]`

## Architecture Patterns

### System Architecture Diagram

```
                          ┌─────────────────────────────────────────────┐
                          │  Open-chat screen (Screen_Whatsapp/...        │
                          │  MessagesPanel)                               │
                          │                                               │
   incoming customer msg  │   ┌──────────────┐                           │
   (Wappi → ChatManager)  │   │  TopBar       │  [SemiAutoToggle icon] ───┼──► toggle on/off
            │             │   │ (MessageHeader│                           │       │
            ▼             │   │  View host)   │                           │       │ persist
  ChatManager.OnLive──────┼──►└──────────────┘                           │       ▼
  MessagesReceived        │           │ reads per-chat state           {CurrentBotId}_
  (List<MessageViewModel>)│           ▼                                  semiAuto_{chatId}
            │             │   ┌────────────────────────────────────┐     (PlayerPrefs)
            │ auto-       │   │     SuggestionsController           │
            │ populate    │   │  (MonoBehaviour, the mediator)      │
            └────────────►│   │  • capture activeChatId + botId     │
              (defers if  │   │  • monotonic _requestSeq guard      │◄──── INT-03 manual refresh
               draft      │   │  • dirty-draft guard (INT-02)       │◄──── card tap (D-01)
               dirty)     │   └───────────┬────────────────────────┘
                          │               │ Request(SuggestionRequest, seq, callback)
                          │               ▼
                          │   ===== ISuggestionsProvider SEAM =====  (no n8n/Wappi/UWR above here)
                          │               │
                          │   ┌───────────▼────────────┐   Phase 2 swaps to N8nSuggestionsProvider
                          │   │ MockSuggestionsProvider │   (zero UI edits)
                          │   │ • coroutine latency     │
                          │   │ • ranked RU stub set    │
                          │   │ • steered re-cluster     │
                          │   │ • adversarial/error path │
                          │   └───────────┬─────────────┘
                          │               │ callback(SuggestionResult{ items[], correlationId/echoedSeq })
                          │               ▼
                          │   guard: result.seq == _requestSeq && capturedChat == CurrentChatId ?
                          │      yes → render | no → DISCARD
                          │               │
                          │   ┌───────────▼─────────────────────────────────┐
                          │   │  SuggestionsPanel  (above composer)          │
                          │   │  [skeleton | 4 cards | empty | error]        │
                          │   │   card0 (Recommended badge) … card3          │
                          │   └───────────┬─────────────────────────────────┘
                          │               │ tap card (D-01): set composer text + re-cluster
                          │               ▼
                          │   ┌───────────────────────────────┐
                          │   │ MessagesBottomPanel.inputField │  (NEVER auto-sends; owner taps Send)
                          │   └───────────────────────────────┘
                          └───────────────────────────────────────────────┘
```

### Recommended Project Structure
```
Assets/Scripts/Chat/
├── ISuggestionsProvider.cs        # the seam (pure C# interface) — DATA-01
├── SuggestionRequest.cs           # value object: chatId, lastIncomingText, steerTowardText, requestSeq
├── SuggestionResult.cs            # value object: items[] (ranked), echoedSeq, status (Ok/Empty/Error)
├── SuggestionItem.cs              # value object: text, intentLabel  (matches Phase-2 { text, label })
├── MockSuggestionsProvider.cs     # DATA-02 — plain C# (+ runner for latency coroutine), RU content
├── SuggestionsController.cs       # MonoBehaviour mediator: capture+guard+dirty-draft+hand-off (DATA-03)
├── SuggestionSequenceGuard.cs     # plain C# monotonic-seq + active-chat capture (unit-testable)
└── SemiAutoStore.cs               # plain C# PlayerPrefs read/write for {botId}_semiAuto_{chatId} (SEMI-02, testable)

Assets/Scripts/UI/
├── SuggestionsPanel.cs            # spawn/clear 4 cards + skeleton + empty/error (model on QuickReplyPanel)
├── SuggestionCard.cs              # whole card = one tap target (model on QuickReplyButton, drop arrow)
└── SemiAutoToggle.cs              # top-bar icon toggle view (lit green = on)  — SEMI-01

Assets/Editor/
└── SuggestionsPanelBuilder.cs     # [MenuItem] builds panel+cards+chip+badge+skeleton+toggle, wires refs

Assets/Tests/Editor/Chat/
├── SuggestionSequenceGuardTests.cs    # discard stale/out-of-order/crossed-chat
├── SemiAutoStoreTests.cs              # key scheme, persist/restore, bot-switch isolation
└── MockSuggestionsProviderTests.cs    # ranked order, steer behavior, error path (pure logic, no latency)
```

> File placement follows STRUCTURE.md: data models + controllers + plain-C# logic in `Assets/Scripts/Chat/`; ViewModels/views in `Assets/Scripts/UI/`; builders in `Assets/Editor/`; tests in `Assets/Tests/Editor/Chat/` (no asmdef).

### Pattern 1: The Provider Seam (DATA-01) — pure C# interface + value objects
**What:** A backend-agnostic interface returning results via callback. Nothing above the seam references n8n/Wappi/UnityWebRequest.
**When to use:** The single boundary between UI/controller and suggestion sourcing.
**Proposed shape** `[ASSUMED — interface design within Claude's discretion per CONTEXT.md]`:
```csharp
// Assets/Scripts/Chat/ISuggestionsProvider.cs  — NO Unity/UWR/n8n types here
public interface ISuggestionsProvider
{
    // requestSeq rides through to the result so the controller can reject stale/out-of-order
    // replies (DATA-03). steerTowardText is null for a plain refresh, set for a re-cluster (INT-04/D-01).
    void Request(SuggestionRequest request, System.Action<SuggestionResult> callback);
}

[System.Serializable]   // Serializable so Phase-2 N8n DTOs can reuse the shape (JsonConvert-friendly)
public class SuggestionRequest
{
    public string chatId;            // captured active chat (scoping)
    public string lastIncomingText;  // the customer message that triggered this (INT-02) or null (manual/pick)
    public string steerTowardText;   // the picked reply for a re-cluster (INT-04/D-01); null = fresh set
    public long   requestSeq;        // monotonic; echoed back for the guard (DATA-03)
}

public class SuggestionResult
{
    public System.Collections.Generic.List<SuggestionItem> items; // ranked best-first (PANEL-03)
    public long requestSeq;          // echoed from the request (the correlation id)
    public SuggestionStatus status;  // Ok | Empty | Error  (drives PANEL-04 states)
}

public class SuggestionItem      // matches the Phase-2 { text, label }[] contract (N8N-01)
{
    public string text;
    public string intentLabel;   // RU category word (D-14): «Приветствие» «Цена» «Наличие» «Запись» «Отказ»
}

public enum SuggestionStatus { Ok, Empty, Error }
```
> Why a `requestSeq` field and not a payload `chatId` comparison like `CrossChatResponseGuard`: suggestions are PUSHED through a callback the controller created — there is no concurrent same-endpoint server crossing (Phase 1 is mock; Phase 2 is a single serial guarded PULL). The real risk is **rapid picks / chat switches** producing out-of-order callbacks. A monotonic sequence + captured chat id is the correct guard for THAT, and it maps cleanly onto Phase-2's correlation id (ROADMAP §Phase 2 SC-3, N8N-01).

### Pattern 2: Correlation/Sequence Guard (DATA-03) — adapted from QuoteResolve
**What:** Reject any provider callback that is not the newest request for the still-open chat.
**How QuoteResolve does it TODAY** (the template), `[VERIFIED: ChatManager.QuoteResolve.cs L82-153]`:
- Captures `string profileId = GetActiveProfileId()` and `cacheRoot` at the start of the drain.
- Before each fetch: `if (GetActiveProfileId() != profileId) break;` — abandons on bot switch.
- After the fetch: `if (GetActiveProfileId() == profileId && !string.IsNullOrEmpty(text)) Apply… else discard`.
- `yield return WaitForChatFetchesToDrain();` before the network call so it never races chat-open.

**Adapted for suggestions** `[ASSUMED — guard design within Claude's discretion]`:
```csharp
// SuggestionsController state
private long   _requestSeq;          // increments on every Request issued
private string _activeChatId;        // captured at issue time

private void IssueRequest(string steerTowardText, string lastIncomingText)
{
    long seq        = ++_requestSeq;                  // newest wins
    string chatId   = ChatManager.Instance.CurrentChatId;   // DATA-04 accessor
    _activeChatId   = chatId;
    ShowSkeleton();                                   // D-12: skeleton on every load

    var req = new SuggestionRequest {
        chatId = chatId, steerTowardText = steerTowardText,
        lastIncomingText = lastIncomingText, requestSeq = seq
    };
    _provider.Request(req, result => OnResult(seq, chatId, result));
}

private void OnResult(long seq, string capturedChatId, SuggestionResult result)
{
    // DISCARD if superseded by a newer pick/refresh, or the chat changed under us.
    if (seq != _requestSeq) return;                           // out-of-order / superseded
    if (capturedChatId != ChatManager.Instance.CurrentChatId) return; // chat switched
    Render(result);                                            // skeleton → cards | empty | error
}
```
**Drain reuse point** `[VERIFIED]`: For Phase 2's live PULL, the provider's coroutine should `yield return ChatManager.Instance.WaitForChatFetchesToDrain()` (the public hook added by DATA-04) before its UnityWebRequest, exactly as `QuoteResolve.DrainQuoteResolveQueue` does at L104. In Phase 1 the mock has no real network, so the drain is optional but the **seam signature must already allow it** (the provider holds a runner). The guard above (seq + chat capture) lives in the controller and is provider-agnostic, so it works identically for mock and live.

### Pattern 3: Auto-populate Trigger (INT-02) — `OnLiveMessagesReceived`
**What:** When a new incoming customer message arrives, re-populate the panel (skeleton → fresh set), but never clobber a draft the owner is editing.
**Event confirmed** `[VERIFIED: ChatManager.cs L55]`: `public event Action<List<MessageViewModel>> OnLiveMessagesReceived;` — "Brand-new messages appended (only ever adds)" (chat-data-flow skill). Fires during `Populate` after `OnBatchMessagesLoaded` and on each live sync (L751, L1014).
**Subscription lifecycle** (model on `MessageListView` `[VERIFIED: MessageListView.cs L102-134]`): subscribe in `OnEnable`, unsubscribe in `OnDisable`. (Header-bound `OnChatSelected` subscriptions go in `Awake` because the panel is inactive between chats — see `MessageHeaderView`/`MessageListView`.)
```csharp
void OnEnable()  { if (ChatManager.Instance != null) ChatManager.Instance.OnLiveMessagesReceived += HandleLive; }
void OnDisable() { if (ChatManager.Instance != null) ChatManager.Instance.OnLiveMessagesReceived -= HandleLive; }

private void HandleLive(List<MessageViewModel> msgs)
{
    if (!_semiAutoOn) return;                       // SEMI-03: only semi-auto chats
    // Only react to INCOMING messages — outgoing echoes also flow through this event.
    bool anyIncoming = msgs != null && msgs.Exists(m => m != null && m.isIncoming);
    if (!anyIncoming) return;
    var last = LastIncoming(msgs);                   // newest incoming for lastIncomingText
    IssueRequest(steerTowardText: null, lastIncomingText: last?.text);   // automatic → defers (see dirty guard)
}
```
**"Never overwrite a dirty composer draft" mechanics** `[ASSUMED — detection mechanism is within discretion]`: *Auto-populate refreshes the CARDS regardless* (the panel is always-expanded, D-13). The "never overwrite" rule applies to the **composer text**, not the cards — and auto-populate never writes the composer anyway (only a card TAP does, D-01/INT-01). So INT-02's guard is simply: **auto-populate never touches `inputField.text`.** A composer is "dirty" when `!string.IsNullOrEmpty(inputField.text.Trim())`; that state only matters for the card-tap path if you ever wanted to confirm-before-overwrite — but D-02 locks "explicit tap overwrites," so no confirmation. Net mechanics:
- Auto-populate (INT-02): refresh cards, **do not** set composer text. ✓ never overwrites.
- Card tap (INT-01/D-01): always sets composer text (overwrite is intended). ✓
> This resolves the apparent tension: the dirty-draft rule is satisfied by *not writing the composer on the automatic path at all*, which is strictly safer than detecting dirtiness. Document this explicitly so the planner doesn't build an unneeded dirty-detection branch.

### Pattern 4: Composer Hand-off (INT-01 / D-01) — set `inputField.text`, never send
**What:** Tapping a card loads its reply into the editable composer.
**Mechanism** `[VERIFIED: MessagesBottomPanel.cs L11 (public TMP_InputField inputField), L113-128 (OnSendClicked is the ONLY send path, triggered by the Send button's PointerDown)]`:
```csharp
// SuggestionsController holds a reference to the MessagesBottomPanel
_bottomPanel.inputField.text = card.replyText;   // hand-off — composer's onValueChanged flips Send button on
_bottomPanel.inputField.ActivateInputField();    // optional: focus for immediate edit (matches reply-preview UX)
// DO NOT call ChatManager.SendTextMessage — only the Send button does (D-03).
```
> `inputField` is already `public` on `MessagesBottomPanel`. Setting `.text` fires the existing `onValueChanged` listener (`UpdateButtonState`) which reveals the Send button. Nothing auto-sends. `[VERIFIED]`

### Pattern 5: Per-chat Semi-Auto Toggle + Persistence (SEMI-01/02/03)
**Host** `[VERIFIED: Main.unity L14989 TopBar (sizeDelta.y=284) → L15028 MessageHeaderView]`: the open-chat header `TopBar` is the per-chat header (NOT `ChatsPanel/TopBar`, which is the chat-LIST header). It already hosts `MessageHeaderView` with 3 children; add the toggle as a sibling. `MessageHeaderView` subscribes `OnChatSelected` in `Awake` and tracks `currentChatId` — the toggle controller reads persisted state on that event.
**Key scheme** `[VERIFIED: CONTEXT.md D + bot-persistence skill + ChatManager.BotState.cs L14 `public string CurrentBotId`]`:
```csharp
// SemiAutoStore.cs — plain C#, unit-testable
private static string Key(string botId, string chatId) => $"{botId}_semiAuto_{chatId}";
public static bool IsOn(string botId, string chatId)
    => PlayerPrefs.GetInt(Key(botId, chatId), 0) == 1;     // default OFF (SEMI-03: others stay manual)
public static void Set(string botId, string chatId, bool on)
{
    PlayerPrefs.SetInt(Key(botId, chatId), on ? 1 : 0);
    PlayerPrefs.Save();                                     // mobile gets killed; flush (bot-persistence skill)
}
```
- `botId` = `ChatManager.Instance.CurrentBotId` (already public). `chatId` = `ChatManager.Instance.CurrentChatId` (DATA-04 adds it).
- **Read on chat open** (`OnChatSelected`) and **on bot switch** (`OnActiveBotChanged` — L98/L121) so the lit state and panel visibility track the active chat/bot (SEMI-02/03).
- **Restart**: PlayerPrefs persists; the first `OnChatSelected` after launch reads the stored value. ✓
- **Orphaned keys on bot delete**: accepted this milestone (STATE.md blocker note) — do NOT add enumeration/cleanup.
**Panel show/hide off the toggle** (D-10/D-11): on = panel active + first `IssueRequest`; off = `panel.Hide()` (slide-out, DOTween). No collapse handle. Composer always present.

### Pattern 6: ChatManager Accessors (DATA-04)
**What's needed** `[VERIFIED: ChatManager.cs L139 `private string currentChatId;`, L1300 `private IEnumerator WaitForChatFetchesToDrain()`]`:
```csharp
// Add to ChatManager.cs (or a small ChatManager partial to keep the diff isolated):
public string CurrentChatId => currentChatId;                 // DATA-04 read accessor
public IEnumerator WaitForChatFetchesDrain() => WaitForChatFetchesToDrain();  // public drain hook for Phase-2 provider
```
> `CurrentBotId` is ALREADY public (`ChatManager.BotState.cs L14`) — only `CurrentChatId` and a public drain wrapper are missing. Keep the existing private members; expose read-only. This is the minimal, non-invasive change required (and the only edit to ChatManager beyond subscriptions).

### Pattern 7: Panel State Machine (PANEL-04) — 5 states, fixed footprint
**States:** `Skeleton` (D-12, first load + every re-cluster) → `Cards` (4, ranked) | `Empty` («Нет предложений / Напишите ответ вручную») | `Error` («Не удалось загрузить…» + «Обновить»). The panel keeps its footprint in all states so there is no layout pop. Skeleton cards match the real card shape exactly (D-12). Driven by `SuggestionResult.status` + the guard.

### Anti-Patterns to Avoid
- **Referencing Wappi/n8n/UnityWebRequest above the seam.** The controller, panel, and views must only know `ISuggestionsProvider`. Any such reference in Phase-1 UI is a seam breach that will force a Phase-2 UI edit. `[CITED: ROADMAP §Phase 1 SC-5]`
- **Building a dirty-draft detection branch for INT-02.** Unneeded — auto-populate simply never writes the composer (see Pattern 3). Adding detection invents complexity the locked decisions don't require.
- **Adding a new Wappi `messages/get` caller for auto-populate.** Forbidden (CONCERNS.md): subscribe to the existing `OnLiveMessagesReceived` instead. `[CITED: CONCERNS.md Wappi concurrent /messages/get crossing]`
- **TMP-drawn glyph icons for the toggle/refresh/badge mark.** They silently don't render — every icon is an `Image` + sprite. `[CITED: UI-SPEC + unity-ui-builder]`
- **`UISprite.psd` on card/panel surfaces.** Blurs edges — use a null-sprite Image + RoundedCorners. `[CITED: project memory feedback_ui_building]`
- **Mockup-pixel sizes.** All sizes are 1080×1920 reference units (dp×3); body text ≈ 42, not 16. `[CITED: unity-ui-builder]`
- **Making `MockSuggestionsProvider` a MonoBehaviour with hard-coded ranking logic.** Keep the ranking/steer/error logic in plain C# so EditMode tests cover it; pass a coroutine runner for latency only.
- **Calling `ChatManager.SendTextMessage` from a card tap.** Violates D-03 (never auto-send). Only the existing Send button sends.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Card/chip/badge rounded corners | Custom mask/sprite-9-slice | `Nobi.UiRoundedCorners.ImageWithRoundedCorners` (radius) / `ImageWithIndependentRoundedCorners` (Vector4 `.r` for top-only) | Project standard; mask is expensive and blurs `[CITED: STACK.md, BotSwitcherSheetBuilder.cs L195/567]` |
| Panel slide/fade + skeleton shimmer | Manual lerp in Update | DOTween `DOAnchorPosY`/`DOFade`/looping tween | Project rule: DOTween for all motion `[CITED: UI-SPEC Animation table]` |
| Constructing the panel GO tree by hand in the scene | Manual hierarchy editing | An `[MenuItem]` Editor builder (model on `ChatsSearchBarBuilder`) wiring refs via `SerializedObject` | Project's established construction pattern; reproducible, rewires consumers `[CITED: STRUCTURE.md "New Page", project memory builders-must-rewire-consumers]` |
| Concurrency/stale-response logic | A fresh ad-hoc flag soup | The QuoteResolve capture-instance-and-discard template (here: monotonic seq + chat capture) | Proven pattern already battle-tested against the exact class of bug `[CITED: ChatManager.QuoteResolve.cs]` |
| Per-chat boolean persistence | A new JSON store/migration | PlayerPrefs `{botId}_semiAuto_{chatId}` | Matches existing per-bot/per-chat convention; STATE.md locks it `[CITED: CONTEXT.md, bot-persistence skill]` |
| Keyboard-aware panel positioning | New keyboard listener | Sit the panel inside MessagesPanel above the composer; respect existing `KeyboardAwarePanel` | Baked safe-area + keyboard tracking already solved `[CITED: KeyboardAwarePanel.cs, UI-SPEC Host surface]` |

**Key insight:** Almost nothing here is genuinely new — the value is in *correctly reshaping* four proven analogs (QuickReplyPanel spawn/clear, MessagesBottomPanel hand-off+motion, QuoteResolve guard, MessageListView subscription lifecycle) and adding a thin pure-C# seam. The only true greenfield code is the interface + value objects + mock content; everything else is a measured adaptation.

## Runtime State Inventory

> This is a greenfield additive phase (no rename/refactor/migration). New PlayerPrefs keys are CREATED, not migrated. Inventory included for completeness because SEMI-02 introduces persisted state.

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | NEW per-chat keys `{CurrentBotId}_semiAuto_{chatId}` in PlayerPrefs. No existing semi-auto data to migrate (greenfield). | Code-create on toggle; default-off read. None to migrate. |
| Live service config | None — Phase 1 has NO n8n/Wappi config changes (all mock). The seam keeps n8n entirely in Phase 2. | None. |
| OS-registered state | None — no Task Scheduler/launchd/pm2 involvement. | None. |
| Secrets/env vars | None — no secrets touched; mock provider needs no keys. | None. |
| Build artifacts / installed packages | None — no new packages, no pyproject/egg/npm artifacts. New `.cs` files compile into existing assemblies (`Assembly-CSharp`, tests into `Assembly-CSharp-Editor`). | Note Unity new-file import quirk (see Pitfall 4). |

**Orphaned-key note** `[VERIFIED: STATE.md blocker note]`: deleting a bot leaves its `{botId}_semiAuto_{chatId}` keys behind. This is **explicitly accepted this milestone** — do not build enumeration/cleanup. PlayerPrefs keys are not reliably enumerable.

## Common Pitfalls

### Pitfall 1: Seam leak (Wappi/n8n/UWR reference above the seam)
**What goes wrong:** A panel or controller imports `UnityWebRequest`/n8n URL/Wappi auth "just to wire it up," and Phase 2 then needs a UI edit.
**Why it happens:** Convenience — the existing networking pattern lives in ChatManager, tempting to call directly.
**How to avoid:** Controller/panel reference `ISuggestionsProvider` ONLY. The mock implements it. Grep the new UI files for `UnityWebRequest|wappi|n8n|X-N8N` before declaring done — must be empty. `[CITED: ROADMAP SC-5]`
**Warning signs:** Any `using UnityEngine.Networking;` in `SuggestionsController.cs`, `SuggestionsPanel.cs`, or `SemiAutoToggle.cs`.

### Pitfall 2: `_chatFetchesInFlight` gate lock (Phase-2-facing, design now)
**What goes wrong:** A future live provider does a `messages/get`-style fetch that skips the `_chatFetchesInFlight++ / Mathf.Max(0, --)` pair, permanently locking the gate (3s timeout then stale data).
**Why it happens:** The gate is a manual reference counter `[VERIFIED: ChatManager.cs L137/L515/L517/L1303]`.
**How to avoid:** The Phase-1 mock does NOT touch the gate (no Wappi call). The DATA-04 public drain hook lets the Phase-2 provider *wait on* the gate without incrementing it (it's a different endpoint, not `messages/get`). Document that the suggestions provider must never increment `_chatFetchesInFlight` — it only waits.
**Warning signs:** Any new `_chatFetchesInFlight++` outside ChatManager's existing message-fetch coroutines.

### Pitfall 3: Subscribing in the wrong lifecycle method
**What goes wrong:** Subscribing to `OnChatSelected` in `OnEnable` misses the event, because the MessagesPanel is **inactive** between chats and during the `Prep` phase when `OnChatSelected` fires.
**Why it happens:** `SelectChat` fires `OnChatSelected` during `Prep`, before `OpenChatRoutine` activates the panel `[VERIFIED: MessageHeaderView.cs comment L21-26, MessageListView.cs L82-86]`.
**How to avoid:** Subscribe to `OnChatSelected`/`OnActiveBotChanged` in **`Awake`** (they may fire while inactive); subscribe to `OnLiveMessagesReceived`/`OnBatchMessagesLoaded` in **`OnEnable`** (only fire while active). Unsubscribe symmetrically (`OnDisable`/`OnDestroy`).
**Warning signs:** Toggle state not restored on first chat-open after launch; panel not appearing for a known semi-auto chat.

### Pitfall 4: Unity new-file import quirk (brand-new test `.cs` silently excluded)
**What goes wrong:** A newly created `.cs` (especially a test) is silently excluded from Bee compilation if written during a busy refresh — type "not found", tests don't run despite a clean compile.
**Why it happens:** Documented Unity 6 quirk `[CITED: project memory project_unity_new_file_import]`.
**How to avoid:** If a new file's type isn't found, delete the `.cs` + `.meta`, let Unity register the deletion, recreate it. Editing existing files is unaffected.
**Warning signs:** Tests "0/0 green" or a compile-clean "type not found" on a file you just created.

### Pitfall 5: RoundedCorners type resolution at runtime
**What goes wrong:** `Type.GetType("Nobi.UiRoundedCorners...,Assembly-CSharp")` returns null (the type is in its OWN UPM assembly), giving square corners.
**Why it happens:** Known project bug class `[CITED: project memory project_roundedcorners_assembly]`.
**How to avoid:** In the **Editor builder**, `using Nobi.UiRoundedCorners;` and `AddComponent<ImageWithRoundedCorners>()` directly (compile-time ref works — `BotSwitcherSheetBuilder.cs` does exactly this). Only runtime reflection needs the AppDomain scan; the builder approach sidesteps it. Set radius ≈ half min-dimension for pills/chips, ~24u for cards (UI-SPEC).
**Warning signs:** Square corners on cards/chips/badge after the build menu item runs.

### Pitfall 6: Card width blow-out from long reply text (PANEL-06)
**What goes wrong:** A long RU reply (the deliberately-long mock entry per D-14) widens the card or wraps to many lines, breaking the stack.
**Why it happens:** TMP with no width clamp + `NoWrap` measures the full line `[CITED: project memory project_quoted_reply_card — same class of bug]`.
**How to avoid:** Set an explicit width clamp on the card text (do NOT rely on NoWrap), `overflowMode = Ellipsis`, and a 2-line `maxVisibleLines` cap; fix card height by layout so the stack never reflows (UI-SPEC Typography). Include the long-reply mock entry specifically to exercise this.
**Warning signs:** One card taller/wider than the others; horizontal overflow; text spilling past the card.

### Pitfall 7: Outgoing echoes re-triggering auto-populate
**What goes wrong:** `OnLiveMessagesReceived` fires for the owner's OWN sent messages too (it "only ever adds," including outgoing echoes), causing a spurious re-cluster right after the owner sends.
**Why it happens:** The event carries any brand-new messages, incoming or outgoing `[VERIFIED: ChatManager.cs L94-96 comment, chat-data-flow skill]`.
**How to avoid:** In `HandleLive`, only act when the batch contains an **incoming** message (`m.isIncoming == true`) — see Pattern 3.
**Warning signs:** Cards reshuffle immediately after the owner sends a message.

## Code Examples

Verified analog patterns the tasks will reuse:

### Spawn/clear lifecycle (model the panel on this)
```csharp
// Source: Assets/Scripts/Chat/QuickReplyPanel.cs L56-110  [VERIFIED]
public void SetReplies(List<(string text, bool isOutgoing)> replies) { Clear(); gameObject.SetActive(true); /* build rows */ }
public void Clear() { foreach (var b in _buttons) if (b) Destroy(b.gameObject); _buttons.Clear(); /* destroy containers */ }
public void Hide()  { Clear(); gameObject.SetActive(false); }
```

### DOTween slide-up + hide-on-complete (panel show/hide motion analog)
```csharp
// Source: Assets/Scripts/Chat/MessagesBottomPanel.cs L166-182  [VERIFIED]
private void ShowPreview() { _tween?.Kill(); bar.SetActive(true);
    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, _hiddenY);
    _tween = rt.DOAnchorPosY(_restY, 0.25f).SetEase(Ease.OutCubic); }
private void HidePreview() { _tween?.Kill();
    _tween = rt.DOAnchorPosY(_hiddenY, 0.2f).SetEase(Ease.InCubic).OnComplete(() => bar.SetActive(false)); }
```

### Editor builder: build tree, wire refs via SerializedObject (rewire consumers)
```csharp
// Source: Assets/Editor/ChatsSearchBarBuilder.cs L60-85  [VERIFIED]
var bar = row.AddComponent<ChatSearchBar>();
var so = new SerializedObject(bar);
so.FindProperty("input").objectReferenceValue = input;
so.FindProperty("clearButton").objectReferenceValue = clearButton;
so.ApplyModifiedPropertiesWithoutUndo();
EditorUtility.SetDirty(content);
// + Undo.SetCurrentGroupName / CollapseUndoOperations, and a "already exists? abort" guard.
```

### RoundedCorners in a builder (direct using, no reflection)
```csharp
// Source: Assets/Editor/BotSwitcherSheetBuilder.cs L2, L195-196, L565-568  [VERIFIED]
using Nobi.UiRoundedCorners;
var topRounded = panel.AddComponent<ImageWithIndependentRoundedCorners>();
topRounded.r = new Vector4(TopCornerRadius, TopCornerRadius, 0f, 0f);   // top-only
private static void AddRoundedCorners(GameObject go, float radius) {
    var rc = go.AddComponent<ImageWithRoundedCorners>(); rc.radius = radius; }
```

### EditMode test shape (no asmdef; pure-C# logic under test)
```csharp
// Source: Assets/Tests/Editor/Chat/OutboxStoreTests.cs pattern (TESTING.md)  [CITED]
using NUnit.Framework;
public class SuggestionSequenceGuardTests
{
    [Test] public void OlderSeq_IsDiscarded() { /* arrange seq=2 current; act result seq=1; assert discarded */ }
    [Test] public void ChatSwitchedUnderRequest_IsDiscarded() { /* captured chat != current → discard */ }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| QuickReplyPanel dual-action button (text + arrow steer) | Single tap does both (load + re-cluster), no arrow (D-01) | This phase | Drop `QuickReplyButton.arrowButton`; whole card = one tap target |
| 2×2 grid of quick replies | Vertical stack of 4 ranked cards (D-04) | This phase | Layout is a `VerticalLayoutGroup`, not two `HorizontalLayoutGroup` rows |
| Manual reply text only | Provider-seamed suggestions (DATA-01) | This phase | New `ISuggestionsProvider` boundary; backend-swappable |
| `private currentChatId` (no external read) | Public `CurrentChatId` accessor (DATA-04) | This phase | Minimal additive ChatManager change |

**Deprecated/outdated:** None — no library deprecations relevant; all deps are current per STACK.md (2026-06-23).

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Provider seam shape: `void Request(SuggestionRequest, Action<SuggestionResult>)` with `requestSeq` echo | Pattern 1 | LOW — explicitly Claude's discretion (CONTEXT.md); any callback-based shape that carries a correlation id satisfies DATA-01/03. Confirm field names with planner. |
| A2 | The correlation guard is a monotonic sequence + captured chatId (not a payload chatId compare) | Pattern 2 / DATA-03 | LOW — driven by the fact that suggestions have no `chatId` payload (verified) and the risk is rapid picks/switches, not server crossing. Aligns with Phase-2 correlation id (ROADMAP SC-3). |
| A3 | INT-02's "never overwrite dirty draft" is satisfied by auto-populate never writing the composer at all | Pattern 3 | MEDIUM — if the product intended auto-populate to ALSO pre-fill the composer (and only then defer on dirty), a dirty-detection branch would be needed. CONTEXT.md D-01/D-02/D-13 strongly imply cards-only on the automatic path; confirm with discuss-phase. |
| A4 | `MockSuggestionsProvider` is plain C# + a passed-in coroutine runner (not a MonoBehaviour) | Mock Provider / Standard Stack | LOW — keeps ranking/steer/error logic EditMode-testable; latency via runner. Alternative (MonoBehaviour) only costs testability. |
| A5 | Toggle host is the open-chat `TopBar` (MessageHeaderView), not `ChatsPanel/TopBar` | Pattern 5 | LOW — D-08 says "chat top bar"; per-chat state belongs on the open-chat header (verified MessageHeaderView is there). Confirm the exact sibling/child placement visually in the builder. |
| A6 | `requestSeq` is a single controller-wide counter (not per-chat) | Pattern 2 | LOW — a chat switch is caught by the chatId capture check, so a global counter is sufficient and simpler. |

**If A3 is the only MEDIUM:** It is the single decision most worth a one-line confirmation from the user before planning, because it changes whether a dirty-draft detection branch exists.

## Open Questions (RESOLVED)

1. **RESOLVED: Auto-populate (INT-02) refreshes cards ONLY and never writes the composer.**
   - Resolution: Locked decisions D-02 ("automatic action defers") + D-13 ("incoming re-populates the CARDS; must never overwrite a dirty composer draft") settle this. "Defers" = never writes the composer; the composer is written ONLY by a deliberate card tap (D-01). No dirty-draft detection branch is needed — the guarantee holds structurally (auto-populate never touches `inputField.text`). Implemented in Plan 01-04 (`HandleLive` issues a request only). Confirmed by orchestrator against CONTEXT.md before planning.

2. **RESOLVED: "Recommended" badge = top-right pill on card 0 only** (Claude's discretion per D-07).
   - Resolution: top card only, green pill, «Рекомендуем», no numeric %. Top-right corner placement; builder/ui-checker finalize visually. Implemented in Plan 01-03.

3. **RESOLVED: Refresh control = icon-only circular-arrow `Image` in the panel header row** (INT-03, Claude's discretion).
   - Resolution: accessible label «Обновить», per UI-SPEC; issues a fresh request (new seq, `steerTowardText=null`). Implemented in Plan 01-03 builder + Plan 01-04 wiring.

## Environment Availability

> Phase is pure in-engine C#/UI with a mock data source — no external runtime services, no network, no new tooling. The only "environment" dependency is the Unity Editor + test harness, which the project already uses.

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Unity Editor | build + EditMode tests | ✓ | 6000.3.9f1 | — |
| DOTween | panel/skeleton motion | ✓ | 2.2.8+ (Assets/Plugins/Demigiant) | — |
| RoundedCorners (Nobi.UiRoundedCorners) | card/chip/badge corners | ✓ | UPM (ceorkm) | — |
| TextMeshPro | all text (Cyrillic font) | ✓ | built-in | — |
| Newtonsoft.Json | optional DTO serialization (Phase-2-ready) | ✓ | 13.0.4 | mock needs none |
| Unity Test Framework / NUnit3 | EditMode tests | ✓ | UTF 1.6.0 | in-Editor bridge or headless script |
| n8n / Wappi / network | NONE in Phase 1 (mock) | n/a | — | the mock IS the fallback by design |

**Missing dependencies with no fallback:** None.
**Missing dependencies with fallback:** None — Phase 1 is deliberately backend-free.

**Test harness availability** `[VERIFIED: TESTING.md, CLAUDE.md]`:
- Editor closed: `Tools/run-tests-headless.sh` (cold batch; outputs to `Tools/test-output/`, exit 0=green).
- Editor open: drop `Temp/claude/run-tests.trigger` → read `Temp/claude/test-summary.json`.
- Unit-testable headlessly: `SemiAutoStore` (PlayerPrefs key scheme — note PlayerPrefs in EditMode writes to the editor registry; prefer a thin injectable seam or unique key prefixes per test + cleanup), `SuggestionSequenceGuard` (pure logic), `MockSuggestionsProvider` ranking/steer/error logic (latency excluded). NOT headlessly testable: panel rendering, DOTween motion, toggle visuals, keyboard positioning — verify on device/Editor.

## Project Constraints (from CLAUDE.md)

These directives carry the same authority as locked decisions — the planner must not recommend approaches that contradict them.

- **Coroutines only — never async/await in MonoBehaviours.** The mock's latency uses `IEnumerator` + `WaitForSeconds` via a runner. `[CITED: CLAUDE.md, .claude/rules/networking.md]`
- **Never hardcode API keys/tokens** — not relevant Phase 1 (no network), but the seam must not bake any future URL/key.
- **All UI text is `TextMeshProUGUI`; all motion is DOTween; UI refs are `[SerializeField] private`.** `[CITED: .claude/rules/ui-scripts.md]` (Note: `MessagesBottomPanel.inputField` is `public` — that's an existing pattern we consume, not a new public field we add.)
- **Singleton managers (`ChatManager.Instance`); event-driven UI (subscribe OnEnable/Awake, unsubscribe OnDisable/OnDestroy); no polling.** `[CITED: CLAUDE.md Conventions]`
- **Programmatic UI via `[MenuItem]` Editor builders** following the existing pattern; **builders must rewire serialized consumers** via `SerializedObject`. `[CITED: CLAUDE.md, project memory]`
- **PlayerPrefs per-bot/per-chat persistence**, key derived from live refs (`CurrentBotId`), `PlayerPrefs.Save()` after writes. `[CITED: bot-persistence skill]`
- **New API endpoints (Phase 2) follow the UnityWebRequest+coroutine+`JsonConvert` pattern, models in `Assets/Scripts/Chat/`** — keep the seam DTOs in that folder and `JsonConvert`-friendly so the Phase-2 provider drops in. `[CITED: CLAUDE.md, networking rule]`
- **Code quality: methods <30 lines where possible, string interpolation, explicit null handling, `[SerializeField] private`.** `validate-cs.sh` runs after every Edit/Write. `[CITED: csharp-quality + unity-general rules]`
- **Do NOT refactor Manager.cs / the PlayerPrefs entity store** — out of scope (REQUIREMENTS Out-of-Scope, CONCERNS.md). `[CITED]`

## Sources

### Primary (HIGH confidence — direct codebase reads this session)
- `Assets/Scripts/Chat/QuickReplyPanel.cs`, `QuickReplyButton.cs` — spawn/clear + dual-action button (to reshape)
- `Assets/Scripts/Chat/MessagesBottomPanel.cs` — composer `inputField` (public), `OnSendClicked` (only send path), DOTween slide-up motion
- `Assets/Scripts/Main/ChatManager.cs` — `OnLiveMessagesReceived` (L55), `currentChatId` (L139, private), `_chatFetchesInFlight` gate (L137/515/517/1303), `WaitForChatFetchesToDrain` (L1300, private), `SendTextMessage` (L1822)
- `Assets/Scripts/Main/ChatManager.QuoteResolve.cs` — serial guarded PULL + capture/discard template (L82-153)
- `Assets/Scripts/Main/ChatManager.BotState.cs` — `CurrentBotId` (L14, public), `OnActiveBotChanged` flow
- `Assets/Scripts/Chat/CrossChatResponseGuard.cs` — foreign-chat detection (DATA-03 analog)
- `Assets/Scripts/UI/MessageListView.cs` — subscription lifecycle (Awake vs OnEnable)
- `Assets/Scripts/Chat/MessageHeaderView.cs` — open-chat header host, Awake subscription, `currentChatId`
- `Assets/Scripts/UI/MessageViewModel.cs` — `isIncoming`, `text`, `senderName` fields for the trigger
- `Assets/Editor/ChatsSearchBarBuilder.cs`, `BotSwitcherSheetBuilder.cs`, `EmptyStateViewBuilder.cs` — builder + RoundedCorners + SerializedObject wiring
- `Assets/Scenes/Main.unity` L14989/15028 — open-chat `TopBar` hosts `MessageHeaderView`
- `Assets/Scripts/Chat/KeyboardAwarePanel.cs`, `ExpandableInput.cs` — positioning host

### Secondary (HIGH confidence — project planning + skill docs)
- `.planning/codebase/{STACK,STRUCTURE,CONVENTIONS,CONCERNS,TESTING}.md` (generated 2026-06-23)
- `.claude/skills/{unity-ui-builder,bot-persistence,chat-data-flow}/SKILL.md`
- `.claude/rules/{networking,ui-scripts,unity-general,csharp-quality}.md`
- `CLAUDE.md`, `CLAUDE.local.md`, project auto-memory (RoundedCorners assembly, new-file import quirk, quoted-card width clamp, builders-rewire-consumers, doodle colors)
- `.planning/{REQUIREMENTS,ROADMAP,STATE}.md`, phase `01-CONTEXT.md`, `01-UI-SPEC.md`

### Tertiary (LOW confidence — none)
- No web/Context7 lookups were needed: the seam is greenfield C#, all dependencies are already pinned in-repo, and every claim is verified against the live codebase. No unverified third-party claims in this research.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all deps verified present in-repo; no installs; versions from project STACK.md.
- Architecture/seam: HIGH on the boundary + guard pattern (modeled on verified analogs); the exact interface field names are `[ASSUMED]` within Claude's discretion (A1).
- Pitfalls: HIGH — all sourced from verified code or documented project memory.
- INT-02 dirty-draft mechanics: MEDIUM — interpretation (A3) is the one item worth a user confirm.

**Research date:** 2026-06-23
**Valid until:** 2026-07-23 (stable — brownfield, no fast-moving external deps; re-verify only if ChatManager event signatures or the open-chat header structure change)

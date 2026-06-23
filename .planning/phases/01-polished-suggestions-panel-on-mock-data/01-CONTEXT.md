# Phase 1: Polished Suggestions Panel on Mock Data - Context

**Gathered:** 2026-06-23
**Status:** Ready for planning

<domain>
## Phase Boundary

Build a fully functional, visually polished Reply Suggestions Panel in the WhatsApp chat screen — per-chat semi-auto toggle, 4 ranked cards, pick→composer hand-off, manual refresh, and the re-cluster steering loop — running end-to-end against a `MockSuggestionsProvider` behind the `ISuggestionsProvider` seam. Demoable and shippable with NO backend. The seam, the stale-response/correlation guards, the per-chat toggle + persistence, and the public `ChatManager` accessors all live in this phase so it is self-contained.

Requirements in scope: SEMI-01, SEMI-02, SEMI-03, PANEL-01..06, INT-01..04, DATA-01..04 (see REQUIREMENTS.md). The n8n live wiring (N8N-*) is Phase 2 and must require zero Phase-1 UI edits (a UI edit in Phase 2 = seam breach).

</domain>

<decisions>
## Implementation Decisions

### Interaction (the pick / edit / re-cluster loop)
- **D-01:** A single tap on a suggestion card does BOTH actions at once — it loads the card's reply text into the composer (editable) AND regenerates a fresh, steered set of 4 suggestions re-clustered toward that pick. INT-01 and INT-04 are unified into one gesture; there is no separate "steer" affordance on the card.
- **D-02:** An explicit card tap OVERWRITES whatever is currently in the composer (a tap is a deliberate choice). This is distinct from auto-populate-on-incoming, which must NEVER overwrite an in-progress composer edit (INT-02). The rule: **deliberate action overwrites, automatic action defers.**
- **D-03:** Nothing ever auto-sends. Tapping only loads + re-clusters; the owner edits the composer draft and uses the existing Send button (INT-01).

### Card layout & anatomy
- **D-04:** Cards render as a **vertical stack of 4**, best-first top-to-bottom (NOT a 2×2 grid, NOT a horizontal scroll row). Top-down order makes the best-first ranking and the top-card badge read naturally and gives each card full width for text + chip + badge.
- **D-05:** Each card's reply text caps at roughly 2 lines and truncates cleanly (ellipsis) per PANEL-06 — long text must not break layout.
- **D-06:** The intent label is a **subtle single-accent rounded chip** (one consistent muted accent color + label text), NOT per-category colors and NOT plain text. Keeps the panel clean and on-brand.
- **D-07:** The "Recommended" badge appears on the **top card only** (locked decision; no numeric % anywhere). Exact placement/styling is Claude's discretion (e.g. a small pill in the top corner).

### Semi-auto toggle & mode indicator
- **D-08:** The per-chat semi-auto toggle is an **icon toggle in the chat top bar** (the WhatsApp chat header), where per-chat mode controls belong. Most discoverable; persistent.
- **D-09:** The toggle's lit "on" state IS the persistent mode indicator — it tells the owner this chat is in semi-auto. No separate banner/pill.

### Panel visibility & states
- **D-10:** While semi-auto is ON, the panel is **always expanded** — there is no manual collapse and no reopen handle. (This supersedes the Area-3 "lit toggle + reopen handle" idea and reinterprets PANEL-05: see D-11.)
- **D-11:** **PANEL-05 reinterpretation** — "dismiss/collapse so the owner can fall back to free typing" is satisfied by: (a) the composer is always present and usable, so the owner can type freely and ignore the cards at any time, and (b) flipping the top-bar toggle OFF removes the panel entirely. There is no separate collapse gesture/state. Downstream planners: treat PANEL-05 as "toggle off = hide," not a collapse handle.
- **D-12:** Loading state = **4 shimmer skeleton cards** matching the real card shape — shown on first load and during each re-cluster (cards shimmer in place so the panel never collapses to empty). No spinner; no layout pop when real cards arrive.
- **D-13:** On a new incoming customer message, the always-expanded panel re-populates its cards (skeleton → fresh set). This auto-populate (INT-02) must never overwrite a dirty composer draft (see D-02).

### Mock data (carries the entire Phase-1 demo)
- **D-14:** `MockSuggestionsProvider` returns **Russian-language** replies (CIS target market) covering realistic small-business intents: greeting, price inquiry, availability, booking/order, polite decline. Include at least one deliberately long reply to exercise truncation (PANEL-06) and a variety of intent labels.
- **D-15:** The mock simulates realistic latency (so the skeleton loading state is genuinely exercised), supports the steered re-cluster (returns a fresh steered set when given the picked reply), and includes an adversarial path that emits out-of-order / superseded responses to exercise the DATA-03 correlation/sequence guard, plus a simulated error path for the error state.

### Claude's Discretion
- Empty-state and error-state visuals within PANEL-04 (e.g. empty: "No suggestions — type your reply"; error: inline message + retry), following the `unity-ui-builder` skill.
- Exact "Recommended" badge placement/styling, chip accent color, card padding/spacing (4px multiples per skill), skeleton shimmer timing, and panel show/hide motion (DOTween).
- Manual-refresh affordance placement (INT-03) — likely a small refresh control on the panel.
- The precise shape of the `ISuggestionsProvider` interface, the correlation/sequence guard, and the mediating controller (within the patterns in code_context).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

This is a brownfield Unity project with no external ADR/spec documents — the canonical references are the project planning docs, the codebase maps, the project skills, and the closest-analog source files.

### Locked requirements & product decisions
- `.planning/REQUIREMENTS.md` — the 17 Phase-1 requirements (SEMI/PANEL/INT/DATA) and their acceptance criteria; the v1 Out-of-Scope table. **MUST read before planning.**
- `.planning/PROJECT.md` — Key Decisions table (ranking + badge, no numeric %; tap→composer never auto-send; re-cluster; per-chat toggle; mock-first; WhatsApp-only) and constraints.
- `.planning/ROADMAP.md` §"Phase 1" — goal, success criteria, the seam contract.

### Codebase maps (read before touching code)
- `.planning/codebase/ARCHITECTURE.md` — system layers, data flow, event model.
- `.planning/codebase/STRUCTURE.md` — directory layout, where new UI/scripts/tests go, builder pattern.
- `.planning/codebase/CONVENTIONS.md` — naming, MonoBehaviour lifecycle, event-driven UI, serialization, color constants.
- `.planning/codebase/INTEGRATIONS.md` — external service patterns (relevant to Phase 2 seam swap).
- `.planning/codebase/CONCERNS.md` — Wappi concurrent-response crossing bugs; Manager.cs god-object (do NOT refactor here).
- `.planning/codebase/STACK.md`, `.planning/codebase/TESTING.md` — stack versions; EditMode test harness conventions.

### Project skills (apply to the matching work)
- `.claude/skills/unity-ui-builder/SKILL.md` — MANDATORY for all panel/card/toggle/chip UI. 1080×1920 reference units, measured type/spacing scale, RoundedCorners (null sprite + RoundedCorners, never UISprite.psd on surfaces), TMP, DOTween.
- `.claude/skills/mobile-app-ui-design/SKILL.md` — design direction (thumb zones, hierarchy) alongside unity-ui-builder.
- `.claude/skills/chat-data-flow/SKILL.md` — the RawMessage→NormalizedMessage→MessageViewModel pipeline + ChatManager events (OnLiveMessagesReceived is the INT-02 trigger).
- `.claude/skills/unity-api-integration/SKILL.md` — coroutine+UnityWebRequest patterns (Phase-1 mock mimics latency; Phase-2 provider).
- `.claude/skills/bot-persistence/SKILL.md` — PlayerPrefs conventions for the per-chat semi-auto state (SEMI-02).

### Closest-analog source files (model the new code on these)
- `Assets/Scripts/Chat/QuickReplyPanel.cs` + `QuickReplyButton.cs` — existing 4-item reply panel (SetReplies/Clear/Hide, dual-action button). Spawn/clear pattern to model; reshape to a vertical stack of cards.
- `Assets/Scripts/Chat/MessagesBottomPanel.cs` — owns the composer (`inputField`, `sendButton`, `SendTextMessage`), references `quickReplyPanel`, and has a DOTween slide-up reply-preview bar (motion + hand-off analog). Composer hand-off = set `inputField.text`.
- `Assets/Scripts/Chat/ExpandableInput.cs`, `KeyboardAwarePanel.cs` — composer input + keyboard-aware positioning the panel must sit above.
- `Assets/Scripts/Chat/CrossChatResponseGuard.cs` — chat-id crossing guard (DATA-03 analog; suggestions have no RawMessage.chatId, so use a correlation/sequence guard instead).
- `Assets/Scripts/Main/ChatManager.cs` — `OnLiveMessagesReceived` event (INT-02 trigger), `_activeChatCache`, `WaitForChatFetchesToDrain`; needs a public current-chat accessor (DATA-04).
- `Assets/Scripts/Main/ChatManager.QuoteResolve.cs` — the serial, guarded PULL pattern (capture instance + chatId, discard crossed/superseded responses) to model the provider fetch + guard on.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`QuickReplyPanel` / `QuickReplyButton`** — already render a 4-item reply panel with spawn/clear lifecycle and a dual-action button. The new suggestions panel reuses this construction pattern but as a vertical stack of richer cards (text + intent chip + badge). The dual-action affordance is NOT needed (single tap does both, D-01).
- **`MessagesBottomPanel`** — the host: owns the composer, already holds a `quickReplyPanel` reference, and has a DOTween slide-up bar (reply preview) that is the motion analog for panel/skeleton transitions. Composer hand-off (D-01) = set `inputField.text`; send path = `ChatManager.SendTextMessage`.
- **`CrossChatResponseGuard` + `ChatManager.QuoteResolve.cs`** — the established serial/guarded-PULL + discard-crossed-response pattern that DATA-03's correlation/sequence guard reuses (no concurrent Wappi-style crossing).

### Established Patterns
- Singleton managers (`ChatManager.Instance`); event-driven UI (subscribe in `OnEnable`, unsubscribe in `OnDisable`); no polling.
- Coroutines + `IEnumerator` for all async (the mock simulates latency via a coroutine/`WaitForSeconds`; no async/await in MonoBehaviours).
- Per-bot/per-chat persistence in PlayerPrefs — semi-auto state keyed `{botId}_semiAuto_{chatId}` (SEMI-02). Orphaned toggle keys on bot delete are acceptable this milestone (STATE.md note; document, don't over-engineer).
- Programmatic UI via `[MenuItem]` Editor builders in `Assets/Editor/` — follow this builder pattern for the new panel/toggle (see STRUCTURE.md "New Page").
- `unity-ui-builder`: 1080×1920 canvas units, 4px spacing, RoundedCorners for card corners, TMP text, DOTween motion.

### Integration Points
- New suggestions panel GO lives in the WhatsApp chat screen, in the `MessagesBottomPanel` area (above the composer), like the existing `quickReplyPanel`, and must respect `KeyboardAwarePanel` positioning.
- Semi-auto toggle added to the WhatsApp chat top bar (chat header).
- Auto-populate (INT-02) subscribes to `ChatManager.OnLiveMessagesReceived`; adds NO new Wappi `messages/get` caller (CONCERNS.md).
- The `ISuggestionsProvider` seam + `MockSuggestionsProvider` + a mediating controller scope to the current chat via a new public `ChatManager` accessor (DATA-04) and gate via `WaitForChatFetchesToDrain` / a correlation guard (DATA-03). Nothing above the seam may reference n8n, UnityWebRequest, or Wappi.

</code_context>

<specifics>
## Specific Ideas

- The owner explicitly revised the panel behavior mid-discussion to **always expanded while semi-auto is on** — no collapse churn. The toggle is the on/off; the composer is the free-typing escape hatch.
- Mock content should feel like a real CIS small-business chat (Russian), good enough to demo the whole loop convincingly without a backend.

</specifics>

<deferred>
## Deferred Ideas

- Feedback thumbs-up/down on suggestions (FB-01), suggestion analytics (FB-02), streaming/animated reveal (POL-01), Telegram chat support (POL-02) — all v2 (REQUIREMENTS.md v2 / STATE.md Deferred Items).
- n8n live wiring (N8N-01..04) — Phase 2, behind the same seam with zero Phase-1 UI edits.

</deferred>

---

*Phase: 01-polished-suggestions-panel-on-mock-data*
*Context gathered: 2026-06-23*

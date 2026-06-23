# Requirements: Reply Suggestions Panel (semi-auto mode)

**Defined:** 2026-06-23
**Core Value:** The owner stays in control along the automation↔semi-auto spectrum — the bot can answer autonomously, or propose replies the owner picks and refines.

## v1 Requirements

Requirements for this milestone. Build order: **Phase 1** = polished UI on mock data · **Phase 2** = n8n live wiring. Each maps to a roadmap phase.

### Semi-Auto Toggle

- [ ] **SEMI-01**: Owner can flip a specific chat into semi-auto mode via a per-chat toggle
- [ ] **SEMI-02**: Semi-auto state persists per chat across app restarts and bot switches (follows existing PlayerPrefs/chat conventions)
- [ ] **SEMI-03**: The suggestions panel appears only in chats flipped to semi-auto; other chats stay autonomous/manual

### Panel UI

- [ ] **PANEL-01**: Suggestions render as a bottom sheet above the composer, in the WhatsApp chat screen (extending the existing QuickReplyPanel / MessagesBottomPanel area)
- [ ] **PANEL-02**: Panel shows 4 suggestion cards, each displaying reply text + an intent label (e.g. Price / Availability / Greeting)
- [ ] **PANEL-03**: Cards are ordered best-first; the top card carries a "Recommended" badge (no numeric confidence shown)
- [ ] **PANEL-04**: Panel handles loading, empty, and error states gracefully (no blank/jank)
- [ ] **PANEL-05**: Panel can be dismissed/collapsed so the owner can fall back to free typing
- [ ] **PANEL-06**: Long reply text truncates cleanly within a card without breaking layout

### Interaction

- [ ] **INT-01**: Tapping a card loads its text into the composer to edit — it never auto-sends
- [ ] **INT-02**: Suggestions auto-populate when a new incoming customer message arrives, but never overwrite an in-progress composer edit
- [ ] **INT-03**: Owner can manually refresh to request a fresh set of suggestions
- [ ] **INT-04**: Picking a card regenerates a fresh set of 4 suggestions re-clustered/re-ranked toward that pick (the steering loop); owner can keep refining or edit + send

### Data & Provider Seam

- [ ] **DATA-01**: An `ISuggestionsProvider` seam abstracts suggestion sourcing so UI is built independent of the backend
- [ ] **DATA-02**: A `MockSuggestionsProvider` supplies realistic stub data (simulated latency, steered re-cluster, correlation echo) for the entire Phase-1 UI build
- [ ] **DATA-03**: Suggestion fetches reject stale/out-of-order responses (monotonic sequence/correlation guard) and survive rapid re-cluster picks and chat switches — reusing the existing CrossChatResponseGuard / WaitForChatFetchesToDrain pattern; no concurrent Wappi-style crossing
- [ ] **DATA-04**: `ChatManager` exposes a public current-chat accessor (and drain hook) needed to scope suggestions to the open chat

### n8n Live Wiring

- [ ] **N8N-01**: Existing n8n automation is extended with a synchronous Webhook + Respond-to-Webhook flow that returns suggestions for a chat as a versioned `{ text, label }[]` payload (ranked best-first) plus correlation id
- [ ] **N8N-02**: A `N8nSuggestionsProvider` consumes the live flow end-to-end with zero changes to Phase-1 UI (the seam holds)
- [ ] **N8N-03**: The live flow supports re-clustering by accepting a "steer toward" field carrying the picked reply
- [ ] **N8N-04**: Suggestion generation validates structured output against a schema and is hardened against prompt injection from customer-message content (labels constrained to a known set; malformed output handled, not surfaced raw)

## v2 Requirements

Deferred. Tracked, not in this roadmap.

### Feedback & Insight

- **FB-01**: Owner can thumbs-up/down a suggestion to improve future ranking
- **FB-02**: Per-chat or per-bot suggestion-quality analytics

### Polish

- **POL-01**: Streaming/animated reveal of suggestions as they generate
- **POL-02**: Telegram chat support for the suggestions panel

## Out of Scope

Explicitly excluded for this milestone.

| Feature | Reason |
|---------|--------|
| Numeric % confidence on cards | Research: miscalibrated LLM confidence erodes trust (core value); ranking + badge instead |
| Auto-sending a suggestion without owner action | Violates the trust+control core value |
| Changing/replacing the existing autonomous automation mode | This milestone only adds the semi-auto path alongside it |
| Telegram chat UI | WhatsApp chat client is the only live chat surface now (deferred to v2 POL-02) |
| Expandable/multi-line confidence-rich cards, thumbs rating in v1 | Anti-features per research; keep v1 panel clean |
| Bot-persistence refactor off PlayerPrefs / Manager.cs split | Real concern (CONCERNS.md) but a separate effort |

## Traceability

Mapped during roadmap creation (2026-06-23). Build order is load-bearing: Phase 1 = polished UI on mock data (includes the seam, guards, toggle, and ChatManager accessors so it ships standalone); Phase 2 = n8n live wiring behind the same seam with no Phase-1 UI edits.

| Requirement | Phase | Status |
|-------------|-------|--------|
| SEMI-01 | Phase 1 | Pending |
| SEMI-02 | Phase 1 | Pending |
| SEMI-03 | Phase 1 | Pending |
| PANEL-01 | Phase 1 | Pending |
| PANEL-02 | Phase 1 | Pending |
| PANEL-03 | Phase 1 | Pending |
| PANEL-04 | Phase 1 | Pending |
| PANEL-05 | Phase 1 | Pending |
| PANEL-06 | Phase 1 | Pending |
| INT-01 | Phase 1 | Pending |
| INT-02 | Phase 1 | Pending |
| INT-03 | Phase 1 | Pending |
| INT-04 | Phase 1 | Pending |
| DATA-01 | Phase 1 | Pending |
| DATA-02 | Phase 1 | Pending |
| DATA-03 | Phase 1 | Pending |
| DATA-04 | Phase 1 | Pending |
| N8N-01 | Phase 2 | Pending |
| N8N-02 | Phase 2 | Pending |
| N8N-03 | Phase 2 | Pending |
| N8N-04 | Phase 2 | Pending |

**Coverage:**
- v1 requirements: 21 total
- Mapped to phases: 21 (Phase 1: 17, Phase 2: 4)
- Unmapped: 0 ✓

---
*Requirements defined: 2026-06-23*
*Last updated: 2026-06-23 after roadmap creation*

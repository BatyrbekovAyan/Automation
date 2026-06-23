# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-06-23)

**Core value:** The owner stays in control along the automation↔semi-auto spectrum — the bot can answer autonomously, or propose replies the owner picks and refines, without losing trust or the ability to take over.
**Current focus:** Phase 1 — Polished Suggestions Panel on Mock Data

## Current Position

Phase: 1 of 2 (Polished Suggestions Panel on Mock Data)
Plan: 0 of TBD in current phase
Status: Ready to plan
Last activity: 2026-06-23 — Roadmap created (2 phases, coarse granularity, 21/21 requirements mapped)

Progress: [░░░░░░░░░░] 0%

## Performance Metrics

**Velocity:**
- Total plans completed: 0
- Average duration: -
- Total execution time: 0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

**Recent Trend:**
- Last 5 plans: -
- Trend: -

*Updated after each plan completion*

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Roadmap]: Two coarse phases driven by the fixed build order (mock-first UI, then n8n live), not requirement categories — the `ISuggestionsProvider` seam is what makes Phase 1 backend-independent.
- [Roadmap]: Foundation (seam, correlation/stale guards, per-chat toggle + persistence, public `ChatManager.CurrentChatId` + `WaitForChatFetchesToDrain` accessors) lives inside Phase 1 so the UI phase is self-contained and shippable on mock data.
- [Project]: Confidence shown as ranking + "Recommended" badge on top card only — no numeric percentage (research arXiv 2402.07632; trust core value). Lock before building card UI.
- [Project]: Tapping a card loads into the composer to edit, never auto-sends; picking regenerates a full steered set of 4 (re-cluster loop).

### Pending Todos

None yet.

### Blockers/Concerns

- [Phase 2 flag]: Measure real n8n cloud round-trip latency with the chosen model before designing timeout/debounce/skeleton; verify exact fast Russian-capable model IDs in the live console; test for the n8n AI Agent `output`-key double-nesting bug and unwrap defensively. Confirm Respond-to-Webhook timeout ceiling vs LLM latency (a too-low ceiling forces an ack+polling redesign).
- [Constraint]: Respect the confirmed Wappi concurrent-response crossing bugs — suggestion fetches must be serial/guarded; the auto-trigger consumes `OnLiveMessagesReceived` and adds NO new Wappi `messages/get` caller.
- [Note, not a blocker]: `{botId}_semiAuto_{chatId}` PlayerPrefs keys are unbounded per chat and not reliably enumerable; orphaned toggle keys on bot delete are acceptable this milestone (document, don't over-engineer).

## Deferred Items

Items acknowledged and carried forward:

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| Feedback | FB-01 thumbs-up/down to improve ranking | Deferred to v2 | Init |
| Insight | FB-02 per-chat/per-bot suggestion analytics | Deferred to v2 | Init |
| Polish | POL-01 streaming/animated suggestion reveal | Deferred to v2 | Init |
| Polish | POL-02 Telegram chat support for the panel | Deferred to v2 | Init |

## Session Continuity

Last session: 2026-06-23 17:53
Stopped at: Roadmap and state initialized; REQUIREMENTS.md traceability populated
Resume file: None

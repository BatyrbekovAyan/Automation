---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: executing
stopped_at: Completed 02-01-PLAN.md (Suggest Replies workflow live on dev)
last_updated: "2026-07-10T15:01:52.341Z"
last_activity: 2026-07-10 — completed 02-01-PLAN.md
progress:
  total_phases: 2
  completed_phases: 1
  total_plans: 8
  completed_plans: 5
  percent: 63
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-06-23)

**Core value:** The owner stays in control along the automation↔semi-auto spectrum — the bot can answer autonomously, or propose replies the owner picks and refines, without losing trust or the ability to take over.
**Current focus:** Phase 2 (n8n Live Wiring) — Plan 02-01 complete, 02-02 next

## Current Position

Phase: 2 (n8n Live Wiring) — EXECUTING
Plan: 2 of 4
Status: Executing Phase 2 — 02-01 complete (Suggest Replies workflow live on dev)
Last activity: 2026-07-10 — completed 02-01-PLAN.md

Progress: [██████░░░░] 63%

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
| Phase 2 P01 | 11min | 2 tasks | 3 files |

## Accumulated Context

### Decisions

Decisions are logged in PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Roadmap]: Two coarse phases driven by the fixed build order (mock-first UI, then n8n live), not requirement categories — the `ISuggestionsProvider` seam is what makes Phase 1 backend-independent.
- [Roadmap]: Foundation (seam, correlation/stale guards, per-chat toggle + persistence, public `ChatManager.CurrentChatId` + `WaitForChatFetchesToDrain` accessors) lives inside Phase 1 so the UI phase is self-contained and shippable on mock data.
- [Project]: Confidence shown as ranking + "Recommended" badge on top card only — no numeric percentage (research arXiv 2402.07632; trust core value). Lock before building card UI.
- [Project]: Tapping a card loads into the composer to edit, never auto-sends; picking regenerates a full steered set of 4 (re-cluster loop).
- [02-01]: Suggest Replies n8n workflow deployed on dev (id 9PTyYcelRQI7bGDb, /webhook/SuggestReplies) — one gpt-4o-mini strict json_schema call, closed 6-label enum, Code-node count/distinct/clamp validation + one retry then generation_failed; requestSeq echoed.
- [02-01]: RAG via vectorStoreSupabase LOAD mode grounded from installed node source (n8n MCP unavailable → REST/curl); single botWaId filter, topK 5, text-embedding-3-small; alwaysOutputData + skipRag-gated Assemble so an empty documents table never kills the branch.
- [02-01]: Committed export carries dev credential ids resolved by NAME (Dashboard precedent); prod bagkz replication remaps by name via build-suggest-replies.py. Supabase cred present+functional on dev; RAG grounding-with-data deferred to prod/seed.

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

Last session: 2026-07-10T14:58:12Z
Stopped at: Completed 02-01-PLAN.md (Suggest Replies workflow live on dev)
Resume file: None

**Planned Phase:** 2 (n8n Live Wiring) — 4 plans — 2026-07-10T14:26:05.936Z

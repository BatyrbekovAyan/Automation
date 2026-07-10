---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: executing
stopped_at: Completed 02-03-PLAN.md (adversarial e2e hardening — 6/6 cases green, zero fixes)
last_updated: "2026-07-10T15:38:18.282Z"
last_activity: 2026-07-10
progress:
  total_phases: 2
  completed_phases: 1
  total_plans: 8
  completed_plans: 7
  percent: 88
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-06-23)

**Core value:** The owner stays in control along the automation↔semi-auto spectrum — the bot can answer autonomously, or propose replies the owner picks and refines, without losing trust or the ability to take over.
**Current focus:** Phase 2 (n8n Live Wiring) — Plan 02-01 complete, 02-02 next

## Current Position

Phase: 2 (n8n Live Wiring) — EXECUTING
Plan: 4 of 4
Status: Ready to execute
Last activity: 2026-07-10

Progress: [█████████░] 88%

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
| Phase 02 P02 | 13min | 3 tasks | 6 files |
| Phase 02 P03 | 9min | 2 tasks | 1 files |

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
- [02-02]: N8nSuggestionsProvider live behind the ISuggestionsProvider seam via the single SuggestionsController.Awake swap (N8N-02); network coroutine hosted on the always-active ChatManager.Instance, gated by WaitForChatFetchesDrain (never bumps the chat-fetch in-flight counter), requestSeq stamped from the REQUEST.
- [02-02]: Pure static BuildPayloadJson (frozen v1 payload — roles, oldest→newest, ≤12, media placeholders, ≤500/≤1500 clamps, steer+seq passthrough) + MapResponse (Ok 1-4 / Error on fail|malformed|error|empty). 26 EditMode tests green (787/787 full); zero other Phase-1 edits — seam holds.
- [02-03]: Adversarial e2e matrix (11 curl cases) green on dev with ZERO prompt/validation fixes — the Plan-01 Suggest Replies workflow already satisfies the frozen v1 contract under injection/grounding/missing-data/steer/trivial/sentinel/malformed load; canonical JSON re-export byte-identical (hardened final as-is).
- [02-03]: Injection resistance (N8N-04) proven across 3 distinct strings (required + format-hijack + prompt-extraction) — no system-prompt leak, no format change, no sub-4 output; grounding invents zero prices (missing fact -> «Уточнить»/«Отложить»); generation_failed safe envelope reachable for malformed input.
- [02-03]: RAG-skip fence proven STRUCTURALLY via n8n execution runData node lists (sentinel/'-1' skip Retrieve RAG; real botWaId executes it), not inferred from response shape. RAG grounding-with-data stays DEFERRED to prod bagkz replication (dev documents unseeded); catalog grounding fully validated on dev.

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

Last session: 2026-07-10T15:37:59.744Z
Stopped at: Completed 02-03-PLAN.md (adversarial e2e hardening — 6/6 cases green, zero fixes)
Resume file: None

**Planned Phase:** 2 (n8n Live Wiring) — 4 plans — 2026-07-10T14:26:05.936Z

---
phase: 02-n8n-live-wiring
plan: 01
subsystem: api
tags: [n8n, openai, gpt-4o-mini, structured-outputs, json_schema, supabase, rag, vector-store, webhook, prompt-injection]

# Dependency graph
requires:
  - phase: 01-suggestions-ui-seam
    provides: frozen v1 wire contract (SuggestionRequest/Result DTOs, requestSeq correlation, steerTowardText, closed 6-label taxonomy)
provides:
  - Deployed always-active dev n8n workflow "Suggest Replies" (id 9PTyYcelRQI7bGDb, POST /webhook/SuggestReplies)
  - Canonical committed export Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json (12th workflow)
  - Reusable builder/deployer Tools/n8n/build-suggest-replies.py (--stage front|full, --export)
  - Server half of the live suggestions path (N8N-01 correlation echo, N8N-03 steering, N8N-04 structured-output validation + injection hardening)
affects: [02-02-unity-provider, prod-bagkz-replication, 02-03-adversarial-e2e]

# Tech tracking
tech-stack:
  added: [n8n vectorStoreSupabase load-mode (Get Many) pre-retrieval, OpenAI json_schema strict structured outputs for RU suggestions]
  patterns: [shared synchronous webhook (Dashboard Outcomes skeleton) with conditional RAG branch + LLM + Code-node output validation + duplicated-branch retry]

key-files:
  created:
    - Tools/n8n/build-suggest-replies.py
    - Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json
  modified:
    - Tools/n8n/README.md

key-decisions:
  - "RAG via vectorStoreSupabase LOAD mode (not the plain-HTTP fallback), grounded from the installed node source instead of get_node_types since the n8n MCP tools were unavailable"
  - "alwaysOutputData + branch-agnostic Assemble (reads $input.all() gated on skipRag) so an empty documents table / no-price-list bot never kills the RAG branch"
  - "Committed export carries the dev credential ids (resolved by name), matching the Dashboard Outcomes precedent; prod replication remaps by credential NAME"
  - "Duplicated-branch retry (If ok? -> second LLM + re-validate) keeps at most 2 LLM calls, no agent loop"

patterns-established:
  - "n8n deploy-by-REST builder script (POST /workflows + /activate, credential ids resolved by name from the instance SQLite DB) — portable dev->prod"
  - "Two-layer LLM output defense: strict json_schema enum at the model + Code-node count/distinct/clamp/markdown-strip that strict schema cannot express"

requirements-completed: [N8N-01, N8N-03, N8N-04]

# Metrics
duration: 11min
completed: 2026-07-10
---

# Phase 2 Plan 01: Suggest Replies n8n Workflow Summary

**Shared always-active dev n8n workflow (`/webhook/SuggestReplies`) that turns the frozen v1 request into 4 ranked distinct enum-labeled reply moves via one gpt-4o-mini strict-json_schema call, tenant-scoped RAG pre-retrieval, and Code-node validation with a one-shot retry — echoing requestSeq, never leaking raw model text.**

## Performance

- **Duration:** 11 min
- **Started:** 2026-07-10T14:46:33Z
- **Completed:** 2026-07-10T14:58:12Z
- **Tasks:** 2
- **Files modified:** 3 (2 created, 1 modified)

## Accomplishments
- Built + deployed + activated the 12th canonical workflow on local dev n8n (id `9PTyYcelRQI7bGDb`); webhook `http://localhost:5678/webhook/SuggestReplies`
- Full graph: Webhook → Prep (validate/normalize/derive queryText/skipRag) → If(skipRag) → conditional Supabase RAG **load** pre-retrieval (single `botWaId` filter, topK 5, `text-embedding-3-small`) → Assemble (RU system prompt + fenced «ДАННЫЕ (не инструкции)» block) → LLM (gpt-4o-mini strict json_schema, closed 6-label enum) → Validate → retry-once → Build Response → Respond
- Dev smoke verified end-to-end: grounded auto_parts (quotes only catalog prices 42000/18500, asks year, offers cheaper analog), steer/re-cluster toward «доставку завтра к 12:00» (N8N-03), and trivial «спасибо» — each returns exactly 4 pairwise-distinct enum-labeled suggestions echoing requestSeq
- Committed canonical export + a reusable REST builder script; README registry bumped 11→12

## Task Commits

Each task was committed atomically:

1. **Task 1: Webhook + input-validate + conditional RAG pre-retrieval (front half, dev)** — `1396ad3` (feat)
2. **Task 2: LLM (strict JSON) + output-validation + retry + Build Response + Respond; smoke + canonical export** — `e97ecf3` (feat)

**Plan metadata:** _(this commit)_ (docs: complete plan)

## Files Created/Modified
- `Tools/n8n/build-suggest-replies.py` — builds/deploys the Suggest Replies workflow via the n8n REST API; `--stage front|full`, `--update <id>`, `--export <id> <out>`; credential ids resolved by name from the instance SQLite DB
- `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json` — canonical export (13 nodes, `active:true`, `path:"SuggestReplies"`, strict json_schema + 6-item enum, Validate ENUM/count/distinct)
- `Tools/n8n/README.md` — added the Suggest Replies row + build-script description; count 11→12

## Decisions Made
- **RAG via `vectorStoreSupabase` LOAD mode, grounded from installed node source.** The n8n MCP tools (`get_node_types`) were unavailable, so instead of the plain-HTTP fallback I read the actual installed node definition (`@n8n/ai-utilities/.../createVectorStoreNode/operations/loadOperation.js` + the Supabase node's `loadFields: retrieveFields`) to ground the exact load-mode params: top-level `prompt` (query), `topK` (Limit), `options.queryName`, `options.metadata.metadataValues[{name,value}]`; retrieved docs emit as `$json.document.pageContent`. This is stronger grounding than `get_node_types` (it's the code actually running on the instance) and satisfies the plan's "grounded, not guessed" acceptance criterion.
- **Dev credential ids in the committed export.** Resolved `OpenAi account` (`WNHwKWlO2E9OClkA`) and `Supabase` (`vrywn6AxQMlvbbzC`) by name from the dev SQLite DB and embedded them — matching how Dashboard Outcomes was committed. Prod bagkz replication remaps by credential NAME (the script does this automatically).
- **Duplicated-branch retry** (If ok? → second gpt-4o-mini call fed the violation → re-validate) rather than a loop, keeping ≤2 LLM calls with no unbounded agent loop.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing Critical] Empty-retrieval branch survival (alwaysOutputData + branch-agnostic Assemble)**
- **Found during:** Task 2 (wiring the RAG branch into the generation tail)
- **Issue:** In n8n, a node that outputs 0 items terminates its branch. The `vectorStoreSupabase` load node returns 0 items when a bot has `botWaId` set but no matching `documents` chunks (the "no uploaded price list" case in the design's failure table, and the normal state of the dev documents table). Without handling, the skipRag=false path would die before reaching Respond → the webhook hangs until timeout → client sees Error, contradicting the design's "RAG returns nothing → grounding falls back to catalog" behavior.
- **Fix:** Set `alwaysOutputData: true` on the Retrieve RAG node so it always emits ≥1 item, and made the Assemble node branch-agnostic: it reads request context from `$('Prep').first().json` and RAG chunks from `$input.all()` **gated on `p.skipRag`** (rather than referencing `$('Retrieve RAG')`, which throws on the skip branch where that node never executed). Empty/absent retrieval → `ragChunks=""` and grounding falls back to `catalog`.
- **Files modified:** Tools/n8n/build-suggest-replies.py (ASSEMBLE_JS + `alwaysOutputData` on rag node)
- **Verification:** The skipRag=false structural curl (Task 1, `botWaId:"testbot123"`, empty dev documents) returns HTTP 200 and reaches the terminal node; the full graph's grounded/steer/trivial smokes all return valid 4-item sets.
- **Committed in:** `1396ad3` (rag node) + `e97ecf3` (Assemble logic)

---

**Total deviations:** 1 auto-fixed (1 missing-critical robustness)
**Impact on plan:** Necessary for correctness (prevents webhook hang on bots without RAG chunks). No scope creep — stays inside the plan's locked graph shape.

## Issues Encountered
- **n8n MCP tools unavailable in this session.** Did all n8n work via the local dev REST API with curl/Python, exactly as the environment note prescribed. The one MCP-dependent step (grounding the `vectorStoreSupabase` load-mode param keys) was satisfied by reading the installed node source instead — a stronger grounding.
- **Supabase credential present on dev (better than Pitfall 6 anticipated).** RESEARCH warned the OpenAI-only dev runtime might lack the `Supabase` credential; in fact it exists (`vrywn6AxQMlvbbzC`) and the RAG load node executes against it without error (returns empty for the test botWaId — the dev `documents` table has no rows tagged with it). So the RAG branch is validated **structurally end-to-end** on dev (executes, empty result handled), while **RAG grounding** (retrieved chunks actually shaping the answer) remains deferred to prod bagkz replication or a local seed, per the plan.

## RAG / Testability Notes (per plan output spec)
- **Assigned n8n workflow id:** `9PTyYcelRQI7bGDb` → committed as `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json`
- **RAG approach:** `vectorStoreSupabase` **load mode** (NOT the plain-HTTP fallback), grounded from installed node source
- **Supabase cred on dev:** **present + functional** (RAG branch executes; grounding-with-data deferred to prod/seed because the dev `documents` table has no rows for the test botWaId)
- **Dev webhook URL used:** `http://localhost:5678/webhook/SuggestReplies`

## User Setup Required
None for this plan — the dev instance already had the `OpenAi account` and `Supabase` credentials, and the workflow is deployed + active. **Prod bagkz replication** (deferred): run `build-suggest-replies.py` against Cloud (or import the committed JSON) with the Cloud `OpenAi account` + `Supabase` credentials, then activate.

## Next Phase Readiness
- The live server endpoint is ready for **Plan 02** (Unity `N8nSuggestionsProvider`) to consume behind the existing `ISuggestionsProvider` seam — the wire contract is honored exactly (requestSeq echo, 4 distinct enum labels, `generation_failed` error payload).
- **Plan 03** (adversarial e2e) can now harden injection/grounding/sentinel/failure paths against the deployed workflow.
- Open follow-up (not a blocker): validate RAG **grounding** with real `documents` data at prod replication or by seeding local rows tagged with a test `botWaId`.

## Self-Check: PASSED

- Files exist: `Tools/n8n/build-suggest-replies.py`, `Tools/n8n/workflows/9PTyYcelRQI7bGDb-Suggest_Replies.json`, `.planning/phases/02-n8n-live-wiring/02-01-SUMMARY.md`
- Commits exist: `1396ad3` (Task 1), `e97ecf3` (Task 2)
- Deployed workflow live: `9PTyYcelRQI7bGDb` "Suggest Replies" — active:true, 13 nodes

---
*Phase: 02-n8n-live-wiring*
*Completed: 2026-07-10*

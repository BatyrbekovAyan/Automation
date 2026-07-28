---
phase: 08-device-uat-milestone-closeout
plan: 02
subsystem: infra
tags: [n8n, prod-replication, runbook, supabase, credentials, telegram-parity, tooling]

# Dependency graph
requires:
  - phase: 04-n8n-telegram-template-parity-dev
    provides: the 4 fixed canonical workflows (Telegram_Bot tapi + both Create RAG re-stamp + Suggest_Replies channel branch) + verify-telegram-parity.py + build-suggest-replies.py
  - phase: 07-vmeste-suggestions-dashboard-on-telegram
    provides: channel-aware Suggest_Replies / Dashboard family that must land on prod identically
provides:
  - "08-PROD-REPLICATION.md — ordered, idempotent-safe one-shot runbook to bulk-copy all 12 dev n8n workflows to dormant prod bagkz (creds BY NAME, templates INACTIVE, go/no-go verify, flagged header-auth)"
  - "verify-telegram-parity.py --dir override — the parity asserts can gate a prod re-export (default = committed workflows/, byte-identical absent --dir)"
  - "build-suggest-replies.py cred-id overrides (--openai-cred/--supabase-cred + N8N_OPENAI_CRED_ID/N8N_SUPABASE_CRED_ID) — bind real recreated-by-name prod creds on a no-SQLite Cloud target"
affects: [08-03-milestone-close, prod-bagkz-deploy]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Prod-targetable tooling via additive, backward-compatible options (env/flag override + --dir) that leave the dev path byte-identical when absent"
    - "One-shot prod-replication runbook: literal-id import + creds-by-name + post-import structural re-verify (--dir) as the deploy go/no-go"

key-files:
  created:
    - .planning/phases/08-device-uat-milestone-closeout/08-PROD-REPLICATION.md
  modified:
    - Tools/n8n/verify-telegram-parity.py
    - Tools/n8n/build-suggest-replies.py

key-decisions:
  - "Suggest_Replies is deployed by its own builder in step 5 (not the manual literal-id import) — the only way to bind prod cred ids on a no-SQLite Cloud target; it is reached by webhook PATH, not literal id, so a fresh prod id is safe (only the two bot templates are literal-id-referenced)"
  - "Dev credential/webhook reference ids (vvRrFiEXzLVqKjOx, ZowntFGvApDJ7UzQ, 0091024b-7b46) cited as non-secret traceability pointers (already in committed README/04-HUMAN-UAT); every prod credential referenced BY NAME, zero secret VALUES (T-08-05)"
  - "Both script edits additive + backward-compatible: byte-identical dev behavior proven offline (graph identical, cred ids == dev fallback) when overrides/--dir absent"

patterns-established:
  - "Additive prod-mode on a dev tool: default preserves committed-source behavior exactly; a flag/env/dir opens the prod target without a separate script"

requirements-completed: []  # plan frontmatter requirements: [PROD-01] — NOT complete until the owner runs Task 3 (the prod deploy); autonomous scope delivers only the runbook + tooling

# Metrics
duration: 5min
completed: 2026-07-15
---

# Phase 8 Plan 02: Prod Replication Runbook + Prod-Targetable Tooling Summary

**08-PROD-REPLICATION.md (the ordered one-shot bulk-copy runbook to dormant prod bagkz — creds BY NAME, both bot templates INACTIVE, Session-pooler-5432 Postgres, Supabase migrations + re-stamp UPDATE grant, `verify-telegram-parity.py --dir` go/no-go, flagged header-auth) plus two additive, dev-byte-identical tooling tweaks that make the copy scriptable + verifiable on a no-SQLite Cloud target.**

## Performance

- **Duration:** ~5 min (autonomous scope)
- **Started:** 2026-07-15T09:29:04Z
- **Completed:** 2026-07-15T09:34:14Z
- **Tasks:** 2 of 3 executed autonomously (Task 3 is the blocking owner-run prod deploy — NOT executed; see Checkpoint)
- **Files modified:** 3 (2 scripts edited, 1 runbook created)

## Accomplishments

- **Task 1 — prod-targetable tooling (additive, dev-byte-identical):**
  - `verify-telegram-parity.py`: added optional `--dir PATH` (default = the committed `workflows/`
    next to the script) so the same structural asserts can gate a prod re-export as a post-import
    go/no-go. Absent `--dir`, output is byte-identical — default run still prints
    `ALL PARITY ASSERTS PASSED` over all 4 parity workflows.
  - `build-suggest-replies.py`: added OpenAi + Supabase credential-id overrides via
    `--openai-cred`/`--supabase-cred` flags AND `N8N_OPENAI_CRED_ID`/`N8N_SUPABASE_CRED_ID` env
    vars (precedence: flag > env > SQLite-by-name > pinned DEV fallback). An override
    short-circuits the SQLite lookup and is used verbatim, so a no-SQLite Cloud deploy binds the
    real recreated-by-name prod creds instead of silently falling back to dev ids. Documented in
    the module docstring + `--help`.
  - **Proven byte-identical (offline, no network):** with no overrides the generated Suggest_Replies
    payload's cred ids equal the dev-resolved values, and the workflow GRAPH (nodes/connections/
    settings) is identical with vs without overrides — only cred ids differ.
- **Task 2 — `08-PROD-REPLICATION.md` (235 lines):** an ordered, idempotent-safe one-shot runbook
  the owner runs ONCE against dormant prod bagkz. Status banner "**prod stays DORMANT — no bot clone
  is created or activated this phase**"; a 12-workflow scope table (id · name · role · prod
  active-state) with both bot templates (`4wYitz5ek30SVNlT` WhatsApp_Bot + `4VN3gsFaC2HUYmcc`
  Telegram_Bot) marked **INACTIVE** (shared webhook `0091024b-7b46`), the webhook family + Suggest_Replies
  active, and Delete_Orphan_Profiles as the hourly scheduled sweep (fresh/empty staticData → activate).
  The 9 ordered steps: (1) pre-flight `verify-telegram-parity.py` green, (2) recreate credentials
  **BY NAME** (Postgres **Session pooler 5432** not 6543/Direct with `UPDATE documents` grant,
  Supabase bare host + service_role JWT, OpenAi, WappiAuthToken via `PUT /workflows/{id}`, Cohere/
  n8nAPIKey; record new prod OpenAi + Supabase ids), (3) apply the 3 Supabase migrations +
  `conversation_outcomes` + prove the re-stamp `UPDATE` grant via the `-1` sentinel probe, (4)
  literal-id import (prod self-API URLs, prod Wappi callback, README prod-pass cleanups, templates
  INACTIVE), (5) deploy Suggest_Replies via `build-suggest-replies.py` with the prod cred-id
  overrides + seed RAG-with-data, (6) Orphan-sweep wiring, (7) post-import
  `verify-telegram-parity.py --dir <prod-export>` go/no-go, (8) FLAGGED header-auth follow-up
  (R-02-01, pre-real-traffic, not a copy blocker), (9) dormant confirmation. Ends with an owner
  result block (PASS/FAIL per major step) + the 08-03 close pointer.

## Task Commits

1. **Task 1: Make verify + deployer prod-targetable** — `32ebdf8` (feat) — `Tools/n8n/verify-telegram-parity.py`, `Tools/n8n/build-suggest-replies.py`
2. **Task 2: Write the ordered 08-PROD-REPLICATION.md runbook** — `6af8dbb` (docs) — `.planning/phases/08-device-uat-milestone-closeout/08-PROD-REPLICATION.md`
3. **Task 3: Owner runs the one-shot prod bulk copy** — **NOT executed** (blocking `checkpoint:human-action`; see Checkpoint)

**Plan metadata:** committed with this SUMMARY. STATE.md / ROADMAP.md left to the orchestrator (checkpoint-phase handling).

## Files Created/Modified

- `Tools/n8n/verify-telegram-parity.py` — added `--dir` override (default = committed `workflows/`); dev path byte-identical.
- `Tools/n8n/build-suggest-replies.py` — added `--openai-cred`/`--supabase-cred` + `N8N_OPENAI_CRED_ID`/`N8N_SUPABASE_CRED_ID` overrides wired into `resolve_cred()`; dev path byte-identical.
- `.planning/phases/08-device-uat-milestone-closeout/08-PROD-REPLICATION.md` — the one-shot prod bulk-copy runbook (12-workflow scope + 9 steps + owner result).

## Decisions Made

- **Suggest_Replies deploy path:** step 4 imports the 11 non-Suggest workflows by literal id; Suggest_Replies is deployed by its own builder in step 5 with the prod cred overrides (the only way to bind prod creds on a no-SQLite Cloud target — the very reason Task 1's overrides exist). It is reached by webhook PATH (`/webhook/SuggestReplies`), not literal id, so a fresh prod id is harmless; only the two bot templates are literal-id-referenced (README invariant). Step 7 exports whatever prod id it received under the canonical filename so the verifier finds it.
- **No secret values; creds BY NAME:** the runbook references every prod credential by name and cites dev reference ids (`vvRrFiEXzLVqKjOx`, `ZowntFGvApDJ7UzQ`, `0091024b-7b46`) only as non-secret traceability pointers already published in committed docs. Secret-value scan (JWT / connection-string / password / 40+char-token) is clean. (T-08-05 / T-08-06 / T-08-08 mitigations.)
- **Additive over prod-flag scripts:** kept both tools single-purpose — a `--dir`/override opens the prod target rather than forking a prod-only script — so the committed dev behavior is provably unchanged.

## Deviations from Plan

None — plan executed exactly as written. Both script edits are additive and backward-compatible; no workflow graph, enum, validation logic, or committed workflow JSON was touched (`git status --porcelain Tools/n8n/workflows/` empty). Nothing was run against prod or any live n8n; `secrets.json` untouched.

## Issues Encountered

None.

## User Setup Required

**The prod deploy itself (Task 3) is the owner gate** and requires prod-only setup Claude cannot perform (deny-ruled): `N8N_BASE_URL=https://bagkz.app.n8n.cloud` + the prod bagkz `N8N_API_KEY`, prod Supabase SQL-editor access, and the prod Wappi token. All steps are documented in `08-PROD-REPLICATION.md` (creds recreated BY NAME, no secret values in the doc).

## Checkpoint — Task 3 is a PENDING blocking owner gate

The autonomous scope (Tasks 1 + 2) is **complete and committed**, but the plan **remains gated on the owner** (`autonomous: false`; Task 3 `checkpoint:human-action`, gate="blocking"). Claude cannot run it — prod is live infra and the prod n8n API key + `secrets.json` are deny-ruled.

- **Status:** awaiting owner gate — PROD-01 stays open until the owner records results in `08-PROD-REPLICATION.md`.
- **What the owner must run:** execute `08-PROD-REPLICATION.md` end-to-end against dormant prod bagkz (pre-flight verify → recreate creds BY NAME → prod Supabase migrations → literal-id import with templates INACTIVE → deploy Suggest_Replies with the prod cred-id overrides → wire the Orphan sweep → post-import `verify-telegram-parity.py --dir` go/no-go). Keep BOTH bot templates inactive, create/activate NO bot clone, leave prod dormant. Record PASS/FAIL per step; note header-auth as pending-before-real-traffic (not a blocker).
- **Resume signal:** owner types **"approved"** once the prod copy is done (go/no-go GREEN) OR describes the deferral (prod copy postponed) so 08-03 rolls it forward.

## Next Phase Readiness

- The runbook + prod-targetable tooling are ready for the owner's one-shot deploy; the `verify-telegram-parity.py --dir` go/no-go is the deploy gate.
- Downstream: **08-03 milestone close** proceeds after the owner signs off here (PROD-01 done) or explicitly defers the prod copy (rolled forward with a reason).

## Self-Check: PASSED

- `08-PROD-REPLICATION.md` exists ✓ · `08-02-SUMMARY.md` exists ✓
- Task-1 commit `32ebdf8` + Task-2 commit `6af8dbb` present in git log ✓
- Task-1 automated verify chain returns OK (default verify green, `--dir` in help, both cred overrides in help) ✓
- Task-2 grep chain returns OK (12-workflow scope, INACTIVE, BY NAME, 5432, both scripts, conversation_outcomes, UPDATE, header-auth, dormant) ✓
- Dev byte-identical proven offline (graph identical, cred ids == dev fallback with no overrides) ✓
- No committed workflow JSON changed (`git status --porcelain Tools/n8n/workflows/` empty) ✓ · secret-value scan clean ✓ · Task 3 NOT performed/ticked ✓

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-15 (autonomous scope; Task 3 blocking owner gate PENDING)*

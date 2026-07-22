---
phase: 09-semi-auto-suppression
plan: 04
subsystem: infra
tags: [n8n, postgres, supabase, webhook, rls, reply-mode-flags, rundata, owner-gate, fail-closed, cred-consolidation, suppression-gate]

# Dependency graph
requires:
  - phase: 09-semi-auto-suppression (09-01)
    provides: "reply_mode_flags DDL + Set_Reply_Mode.json canonical workflow + build-set-reply-mode.py deployer — the artifacts this plan deploys live"
  - phase: 09-semi-auto-suppression (09-02)
    provides: "the Read Reply Mode + Suppressed? fail-closed gate spliced into both bot templates — the branch this plan proves by runData"
  - phase: 09-semi-auto-suppression (09-03)
    provides: "the client write path (Manager.SyncReplyMode POST /webhook/SetReplyMode) the deployed webhook now serves"
  - phase: 10-message-batching-debounce (10-03)
    provides: "the reply_mode_flags DDL pre-applied to dev mid-Phase-10 (Deviation 2); both templates REST-imported; the fresh clones fKCMIGXJSbLRimdR/pOMkkP8MYS8WhiNY used for the SUP-05 grep; the debounce chain now sitting on Suppressed? main[1]"
provides:
  - "reply_mode_flags LIVE on dev Supabase behind cred vvRrFiEXzLVqKjOx with default-deny RLS confirmed (relrowsecurity=true, anon select denied)"
  - "Set Reply Mode webhook DEPLOYED + ACTIVATED on dev (id SCLcpn6DMDG3Z4VN, shared always-active); curl matrix + precedence SQL proven live (upsert default+override, malformed->bad_request with no partial write, override>default, absence->reply)"
  - "Fail-closed suppression PROVEN by runData on BOTH channels — a suppressed 1:1 chat dead-ends at Suppressed? (whole downstream incl. the Phase-10 debounce chain absent, stays unread); a non-suppressed chat runs the full reply path"
  - "A1 outcome recorded: the boolean routed CLEAN — no suppressed::boolean cast needed"
  - "Fresh bot inherits the gate (SUP-05): REST grep on the 10-03 fresh clones shows Read Reply Mode + Suppressed? fed by the group-chat If"
  - "Postgres cred consolidated repo-wide to vvRrFiEXzLVqKjOx (dev's single cred); the dead 1H5xlpFSESU4w6JH id no longer binds anywhere"
affects: [09-05 (behavioral HUMAN-UAT — now unblocked), 10-04 (scenario 4/5 UAT debt re-verify), prod bagkz replication (fold in the gate + the cred consolidation)]

# Tech tracking
tech-stack:
  added: []  # no new libraries — live bring-up of the 09-01..03 artifacts
  patterns:
    - "runData introspection as structural proof of a fail-closed branch: a suppressed exec must dead-end at Suppressed? with the whole downstream (debounce chain + Input type + Mark Read + agent + send) absent"
    - "deploy -> id-finalize -> rename canonical: first deploy assigns the n8n id, --export re-emits <id>-Name.json, the provisional is git-rm'd and CANONICAL + README repointed"
    - "cred consolidation by explicit id: bind Postgres by the id that actually exists on the target (ground-truthed against SQLite credentials_entity), keep the id-binding mechanism for prod portability"

key-files:
  created:
    - .planning/phases/09-semi-auto-suppression/09-04-SUMMARY.md
    - Tools/n8n/workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json
  modified:
    - Tools/n8n/build-set-reply-mode.py
    - Tools/n8n/README.md
    - Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json
    - Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json
    - Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql
  removed:
    - Tools/n8n/workflows/Set_Reply_Mode.json  # provisional, renamed to the id-prefixed canonical

key-decisions:
  - "Postgres cred consolidated to vvRrFiEXzLVqKjOx — the dev instance has a SINGLE Postgres cred (SQLite credentials_entity + live bindings); the plan/research id 1H5xlpFSESU4w6JH does not exist on the instance. Both ids always targeted the same Supabase DB (A3), so a dangling-binding hazard only, no data risk. Explicit-id binding kept for prod portability"
  - "A1 boolean routed clean — NO suppressed::boolean cast applied (the Suppressed? loose typeValidation coerced correctly as-is)"
  - "Set Reply Mode canonical finalized as SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json; provisional removed; CANONICAL + README repointed (Task 2 acceptance)"
  - "Suppressed dead-end runData now also asserts the Phase-10 debounce chain (Debounce Wait/Fetch Recent/Latest+Combine/Is Latest?) is absent — the chain sits on Suppressed? FALSE, downstream of the gate"

patterns-established:
  - "Live owner gate reconciled against cross-phase pre-satisfaction: 10-03 pre-applied the DDL + REST-imported the templates + created the fresh clones, so 09-04 verified only the unrecorded delta (probe round-trip + RLS, SetReplyMode deploy + curl, suppressed-branch runData) rather than redoing proven work"

requirements-completed: [SUP-01, SUP-02, SUP-03, SUP-04, SUP-05]

# Metrics
duration: owner-gate (multi-checkpoint live bring-up 2026-07-22; plan authored 2026-07-19)
completed: 2026-07-22
---

# Phase 9 Plan 04: Live Bring-up Gate Summary

**The server side of the semi-auto «Авто/Вместе» suppression is now LIVE on dev: `reply_mode_flags` exists with default-deny RLS, the Set Reply Mode webhook is deployed + activated (id `SCLcpn6DMDG3Z4VN`, cred `vvRrFiEXzLVqKjOx`), the curl+precedence matrix passes, and runData on BOTH channels proves a suppressed 1:1 chat dead-ends at `Suppressed?` (whole reply path incl. the Phase-10 debounce chain absent, stays unread) while a non-suppressed chat replies — the boolean routed clean with no cast, a fresh bot inherits the gate, and the repo's Postgres cred was consolidated to the single id that actually exists on the instance.**

## Performance

- **Duration:** owner-gate — multi-checkpoint live bring-up on 2026-07-22 (plan authored 2026-07-19)
- **Tasks:** 3 (Task 1 `checkpoint:human-action`; Tasks 2 & 3 `checkpoint:human-verify`) — all owner-run live
- **Files modified:** 6 across two repo commits (`ec15832` cred consolidation + `605e399` Task-2 finalize), plus this SUMMARY. The tasks' live work (DDL apply, deploy, curl, runData) has no repo diff.

## Accomplishments

- **Task 1 (SUP-01 live) — CLOSED.** The DDL was pre-applied to dev mid-Phase-10 (10-03 Deviation 2); the delta was verified today via `executeQuery` on the single Postgres cred `vvRrFiEXzLVqKjOx`: probe `insert → select` returned `true` → row deleted; `relrowsecurity = true`; `not has_table_privilege('anon', 'public.reply_mode_flags', 'select') = true` (default-deny RLS confirmed).
- **Task 2 (SUP-02/04) — CLOSED.** Set Reply Mode deployed + activated (id `SCLcpn6DMDG3Z4VN`, shared always-active by design, Upsert bound to `vvRrFiEXzLVqKjOx`). Curl matrix: (a) `{"success":true,"written":1}`, (b) `{"success":true,"written":1}`, (c) `{"success":false,"error":"bad_request"}`. Precedence SQL: `false` (override beats `'*'`), `true` (falls back to `'*'`), `false` (absence→reply); `count(*)` for `probeP` = 2, so the malformed call wrote NO row; `probeP` rows deleted. Repo finalized (`605e399`).
- **Task 3 (SUP-03/04/05) — CLOSED.** Suppressed dead-end runData verified on BOTH channels (n8n UI executions): with `suppressed:true` seeded via the live webhook, only `Webhook → … → Read Reply Mode → Suppressed?` executed; the entire downstream — the Phase-10 debounce chain (`Debounce Wait`/`Fetch Recent`/`Latest+Combine`/`Is Latest?`), `Input type`, `Mark Read`, agent, send — was absent; no reply, chat stayed unread. After `suppressed:false`, the full reply path ran and a reply arrived. **A1: the boolean came through CLEAN** — no `suppressed::boolean` cast needed, the branch routed correctly as-is. Fresh-bot propagation (SUP-05): REST grep PASSED on the 10-03 fresh clones (`fKCMIGXJSbLRimdR`/`pOMkkP8MYS8WhiNY`) — `Read Reply Mode` + `Suppressed?` present, fed by the group-chat `If`, `Suppressed?` TRUE→`[]` dead-end, FALSE→`Debounce Wait`. Test clones deactivated after the window; suppression rows deleted (table back to empty = fail-open default).
- **Cred consolidation (cross-cutting deviation).** Repo-wide swap of the dead `1H5xlpFSESU4w6JH` to the single live cred `vvRrFiEXzLVqKjOx` (deployer default + narrative, both templates, provisional workflow, SQL header, README) — prevents dangling bindings on any future REST re-import.

## Task Commits

The three plan tasks are owner-run live-instance gates (DDL apply, deploy, curl, runData) with no repo diff of their own. The repo changes this plan produced:

1. **Cred consolidation (Rule-3 blocking deviation, cross-cutting)** — `ec15832` (fix): `DEFAULT_POSTGRES_CRED_ID` → `vvRrFiEXzLVqKjOx` + de-staled the C5 narrative; both templates' Chat Memory + Read Reply Mode cred ids swapped; provisional workflow, SQL header, and README updated. `verify-telegram-parity.py` left untouched (its `PG_MEMORY_CRED` is a negative assertion).
2. **Task 2 repo finalize** — `605e399` (feat): added the id-finalized canonical `SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json`, `git rm` the provisional, repointed `CANONICAL` + the README refs.

**Plan metadata:** _(this docs commit — SUMMARY + STATE + ROADMAP)_

## Files Created/Modified

- `Tools/n8n/workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json` — NEW; the id-finalized canonical export (`active:true`, `Webhook[SetReplyMode]`/`Validate`/`If invalid?`/`Respond Error`/`Upsert Reply Mode[cred vvRrFiEXzLVqKjOx]`/`Respond`).
- `Tools/n8n/workflows/Set_Reply_Mode.json` — REMOVED (provisional, renamed to the id-prefixed canonical).
- `Tools/n8n/build-set-reply-mode.py` — `DEFAULT_POSTGRES_CRED_ID` → `vvRrFiEXzLVqKjOx`; `CANONICAL` → the id-prefixed file; C5 narrative de-staled across docstring/comment/help.
- `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` + `4VN3gsFaC2HUYmcc-Telegram_Bot.json` — Chat Memory + Read Reply Mode cred id → `vvRrFiEXzLVqKjOx` (2 spots each, name "Postgres" kept).
- `Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql` — header apply-through cred guidance → `vvRrFiEXzLVqKjOx` + single-cred note.
- `Tools/n8n/README.md` — deployer bullet, layout note, and the workflow-table row (id column + finalized filename) all repointed.

## Decisions Made

- **Consolidate the Postgres cred to `vvRrFiEXzLVqKjOx`** — ground truth 2026-07-22: dev has a single Postgres cred (SQLite `credentials_entity` + all four live template bindings); `1H5xlpFSESU4w6JH` does not exist on the instance. Both ids always pointed at the same Supabase DB (09-RESEARCH A3), so this is reference hygiene, not a data move. Explicit-id binding kept — load-bearing for prod (whose cred ids differ).
- **A1 boolean clean, no cast** — the `Suppressed?` `typeValidation: loose` coerced the incoming `suppressed` correctly; the `suppressed::boolean` fallback was NOT applied.
- **Verify only the delta, not the proven work** — 10-03 had already pre-applied the DDL, REST-imported both templates, and created the fresh clones; 09-04 verified the unrecorded remainder (probe round-trip + RLS, SetReplyMode deploy + curl/precedence, suppressed-branch runData) rather than redoing it.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Stale Postgres cred id would dangle on re-import**
- **Found during:** Task 2 (owner's live session, mid-deploy)
- **Issue:** The plan/research bound Postgres by id `1H5xlpFSESU4w6JH` ("bot-template Chat Memory"), but the dev instance has exactly ONE Postgres credential `vvRrFiEXzLVqKjOx` — `1H5xlpFSESU4w6JH` does not exist on it (ground-truthed via SQLite `credentials_entity` + all four live template bindings). Importing/re-importing any workflow that binds the dead id would dangle.
- **Fix:** Repo-wide consolidation to `vvRrFiEXzLVqKjOx` (deployer default + narrative, both bot templates ×2, provisional workflow, SQL header, README). Kept the explicit-id binding mechanism (portable to prod). `verify-telegram-parity.py` left untouched — its `PG_MEMORY_CRED` constant is a negative assertion (restamp cred ≠ memory cred) that stays true.
- **Files modified:** `build-set-reply-mode.py`, both templates, `Set_Reply_Mode.json`, the SQL, `README.md`.
- **Verification:** all 3 edited JSONs parse; binding-form grep `'"id": "1H5xlpFSESU4w6JH"'` returns zero; `--dry-run` binds `vvRrFiEXzLVqKjOx`; no data risk (same DB, A3).
- **Committed in:** `ec15832`.

**2. [Issue, owner-resolved] Accidental second deploy created a stray duplicate workflow**
- **Found during:** Task 2 (deploy)
- **Issue:** An accidental second deploy created a stray duplicate Set Reply Mode workflow bound to the dead cred.
- **Fix:** Owner deleted it (deactivate + DELETE via REST). No impact — the real workflow `SCLcpn6DMDG3Z4VN` served all curls (proven by `written:1`).
- **Committed in:** n/a (live-instance cleanup).

---

**Total deviations:** 1 auto-fixed (Rule 3 - blocking, repo commit `ec15832`) + 1 owner-resolved live-instance issue.
**Impact on plan:** The cred consolidation was required for correctness of any future re-import; no scope creep, no data risk (same Supabase DB). The stray-workflow cleanup had no bearing on the verdicts.

## Issues Encountered

- **Pre-existing unrelated red — NOT part of this plan:** `verify-telegram-parity.py` fails `node count 30 != 24`. Proven pre-existing (the templates are 30 nodes in HEAD and working; the verifier fails identically on the pre-edit HEAD) — caused by the Phase 9 gate (+2) and Phase 10 debounce (+4) splices the verifier's expected-count invariant was never updated for. Spun off to a separate background task (`task_b8134810`); untouched here per the "historical verifier, do not churn" constraint and scope boundary.

## User Setup Required

All live steps were owner-run (`secrets.json` / dev n8n / tunnel are deny-ruled for Claude): apply/verify the DDL through cred `vvRrFiEXzLVqKjOx`, deploy + activate Set Reply Mode, rebind its cred via `--update`, run the curl + precedence matrix, seed suppression via the live webhook, inspect runData on both channels, grep the fresh clones, and deactivate every test clone. Prod bagkz stayed dormant/untouched.

## Next Phase Readiness

- **09-05 (behavioral HUMAN-UAT — 5-scenario e2e on one build, both channels) is UNBLOCKED** — the server side is live and proven fail-closed; a customer-sends-a-message end-to-end is the phase-closing gate.
- **10-04 UAT debt** — scenario 4 (suggestions coalesce, «Вместе») and scenario 5 (semi-auto skips path) now re-verify trivially: `/webhook/SetReplyMode` is live (was the 404 blocker) and the suppression-before-debounce ordering is proven by this plan's runData.
- **Prod bagkz replication** must fold in BOTH the suppression gate AND the cred consolidation (bind Postgres to the id that exists on the prod instance) — stays dormant until run.

## Self-Check: PASSED

- Created files exist: `09-04-SUMMARY.md`, `Tools/n8n/workflows/SCLcpn6DMDG3Z4VN-Set_Reply_Mode.json` — FOUND
- Removed file gone: `Tools/n8n/workflows/Set_Reply_Mode.json` — ABSENT (renamed)
- Commits exist: `ec15832` (cred consolidation), `605e399` (Task-2 finalize) — FOUND
- Live verdicts recorded: Task 1 probe/RLS pass; Task 2 curl (a/b/c) + precedence (false/true/false) + no-partial-write; Task 3 suppressed dead-end both channels + A1 clean + fresh-bot grep pass; all clones deactivated.

---
*Phase: 09-semi-auto-suppression*
*Completed: 2026-07-22*

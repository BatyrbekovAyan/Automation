---
phase: 09-semi-auto-suppression
plan: 01
subsystem: infra
tags: [n8n, postgres, supabase, webhook, rls, reply-mode-flags, deployer]

# Dependency graph
requires:
  - phase: 07-dashboard-telegram (v1.1)
    provides: DashboardOutcomes /webhook pattern + Postgres executeQuery + queryReplacement idiom this workflow mirrors
  - phase: v1.0 live-suggestions
    provides: build-suggest-replies.py REST deployer skeleton (req/canonical_payload/deploy/export) + Suggest_Replies webhook->validate->respond graph
provides:
  - reply_mode_flags Postgres table DDL (pk(profile_id, chat_id), chat_id default '*', suppressed bool, default-deny RLS) — the persistence layer for SUP-01/SUP-04
  - Set_Reply_Mode.json canonical n8n workflow (Webhook -> Validate -> If invalid? -> Upsert(Postgres) -> Respond) — the /webhook/SetReplyMode write path for SUP-02
  - build-set-reply-mode.py REST deployer binding the Postgres cred by explicit id (C5)
affects: [09-02-gate-node, 09-03-client-sync, 09-04-live-apply]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "n8n write-webhook: Webhook(2.1) -> Validate(Code 2) fan-out one item per id -> If invalid?(2.2 boolean) -> Postgres upsert(2.6, on conflict do update, $n::boolean cast) -> respondToWebhook(1.5)"
    - "Postgres cred bound by EXPLICIT id (default 1H5xlpFSESU4w6JH), never by ambiguous name — two creds share the name 'Postgres'"
    - "default-deny RLS on a server-only table (enable RLS + revoke all from anon, authenticated; no policies — owner cred exempt from non-FORCE RLS)"

key-files:
  created:
    - Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql
    - Tools/n8n/workflows/Set_Reply_Mode.json
    - Tools/n8n/build-set-reply-mode.py
  modified:
    - Tools/n8n/README.md

key-decisions:
  - "SQL date-prefixed (C7): 2026-07-19-reply-mode-flags.sql, not reply_mode_flags.sql"
  - "Postgres cred bound by explicit id 1H5xlpFSESU4w6JH (C5) in both the workflow JSON and the deployer default; --postgres-cred / N8N_POSTGRES_CRED_ID override"
  - "Upsert casts $3::boolean (C6) because queryReplacement passes params as text"
  - "Validate fans out one {profileId,chatId,suppressed} item per surviving profileId; malformed body -> single {invalid:true} -> Respond-error BEFORE any DB write"
  - "Canonical workflow authored WITHOUT a top-level n8n id — id assigned on first deploy (09-04), then filename finalized to <id>-Set_Reply_Mode.json"

patterns-established:
  - "Write-side n8n webhook (validate-then-upsert) composed from Suggest_Replies ingress + Delete_File Postgres node"
  - "Deployer C5 variant: CRED_OVERRIDES['postgres'] always filled (flag > env > default id) so resolve_cred never guesses by name"

requirements-completed: [SUP-01, SUP-02, SUP-04]

# Metrics
duration: 6min
completed: 2026-07-19
---

# Phase 9 Plan 01: Suppression Persistence + Write Path Summary

**reply_mode_flags Postgres table (default-deny RLS) + the shared /webhook/SetReplyMode n8n workflow (validate -> upsert-on-conflict with $3::boolean, Postgres cred bound by explicit id 1H5xlpFSESU4w6JH) + its REST deployer — authoring only, no live DB/n8n touched.**

## Performance

- **Duration:** 6 min
- **Started:** 2026-07-19T12:12:48Z
- **Completed:** 2026-07-19T12:18:08Z
- **Tasks:** 2
- **Files modified:** 4 (3 created, 1 modified)

## Accomplishments
- Authored `reply_mode_flags` DDL: idempotent `pk(profile_id, chat_id)`, `chat_id default '*'` (bot-wide default row vs per-chat override), `suppressed boolean default false`, default-deny RLS + `revoke all from anon, authenticated`, header naming apply-through cred `1H5xlpFSESU4w6JH` (SUP-01, SUP-04 precedence support).
- Authored `Set_Reply_Mode.json` canonical workflow: `Webhook(2.1) -> Validate(Code 2) -> If invalid?(2.2) -> [true] Respond-error / [false] Upsert(Postgres 2.6) -> Respond(1.5)`. Malformed body responds `bad_request` before any DB write; valid body fans out one item per surviving profileId and upserts `on conflict (profile_id, chat_id) do update` with `$3::boolean` (SUP-02, SUP-04).
- Authored `build-set-reply-mode.py`: mirrors `build-suggest-replies.py` (req / canonical_payload / deploy / export) but binds the Postgres cred by **explicit id** — default `1H5xlpFSESU4w6JH`, override `--postgres-cred` / `N8N_POSTGRES_CRED_ID` (C5) — verified end-to-end via `--dry-run` (payload valid, cred bound to `1H5xlpFSESU4w6JH`).
- README bumped 12 -> 13 canonical workflows with a Set Reply Mode table row + deployer bullet.

## Task Commits

Each task was committed atomically:

1. **Task 1: Author reply_mode_flags DDL (SUP-01)** - `dd8562c` (feat)
2. **Task 2: Author Set Reply Mode workflow JSON + REST deployer (SUP-02, SUP-04)** - `69fa671` (feat)

**Plan metadata:** _(final docs commit — see below)_

## Files Created/Modified
- `Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql` - reply_mode_flags table DDL + default-deny RLS; header names cred 1H5xlpFSESU4w6JH as the apply target
- `Tools/n8n/workflows/Set_Reply_Mode.json` - shared /webhook/SetReplyMode workflow (validate -> upsert -> respond); no top-level id yet (assigned on first deploy)
- `Tools/n8n/build-set-reply-mode.py` - REST deployer; Postgres cred by explicit id, --dry-run / --update / --export
- `Tools/n8n/README.md` - workflow count 12 -> 13, Set Reply Mode row/bullet, deployer entry

## Decisions Made
- Followed the plan's C1-C7 corrections verbatim (the RESEARCH staleness table): date-prefixed SQL (C7), Postgres cred by explicit id not name (C5), `$3::boolean` cast (C6).
- Generated `Set_Reply_Mode.json` via a throwaway Python `json.dump` builder (scratchpad) rather than hand-escaping the embedded Validate jsCode — guarantees valid JSON escaping. The canonical file will be normalized on the 09-04 export round-trip.
- Omitted a top-level n8n `id`/`active` from the workflow JSON (the plan says do not invent an id); the deployer's `canonical_payload()` only reads name/nodes/connections/settings, so this is inert until 09-04 assigns the real id.
- Bumped the README workflow-table heading (12 -> 13) and added a table row with an `_(assigned on first deploy)_` id placeholder — keeps the README internally consistent without inventing an id.

## Deviations from Plan

None - plan executed exactly as written. No Rule 1-4 deviations were needed; all authoring matched the plan's locked node shapes, SQL, and deployer contract.

## Issues Encountered
- The scratchpad generator wrote its output one directory above `scratchpad/` (a `dirname` in the throwaway script), so the first `cp` missed. Re-ran the `cp` from the actual output path. No effect on the committed artifact.

## User Setup Required
None for this plan. Live application is explicitly deferred: applying the DDL on Supabase (through cred `1H5xlpFSESU4w6JH`) and deploying/activating the workflow on dev n8n + the curl matrix are the **09-04 owner gate** (RESEARCH Q2 [BLOCKING] — `secrets.json` is deny-ruled for Claude, so no live instance was touched here).

## Next Phase Readiness
- **09-02 (gate node)** can now reference the `reply_mode_flags` table shape and the locked resolve-query precedence; the table DDL exists to read against.
- **09-03 (client sync)** has the `/webhook/SetReplyMode` contract (`{ profileIds:[...], chatId, suppressed }`) to POST against, plus the sentinel-drop + one-row-per-profile semantics the client must mirror.
- **09-04 (live apply)** has the deployer (`build-set-reply-mode.py`) and DDL file ready to run; it assigns the real n8n id, renames the workflow file to `<id>-Set_Reply_Mode.json`, and runs the curl matrix.
- No blockers introduced. The unauthenticated webhook is accepted-risk (R-02-01 / T-09-02), consistent with every other app `/webhook/*`.

## Self-Check: PASSED

- Files: all 4 present (`2026-07-19-reply-mode-flags.sql`, `Set_Reply_Mode.json`, `build-set-reply-mode.py`, `09-01-SUMMARY.md`); `README.md` modified in `69fa671`.
- Commits: `dd8562c` (Task 1) and `69fa671` (Task 2) both exist in git log.
- Structural verify: SQL greps OK; `Set_Reply_Mode.json` valid JSON with correct typeVersions + `$3::boolean` + cred `1H5xlpFSESU4w6JH`; deployer `py_compile` + `--dry-run` OK (cred bound to `1H5xlpFSESU4w6JH`); README count is 13.

---
*Phase: 09-semi-auto-suppression*
*Completed: 2026-07-19*

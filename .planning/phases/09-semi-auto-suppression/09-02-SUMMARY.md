---
phase: 09-semi-auto-suppression
plan: 02
subsystem: infra
tags: [n8n, postgres, workflow, bot-template, reply-mode-flags, fail-closed, suppression-gate]

# Dependency graph
requires:
  - phase: 09-01 (this phase, Wave 1)
    provides: reply_mode_flags table shape (pk(profile_id, chat_id), chat_id default '*', suppressed bool) + Postgres cred id 1H5xlpFSESU4w6JH — the table this gate reads and the cred it binds
  - phase: v1.0/07 (Delete_File + Suggest_Replies)
    provides: Postgres executeQuery node shape (positional queryReplacement) + boolean-true If node shape ("If invalid?") copied for the gate
provides:
  - Read Reply Mode + Suppressed? fail-closed suppression gate spliced onto the group-chat If.main[0] in BOTH bot templates (WhatsApp + Telegram)
  - Byte-identical gate node bodies across both templates (only node id/position differ)
  - A suppressed 1:1 chat now dead-ends the reply BEFORE Input type -> no auto-reply, never marked read (stays unread)
affects: [09-03-client-sync, 09-04-live-apply, 09-05]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "n8n gate splice: rewire the group-chat If.main[0] TRUE output through a Postgres read (Read Reply Mode) + a boolean If (Suppressed?) whose TRUE branch is an empty array (dead-end) and FALSE branch continues the existing reply path"
    - "Fail-closed read node: no continueOnFail/onError/retryOnFail/alwaysOutputData so a Postgres error halts the execution (SUP-04)"
    - "Author the gate once, paste byte-identically into both templates (structurally identical at the insertion point)"

key-files:
  created: []
  modified:
    - Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json
    - Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json

key-decisions:
  - "Included the inline SQL comment (-- specific chat_id sorts before the '*' default) in the coalesce query — matches the design-spec/PATTERNS form verbatim; grep for the locked query text still matches"
  - "Placed the gate nodes below the If->Input flow line (positions offset in y) so the graph stays readable; positions are cosmetic"
  - "Suppressed? uses typeValidation:loose (copied from Suggest_Replies 'If invalid?') so a string 'true' coerces — 09-04 runData confirms; fallback is suppressed::boolean cast in Node A's query"

patterns-established:
  - "Suppression gate = Delete_File Postgres node shape + Suggest_Replies boolean-true If, composed onto If.main[0], TRUE=dead-end / FALSE=Input type"

requirements-completed: [SUP-03, SUP-04]

# Metrics
duration: 3min
completed: 2026-07-19
---

# Phase 9 Plan 02: Bot-Template Suppression Gate Summary

**Fail-closed «Вместе» suppression gate (Read Reply Mode Postgres read of reply_mode_flags via cred 1H5xlpFSESU4w6JH + a boolean Suppressed? If) spliced byte-identically onto the group-chat If.main[0] in both bot templates — a suppressed 1:1 chat now dead-ends the reply and stays unread; no error tolerance so a DB error halts the run (SUP-04).**

## Performance

- **Duration:** 3 min
- **Started:** 2026-07-19T12:23:08Z
- **Completed:** 2026-07-19T12:26:21Z
- **Tasks:** 2
- **Files modified:** 2 (both modified, 0 created)

## Accomplishments
- Authored the two gate nodes ONCE and pasted them byte-identically into both templates: `Read Reply Mode` (Postgres `executeQuery`, typeVersion 2.6, cred `1H5xlpFSESU4w6JH`) running the LOCKED always-one-row `coalesce(... order by (chat_id = '*') ... limit 1), false)` resolve query with `queryReplacement` binding `messages[0].profile_id` + `messages[0].from`, and `Suppressed?` (boolean-true If, typeVersion 2.2, typeValidation loose) reading `{{ $json.suppressed }}`.
- Rewired each template's group-chat `If.main[0]`: `If -> Read Reply Mode -> Suppressed?`; `Suppressed?` TRUE (main[0]) = empty array dead-end (no `Mark Read`, stays unread), FALSE (main[1]) -> `Input type` (existing reply path, byte-identical downstream).
- Fail-closed preserved (SUP-04): NO `continueOnFail`/`onError`/`retryOnFail`/`alwaysOutputData` anywhere — a genuine Postgres error throws and halts the execution, so a «Вместе» chat is never silently auto-answered. `grep -c continueOnFail` == 0 in both files.
- Verified the two gate node bodies are IDENTICAL across templates (Python deep-equal modulo node id/position/condition-uuid) and that node ids are unique within each file.

## Task Commits

Each task was committed atomically:

1. **Task 1: WhatsApp template reply-mode gate (SUP-03, SUP-04)** - `a1f94c1` (feat)
2. **Task 2: Telegram template reply-mode gate (identical paste) (SUP-03, SUP-04)** - `d50e34c` (feat)

**Plan metadata:** _(final docs commit — see below)_

## Files Created/Modified
- `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` - added Read Reply Mode + Suppressed? nodes on If.main[0]; rewired connections (If -> Read Reply Mode -> Suppressed?; TRUE dead-end, FALSE -> Input type). 81 insertions, 0 deletions — nothing downstream of Input type touched.
- `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` - byte-identical gate + identical rewire (fresh node ids/positions). 81 insertions, 0 deletions.

## Decisions Made
- Kept the inline SQL comment in the coalesce query (matches the design-spec/PATTERNS verbatim form); the acceptance grep for `order by (chat_id = '*')` still matches because the query is one JSON string (`\n`-escaped, single physical line).
- Gate node positions offset below the `If -> Input type` flow line for readability (WhatsApp `[-3456, 96]`/`[-3360, 96]`; Telegram `[-736, 544]`/`[-640, 544]`) — cosmetic only.
- `Suppressed?` uses `typeValidation: loose` (copied from `Suggest_Replies` "If invalid?") to coerce a possible string `"true"`; the runData branch confirmation + string-vs-bool check are the 09-04 owner gate.
- git rendered each rewire as a pure insertion (0 deletions) because the `"node": "Input type"` reference relocated into the `Suppressed?` FALSE branch rather than being removed — net effect verified correct by a Python graph assertion, not by the line diff.

## Deviations from Plan

None - plan executed exactly as written. No Rule 1-4 deviations were needed; both gates matched the plan's locked node shapes, query, cred, and rewiring exactly.

## Issues Encountered
None. Both templates were structurally identical at the insertion point as the plan promised (same `If` params, same Chat Memory cred `1H5xlpFSESU4w6JH`, same single-output `If.main[0] -> Input type` wiring, Telegram reply-entry node also named `Input type`), so the gate pasted cleanly into both.

## User Setup Required
None for this plan. This is pure workflow-JSON authoring — no live n8n or Supabase was touched (per plan constraint). Live redeploy of the templates + runData branch confirmation (Suppressed? TRUE dead-ends / FALSE reaches Input type) + fresh-bot propagation (SUP-05) are the **09-04 owner gate**.

## Next Phase Readiness
- **09-03 (client sync)** can wire the app-side toggle knowing the gate reads `reply_mode_flags` by `profile_id` + `from` — the client must send the exact `chatId` string the gate reads at `messages[0].from` (`…@c.us` on WhatsApp; tapi shape on Telegram) or a per-chat override silently never matches (the highest-risk integration detail, CONTEXT §decisions).
- **09-04 (live apply)** must: deploy both edited templates to dev n8n (import-by-literal-id, keep inactive), confirm via runData that `Suppressed?` TRUE dead-ends and FALSE reaches `Input type` (cast `suppressed::boolean` in the read if runData shows a string), and verify a freshly-created bot inherits the gate (SUP-05).
- No blockers introduced. The gate adds no NEW point of failure — the workflow already depends on this same Postgres cred for Chat Memory (T-09-07 accept).

## Self-Check: PASSED

- Files: both templates present and modified (`4wYitz5ek30SVNlT-WhatsApp_Bot.json`, `4VN3gsFaC2HUYmcc-Telegram_Bot.json`); this SUMMARY created.
- Commits: `a1f94c1` (Task 1) and `d50e34c` (Task 2) both exist in git log.
- Structural verify: both files valid JSON; `Read Reply Mode` + `Suppressed?` nodes exist; cred `1H5xlpFSESU4w6JH` bound; locked `coalesce(... order by (chat_id = '*') ... limit 1), false)` query present; `queryReplacement` binds `messages[0].profile_id` + `messages[0].from`; connections route `If -> Read Reply Mode -> Suppressed?` with TRUE=empty dead-end and FALSE=`Input type`; `continueOnFail` count 0 in both; gate node bodies IDENTICAL across templates.

---
*Phase: 09-semi-auto-suppression*
*Completed: 2026-07-19*

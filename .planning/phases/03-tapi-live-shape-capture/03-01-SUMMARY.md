---
phase: 03-tapi-live-shape-capture
plan: 01
subsystem: tooling
tags: [bash, curl, jq, wappi, tapi, telegram, shape-capture, read-only, security]

# Dependency graph
requires:
  - phase: research/telegram-parity
    provides: tapi endpoint map (§1) + the 13 MUST-VERIFY shape questions (§11)
provides:
  - Read-only owner-runnable tapi shape-capture script (Tools/tapi/capture-shapes.sh)
  - 13-question verdict checklist SHAPES.md (ships PENDING CAPTURE) + reactions-receive go/no-go
  - samples/ gitignore rule (PII-bearing raw payloads never committed)
  - Owner run + verdict-fill README and the 03-HUMAN-UAT phase-closing gate
affects: [Phase 4 (n8n Telegram template), Phase 5 (channel-aware parser/media/Normalize), TG-REACT-RECV]

# Tech tracking
tech-stack:
  added: [bash + curl + jq capture tooling under Tools/tapi/]
  patterns:
    - "Read-only endpoint allowlist enforced by acceptance greps (no send/reply/reaction/profile-mutation/auth/webhook, no mark_all)"
    - "Token read locally from secrets.json at runtime; used only in Authorization header; never echoed/logged/written (mirrors run-tests-headless.sh redaction discipline)"
    - "Machine-readable INDEX.json maps captured samples to the §11 questions so verdict-fill is mechanical"
    - "PII-bearing raw samples gitignored; only structural verdicts + redacted excerpts committed"

key-files:
  created:
    - Tools/tapi/capture-shapes.sh
    - Tools/tapi/SHAPES.md
    - Tools/tapi/README.md
    - .planning/phases/03-tapi-live-shape-capture/03-HUMAN-UAT.md
  modified:
    - .gitignore

key-decisions:
  - "Q9-Q13 ship as DEFERRED verdicts (resend-code cooldown, webhook payloads, quoted_message_id send, typing/start, mark_all mutation) — not observable via a read-only capture; a recorded disposition, not a scope cut"
  - "profile/all/get response is used only for tg-profile selection and is NOT written to samples (avoids persisting profile-list data); get/status is the first saved sample"
  - "messages/id/get is probed on up to 6 candidate ids to surface the reactions-field shape (reactions only appear on id/get, never messages/get)"

patterns-established:
  - "Owner-run live-probe tooling: deny-ruled secrets stay owner-side; Claude ships the safe transparent script + pre-filled checklist, the owner runs it (human gate)"

requirements-completed: [VER-01, VER-02]

# Metrics
duration: 10 min
completed: 2026-07-12
---

# Phase 3 Plan 1: tapi Live-Shape Capture Summary

**Read-only owner-runnable `Tools/tapi/capture-shapes.sh` (bash+curl+jq) that captures real Wappi tapi Telegram response shapes into a gitignored samples/INDEX.json, plus a pre-filled 13-question SHAPES.md verdict checklist with a reactions-receive go/no-go — the human gate grounding all Phase-5 Telegram parser/media work.**

## Performance

- **Duration:** 10 min
- **Started:** 2026-07-12T12:07:21Z
- **Completed:** 2026-07-12T12:17:49Z
- **Tasks:** 2
- **Files modified:** 5 (4 created, 1 modified)

## Accomplishments

- `Tools/tapi/capture-shapes.sh`: a provably read-only tapi probe — calls ONLY the 8 allowlisted GET/list endpoints (`profile/all/get`, `get/status`, `chats/get|filter|days/get`, `messages/get|id/get`, `contact/get`), contains none of the forbidden mutating/auth/webhook endpoints and never passes `mark_all`.
- Token safety: the Wappi token is read locally from `secrets.json` via `jq` and used only inside the `Authorization` header — never echoed, logged, argv-passed, or written to a sample file (enforced by acceptance greps).
- Media-variety auto-detection: walks each `messages/get` response, collects distinct `type` values, and saves one full sample per type; probes `messages/id/get` for a reply (isReply) and for a non-null `reactions` target; emits `samples/INDEX.json` mapping samples to the §11 questions.
- `--dry-run` prints the exact endpoint plan and exits 0 with no network call and no token read; graceful exits for missing jq (install hint) and no authorized TG profile (RU/EN in-app-auth message, exit 3).
- `SHAPES.md`: all 13 §11 questions pre-filled with Question / Evidence / VERDICT (`PENDING CAPTURE` for Q1–Q8, `DEFERRED` for Q9–Q13) / Downstream Phase-5 impact, plus an explicit "Reactions-receive go/no-go" section.
- `.gitignore` excludes `Tools/tapi/samples/`; `README.md` gives the owner run + verdict-fill guide; `03-HUMAN-UAT.md` records the phase-closing human gate.

## Task Commits

Each task was committed atomically:

1. **Task 1: Read-only tapi shape-capture script** - `f7970c3` (feat)
2. **Task 2: Verdict checklist + gitignore + README + human-gate note** - `423614b` (docs)

**Plan metadata:** committed separately (docs: complete plan)

## Files Created/Modified

- `Tools/tapi/capture-shapes.sh` - Owner-runnable read-only bash+curl+jq tapi shape-capture probe (374 lines)
- `Tools/tapi/SHAPES.md` - 13-question verdict checklist (PENDING CAPTURE / DEFERRED) + reactions-receive go/no-go
- `Tools/tapi/README.md` - Owner run + verdict-fill guide (token-local + samples-gitignored guarantees)
- `.planning/phases/03-tapi-live-shape-capture/03-HUMAN-UAT.md` - Phase-closing human gate checklist
- `.gitignore` - Added `Tools/tapi/samples/` exclusion (PII-bearing raw payloads)

## Decisions Made

- **Q9–Q13 ship DEFERRED, not blank:** resend-code cooldown, webhook payloads, send-side `quoted_message_id`, `chats/typing/start`, and `mark_all` mutation cannot be settled by a read-only capture. Each carries a `DEFERRED — ... resolve in Phase 4 e2e / Phase 5 / Phase 8` verdict with reason — a recorded disposition per VER-02, not a scope cut.
- **`profile/all/get` not persisted to samples:** used only for tg-profile selection; the first saved sample is `get/status`. Keeps the profile-list payload out of disk output.
- **Reactions probing via `messages/id/get`:** the `reactions` field surfaces on `messages/id/get`, never `messages/get`, so the script probes up to 6 candidate message ids and saves `message_id_full.json` (always, field-shape evidence) plus `message_id_reactions.json` (first non-null hit).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] `--dry-run` exited 2 instead of 0 on empty `--profile`**
- **Found during:** Task 1 (verification loop)
- **Issue:** Arg validation used `printf '%s' "$PROFILE_ID" | grep -Eq '^[A-Za-z0-9_-]*$'`. With an empty default `PROFILE_ID`, `printf '%s' ""` emits zero lines, so `grep -q` returns non-zero (nothing to match) and the script wrongly rejected the empty value — breaking the `--dry-run` exit-0 acceptance criterion on macOS bash 3.2.
- **Fix:** Switched both arg validations to bash `[[ "$X" =~ ... ]]`, which matches an empty string against a `*`-quantified pattern correctly.
- **Files modified:** Tools/tapi/capture-shapes.sh
- **Verification:** `--dry-run` (default and with `--profile`/`--chats`) now exits 0; bad `--profile`/`--chats` still exit 2.
- **Committed in:** f7970c3 (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug).
**Impact on plan:** The fix was required to meet the plan's own acceptance criterion (dry-run exit 0); no scope change.

## Issues Encountered

None — both tasks executed as written aside from the dry-run bug fixed above.

## User Setup Required

None - no external service configuration required by this plan. (The owner-run
capture itself is a separate human gate — see Next Phase Readiness.)

## Known Stubs

The `SHAPES.md` verdicts ship as `PENDING CAPTURE` (Q1–Q8) by design — this is the
plan's intended pre-filled state, resolved by the owner's capture run recorded in
`03-HUMAN-UAT.md`. Not a code stub; the tooling and checklist are complete.

## Next Phase Readiness

- **Code-complete and green.** The tooling is built, provably read-only + token-safe, and documented; all Task 1 + Task 2 acceptance greps pass.
- **Phase CLOSES on the owner-run gate (not automatable):** the owner must run `Tools/tapi/capture-shapes.sh` against an authorized dev Telegram profile, then fill the 13 `SHAPES.md` verdicts + the reactions-receive go/no-go (checklist in `03-HUMAN-UAT.md`). `secrets.json` is deny-ruled for Claude, so this cannot be done in an agent session.
- **Blocks Phase 5:** Telegram media/Normalize work (CHAT-03, CHAT-07) and the `type:"text"` mapping stay blocked until those verdicts are recorded.

## Self-Check: PASSED

- All 5 created files verified on disk (`capture-shapes.sh`, `SHAPES.md`, `README.md`, `03-HUMAN-UAT.md`, `03-01-SUMMARY.md`).
- `.gitignore` `Tools/tapi/samples/` rule verified present.
- Task commits verified in git log: `f7970c3` (feat), `423614b` (docs).
- All Task 1 + Task 2 acceptance greps re-run green; plan-level `bash -n` + `--dry-run` exit 0 with no network/token access.

---
*Phase: 03-tapi-live-shape-capture*
*Completed: 2026-07-12*

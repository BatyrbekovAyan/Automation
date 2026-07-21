---
phase: 10-message-batching-debounce
plan: 01
subsystem: infra
tags: [n8n, workflow, debounce, batching, wappi, whatsapp, telegram, python, migration]

# Dependency graph
requires:
  - phase: 09-semi-auto-suppression
    provides: "the Suppressed? If gate (main[1] FALSE branch) that the debounce stage splices onto"
  - phase: 04-telegram-template-parity
    provides: "Telegram_Bot on tapi bases + Input type matching chat||text (makes ONE channel-agnostic Code body work in both templates)"
provides:
  - "Pre-generation debounce+combine stage (Debounce Wait -> Fetch Recent -> Latest+Combine -> Is Latest?) spliced onto the Suppressed? FALSE branch in BOTH bot templates (BATCH-01)"
  - "One messages/get fetch (limit only, no mark_all) driving both the is-latest dedupe and the text combine; channel-agnostic Code body re-emitting the webhook body (BATCH-02)"
  - "apply-message-batching.py — idempotent by-node-name migration that authors the splice in both templates"
  - "verify-message-batching.py — structural verifier (with --dir) gating the fail-safe invariants"
affects: [10-03 (owner-run live deploy + runData gate re-imports both templates), prod bagkz replication (folds into the one-shot bulk copy), 10-02 (sibling client-side debounce)]

# Tech tracking
tech-stack:
  added: []  # no new libraries — composes existing n8n core nodes (wait 1.1 / httpRequest 4.2 / code 2 / if 2.2)
  patterns:
    - "Idempotent by-node-name n8n migration (apply-*.py: load/save indent=2 ensure_ascii=False, stable uuid5 node ids, find(...) is None guards, both-template loop)"
    - "Structural-assert verifier with argparse --dir so the same asserts gate a prod re-export (verify-*.py)"
    - "Re-emit the webhook body from a Code node inserted after an HTTP fetch so bare $json.body keeps resolving downstream (Pitfall 1)"

key-files:
  created:
    - Tools/n8n/apply-message-batching.py
    - Tools/n8n/verify-message-batching.py
  modified:
    - Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json
    - Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json
    - Tools/n8n/README.md

key-decisions:
  - "8s Debounce Wait (single tunable; < 65s -> n8n resumes in memory, no DB offload)"
  - "Latest+Combine re-emits the webhook body ({...wh, abort, combinedText}) so Input type / Download Audio / Text keep resolving $json.body after the inserted fetch+code"
  - "ONE channel-agnostic Code body (type === 'chat' || 'text'); only Fetch Recent base differs, derived per template from that template's own Mark Read url"
  - "Fetch Recent is limit-only with NO mark_all so the chat is not marked read during the wait (defers to the downstream humanizer Mark Read)"
  - "Text fallback reads bare $json.body (not $('Webhook').item) to match the re-emitted body and avoid fragile paired-item resolution across the Wait+HTTP+Code nodes"
  - "Debounce Wait carries no webhookId (per plan spec: parameters:{amount:8}); sub-65s waits resume in-memory and never invoke the resume webhook — n8n auto-assigns one on import if ever needed"

patterns-established:
  - "apply-message-batching.py mirrors apply-rag-fixes.py's skeleton for a second, different splice over the same two templates"
  - "verify-message-batching.py mirrors verify-telegram-parity.py (assert_that + node() helpers, exit 0 'ALL BATCHING ASSERTS PASSED' / exit 1 'BATCHING FAIL: <reason>')"

requirements-completed: [BATCH-01, BATCH-02]

# Metrics
duration: 9min
completed: 2026-07-21
---

# Phase 10 Plan 01: Message-Batching / Debounce Splice Summary

**A pre-generation `Debounce Wait -> Fetch Recent -> Latest+Combine -> Is Latest?` stage spliced onto the Suppressed? FALSE branch in BOTH bot templates via one idempotent Python migration, so a multi-fragment customer burst becomes ONE combined auto-reply; a structural verifier gates the fail-safe invariants.**

## Performance

- **Duration:** ~9 min
- **Started:** 2026-07-21T13:24:00Z
- **Completed:** 2026-07-21T13:33:51Z
- **Tasks:** 2
- **Files modified:** 5 (2 created, 3 modified)

## Accomplishments
- Authored `apply-message-batching.py` and ran it to splice the identical 4-node debounce stage onto `Suppressed?` `main[1]` (FALSE) in both `4wYitz5ek30SVNlT-WhatsApp_Bot.json` and `4VN3gsFaC2HUYmcc-Telegram_Bot.json`, before `Input type`.
- The `Latest+Combine` Code node re-emits the webhook body (Pitfall 1), sorts the fetch by `time` desc (Pitfall 3), treats `chat || text` as text (Pitfall 4, channel-agnostic), and uses `$('Webhook').first().json` (not `.item`, paired-item safety) — byte-identical in both templates.
- `Fetch Recent` GETs `<base>messages/get` with `profile_id`+`chat_id`+`limit` only and NO `mark_all` (Pitfall 5); base derived per template from its own `Mark Read` url (`api/sync` WA vs `tapi/sync` TG).
- `Is Latest?` dead-ends aborted fragments (`main[0]`) and proceeds the winner to `Input type` (`main[1]`); the `Text` set node now injects `={{ $json.combinedText ?? $json.body.messages[0].body }}`.
- Migration proven idempotent (second run = byte-identical, empty diff via content-hash compare); git diff touches ONLY the 4 new nodes + 5 rewired connection entries + the Text value.
- Authored `verify-message-batching.py` (with `--dir` for a future prod re-export gate); it greens on both spliced templates and was corruption-verified (injecting `mark_all` into a disposable copy → exit 1 `BATCHING FAIL`).
- README documents the splice + the re-run/verify workflow; workflow count unchanged (still 13 — no new canonical workflow).

## Task Commits

Each task was committed atomically:

1. **Task 1: Author + run apply-message-batching.py (splice 4 nodes into BOTH templates)** — `59eb6fa` (feat)
2. **Task 2: Structural verifier + README bump** — `26f68d3` (chore)

**Plan metadata:** _(final docs commit — SUMMARY + STATE + ROADMAP + REQUIREMENTS)_

## Files Created/Modified
- `Tools/n8n/apply-message-batching.py` — NEW; idempotent by-node-name splice of the 4 debounce nodes into both templates (skeleton copied from `apply-rag-fixes.py`).
- `Tools/n8n/verify-message-batching.py` — NEW; structural verifier asserting the 4 nodes, amount==8, the messages/get GET with no mark_all, the Code-node body re-emit, the Suppressed?→Debounce Wait / Is Latest?→Input type rewire, the Text fallback, and cross-template identity (skeleton from `verify-telegram-parity.py`).
- `Tools/n8n/workflows/4wYitz5ek30SVNlT-WhatsApp_Bot.json` — +4 nodes, Suppressed? main[1] rewired, Text value changed (base `api/sync`).
- `Tools/n8n/workflows/4VN3gsFaC2HUYmcc-Telegram_Bot.json` — same splice, base `tapi/sync`.
- `Tools/n8n/README.md` — Phase-10 note on both Bot rows + a shared note block; count stays 13.

## Decisions Made
- **8s window, single tunable** — sub-65s Wait resumes in memory (no DB offload); exact value tunes at the 10-03 owner e2e.
- **Body re-emit is non-negotiable (Pitfall 1)** — after the inserted HTTP fetch replaces the item, `Input type`/`Download Audio` (which read bare `$json.body`) would break; the Code node restores it with `{ ...wh, abort, combinedText }`.
- **One Code body for both channels** — `chat || text` mirrors the Phase-4 Telegram `Input type` `or`; only the Fetch Recent base URL diverges, derived from each template's Mark Read url (no hardcoded per-channel branch in the script).
- **Node positions** — the 4 nodes are laid out in a readable horizontal chain below `Suppressed?` (x-offsets +208/+416/+624, y+220); purely cosmetic, no functional effect. Stable `uuid5` node ids keyed off `wf["id"]+"-<name>"` keep re-runs byte-stable and the two templates distinct.
- **Debounce Wait has no webhookId** — followed the plan's `parameters:{amount:8}` spec exactly; sub-65s waits never use the resume webhook, and n8n auto-assigns a webhookId on import if it ever needs one.

## Deviations from Plan

None - plan executed exactly as written.

Every node shape, the verbatim `Latest+Combine` Code body, the connection rewire, the `Text` value, and the README note followed the plan's explicit specification. The only discretionary choices (node x-offset spacing; the exact `assert_that`/`node()` verifier helper shape) were left to Claude's discretion by the plan and CONTEXT.

## Issues Encountered
None. The JS `'\n'` join delimiter was authored via a Python raw string so it serializes to the correct `\\n` (two-char escape) in the JSON rather than a real newline — verified in the committed diff and by the byte-identical cross-template assert.

## Threat Mitigations (from the plan's threat register)
- **T-10-01-02** (Fetch Recent marking the chat read early) — mitigated: `Fetch Recent` carries no `mark_all`; the verifier asserts its absence (and the corruption spot-check proved the assert fires when `mark_all` is added).
- **T-10-01-03** (suppression-bypass if debounce spliced before the gate) — mitigated: the stage is on `Suppressed?` `main[1]` (not-suppressed); a semi-auto chat still dead-ends at `main[0]` and never enters the wait/fetch. The verifier asserts the `Suppressed? → Debounce Wait` edge.

No new security-relevant surface beyond the threat_model: `Fetch Recent` reuses the existing `WappiAuthToken` cred, hits an already-used Wappi endpoint (`messages/get`), adds no new secret and no schema change.

## Known Stubs
None. The `abort = false` / `combinedText = null` initial values in the Code node are legitimate defaults (media-latest correctly stays `combinedText = null` → processed alone), not stubs.

## User Setup Required
None for this plan — pure structural authoring of the committed JSON. Live deploy is owner-run in **10-03**: re-import BOTH templates by literal id, recreate any existing frozen dev clone so it inherits the debounce, activate only for the runData/e2e window, then deactivate. `secrets.json` / dev n8n / tunnel remain owner-run (deny-ruled for Claude).

## Next Phase Readiness
- Both templates carry the byte-identical debounce splice; `verify-message-batching.py` is green and gates the invariants (including as a `--dir` prod re-export go/no-go).
- **10-02** (client-side `IncomingDebounceGate` in `SuggestionsController`, BATCH-03) is independent and can proceed.
- **10-03** (owner-run live redeploy + runData abort-vs-combine proof + curl matrix) is the gate that validates this splice against the live dev instance — the structural half is done.

## Self-Check: PASSED

- Created files exist: `apply-message-batching.py`, `verify-message-batching.py` — FOUND
- Modified files exist: both `*-Bot.json` templates, `README.md` — FOUND
- Task commits exist: `59eb6fa` (Task 1), `26f68d3` (Task 2) — FOUND
- `apply-message-batching.py` idempotent (second run = empty diff) and `verify-message-batching.py` exits 0 "ALL BATCHING ASSERTS PASSED"; corruption spot-check → exit 1.

---
*Phase: 10-message-batching-debounce*
*Completed: 2026-07-21*

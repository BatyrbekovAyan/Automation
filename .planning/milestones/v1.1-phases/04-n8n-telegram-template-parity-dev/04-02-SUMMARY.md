---
phase: 04-n8n-telegram-template-parity-dev
plan: 02
subsystem: api
tags: [unity, n8n, wappi, tapi, rag, webhook, wwwform, workflows, uat]

# Dependency graph
requires:
  - phase: 04-n8n-telegram-template-parity-dev (04-01)
    provides: server-side RAG re-stamp reading WhatsappWorkflowId/TelegramWorkflowId from the Create webhook body ($1/$2 queryReplacement)
provides:
  - All four Unity create-workflow coroutines now send the OPPOSITE channel's workflow id (sentinel-guarded) so the 04-01 re-stamp can key on it
  - 04-HUMAN-UAT.md — the owner-driven dev deploy + live e2e runbook that closes TPL-06 and the phase
affects: [phase-5 (chat pipeline), phase-7 (client suggestions payload), phase-8 (prod bagkz replication)]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Opposite-channel workflow id carried as a WWWForm.AddField scalar, empty/null coerced to Bot.UnauthedProfileSentinel ('-1') so the server WHERE matches nothing when absent — no If-node, no transport change"

key-files:
  created:
    - .planning/phases/04-n8n-telegram-template-parity-dev/04-HUMAN-UAT.md
  modified:
    - Assets/Scripts/Main/Manager.cs

key-decisions:
  - "Inline sentinel guard (string.IsNullOrEmpty(x) ? Bot.UnauthedProfileSentinel : x) repeated in all 4 methods — no shared helper, matches each method's existing local-var style and keeps the diff minimal per the hard constraint"
  - "Distinct locals per method (tgId in the WhatsApp create methods, waId in the Telegram create methods) — no scope collisions, each declared once in its own method"

patterns-established:
  - "New create-workflow form fields sit immediately after the existing *ProfileId AddField so channel-identity fields stay grouped"

requirements-completed: [TPL-05, TPL-06]

# Metrics
duration: ~10min
completed: 2026-07-12
---

# Phase 4 Plan 02: Unity Opposite-Channel Workflow Id + TPL-06 Owner Gate Summary

**All four Unity create-workflow coroutines now POST the opposite channel's workflow id (sentinel-guarded to "-1") so the 04-01 server-side RAG re-stamp can convert a bot's pre-auth "-1" chunks on late channel auth, plus a complete owner-driven dev deploy + live e2e runbook (04-HUMAN-UAT.md) that closes TPL-06.**

## Performance

- **Duration:** ~10 min
- **Started:** 2026-07-12T13:45Z (approx, first file read)
- **Completed:** 2026-07-12T13:55Z
- **Tasks:** 2
- **Files modified:** 1 modified (Manager.cs) + 1 created (04-HUMAN-UAT.md)

## Accomplishments
- **TPL-05 client half:** `CreateWhatsappWorkflowFromStart` / `FromEdit` now send `TelegramWorkflowId`; `CreateTelegramWorkflowFromStart` / `FromEdit` now send `WhatsappWorkflowId`. Each value is read from the bot's own opposite-channel field (`bot`/`openBot` `.GetComponent<Bot>().{telegram,whatsapp}WorkflowId`) and coerced to `Bot.UnauthedProfileSentinel` ("-1") when empty/null. The fields sit right after the existing `*ProfileId` AddField (grouped), read server-side from `$json.body` exactly like Name/BusinessTypeId — zero transport change.
- **TPL-06 owner gate:** `04-HUMAN-UAT.md` documents the 6 locked owner steps — verifier + Postgres credential (`vvRrFiEXzLVqKjOx`) resolve/UPDATE pre-flight, dev n8n + tunnel start with `rotate-tunnel.py`, import the 4 workflows by literal id (templates INACTIVE) + recreate stale TG clones, authorize a dev TG profile + create a bot, the 4-check conversation e2e (text / voice / memory / pre-auth file re-stamp with a `botTgId` Supabase spot-check), then deactivate the clone. Result boxes left open per spec.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add opposite-channel workflow id to all 4 create-workflow forms (TPL-05 client)** - `71e193d` (feat)
2. **Task 2: Write 04-HUMAN-UAT.md (TPL-06 owner deploy + e2e gate)** - `3216c8a` (docs)

**Plan metadata:** committed with STATE.md/ROADMAP.md/REQUIREMENTS.md updates.

## Files Created/Modified
- `Assets/Scripts/Main/Manager.cs` - 4 new `form.AddField(...)` lines (2 `TelegramWorkflowId` in the WhatsApp create methods, 2 `WhatsappWorkflowId` in the Telegram create methods), each preceded by a sentinel-guarded local; nothing else touched (URL, response handling, other fields unchanged)
- `.planning/phases/04-n8n-telegram-template-parity-dev/04-HUMAN-UAT.md` - owner runbook (6 locked steps, workflow-id table, PASS/FAIL result section, closes TPL-06)

## Decisions Made
- **Inline sentinel guard, no helper.** Each of the four methods declares a single local (`tgId` in the WhatsApp create methods, `waId` in the Telegram create methods) and inlines `string.IsNullOrEmpty(x) ? Bot.UnauthedProfileSentinel : x`. Matches the surrounding style and keeps the change inside the "only add the form fields" hard constraint — no shared helper, no other Manager.cs edits.
- **Field placement.** New field added immediately after each method's existing `*ProfileId` AddField so channel-identity fields stay grouped and the diff is a clean +2 lines per method.

## Deviations from Plan

### Documentation-only deviation (verify command count)

**1. [Rule 1 - Verify assertion off-by-one due to pre-existing occurrences]**
- **Found during:** Task 1 verification.
- **Issue:** The plan's automated verify asserts `grep -c 'AddField("WhatsappWorkflowId"' == 2` and `grep -c 'AddField("TelegramWorkflowId"' == 2`. The file ALREADY contained one `AddField("WhatsappWorkflowId")` and one `AddField("TelegramWorkflowId")` at lines 3177-3178 inside `SaveWorkflows(...)` — the **Edit**-webhook (EditWhatsappWorkflow/EditTelegramWorkflow) form, which is out of scope for this plan. After adding the two required create-method fields for each key, the whole-file count is **3**, not 2.
- **Fix:** Implemented the functional intent exactly as specified — both create methods now carry the opposite-channel field (the objective + hard constraint govern). Did NOT touch the pre-existing `SaveWorkflows` fields (hard constraint: only the four create coroutines). The verify command's expected literal (2) is based on a stale assumption of zero pre-existing occurrences; the correct post-change whole-file count is 3 (1 pre-existing Edit-form + 2 new create-form) for each key.
- **Files modified:** Assets/Scripts/Main/Manager.cs (create methods only).
- **Verification:** Confirmed via `grep -n` that the four new fields land at lines 2736 (`TelegramWorkflowId`, CreateWhatsapp FromStart), 2828 (`TelegramWorkflowId`, CreateWhatsapp FromEdit), 2879 (`WhatsappWorkflowId`, CreateTelegram FromStart), 2974 (`WhatsappWorkflowId`, CreateTelegram FromEdit); braces/parens balanced (478/478, 1952/1952).
- **Committed in:** `71e193d` (Task 1 commit).

---

**Total deviations:** 1 (documentation-only — verify assertion vs. pre-existing scope-external occurrences; no code scope creep).
**Impact on plan:** None functional. Both create methods carry the sentinel-guarded opposite-channel id exactly as the objective and hard constraint require. The Task-2 verify passed verbatim.

## Issues Encountered
None. The Task-1 grep-count nuance (above) is the only wrinkle and is a stale plan assertion, not a code problem.

## Compile Verification
- **NOT run here** (per hard constraint + plan acceptance criteria — Editor state unknown; do not run the Unity test suite or recompile via MCP). Brace/paren balance verified statically (478/478 braces, 1952/1952 parens). The `validate-cs.sh` Edit/Write hook ran on each edit with no errors. **An Editor/device compile pass is owner-confirmed as part of the TPL-06 gate** — recorded as pending for the phase verifier.

## User Setup Required
None in this plan's code. The live deploy + e2e (TPL-06) is the OWNER gate documented in `04-HUMAN-UAT.md`: start dev n8n + tunnel, import the 4 workflows by literal id (templates INACTIVE), authorize a dev Telegram profile, create a Telegram bot, run the text/voice/memory/pre-auth-re-stamp e2e, then DEACTIVATE the clone. Not runnable here — no live n8n and its API key is in deny-ruled `secrets.json`.

## Next Phase Readiness
- **Phase 4 code-complete:** 04-01 (server workflows) + 04-02 Task 1 (Unity form fields) are done and committed; only the owner-run TPL-06 gate (04-HUMAN-UAT.md) remains to close the phase.
- **Re-stamp wired end to end:** the client now emits `WhatsappWorkflowId`/`TelegramWorkflowId`; the 04-01 re-stamp keys on them and is a safe no-op when absent/"-1".
- **Phase 5 (chat pipeline) / Phase 7 (client suggestions payload):** unaffected by this plan; no seam changes here.
- **Carried blocker:** existing dev Telegram clones carry old `api/sync` URLs — the UAT runbook step 3 instructs recreating them after importing the fixed template.

## Self-Check: PASSED

- FOUND: Assets/Scripts/Main/Manager.cs (4 new AddField lines at 2736/2828/2879/2974)
- FOUND: .planning/phases/04-n8n-telegram-template-parity-dev/04-HUMAN-UAT.md
- FOUND commits: 71e193d, 3216c8a

---
*Phase: 04-n8n-telegram-template-parity-dev*
*Completed: 2026-07-12*

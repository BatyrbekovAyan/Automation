---
phase: 10-message-batching-debounce
plan: 03
subsystem: infra
tags: [n8n, workflow, debounce, batching, wappi, whatsapp, telegram, rundata, owner-gate, binarymode, migration]

# Dependency graph
requires:
  - phase: 10-message-batching-debounce (10-01)
    provides: "the committed debounce splice (Debounce Wait -> Fetch Recent -> Latest+Combine -> Is Latest?) in both bot templates + verify-message-batching.py"
  - phase: 10-message-batching-debounce (10-02)
    provides: "the sibling client-side IncomingDebounceGate (independent; verified separately)"
  - phase: 09-semi-auto-suppression
    provides: "the Read Reply Mode + Suppressed? gate the debounce sits after (its reply_mode_flags DDL was still an open 09-04 gate — applied mid-run here)"
provides:
  - "Both debounce templates verified LIVE on dev n8n: a multi-fragment message yields exactly ONE combined reply, earlier fragments abort at Is Latest? (BATCH-01), proven via execution runData on BOTH channels"
  - "id-equality (webhook messages[0].id == messages/get newest-incoming id) confirmed on both channels — the highest-risk assumption (Pitfall 2/A3) holds; abort mechanism rides the inequality (BATCH-02)"
  - "Fresh bot clones inherit the debounce (Pitfall 6 propagation) — proven by two post-fix clones carrying all 4 nodes"
  - "fix-orchestrator-settings.py — two-mode (--canonical / --live) binaryMode-strip migration for the 4 Create/Edit orchestrators"
affects: [10-04 (closing behavioral UAT — depends on this live proof), 09-04 (reply_mode_flags DDL partially pre-satisfied here), prod bagkz replication (binaryMode strip + debounce fold into the one-shot copy)]

# Tech tracking
tech-stack:
  added: []  # no new libraries — composes existing n8n core nodes + REST
  patterns:
    - "Two-mode n8n migration (--canonical: patch committed JSONs offline; --live: surgical GET/patch/PUT/re-activate the live instance) so a dev-URL/prod-URL divergence is never fixed by re-importing a canonical file"
    - "Blacklist-strip a stored settings key (binaryMode) the write schema rejects, via a Set-node assignment overriding the passed-through settings — mirrors rotate-tunnel.py's PUT idiom"
    - "runData introspection as structural proof: per-execution abort/combine/id-equality verdicts over the debounce nodes (preferred over JSON grep — proves behavior, not shape)"

key-files:
  created:
    - Tools/n8n/fix-orchestrator-settings.py
  modified:
    - Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json
    - Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json
    - Tools/n8n/workflows/3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json
    - Tools/n8n/workflows/TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json

key-decisions:
  - "Debounce Wait amount stays 8s — no window tuning requested at e2e (propagation within window confirmed; single-message latency acceptable)"
  - "Orchestrator binaryMode fix is a blacklist (strip ONLY binaryMode, keep availableInMCP) — the write schema accepts everything else; whitelisting settings would drop future-legit keys"
  - "Live orchestrators fixed surgically in place (--live), never by canonical re-import — live dev uses localhost URLs, canonical carries prod Cloud URLs"
  - "Edit orchestrators patched proactively (same latent passthrough) though only the Create path had failed"

patterns-established:
  - "fix-orchestrator-settings.py mirrors rotate-tunnel.py conventions (key from N8N_API_KEY|secrets.json at owner runtime; base from N8N_BASE_URL; http() helper; binaryMode strip) for a different surgical live repair"

requirements-completed: [BATCH-01, BATCH-02]

# Metrics
duration: owner-gate (multi-round live bring-up + runData + 2 cross-phase repairs)
completed: 2026-07-22
---

# Phase 10 Plan 03: Live Debounce Bring-up + runData Matrix Summary

**Both debounce templates proven LIVE on dev n8n via execution runData on BOTH channels — a two-fragment burst yields exactly ONE combined reply (the earlier fragment aborts at `Is Latest?`, the winner combines the concatenation), single/boundary cases hold, webhook↔sync id-equality holds (WA jid-hex, TG bare numeric), and fresh clones inherit the debounce — after unblocking two cross-phase live-only defects (an n8n 2.27.4 `binaryMode` orchestrator 400 and the still-unapplied Phase-9 `reply_mode_flags` DDL).**

## Performance

- **Duration:** owner-gate cycle (live deploy → runData matrix → 2 cross-phase repairs), completed 2026-07-22
- **Tasks:** 2 (both `checkpoint:human-verify`, owner-run)
- **Files modified:** 5 (1 created, 4 modified) — all in the deviation commit; the two plan tasks are live-instance actions with no repo diff

## Accomplishments
- **Task 1 (owner-deployed):** both templates re-imported to dev by literal id (`4wYitz5ek30SVNlT` / `4VN3gsFaC2HUYmcc`), `active=False`; all 4 debounce nodes wired after `Suppressed?`; `amount=8`; frozen clones recreated (later superseded by the post-fix recreate). Claude pre-ran `verify-message-batching.py` (exit 0) as the pre-deploy gate.
- **Task 2 (owner-run runData matrix, BOTH channels):** the mechanism proven end-to-end (verdicts below). id-equality — the highest-risk assumption — holds on every winner.
- **Fresh-clone propagation (scenario E):** two newly created bots (`fKCMIGXJSbLRimdR`, `pOMkkP8MYS8WhiNY`, both `name=11`) carry all 4 debounce nodes — proving the orchestrator fix worked AND that the Create clone path inherits the debounced template.
- **Unblocked two live-only cross-phase defects** (see Deviations): the `binaryMode` orchestrator 400 (fixed in `d594f17`) and the missing Phase-9 `reply_mode_flags` table (owner-applied DDL mid-gate).

## runData Matrix — recorded verdicts (both channels)

**(A) Two fragments → ONE combined reply.** Sent «Здравствуйте, у вас есть колодки?» then «На камри 70».
- **Telegram:** exec **847 ABORTED** (trigger `'23483'`, newest `'23484'`, `abort:true`, no reply nodes) / exec **848 WINNER** (trigger `'23484'`==newest, id-equality True, `Input type`/`Text`/`AI Agent` all present).
- **WhatsApp:** exec **851 ABORTED** (trigger `'3A25BFB47B58D30A1E12'`, newest `'3A44E70ED15E2DA10912'`) / exec **852 WINNER** (trigger `'3A44E70ED15E2DA10912'`==newest, id-equality True).
- One reply arrived per channel (owner confirmed "ok").
- **combinedText was the fragment pair REPEATED (4 lines) — NOT a defect** (see analysis note below).

**(B) Single complete message → ONE reply after the window.** «Здравствуйте, у вас есть запчасти на лексус?»
- **Telegram:** exec **864 WINNER**, `combinedText` == that single line (trigger `'23486'`).
- **WhatsApp:** exec **863 WINNER**, same text (trigger `'3A943C761C76A5B308D3'`), id-equality True. (863's ran-list omitted the Supabase Vector Store — a conditional RAG skip, unrelated.)
- Clean 1-line `combinedText` here (immediately after A) confirms the combine boundary reset once a reply landed.

**(C) Bot/owner reply between fragments bounds the run.**
- **WhatsApp:** exec **866 WINNER**, `combinedText` == `'2007 года'` only (trigger `'3A0CF57FDCB6CC065BE5'`).
- **Telegram:** exec **868 WINNER**, same (trigger `'23490'`).
- The combine walk stopped at the `fromMe` reply; fragment A was not re-answered.

**(D) id-equality (Pitfall 2/A3 — HIGHEST RISK).** `id-equality str==` **True on ALL winners** (848, 852, 863, 864, 866, 868). Id forms recorded per channel:
- **WhatsApp:** jid-hex style — `3A25BFB47B58D30A1E12`, `3A44E70ED15E2DA10912`, `3A943C761C76A5B308D3`, `3A0CF57FDCB6CC065BE5`.
- **Telegram:** bare numeric strings — `23483`, `23484`, `23486`, `23490`.
- The `False` on execs 847/851 is the aborted fragment — **that inequality IS the abort mechanism** (a newer fragment arrived during the wait), expected, not a failure.

**(E) Fresh-clone propagation + Step-6 deactivation sweep (one audit run):**
```
OK 4VN3gsFaC2HUYmcc active=False  Telegram Bot
OK 4wYitz5ek30SVNlT active=False  WhatsApp Bot
OK fKCMIGXJSbLRimdR active=False  name=11   (fresh clone)
OK pOMkkP8MYS8WhiNY active=False  name=11   (fresh clone)
```
Both templates + both fresh clones carry all 4 debounce nodes; everything left `active=False` (real-contacts constraint satisfied; prod bagkz untouched). This one run de-facto proves Task-1's Step-0 per-id grep and R1's fix (clones exist post-fix and both templates read `OK`).

## Task Commits

The two plan tasks are owner-run live-instance actions (deploy + runData) with no repo diff; the only repo change was the blocking-issue deviation:

1. **Task 1: Redeploy both templates by literal id + recreate frozen clones** — owner-run live (splice authored in `59eb6fa`/10-01); no repo commit.
2. **Task 2: runData matrix + fresh-clone propagation** — owner-run live runData; no repo commit.

**Deviation:** `d594f17` (fix) — strip `binaryMode` from the orchestrator clone payload (4 orchestrators + `fix-orchestrator-settings.py`).
**Plan metadata:** _(this docs commit — SUMMARY + STATE + ROADMAP)_

## Files Created/Modified
- `Tools/n8n/fix-orchestrator-settings.py` — NEW; two-mode (`--canonical`/`--live`) migration that appends a `binaryMode`-stripping `settings` assignment to every Set node passing settings through, and (live) PUTs the fix back + re-activates the Create orchestrators.
- `Tools/n8n/workflows/XuvOp7TxOImOAmlj-CreateWhatsappWorkflow.json` — +1 `settings` assignment (`Set Fields`).
- `Tools/n8n/workflows/Uz6HBBUpAiUqVysB-CreateTelegramWorkflow.json` — +1 (`Set Fields`).
- `Tools/n8n/workflows/3qax5J9u2qsT9Vao-Edit_Whatsapp_Workflow.json` — +2 (`Set Fields`, `Set Bussiness Type`).
- `Tools/n8n/workflows/TwWPW3gIyjZS3foR-Edit_Telegram_Workflow.json` — +2 (`Set Fields`, `Set Bussiness Type`).

## Decisions Made
- **Window stays 8s** — no tuning requested; propagation-within-window (A2) confirmed by every winner's `Fetch Recent` carrying the just-arrived fragment; single-message latency accepted.
- **Blacklist-strip binaryMode only** — the write schema accepts `availableInMCP` and the rest; whitelisting would silently drop future-legit settings keys.
- **Surgical live repair, never canonical re-import** — dev orchestrators use `localhost` URLs, canonical exports carry prod Cloud URLs; `--live` edits in place.
- **Patched both Edit orchestrators proactively** — identical latent passthrough would 400 on the next app-side bot edit of a `binaryMode`-stamped clone.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] n8n 2.27.4 `binaryMode` breaks the Create/Edit orchestrators (400)**
- **Found during:** Task 2, fresh-bot test (scenario E) — both Create orchestrators failed; app-side bots looked created but NO n8n clone workflows appeared.
- **Issue:** n8n 2.27.4 stamps `"binaryMode":"separate"` into a workflow's STORED settings on save. Task 1's REST-PUT redeploy re-saved both bot templates (2026-07-21 16:37/16:38Z), so `Get Sample Workflow` now returns settings carrying `binaryMode`; the orchestrators' `Set Fields`/`Set Bussiness Type` nodes pass settings through verbatim (`includeFields "...,settings"`) into `Create Workflow` (POST) / `Update Workflow` (PUT); the n8n public write schema rejects the unknown property → `NodeApiError 400 "request/body/settings must NOT have additional properties"`. Evidence: dev execs **831** (CreateTelegramWorkflow 05:55:52Z) / **832** (CreateWhatsappWorkflow 05:57:14Z) both `status=error`, `lastNodeExecuted "Create Workflow"`; last good create 2026-07-21 08:06Z (pre-redeploy). Both `Edit_*` orchestrators carried the same latent passthrough.
- **Fix:** New `fix-orchestrator-settings.py` appends a `settings` assignment overriding the passed-through settings with a copy that drops ONLY `binaryMode` (blacklist; keeps `availableInMCP`). `--canonical` patched the 4 committed JSONs (+6 assignments, URLs untouched, idempotent, disk-verified); owner ran `--live` (R1) to GET/patch/PUT each live orchestrator (stripping `binaryMode` off its OWN settings too, else the PUT 400s) and re-activate the two Create orchestrators (`CreateWhatsappWorkflow` had been owner-deactivated while investigating).
- **Files modified:** `Tools/n8n/fix-orchestrator-settings.py` + the 4 orchestrator JSONs.
- **Verification:** scenario E — two fresh clones created post-`--live` carry all 4 debounce nodes; the create flow no longer 400s.
- **Committed in:** `d594f17`.

**2. [Rule 3 - Blocking, cross-phase] Phase-9 `reply_mode_flags` table did not exist on dev**
- **Found during:** Task 2, scenario A first attempt (after the orchestrator fix) — the execution failed at the Phase-9 `Read Reply Mode` node: `relation "reply_mode_flags" does not exist`.
- **Issue:** Phase 9's `09-04` Task 1 ([BLOCKING] DDL apply of `Tools/n8n/supabase/2026-07-19-reply-mode-flags.sql`) is a still-open owner gate; the 10-03 redeploy was the FIRST live execution of the Phase-9-gated template on a clone, so the missing table surfaced here.
- **Fix:** Owner applied the DDL mid-gate through the Chat Memory Postgres cred connection (per the SQL file header) and re-ran. The gate now passes — `Read Reply Mode` + `Suppressed?` → `main[1]` visible in every subsequent exec; an empty table **fails open**, so Авто replies proceed.
- **Files modified:** none in this repo (live DB mutation; the DDL file is Phase-9's).
- **Verification:** all scenario A–D winners ran the full reply path through the gate.
- **Committed in:** n/a (live DB). **Note:** this partially pre-satisfies `09-04` Task 1, but 09-04's own SetReplyMode deploy + suppression-branch runData checks remain OPEN, and 10-04's "semi-auto chat skips the path" composition check depends on them.

### Analysis note — scenario-A `combinedText` duplication is EXPECTED, not a defect
Both scenario-A winners' `combinedText` was the two fragments **repeated** (4 lines: `Здравствуйте, у вас есть колодки?\nНа камри 70\nЗдравствуйте, у вас есть колодки?\nНа камри 70`). This is the combine walk working correctly, not per-message duplication: the owner's FIRST scenario-A attempt (pre-DDL, exec-831 era) died at `Read Reply Mode` BEFORE any reply, leaving those two fragments un-replied in history. The combine walk correctly spans the whole un-replied trailing run back to the last `fromMe` boundary, so the single reply covered both un-replied rounds. The chronological order (frag1, frag2, frag1, frag2 = two ordered pairs) confirms a run-walk, not duplication. Scenario B's clean 1-line `combinedText` immediately after proves the boundary reset once a reply landed. Recorded so a future reader does not misread it as a bug.

---

**Total deviations:** 2 auto-fixed (both Rule 3 - blocking; one repo fix `d594f17`, one live-DB DDL owner-applied).
**Impact on plan:** Both were live-only defects that a JSON/structural check could never surface — exactly why this owner-run runData gate exists. No scope creep; the plan's success criteria are met. Window unchanged (`amount=8`).

## Issues Encountered
- Both blocking defects above were live-instance-only (n8n version behavior + an unapplied upstream DDL); resolved within the gate. No other issues.

## Threat Mitigations (from the plan's threat register)
- **T-10-03-01** (test clone left ACTIVE) — mitigated: every clone `active=False` in the Step-6 audit; the Create-orchestrator auto-activation of new clones was deactivated post-test.
- **T-10-03-02** (webhook vs sync id divergence) — mitigated: id-equality is a first-class runData assertion, True on all 6 winners across both channels; the aborted-fragment inequality is the intended abort signal.
- **T-10-03-03** (Fetch Recent marking read early) — upheld: `Fetch Recent` has no `mark_all` (verifier-asserted in 10-01); the reply path marks read only via the downstream humanizer `Mark Read`.
- **T-10-03-04** (prod bagkz accidental target) — upheld: all deploy/runData dev-only; prod dormant and untouched.

## Known Stubs
None.

## User Setup Required
All live steps were owner-run this gate: re-import both templates by literal id, `fix-orchestrator-settings.py --live`, apply the Phase-9 `reply_mode_flags` DDL, recreate + test bots, deactivate every clone. `secrets.json` / dev n8n / tunnel remain owner-run (deny-ruled for Claude).

## Next Phase Readiness
- **10-04** (closing behavioral UAT — multi-fragment → one reply arrives; suggestions coalesce; semi-auto skips the path) is unblocked: the auto-reply half is live-proven. 10-04's "semi-auto skips the path" check depends on the Phase-9 suppression runData (still open in 09-04).
- **Prod bagkz replication** must carry BOTH the debounce splice AND the `binaryMode` orchestrator strip (fold into the one-shot copy).
- **09-04** Task 1 (reply_mode_flags DDL) is partially pre-satisfied on dev; its SetReplyMode deploy + suppression runData remain open.

## Self-Check: PASSED

- Created file exists: `10-03-SUMMARY.md`, `fix-orchestrator-settings.py` — FOUND
- Modified files exist: all 4 orchestrator JSONs — FOUND
- Deviation commit exists: `d594f17` — FOUND
- `fix-orchestrator-settings.py --canonical` idempotent (re-run adds 0), URLs untouched, disk-verified; runData verdicts (A–E) recorded on both channels with id-equality True on all 6 winners.

---
*Phase: 10-message-batching-debounce*
*Completed: 2026-07-22*

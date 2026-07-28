---
phase: 08-device-uat-milestone-closeout
verified: 2026-07-21T18:00:00Z
status: passed
reconciled: 2026-07-21T00:00:00Z
score: 11/11 autonomous must-haves verified (3 ROADMAP success criteria + 8 plan/phase-level truths); 0 failed; 1 pre-existing tooling-drift finding (not phase-08-caused, see Finding F-1); 3 owner-run items pending
overrides_applied: 0
supersedes: "08-VERIFICATION.md dated 2026-07-15T10:07:35Z (written when the phase was 3 plans / 1 Gate-A round; the phase since grew to 35 plans across 7 gap-closure rounds — that version is preserved in git history, this version is authoritative)"
human_verification:
  - test: "Gate B — One-shot prod bagkz bulk replication (08-PROD-REPLICATION.md, 9 ordered steps)"
    expected: "Owner executes the runbook against dormant prod bagkz: pre-flight verify green -> recreate credentials BY NAME (Postgres Session-pooler 5432, Supabase, OpenAi, WappiAuthToken) -> apply the 3 Supabase migrations + prove the re-stamp UPDATE grant -> literal-id import of the workflows with BOTH bot templates left INACTIVE -> deploy Suggest_Replies via build-suggest-replies.py with the new prod cred-id overrides -> wire the Orphan sweep -> post-import `verify-telegram-parity.py --dir <prod-export>` prints ALL PARITY ASSERTS PASSED -> confirm prod stays dormant (no bot clone created/activated). OR the owner explicitly defers the copy with a recorded reason."
    why_human: "Prod n8n API key and secrets.json are deny-ruled from Claude, and prod bagkz is live infra — this one-shot deploy can only be run by the owner. NOTE (Finding F-1, discovered this verification): before running Step 7, first bump `Tools/n8n/verify-telegram-parity.py`'s `TG_BOT_NODE_COUNT` constant from 24 to 26 — Phase 09's SUP-03/04 commit (`d50e34c`) legitimately added two nodes (`Read Reply Mode`, `Suppressed?`) to the committed Telegram_Bot template after this phase's runbook was authored. Confirmed by direct execution: running the script unmodified against the CURRENT committed `Tools/n8n/workflows/` (i.e. exactly what Gate B would import) today prints `PARITY FAIL: node count 26 != 24` even though nothing is actually broken; patching the constant to 26 makes every other assert (incl. the D10 RAG-relevance and re-stamp checks) pass cleanly and the script prints ALL PARITY ASSERTS PASSED. Skipping this pre-step will cause a false-negative go/no-go on an otherwise-correct prod import."
  - test: "Pre-Gate-B cleanup sweep (08-REVIEW.md Phase-Close Cleanup Inventory, 16 rows)"
    expected: "Before real prod traffic, apply the round-7 review's definitive strip list: GATE/DELETE the un-gated `response.txt` full-chat-list PII dump in `ChatManager.SyncAllChats` (WR-01, ChatManager.cs:456-461 — the only ungated one of three; it is main-thread-blocking file I/O writing every chat name/id/preview to `persistentDataPath/response.txt` in plaintext on every background sync, on every device); strip the four now-superfluous device-UAT diagnostic log sites (WR-02: `[D15]` in ChatManager.cs, `[D2-view]` x2 in MessageItemView.cs + its `Diagnostic*` properties in ReactionPillView.cs, `[D2-merge]` in TelegramReactionMerge.cs) while KEEPING the two `#if UNITY_EDITOR`-gated `[D15-probe]` sites (the documented v1.2 detection seam) and the `[D12]` abort warnings; delete the two dead debug artifacts in IN-06 (`Manager.GetWhatsappMesseges()`, `WappiUnitySync.cs`). Post-cleanup grep for `\\[D15\\]|\\[D2-view\\]|\\[D2-merge\\]|\\[D12\\]|response\\.txt|Diagnostic` should return only the KEEP rows."
    why_human: "This is a code-cleanup task the review explicitly scoped as pre-prod (not a round-7 correctness defect) and left for a deliberate follow-up pass rather than folding into the already-verified round-7 fix — it needs a human decision on timing (before or after Gate B) and a build to confirm no regression, which this session cannot make on the owner's behalf."
  - test: "Gate C — Owner confirms Gate A + Gate B then runs /gsd-complete-milestone (08-MILESTONE-CLOSE.md)"
    expected: "Once Gate A (PASSED, this verification) and Gate B are each dispositioned (PASS or explicit defer), the owner runs `/clear` then `/gsd-complete-milestone` for v1.1 \"Telegram Parity\", producing the PROJECT.md Active->Validated move, ROADMAP reorg, milestone.complete archival, RETROSPECTIVE.md update, and the `v1.1` git tag; SUPPRESS-01 is confirmed rolled forward into v1.2 Phase 9 (already true — Phase 9 is executing) and the D15 follow-up + any residual carried items land in STATE.md Deferred Items (already true)."
    why_human: "This is the actual milestone-close mechanics run (`/gsd-complete-milestone`), explicitly reserved for the owner by the checklist's own text (\"do NOT tick on the owner's behalf\"); it is also gated on Gate B being dispositioned first, which has not happened yet."
---

> ## Reconciliation closure — 2026-07-21
>
> **Owner decision (2026-07-21):** "yes, close Group 1 and 2. Group 3 i will close later after finish phase 10 and 11."
>
> Gate A **PASSED** on the round-7 device re-verify (2026-07-21). The phase was **approved complete by the owner 2026-07-21**; frontmatter `status:` advanced `human_needed → passed`. The three `human_verification` items are operational disposition (not pass/fail) and are updated in place below — none is claimed PASS:
>
> 1. **Gate B — one-shot prod bagkz bulk replication (08-PROD-REPLICATION.md)** → **PARKED indefinitely per owner** (not pending). Prod bagkz stays dormant; the replication runbook is deferred with a recorded owner reason. (Finding F-1 pre-step — bump `verify-telegram-parity.py` `TG_BOT_NODE_COUNT` 24→26 — still applies whenever the copy is eventually run.)
> 2. **Pre-Gate-B cleanup sweep (08-REVIEW.md Phase-Close Cleanup Inventory)** → **IN PROGRESS in a parallel session** (cleanup task spawned 2026-07-21).
> 3. **Gate C — milestone close (`/gsd-complete-milestone`)** → **at owner discretion** (the milestone-close mechanics are owner-run; the owner runs `/gsd-complete-milestone` for v1.1 when ready).

# Phase 8: Device UAT + Milestone Closeout Verification Report

**Phase Goal:** Consolidated device UAT of all carried v1.0/v1.1 items + milestone closeout runbooks — the phase closes the v1.1 Telegram Parity milestone's verification debt: Gate A (owner device UAT), Gate B (prod replication runbook), Gate C (milestone close runbook).
**Verified:** 2026-07-21T18:00:00Z
**Status:** human_needed
**Re-verification:** Effectively initial — a prior `08-VERIFICATION.md` (2026-07-15, human_needed, 10/10) was written when the phase was 3 plans deep, before the phase grew through 7 gap-closure rounds to 35 plans. That version had no `gaps:` section to carry forward (it correctly classified everything as owner-run human_verification, not gaps) and its scope (Gate A not yet run at all) is now stale — Gate A has since PASSED. This report supersedes it in full; the old version is preserved in git history.

## Method

This is a DEVICE + USER-ASSISTED closeout phase by design. Verification split into three tracks: (1) re-derive and re-confirm Gate A's PASS disposition directly from the authoritative ledger (`08-DEVICE-UAT.md`, 1317 lines, 7 rounds) rather than trusting the round-7 SUMMARY's claim — read the full Round-7 verdict block, the Defects table's final dispositions for all 17 defect rows (D1-D17 + D2-ext/D2-view/D12-ext), and the historical round narratives; (2) independently re-run what's re-runnable — the headless EditMode result file (`Tools/test-output/headless-summary.json`), the `verify-telegram-parity.py` / `build-suggest-replies.py` CLI tools, a grep sweep for the review's flagged diagnostic/PII code sites, and a git-log trace of exactly which commit touched the Telegram_Bot template and why; (3) classify the two remaining owner-run gates (B, C) plus the pre-Gate-B cleanup sweep as `human_verification` items, not gaps, since Gate A (the phase's own on-device pass) has already passed and nothing in the ROADMAP's Success Criteria requires Gates B/C to be *executed* within this phase (SC3 requires the prod-replication *checklist* to be updated, not the copy to run).

Re-running the Gate-B tooling surfaced one finding the launching agent's brief did not anticipate: `verify-telegram-parity.py`, run unmodified against the CURRENT committed `Tools/n8n/workflows/`, fails a hardcoded node-count assert because Phase 9 (a separate, later, still-executing milestone) added two nodes to the shared Telegram_Bot template after this phase's tooling was authored. This is not a Phase-8 regression (the tool was correct when written and Phase 8's own device-UAT scope never touched that template), but it WILL false-fail Gate B's go/no-go check if run today without a one-line fix — see Finding F-1 below and the Gate B human-verification item.

## Goal Achievement

### ROADMAP Success Criteria (the contract)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| SC1 | A Telegram-authed bot is exercised on-device end-to-end (auth+2FA, chat/media, switch, auto-reply, «Вместе», dashboard) with results recorded in a HUMAN-UAT doc | ✓ VERIFIED | `08-DEVICE-UAT.md` Round-7 Overall = "**PASS (GATE A PASSED @ round 7, 2026-07-21)**" (line 1179). All groups A-H PASS/N-A across the consolidated pass and 7 re-verify rounds; every prior FAIL is now RESOLVED, SUPERSEDED (owner-approved scope change), or explicitly OPEN-DEFERRED by owner decision (D15 — see truth 2 below). Read the full round-7 verdict block (lines 976-1138) plus every historical round bullet (1189-1301) rather than trusting the SUMMARY alone. |
| SC2 | Carried v1.0 deferred device-UAT scenarios (Phases 01-02) are run or explicitly re-deferred with a reason | ✓ VERIFIED | Group I (10 items = 4 Phase-01 + 5 Phase-02 + 1 aggregate 01-VERIFICATION sign-off) all dispositioned; item I.3 #10 (the formal 01-VERIFICATION human sign-off) was re-deferred pending D5, then **re-aggregated to PASS at round 7** once D5's live-incoming-render blocker resolved (08-35-SUMMARY.md line 17, confirmed against 08-DEVICE-UAT.md line 1131 "I.3 #10 ... re-aggregated to PASS"). |
| SC3 | Prod bagkz bulk-replication checklist updated to cover the Telegram template fixes + Suggest Replies channel-awareness (the checklist itself, not the copy) | ✓ VERIFIED | `08-PROD-REPLICATION.md` (per its own text) scopes the fixed Telegram_Bot template, both Create orchestrators' RAG re-stamp, and channel-branched Suggest_Replies; explicitly states prod stays dormant until run. The ROADMAP wording is deliberately about the *checklist*, not execution — SC3 does not require Gate B to have run. |

**Score:** 3/3 ROADMAP Success Criteria verified.

### Plan/Phase-Level Truths (supplementing the ROADMAP contract)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | All 35 plans (08-01..08-35) across the original 3 + 7 gap-closure rounds have SUMMARYs; nothing left mid-execution | ✓ VERIFIED | `ls .planning/phases/08-device-uat-milestone-closeout/*-SUMMARY.md \| wc -l` = 35; `*-PLAN.md` = 35. Loop-checked every `08-01` through `08-35` SUMMARY explicitly exists — no gaps in the sequence. |
| 2 | Every defect the device pass ever filed (D1-D17, D2-ext, D2-view, D12-ext, plus WR-01/WR-02/G6/echo-hex) reaches an explicit terminal disposition — RESOLVED, SUPERSEDED (owner-approved scope change), or OPEN-DEFERRED (owner decision, tracked follow-up) — none silently dropped | ✓ VERIFIED | Read the full Defects table (08-DEVICE-UAT.md lines 1149-1168): every row's final annotation is one of RESOLVED / SUPERSEDED / CLOSED / OPEN-DEFERRED. Only D15 remains open, and it is explicitly OPEN-DEFERRED by owner decision ("Flip Gate A now"), not silently ignored — tracked in both `08-DEVICE-UAT.md` and `STATE.md` Deferred Items (line 164). |
| 3 | EditMode suite green at the phase's final code-touching commit, with no regression carried into the docs-only tail | ✓ VERIFIED | `Tools/test-output/headless-summary.json`: `{"overall":"Passed","total":1191,"passed":1191,"failed":0,"unityExit":0}`, timestamped against `editor.log` at 2026-07-21 14:42 — matches HEAD-at-that-time `974b66b` ("docs(08-34): complete D2-view ... plan"). `git log` confirms the 3 commits after `974b66b` (`124c6bd`, `06b7969`, `d2e78bb`) are docs/planning-only (no `.cs` files touched) — the green result still holds at current HEAD. |
| 4 | Round-7 code review finds 0 critical correctness defects in the shipped D2-view/D15 logic | ✓ VERIFIED | `08-REVIEW.md` frontmatter: `critical: 0, warning: 2, info: 6, status: issues_found` — both warnings (WR-01, WR-02) are explicitly scoped as pre-prod cleanup debt, not correctness defects in the round-7 logic ("The shipped logic is sound"). Independently spot-checked the review's core claims: `displacedEmoji` field exists on `MessageReaction.cs:16`; `Merge`/`Reconcile` exist in `TelegramReactionMerge.cs` matching the described seam. |
| 5 | Requirements traceability clean — Phase 8 owns no new v1.1 REQ-ID; carried items (PROD-01, SUPPRESS-01) correctly tracked, not orphaned | ✓ VERIFIED | `REQUIREMENTS.md` line 136: "Phase 8 ... owns no v1.1 REQ by design"; all 29 v1.1 REQ-IDs already mapped Complete to Phases 3-7 (0 unmapped, 0 orphans). `PROD-01` is declared in `08-02-PLAN.md`'s frontmatter as the carried item it closes (the checklist, per SC3); `SUPPRESS-01` is confirmed genuinely rolled forward — Phase 9 (v1.2 "Reply-Trigger Discipline") is already executing against it (commits `69fa671`/`a1f94c1`/`d50e34c`). |
| 6 | The two owner-run runbooks (`08-PROD-REPLICATION.md`, `08-MILESTONE-CLOSE.md`) accurately name their green conditions and stay un-ticked (no premature self-completion) | ✓ VERIFIED | Both files' status headers read "OPEN (owner-run)"; `08-PROD-REPLICATION.md` has 0 of its ~10 step checkboxes ticked; `08-MILESTONE-CLOSE.md` Gate A/B checkboxes are still `☐` even though Gate A has in fact passed (the checklist itself is inert prose, correctly not self-updating — the owner ticks it at close time). |
| 7 | Gate B (prod replication) has actually been executed against prod bagkz, or explicitly deferred with a reason | ✗ NOT YET DONE | `08-PROD-REPLICATION.md` all steps blank; `STATE.md` Deferred Items line 161 still reads "Prod bagkz replication ... pending → Phase 8 checklist" (unchanged status). This is expected/by-design (owner-only, prod secrets deny-ruled) — tracked as Human Verification item 1, not a gap. |
| 8 | Gate C (`/gsd-complete-milestone`) has been run for v1.1, or Gate C mechanics are at minimum correctly gated on A+B | ✗ NOT YET DONE (correctly gated) | `08-MILESTONE-CLOSE.md` explicitly blocks the close on both gates ("Both gates are owner-run. The close does not proceed until each is either PASS or explicitly deferred") and Gate B has not run yet, so Gate C correctly has not run either — `STATE.md` `status: executing`, `milestone: v1.1` (not yet closed). Tracked as Human Verification item 3. |

**Score:** 6/8 plan-level truths fully VERIFIED-and-DONE; 2 correctly NOT-YET-DONE by design (owner-run gates gated in the right order) — no truth is FAILED or silently broken.

### Finding F-1 — Gate-B tooling drift (discovered this verification, not part of the phase's own regression)

**What:** `Tools/n8n/verify-telegram-parity.py` (a Phase-8-authored, `--dir`-overridable Gate-B go/no-go tool) hardcodes `TG_BOT_NODE_COUNT = 24` (line 43) as an order/insertion-invariant guard on the committed `4VN3gsFaC2HUYmcc-Telegram_Bot.json`. Run unmodified against the CURRENT committed workflow directory (exactly what Gate B would import to prod today), it fails:
```
PARITY FAIL: 4VN3gsFaC2HUYmcc-Telegram_Bot.json: node count 26 != 24 (order/insertion invariant broken)
```
**Why:** Commit `d50e34c` ("feat(09-02): Telegram bot template reply-mode suppression gate (SUP-03, SUP-04)") — Phase 9 work, a separate and still-executing v1.2 milestone — legitimately pasted two new nodes (`Read Reply Mode`, `Suppressed?`) onto the template after Phase 8's tooling was authored and last verified (the original 3-plan-era verification confirmed `ALL PARITY ASSERTS PASSED` on 2026-07-15, before `d50e34c` landed 2026-07-19). Phase 8's own device-UAT rounds never touched this file, so the drift was invisible until this session directly re-ran the script.
**Confirmed fix is trivial and isolated:** patching the constant to 26 (in-memory, not committed) makes every other assert in the script — including the D10 RAG-relevance directive check and both Create-orchestrator re-stamp checks — pass cleanly; the script prints `ALL PARITY ASSERTS PASSED`. No other hardcoded count in the script is affected.
**Disposition:** Not scored as a phase-08 FAIL (it isn't phase-08's regression, and SC3 only requires the checklist be updated, which it accurately is). Surfaced as a pre-step inside the Gate B human-verification item, since running Gate B unmodified today would produce a false-negative go/no-go on an otherwise-correct prod import.

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `08-DEVICE-UAT.md` | Consolidated owner-run device-UAT ledger, 7 rounds, Overall = PASS | ✓ VERIFIED | 1317 lines; Round-7 Overall = PASS (line 1179); all defect rows terminally dispositioned. |
| `08-PROD-REPLICATION.md` | Ordered prod bulk-replication runbook, both templates INACTIVE, still unrun | ✓ VERIFIED | Status "OPEN (owner-run)"; steps 1-9 all blank; scope matches what actually shipped (Telegram_Bot fixes, RAG re-stamp, channel-branched Suggest_Replies). |
| `08-MILESTONE-CLOSE.md` | Gated close checklist referencing Gates A/B + `/gsd-complete-milestone` | ✓ VERIFIED | Status "OPEN (owner-run)"; both gate checkboxes correctly still blank (owner ticks at close time, not automatically). |
| `08-REVIEW.md` | Round-7 final code review with a definitive cleanup inventory | ✓ VERIFIED | `critical:0, warning:2, info:6`; 16-row Phase-Close Cleanup Inventory present and internally consistent with the grep-verified code state (WR-01/WR-02 findings independently reproduced). |
| `Tools/n8n/verify-telegram-parity.py` | `--dir` override; byte-identical default behavior | ⚠️ VERIFIED-WITH-DRIFT | `--dir` flag present and functions; mechanism sound; but see Finding F-1 — a hardcoded assert is now stale relative to current committed HEAD due to concurrent Phase-9 work. |
| `Tools/n8n/build-suggest-replies.py` | OpenAI/Supabase cred-id overrides | ✓ VERIFIED | `--openai-cred`/`--supabase-cred` + env-var equivalents present in `--help`, unchanged since original verification. |
| `CLAUDE.md` D15 note | Revised from "confirmed platform limit" to a tracked follow-up, matching the probe evidence | ✓ VERIFIED | grep confirms no "Confirmed platform limit" string remains on the `message/reaction` bullet; the revised text (reaction key surfaced, absence-based reconcile possible, v1.2 follow-up) matches `08-DEVICE-UAT.md`'s round-7 disposition exactly. |
| `STATE.md` / `ROADMAP.md` | Gate A PASSED reflected in Current Position, Deferred Items, progress table | ✓ VERIFIED | `stopped_at: Completed 08-35-PLAN.md (round-7 owner re-verify — GATE A PASSED)`; progress table row for Phase 8 states "GATE A PASSED" and correctly lists Gates B/C as next. |

### Key Link Verification

| From | To | Via | Status | Details |
|------|-----|-----|--------|---------|
| `08-DEVICE-UAT.md` Round-7 item 5 | `ChatManager.cs` `[D15-probe]` | Editor-Console evidence quoted in the ledger | ✓ WIRED | Probe code (`_d15ProbeArmed`, `#if UNITY_EDITOR`) exists exactly where cited; the quoted log line format (`reactionsKey=… reactionKey=…`) matches the actual `Debug.Log` call site in `ChatManager.QuoteResolve.cs`. |
| `08-DEVICE-UAT.md` D2-view resolution | `MessageReaction.displacedEmoji` + `TelegramReactionMerge.Merge`/`Reconcile` | code citation | ✓ WIRED | Both artifacts independently grep-confirmed present and matching the described seam (field on line 16 of `MessageReaction.cs`; `Merge`/`Reconcile` functions in `TelegramReactionMerge.cs`). |
| `08-MILESTONE-CLOSE.md` Gate A section | `08-DEVICE-UAT.md` Overall = PASS | blocking-gate reference | ✓ WIRED (source now green) | The checklist still shows an un-ticked `☐ Gate A` box (correct — owner ticks at close time), but the referenced source document it points at genuinely now reads PASS. |
| `08-MILESTONE-CLOSE.md` Gate B section | `08-PROD-REPLICATION.md` Overall = PASS/DEFERRED | blocking-gate reference | ✓ WIRED (source still open) | Correctly still open — Gate B has not run. |
| `08-PROD-REPLICATION.md` step 7 | `verify-telegram-parity.py --dir <prod-export>` | post-import structural re-verify | ⚠️ WIRED-BUT-STALE | The link is structurally correct (flag exists, script callable), but per Finding F-1 it will currently false-fail against a correct prod import until the hardcoded node count is bumped. |
| `REQUIREMENTS.md` PROD-01 | `08-02-PLAN.md` frontmatter `requirements: [PROD-01]` | requirement ownership | ✓ WIRED | Matches exactly; not orphaned. |
| `ROADMAP.md` Phase 9 goal | `STATE.md` SUPPRESS-01 roll-forward | milestone carry-forward | ✓ WIRED | Phase 9 (v1.2) is confirmed already executing against the suppression feature (commits `69fa671`, `a1f94c1`, `d50e34c`). |

### Data-Flow Trace (Level 4)

Not applicable in the UI-rendering sense — this phase's own artifacts are planning documents (runbooks/ledgers) and CLI tooling, not application UI. The closest analogue, the round-7 reaction-merge data path (`TelegramReactionMerge.Merge`/`Reconcile` → `RefreshCachedMessageReactions` → view repaint), was traced structurally in `08-REVIEW.md` and independently spot-checked here (field/function existence confirmed); the owner's own device evidence (two successive `[D2-view]` events with healthy post-renders, quoted verbatim in the ledger) is the actual end-to-end data-flow proof for that logic, which this session cannot re-produce without a device.

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| EditMode suite green at current effective HEAD | read `Tools/test-output/headless-summary.json` | `{"total":1191,"passed":1191,"failed":0}` | ✓ PASS |
| `verify-telegram-parity.py` default run (committed workflows/) | `python3 Tools/n8n/verify-telegram-parity.py` | `PARITY FAIL: ... node count 26 != 24` | ✗ FAIL — see Finding F-1 (drift, not a phase-08 regression; one-line fix confirmed) |
| `verify-telegram-parity.py --dir` override still functions | `python3 Tools/n8n/verify-telegram-parity.py --dir Tools/n8n/workflows` | Same result as default (mechanism intact) | ✓ PASS (mechanism); ✗ underlying assert stale |
| `build-suggest-replies.py` cred-override flags present | `python3 Tools/n8n/build-suggest-replies.py --help` | `--openai-cred`/`--supabase-cred` + env fallbacks documented | ✓ PASS |
| No workflow-graph edits smuggled in under Phase 8 (guardrail) | `git show 32ebdf8 --stat`, `git log -- Tools/n8n/verify-telegram-parity.py` | Additive diffs only; Phase-8 commits to the parity tool are `6aaea8f`(P4)/`584bd58`(P4 fix)/`32ebdf8`(08-02)/`02e32a3`(08 fix) — none touch the node-count constant | ✓ PASS (Phase 8 itself never destabilized this) |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| (none new) | — | Phase 8 owns no v1.1 REQ-ID by design | ✓ SATISFIED (by design) | `REQUIREMENTS.md:136` |
| PROD-01 | 08-02-PLAN.md | Prod bagkz bulk replication (all dev workflows incl. Telegram fixes) | ⧗ CHECKLIST DONE, EXECUTION PENDING | Runbook written + accurate (SC3); actual copy not yet run — tracked as Human Verification item 1 |
| SUPPRESS-01 | (carried, not owned by Phase 8) | Server-side «Вместе» suppression | ✓ ROLLED FORWARD, confirmed executing | Phase 9 (v1.2) commits already landed against it |

No orphaned requirements found.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---|---|---|---|
| `Assets/Scripts/Main/ChatManager.cs` | 456-461 | Un-gated `response.txt` full chat-list dump (WR-01) — writes every chat name/id/preview to plaintext disk, synchronously, on every background sync, on every device (no `#if UNITY_EDITOR`, unlike its two siblings) | 🛑 Blocker (for going to real prod traffic — not for Gate A/this phase's own device-UAT scope) | PII-at-rest + main-thread-blocking I/O on a hot path; flagged pre-existing (IN-03 lineage) but now urgent since v1.1 heads to prod. Human Verification item 2. |
| `Assets/Scripts/Main/ChatManager.cs`, `Assets/Scripts/UI/MessageItemView.cs` (x2), `Assets/Scripts/UI/ReactionPillView.cs`, `Assets/Scripts/Chat/TelegramReactionMerge.cs` | 658-665 / 4659-4661,4681-4687 / 66-69 / 75-77 | Compiled (non-Editor-gated, except the merge one) device-UAT diagnostic logs (`[D15]`, `[D2-view]` x2 + `Diagnostic*` props, `[D2-merge]`) whose purpose (confirming Gate A behavior on-device) is now fulfilled | ⚠️ Warning | Log spam + minor perf cost on every reaction poll/change; not a correctness defect. `[D15-probe]` sites (already `#if UNITY_EDITOR`) correctly KEPT as the v1.2 detection seam. Human Verification item 2. |
| `Assets/Scripts/Main/Manager.cs:3930-3970`, `Assets/Scripts/Main/WappiUnitySync.cs` (whole file) | — | Dead debug code: zero-caller method with hardcoded real profile/message ids + un-gated `response.txt` write; an unattached-in-scene sync helper that would `Debug.Log` full message payloads (PII) and uses async/await (violates the coroutine-only rule) if ever re-attached | ℹ️ Info | Not currently executing (unreachable / not in scene) but compiled into every build; scoped for deletion in the same cleanup pass (IN-06). |
| `Tools/n8n/verify-telegram-parity.py:43` | 43 | Hardcoded `TG_BOT_NODE_COUNT = 24`, now stale vs. the committed template (26 nodes after Phase 9's SUP-03/04 addition) | 🛑 Blocker (for Gate B execution specifically) | See Finding F-1. One-line fix confirmed sufficient; not a Phase-8 regression. |

None of these are correctness defects in the round-7 D2-view/D15 logic itself (per `08-REVIEW.md`'s own conclusion, independently corroborated here) — they are pre-prod hygiene items and one cross-phase tooling-drift item, all appropriately scoped as pre-Gate-B/pre-prod work rather than Gate-A blockers.

### Human Verification Required

See the `human_verification` block in the frontmatter for the full text of each item. Summary:

1. **Gate B — prod bagkz bulk replication.** Owner-only (prod secrets deny-ruled from Claude). Fix Finding F-1 (bump `TG_BOT_NODE_COUNT` to 26) before running Step 7's go/no-go, or it will false-fail.
2. **Pre-Gate-B cleanup sweep.** Apply the 16-row `08-REVIEW.md` inventory (priority: WR-01's un-gated PII dump) — a human/build decision on timing, not something this session can perform unattended.
3. **Gate C — `/gsd-complete-milestone` for v1.1.** Owner-only command, correctly gated on Gate B being dispositioned first (it is not yet).

### Gaps Summary

No must-have from the ROADMAP contract (SC1-SC3) or from the plan-level truth set is FAILED. Gate A — the phase's own on-device validation mandate — has genuinely PASSED after 7 real gap-closure rounds, with every defect traced to a terminal disposition and the EditMode suite green at the code-frozen commit. What remains open are three owner-run items that this automated session is structurally unable to perform (prod deploy with deny-ruled secrets, a build-gated cleanup sweep, and the milestone-close command reserved for the owner) — correctly classified as `human_needed`, not `gaps_found`. One new finding (F-1, a stale hardcoded assert in Gate-B's go/no-go tool, caused by concurrent Phase-9 template work) is surfaced as a pre-step inside the Gate-B human-verification item rather than as a Phase-8 gap, since it is not a regression in anything Phase 8 itself shipped and the fix is a single, already-confirmed-correct constant change.

---

_Verified: 2026-07-21T18:00:00Z_
_Verifier: Claude (gsd-verifier)_

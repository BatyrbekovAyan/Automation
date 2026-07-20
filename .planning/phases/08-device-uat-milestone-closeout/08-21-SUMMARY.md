---
phase: 08-device-uat-milestone-closeout
plan: 21
subsystem: testing
tags: [device-uat, telegram, gate-a, reactions, empty-state-cta, sync-cover, owner-gate]

# Dependency graph
requires:
  - phase: 08-device-uat-milestone-closeout
    provides: "Round-3 gap fixes on the built tree: 08-17 (D2-ext loaded-window reaction reconcile), 08-18 (D12 channel-switch re-wire + overlay-open guarantee), 08-19 (D13a TG post-creation cover parity), 08-20 (D13b «Синхронизация…» pill removal)"
provides:
  - "Round-3 owner device verdicts recorded VERBATIM (D2-ext, D12, D13-cover, D13-pill, G6), each mapped to its 08-DEVICE-UAT.md anchor"
  - "Gate A disposition: remains ISSUES — open D2-view (bubble repaint miss), D12-ext (CTA dies after channel switch), D14 (TG cover brand-blue polish)"
  - "D13 fully RESOLVED (cover + pill); D2-ext data layer RESOLVED (merge/reconcile provably correct — logs always show the right reaction)"
  - "Round-4 gap scope named: D2-view / D12-ext / D14 via /gsd-plan-phase 08 --gaps"
affects: [round-4 gap plans, 08-DEVICE-UAT.md, Gate B prod replication, Gate C milestone close]

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created:
    - .planning/phases/08-device-uat-milestone-closeout/08-21-SUMMARY.md
  modified:
    - .planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md
    - .planning/STATE.md

key-decisions:
  - "Gate A remains ISSUES: D13 resolved in full, but D2-ext leaves a view-layer residual (D2-view), D12 leaves a channel-switch residual (D12-ext), and the owner approved a new polish scope (D14 — TG cover brand blue)"
  - "D2-view scoped to the VIEW/refresh layer (OnMessageReactionsChanged → bubble re-render path), NOT the merge — the owner's own observation (logs always correct) proves the data layer"
  - "D12-ext lead filed explicitly: 08-18's OnActiveChannelChanged re-configure path is directly implicated (CTA works until a WhatsApp↔Telegram chip switch, then dies on BOTH channels)"
  - "The 08-20 1136-green test gate recorded as SUPERSEDED by later suite growth — the Editor Bee crash was resolved post-checkpoint and parallel phase-9/11 sessions grew the suite green past 1136 (1165 @ 11-01, 1170 @ 09-03)"

patterns-established: []

requirements-completed: []

# Metrics
duration: owner-gated (checkpoint prepared 2026-07-17/20; owner device pass reported 2026-07-20)
completed: 2026-07-20
---

# Phase 8 Plan 21: Round-3 Device Re-verify (D2-ext / D12 / D13) Summary

**One owner-run Android pass over the round-3 fixes: D13 fully resolved (TG cover works, pill gone), D2-ext's data layer resolved (logs always show the correct reaction) with a NEW view-repaint residual D2-view, D12 partially re-failed (CTA works until a channel-chip switch, then dies on BOTH channels → D12-ext), plus a new owner-approved polish scope D14 (TG cover brand-blue elements) — Gate A stays ISSUES, round-4 scope = D2-view / D12-ext / D14.**

## Performance

- **Duration:** owner-gated — checkpoint prepared 2026-07-17/20; owner ran the device pass and reported 2026-07-20
- **Tasks:** 1 (checkpoint:human-verify — the owner gate itself)
- **Files modified:** 3 (planning docs only; NO app code — by design)

## Owner Verdicts (VERBATIM, per item)

Recorded exactly as the owner stated them — not paraphrased, not ticked on the owner's behalf.

| # | Item | Owner verdict (verbatim) | Disposition | Source anchor |
|---|------|--------------------------|-------------|---------------|
| 1 | **D2-ext** — TG-app-side reaction change/removal reflects in-app | "it seems working, but sometimes reaction on message bubble in our app is not updated when it is changed in telegram app. logs actually always shows correct reaction but on message bubble it is not changed sometimes (not sure but i guess reaction is not updating appears when i was first changing reaction on one bubble then started to change on another message bubble.)" | **PARTIAL — data layer RESOLVED, NEW residual D2-view** | 08-DEVICE-UAT.md D2-ext / B9–B13 |
| 2 | **D12** — Telegram create-bot CTA opens the form | "works, but stops working when whatsapp/telegram chip is switched, both whatsapp and telegram create first bot button stops working." | **PARTIAL RE-FAIL — residual D12-ext** | 08-DEVICE-UAT.md D12 / F-group |
| 3 | **D13 cover** — fresh TG bot shows the ~5-min cover | "works, change the green colored objects at this page to telegrams brand blue. (spinner, sync)" | **RESOLVED** + NEW owner-approved polish **D14** | 08-DEVICE-UAT.md D13 (cover) / E/B-group |
| 4 | **D13 pill** — «Синхронизация…» pill gone everywhere | "ok" | **RESOLVED** | 08-DEVICE-UAT.md D13 (pill removal) / D9-superseded |
| 5 | **G6** — deactivate the dev test clone after the window | (not reported by the owner) | **STILL OUTSTANDING** — third consecutive checkpoint; carry again | 08-DEVICE-UAT.md G6 / 04-HUMAN-UAT.md #6 |

**D2-ext echo-hex capture:** **NOT captured** this pass (as in 08-16 — its absence is explicitly noted rather than fabricated). With the data layer now proven correct by the owner's log observation, the capture's diagnostic value is reduced; it remains a nice-to-have for any future merge-layer question, not a round-4 blocker.

## Gate A Disposition

**Gate A remains ISSUES.** Open items after round 3:

1. **D2-view** (NEW residual, split from D2-ext) — the merge/reconcile pipeline is provably correct (owner: "logs actually always shows correct reaction") but the message-bubble VISUAL sometimes misses the update. **Repro hint (owner):** change a reaction on one bubble, then start changing a reaction on ANOTHER message bubble — the second bubble may not repaint. **Round-4 scope: the view/refresh layer (`OnMessageReactionsChanged` → bubble re-render path), NOT the merge.**
2. **D12-ext** (residual, split from D12) — the 08-18 fix is effective initially (CTA opens the form), but after a WhatsApp↔Telegram chip switch the create-first-bot button stops working on **BOTH** channels. **Lead filed for round 4: this directly implicates the `OnActiveChannelChanged` re-configure path 08-18 added** (`HandleActiveChannelChanged` → `ConfigureForReason(_lastReason)+Show`). Secondary named suspect (pre-flagged in 08-18's SUMMARY as a documented-not-fixed latent bug): `BeginLoadForActiveBot` resolves zero-bots via `FindBotByName(DefaultBotId)==null` → fires a connect-state reason instead of `NoBotsExist` on a channel switch with zero bots.
3. **D14** (NEW, owner-approved polish) — on the Telegram post-creation cover, the green-tinted elements (spinner, sync progress) must use Telegram brand blue (#2AABEE) instead of WhatsApp green.

**Resolved this round:** D13 in full (cover + pill — both halves of the 08-16 owner decision), and the D2-ext **data layer** (08-17's loaded-window reconcile works; the residual is strictly visual).

**Consequences:** the re-deferred I.3 #10 (01-VERIFICATION sign-off) does NOT re-aggregate yet (Gate A not PASS); Gates B (prod replication) and C (milestone close) stay blocked on the round-4 closure. **Next step: `/gsd-plan-phase 08 --gaps` for D2-view / D12-ext / D14.**

## Test Gate (superseded)

The checkpoint's pre-build gate ("fresh 1136/1136 after Editor restart") is recorded as **SUPERSEDED — not confirmed at 1136 and deliberately not claimed**. The environmental Editor Bee crash (exit 134, `Interop.Sys.GetGroups`) was resolved after the checkpoint was prepared, and parallel sessions have since landed phase-9 and phase-11 work on main — the EditMode suite grew well past 1136 and ran green in those sessions (1165/1165 recorded at 11-01, 1170/1170 at 09-03), which transitively includes the 08-17..08-20 changes. No 1136 confirmation is recorded; the superseding later greens stand in its place.

## Task Commits

None — this plan's single task was the `checkpoint:human-verify` owner gate; no app code was changed (per the plan's hard constraint). Pre-checkpoint verification confirmed all nine round-3 commits (`c78ac99`/`ba825d0`, `791447b`/`19c1ef2`, `3cd6537`/`91b97b3`, `1f28310`/`d2c800a`/`5185620`) present on the tree the owner built from, with no uncommitted round-3 app code.

**Plan metadata:** the docs commit carrying this SUMMARY + the 08-DEVICE-UAT.md and STATE.md updates.

## Files Created/Modified

- `.planning/phases/08-device-uat-milestone-closeout/08-21-SUMMARY.md` — this record.
- `.planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md` — D13 marked RESOLVED (both halves); D2-ext row closed to residual **D2-view** (with the owner's repro hint); D12 row closed to residual **D12-ext** (with the channel-switch lead); new **D14** row (owner-approved polish); status header + Overall updated (still ISSUES).
- `.planning/STATE.md` — surgical append of the Gate A round-3 blocker entry ONLY (parallel sessions own the rest of the file; frontmatter/Current Position are phase-09/10's and were not touched).

## Decisions Made

- **Gate A stays ISSUES** — two partials leave residuals (D2-view, D12-ext) and one new owner-approved polish scope (D14); each FAIL/residual spins into round 4 per the plan's own success criteria.
- **D2-view is a view-layer defect, not a merge defect** — scoped off the owner's direct observation that the logs are always correct; round 4 must NOT touch `TelegramReactionMerge`/reconcile.
- **D12-ext's lead names 08-18's own fix path** — the failure onset is exactly the event (channel switch) whose handler 08-18 added, on both channels; that re-configure path is the first thing round 4 must diagnose.
- **1136 gate superseded, not skipped silently** — recorded with the superseding evidence (later, larger green suites) rather than claiming a number that was never confirmed.

## Deviations from Plan

None in execution mechanics — the plan's single checkpoint ran as designed (owner-run, verdicts transcribed verbatim, no app code changed). Two outcome-level notes, recorded for accuracy rather than as rule deviations:

1. The plan's pre-build test gate (fresh 1136/1136) was overtaken by events (Editor recovery + parallel-session suite growth) and is recorded as superseded — see Test Gate above.
2. The exact build commit the owner used is not independently confirmable from this session; the round-3 fixes were all committed on main well before the pass (verified pre-checkpoint), so the built tree includes them.

## Issues Encountered

- **G6 (deactivate the dev test clone) is now outstanding across THREE consecutive checkpoints** (08-10 → 08-16 → 08-21). The bot-activation policy matters here — clones run against real contacts. It must ride the round-4 checkpoint again, ideally as a blocking line item rather than a reminder.
- The D2-ext echo-hex capture was requested for the third time and not provided; downgraded to nice-to-have now that the data layer is proven by the owner's log observation.

## User Setup Required

None — no external service configuration.

## Next Phase Readiness

- **Round 4:** run `/gsd-plan-phase 08 --gaps` for **D2-view** (bubble repaint on reaction change — view layer only, owner repro hint recorded), **D12-ext** (CTA dies after channel-chip switch on both channels — diagnose 08-18's `OnActiveChannelChanged` re-configure path first, plus the documented `BeginLoadForActiveBot` zero-bots latent bug), and **D14** (TG cover spinner/sync progress → brand blue #2AABEE; the `ChannelAccent.Resolve` seam from 05-10 is the established pattern).
- **Gate A → Gates B/C:** still blocked; re-aggregate I.3 #10 and proceed to prod replication + milestone close only when a future re-verify goes all-PASS.
- **Carry into the round-4 checkpoint:** G6 dev-clone deactivation (third carry) + prod bagkz stays dormant.

## Self-Check: PASSED

- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-21-SUMMARY.md`
- FOUND: `.planning/phases/08-device-uat-milestone-closeout/08-DEVICE-UAT.md` (updated: D13 RESOLVED ×2, D2-view/D12-ext/D14 rows present, Overall = ISSUES)
- FOUND: `.planning/STATE.md` Gate A round-3 blocker entry (surgical append only)
- All five verdicts transcribed VERBATIM with anchors; echo-hex absence explicitly noted; G6 outstanding recorded; no app code changed by this plan (`git status --porcelain Assets` shows only pre-existing unrelated churn from other sessions).

---
*Phase: 08-device-uat-milestone-closeout*
*Completed: 2026-07-20*
